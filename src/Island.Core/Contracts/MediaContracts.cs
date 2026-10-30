namespace Island.Core.Contracts;

public enum MediaPlaybackState
{
    Unknown = 0,
    Closed = 1,
    Opened = 2,
    Changing = 3,
    Stopped = 4,
    Playing = 5,
    Paused = 6
}

public enum MediaProviderKind
{
    Other = 0,
    Spotify = 1,
    AppleMusic = 2,
    Browser = 3
}

public enum MediaControlCommand
{
    Play = 0,
    Pause = 1,
    TogglePlayPause = 2,
    SkipNext = 3,
    SkipPrevious = 4
}

/// <summary>
/// Capacidades reales publicadas por la sesión GSMTC del reproductor activo (Sección 6).
/// </summary>
public sealed record MediaTransportCapabilities(
    bool CanPlay,
    bool CanPause,
    bool CanTogglePlayPause,
    bool CanStop,
    bool CanSkipNext,
    bool CanSkipPrevious,
    bool CanSeek);

/// <summary>
/// Estado de la línea de tiempo reportado por GSMTC.
/// Incluye validación frente a duraciones nulas, negativas o desactualizadas.
/// </summary>
public sealed record MediaTimelineSnapshot(
    TimeSpan Position,
    TimeSpan Duration,
    TimeSpan StartTime,
    TimeSpan EndTime,
    DateTimeOffset LastUpdatedAtUtc,
    bool IsTimelineValid)
{
    public double ProgressRatio =>
        IsTimelineValid && Duration.TotalMilliseconds > 0
            ? Math.Clamp(Position.TotalMilliseconds / Duration.TotalMilliseconds, 0.0, 1.0)
            : 0.0;
}

/// <summary>
/// Instantánea normalizada de una sesión multimedia de Windows (GSMTC).
/// </summary>
public sealed record MediaSessionSnapshot(
    string SourceAppUserModelId,
    MediaProviderKind ProviderKind,
    bool IsCurrentSystemSession,
    string Title,
    string Artist,
    string AlbumTitle,
    MediaPlaybackState PlaybackState,
    MediaTransportCapabilities Capabilities,
    MediaTimelineSnapshot Timeline,
    bool HasThumbnailStream,
    int ThumbnailByteCount,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Resultado del diagnóstico de viabilidad sobre GSMTC.
/// </summary>
public sealed record MediaViabilityProbeResult(
    IntegrationCapabilityReport CapabilityReport,
    string? CurrentSessionId,
    IReadOnlyList<MediaSessionSnapshot> Sessions,
    bool SpotifySessionDetected,
    bool AppleMusicSessionDetected,
    DateTimeOffset ProbedAtUtc);

public interface IMediaSessionProvider : IAsyncDisposable
{
    event EventHandler<IReadOnlyList<MediaSessionSnapshot>>? SessionsChanged;

    IntegrationCapabilityReport GetCapabilityReport();

    Task<MediaViabilityProbeResult> ProbeSessionsAsync(CancellationToken cancellationToken = default);

    Task<bool> TryExecuteCommandAsync(
        string sourceAppUserModelId,
        MediaControlCommand command,
        CancellationToken cancellationToken = default);
}
