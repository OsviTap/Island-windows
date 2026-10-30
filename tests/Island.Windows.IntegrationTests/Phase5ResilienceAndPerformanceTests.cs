using Island.Application.Coordination;
using Island.Application.Diagnostics;
using Island.Application.Theming;
using Island.Core.Contracts;
using Island.Core.Domain;
using Island.Windows.Runtime;
using Island.Windows.Windowing;
using Xunit;

namespace Island.Windows.IntegrationTests;

public sealed class Phase5ResilienceAndPerformanceTests
{
    [Fact]
    public void ProcessPerformanceProbe_CapturesValidHardwareAndProcessMetrics()
    {
        var hw = ProcessPerformanceProbe.CaptureHardwareSummary();
        Assert.True(hw.LogicalProcessorCount >= 1);
        Assert.True(hw.TotalPhysicalMemoryMb > 512);
        Assert.False(string.IsNullOrWhiteSpace(hw.OsDescription));

        var snapshot = ProcessPerformanceProbe.CaptureSnapshot("TEST-01", "Unit Test Snapshot");
        Assert.Equal("TEST-01", snapshot.ScenarioId);
        Assert.True(snapshot.PrivateBytesMb > 0);
        Assert.True(snapshot.WorkingSetMb > 0);
        Assert.True(snapshot.HandleCount > 0);
    }

    [Fact]
    public void SystemLifecycleResilienceMonitor_PowerSuspendAndResume_CancelsTransientExpansionAndRestoresPolicy()
    {
        var logger = new BoundedDiagnosticLogger();
        var envInspector = new WindowsSystemEnvironmentInspector();
        var displayProber = new WindowsDisplayAndWindowProber(logger);
        var themeService = new ThemeService(logger);
        var coordinator = new IslandStructuralCoordinator(
            themeService,
            logger,
            envInspector.CaptureSnapshot());

        var resilience = new SystemLifecycleResilienceMonitor(
            coordinator,
            envInspector,
            displayProber,
            logger);

        // Disparar una expansión automática pasiva (ej. cambio de pista)
        bool triggered = coordinator.TryPresentAutomaticEvent(IslandContentKind.MediaSummary, "TRACK_CHANGE", out var expandedState);
        Assert.True(triggered);
        Assert.Equal(IslandVisibility.Expanded, expandedState.Visibility);

        // Al entrar en suspensión, debe contraer inmediatamente la expansión transitoria y poner Policy = Paused
        var suspended = resilience.OnPowerSuspending();
        Assert.True(resilience.IsSuspended);
        Assert.Equal(IslandPolicyMode.Paused, suspended.Policy);
        Assert.Equal(IslandVisibility.Compact, suspended.Visibility);

        // Al reanudar, debe restaurar Policy = Normal y registrar latencia de recuperación
        var resumed = resilience.OnPowerResumed(
            windowController: null,
            preferredMonitorDeviceName: null,
            anchorHorizontalRatio: 0.5,
            anchorTopOffsetDip: 12.0);

        Assert.False(resilience.IsSuspended);
        Assert.Equal(IslandPolicyMode.Normal, resumed.ResultingState.Policy);
        Assert.True(resumed.ActiveMonitorsCount >= 1);
        Assert.True(resumed.RecoveryLatencyMs >= 0);
    }

    [Fact]
    public void SystemLifecycleResilienceMonitor_DisplayChangeWithUnpluggedMonitor_RecoversSafely()
    {
        var logger = new BoundedDiagnosticLogger();
        var envInspector = new WindowsSystemEnvironmentInspector();
        var displayProber = new WindowsDisplayAndWindowProber(logger);
        var themeService = new ThemeService(logger);
        var coordinator = new IslandStructuralCoordinator(
            themeService,
            logger,
            envInspector.CaptureSnapshot());

        var resilience = new SystemLifecycleResilienceMonitor(
            coordinator,
            envInspector,
            displayProber,
            logger);

        using var controller = new IslandWindowController(0, displayProber, logger);
        var result = resilience.OnDisplayOrEnvironmentChanged(
            controller,
            preferredMonitorDeviceName: @"\\.\DISPLAY_DISCONNECTED_99",
            anchorHorizontalRatio: 0.5,
            anchorTopOffsetDip: 12.0);

        Assert.True(result.UsedPrimaryMonitorFallback);
        Assert.True(result.ActiveMonitorsCount >= 1);
    }
}
