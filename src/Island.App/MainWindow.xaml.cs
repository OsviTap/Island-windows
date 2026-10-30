using System.Globalization;
using Island.App.ViewModels;
using Island.Windows.Windowing;
using Island.Core.Contracts;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Island.App;

public sealed partial class MainWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _marqueeTimer;
    private double _compactMarqueeOffset;
    private double _compactMarqueeDirection = -1.0;
    private int _compactPauseTicks = 30;

    private double _expandedMarqueeOffset;
    private double _expandedMarqueeDirection = -1.0;
    private int _expandedPauseTicks = 30;
    private string _lastWatchedHeadline = string.Empty;
    private string _lastWatchedMediaTitle = string.Empty;
    private WindowsDisplayAndWindowProber? _displayProber;
    private (int X, int Y)? _dragStart;
    private ScreenRectPx? _dragBounds;
    private bool _dragMoved;
    private bool _openAfterClick;
    private int _dragAnchorY;

    public bool IsDragging => _dragStart.HasValue;
    public void ConfigureDragging(WindowsDisplayAndWindowProber displayProber) => _displayProber = displayProber;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        ViewModel.LayoutOrThemeUpdated += (_, _) => ApplyVisualThemeToPill();
        ApplyVisualThemeToPill();

        if (DispatcherQueue is not null)
        {
            _marqueeTimer = DispatcherQueue.CreateTimer();
            _marqueeTimer.Interval = TimeSpan.FromMilliseconds(33); // ~30 FPS suave para el ticker musical
            _marqueeTimer.Tick += OnMarqueeTimerTick;
            _marqueeTimer.Start();
        }
    }

    public void ApplyVisualThemeToPill()
    {
        // DWMWCP_ROUND en Windows 11 aplica un radio de esquina nativo de 8 DIPs con anti-aliasing por hardware.
        // Sincronizamos el CornerRadius del borde interior con 8 DIPs para evitar cualquier esquina exterior expuesta.
        IslandPillBorder.CornerRadius = new CornerRadius(8);
        IslandPillBorder.Background = new SolidColorBrush(ParseColorHex(ViewModel.ActiveBackgroundHex, Color.FromArgb(0x4D, 0x0E, 0x10, 0x15)));
        IslandPillBorder.BorderBrush = new SolidColorBrush(ParseColorHex(ViewModel.ActiveBorderHex, Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF)));

        var fgBrush = new SolidColorBrush(ParseColorHex(ViewModel.ActiveForegroundHex, Color.FromArgb(0xFF, 0xF4, 0xF4, 0xF5)));
        var accentBrush = new SolidColorBrush(ParseColorHex(ViewModel.ActiveAccentHex, Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8)));

        CompactHeadlineTextBlock.Foreground = fgBrush;
        MediaTitleTextBlock.Foreground = fgBrush;
        NotificationHeadlineTextBlock.Foreground = fgBrush;
        MiniAccentDot.Background = accentBrush;
        MiniAccentIcon.Foreground = accentBrush;
        HeaderAccentDot.Background = accentBrush;
        CompactAccentIcon.Foreground = accentBrush;
        ExpandedAccentIcon.Foreground = accentBrush;
        NotificationAccentIcon.Foreground = accentBrush;
        var accent = ParseColorHex(ViewModel.ActiveAccentHex, Color.FromArgb(255, 56, 189, 248));
        var accentSurface = new SolidColorBrush(Color.FromArgb(65, accent.R, accent.G, accent.B));
        var accentBorder = new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
        CompactArtworkFrame.Background = accentSurface;
        CompactArtworkFrame.BorderBrush = accentBorder;
        ExpandedArtworkFrame.Background = accentSurface;
        ExpandedArtworkFrame.BorderBrush = accentBorder;
        CompactPlayButton.Background = accentSurface;
        CompactPlayButton.BorderBrush = accentBorder;
        CompactPlayButton.Foreground = fgBrush;
        ExpandedPlayButton.Background = accentSurface;
        ExpandedPlayButton.BorderBrush = accentBorder;
        ExpandedPlayButton.Foreground = fgBrush;


        if (!string.Equals(_lastWatchedHeadline, ViewModel.CompactHeadlineText, StringComparison.Ordinal))
        {
            _lastWatchedHeadline = ViewModel.CompactHeadlineText;
            _compactMarqueeOffset = 0;
            _compactMarqueeDirection = -1.0;
            _compactPauseTicks = 25;
            CompactHeadlineTranslateTransform.X = 0;
        }

        if (!string.Equals(_lastWatchedMediaTitle, ViewModel.MediaTitleText, StringComparison.Ordinal))
        {
            _lastWatchedMediaTitle = ViewModel.MediaTitleText;
            _expandedMarqueeOffset = 0;
            _expandedMarqueeDirection = -1.0;
            _expandedPauseTicks = 25;
            ExpandedTitleTranslateTransform.X = 0;
        }
    }

    private SettingsWindow? _settingsWindow;
    public bool IsSettingsOpen => _settingsWindow is not null;

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(ViewModel);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private void OnDragPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || _displayProber is null || !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed) return;
        _dragStart = WindowsDisplayAndWindowProber.GetCursorScreenPosition();
        _dragBounds = WindowsDisplayAndWindowProber.GetHwndScreenRect(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var originalMonitor = _displayProber.CaptureMonitors().FirstOrDefault(m => m.DeviceName == ViewModel.PreferredMonitorDeviceName)
            ?? _displayProber.CaptureMonitors().FirstOrDefault(m => m.BoundsPx.Contains(_dragBounds.X + _dragBounds.Width / 2, _dragBounds.Y));
        _dragAnchorY = originalMonitor is not null && ViewModel.CurrentSettings.AnchorVerticalRatio is double ratio
            ? originalMonitor.WorkAreaPx.Y + (int)Math.Round(originalMonitor.WorkAreaPx.Height * ratio)
            : _dragBounds.Y + (int)Math.Round(14.0 * (originalMonitor?.DpiY ?? 96) / 96.0);
        _dragMoved = false;
        _openAfterClick = ReferenceEquals(sender, IslandPillBorder) && !ViewModel.IsExpandedMode;
        if (_dragStart.HasValue) element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnDragMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not { } start || _dragBounds is null || _displayProber is null) return;
        var cursor = WindowsDisplayAndWindowProber.GetCursorScreenPosition();
        if (cursor is not { } point) return;
        int dx = point.X - start.X, dy = point.Y - start.Y;
        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 6) return;
        _dragMoved = true;
        int centerX = _dragBounds.X + _dragBounds.Width / 2 + dx;
        // Convert the expanded panel's edge back to the resting capsule anchor.
        int anchorY = _dragAnchorY + dy;
        var monitors = _displayProber.CaptureMonitors();
        var monitor = monitors.FirstOrDefault(m => m.BoundsPx.Contains(point.X, point.Y)) ?? monitors.FirstOrDefault(m => m.IsPrimary);
        if (monitor is null || monitor.WorkAreaPx.Width <= 0 || monitor.WorkAreaPx.Height <= 0) return;
        ViewModel.MoveAnchor(monitor.DeviceName, (centerX - monitor.WorkAreaPx.X) / (double)monitor.WorkAreaPx.Width,
            (anchorY - monitor.WorkAreaPx.Y) / (double)monitor.WorkAreaPx.Height, false);
        e.Handled = true;
    }

    private void OnDragReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragStart.HasValue) return;
        bool open = _openAfterClick && !_dragMoved;
        if (_dragMoved && ViewModel.PreferredMonitorDeviceName is { } monitor && ViewModel.CurrentSettings.AnchorVerticalRatio is double vertical)
            ViewModel.MoveAnchor(monitor, ViewModel.CurrentSettings.AnchorHorizontalRatio, vertical, true);
        _dragStart = null;
        _dragBounds = null;
        _openAfterClick = false;
        if (sender is UIElement element) element.ReleasePointerCapture(e.Pointer);
        if (open) ViewModel.ToggleExpandedCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDragCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _openAfterClick = false;
        OnDragReleased(sender, e);
    }

    private void OnPillPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Respaldo inmediato del evento nativo XAML: Nivel 1 -> Nivel 2 al entrar el cursor.
        if (ViewModel.IsMiniClosedMode)
        {
            ViewModel.SetCursorHover(true);
        }
    }

    private void OnPillPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Nivel 1 o Nivel 2 -> Nivel 3: un clic en cualquier parte de la píldora la despliega por completo.
        if (!ViewModel.IsExpandedMode)
        {
            OnDragPressed(sender, e);
        }
    }

    private void OnMarqueeTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (!ViewModel.CurrentSettings.AnimationsEnabled)
        {
            CompactHeadlineTranslateTransform.X = 0;
            ExpandedTitleTranslateTransform.X = 0;
            return;
        }
        // 1. Ticker horizontal en vista compacta (Nivel 2)
        if (ViewModel.IsCompactMode && CompactMarqueeScrollViewer.ActualWidth > 10)
        {
            double overflow = CompactHeadlineTextBlock.ActualWidth - CompactMarqueeScrollViewer.ActualWidth;
            if (overflow > 4)
            {
                if (_compactPauseTicks > 0)
                {
                    _compactPauseTicks--;
                }
                else
                {
                    _compactMarqueeOffset += _compactMarqueeDirection * 1.1;
                    if (_compactMarqueeOffset <= -overflow)
                    {
                        _compactMarqueeOffset = -overflow;
                        _compactMarqueeDirection = 1.0;
                        _compactPauseTicks = 35;
                    }
                    else if (_compactMarqueeOffset >= 0)
                    {
                        _compactMarqueeOffset = 0;
                        _compactMarqueeDirection = -1.0;
                        _compactPauseTicks = 35;
                    }

                    CompactHeadlineTranslateTransform.X = _compactMarqueeOffset;
                }
            }
            else
            {
                CompactHeadlineTranslateTransform.X = 0;
            }
        }

        // 2. Ticker horizontal en vista expandida (Nivel 3)
        if (ViewModel.IsExpandedMode && ExpandedMarqueeScrollViewer.ActualWidth > 10)
        {
            double overflow = MediaTitleTextBlock.ActualWidth - ExpandedMarqueeScrollViewer.ActualWidth;
            if (overflow > 4)
            {
                if (_expandedPauseTicks > 0)
                {
                    _expandedPauseTicks--;
                }
                else
                {
                    _expandedMarqueeOffset += _expandedMarqueeDirection * 1.1;
                    if (_expandedMarqueeOffset <= -overflow)
                    {
                        _expandedMarqueeOffset = -overflow;
                        _expandedMarqueeDirection = 1.0;
                        _expandedPauseTicks = 35;
                    }
                    else if (_expandedMarqueeOffset >= 0)
                    {
                        _expandedMarqueeOffset = 0;
                        _expandedMarqueeDirection = -1.0;
                        _expandedPauseTicks = 35;
                    }

                    ExpandedTitleTranslateTransform.X = _expandedMarqueeOffset;
                }
            }
            else
            {
                ExpandedTitleTranslateTransform.X = 0;
            }
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            ViewModel.CollapseOnEscapeCommand.Execute(null);
            e.Handled = true;
        }
    }

    internal static Color ParseColorHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#')
        {
            return fallback;
        }

        ReadOnlySpan<char> span = hex.AsSpan(1);
        if (span.Length == 6 &&
            uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return Color.FromArgb(
                0xFF,
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF));
        }

        if (span.Length == 8 &&
            uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            return Color.FromArgb(
                (byte)((argb >> 24) & 0xFF),
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF));
        }

        return fallback;
    }
}
