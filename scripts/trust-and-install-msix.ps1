param(
    [string]$Version = "0.2.0.0"
)

# Requiere ejecutarse en una consola PowerShell como Administrador una única vez para confiar en el certificado de desarrollo.
$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Este script debe ejecutarse desde una ventana de PowerShell con permisos de Administrador para importar el certificado raiz en LocalMachine."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$cerPath = Join-Path $repoRoot "artifacts\IslandWidgetDev.cer"
$msixPath = Join-Path $repoRoot "artifacts\IslandWidget.Dev_${Version}_x64.msix"

if (-not (Test-Path $cerPath)) {
    throw "No se encontro el certificado en: $cerPath"
}

if (-not (Test-Path $msixPath)) {
    throw "No se encontro el paquete MSIX en: $msixPath"
}

Write-Host "1. Habilitando sideloading de aplicaciones firmadas (AllowAllTrustedApps = 1)..."
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -Name "AllowAllTrustedApps" -Value 1 -Type DWord

Write-Host "2. Importando certificado de desarrollo en LocalMachine\Root y LocalMachine\TrustedPeople..."
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null

Write-Host "3. Instalando / actualizando paquete MSIX ($msixPath)..."
Add-AppxPackage -Path $msixPath -ForceApplicationShutdown

$installed = Get-AppxPackage -Name "IslandWidget.Dev"
if ($null -eq $installed) {
    throw "El paquete no aparece registrado tras Add-AppxPackage."
}

Write-Host "Instalacion/actualizacion MSIX comprobada: $($installed.PackageFullName) (Version $($installed.Version))"
