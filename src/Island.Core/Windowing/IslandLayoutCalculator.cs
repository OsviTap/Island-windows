using Island.Core.Contracts;
using Island.Core.Domain;
using Island.Core.Theming;

namespace Island.Core.Windowing;

/// <summary>
/// Geometría física (en píxeles) y lógica (en DIPs) calculada para posicionar la ventana contenedora
/// y recortar exactamente la región interactiva de la píldora según el monitor, escala DPI y estado (Secciones 4 y 7).
/// </summary>
public sealed record IslandComputedGeometry(
    string TargetMonitorDeviceName,
    bool UsedPrimaryMonitorFallback,
    int ScalePercent,
    double ScaleFactor,
    double PillWidthDip,
    double PillHeightDip,
    double CornerRadiusDip,
    int CornerRadiusPx,
    ScreenRectPx ContainerScreenBoundsPx,
    ScreenRectPx PillRelativeBoundsPx,
    ScreenRectPx PillScreenBoundsPx);

/// <summary>
/// Calculador determinista de geometría multi-monitor y multi-DPI para la isla flotante.
/// Garantiza que la isla nunca quede fuera del área de trabajo (WorkAreaPx) ante cambios de resolución,
/// desconexión de monitores o escalas DPI (100%, 125%, 150%, 175%, 200%).
/// </summary>
public static class IslandLayoutCalculator
{
    private const double ContainerMarginDip = 12.0;

    public static IslandComputedGeometry ComputeGeometry(
        IReadOnlyList<MonitorDisplayDescriptor> monitors,
        string? preferredMonitorDeviceName,
        IslandVisibility visibility,
        ThemeDefinition theme,
        double anchorHorizontalRatio = 0.5,
        double anchorTopOffsetDip = 12.0,
        double? anchorVerticalRatio = null,
        double? widthOverrideDip = null,
        double? heightOverrideDip = null)
    {
        ArgumentNullException.ThrowIfNull(theme);

        MonitorDisplayDescriptor targetMonitor;
        bool usedFallback = false;

        if (monitors is null || monitors.Count == 0)
        {
            targetMonitor = new MonitorDisplayDescriptor(
                DeviceName: "FALLBACK_PRIMARY",
                IsPrimary: true,
                BoundsPx: new ScreenRectPx(0, 0, 1920, 1080),
                WorkAreaPx: new ScreenRectPx(0, 0, 1920, 1032),
                DpiX: 96,
                DpiY: 96,
                ScalePercent: 100);
            usedFallback = true;
        }
        else if (!string.IsNullOrWhiteSpace(preferredMonitorDeviceName))
        {
            var matched = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                targetMonitor = matched;
            }
            else
            {
                targetMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                usedFallback = true;
            }
        }
        else
        {
            targetMonitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        }

        double scaleFactor = Math.Max(1.0, targetMonitor.DpiX / 96.0);
        double clampedRatio = Math.Clamp(double.IsNaN(anchorHorizontalRatio) ? 0.5 : anchorHorizontalRatio, 0.05, 0.95);
        double clampedTopOffsetDip = Math.Clamp(double.IsNaN(anchorTopOffsetDip) ? 12.0 : anchorTopOffsetDip, 0.0, 120.0);

        double densityMultiplier = theme.Density == VisualDensity.Comfortable ? 1.08 : 1.0;

        double pillWidthDip = visibility == IslandVisibility.Expanded
            ? Math.Clamp(theme.ExpandedWidth * densityMultiplier, ThemeGeometryLimits.MinExpandedWidth, ThemeGeometryLimits.MaxExpandedWidth)
            : Math.Clamp(theme.CompactWidth * densityMultiplier, ThemeGeometryLimits.MinCompactWidth, ThemeGeometryLimits.MaxCompactWidth);

        double pillHeightDip = visibility == IslandVisibility.Expanded
            ? Math.Clamp(theme.ExpandedHeight * densityMultiplier, ThemeGeometryLimits.MinExpandedHeight, ThemeGeometryLimits.MaxExpandedHeight)
            : Math.Clamp(theme.CompactHeight * densityMultiplier, ThemeGeometryLimits.MinCompactHeight, ThemeGeometryLimits.MaxCompactHeight);

        pillWidthDip = widthOverrideDip ?? pillWidthDip;
        pillHeightDip = heightOverrideDip ?? pillHeightDip;

        double maxContainerWidthDip = Math.Max(pillWidthDip, ThemeGeometryLimits.MaxExpandedWidth) + (ContainerMarginDip * 2.0);
        double maxContainerHeightDip = Math.Max(pillHeightDip, ThemeGeometryLimits.MaxExpandedHeight) + (ContainerMarginDip * 2.0);

        int containerWidthPx = Math.Max(1, (int)Math.Round(maxContainerWidthDip * scaleFactor));
        int containerHeightPx = Math.Max(1, (int)Math.Round(maxContainerHeightDip * scaleFactor));

        int pillWidthPx = visibility == IslandVisibility.Hidden
            ? 0
            : Math.Max(1, (int)Math.Round(pillWidthDip * scaleFactor));

        int pillHeightPx = visibility == IslandVisibility.Hidden
            ? 0
            : Math.Max(1, (int)Math.Round(pillHeightDip * scaleFactor));

        int cornerRadiusPx = Math.Max(0, (int)Math.Round(theme.CornerRadius * scaleFactor));
        int topOffsetPx = (int)Math.Round(clampedTopOffsetDip * scaleFactor);
        int marginPx = (int)Math.Round(ContainerMarginDip * scaleFactor);

        var workArea = targetMonitor.WorkAreaPx;

        // El centro horizontal deseado dentro del WorkArea del monitor objetivo
        int anchorCenterX = workArea.X + (int)Math.Round(workArea.Width * clampedRatio);
        int rawContainerX = anchorCenterX - (containerWidthPx / 2);
        int rawContainerY = workArea.Y + topOffsetPx - marginPx;

        // Garantizar que el contenedor nunca quede fuera de los límites horizontales o verticales del WorkArea
        int minContainerX = workArea.X;
        int maxContainerX = Math.Max(minContainerX, workArea.Right - containerWidthPx);
        int clampedContainerX = Math.Clamp(rawContainerX, minContainerX, maxContainerX);

        int minContainerY = workArea.Y;
        int maxContainerY = Math.Max(minContainerY, workArea.Bottom - containerHeightPx);
        int clampedContainerY = Math.Clamp(rawContainerY, minContainerY, maxContainerY);

        var containerScreenBounds = new ScreenRectPx(
            clampedContainerX,
            clampedContainerY,
            containerWidthPx,
            containerHeightPx);

        int pillRelX = (containerWidthPx - pillWidthPx) / 2;
        int pillRelY = marginPx;

        var pillRelativeBounds = new ScreenRectPx(
            pillRelX,
            pillRelY,
            pillWidthPx,
            pillHeightPx);

        var pillScreenBounds = new ScreenRectPx(
            containerScreenBounds.X + pillRelativeBounds.X,
            containerScreenBounds.Y + pillRelativeBounds.Y,
            pillRelativeBounds.Width,
            pillRelativeBounds.Height);

        if (anchorVerticalRatio is double vertical && double.IsFinite(vertical))
        {
            // Anchor describes the center of the resting capsule. On the lower half,
            // the larger panel grows upward; elsewhere it grows downward.
            int anchorY = workArea.Y + (int)Math.Round(workArea.Height * Math.Clamp(vertical, 0, 1));
            int restingHalfHeight = (int)Math.Round(14 * scaleFactor);
            int x = Math.Clamp(anchorCenterX - pillWidthPx / 2, workArea.X, Math.Max(workArea.X, workArea.Right - pillWidthPx));
            int y = vertical > 0.5 ? anchorY + restingHalfHeight - pillHeightPx : anchorY - restingHalfHeight;
            y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - pillHeightPx));
            pillScreenBounds = new ScreenRectPx(x, y, pillWidthPx, pillHeightPx);
        }

        return new IslandComputedGeometry(
            TargetMonitorDeviceName: targetMonitor.DeviceName,
            UsedPrimaryMonitorFallback: usedFallback,
            ScalePercent: targetMonitor.ScalePercent,
            ScaleFactor: scaleFactor,
            PillWidthDip: pillWidthDip,
            PillHeightDip: pillHeightDip,
            CornerRadiusDip: theme.CornerRadius,
            CornerRadiusPx: cornerRadiusPx,
            ContainerScreenBoundsPx: containerScreenBounds,
            PillRelativeBoundsPx: pillRelativeBounds,
            PillScreenBoundsPx: pillScreenBounds);
    }
}
