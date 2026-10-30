namespace Island.Core.Contracts;

public sealed record ScreenRectPx(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int px, int py) =>
        px >= X && px < Right && py >= Y && py < Bottom;
}

/// <summary>
/// Geometría y factor de escala DPI de un monitor físico conectado (Sección 7).
/// </summary>
public sealed record MonitorDisplayDescriptor(
    string DeviceName,
    bool IsPrimary,
    ScreenRectPx BoundsPx,
    ScreenRectPx WorkAreaPx,
    uint DpiX,
    uint DpiY,
    int ScalePercent);

/// <summary>
/// Estado de ocupación / pantalla completa del escritorio (SHQueryUserNotificationState + ventana activa).
/// </summary>
public sealed record FullScreenStateSnapshot(
    int QueryUserNotificationStateCode,
    string NotificationStateName,
    bool IsBusyOrFullScreenD3D,
    bool IsPresentationMode,
    bool ShouldPauseAutomaticExpansions,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Evidencia objetiva del prototipo de ventana flotante, transparencia, paso de clics (click-through),
/// preservación de foco y materiales en Windows 11 (Secciones 4 y 7).
/// </summary>
public sealed record WindowViabilityProbeResult(
    IntegrationCapabilityReport CapabilityReport,
    bool WindowCreated,
    bool IsTopMostApplied,
    bool IsToolWindowStyleApplied,
    bool IsNoActivateStyleApplied,
    bool ExteriorTransparentClickThroughPassed,
    bool InteriorIslandHitTestPassed,
    bool PassiveShowPreservedForegroundFocus,
    bool DesktopAcrylicControllerSupported,
    bool MicaControllerSupported,
    bool SystemBackdropElementTypePresent,
    string BackdropVerificationNotes,
    IReadOnlyList<MonitorDisplayDescriptor> Monitors,
    FullScreenStateSnapshot FullScreenState,
    DateTimeOffset ProbedAtUtc);
