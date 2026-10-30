using System.Runtime.InteropServices;
using Island.Core.Contracts;

namespace Island.Windows.Runtime;

/// <summary>
/// Verifica en tiempo de ejecución si el proceso actual posee identidad de paquete MSIX real
/// invocando directamente la API Win32 GetCurrentPackageFullName (kernel32.dll).
/// </summary>
public sealed partial class WindowsPackageIdentityProvider : IPackageIdentityProvider
{
    public const int ErrorSuccess = 0;
    public const int ErrorInsufficientBuffer = 122;
    public const int AppModelErrorNoPackage = 15700;

    private readonly TimeProvider _timeProvider;

    public WindowsPackageIdentityProvider(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public unsafe PackageIdentitySnapshot GetCurrentIdentity()
    {
        var now = _timeProvider.GetUtcNow();
        uint length = 0;
        int firstResult = GetCurrentPackageFullName(ref length, null);

        if (firstResult == AppModelErrorNoPackage)
        {
            return new PackageIdentitySnapshot(
                HasPackageIdentity: false,
                PackageFullName: string.Empty,
                Win32ErrorCode: AppModelErrorNoPackage,
                CheckedAtUtc: now);
        }

        if (firstResult != ErrorInsufficientBuffer || length == 0)
        {
            return new PackageIdentitySnapshot(
                HasPackageIdentity: false,
                PackageFullName: string.Empty,
                Win32ErrorCode: firstResult,
                CheckedAtUtc: now);
        }

        char[] buffer = new char[length];
        fixed (char* ptr = buffer)
        {
            int secondResult = GetCurrentPackageFullName(ref length, ptr);
            if (secondResult != ErrorSuccess)
            {
                return new PackageIdentitySnapshot(
                    HasPackageIdentity: false,
                    PackageFullName: string.Empty,
                    Win32ErrorCode: secondResult,
                    CheckedAtUtc: now);
            }

            int stringLength = (int)length > 0 && buffer[length - 1] == '\0'
                ? (int)length - 1
                : (int)length;

            string fullName = new(buffer, 0, Math.Max(0, stringLength));
            return new PackageIdentitySnapshot(
                HasPackageIdentity: !string.IsNullOrWhiteSpace(fullName),
                PackageFullName: fullName,
                Win32ErrorCode: ErrorSuccess,
                CheckedAtUtc: now);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentPackageFullName")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint packageFullNameLength, char* packageFullName);
}
