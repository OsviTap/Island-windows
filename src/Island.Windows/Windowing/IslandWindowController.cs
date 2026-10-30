using System.Runtime.InteropServices;
using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Domain;
using Island.Core.Theming;
using Island.Core.Windowing;

namespace Island.Windows.Windowing;

/// <summary>
/// Controlador de ventana en vivo para la isla flotante (Secciones 4 y 7).
/// Sincroniza la geometría física (DPI/Monitor), la región redondeada de paso de clics (SetWindowRgn),
/// los estilos extendidos (WS_EX_NOACTIVATE en modo pasivo vs interactivo en modo activo/anclado)
/// y el registro opcional de atajo global de recuperación (Alt+Shift+I).
/// </summary>
public sealed class IslandWindowController : IDisposable
{
    public const int RecoveryHotKeyId = 0x4953; // 'IS'
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkI = 0x49;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private readonly nint _hwnd;
    private readonly WindowsDisplayAndWindowProber _displayProber;
    private readonly IDiagnosticLogger _logger;
    private bool _hotKeyRegistered;
    private IslandComputedGeometry? _lastAppliedGeometry;

    public IslandWindowController(
        nint hwnd,
        WindowsDisplayAndWindowProber displayProber,
        IDiagnosticLogger logger)
    {
        _hwnd = hwnd;
        _displayProber = displayProber ?? throw new ArgumentNullException(nameof(displayProber));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IslandComputedGeometry? LastAppliedGeometry => _lastAppliedGeometry;
    public bool IsRecoveryHotKeyRegistered => _hotKeyRegistered;

    /// <summary>
    /// Indica si la isla está suprimida (oculta por completo) por pantalla completa o por petición del usuario.
    /// Mientras esté suprimida, ApplyLayoutAndRegion calcula la geometría pero nunca vuelve a mostrar la ventana.
    /// </summary>
    public bool IsSuppressed { get; private set; }

    /// <summary>
    /// Oculta (SW_HIDE) o permite mostrar de nuevo la ventana de la isla.
    /// Al levantar la supresión, el llamador debe volver a aplicar el layout para reposicionar y mostrar la ventana.
    /// </summary>
    public void SetSuppressed(bool suppressed)
    {
        if (IsSuppressed == suppressed)
        {
            return;
        }

        IsSuppressed = suppressed;
        if (suppressed && _hwnd != 0)
        {
            ShowWindow(_hwnd, SwHide);
        }
    }

    private bool _hotKeyBoundToHwnd;

    public bool TryRegisterRecoveryHotKey()
    {
        try
        {
            if (_hwnd != 0 && RegisterHotKey(_hwnd, RecoveryHotKeyId, ModAlt | ModShift | ModNoRepeat, VkI))
            {
                _hotKeyRegistered = true;
                _hotKeyBoundToHwnd = true;
                return true;
            }

            if (RegisterHotKey(0, RecoveryHotKeyId, ModAlt | ModShift | ModNoRepeat, VkI))
            {
                _hotKeyRegistered = true;
                _hotKeyBoundToHwnd = false;
                return true;
            }

            _hotKeyRegistered = false;
            return false;
        }
        catch (Exception)
        {
            _hotKeyRegistered = false;
            return false;
        }
    }

    /// <summary>
    /// Aplica el estado visual, tema y anclaje sobre el HWND real de la isla.
    /// Si la visibilidad es Hidden, vacía la región y oculta la ventana (SW_HIDE) para garantizar
    /// cero áreas fantasma en pantalla.
    /// </summary>
    public IslandComputedGeometry ApplyLayoutAndRegion(
        IslandStateSnapshot state,
        ThemeDefinition theme,
        string? preferredMonitorDeviceName,
        double anchorHorizontalRatio,
        double anchorTopOffsetDip,
        double? customWidthDipOverride = null,
        double? customHeightDipOverride = null,
        double? anchorVerticalRatio = null)
    {
        var effectiveTheme = theme;
        if (customWidthDipOverride.HasValue || customHeightDipOverride.HasValue)
        {
            effectiveTheme = theme with
            {
                CompactWidth = customWidthDipOverride ?? theme.CompactWidth,
                CompactHeight = customHeightDipOverride ?? theme.CompactHeight
            };
        }

        var monitors = _displayProber.CaptureMonitors();
        var geometry = IslandLayoutCalculator.ComputeGeometry(
            monitors,
            preferredMonitorDeviceName,
            state.Visibility,
            effectiveTheme,
            anchorHorizontalRatio,
            anchorTopOffsetDip,
            anchorVerticalRatio,
            customWidthDipOverride,
            customHeightDipOverride);

        if (anchorVerticalRatio is null && state.Visibility != IslandVisibility.Hidden &&
            (customWidthDipOverride is > 0 || customHeightDipOverride is > 0))
        {
            double wDip = customWidthDipOverride ?? geometry.PillWidthDip;
            double hDip = customHeightDipOverride ?? geometry.PillHeightDip;
            int wPx = Math.Max(1, (int)Math.Round(wDip * geometry.ScaleFactor));
            int hPx = Math.Max(1, (int)Math.Round(hDip * geometry.ScaleFactor));
            int centerX = geometry.PillScreenBoundsPx.X + (geometry.PillScreenBoundsPx.Width / 2);
            int customX = centerX - (wPx / 2);
            var customPillBounds = new ScreenRectPx(customX, geometry.PillScreenBoundsPx.Y, wPx, hPx);

            geometry = geometry with
            {
                PillWidthDip = wDip,
                PillHeightDip = hDip,
                PillScreenBoundsPx = customPillBounds
            };
        }

        _lastAppliedGeometry = geometry;

        if (_hwnd == 0)
        {
            return geometry;
        }

        if (IsSuppressed)
        {
            ShowWindow(_hwnd, SwHide);
            return geometry;
        }

        if (state.Visibility == IslandVisibility.Hidden)
        {
            nint emptyRgn = CreateRectRgn(0, 0, 0, 0);
            if (emptyRgn != 0)
            {
                if (SetWindowRgn(_hwnd, emptyRgn, bRedraw: false) == 0)
                {
                    DeleteObject(emptyRgn);
                }
            }

            ShowWindow(_hwnd, SwHide);
            return geometry;
        }

        bool passiveNoActivate = state.Interaction == IslandInteractionState.Passive;

        // Sincronizamos el HWND directamente con las coordenadas exactas de la píldora en pantalla (PillScreenBoundsPx)
        // y recortamos la región redondeada completa (0, 0, Width, Height) para eliminar cualquier desfase entre el
        // área cliente de WinUI 3 (DesktopChildSiteBridge) y el borde no-cliente oculto de OverlappedPresenter.
        var directPillRelativeBounds = new ScreenRectPx(
            0,
            0,
            geometry.PillScreenBoundsPx.Width,
            geometry.PillScreenBoundsPx.Height);

        WindowsDisplayAndWindowProber.ApplyIslandWindowRegionAndStyles(
            _hwnd,
            geometry.PillScreenBoundsPx,
            directPillRelativeBounds,
            geometry.CornerRadiusPx,
            passiveNoActivate);

        ShowWindow(_hwnd, SwShowNoActivate);
        return geometry;
    }

    public void Dispose()
    {
        if (_hotKeyRegistered)
        {
            try
            {
                UnregisterHotKey(_hotKeyBoundToHwnd ? _hwnd : 0, RecoveryHotKeyId);
            }
            catch (Exception)
            {
            }

            _hotKeyRegistered = false;
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteObject(nint ho);
}
