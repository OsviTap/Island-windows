param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "0.2.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProjectDir = Join-Path $repoRoot "src\Island.App"
$assetsDir = Join-Path $appProjectDir "Assets"

New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

# Valid 1x1 transparent/solid PNG bytes to ensure makeappx validation succeeds
$pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPj/HwADBwIAMCbHYQAAAABJRU5ErkJggg=="
$pngBytes = [Convert]::FromBase64String($pngBase64)

foreach ($assetName in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
    $assetPath = Join-Path $assetsDir $assetName
    if (-not (Test-Path $assetPath)) {
        [System.IO.File]::WriteAllBytes($assetPath, $pngBytes)
    }
}

$candidateDirs = @(
    (Join-Path $appProjectDir "bin\$ Platform\$Configuration\net10.0-windows10.0.22621.0\$RuntimeIdentifier"),
    (Join-Path $appProjectDir "bin\$Configuration\net10.0-windows10.0.22621.0\$RuntimeIdentifier")
)
$candidateDirs = @(
    (Join-Path $appProjectDir "bin\$Platform\$Configuration\net10.0-windows10.0.22621.0\$RuntimeIdentifier"),
    (Join-Path $appProjectDir "bin\$Configuration\net10.0-windows10.0.22621.0\$RuntimeIdentifier")
)
$buildOutDir = $candidateDirs | Where-Object { Test-Path (Join-Path $_ "Island.App.exe") } | Select-Object -First 1
if (-not $buildOutDir) {
    throw "No se encontro el binario compilado en ninguna ruta esperada: $($candidateDirs -join ', ')"
}

$stagingDir = Join-Path $repoRoot "artifacts\msix-staging"
if (Test-Path $stagingDir) {
    Remove-Item -Path $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

Copy-Item -Path (Join-Path $buildOutDir "*") -Destination $stagingDir -Recurse -Force

$manifestSource = Join-Path $appProjectDir "Package.appxmanifest"
$manifestDest = Join-Path $stagingDir "AppxManifest.xml"
$manifestXml = [System.IO.File]::ReadAllText($manifestSource)
$manifestXml = $manifestXml -replace 'Version="[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+"', "Version=`"$Version`""
[System.IO.File]::WriteAllText($manifestDest, $manifestXml, [System.Text.Encoding]::UTF8)

$stagingAssets = Join-Path $stagingDir "Assets"
New-Item -ItemType Directory -Force -Path $stagingAssets | Out-Null
Copy-Item -Path (Join-Path $assetsDir "*") -Destination $stagingAssets -Force

$msixPath = Join-Path $repoRoot "artifacts\IslandWidget.Dev_${Version}_${Platform}.msix"
$makeAppxExe = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\makeappx.exe"
$signToolExe = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"

& $makeAppxExe pack /o /nv /d $stagingDir /p $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "makeappx fallo con codigo $LASTEXITCODE"
}

$pfxPath = Join-Path $repoRoot "artifacts\IslandWidgetDev.pfx"
$cerPath = Join-Path $repoRoot "artifacts\IslandWidgetDev.cer"
$passwordPlain = "IslandDevCert2026!"

if (-not (Test-Path $pfxPath)) {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $subject = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new("CN=IslandWidgetDev")
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $subject,
        $rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

    $basicConstraints = [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true)
    $keyUsage = [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
        $true)
    $ekuOids = [System.Security.Cryptography.OidCollection]::new()
    [void]$ekuOids.Add([System.Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.3")) # Code Signing
    $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($ekuOids, $true)

    $req.CertificateExtensions.Add($basicConstraints)
    $req.CertificateExtensions.Add($keyUsage)
    $req.CertificateExtensions.Add($eku)

    $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $notAfter = [DateTimeOffset]::UtcNow.AddYears(2)
    $cert = $req.CreateSelfSigned($notBefore, $notAfter)

    [System.IO.File]::WriteAllBytes($pfxPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $passwordPlain))
    [System.IO.File]::WriteAllBytes($cerPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
}

& $signToolExe sign /fd SHA256 /f $pfxPath /p $passwordPlain $msixPath
if ($LASTEXITCODE -ne 0) {
    throw "signtool fallo con codigo $LASTEXITCODE"
}

$sig = Get-AuthenticodeSignature -FilePath $msixPath
Write-Output "MSIX v$Version empaquetado y firmado en: $msixPath (Signer=$($sig.SignerCertificate.Subject), Thumbprint=$($sig.SignerCertificate.Thumbprint))"
