using System.Diagnostics;
using Island.Application.Coordination;
using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Domain;
using Island.Core.Theming;
using Island.Windows.Windowing;

namespace Island.Windows.Runtime;

public sealed record LifecycleRecoveryResult(
    string EventName,
    double RecoveryLatencyMs,
    IslandStateSnapshot ResultingState,
    AppliedThemeState ResultingTheme,
    int ActiveMonitorsCount,
    bool UsedPrimaryMonitorFallback);

/// <summary>
/// Coordinador de resiliencia ante eventos del sistema operativo (Sección 7 y Fase 5):
/// 1. Suspensión y reanudación de energía (cancela temporizadores transitorios al suspender y re-sincroniza al reanudar).
/// 2. Cambios de monitores, resolución, escala DPI, transparencia o Alto Contraste.
/// 3. Detección automática de pantalla completa / modo presentación (SHQueryUserNotificationState).
/// </summary>
public sealed class SystemLifecycleResilienceMonitor
{
    private readonly IslandStructuralCoordinator _coordinator;
    private readonly ISystemEnvironmentInspector _environmentInspector;
    private readonly WindowsDisplayAndWindowProber _displayProber;
    private readonly IDiagnosticLogger _logger;

    private IslandPolicyMode _policyBeforeSuspend = IslandPolicyMode.Normal;
    private bool _isSuspended;
    private bool _pausedByFullScreen;

    public SystemLifecycleResilienceMonitor(
        IslandStructuralCoordinator coordinator,
        ISystemEnvironmentInspector environmentInspector,
        WindowsDisplayAndWindowProber displayProber,
        IDiagnosticLogger logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _environmentInspector = environmentInspector ?? throw new ArgumentNullException(nameof(environmentInspector));
        _displayProber = displayProber ?? throw new ArgumentNullException(nameof(displayProber));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsSuspended => _isSuspended;

    public IslandStateSnapshot OnPowerSuspending()
    {
        _isSuspended = true;
        _policyBeforeSuspend = _coordinator.CurrentState.Policy;

        // Al suspender el equipo, pasamos a Paused para cancelar inmediatamente cualquier
        // expansión transitoria y evitar temporizadores huérfanos al despertar.
        var snapshot = _coordinator.SetPolicyMode(IslandPolicyMode.Paused);

        _logger.Log(
            DiagnosticLogLevel.Info,
            "Lifecycle",
            "POWER_SUSPEND_HANDLED",
            $"Equipo entrando en suspensión; política previa={_policyBeforeSuspend}, estado contraído de forma segura.");

        return snapshot;
    }

    public LifecycleRecoveryResult OnPowerResumed(
        IslandWindowController? windowController,
        string? preferredMonitorDeviceName,
        double anchorHorizontalRatio,
        double anchorTopOffsetDip)
    {
        long startStamp = Stopwatch.GetTimestamp();
        _isSuspended = false;

        var env = _environmentInspector.CaptureSnapshot();
        var currentThemeDef = _coordinator.CurrentThemeState.Theme;
        var appliedTheme = _coordinator.ApplyTheme(currentThemeDef, env);

        var restoredPolicy = _policyBeforeSuspend == IslandPolicyMode.Paused && !_pausedByFullScreen
            ? IslandPolicyMode.Normal
            : _policyBeforeSuspend;

        var state = _coordinator.SetPolicyMode(restoredPolicy);

        var monitors = _displayProber.CaptureMonitors();
        bool usedFallback = false;
        if (windowController is not null)
        {
            var geo = windowController.ApplyLayoutAndRegion(
                state,
                appliedTheme.Theme,
                preferredMonitorDeviceName,
                anchorHorizontalRatio,
                anchorTopOffsetDip);
            usedFallback = geo.UsedPrimaryMonitorFallback;
        }

        double latencyMs = Math.Round(Stopwatch.GetElapsedTime(startStamp).TotalMilliseconds, 3);

        _logger.Log(
            DiagnosticLogLevel.Info,
            "Lifecycle",
            "POWER_RESUME_RECOVERED",
            $"Reanudación completada en {latencyMs} ms (Monitores={monitors.Count}, Material={appliedTheme.EffectiveMaterial}).");

        return new LifecycleRecoveryResult(
            EventName: "PowerResume",
            RecoveryLatencyMs: latencyMs,
            ResultingState: state,
            ResultingTheme: appliedTheme,
            ActiveMonitorsCount: monitors.Count,
            UsedPrimaryMonitorFallback: usedFallback);
    }

    public LifecycleRecoveryResult OnDisplayOrEnvironmentChanged(
        IslandWindowController? windowController,
        string? preferredMonitorDeviceName,
        double anchorHorizontalRatio,
        double anchorTopOffsetDip,
        SystemEnvironmentSnapshot? overrideEnvironment = null)
    {
        long startStamp = Stopwatch.GetTimestamp();

        var env = overrideEnvironment ?? _environmentInspector.CaptureSnapshot();
        var currentThemeDef = _coordinator.CurrentThemeState.Theme;
        var appliedTheme = _coordinator.ApplyTheme(currentThemeDef, env);
        var state = _coordinator.CurrentState;

        var monitors = _displayProber.CaptureMonitors();
        bool usedFallback = false;
        if (windowController is not null)
        {
            var geo = windowController.ApplyLayoutAndRegion(
                state,
                appliedTheme.Theme,
                preferredMonitorDeviceName,
                anchorHorizontalRatio,
                anchorTopOffsetDip);
            usedFallback = geo.UsedPrimaryMonitorFallback;
        }

        double latencyMs = Math.Round(Stopwatch.GetElapsedTime(startStamp).TotalMilliseconds, 3);

        _logger.Log(
            DiagnosticLogLevel.Info,
            "Lifecycle",
            "DISPLAY_OR_ENV_CHANGED_RECOVERED",
            $"Reajuste de monitor/DPI/entorno en {latencyMs} ms (Monitores={monitors.Count}, FallbackPrimary={usedFallback}, Material={appliedTheme.EffectiveMaterial}).");

        return new LifecycleRecoveryResult(
            EventName: "DisplayOrEnvironmentChanged",
            RecoveryLatencyMs: latencyMs,
            ResultingState: state,
            ResultingTheme: appliedTheme,
            ActiveMonitorsCount: monitors.Count,
            UsedPrimaryMonitorFallback: usedFallback);
    }

    public FullScreenStateSnapshot EvaluateFullScreenPolicy()
    {
        var fs = _displayProber.CaptureFullScreenState();
        var currentPolicy = _coordinator.CurrentState.Policy;

        if (fs.ShouldPauseAutomaticExpansions && currentPolicy == IslandPolicyMode.Normal)
        {
            _pausedByFullScreen = true;
            _coordinator.SetPolicyMode(IslandPolicyMode.Paused);
        }
        else if (!fs.ShouldPauseAutomaticExpansions && _pausedByFullScreen && currentPolicy == IslandPolicyMode.Paused)
        {
            _pausedByFullScreen = false;
            _coordinator.SetPolicyMode(IslandPolicyMode.Normal);
        }

        return fs;
    }
}
