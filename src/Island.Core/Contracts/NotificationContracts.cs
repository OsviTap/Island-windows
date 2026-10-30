namespace Island.Core.Contracts;

public enum NotificationAccessState
{
    Unknown = 0,
    Allowed = 1,
    Denied = 2,
    Unspecified = 3,
    UnpackagedNoIdentity = 4,
    NotSupportedByOs = 5,
    Faulted = 6
}

/// <summary>
/// Metadatos estructurales de una notificación capturada en Windows.
/// Los campos de vista previa viven solo en memoria volátil y nunca se persisten en disco por defecto (Sección 6).
/// </summary>
public sealed record NotificationToastSummary(
    uint NotificationId,
    string AppUserModelId,
    string AppDisplayName,
    bool IsWhatsAppMatch,
    DateTimeOffset CreationTimeUtc,
    bool HasToastGenericBinding,
    int TextElementsCount);

/// <summary>
/// Resultado de comprobar la viabilidad de UserNotificationListener (incluyendo el riesgo #6172).
/// </summary>
public sealed record NotificationViabilityProbeResult(
    IntegrationCapabilityReport CapabilityReport,
    bool HasPackageIdentity,
    string PackageFullName,
    NotificationAccessState AccessState,
    bool EventSubscriptionSucceeded,
    string EventSubscriptionDiagnostic,
    int TotalActiveToastsRead,
    int WhatsAppToastsRead,
    IReadOnlyList<NotificationToastSummary> ToastSummaries,
    DateTimeOffset ProbedAtUtc);

public interface INotificationListenerProbe
{
    Task<NotificationViabilityProbeResult> ProbeAsync(
        bool requestPermissionIfUnspecified = false,
        CancellationToken cancellationToken = default);
}
