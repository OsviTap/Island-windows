using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Island.Application.Configuration;
using Island.Application.Coordination;
using Island.Application.Theming;
using Island.Core.Configuration;
using Island.Core.Contracts;
using Island.Core.Domain;
using Island.Core.Theming;
using Microsoft.UI.Dispatching;

namespace Island.App.ViewModels;

/// <summary>
/// ViewModel principal de la Isla Flotante (Fase 4: Interfaz y temas).
/// Utiliza propiedades parciales con [ObservableProperty] de CommunityToolkit.Mvvm 8.4.2
/// para mantener 0 advertencias AOT/CsWinRT (MVVMTK0045).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IslandStructuralCoordinator _coordinator;
    private readonly ISystemEnvironmentInspector _environmentInspector;
    private readonly JsonSettingsStore _settingsStore;
    private readonly JsonThemeCatalog _themeCatalog;
    private readonly IMediaSessionProvider? _mediaProvider;
    private readonly IAudioEndpointInspector? _audioInspector;

    private AppSettingsDocument _currentSettings;
    private IReadOnlyList<MediaSessionSnapshot> _lastKnownSessions = [];
    private DispatcherQueue? _dispatcherQueue;

    public event EventHandler? LayoutOrThemeUpdated;

    [ObservableProperty]
    public partial string VisibilityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InteractionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ContentText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PolicyText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RequestedMaterialText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EffectiveMaterialText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeaderMaterialLabel { get; set; } = "Isla Glass";

    [ObservableProperty]
    public partial string PolicyLabelText { get; set; } = "Normal";

    [ObservableProperty]
    public partial string PlayPauseGlyph { get; set; } = "\uE768";

    [ObservableProperty]
    public partial string VolumeGlyph { get; set; } = "\uE767";

    [ObservableProperty]
    public partial string PinGlyph { get; set; } = "\uE718";

    [ObservableProperty]
    public partial bool HasPendingNotification { get; set; }

    [ObservableProperty]
    public partial string PackageIdentityStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCompactMode { get; set; }

    [ObservableProperty]
    public partial bool IsMiniClosedMode { get; set; } = true;

    [ObservableProperty]
    public partial bool IsHoverPreviewMode { get; set; }

    [ObservableProperty]
    public partial bool IsExpandedMode { get; set; }

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    [ObservableProperty]
    public partial string PinButtonText { get; set; } = "Anclar";

    [ObservableProperty]
    public partial double PillWidthDip { get; set; } = 210.0;

    [ObservableProperty]
    public partial double PillHeightDip { get; set; } = 38.0;

    [ObservableProperty]
    public partial double PillCornerRadiusDip { get; set; } = 18.0;

    [ObservableProperty]
    public partial string ActiveBackgroundHex { get; set; } = "#FF18181B";

    [ObservableProperty]
    public partial string ActiveForegroundHex { get; set; } = "#FFF4F4F5";

    [ObservableProperty]
    public partial string ActiveBorderHex { get; set; } = "#FF3F3F46";

    [ObservableProperty]
    public partial string ActiveAccentHex { get; set; } = "#FF38BDF8";

    [ObservableProperty]
    public partial string CompactHeadlineText { get; set; } = "Isla lista";

    [ObservableProperty]
    public partial string CompactBadgeText { get; set; } = "IDLE";

    [ObservableProperty]
    public partial string MediaTitleText { get; set; } = "Sin reproducci\u00F3n activa";

    [ObservableProperty]
    public partial string MediaArtistText { get; set; } = "Abre Spotify o Apple Music";

    [ObservableProperty]
    public partial string MediaSourceBadgeText { get; set; } = "GSMTC";

    [ObservableProperty]
    public partial string PlayPauseButtonText { get; set; } = "Reproducir";

    [ObservableProperty]
    public partial string MasterVolumeText { get; set; } = "Vol: --%";

    [ObservableProperty]
    public partial string MuteButtonText { get; set; } = "Silenciar";

    [ObservableProperty]
    public partial string NotificationHeadlineText { get; set; } = "Sin avisos pendientes";

    [ObservableProperty]
    public partial string NotificationBodyText { get; set; } = "Filtro activo: WhatsApp";

    [ObservableProperty]
    public partial string NotificationBurstBadgeText { get; set; } = string.Empty;

    public AppSettingsDocument CurrentSettings => _currentSettings;
    public string? PreferredMonitorDeviceName { get; private set; }

    public MainViewModel(
        IslandStructuralCoordinator coordinator,
        ISystemEnvironmentInspector environmentInspector,
        PackageIdentitySnapshot packageIdentity,
        JsonSettingsStore settingsStore,
        JsonThemeCatalog themeCatalog,
        AppSettingsDocument initialSettings,
        IMediaSessionProvider? mediaProvider = null,
        IAudioEndpointInspector? audioInspector = null)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _environmentInspector = environmentInspector ?? throw new ArgumentNullException(nameof(environmentInspector));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _themeCatalog = themeCatalog ?? throw new ArgumentNullException(nameof(themeCatalog));
        _currentSettings = initialSettings ?? AppSettingsDocument.CreateDefault();
        _mediaProvider = mediaProvider;
        _audioInspector = audioInspector;

        PackageIdentityStatusText = packageIdentity.HasPackageIdentity
            ? $"MSIX: {packageIdentity.PackageFullName}"
            : $"Modo desarrollo sin MSIX (Win32 {packageIdentity.Win32ErrorCode})";

        _coordinator.StateChanged += (_, snapshot) => RunOnUiThread(() => SyncState(snapshot));
        _coordinator.ThemeChanged += (_, themeState) => RunOnUiThread(() => SyncTheme(themeState));

        SyncTheme(_coordinator.CurrentThemeState);
        SyncState(_coordinator.CurrentState);
        RefreshAudioStatus();
    }

    public void AttachDispatcherQueue(DispatcherQueue? dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    public void UpdateMediaSessionsSnapshot(IReadOnlyList<MediaSessionSnapshot> sessions)
    {
        _lastKnownSessions = sessions ?? [];
        _coordinator.ProcessMediaSessions(_lastKnownSessions);
        SyncState(_coordinator.CurrentState);
    }

    public void RefreshAudioStatus()
    {
        if (_audioInspector is null)
        {
            return;
        }

        var snapshot = _audioInspector.CaptureAudioSnapshot();
        if (!snapshot.HasDefaultRenderDevice)
        {
            MasterVolumeText = "Sin salida";
            MuteButtonText = "N/D";
            return;
        }

        int pct = (int)Math.Round(snapshot.MasterVolumeScalar * 100.0f);
        MasterVolumeText = snapshot.IsMasterMuted ? $"Mudo ({pct}%)" : $"{pct}%";
        MuteButtonText = snapshot.IsMasterMuted ? "Activar audio" : "Silenciar";
        VolumeGlyph = snapshot.IsMasterMuted ? "\uE74F" : "\uE767";
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        var current = _coordinator.CurrentState;
        if (current.Visibility == IslandVisibility.Expanded)
        {
            IsHoverPreviewMode = false;
            _coordinator.SetUserInteraction(IslandInteractionState.Passive);
        }
        else
        {
            _coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse);
        }
    }

    public void SetCursorHover(bool isCursorInside)
    {
        if (IsHoverPreviewMode == isCursorInside)
        {
            return;
        }

        IsHoverPreviewMode = isCursorInside;
        SyncState(_coordinator.CurrentState);
    }

    public void CollapseFromOutsideClick()
    {
        var current = _coordinator.CurrentState;
        if (current.Visibility == IslandVisibility.Expanded && current.Interaction != IslandInteractionState.PinnedOpen)
        {
            IsHoverPreviewMode = false;
            _coordinator.SetUserInteraction(IslandInteractionState.Passive);
        }
        else if (IsHoverPreviewMode)
        {
            IsHoverPreviewMode = false;
            SyncState(_coordinator.CurrentState);
        }
    }

    [RelayCommand]
    private void CollapseOnEscape()
    {
        IsHoverPreviewMode = false;
        var current = _coordinator.CurrentState;
        if (current.Interaction != IslandInteractionState.Passive || current.Visibility == IslandVisibility.Expanded)
        {
            _coordinator.SetUserInteraction(IslandInteractionState.Passive);
        }
        else
        {
            SyncState(_coordinator.CurrentState);
        }
    }

    [RelayCommand]
    private void TogglePin()
    {
        var nextInteraction = _coordinator.CurrentState.Interaction == IslandInteractionState.PinnedOpen
            ? IslandInteractionState.ActiveInUse
            : IslandInteractionState.PinnedOpen;
        _coordinator.SetUserInteraction(nextInteraction);
    }

    [RelayCommand]
    private void DismissNotification()
    {
        _coordinator.SetUserInteraction(IslandInteractionState.Passive);
    }

    [RelayCommand]
    private void CyclePolicy()
    {
        var nextPolicy = _coordinator.CurrentState.Policy switch
        {
            IslandPolicyMode.Normal => IslandPolicyMode.Private,
            IslandPolicyMode.Private => IslandPolicyMode.Paused,
            _ => IslandPolicyMode.Normal
        };

        _coordinator.SetPolicyMode(nextPolicy);
    }

    [RelayCommand]
    private void SwitchToSolidTheme() => ApplyAndPersistTheme(BuiltInThemes.DefaultSolid);

    [RelayCommand]
    private void SwitchToGlassTheme() => ApplyAndPersistTheme(BuiltInThemes.DefaultGlass);

    [RelayCommand]
    private void SwitchToMicaTheme() => ApplyAndPersistTheme(BuiltInThemes.DefaultMica);

    [RelayCommand]
    private void ResetPositionToPrimaryMonitor()
    {
        PreferredMonitorDeviceName = null;
        _currentSettings = _currentSettings with
        {
            AnchorHorizontalRatio = 0.5,
            AnchorTopOffsetDip = 12.0
        };
        TrySaveSettings();
        _coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse);
        LayoutOrThemeUpdated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task PlayPauseMediaAsync()
    {
        var media = _coordinator.ActiveMediaView;
        if (_mediaProvider is null || media is null)
        {
            return;
        }

        await _mediaProvider.TryExecuteCommandAsync(media.SourceAppUserModelId, MediaControlCommand.TogglePlayPause);
        var probe = await _mediaProvider.ProbeSessionsAsync();
        UpdateMediaSessionsSnapshot(probe.Sessions);
    }

    [RelayCommand]
    private async Task NextTrackAsync()
    {
        var media = _coordinator.ActiveMediaView;
        if (_mediaProvider is null || media is null)
        {
            return;
        }

        await _mediaProvider.TryExecuteCommandAsync(media.SourceAppUserModelId, MediaControlCommand.SkipNext);
        var probe = await _mediaProvider.ProbeSessionsAsync();
        UpdateMediaSessionsSnapshot(probe.Sessions);
    }

    [RelayCommand]
    private async Task PreviousTrackAsync()
    {
        var media = _coordinator.ActiveMediaView;
        if (_mediaProvider is null || media is null)
        {
            return;
        }

        await _mediaProvider.TryExecuteCommandAsync(media.SourceAppUserModelId, MediaControlCommand.SkipPrevious);
        var probe = await _mediaProvider.ProbeSessionsAsync();
        UpdateMediaSessionsSnapshot(probe.Sessions);
    }

    [RelayCommand]
    private void CycleMediaPlayer()
    {
        if (_lastKnownSessions.Count <= 1)
        {
            return;
        }

        string? currentId = _coordinator.ActiveMediaView?.SourceAppUserModelId;
        int currentIndex = -1;
        for (int i = 0; i < _lastKnownSessions.Count; i++)
        {
            if (string.Equals(_lastKnownSessions[i].SourceAppUserModelId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + 1) % _lastKnownSessions.Count;
        _coordinator.SetPinnedMediaSource(_lastKnownSessions[nextIndex].SourceAppUserModelId);
        _coordinator.ProcessMediaSessions(_lastKnownSessions);
        SyncState(_coordinator.CurrentState);
    }

    [RelayCommand]
    private void VolumeUp() => AdjustMasterVolume(+0.05f);

    [RelayCommand]
    private void VolumeDown() => AdjustMasterVolume(-0.05f);

    [RelayCommand]
    private void ToggleMute()
    {
        if (_audioInspector is null)
        {
            return;
        }

        var snapshot = _audioInspector.CaptureAudioSnapshot();
        if (snapshot.HasDefaultRenderDevice)
        {
            _audioInspector.TrySetMasterMute(!snapshot.IsMasterMuted);
            RefreshAudioStatus();
        }
    }

    private void AdjustMasterVolume(float delta)
    {
        if (_audioInspector is null)
        {
            return;
        }

        var snapshot = _audioInspector.CaptureAudioSnapshot();
        if (snapshot.HasDefaultRenderDevice)
        {
            float target = Math.Clamp(snapshot.MasterVolumeScalar + delta, 0f, 1f);
            _audioInspector.TrySetMasterVolume(target);
            RefreshAudioStatus();
        }
    }

    private void ApplyAndPersistTheme(ThemeDefinition theme)
    {
        var env = _environmentInspector.CaptureSnapshot();
        _coordinator.ApplyTheme(theme, env);
        _currentSettings = _currentSettings with
        {
            SelectedThemeId = theme.Id,
            RequestedMaterialOverride = theme.RequestedMaterial
        };
        TrySaveSettings();
    }

    private void TrySaveSettings()
    {
        try
        {
            _settingsStore.SaveAtomically(_currentSettings);
        }
        catch (Exception)
        {
        }
    }

    private void SyncState(IslandStateSnapshot state)
    {
        VisibilityText = state.Visibility.ToString();
        InteractionText = state.Interaction.ToString();
        ContentText = state.Content.ToString();
        PolicyText = state.Policy.ToString();
        PolicyLabelText = state.Policy switch
        {
            IslandPolicyMode.Normal => "Normal",
            IslandPolicyMode.Private => "Privado",
            IslandPolicyMode.Paused => "Pausado",
            _ => state.Policy.ToString()
        };

        IsExpandedMode = state.Visibility == IslandVisibility.Expanded;
        IsCompactMode = !IsExpandedMode && IsHoverPreviewMode;
        IsMiniClosedMode = !IsExpandedMode && !IsHoverPreviewMode;

        IsPinned = state.Interaction == IslandInteractionState.PinnedOpen;
        PinButtonText = IsPinned ? "Fijada" : "Fijar";
        PinGlyph = IsPinned ? "\uE77A" : "\uE718";

        var media = _coordinator.ActiveMediaView;
        if (media is not null)
        {
            MediaTitleText = media.Title;
            MediaArtistText = string.IsNullOrWhiteSpace(media.Artist) ? media.ProviderKind.ToString() : media.Artist;
            MediaSourceBadgeText = media.IsPinnedByUser ? $"{media.ProviderKind} \u2022 Fijo" : media.ProviderKind.ToString();
            PlayPauseButtonText = media.PlaybackState == MediaPlaybackState.Playing ? "Pausar" : "Reproducir";
            PlayPauseGlyph = media.PlaybackState == MediaPlaybackState.Playing ? "\uE769" : "\uE768";
        }
        else
        {
            MediaTitleText = "Sin reproducci\u00F3n activa";
            MediaArtistText = "Spotify o Apple Music";
            MediaSourceBadgeText = "Multimedia";
            PlayPauseButtonText = "Reproducir";
            PlayPauseGlyph = "\uE768";
        }

        var notification = _coordinator.ActiveNotificationView;
        if (notification is not null)
        {
            HasPendingNotification = true;
            NotificationHeadlineText = $"{notification.AppDisplayName} \u2022 {notification.HeaderText}";
            NotificationBodyText = notification.BodyText;
            NotificationBurstBadgeText = notification.BurstCount > 1 ? $"x{notification.BurstCount}" : string.Empty;
        }
        else
        {
            HasPendingNotification = false;
            NotificationHeadlineText = "WhatsApp \u2022 Sin avisos nuevos";
            NotificationBodyText = $"Modo {PolicyLabelText} \u2022 En cola: {_coordinator.PendingNotificationCount}";
            NotificationBurstBadgeText = string.Empty;
        }

        if (state.Content == IslandContentKind.NotificationToast && notification is not null)
        {
            CompactBadgeText = notification.BurstCount > 1
                ? $"{notification.AppDisplayName} x{notification.BurstCount}"
                : notification.AppDisplayName;
            CompactHeadlineText = $"{notification.HeaderText}: {notification.BodyText}";
        }
        else if (media is not null)
        {
            CompactBadgeText = media.PlaybackState == MediaPlaybackState.Playing ? "PLAY" : "PAUSA";
            CompactHeadlineText = string.IsNullOrWhiteSpace(media.Artist)
                ? media.Title
                : $"{media.Title} \u2022 {media.Artist}";
        }
        else
        {
            CompactBadgeText = state.Policy == IslandPolicyMode.Normal ? "ISLA" : PolicyLabelText.ToUpperInvariant();
            CompactHeadlineText = "Isla Din\u00E1mica activa";
        }

        UpdatePillDimensions(state.Visibility, _coordinator.CurrentThemeState.Theme);
        LayoutOrThemeUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void SyncTheme(AppliedThemeState themeState)
    {
        RequestedMaterialText = themeState.RequestedMaterial.ToString();
        EffectiveMaterialText = $"{themeState.EffectiveMaterial} ({themeState.StatusReasonCode})";
        HeaderMaterialLabel = themeState.EffectiveMaterial switch
        {
            EffectiveMaterialKind.DesktopAcrylic => "Isla \u2022 Glass",
            EffectiveMaterialKind.Mica => "Isla \u2022 Mica",
            EffectiveMaterialKind.HighContrastSolid => "Alto Contraste",
            _ => "Isla \u2022 S\u00F3lida"
        };

        var colors = themeState.Theme.Colors;
        if (themeState.IsHighContrastActive)
        {
            ActiveBackgroundHex = colors.HighContrastBackgroundHex;
            ActiveForegroundHex = colors.HighContrastForegroundHex;
            ActiveBorderHex = colors.HighContrastBorderHex;
            ActiveAccentHex = colors.HighContrastAccentHex;
        }
        else
        {
            ActiveBackgroundHex = colors.BackgroundHex;
            ActiveForegroundHex = colors.ForegroundHex;
            ActiveBorderHex = colors.BorderHex;
            ActiveAccentHex = colors.AccentHex;
        }

        PillCornerRadiusDip = themeState.Theme.CornerRadius;
        UpdatePillDimensions(_coordinator.CurrentState.Visibility, themeState.Theme);
        LayoutOrThemeUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePillDimensions(IslandVisibility visibility, ThemeDefinition theme)
    {
        if (visibility == IslandVisibility.Expanded)
        {
            PillWidthDip = theme.ExpandedWidth;
            PillHeightDip = theme.ExpandedHeight;
        }
        else if (IsHoverPreviewMode)
        {
            PillWidthDip = theme.CompactWidth;
            PillHeightDip = theme.CompactHeight;
        }
        else
        {
            // Nivel 1 (Cápsula cerrada mínima tipo Dynamic Island en reposo)
            PillWidthDip = 128.0;
            PillHeightDip = 28.0;
        }
    }
}
