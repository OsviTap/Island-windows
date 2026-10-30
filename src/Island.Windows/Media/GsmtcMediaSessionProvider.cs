using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Island.Windows.Media;

/// <summary>
/// Proveedor real de sesiones multimedia basado en Windows GSMTC
/// (GlobalSystemMediaTransportControlsSessionManager).
/// </summary>
public sealed class GsmtcMediaSessionProvider : IMediaSessionProvider
{
    private const int MaxThumbnailInspectBytes = 2 * 1024 * 1024;

    private readonly IDiagnosticLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly List<GlobalSystemMediaTransportControlsSession> _subscribedSessions = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IntegrationCapabilityReport _lastCapabilityReport;
    private bool _disposed;

    public GsmtcMediaSessionProvider(IDiagnosticLogger logger, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastCapabilityReport = new IntegrationCapabilityReport(
            IntegrationModuleId.Media,
            IntegrationCapabilityState.Disconnected,
            "GSMTC_NOT_PROBED",
            "Aún no se ha consultado GlobalSystemMediaTransportControlsSessionManager.",
            [],
            _timeProvider.GetUtcNow());
    }

    public event EventHandler<IReadOnlyList<MediaSessionSnapshot>>? SessionsChanged;

    public IntegrationCapabilityReport GetCapabilityReport() => _lastCapabilityReport;

    public async Task<MediaViabilityProbeResult> ProbeSessionsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var now = _timeProvider.GetUtcNow();

        try
        {
            if (_manager is null)
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager is not null)
                {
                    _manager.SessionsChanged += OnManagerSessionsChanged;
                    _manager.CurrentSessionChanged += OnManagerCurrentSessionChanged;
                }
            }

            if (_manager is null)
            {
                _lastCapabilityReport = new IntegrationCapabilityReport(
                    IntegrationModuleId.Media,
                    IntegrationCapabilityState.Disconnected,
                    "GSMTC_MANAGER_NULL",
                    "RequestAsync devolvió null.",
                    [],
                    now);

                return new MediaViabilityProbeResult(_lastCapabilityReport, null, [], false, false, now);
            }

            var currentSession = _manager.GetCurrentSession();
            string? currentId = currentSession?.SourceAppUserModelId;
            var rawSessions = _manager.GetSessions();
            SyncSessionEventSubscriptions(rawSessions);
            var snapshots = new List<MediaSessionSnapshot>(rawSessions.Count);

            foreach (var session in rawSessions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snap = await CaptureSessionSnapshotAsync(session, currentId, now);
                snapshots.Add(snap);
            }

            bool spotifyFound = snapshots.Any(s => s.ProviderKind == MediaProviderKind.Spotify);
            bool appleMusicFound = snapshots.Any(s => s.ProviderKind == MediaProviderKind.AppleMusic);

            _lastCapabilityReport = new IntegrationCapabilityReport(
                IntegrationModuleId.Media,
                IntegrationCapabilityState.Available,
                "GSMTC_OPERATIONAL",
                $"GSMTC operativo. Sesiones activas detectadas: {snapshots.Count} (Spotify={spotifyFound}, AppleMusic={appleMusicFound}).",
                ["EnumerateSessions", "ReadMetadata", "ReadTimeline", "ReadThumbnail", "TransportControls"],
                now);

            _logger.Log(
                DiagnosticLogLevel.Info,
                "Media",
                "GSMTC_PROBE_COMPLETED",
                $"Sesiones GSMTC leídas: {snapshots.Count} (Spotify={spotifyFound}, AppleMusic={appleMusicFound}).");

            return new MediaViabilityProbeResult(
                CapabilityReport: _lastCapabilityReport,
                CurrentSessionId: currentId,
                Sessions: snapshots,
                SpotifySessionDetected: spotifyFound,
                AppleMusicSessionDetected: appleMusicFound,
                ProbedAtUtc: now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastCapabilityReport = new IntegrationCapabilityReport(
                IntegrationModuleId.Media,
                IntegrationCapabilityState.Faulted,
                $"GSMTC_ERROR_{ex.GetType().Name}",
                $"Error al consultar GSMTC: {ex.Message}",
                [],
                now);

            _logger.Log(
                DiagnosticLogLevel.Error,
                "Media",
                "GSMTC_PROBE_FAILED",
                $"Fallo al consultar GSMTC ({ex.GetType().Name}): {ex.Message}");

            return new MediaViabilityProbeResult(_lastCapabilityReport, null, [], false, false, now);
        }
    }

    public async Task<bool> TryExecuteCommandAsync(
        string sourceAppUserModelId,
        MediaControlCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId) || _manager is null)
        {
            return false;
        }

        try
        {
            var target = _manager.GetSessions()
                .FirstOrDefault(s => string.Equals(s.SourceAppUserModelId, sourceAppUserModelId, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return command switch
            {
                MediaControlCommand.Play => await target.TryPlayAsync(),
                MediaControlCommand.Pause => await target.TryPauseAsync(),
                MediaControlCommand.TogglePlayPause => await target.TryTogglePlayPauseAsync(),
                MediaControlCommand.SkipNext => await target.TrySkipNextAsync(),
                MediaControlCommand.SkipPrevious => await target.TrySkipPreviousAsync(),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.Log(
                DiagnosticLogLevel.Warning,
                "Media",
                "GSMTC_COMMAND_FAILED",
                $"Error al enviar comando {command}: {ex.GetType().Name}");
            return false;
        }
    }

    public static MediaProviderKind ClassifyProvider(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return MediaProviderKind.Other;
        }

        if (sourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            return MediaProviderKind.Spotify;
        }

        if (sourceAppUserModelId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("AppleInc.AppleMusicWin", StringComparison.OrdinalIgnoreCase))
        {
            return MediaProviderKind.AppleMusic;
        }

        if (sourceAppUserModelId.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("chrome", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("firefox", StringComparison.OrdinalIgnoreCase) ||
            sourceAppUserModelId.Contains("brave", StringComparison.OrdinalIgnoreCase))
        {
            return MediaProviderKind.Browser;
        }

        return MediaProviderKind.Other;
    }

    private static async Task<MediaSessionSnapshot> CaptureSessionSnapshotAsync(
        GlobalSystemMediaTransportControlsSession session,
        string? currentSessionId,
        DateTimeOffset now)
    {
        string sourceId = session.SourceAppUserModelId ?? string.Empty;
        var providerKind = ClassifyProvider(sourceId);
        bool isCurrent = !string.IsNullOrEmpty(currentSessionId) &&
                         string.Equals(sourceId, currentSessionId, StringComparison.OrdinalIgnoreCase);

        var playbackInfo = session.GetPlaybackInfo();
        var controls = playbackInfo?.Controls;
        var capabilities = new MediaTransportCapabilities(
            CanPlay: controls?.IsPlayEnabled ?? false,
            CanPause: controls?.IsPauseEnabled ?? false,
            CanTogglePlayPause: controls?.IsPlayPauseToggleEnabled ?? false,
            CanStop: controls?.IsStopEnabled ?? false,
            CanSkipNext: controls?.IsNextEnabled ?? false,
            CanSkipPrevious: controls?.IsPreviousEnabled ?? false,
            CanSeek: controls?.IsPlaybackPositionEnabled ?? false);

        var playbackState = MapPlaybackStatus(playbackInfo?.PlaybackStatus);

        var timelineProps = session.GetTimelineProperties();
        TimeSpan start = timelineProps?.StartTime ?? TimeSpan.Zero;
        TimeSpan end = timelineProps?.EndTime ?? TimeSpan.Zero;
        TimeSpan pos = timelineProps?.Position ?? TimeSpan.Zero;
        TimeSpan duration = end > start ? end - start : TimeSpan.Zero;
        DateTimeOffset lastUpdated = timelineProps?.LastUpdatedTime ?? DateTimeOffset.MinValue;
        bool isTimelineValid = duration > TimeSpan.Zero && pos >= TimeSpan.Zero && pos <= duration;

        var timeline = new MediaTimelineSnapshot(
            Position: pos,
            Duration: duration,
            StartTime: start,
            EndTime: end,
            LastUpdatedAtUtc: lastUpdated,
            IsTimelineValid: isTimelineValid);

        string title = string.Empty;
        string artist = string.Empty;
        string album = string.Empty;
        bool hasThumbnail = false;
        int thumbnailBytes = 0;
        byte[]? thumbnailData = null;

        try
        {
            var mediaProps = await session.TryGetMediaPropertiesAsync();
            if (mediaProps is not null)
            {
                title = mediaProps.Title ?? string.Empty;
                artist = mediaProps.Artist ?? string.Empty;
                album = mediaProps.AlbumTitle ?? string.Empty;

                if (mediaProps.Thumbnail is not null)
                {
                    using IRandomAccessStreamWithContentType stream = await mediaProps.Thumbnail.OpenReadAsync();
                    if (stream is not null && stream.Size > 0)
                    {
                        hasThumbnail = true;
                        thumbnailBytes = (int)Math.Min(stream.Size, (ulong)int.MaxValue);
                        if (stream.Size <= MaxThumbnailInspectBytes)
                        {
                            using var reader = new DataReader(stream.GetInputStreamAt(0));
                            uint loaded = await reader.LoadAsync((uint)stream.Size);
                            if (loaded == stream.Size)
                            {
                                thumbnailData = new byte[loaded];
                                reader.ReadBytes(thumbnailData);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Algunos proveedores pueden cerrar la sesión mientras se consultan las propiedades asíncronas.
        }

        return new MediaSessionSnapshot(
            SourceAppUserModelId: sourceId,
            ProviderKind: providerKind,
            IsCurrentSystemSession: isCurrent,
            Title: title,
            Artist: artist,
            AlbumTitle: album,
            PlaybackState: playbackState,
            Capabilities: capabilities,
            Timeline: timeline,
            HasThumbnailStream: hasThumbnail,
            ThumbnailByteCount: thumbnailBytes,
            CapturedAtUtc: now) { ThumbnailData = thumbnailData };
    }

    private static MediaPlaybackState MapPlaybackStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) =>
        status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.Closed,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackState.Opened,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Changing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
            _ => MediaPlaybackState.Unknown
        };

    private async void OnManagerSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        var result = await ProbeSessionsAsync();
        SessionsChanged?.Invoke(this, result.Sessions);
    }

    private async void OnManagerCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        var result = await ProbeSessionsAsync();
        SessionsChanged?.Invoke(this, result.Sessions);
    }

    private void SyncSessionEventSubscriptions(IReadOnlyList<GlobalSystemMediaTransportControlsSession> currentSessions)
    {
        lock (_subscribedSessions)
        {
            foreach (var oldSession in _subscribedSessions)
            {
                try
                {
                    oldSession.MediaPropertiesChanged -= OnSessionPropertiesOrPlaybackChanged;
                    oldSession.PlaybackInfoChanged -= OnSessionPropertiesOrPlaybackChanged;
                }
                catch (Exception)
                {
                }
            }

            _subscribedSessions.Clear();

            foreach (var session in currentSessions)
            {
                try
                {
                    session.MediaPropertiesChanged += OnSessionPropertiesOrPlaybackChanged;
                    session.PlaybackInfoChanged += OnSessionPropertiesOrPlaybackChanged;
                    _subscribedSessions.Add(session);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private async void OnSessionPropertiesOrPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        var result = await ProbeSessionsAsync();
        SessionsChanged?.Invoke(this, result.Sessions);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            lock (_subscribedSessions)
            {
                foreach (var oldSession in _subscribedSessions)
                {
                    try
                    {
                        oldSession.MediaPropertiesChanged -= OnSessionPropertiesOrPlaybackChanged;
                        oldSession.PlaybackInfoChanged -= OnSessionPropertiesOrPlaybackChanged;
                    }
                    catch (Exception)
                    {
                    }
                }
                _subscribedSessions.Clear();
            }

            if (_manager is not null)
            {
                _manager.SessionsChanged -= OnManagerSessionsChanged;
                _manager.CurrentSessionChanged -= OnManagerCurrentSessionChanged;
                _manager = null;
            }
        }

        return ValueTask.CompletedTask;
    }
}
