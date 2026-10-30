using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Windows.Runtime;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Island.Windows.Notifications;

/// <summary>
/// Sonda de viabilidad para UserNotificationListener en Windows 11.
/// Comprueba identidad de paquete MSIX, estado de permiso, lectura de notificaciones Toast
/// y el comportamiento de suscripción al evento NotificationChanged (Riesgo #6172).
/// </summary>
public sealed class WindowsNotificationListenerProbe : INotificationListenerProbe
{
    public const string WhatsAppPackageFamilyPrefix = "5319275A.WhatsAppDesktop";

    private readonly IPackageIdentityProvider _identityProvider;
    private readonly IDiagnosticLogger _logger;
    private readonly TimeProvider _timeProvider;

    public WindowsNotificationListenerProbe(
        IPackageIdentityProvider identityProvider,
        IDiagnosticLogger logger,
        TimeProvider? timeProvider = null)
    {
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<NotificationViabilityProbeResult> ProbeAsync(
        bool requestPermissionIfUnspecified = false,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var identity = _identityProvider.GetCurrentIdentity();

        bool isApiPresent = false;
        try
        {
            isApiPresent = ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener");
        }
        catch (Exception)
        {
            isApiPresent = false;
        }

        if (!isApiPresent)
        {
            var unsupportedReport = new IntegrationCapabilityReport(
                IntegrationModuleId.Notifications,
                IntegrationCapabilityState.Disconnected,
                "API_NOT_PRESENT",
                "UserNotificationListener no está presente en este entorno.",
                [],
                now);

            return new NotificationViabilityProbeResult(
                CapabilityReport: unsupportedReport,
                HasPackageIdentity: identity.HasPackageIdentity,
                PackageFullName: identity.PackageFullName,
                AccessState: NotificationAccessState.NotSupportedByOs,
                EventSubscriptionSucceeded: false,
                EventSubscriptionDiagnostic: "API_NOT_PRESENT",
                TotalActiveToastsRead: 0,
                WhatsAppToastsRead: 0,
                ToastSummaries: [],
                ProbedAtUtc: now);
        }

        try
        {
            var listener = UserNotificationListener.Current;
            UserNotificationListenerAccessStatus rawStatus;

            if (!identity.HasPackageIdentity)
            {
                // En procesos sin identidad de paquete MSIX, comprobamos empíricamente qué devuelve GetAccessStatus y NotificationChanged (#6172)
                try
                {
                    rawStatus = listener.GetAccessStatus();
                }
                catch (Exception ex)
                {
                    var noIdReport = new IntegrationCapabilityReport(
                        IntegrationModuleId.Notifications,
                        IntegrationCapabilityState.PermissionRequired,
                        $"UNPACKAGED_GETACCESS_ERROR_0x{ex.HResult:X8}",
                        "Se requiere identidad MSIX para consultar UserNotificationListener.",
                        [],
                        now);

                    return new NotificationViabilityProbeResult(
                        CapabilityReport: noIdReport,
                        HasPackageIdentity: false,
                        PackageFullName: string.Empty,
                        AccessState: NotificationAccessState.UnpackagedNoIdentity,
                        EventSubscriptionSucceeded: false,
                        EventSubscriptionDiagnostic: $"GETACCESS_THREW_{ex.GetType().Name}_0x{ex.HResult:X8}",
                        TotalActiveToastsRead: 0,
                        WhatsAppToastsRead: 0,
                        ToastSummaries: [],
                        ProbedAtUtc: now);
                }
            }
            else
            {
                rawStatus = listener.GetAccessStatus();
                if (rawStatus == UserNotificationListenerAccessStatus.Unspecified && requestPermissionIfUnspecified)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    rawStatus = await listener.RequestAccessAsync();
                }
            }

            // Comprobación empírica de suscripción al evento NotificationChanged (Riesgo documentado #6172)
            bool eventSubSucceeded = false;
            string eventSubDiag;
            TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs> dummyHandler = static (_, _) => { };
            try
            {
                listener.NotificationChanged += dummyHandler;
                listener.NotificationChanged -= dummyHandler;
                eventSubSucceeded = true;
                eventSubDiag = "SUBSCRIPTION_OK";
            }
            catch (Exception subEx)
            {
                eventSubSucceeded = false;
                eventSubDiag = $"SUBSCRIPTION_FAILED_{subEx.GetType().Name}_0x{subEx.HResult:X8}:{subEx.Message}";
            }

            var accessState = rawStatus switch
            {
                UserNotificationListenerAccessStatus.Allowed => NotificationAccessState.Allowed,
                UserNotificationListenerAccessStatus.Denied => NotificationAccessState.Denied,
                UserNotificationListenerAccessStatus.Unspecified => identity.HasPackageIdentity
                    ? NotificationAccessState.Unspecified
                    : NotificationAccessState.UnpackagedNoIdentity,
                _ => NotificationAccessState.Unknown
            };

            var summaries = new List<NotificationToastSummary>();
            if (rawStatus == UserNotificationListenerAccessStatus.Allowed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in notifications)
                {
                    string aumid = string.Empty;
                    string displayName = string.Empty;
                    try
                    {
                        aumid = n.AppInfo?.AppUserModelId ?? string.Empty;
                        displayName = n.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        // Algunas notificaciones del sistema no exponen AppInfo completo.
                    }

                    bool isWhatsApp = aumid.Contains(WhatsAppPackageFamilyPrefix, StringComparison.OrdinalIgnoreCase) ||
                                      aumid.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
                                      displayName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);

                    bool hasGeneric = false;
                    int textCount = 0;
                    try
                    {
                        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                        if (binding is not null)
                        {
                            hasGeneric = true;
                            textCount = binding.GetTextElements()?.Count ?? 0;
                        }
                    }
                    catch (Exception)
                    {
                    }

                    summaries.Add(new NotificationToastSummary(
                        NotificationId: n.Id,
                        AppUserModelId: aumid,
                        AppDisplayName: displayName,
                        IsWhatsAppMatch: isWhatsApp,
                        CreationTimeUtc: n.CreationTime,
                        HasToastGenericBinding: hasGeneric,
                        TextElementsCount: textCount));
                }
            }

            int whatsAppCount = summaries.Count(s => s.IsWhatsAppMatch);

            var capabilityState = accessState switch
            {
                NotificationAccessState.Allowed when eventSubSucceeded => IntegrationCapabilityState.Available,
                NotificationAccessState.Allowed when !eventSubSucceeded => IntegrationCapabilityState.PartiallySupported,
                NotificationAccessState.Denied => IntegrationCapabilityState.PermissionDenied,
                NotificationAccessState.Unspecified or NotificationAccessState.UnpackagedNoIdentity => IntegrationCapabilityState.PermissionRequired,
                _ => IntegrationCapabilityState.Disconnected
            };

            var report = new IntegrationCapabilityReport(
                IntegrationModuleId.Notifications,
                capabilityState,
                $"ACCESS_{accessState}_EVENTS_{(eventSubSucceeded ? "OK" : "FAIL")}",
                $"HasPackageIdentity={identity.HasPackageIdentity}, Access={accessState}, EventSub={eventSubDiag}, Toasts={summaries.Count}, WhatsApp={whatsAppCount}",
                rawStatus == UserNotificationListenerAccessStatus.Allowed
                    ? ["GetNotificationsAsync", .. (eventSubSucceeded ? new[] { "NotificationChangedEvent" } : Array.Empty<string>())]
                    : [],
                now);

            _logger.Log(
                DiagnosticLogLevel.Info,
                "Notifications",
                "NOTIFICATION_PROBE_COMPLETED",
                report.DiagnosticDetail);

            return new NotificationViabilityProbeResult(
                CapabilityReport: report,
                HasPackageIdentity: identity.HasPackageIdentity,
                PackageFullName: identity.PackageFullName,
                AccessState: accessState,
                EventSubscriptionSucceeded: eventSubSucceeded,
                EventSubscriptionDiagnostic: eventSubDiag,
                TotalActiveToastsRead: summaries.Count,
                WhatsAppToastsRead: whatsAppCount,
                ToastSummaries: summaries,
                ProbedAtUtc: now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var faultReport = new IntegrationCapabilityReport(
                IntegrationModuleId.Notifications,
                IntegrationCapabilityState.Faulted,
                $"NOTIF_FAULT_0x{ex.HResult:X8}",
                $"Excepción en UserNotificationListener: {ex.GetType().Name} (0x{ex.HResult:X8})",
                [],
                now);

            return new NotificationViabilityProbeResult(
                CapabilityReport: faultReport,
                HasPackageIdentity: identity.HasPackageIdentity,
                PackageFullName: identity.PackageFullName,
                AccessState: NotificationAccessState.Faulted,
                EventSubscriptionSucceeded: false,
                EventSubscriptionDiagnostic: $"{ex.GetType().Name}_0x{ex.HResult:X8}",
                TotalActiveToastsRead: 0,
                WhatsAppToastsRead: 0,
                ToastSummaries: [],
                ProbedAtUtc: now);
        }
    }
}
