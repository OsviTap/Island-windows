using Island.Core.Contracts;
using Island.Core.Domain;
using Island.Core.Theming;
using Island.Core.Windowing;
using Xunit;

namespace Island.Core.Tests;

public sealed class Phase4LayoutAndThemesTests
{
    [Theory]
    [InlineData(96, 100, 1.0)]
    [InlineData(120, 125, 1.25)]
    [InlineData(144, 150, 1.5)]
    [InlineData(192, 200, 2.0)]
    public void ComputeGeometry_AcrossMultipleDpiScales_ScalesProportionallyAndClampsInsideWorkArea(
        uint dpi,
        int expectedScalePercent,
        double expectedScaleFactor)
    {
        var monitor = new MonitorDisplayDescriptor(
            DeviceName: @"\\.\DISPLAY1",
            IsPrimary: true,
            BoundsPx: new ScreenRectPx(0, 0, 1920, 1080),
            WorkAreaPx: new ScreenRectPx(0, 0, 1920, 1032),
            DpiX: dpi,
            DpiY: dpi,
            ScalePercent: expectedScalePercent);

        var compactGeo = IslandLayoutCalculator.ComputeGeometry(
            [monitor],
            @"\\.\DISPLAY1",
            IslandVisibility.Compact,
            BuiltInThemes.DefaultGlass,
            anchorHorizontalRatio: 0.5,
            anchorTopOffsetDip: 12.0);

        var expandedGeo = IslandLayoutCalculator.ComputeGeometry(
            [monitor],
            @"\\.\DISPLAY1",
            IslandVisibility.Expanded,
            BuiltInThemes.DefaultGlass,
            anchorHorizontalRatio: 0.5,
            anchorTopOffsetDip: 12.0);

        Assert.Equal(expectedScalePercent, compactGeo.ScalePercent);
        Assert.Equal(expectedScaleFactor, compactGeo.ScaleFactor, precision: 3);
        Assert.False(compactGeo.UsedPrimaryMonitorFallback);

        Assert.Equal((int)Math.Round(BuiltInThemes.DefaultGlass.CompactWidth * expectedScaleFactor), compactGeo.PillScreenBoundsPx.Width);
        Assert.Equal((int)Math.Round(BuiltInThemes.DefaultGlass.CompactHeight * expectedScaleFactor), compactGeo.PillScreenBoundsPx.Height);
        Assert.Equal((int)Math.Round(BuiltInThemes.DefaultGlass.ExpandedWidth * expectedScaleFactor), expandedGeo.PillScreenBoundsPx.Width);
        Assert.Equal((int)Math.Round(BuiltInThemes.DefaultGlass.ExpandedHeight * expectedScaleFactor), expandedGeo.PillScreenBoundsPx.Height);

        var leftEdgeGeo = IslandLayoutCalculator.ComputeGeometry(
            [monitor],
            @"\\.\DISPLAY1",
            IslandVisibility.Expanded,
            BuiltInThemes.DefaultGlass,
            anchorHorizontalRatio: 0.05,
            anchorTopOffsetDip: 12.0);

        var rightEdgeGeo = IslandLayoutCalculator.ComputeGeometry(
            [monitor],
            @"\\.\DISPLAY1",
            IslandVisibility.Expanded,
            BuiltInThemes.DefaultGlass,
            anchorHorizontalRatio: 0.95,
            anchorTopOffsetDip: 12.0);

        Assert.True(leftEdgeGeo.ContainerScreenBoundsPx.X >= monitor.WorkAreaPx.X);
        Assert.True(rightEdgeGeo.ContainerScreenBoundsPx.Right <= monitor.WorkAreaPx.Right);
    }

    [Fact]
    public void ComputeGeometry_WhenPreferredMonitorDisconnected_FallsBackToPrimaryMonitorSafely()
    {
        var primary = new MonitorDisplayDescriptor(
            DeviceName: @"\\.\DISPLAY1",
            IsPrimary: true,
            BoundsPx: new ScreenRectPx(0, 0, 1920, 1080),
            WorkAreaPx: new ScreenRectPx(0, 0, 1920, 1032),
            DpiX: 96,
            DpiY: 96,
            ScalePercent: 100);

        var geo = IslandLayoutCalculator.ComputeGeometry(
            [primary],
            preferredMonitorDeviceName: @"\\.\DISPLAY99_DISCONNECTED",
            visibility: IslandVisibility.Compact,
            theme: BuiltInThemes.DefaultSolid);

        Assert.True(geo.UsedPrimaryMonitorFallback);
        Assert.Equal(@"\\.\DISPLAY1", geo.TargetMonitorDeviceName);
    }

    [Fact]
    public void ComputeGeometry_WhenVisibilityIsHidden_ZeroesPillHitAreaToPreventGhostBlocking()
    {
        var primary = new MonitorDisplayDescriptor(
            DeviceName: @"\\.\DISPLAY1",
            IsPrimary: true,
            BoundsPx: new ScreenRectPx(0, 0, 1920, 1080),
            WorkAreaPx: new ScreenRectPx(0, 0, 1920, 1032),
            DpiX: 96,
            DpiY: 96,
            ScalePercent: 100);

        var geo = IslandLayoutCalculator.ComputeGeometry(
            [primary],
            preferredMonitorDeviceName: @"\\.\DISPLAY1",
            visibility: IslandVisibility.Hidden,
            theme: BuiltInThemes.DefaultSolid);

        Assert.Equal(0, geo.PillRelativeBoundsPx.Width);
        Assert.Equal(0, geo.PillRelativeBoundsPx.Height);
    }

    [Fact]
    public void BuiltInThemes_ContainsThreeDistinctMaterials()
    {
        Assert.Equal(3, BuiltInThemes.All.Count);
        Assert.Equal(MaterialKind.Solid, BuiltInThemes.DefaultSolid.RequestedMaterial);
        Assert.Equal(MaterialKind.Glass, BuiltInThemes.DefaultGlass.RequestedMaterial);
        Assert.Equal(MaterialKind.Mica, BuiltInThemes.DefaultMica.RequestedMaterial);
    }
}
