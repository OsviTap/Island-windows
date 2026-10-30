using Island.Application.Coordination;
using Island.Application.Diagnostics;
using Island.Application.Theming;
using Island.Core.Contracts;
using Island.Core.Domain;

namespace Island.Application.Tests;

/// <summary>
/// Proveedor de reloj simulado (explícitamente identificado como SIMULADO según Sección 13)
/// para probar reglas de tiempo, caducidad, ráfagas y cancelación sin depender de esperas reales.
/// </summary>
public sealed class SimulatedTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private DateTimeOffset _utcNow;
    private readonly List<SimulatedTimer> _timers = new();

    public SimulatedTimeProvider(DateTimeOffset? initialUtc = null)
    {
        _utcNow = initialUtc ?? new DateTimeOffset(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        List<SimulatedTimer> dueTimers;
        lock (_lock)
        {
            _utcNow = _utcNow.Add(delta);
            dueTimers = _timers
                .Where(t => !t.IsDisposed && t.DueAtUtc is { } dueAt && dueAt <= _utcNow)
                .ToList();

            foreach (var timer in dueTimers)
            {
                timer.MarkFired();
            }
        }

        foreach (var timer in dueTimers)
        {
            timer.InvokeCallback();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_lock)
        {
            DateTimeOffset? dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _utcNow.Add(dueTime);
            var timer = new SimulatedTimer(callback, state, dueAt);
            _timers.Add(timer);
            return timer;
        }
    }

    private sealed class SimulatedTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public SimulatedTimer(TimerCallback callback, object? state, DateTimeOffset? dueAtUtc)
        {
            _callback = callback;
            _state = state;
            DueAtUtc = dueAtUtc;
        }

        public DateTimeOffset? DueAtUtc { get; private set; }
        public bool IsDisposed { get; private set; }

        public void MarkFired() => DueAtUtc = null;

        public void InvokeCallback()
        {
            if (!IsDisposed)
            {
                _callback(_state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed) return false;
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            DueAtUtc = null;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class Phase3CoordinationAndConcurrencyTests
{
    private const string WhatsAppAumid = "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App";
    private const string SpotifyAumid = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
    private const string BraveAumid = "Brave";

    private static SystemEnvironmentSnapshot CreateDefaultEnv() =>
        new("10.0.26300.0", 26300, "X64", false, "", true, true, DateTimeOffset.UtcNow);

    private static MediaSessionSnapshot CreateMediaSnapshot(
        string aumid,
        MediaProviderKind kind,
        string title,
        string artist,
        MediaPlaybackState state,
        DateTimeOffset now) =>
        new(
            SourceAppUserModelId: aumid,
            ProviderKind: kind,
            IsCurrentSystemSession: true,
            Title: title,
            Artist: artist,
            AlbumTitle: "Album",
            PlaybackState: state,
            Capabilities: new MediaTransportCapabilities(true, true, true, true, true, true, true),
            Timeline: new MediaTimelineSnapshot(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(180), TimeSpan.Zero, TimeSpan.FromSeconds(180), now, true),
            HasThumbnailStream: true,
            ThumbnailByteCount: 1024,
            CapturedAtUtc: now);

    [Fact]
    public void StartupWatermark_IgnoresOldNotifications_AndPresentsOnlyNewerOnes()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        // Sincronizamos con IDs existentes reales al iniciar (ej. hasta 247565)
        coordinator.SynchronizeNotificationWatermark([247066u, 247462u, 247565u]);

        var oldResult = coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
            NotificationId: 247462u,
            AppUserModelId: WhatsAppAumid,
            AppDisplayName: "WhatsApp",
            Title: "Contacto",
            Body: "Mensaje antiguo",
            TimestampUtc: clock.GetUtcNow()));

        Assert.False(oldResult.Accepted);
        Assert.Equal("IGNORED_OLDER_THAN_STARTUP_WATERMARK", oldResult.ReasonCode);
        Assert.Equal(IslandVisibility.Compact, coordinator.CurrentState.Visibility);

        var newResult = coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
            NotificationId: 247566u,
            AppUserModelId: WhatsAppAumid,
            AppDisplayName: "WhatsApp",
            Title: "Ana",
            Body: "Hola, mensaje nuevo",
            TimestampUtc: clock.GetUtcNow()));

        Assert.True(newResult.Accepted);
        Assert.True(newResult.ShouldTriggerNewExpansion);
        Assert.Equal(IslandVisibility.Expanded, coordinator.CurrentState.Visibility);
        Assert.Equal(IslandContentKind.NotificationToast, coordinator.CurrentState.Content);
    }

    [Fact]
    public void DuplicateNotifications_AreIgnored_WithoutDuplicateStateEvents()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        int stateEventsCount = 0;
        coordinator.StateChanged += (_, _) => stateEventsCount++;

        var incoming = new IncomingNotificationEvent(1001u, WhatsAppAumid, "WhatsApp", "Carlos", "Hola", clock.GetUtcNow());

        var first = coordinator.ProcessIncomingNotification(incoming);
        var second = coordinator.ProcessIncomingNotification(incoming);
        var third = coordinator.ProcessIncomingNotification(incoming);

        Assert.True(first.Accepted);
        Assert.False(second.Accepted);
        Assert.Equal("IGNORED_DUPLICATE_ID", second.ReasonCode);
        Assert.False(third.Accepted);
        Assert.Equal(1, stateEventsCount);
    }

    [Fact]
    public void BurstOfMessages_CoalescesInPlace_WithoutContinuousExpansions()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        int stateTransitionCount = 0;
        coordinator.StateChanged += (_, _) => stateTransitionCount++;

        // Llegan 12 mensajes de WhatsApp en ráfaga (1 cada 200 ms)
        for (uint i = 1; i <= 12; i++)
        {
            var res = coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
                NotificationId: 2000u + i,
                AppUserModelId: WhatsAppAumid,
                AppDisplayName: "WhatsApp",
                Title: "Grupo Proyecto",
                Body: $"Mensaje #{i}",
                TimestampUtc: clock.GetUtcNow()));

            Assert.True(res.Accepted);
            if (i == 1)
            {
                Assert.True(res.ShouldTriggerNewExpansion);
            }
            else
            {
                Assert.False(res.ShouldTriggerNewExpansion);
                Assert.True(res.UpdatedActiveInPlace);
            }

            clock.Advance(TimeSpan.FromMilliseconds(200));
        }

        // Solo hubo 1 expansión de ventana para toda la ráfaga de 12 mensajes
        Assert.Equal(1, stateTransitionCount);
        Assert.NotNull(coordinator.ActiveNotificationView);
        Assert.Equal(12, coordinator.ActiveNotificationView!.BurstCount);
        Assert.Contains("12 mensajes", coordinator.ActiveNotificationView.HeaderText);
    }

    [Fact]
    public void NotificationQueue_EnforcesMaxCapacity_AndPrunesExpiredItems()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        var policy = CoordinatorTimingPolicy.CreateDefault() with
        {
            MaxNotificationQueueSize = 3,
            BurstCoalesceWindow = TimeSpan.Zero // Desactivar agrupación para probar límite estricto de entradas en cola
        };
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock, policy);

        // Usuario abre el panel de control; los avisos entrantes deben ir a la cola sin interrumpir
        coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse);

        for (uint i = 1; i <= 10; i++)
        {
            coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
                NotificationId: 3000u + i,
                AppUserModelId: WhatsAppAumid,
                AppDisplayName: "WhatsApp",
                Title: $"Remitente {i}",
                Body: $"Texto {i}",
                TimestampUtc: clock.GetUtcNow()));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // La cola nunca supera MaxNotificationQueueSize (3)
        Assert.Equal(3, coordinator.PendingNotificationCount);

        // Si pasa más tiempo que NotificationMaxAge (2 minutos), los elementos caducan y no se muestran
        clock.Advance(TimeSpan.FromMinutes(3));
        coordinator.SetUserInteraction(IslandInteractionState.Passive);

        var dummyCheck = coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
            NotificationId: 9999u,
            AppUserModelId: "OtherApp.NotAllowed",
            AppDisplayName: "Other",
            Title: "X",
            Body: "Y",
            TimestampUtc: clock.GetUtcNow()));

        Assert.Equal(0, dummyCheck.PendingQueueCount);
    }

    [Fact]
    public void CancelledGeneration_CannotRevertState_WhenOldTimerFires()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
            NotificationId: 4001u,
            AppUserModelId: WhatsAppAumid,
            AppDisplayName: "WhatsApp",
            Title: "Laura",
            Body: "Aviso temporal",
            TimestampUtc: clock.GetUtcNow()));

        long toastGeneration = coordinator.ActiveGenerationId;
        Assert.Equal(IslandVisibility.Expanded, coordinator.CurrentState.Visibility);
        Assert.Equal(IslandContentKind.NotificationToast, coordinator.CurrentState.Content);

        // El usuario hace clic y fija abierto el centro de control antes de que venza el temporizador del aviso
        coordinator.SetUserInteraction(IslandInteractionState.PinnedOpen);
        Assert.NotEqual(toastGeneration, coordinator.ActiveGenerationId);

        // Aunque el temporizador antiguo intente completarse o el reloj avance 10 segundos, no cierra el panel del usuario
        bool oldCompleted = coordinator.TryCompleteTransientGeneration(toastGeneration);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.False(oldCompleted);
        Assert.Equal(IslandInteractionState.PinnedOpen, coordinator.CurrentState.Interaction);
        Assert.Equal(IslandContentKind.ControlCenter, coordinator.CurrentState.Content);
    }

    [Fact]
    public void MediaCoordinator_SongChangeDoesNotInterruptUser_AndRecoversOnPlayerRestart()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        // 1. Primera sincronización en el arranque no provoca expansión automática
        var initialEval = coordinator.ProcessMediaSessions([
            CreateMediaSnapshot(SpotifyAumid, MediaProviderKind.Spotify, "Canción 1", "Artista 1", MediaPlaybackState.Playing, clock.GetUtcNow()),
            CreateMediaSnapshot(BraveAumid, MediaProviderKind.Browser, "Video", "Canal", MediaPlaybackState.Paused, clock.GetUtcNow())
        ]);

        Assert.False(initialEval.ShouldTriggerTrackChangeExpansion);
        Assert.Equal(SpotifyAumid, coordinator.SelectedMediaSourceId);
        Assert.Equal(IslandVisibility.Compact, coordinator.CurrentState.Visibility);

        // 2. Cambio de canción mientras está en modo pasivo sí expande por MediaExpansionDuration (3.5s) y luego vuelve a Compact
        var track2Eval = coordinator.ProcessMediaSessions([
            CreateMediaSnapshot(SpotifyAumid, MediaProviderKind.Spotify, "Canción 2", "Artista 1", MediaPlaybackState.Playing, clock.GetUtcNow())
        ]);

        Assert.True(track2Eval.ShouldTriggerTrackChangeExpansion);
        Assert.Equal(IslandVisibility.Expanded, coordinator.CurrentState.Visibility);
        Assert.Equal(IslandContentKind.MediaSummary, coordinator.CurrentState.Content);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(IslandVisibility.Compact, coordinator.CurrentState.Visibility);

        // 3. Si el usuario está usando el panel, un cambio a Canción 3 actualiza los datos pero no cierra su panel
        coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse);
        coordinator.ProcessMediaSessions([
            CreateMediaSnapshot(SpotifyAumid, MediaProviderKind.Spotify, "Canción 3", "Artista 1", MediaPlaybackState.Playing, clock.GetUtcNow())
        ]);

        Assert.Equal("Canción 3", coordinator.ActiveMediaView?.Title);
        Assert.Equal(IslandInteractionState.ActiveInUse, coordinator.CurrentState.Interaction);
        Assert.Equal(IslandContentKind.ControlCenter, coordinator.CurrentState.Content);

        // 4. Al cerrarse el reproductor y volver a modo pasivo, limpia la sesión y reconecta limpiamente al reabrirse
        coordinator.SetUserInteraction(IslandInteractionState.Passive);
        var disconnectEval = coordinator.ProcessMediaSessions([]);
        Assert.True(disconnectEval.ActivePlayerDisconnected);
        Assert.Null(coordinator.ActiveMediaView);
        Assert.Equal(IslandContentKind.Idle, coordinator.CurrentState.Content);

        var reconnectEval = coordinator.ProcessMediaSessions([
            CreateMediaSnapshot(SpotifyAumid, MediaProviderKind.Spotify, "Canción Reconectada", "Artista 2", MediaPlaybackState.Playing, clock.GetUtcNow())
        ]);
        Assert.True(reconnectEval.ShouldTriggerTrackChangeExpansion);
        Assert.Equal("Canción Reconectada", coordinator.ActiveMediaView?.Title);
    }

    [Fact]
    public void PrivatePolicyMode_RedactsNotificationBodyAndSenderInActiveView()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        coordinator.SetPolicyMode(IslandPolicyMode.Private);

        coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
            NotificationId: 5001u,
            AppUserModelId: WhatsAppAumid,
            AppDisplayName: "WhatsApp",
            Title: "Secreto Bancario",
            Body: "Tu código es 987654",
            TimestampUtc: clock.GetUtcNow()));

        var view = coordinator.ActiveNotificationView;
        Assert.NotNull(view);
        Assert.True(view!.IsRedactedByPolicy);
        Assert.DoesNotContain("Secreto Bancario", view.HeaderText);
        Assert.DoesNotContain("987654", view.BodyText);
    }

    [Fact]
    public async Task ConcurrentMultithreadedOperations_MaintainInvariantsWithoutExceptions()
    {
        var clock = new SimulatedTimeProvider();
        var logger = new BoundedDiagnosticLogger(timeProvider: clock);
        using var coordinator = new IslandStructuralCoordinator(new ThemeService(logger), logger, CreateDefaultEnv(), clock);

        var tasks = new List<Task>();
        for (int worker = 0; worker < 6; worker++)
        {
            int workerId = worker;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 60; i++)
                {
                    uint notifId = (uint)((workerId * 1000) + i + 1);
                    coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
                        notifId,
                        WhatsAppAumid,
                        "WhatsApp",
                        $"User {workerId}",
                        $"Msg {i}",
                        clock.GetUtcNow()));

                    coordinator.ProcessMediaSessions([
                        CreateMediaSnapshot(SpotifyAumid, MediaProviderKind.Spotify, $"Song {i % 5}", "Artist", MediaPlaybackState.Playing, clock.GetUtcNow())
                    ]);

                    if (i % 10 == 0)
                    {
                        coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse);
                        coordinator.SetUserInteraction(IslandInteractionState.Passive);
                    }

                    clock.Advance(TimeSpan.FromMilliseconds(250));
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.True(coordinator.PendingNotificationCount <= 10);
        Assert.NotNull(coordinator.CurrentState);
    }
}
