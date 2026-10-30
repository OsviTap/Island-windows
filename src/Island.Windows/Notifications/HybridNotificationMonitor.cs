using Island.Application.Coordination;
using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Domain;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Island.Windows.Notifications;

/// <summary>
/// Monitor híbrido de notificaciones de Windows 11 que alimenta a IslandStructuralCoordinator:
/// 1. Al iniciar, sincroniza la marca de agua de IDs existentes sin mostrar avisos antiguos.
/// 2. Utiliza el evento push NotificationChanged cuando está disponible (identidad MSIX).
/// 3. Si ocurre la incidencia #6172 (0x80070490 sin identidad MSIX), opera mediante sincronización
///    diferencial sobre GetNotificationsAsync(NotificationKinds.Toast).
/// 4. Nunca elimina notificaciones del Centro de Actividades de Windows cuando se cierra el globo de la isla.
/// </summary>
public sealed class HybridNotificationMonitor : IAsyncDisposable
{
    private readonly IslandStructuralCoordinator _coordinator;
    private readonly IDiagnosticLogger _logger;
    private readonly TimeProvider _timeProvider;

    private UserNotificationListener? _listener;
    private bool _pushSubscribed;
    private bool _disposed;

    public HybridNotificationMonitor(
        IslandStructuralCoordinator coordinator,
        IDiagnosticLogger logger,
        TimeProvider? timeProvider = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsPushEventActive => _pushSubscribed;

    public async Task<bool> InitializeAndSyncWatermarkAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _listener = UserNotificationListener.Current;
            var status = _listener.GetAccessStatus();
            if (status != UserNotificationListenerAccessStatus.Allowed)
            {
                _coordinator.ReportModuleCapability(new IntegrationCapabilityReport(
                    IntegrationModuleId.Notifications,
                    status == UserNotificationListenerAccessStatus.Denied
                        ? IntegrationCapabilityState.PermissionDenied
                        : IntegrationCapabilityState.PermissionRequired,
                    $"MONITOR_ACCESS_{status}",
                    $"Acceso a UserNotificationListener: {status}",
                    [],
                    _timeProvider.GetUtcNow()));
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var existingToasts = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var existingIds = existingToasts.Select(t => t.Id).ToArray();

            // Regla Sección 6: sincronizar estado inicial sin volver a mostrar notificaciones antiguas.
            _coordinator.SynchronizeNotificationWatermark(existingIds);

            try
            {
                _listener.NotificationChanged += OnListenerNotificationChanged;
                _pushSubscribed = true;
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490))
            {
                // Contingencia documentada para #6172: operar por sincronización diferencial de marca de agua.
                _pushSubscribed = false;
            }

            _coordinator.ReportModuleCapability(new IntegrationCapabilityReport(
                IntegrationModuleId.Notifications,
                _pushSubscribed ? IntegrationCapabilityState.Available : IntegrationCapabilityState.PartiallySupported,
                _pushSubscribed ? "MONITOR_PUSH_ACTIVE" : "MONITOR_WATERMARK_SYNC_ACTIVE",
                $"Monitor inicializado con {existingIds.Length} notificaciones previas sincronizadas en marca de agua (Push={_pushSubscribed}).",
                _pushSubscribed ? ["GetNotificationsAsync", "NotificationChanged"] : ["GetNotificationsAsync", "DifferentialWatermarkSync"],
                _timeProvider.GetUtcNow()));

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(
                DiagnosticLogLevel.Warning,
                "Notifications",
                "HYBRID_MONITOR_INIT_ERROR",
                $"Error al inicializar HybridNotificationMonitor: {ex.GetType().Name} (0x{ex.HResult:X8})");
            return false;
        }
    }

    /// <summary>
    /// Sincroniza únicamente las notificaciones nuevas que hayan llegado después de la marca de agua inicial.
    /// </summary>
    public async Task<int> SyncDifferentialNotificationsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _listener is null)
        {
            return 0;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toasts = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            int ingestedCount = 0;

            foreach (var n in toasts.OrderBy(t => t.Id))
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
                }

                string title = string.Empty;
                string body = string.Empty;
                try
                {
                    var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements();
                    if (texts is { Count: > 0 })
                    {
                        title = texts[0]?.Text ?? string.Empty;
                        if (texts.Count > 1)
                        {
                            body = texts[1]?.Text ?? string.Empty;
                        }
                    }
                }
                catch (Exception)
                {
                }

                var incoming = new IncomingNotificationEvent(
                    NotificationId: n.Id,
                    AppUserModelId: aumid,
                    AppDisplayName: displayName,
                    Title: title,
                    Body: body,
                    TimestampUtc: n.CreationTime);

                var res = _coordinator.ProcessIncomingNotification(incoming);
                if (res.Accepted)
                {
                    ingestedCount++;
                }
            }

            return ingestedCount;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private async void OnListenerNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (_disposed || args.ChangeKind != UserNotificationChangedKind.Added)
        {
            return;
        }

        await SyncDifferentialNotificationsAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_listener is not null && _pushSubscribed)
            {
                try
                {
                    _listener.NotificationChanged -= OnListenerNotificationChanged;
                }
                catch (Exception)
                {
                }
            }
            _listener = null;
        }

        return ValueTask.CompletedTask;
    }
}
