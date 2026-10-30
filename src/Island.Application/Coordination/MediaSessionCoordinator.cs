using Island.Core.Contracts;
using Island.Core.Domain;

namespace Island.Application.Coordination;

public sealed record MediaCoordinationEvaluation(
    DisplayedMediaView? ActiveMedia,
    bool ShouldTriggerTrackChangeExpansion,
    bool ActivePlayerDisconnected,
    string ReasonCode);

/// <summary>
/// Coordina la selección entre múltiples reproductores multimedia (automática, manual y fijada),
/// la detección de cambio de pista sin duplicados y la reconexión al cerrarse o reiniciarse un reproductor (Secciones 6 y 8).
/// </summary>
public sealed class MediaSessionCoordinator
{
    private string? _selectedSourceId;
    private string? _pinnedSourceId;
    private string? _lastAnnouncedTrackKey;
    private bool _hasBaselineSnapshot;

    public string? SelectedSourceId => _selectedSourceId;
    public string? PinnedSourceId => _pinnedSourceId;

    public void SetPinnedSource(string? sourceAppUserModelId)
    {
        _pinnedSourceId = string.IsNullOrWhiteSpace(sourceAppUserModelId) ? null : sourceAppUserModelId;
        if (_pinnedSourceId is not null)
        {
            _selectedSourceId = _pinnedSourceId;
        }
    }

    public void SelectManualSource(string? sourceAppUserModelId)
    {
        _selectedSourceId = string.IsNullOrWhiteSpace(sourceAppUserModelId) ? null : sourceAppUserModelId;
    }

    public MediaCoordinationEvaluation EvaluateSessions(
        IReadOnlyList<MediaSessionSnapshot> sessions,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (sessions.Count == 0)
        {
            bool hadActive = _selectedSourceId is not null;
            _selectedSourceId = _pinnedSourceId; // Si hay uno fijado, se conserva la preferencia esperando reconexión
            _lastAnnouncedTrackKey = null;
            _hasBaselineSnapshot = true;

            return new MediaCoordinationEvaluation(
                ActiveMedia: null,
                ShouldTriggerTrackChangeExpansion: false,
                ActivePlayerDisconnected: hadActive,
                ReasonCode: hadActive ? "ALL_PLAYERS_DISCONNECTED" : "NO_SESSIONS");
        }

        var chosen = SelectBestSession(sessions);
        if (chosen is null)
        {
            return new MediaCoordinationEvaluation(null, false, false, "NO_MATCHING_SESSION");
        }

        bool wasFirstSnapshot = !_hasBaselineSnapshot;
        _hasBaselineSnapshot = true;
        _selectedSourceId = chosen.SourceAppUserModelId;

        bool isPinned = _pinnedSourceId is not null &&
                        string.Equals(chosen.SourceAppUserModelId, _pinnedSourceId, StringComparison.OrdinalIgnoreCase);

        var view = new DisplayedMediaView(
            SourceAppUserModelId: chosen.SourceAppUserModelId,
            ProviderKind: chosen.ProviderKind,
            Title: chosen.Title,
            Artist: chosen.Artist,
            AlbumTitle: chosen.AlbumTitle,
            PlaybackState: chosen.PlaybackState,
            Capabilities: chosen.Capabilities,
            Timeline: chosen.Timeline,
            HasThumbnail: chosen.HasThumbnailStream,
            IsPinnedByUser: isPinned,
            UpdatedAtUtc: nowUtc);

        string currentTrackKey = BuildTrackIdentityKey(chosen);
        bool hasValidTitle = !string.IsNullOrWhiteSpace(chosen.Title);

        if (wasFirstSnapshot)
        {
            if (hasValidTitle)
            {
                _lastAnnouncedTrackKey = currentTrackKey;
            }

            return new MediaCoordinationEvaluation(
                ActiveMedia: view,
                ShouldTriggerTrackChangeExpansion: false,
                ActivePlayerDisconnected: false,
                ReasonCode: "INITIAL_BASELINE_SYNCED");
        }

        bool trackChanged = hasValidTitle &&
                            chosen.PlaybackState == MediaPlaybackState.Playing &&
                            !string.Equals(_lastAnnouncedTrackKey, currentTrackKey, StringComparison.Ordinal);

        if (hasValidTitle && (chosen.PlaybackState == MediaPlaybackState.Playing || _lastAnnouncedTrackKey is null))
        {
            _lastAnnouncedTrackKey = currentTrackKey;
        }

        return new MediaCoordinationEvaluation(
            ActiveMedia: view,
            ShouldTriggerTrackChangeExpansion: trackChanged,
            ActivePlayerDisconnected: false,
            ReasonCode: trackChanged ? "TRACK_CHANGED_PLAYING" : "SESSION_UPDATED");
    }

    private MediaSessionSnapshot? SelectBestSession(IReadOnlyList<MediaSessionSnapshot> sessions)
    {
        // 1. Si el usuario fijó un reproductor y su sesión está presente, tiene prioridad absoluta.
        if (_pinnedSourceId is not null)
        {
            var pinnedMatch = sessions.FirstOrDefault(s =>
                string.Equals(s.SourceAppUserModelId, _pinnedSourceId, StringComparison.OrdinalIgnoreCase));
            if (pinnedMatch is not null)
            {
                return pinnedMatch;
            }
        }

        // 2. Si hay sesiones reproduciendo actualmente (Playing), priorizar reproductores dedicados (Spotify/AppleMusic)
        // o mantener la sesión seleccionada si ya está en Playing.
        var playingSessions = sessions.Where(s => s.PlaybackState == MediaPlaybackState.Playing).ToList();
        if (playingSessions.Count > 0)
        {
            if (_selectedSourceId is not null)
            {
                var currentStillPlaying = playingSessions.FirstOrDefault(s =>
                    string.Equals(s.SourceAppUserModelId, _selectedSourceId, StringComparison.OrdinalIgnoreCase));

                // Si la sesión actual sigue en Playing y no apareció un reproductor dedicado de mayor prioridad en Playing, la conservamos.
                if (currentStillPlaying is not null)
                {
                    int currentPriority = GetProviderPriority(currentStillPlaying.ProviderKind);
                    bool higherDedicatedStarted = playingSessions.Any(s => GetProviderPriority(s.ProviderKind) > currentPriority);
                    if (!higherDedicatedStarted)
                    {
                        return currentStillPlaying;
                    }
                }
            }

            return playingSessions
                .OrderByDescending(s => GetProviderPriority(s.ProviderKind))
                .ThenByDescending(s => s.IsCurrentSystemSession)
                .First();
        }

        // 3. Si ninguna está en Playing, mantener la seleccionada manualmente/previamente si sigue abierta.
        if (_selectedSourceId is not null)
        {
            var previousMatch = sessions.FirstOrDefault(s =>
                string.Equals(s.SourceAppUserModelId, _selectedSourceId, StringComparison.OrdinalIgnoreCase));
            if (previousMatch is not null)
            {
                return previousMatch;
            }
        }

        // 4. Preferir reproductores dedicados (Spotify/AppleMusic) sobre pestañas de navegador en pausa.
        return sessions
            .OrderByDescending(s => GetProviderPriority(s.ProviderKind))
            .ThenByDescending(s => s.IsCurrentSystemSession)
            .First();
    }

    private static int GetProviderPriority(MediaProviderKind kind) =>
        kind switch
        {
            MediaProviderKind.Spotify => 30,
            MediaProviderKind.AppleMusic => 30,
            MediaProviderKind.Other => 20,
            MediaProviderKind.Browser => 10,
            _ => 0
        };

    private static string BuildTrackIdentityKey(MediaSessionSnapshot session) =>
        $"{session.SourceAppUserModelId.Trim().ToUpperInvariant()}|{session.Title.Trim()}|{session.Artist.Trim()}";
}
