using Island.Application.Diagnostics;
using Island.Core.Contracts;
using Island.Windows.Audio;
using Island.Windows.Media;
using Island.Windows.Notifications;
using Island.Windows.Runtime;
using Island.Windows.Windowing;

namespace Island.Windows.IntegrationTests;

public sealed class WindowsEnvironmentIntegrationTests
{
    [Fact]
    public void WindowsPackageIdentityProvider_UnpackagedTestRunner_ReportsNoPackageError15700()
    {
        var provider = new WindowsPackageIdentityProvider();
        var identity = provider.GetCurrentIdentity();

        // El ejecutor de pruebas xUnit corre como proceso Win32 sin empaquetar (APPMODEL_ERROR_NO_PACKAGE = 15700).
        Assert.False(identity.HasPackageIdentity);
        Assert.Equal(WindowsPackageIdentityProvider.AppModelErrorNoPackage, identity.Win32ErrorCode);
        Assert.Empty(identity.PackageFullName);
    }

    [Fact]
    public void WindowsSystemEnvironmentInspector_CapturesRealWindows11Environment()
    {
        var inspector = new WindowsSystemEnvironmentInspector();
        var snapshot = inspector.CaptureSnapshot();

        Assert.True(snapshot.OsBuildNumber >= 22621, $"Se esperaba compilación >= 22621 pero se obtuvo {snapshot.OsBuildNumber}");
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ProcessArchitecture));
    }

    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", MediaProviderKind.Spotify)]
    [InlineData("Spotify.exe", MediaProviderKind.Spotify)]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", MediaProviderKind.AppleMusic)]
    [InlineData("msedge.exe", MediaProviderKind.Browser)]
    [InlineData("CustomPlayer.exe", MediaProviderKind.Other)]
    public void GsmtcMediaSessionProvider_ClassifyProvider_IdentifiesTargetApps(string aumid, MediaProviderKind expected)
    {
        var kind = GsmtcMediaSessionProvider.ClassifyProvider(aumid);
        Assert.Equal(expected, kind);
    }

    [Fact]
    public async Task GsmtcMediaSessionProvider_ProbeSessionsAsync_ReturnsOperationalReport()
    {
        var logger = new BoundedDiagnosticLogger();
        await using var provider = new GsmtcMediaSessionProvider(logger);

        var result = await provider.ProbeSessionsAsync();

        Assert.Equal(IntegrationModuleId.Media, result.CapabilityReport.ModuleId);
        Assert.Equal(IntegrationCapabilityState.Available, result.CapabilityReport.State);
        Assert.NotNull(result.Sessions);
    }

    [Fact]
    public void CoreAudioEndpointInspector_CapturesAudioEndpointSnapshot()
    {
        var logger = new BoundedDiagnosticLogger();
        var inspector = new CoreAudioEndpointInspector(logger);

        var snapshot = inspector.CaptureAudioSnapshot();

        Assert.Equal(IntegrationModuleId.Audio, snapshot.CapabilityReport.ModuleId);
        Assert.NotEqual(IntegrationCapabilityState.Faulted, snapshot.CapabilityReport.State);
    }

    [Fact]
    public async Task WindowsNotificationListenerProbe_UnpackagedProcess_HandlesGracefullyWithoutCrashing()
    {
        var logger = new BoundedDiagnosticLogger();
        var identityProvider = new WindowsPackageIdentityProvider();
        var probe = new WindowsNotificationListenerProbe(identityProvider, logger);

        var result = await probe.ProbeAsync(requestPermissionIfUnspecified: false);

        Assert.False(result.HasPackageIdentity);
        Assert.Equal(IntegrationModuleId.Notifications, result.CapabilityReport.ModuleId);
        Assert.False(string.IsNullOrWhiteSpace(result.EventSubscriptionDiagnostic));
    }

    [Fact]
    public void WindowsDisplayAndWindowProber_CapturesMonitorsAndFullScreenState()
    {
        var logger = new BoundedDiagnosticLogger();
        var prober = new WindowsDisplayAndWindowProber(logger);

        var monitors = prober.CaptureMonitors();
        var fullScreen = prober.CaptureFullScreenState();

        Assert.NotEmpty(monitors);
        Assert.Contains(monitors, m => m.IsPrimary);
        Assert.All(monitors, m =>
        {
            Assert.True(m.BoundsPx.Width > 0);
            Assert.True(m.BoundsPx.Height > 0);
            Assert.True(m.ScalePercent >= 100);
        });

        Assert.False(string.IsNullOrWhiteSpace(fullScreen.NotificationStateName));
    }
}
