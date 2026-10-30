using Island.Application.Theming;
using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Domain;
using Island.Core.Theming;

namespace Island.Application.Coordination;

/// <summary>
/// Coordinador central de estados, prioridades, colas, tiempos y reconexiones de la isla (Secciones 3, 6 y 8).
/// Controla todos los temporizadores desde un único punto mediante un identificador monotónico de generación
/// para impedir que operaciones canceladas restauren estados o contenidos antiguos.
/// </summary>
public sealed class IslandStructuralCoordinator : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<IntegrationModuleId, IntegrationCapabilityReport> _capabilities = new();
    private readonly ThemeService _themeService;
    private readonly IDiagnosticLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly CoordinatorTimingPolicy _timingPolicy;
    private readonly MediaSessionCoordinator _mediaCoordinator;
    private readonly NotificationQueueCoordinator _notificationCoordinator;

    private IslandStateSnapshot _currentState;
    private AppliedThemeState _currentThemeState;
    private DisplayedMediaView? _activeMediaView;
    private DisplayedNotificationView? _activeNotificationView;
    private long _activeGenerationId;
    private ITimer? _transientTimer;
    private bool _disposed;

    public IslandStructuralCoordinator(
        ThemeService themeService,
        IDiagnosticLogger logger,
        SystemEnvironmentSnapshot initialEnvironment,
        TimeProvider? timeProvider = null,
        CoordinatorTimingPolicy? timingPolicy = null)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(initialEnvironment);

        _timeProvider = timeProvider ?? TimeProvider.System;
        _timingPolicy = timingPolicy ?? CoordinatorTimingPolicy.CreateDefault();
        _mediaCoordinator = new MediaSessionCoordinator();
        _notificationCoordinator = new NotificationQueueCoordinator(_timingPolicy);

        var now = _timeProvider.GetUtcNow();
        _currentState = IslandStateSnapshot.CreateInitial(now);
        _currentThemeState = _themeService.ResolveTheme(BuiltInThemes.DefaultSolid, initialEnvironment);

        foreach (IntegrationModuleId moduleId in Enum.GetValues<IntegrationModuleId>())
        {
            _capabilities[moduleId] = new IntegrationCapabilityReport(
                ModuleId: moduleId,
                State: IntegrationCapabilityState.Disconnected,
                StatusCode: "NOT_INITIALIZED",
                DiagnosticDetail: "Módulo pendiente de inicialización.",
                SupportedOperations: [],
                ReportedAtUtc: now);
        }
    }

    public event EventHandler<IslandStateSnapshot>? StateChanged;
    public event EventHandler<AppliedThemeState>? ThemeChanged;
    public event EventHandler<IntegrationCapabilityReport>? CapabilityUpdated;
    public event EventHandler<DisplayedMediaView?>? MediaViewChanged;
    public event EventHandler<DisplayedNotificationView?>? NotificationViewChanged;

    public IslandStateSnapshot CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _currentState;
            }
        }
    }

    public AppliedThemeState CurrentThemeState
    {
        get
        {
            lock (_sync)
            {
                return _currentThemeState;
            }
        }
    }

    public DisplayedMediaView? ActiveMediaView
    {
        get
        {
            lock (_sync)
            {
                return _activeMediaView;
            }
        }
    }

    public DisplayedNotificationView? ActiveNotificationView
    {
        get
        {
            lock (_sync)
            {
                return _activeNotificationView;
            }
        }
    }

    public int PendingNotificationCount
    {
        get
        {
            lock (_sync)
            {
                return _notificationCoordinator.PendingQueueCount;
            }
        }
    }

    public long ActiveGenerationId
    {
        get
        {
            lock (_sync)
            {
                return _activeGenerationId;
            }
        }
    }

    public string? SelectedMediaSourceId
    {
        get
        {
            lock (_sync)
            {
                return _mediaCoordinator.SelectedSourceId;
            }
        }
    }

    public string? PinnedMediaSourceId
    {
        get
        {
            lock (_sync)
            {
                return _mediaCoordinator.PinnedSourceId;
            }
        }
    }

    public IReadOnlyDictionary<IntegrationModuleId, IntegrationCapabilityReport> GetCapabilitiesSnapshot()
    {
        lock (_sync)
        {
            return new Dictionary<IntegrationModuleId, IntegrationCapabilityReport>(_capabilities);
        }
    }

    public void ReportModuleCapability(IntegrationCapabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_sync)
        {
            _capabilities[report.ModuleId] = report;
        }

        _logger.Log(
            report.State == IntegrationCapabilityState.Faulted ? DiagnosticLogLevel.Error : DiagnosticLogLevel.Info,
            "Capabilities",
            $"MODULE_{report.ModuleId.ToString().ToUpperInvariant()}_{report.State.ToString().ToUpperInvariant()}",
            $"Módulo {report.ModuleId} reportó estado {report.State} ({report.StatusCode}).");

        CapabilityUpdated?.Invoke(this, report);
    }

    public void SelectMediaSource(string? sourceAppUserModelId)
    {
        lock (_sync)
        {
            _mediaCoordinator.SelectManualSource(sourceAppUserModelId);
        }
    }

    public void SetPinnedMediaSource(string? sourceAppUserModelId)
    {
        lock (_sync)
        {
            _mediaCoordinator.SetPinnedSource(sourceAppUserModelId);
        }
    }

    public void SynchronizeNotificationWatermark(IEnumerable<uint> existingNotificationIds)
    {
        lock (_sync)
        {
            _notificationCoordinator.SynchronizeStartupWatermark(existingNotificationIds);
        }
    }

    public void SetAllowedNotificationApps(IEnumerable<string> allowedAppTokens)
    {
        lock (_sync)
        {
            _notificationCoordinator.SetAllowedAppTokens(allowedAppTokens);
        }
    }

    public AppliedThemeState ApplyTheme(
        ThemeDefinition? candidateTheme,
        SystemEnvironmentSnapshot environment,
        MaterialKind? requestedMaterialOverride = null,
        bool isBackdropMaterialSupportedByRuntime = true)
    {
        AppliedThemeState resolved;
        lock (_sync)
        {
            resolved = _themeService.ResolveTheme(
                candidateTheme,
                environment,
                requestedMaterialOverride,
                isBackdropMaterialSupportedByRuntime);
            _currentThemeState = resolved;
        }

        ThemeChanged?.Invoke(this, resolved);
        return resolved;
    }

    /// <summary>
    /// Actualiza la política operativa (Normal, Paused por pantalla completa, Private).
    /// </summary>
    public IslandStateSnapshot SetPolicyMode(IslandPolicyMode policyMode)
    {
        IslandStateSnapshot updatedState;
        DisplayedNotificationView? updatedNotif = null;
        bool notifChanged = false;

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            bool shouldCollapseTransient = policyMode == IslandPolicyMode.Paused &&
                                          !_currentState.IsUserProtected &&
                                          _currentState.Visibility == IslandVisibility.Expanded;

            if (shouldCollapseTransient)
            {
                InvalidateAndCancelTimerLocked();
                updatedState = _currentState with
                {
                    Policy = policyMode,
                    Visibility = IslandVisibility.Compact,
                    Content = ResolvePassiveIdleOrMediaContentLocked(),
                    ActiveReasonCode = "POLICY_PAUSED_COLLAPSED",
                    UpdatedAtUtc = now
                };
            }
            else
            {
                updatedState = _currentState with
                {
                    Policy = policyMode,
                    ActiveReasonCode = $"POLICY_{policyMode.ToString().ToUpperInvariant()}",
                    UpdatedAtUtc = now
                };
            }

            _currentState = updatedState;

            if (_activeNotificationView is not null)
            {
                updatedNotif = _notificationCoordinator.GetActiveView(policyMode, now);
                _activeNotificationView = updatedNotif;
                notifChanged = true;
            }
        }

        StateChanged?.Invoke(this, updatedState);
        if (notifChanged)
        {
            NotificationViewChanged?.Invoke(this, updatedNotif);
        }

        return updatedState;
    }

    /// <summary>
    /// Acción explícita del usuario (abrir, cerrar o fijar el panel).
    /// Cancela inmediatamente cualquier temporizador automático en curso (Sección 8).
    /// </summary>
    public IslandStateSnapshot SetUserInteraction(
        IslandInteractionState interaction,
        IslandVisibility? visibilityOverride = null,
        IslandContentKind? contentOverride = null)
    {
        IslandStateSnapshot updated;
        lock (_sync)
        {
            InvalidateAndCancelTimerLocked();

            var nextVisibility = visibilityOverride ?? (interaction == IslandInteractionState.Passive
                ? IslandVisibility.Compact
                : IslandVisibility.Expanded);

            var nextContent = contentOverride ?? (interaction == IslandInteractionState.Passive
                ? ResolvePassiveIdleOrMediaContentLocked()
                : IslandContentKind.ControlCenter);

            updated = _currentState with
            {
                Visibility = nextVisibility,
                Interaction = interaction,
                Content = nextContent,
                ActiveReasonCode = $"USER_INTERACTION_{interaction.ToString().ToUpperInvariant()}",
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            };

            _currentState = updated;
        }

        StateChanged?.Invoke(this, updated);
        return updated;
    }

    /// <summary>
    /// Procesa una actualización de las sesiones multimedia de GSMTC.
    /// Si el usuario está utilizando el panel, actualiza los metadatos de fondo sin cerrar ni interrumpir su panel.
    /// </summary>
    public MediaCoordinationEvaluation ProcessMediaSessions(IReadOnlyList<MediaSessionSnapshot> sessions)
    {
        MediaCoordinationEvaluation eval;
        IslandStateSnapshot? stateToNotify = null;
        DisplayedMediaView? mediaToNotify;

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            eval = _mediaCoordinator.EvaluateSessions(sessions, now);
            _activeMediaView = eval.ActiveMedia;
            mediaToNotify = _activeMediaView;

            if (eval.ActivePlayerDisconnected &&
                !_currentState.IsUserProtected &&
                _currentState.Content == IslandContentKind.MediaSummary)
            {
                InvalidateAndCancelTimerLocked();
                _currentState = _currentState with
                {
                    Visibility = IslandVisibility.Compact,
                    Content = IslandContentKind.Idle,
                    ActiveReasonCode = "MEDIA_PLAYER_DISCONNECTED",
                    UpdatedAtUtc = now
                };
                stateToNotify = _currentState;
            }
            else if (eval.ShouldTriggerTrackChangeExpansion &&
                     !_currentState.IsUserProtected &&
                     _currentState.Policy != IslandPolicyMode.Paused &&
                     _currentState.Content != IslandContentKind.NotificationToast)
            {
                TimeSpan duration = ResolveEffectiveDurationLocked(_timingPolicy.MediaExpansionDuration);
                if (duration > TimeSpan.Zero)
                {
                    ScheduleTransientExpansionLocked(
                        IslandContentKind.MediaSummary,
                        "MEDIA_TRACK_CHANGED",
                        duration,
                        now);
                    stateToNotify = _currentState;
                }
            }
            else if (!_currentState.IsUserProtected &&
                     _currentState.Visibility == IslandVisibility.Compact &&
                     _currentState.Content is IslandContentKind.Idle or IslandContentKind.MediaSummary)
            {
                var nextPassiveContent = ResolvePassiveIdleOrMediaContentLocked();
                if (_currentState.Content != nextPassiveContent)
                {
                    _currentState = _currentState with
                    {
                        Content = nextPassiveContent,
                        ActiveReasonCode = "PASSIVE_MEDIA_STATE_SYNC",
                        UpdatedAtUtc = now
                    };
                    stateToNotify = _currentState;
                }
            }
        }

        MediaViewChanged?.Invoke(this, mediaToNotify);
        if (stateToNotify is not null)
        {
            StateChanged?.Invoke(this, stateToNotify);
        }

        return eval;
    }

    /// <summary>
    /// Procesa una notificación entrante aplicando filtro de aplicación, marca de agua de inicio,
    /// deduplicación, agrupación de ráfagas y cola limitada.
    /// </summary>
    public NotificationIngestionResult ProcessIncomingNotification(IncomingNotificationEvent notification)
    {
        NotificationIngestionResult result;
        IslandStateSnapshot? stateToNotify = null;
        DisplayedNotificationView? notifToNotify = null;
        bool notifyNotifView = false;

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            bool canShowImmediately = !_currentState.IsUserProtected &&
                                      _currentState.Policy != IslandPolicyMode.Paused;

            result = _notificationCoordinator.IngestNotification(
                notification,
                _currentState.Policy,
                canShowImmediately,
                now);

            if (result.Accepted)
            {
                _activeNotificationView = result.ActiveView;
                notifToNotify = _activeNotificationView;
                notifyNotifView = true;

                if (result.ShouldTriggerNewExpansion)
                {
                    TimeSpan duration = ResolveEffectiveDurationLocked(_timingPolicy.NotificationExpansionDuration);
                    if (duration > TimeSpan.Zero)
                    {
                        ScheduleTransientExpansionLocked(
                            IslandContentKind.NotificationToast,
                            "NOTIFICATION_RECEIVED",
                            duration,
                            now);
                        stateToNotify = _currentState;
                    }
                }
            }
        }

        if (notifyNotifView)
        {
            NotificationViewChanged?.Invoke(this, notifToNotify);
        }

        if (stateToNotify is not null)
        {
            StateChanged?.Invoke(this, stateToNotify);
        }

        return result;
    }

    public bool TryPresentAutomaticEvent(IslandContentKind eventContent, string reasonCode, out IslandStateSnapshot resultingState)
    {
        bool applied;
        IslandStateSnapshot snapshot;
        lock (_sync)
        {
            if (_currentState.IsUserProtected || _currentState.Policy == IslandPolicyMode.Paused)
            {
                resultingState = _currentState;
                return false;
            }

            var now = _timeProvider.GetUtcNow();
            TimeSpan duration = eventContent == IslandContentKind.NotificationToast
                ? _timingPolicy.NotificationExpansionDuration
                : _timingPolicy.MediaExpansionDuration;

            ScheduleTransientExpansionLocked(eventContent, reasonCode, ResolveEffectiveDurationLocked(duration), now);
            snapshot = _currentState;
            resultingState = snapshot;
            applied = true;
        }

        if (applied)
        {
            StateChanged?.Invoke(this, snapshot);
        }

        return applied;
    }

    /// <summary>
    /// Completa la exposición temporal asociada a una generación específica.
    /// Si el identificador de generación ya fue invalidado o cancelado, devuelve false y no altera el estado.
    /// </summary>
    public bool TryCompleteTransientGeneration(long expectedGenerationId)
    {
        IslandStateSnapshot? stateToNotify = null;
        DisplayedNotificationView? notifToNotify = null;
        bool notifChanged = false;

        lock (_sync)
        {
            if (_disposed || expectedGenerationId != _activeGenerationId)
            {
                return false;
            }

            DisposeTimerOnlyLocked();
            _activeGenerationId++;

            if (_currentState.IsUserProtected)
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow();

            // Si había avisos pendientes no caducados en la cola y no estamos en Paused, avanzamos al siguiente.
            if (_currentState.Policy != IslandPolicyMode.Paused)
            {
                var nextQueuedNotif = _notificationCoordinator.AdvanceOrCompleteCurrent(_currentState.Policy, now);
                if (nextQueuedNotif is not null)
                {
                    _activeNotificationView = nextQueuedNotif;
                    notifToNotify = nextQueuedNotif;
                    notifChanged = true;

                    TimeSpan duration = ResolveEffectiveDurationLocked(_timingPolicy.NotificationExpansionDuration);
                    ScheduleTransientExpansionLocked(
                        IslandContentKind.NotificationToast,
                        "NOTIFICATION_QUEUE_ADVANCED",
                        duration,
                        now);
                    stateToNotify = _currentState;
                }
                else if (_activeNotificationView is not null)
                {
                    _activeNotificationView = null;
                    notifToNotify = null;
                    notifChanged = true;
                }
            }

            if (stateToNotify is null)
            {
                _currentState = _currentState with
                {
                    Visibility = IslandVisibility.Compact,
                    Interaction = IslandInteractionState.Passive,
                    Content = ResolvePassiveIdleOrMediaContentLocked(),
                    ActiveReasonCode = "TRANSIENT_EXPIRED_RETURN_COMPACT",
                    UpdatedAtUtc = now
                };
                stateToNotify = _currentState;
            }
        }

        if (notifChanged)
        {
            NotificationViewChanged?.Invoke(this, notifToNotify);
        }

        if (stateToNotify is not null)
        {
            StateChanged?.Invoke(this, stateToNotify);
        }

        return true;
    }

    private void ScheduleTransientExpansionLocked(
        IslandContentKind contentKind,
        string reasonCode,
        TimeSpan duration,
        DateTimeOffset nowUtc)
    {
        InvalidateAndCancelTimerLocked();
        long scheduledGen = _activeGenerationId;

        _currentState = _currentState with
        {
            Visibility = IslandVisibility.Expanded,
            Interaction = IslandInteractionState.Passive,
            Content = contentKind,
            ActiveReasonCode = reasonCode,
            UpdatedAtUtc = nowUtc
        };

        _transientTimer = _timeProvider.CreateTimer(
            callback: state =>
            {
                if (state is long genId)
                {
                    TryCompleteTransientGeneration(genId);
                }
            },
            state: scheduledGen,
            dueTime: duration,
            period: Timeout.InfiniteTimeSpan);
    }

    private TimeSpan ResolveEffectiveDurationLocked(TimeSpan baseDuration) =>
        _currentThemeState.EffectiveAnimationProfile switch
        {
            AnimationProfile.Reduced => TimeSpan.FromMilliseconds(Math.Max(1500, baseDuration.TotalMilliseconds * 0.75)),
            _ => baseDuration
        };

    private IslandContentKind ResolvePassiveIdleOrMediaContentLocked() =>
        _activeMediaView is { PlaybackState: MediaPlaybackState.Playing }
            ? IslandContentKind.MediaSummary
            : IslandContentKind.Idle;

    private void InvalidateAndCancelTimerLocked()
    {
        _activeGenerationId++;
        DisposeTimerOnlyLocked();
    }

    private void DisposeTimerOnlyLocked()
    {
        if (_transientTimer is not null)
        {
            _transientTimer.Dispose();
            _transientTimer = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                InvalidateAndCancelTimerLocked();
            }
        }
    }
}
