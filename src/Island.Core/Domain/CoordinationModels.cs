using Island.Core.Contracts;

namespace Island.Core.Domain;

/// <summary>
/// Política de tiempos y límites controlada desde un único coordinador (Sección 8).
/// </summary>
public sealed record CoordinatorTimingPolicy(
    TimeSpan MediaExpansionDuration,
    TimeSpan NotificationExpansionDuration,
    TimeSpan NotificationMaxAge,
    TimeSpan BurstCoalesceWindow,
    TimeSpan BurstExpansionCooldown,
    int MaxNotificationQueueSize)
{
    public static CoordinatorTimingPolicy CreateDefault() => new(
        MediaExpansionDuration: TimeSpan.FromSeconds(3.5),
        NotificationExpansionDuration: TimeSpan.FromSeconds(5.0),
        NotificationMaxAge: TimeSpan.FromMinutes(2.0),
        BurstCoalesceWindow: TimeSpan.FromSeconds(4.0),
        BurstExpansionCooldown: TimeSpan.FromSeconds(6.0),
        MaxNotificationQueueSize: 10);
}

/// <summary>
/// Modelo en memoria volátil de un aviso entrante.
/// Nunca se serializa a disco por defecto (Sección 6).
/// </summary>
public sealed record IncomingNotificationEvent(
    uint NotificationId,
    string AppUserModelId,
    string AppDisplayName,
    string Title,
    string Body,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Vista procesada de un aviso lista para mostrarse en la isla,
/// con agrupación de ráfagas y aplicación de política de privacidad.
/// </summary>
public sealed record DisplayedNotificationView(
    uint LatestNotificationId,
    string AppUserModelId,
    string AppDisplayName,
    string HeaderText,
    string BodyText,
    int BurstCount,
    bool IsRedactedByPolicy,
    DateTimeOffset FirstReceivedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Vista procesada de la sesión multimedia seleccionada actualmente.
/// </summary>
public sealed record DisplayedMediaView(
    string SourceAppUserModelId,
    MediaProviderKind ProviderKind,
    string Title,
    string Artist,
    string AlbumTitle,
    MediaPlaybackState PlaybackState,
    MediaTransportCapabilities Capabilities,
    MediaTimelineSnapshot Timeline,
    bool HasThumbnail,
    bool IsPinnedByUser,
    DateTimeOffset UpdatedAtUtc);
