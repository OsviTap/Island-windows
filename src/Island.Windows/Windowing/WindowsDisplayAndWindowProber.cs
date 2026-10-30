using System.Runtime.InteropServices;
using Island.Core.Contracts;
using Island.Core.Diagnostics;

namespace Island.Windows.Windowing;

/// <summary>
/// Verifica en tiempo de ejecución la topología de monitores/DPI, el estado de pantalla completa
/// (SHQueryUserNotificationState) y la viabilidad real de la ventana flotante (TopMost, ToolWindow,
/// NoActivate, paso de clics transparente al exterior mediante región recortada e hit-test interior).
/// </summary>
public sealed class WindowsDisplayAndWindowProber
{
    public const int GwlExStyle = -20;
    public const nint WsExTopMost = 0x00000008;
    public const nint WsExToolWindow = 0x00000080;
    public const nint WsExLayered = 0x00080000;
    public const nint WsExNoActivate = 0x08000000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopMost = -1;

    private const int SwShowNoActivate = 4;
    private const int MdtEffectiveDpi = 0;

    private readonly IDiagnosticLogger _logger;
    private readonly TimeProvider _timeProvider;

    public WindowsDisplayAndWindowProber(IDiagnosticLogger logger, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<MonitorDisplayDescriptor> CaptureMonitors()
    {
        var monitors = new List<MonitorDisplayDescriptor>();
        MonitorEnumProc callback = (nint hMonitor, nint hdc, ref Rect lprcMonitor, nint dwData) =>
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                uint dpiX = 96;
                uint dpiY = 96;
                try
                {
                    if (GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out uint dx, out uint dy) == 0 && dx > 0)
                    {
                        dpiX = dx;
                        dpiY = dy;
                    }
                }
                catch (Exception)
                {
                }

                int scalePercent = (int)Math.Round(dpiX * 100.0 / 96.0);
                monitors.Add(new MonitorDisplayDescriptor(
                    DeviceName: info.szDevice ?? string.Empty,
                    IsPrimary: (info.dwFlags & 1) != 0,
                    BoundsPx: new ScreenRectPx(
                        info.rcMonitor.Left,
                        info.rcMonitor.Top,
                        info.rcMonitor.Right - info.rcMonitor.Left,
                        info.rcMonitor.Bottom - info.rcMonitor.Top),
                    WorkAreaPx: new ScreenRectPx(
                        info.rcWork.Left,
                        info.rcWork.Top,
                        info.rcWork.Right - info.rcWork.Left,
                        info.rcWork.Bottom - info.rcWork.Top),
                    DpiX: dpiX,
                    DpiY: dpiY,
                    ScalePercent: scalePercent));
            }
            return true;
        };

        EnumDisplayMonitors(0, 0, callback, 0);
        return monitors;
    }

    public FullScreenStateSnapshot CaptureFullScreenState()
    {
        var now = _timeProvider.GetUtcNow();
        int stateCode = 5; // QUNS_ACCEPTS_NOTIFICATIONS por defecto
        try
        {
            if (SHQueryUserNotificationState(out int quns) == 0)
            {
                stateCode = quns;
            }
        }
        catch (Exception)
        {
        }

        string stateName = stateCode switch
        {
            1 => "QUNS_NOT_PRESENT",
            2 => "QUNS_BUSY",
            3 => "QUNS_RUNNING_D3D_FULL_SCREEN",
            4 => "QUNS_PRESENTATION_MODE",
            5 => "QUNS_ACCEPTS_NOTIFICATIONS",
            6 => "QUNS_QUIET_TIME",
            7 => "QUNS_APP",
            _ => $"UNKNOWN_{stateCode}"
        };

        bool isFullScreenD3DOrApp = stateCode is 3 or 7;
        bool isPresentation = stateCode == 4;
        bool isBusy = stateCode == 2;

        // NOTA TÉCNICA (Wallpaper Engine / Fondos Direct3D):
        // Aplicaciones como Wallpaper Engine mantienen una superficie Direct3D activa en el fondo del escritorio,
        // provocando que Windows Shell devuelva QUNS_BUSY (2) de forma permanente durante el uso normal del escritorio.
        // Por ello, IsBusyOrFullScreenD3D conserva el indicador diagnóstico informativo (2, 3 o 7), pero
        // ShouldPauseAutomaticExpansions solo pausa expansiones automáticas ante pantalla completa real D3D (3),
        // modo presentación (4) o aplicación exclusiva a pantalla completa (7).
        return new FullScreenStateSnapshot(
            QueryUserNotificationStateCode: stateCode,
            NotificationStateName: stateName,
            IsBusyOrFullScreenD3D: isBusy || isFullScreenD3DOrApp,
            IsPresentationMode: isPresentation,
            ShouldPauseAutomaticExpansions: isFullScreenD3DOrApp || isPresentation,
            CapturedAtUtc: now);
    }

    /// <summary>
    /// Configura una ventana nativa (HWND) como isla flotante sin robar foco y recorta su región
    /// interactiva únicamente a la píldora visible (pillRectRelativePx), dejando el exterior
    /// 100% transparente al paso de clics (click-through) hacia otras aplicaciones.
    /// </summary>
    public static void ApplyIslandWindowRegionAndStyles(
        nint islandHwnd,
        ScreenRectPx windowScreenBoundsPx,
        ScreenRectPx pillRelativeBoundsPx,
        int cornerRadiusPx,
        bool passiveNoActivate = true)
    {
        nint currentExStyle = GetWindowLongPtrW(islandHwnd, GwlExStyle);
        nint desiredExStyle = currentExStyle | WsExTopMost | WsExToolWindow;
        if (passiveNoActivate)
        {
            desiredExStyle |= WsExNoActivate;
        }
        else
        {
            desiredExStyle &= ~WsExNoActivate;
        }

        SetWindowLongPtrW(islandHwnd, GwlExStyle, desiredExStyle);

        // Aplicamos redondeo con anti-aliasing nativo del compositor DWM de Windows 11 (DWMWCP_ROUND = 2)
        // y desactivamos el borde gris del marco de DWM (DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE = 0xFFFFFFFE)
        // para eliminar por completo los dientes de sierra y halos blancos en las esquinas.
        ApplySmoothDwmCornerAndBorderlessFrame(islandHwnd);

        // Posicionamos y dimensionamos el HWND primero para que WinUI 3 (DesktopChildSiteBridge)
        // sincronice su superficie de composición.
        SetWindowPos(
            islandHwnd,
            HwndTopMost,
            windowScreenBoundsPx.X,
            windowScreenBoundsPx.Y,
            windowScreenBoundsPx.Width,
            windowScreenBoundsPx.Height,
            SwpNoActivate | SwpShowWindow);

        // Si la píldora ocupa el 100% del HWND (modo de ventana ajustada 1:1 en vivo), limpiamos cualquier
        // región GDI binaria (SetWindowRgn = 0) para que DWM aplique sus esquinas redondeadas suaves con Acrylic/Mica.
        // Si la píldora es un sub-rectángulo dentro de un contenedor mayor (ej. en pruebas de click-through exterior),
        // recortamos con CreateRoundRectRgn.
        bool isFullWindowPill =
            pillRelativeBoundsPx.X == 0 &&
            pillRelativeBoundsPx.Y == 0 &&
            pillRelativeBoundsPx.Width == windowScreenBoundsPx.Width &&
            pillRelativeBoundsPx.Height == windowScreenBoundsPx.Height &&
            pillRelativeBoundsPx.Width > 0 &&
            pillRelativeBoundsPx.Height > 0;

        if (isFullWindowPill)
        {
            SetWindowRgn(islandHwnd, 0, bRedraw: true);
        }
        else
        {
            nint hRgn = CreateRoundRectRgn(
                pillRelativeBoundsPx.X,
                pillRelativeBoundsPx.Y,
                pillRelativeBoundsPx.Right,
                pillRelativeBoundsPx.Bottom,
                cornerRadiusPx * 2,
                cornerRadiusPx * 2);

            if (hRgn != 0)
            {
                if (SetWindowRgn(islandHwnd, hRgn, bRedraw: true) == 0)
                {
                    DeleteObject(hRgn);
                }
            }
        }
    }

    public static void ApplySmoothDwmCornerAndBorderlessFrame(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        try
        {
            // DWMWA_WINDOW_CORNER_PREFERENCE (33) = DWMWCP_ROUND (2)
            int cornerPreference = 2;
            DwmSetWindowAttribute(hwnd, 33, ref cornerPreference, sizeof(int));

            // DWMWA_BORDER_COLOR (34) = DWMWA_COLOR_NONE (0xFFFFFFFE) para ocultar el marco externo de DWM
            uint borderColorNone = 0xFFFFFFFEu;
            DwmSetWindowAttributeUInt(hwnd, 34, ref borderColorNone, sizeof(uint));
        }
        catch (Exception)
        {
        }
    }

    public static bool IsHwndVisible(nint hwnd) => hwnd != 0 && IsWindowVisible(hwnd);

    public static ScreenRectPx GetHwndScreenRect(nint hwnd)
    {
        if (hwnd != 0 && GetWindowRect(hwnd, out var rc))
        {
            return new ScreenRectPx(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
        }

        return new ScreenRectPx(0, 0, 0, 0);
    }

    public static bool IsCursorInsideHwnd(nint hwnd, int paddingPx = 0)
    {
        if (hwnd == 0 || !GetCursorPos(out var pt) || !GetWindowRect(hwnd, out var rc))
        {
            return false;
        }

        return pt.X >= rc.Left - paddingPx &&
               pt.X <= rc.Right + paddingPx &&
               pt.Y >= rc.Top - paddingPx &&
               pt.Y <= rc.Bottom + paddingPx;
    }

    public static bool IsLeftMouseButtonPressed() =>
        (GetAsyncKeyState(0x01) & 0x8000) != 0;

    /// <summary>
    /// Ejecuta una prueba empírica real sobre una ventana de isla WinUI/Win32 (islandHwnd) creando
    /// una ventana testigo detrás y comprobando con WindowFromPoint y GetForegroundWindow:
    /// 1. Que mostrar/posicionar la isla en modo pasivo no roba el foco.
    /// 2. Que un punto en la zona exterior transparente del contenedor golpea la ventana testigo inferior (click-through).
    /// 3. Que un punto dentro de la píldora visible golpea la ventana de la isla (interactiva).
    /// </summary>
    public WindowViabilityProbeResult ProbeWindowAndBackdropViability(
        nint islandHwnd,
        bool desktopAcrylicSupported,
        bool micaSupported,
        bool systemBackdropElementTypePresent,
        string backdropNotes)
    {
        var now = _timeProvider.GetUtcNow();
        var monitors = CaptureMonitors();
        var fullScreenState = CaptureFullScreenState();

        if (islandHwnd == 0)
        {
            var errReport = new IntegrationCapabilityReport(
                IntegrationModuleId.Windowing,
                IntegrationCapabilityState.Faulted,
                "INVALID_HWND",
                "El HWND proporcionado es 0.",
                [],
                now);

            return new WindowViabilityProbeResult(
                errReport, false, false, false, false, false, false, false,
                desktopAcrylicSupported, micaSupported, systemBackdropElementTypePresent,
                backdropNotes, monitors, fullScreenState, now);
        }

        // 1. Creamos una ventana testigo ("STATIC") con WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        // inmediatamente debajo de nuestra isla en el orden Z TopMost para probar el paso de clics sin interferencia de otras ventanas.
        var containerRect = new ScreenRectPx(200, 120, 500, 240);
        // La píldora visible ocupa solo el centro superior del contenedor: X=150..350 (ancho 200), Y=10..50 (alto 40)
        var pillRelativeRect = new ScreenRectPx(150, 10, 200, 40);

        nint backgroundTargetHwnd = CreateWindowExW(
            0x00000008u | 0x00000080u | 0x08000000u, // WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            "STATIC",
            "IslandClickThroughTarget",
            0x80000000u | 0x10000000u | 0x00000100u, // WS_POPUP | WS_VISIBLE | SS_NOTIFY (para que StaticWndProc no devuelva HTTRANSPARENT)
            containerRect.X,
            containerRect.Y,
            containerRect.Width,
            containerRect.Height,
            0,
            0,
            0,
            0);

        try
        {
            if (backgroundTargetHwnd != 0)
            {
                SetWindowPos(
                    backgroundTargetHwnd,
                    HwndTopMost,
                    containerRect.X,
                    containerRect.Y,
                    containerRect.Width,
                    containerRect.Height,
                    SwpNoActivate | SwpShowWindow);
            }

            nint foregroundBefore = GetForegroundWindow();

            ApplyIslandWindowRegionAndStyles(
                islandHwnd,
                containerRect,
                pillRelativeRect,
                cornerRadiusPx: 18,
                passiveNoActivate: true);

            ShowWindow(islandHwnd, SwShowNoActivate);

            nint foregroundAfter = GetForegroundWindow();
            bool focusPreserved = foregroundAfter != islandHwnd &&
                                  !IsChild(islandHwnd, foregroundAfter) &&
                                  (foregroundBefore == foregroundAfter || foregroundBefore == 0);

            nint exStyleAfter = GetWindowLongPtrW(islandHwnd, GwlExStyle);
            bool isTopMost = (exStyleAfter & WsExTopMost) != 0;
            bool isToolWindow = (exStyleAfter & WsExToolWindow) != 0;
            bool isNoActivate = (exStyleAfter & WsExNoActivate) != 0;

            // Punto exterior dentro del rectángulo (200..700, 120..360) pero fuera de la píldora (X=220, Y=200)
            var exteriorPt = new PointPx { X = containerRect.X + 20, Y = containerRect.Y + 80 };
            nint hitExteriorHwnd = WindowFromPoint(exteriorPt);
            string extClass = GetWindowClassNameSafe(hitExteriorHwnd);

            // Punto interior en el centro exacto de la píldora (X=200+250=450, Y=120+30=150)
            var interiorPt = new PointPx { X = containerRect.X + pillRelativeRect.X + (pillRelativeRect.Width / 2), Y = containerRect.Y + pillRelativeRect.Y + (pillRelativeRect.Height / 2) };
            nint hitInteriorHwnd = WindowFromPoint(interiorPt);
            string intClass = GetWindowClassNameSafe(hitInteriorHwnd);

            // El paso de clics es válido si el punto exterior (dentro del HWND de la isla pero fuera de la píldora)
            // NO golpea la isla ni a ninguno de sus hijos WinUI (DesktopChildSiteBridge).
            bool clickThroughPassed = hitExteriorHwnd != 0 &&
                                      hitExteriorHwnd != islandHwnd &&
                                      !IsChild(islandHwnd, hitExteriorHwnd);

            // El hit-test interior es válido si el punto dentro de la píldora golpea nuestra ventana de isla (o un hijo suyo).
            bool interiorHitPassed = hitInteriorHwnd == islandHwnd || IsChild(islandHwnd, hitInteriorHwnd);

            bool allOk = isTopMost && isToolWindow && isNoActivate && clickThroughPassed && interiorHitPassed && focusPreserved;
            var capState = allOk ? IntegrationCapabilityState.Available : IntegrationCapabilityState.PartiallySupported;

            var report = new IntegrationCapabilityReport(
                IntegrationModuleId.Windowing,
                capState,
                allOk ? "WINDOW_VIABILITY_VERIFIED" : "WINDOW_VIABILITY_PARTIAL",
                $"TopMost={isTopMost}, ToolWindow={isToolWindow}, NoActivate={isNoActivate}, ClickThrough={clickThroughPassed} (ExtClass={extClass}), InteriorHit={interiorHitPassed} (IntClass={intClass}), FocusPreserved={focusPreserved}, Monitors={monitors.Count}",
                ["TopMost", "ToolWindow", "NoActivate", "RegionClickThrough", "PerMonitorDpi", "FullScreenDetection"],
                now);

            _logger.Log(
                DiagnosticLogLevel.Info,
                "Windowing",
                "WINDOW_VIABILITY_PROBE_COMPLETED",
                report.DiagnosticDetail);

            return new WindowViabilityProbeResult(
                CapabilityReport: report,
                WindowCreated: true,
                IsTopMostApplied: isTopMost,
                IsToolWindowStyleApplied: isToolWindow,
                IsNoActivateStyleApplied: isNoActivate,
                ExteriorTransparentClickThroughPassed: clickThroughPassed,
                InteriorIslandHitTestPassed: interiorHitPassed,
                PassiveShowPreservedForegroundFocus: focusPreserved,
                DesktopAcrylicControllerSupported: desktopAcrylicSupported,
                MicaControllerSupported: micaSupported,
                SystemBackdropElementTypePresent: systemBackdropElementTypePresent,
                BackdropVerificationNotes: backdropNotes,
                Monitors: monitors,
                FullScreenState: fullScreenState,
                ProbedAtUtc: now);
        }
        finally
        {
            if (backgroundTargetHwnd != 0)
            {
                DestroyWindow(backgroundTargetHwnd);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointPx
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref Rect lprcMonitor, nint dwData);

    private static string GetWindowClassNameSafe(nint hWnd)
    {
        if (hWnd == 0)
        {
            return "NULL";
        }

        char[] buf = new char[128];
        int len = GetClassNameW(hWnd, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len) : "UNKNOWN";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHQueryUserNotificationState(out int pquns);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(PointPx Point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetCursorPos(out PointPx lpPoint);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsChild(nint hWndParent, nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteObject(nint ho);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute", ExactSpelling = true)]
    private static extern int DwmSetWindowAttributeUInt(nint hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
}
