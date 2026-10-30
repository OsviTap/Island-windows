using Island.Core.Domain;

namespace Island.Application.Coordination;

public sealed record NotificationIngestionResult(
    bool Accepted,
    bool ShouldTriggerNewExpansion,
    bool UpdatedActiveInPlace,
    DisplayedNotificationView? ActiveView,
    int PendingQueueCount,
    string ReasonCode);

/// <summary>
/// Gestiona la sincronización inicial sin re-mostrar notificaciones antiguas, filtrado por aplicación,
/// deduplicación, agrupación de ráfagas (burst coalescing), caducidad y límite de cola (Secciones 6 y 8).
/// </summary>
public sealed class NotificationQueueCoordinator
{
    private const int MaxRememberedSeenIds = 512;

    private readonly CoordinatorTimingPolicy _timingPolicy;
    private readonly HashSet<uint> _seenIds = new();
    private readonly Queue<uint> _seenIdsOrder = new();
    private readonly List<QueuedNotificationEntry> _queue = new();
    private readonly HashSet<string> _allowedAppTokens = new(StringComparer.OrdinalIgnoreCase);

    private uint _startupWatermarkMaxId;
    private bool _watermarkInitialized;
    private QueuedNotificationEntry? _activeDisplayedEntry;
    private DateTimeOffset _lastExpansionTriggeredAtUtc = DateTimeOffset.MinValue;

    public NotificationQueueCoordinator(
        CoordinatorTimingPolicy timingPolicy,
        IEnumerable<string>? initialAllowedAppTokens = null)
    {
        _timingPolicy = timingPolicy ?? throw new ArgumentNullException(nameof(timingPolicy));
        if (initialAllowedAppTokens is not null)
        {
            foreach (string token in initialAllowedAppTokens)
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _allowedAppTokens.Add(token.Trim());
                }
            }
        }

        if (_allowedAppTokens.Count == 0)
        {
            _allowedAppTokens.Add("5319275A.WhatsAppDesktop");
            _allowedAppTokens.Add("WhatsApp");
        }
    }

    public int PendingQueueCount => _queue.Count;
    public uint StartupWatermarkMaxId => _startupWatermarkMaxId;
    public bool IsWatermarkInitialized => _watermarkInitialized;

    /// <summary>
    /// Al iniciar la aplicación, registra los identificadores de todas las notificaciones ya existentes
    /// en Windows para que no vuelvan a mostrarse como avisos nuevos (Sección 6).
    /// </summary>
    public void SynchronizeStartupWatermark(IEnumerable<uint> existingNotificationIds)
    {
        ArgumentNullException.ThrowIfNull(existingNotificationIds);

        foreach (uint id in existingNotificationIds)
        {
            RememberSeenId(id);
            if (id > _startupWatermarkMaxId)
            {
                _startupWatermarkMaxId = id;
            }
        }

        _watermarkInitialized = true;
    }

    public void SetAllowedAppTokens(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _allowedAppTokens.Clear();
        foreach (string token in tokens)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                _allowedAppTokens.Add(token.Trim());
            }
        }
    }

    public NotificationIngestionResult IngestNotification(
        IncomingNotificationEvent notification,
        IslandPolicyMode policyMode,
        bool canShowImmediately,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(notification);
        PruneExpired(nowUtc);

        if (_watermarkInitialized && notification.NotificationId <= _startupWatermarkMaxId)
        {
            return new NotificationIngestionResult(
                Accepted: false,
                ShouldTriggerNewExpansion: false,
                UpdatedActiveInPlace: false,
                ActiveView: GetActiveView(policyMode, nowUtc),
                PendingQueueCount: _queue.Count,
                ReasonCode: "IGNORED_OLDER_THAN_STARTUP_WATERMARK");
        }

        if (_seenIds.Contains(notification.NotificationId))
        {
            return new NotificationIngestionResult(
                Accepted: false,
                ShouldTriggerNewExpansion: false,
                UpdatedActiveInPlace: false,
                ActiveView: GetActiveView(policyMode, nowUtc),
                PendingQueueCount: _queue.Count,
                ReasonCode: "IGNORED_DUPLICATE_ID");
        }

        if (!IsAppAllowed(notification.AppUserModelId, notification.AppDisplayName))
        {
            return new NotificationIngestionResult(
                Accepted: false,
                ShouldTriggerNewExpansion: false,
                UpdatedActiveInPlace: false,
                ActiveView: GetActiveView(policyMode, nowUtc),
                PendingQueueCount: _queue.Count,
                ReasonCode: "IGNORED_APP_NOT_IN_ALLOWLIST");
        }

        RememberSeenId(notification.NotificationId);
        DateTimeOffset expiresAt = nowUtc + _timingPolicy.NotificationMaxAge;

        // 1. Si hay un aviso activo en pantalla de la misma aplicación dentro de la ventana de ráfaga,
        // agrupamos en el aviso activo sin provocar expansiones continuas.
        if (_activeDisplayedEntry is not null &&
            string.Equals(_activeDisplayedEntry.AppUserModelId, notification.AppUserModelId, StringComparison.OrdinalIgnoreCase) &&
            (nowUtc - _activeDisplayedEntry.UpdatedAtUtc) <= _timingPolicy.BurstCoalesceWindow)
        {
            _activeDisplayedEntry = _activeDisplayedEntry with
            {
                LatestNotificationId = notification.NotificationId,
                Title = notification.Title,
                Body = notification.Body,
                BurstCount = _activeDisplayedEntry.BurstCount + 1,
                UpdatedAtUtc = nowUtc,
                ExpiresAtUtc = expiresAt
            };

            return new NotificationIngestionResult(
                Accepted: true,
                ShouldTriggerNewExpansion: false,
                UpdatedActiveInPlace: true,
                ActiveView: ToDisplayedView(_activeDisplayedEntry, policyMode),
                PendingQueueCount: _queue.Count,
                ReasonCode: "COALESCED_INTO_ACTIVE_BURST");
        }

        // 2. Si no se puede mostrar de inmediato (ej. usuario usando el panel o política Paused, o en cooldown de ráfaga),
        // se agrupa en la cola limitada.
        bool inBurstCooldown = _lastExpansionTriggeredAtUtc != DateTimeOffset.MinValue &&
                               (nowUtc - _lastExpansionTriggeredAtUtc) < _timingPolicy.BurstExpansionCooldown &&
                               _activeDisplayedEntry is not null;

        if (!canShowImmediately || policyMode == IslandPolicyMode.Paused || inBurstCooldown)
        {
            EnqueueOrCoalesce(notification, nowUtc, expiresAt);
            return new NotificationIngestionResult(
                Accepted: true,
                ShouldTriggerNewExpansion: false,
                UpdatedActiveInPlace: false,
                ActiveView: GetActiveView(policyMode, nowUtc),
                PendingQueueCount: _queue.Count,
                ReasonCode: "QUEUED_BOUNDED");
        }

        // 3. Mostrar inmediatamente
        _activeDisplayedEntry = new QueuedNotificationEntry(
            LatestNotificationId: notification.NotificationId,
            AppUserModelId: notification.AppUserModelId,
            AppDisplayName: string.IsNullOrWhiteSpace(notification.AppDisplayName) ? "Aplicación" : notification.AppDisplayName,
            Title: notification.Title,
            Body: notification.Body,
            BurstCount: 1,
            FirstReceivedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc,
            ExpiresAtUtc: expiresAt);

        _lastExpansionTriggeredAtUtc = nowUtc;

        return new NotificationIngestionResult(
            Accepted: true,
            ShouldTriggerNewExpansion: true,
            UpdatedActiveInPlace: false,
            ActiveView: ToDisplayedView(_activeDisplayedEntry, policyMode),
            PendingQueueCount: _queue.Count,
            ReasonCode: "PRESENTED_IMMEDIATELY");
    }

    /// <summary>
    /// Extrae el siguiente aviso no caducado de la cola (si existe) para mostrarlo,
    /// o limpia el aviso activo actual cuando vence su tiempo de exposición.
    /// No elimina notificaciones del centro de actividades de Windows (Sección 6).
    /// </summary>
    public DisplayedNotificationView? AdvanceOrCompleteCurrent(IslandPolicyMode policyMode, DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);

        if (_queue.Count > 0)
        {
            _activeDisplayedEntry = _queue[0];
            _queue.RemoveAt(0);
            _lastExpansionTriggeredAtUtc = nowUtc;
            return ToDisplayedView(_activeDisplayedEntry, policyMode);
        }

        _activeDisplayedEntry = null;
        return null;
    }

    public DisplayedNotificationView? GetActiveView(IslandPolicyMode policyMode, DateTimeOffset nowUtc)
    {
        PruneExpired(nowUtc);
        return _activeDisplayedEntry is null ? null : ToDisplayedView(_activeDisplayedEntry, policyMode);
    }

    public void PruneExpired(DateTimeOffset nowUtc)
    {
        _queue.RemoveAll(item => item.ExpiresAtUtc <= nowUtc);
        if (_activeDisplayedEntry is not null && _activeDisplayedEntry.ExpiresAtUtc <= nowUtc)
        {
            _activeDisplayedEntry = null;
        }
    }

    private void EnqueueOrCoalesce(IncomingNotificationEvent notification, DateTimeOffset nowUtc, DateTimeOffset expiresAt)
    {
        int existingIndex = _queue.FindLastIndex(q =>
            string.Equals(q.AppUserModelId, notification.AppUserModelId, StringComparison.OrdinalIgnoreCase) &&
            (nowUtc - q.UpdatedAtUtc) <= _timingPolicy.BurstCoalesceWindow);

        if (existingIndex >= 0)
        {
            var existing = _queue[existingIndex];
            _queue[existingIndex] = existing with
            {
                LatestNotificationId = notification.NotificationId,
                Title = notification.Title,
                Body = notification.Body,
                BurstCount = existing.BurstCount + 1,
                UpdatedAtUtc = nowUtc,
                ExpiresAtUtc = expiresAt
            };
            return;
        }

        if (_queue.Count >= _timingPolicy.MaxNotificationQueueSize)
        {
            _queue.RemoveAt(0); // Descarta el más antiguo para respetar el límite estricto de la cola
        }

        _queue.Add(new QueuedNotificationEntry(
            LatestNotificationId: notification.NotificationId,
            AppUserModelId: notification.AppUserModelId,
            AppDisplayName: string.IsNullOrWhiteSpace(notification.AppDisplayName) ? "Aplicación" : notification.AppDisplayName,
            Title: notification.Title,
            Body: notification.Body,
            BurstCount: 1,
            FirstReceivedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc,
            ExpiresAtUtc: expiresAt));
    }

    private bool IsAppAllowed(string appUserModelId, string appDisplayName)
    {
        foreach (string token in _allowedAppTokens)
        {
            if ((!string.IsNullOrEmpty(appUserModelId) && appUserModelId.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(appDisplayName) && appDisplayName.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private void RememberSeenId(uint id)
    {
        if (_seenIds.Add(id))
        {
            _seenIdsOrder.Enqueue(id);
            while (_seenIdsOrder.Count > MaxRememberedSeenIds)
            {
                uint oldest = _seenIdsOrder.Dequeue();
                _seenIds.Remove(oldest);
            }
        }
    }

    private static DisplayedNotificationView ToDisplayedView(QueuedNotificationEntry entry, IslandPolicyMode policyMode)
    {
        bool redact = policyMode == IslandPolicyMode.Private;
        string header;
        string body;

        if (redact)
        {
            header = entry.BurstCount > 1
                ? $"{entry.AppDisplayName} ({entry.BurstCount} avisos nuevos)"
                : entry.AppDisplayName;
            body = "Contenido oculto por modo privado";
        }
        else if (entry.BurstCount > 1)
        {
            header = $"{entry.AppDisplayName} ({entry.BurstCount} mensajes)";
            body = string.IsNullOrWhiteSpace(entry.Title)
                ? entry.Body
                : $"{entry.Title}: {entry.Body}";
        }
        else
        {
            header = string.IsNullOrWhiteSpace(entry.Title) ? entry.AppDisplayName : entry.Title;
            body = entry.Body;
        }

        return new DisplayedNotificationView(
            LatestNotificationId: entry.LatestNotificationId,
            AppUserModelId: entry.AppUserModelId,
            AppDisplayName: entry.AppDisplayName,
            HeaderText: header,
            BodyText: body,
            BurstCount: entry.BurstCount,
            IsRedactedByPolicy: redact,
            FirstReceivedAtUtc: entry.FirstReceivedAtUtc,
            UpdatedAtUtc: entry.UpdatedAtUtc,
            ExpiresAtUtc: entry.ExpiresAtUtc);
    }

    private sealed record QueuedNotificationEntry(
        uint LatestNotificationId,
        string AppUserModelId,
        string AppDisplayName,
        string Title,
        string Body,
        int BurstCount,
        DateTimeOffset FirstReceivedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset ExpiresAtUtc);
}
