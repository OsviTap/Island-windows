using System.Globalization;
using Island.App.ViewModels;
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
        CompactBadgeTextBlock.Foreground = accentBrush;

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
            ViewModel.ToggleExpandedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMarqueeTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
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
