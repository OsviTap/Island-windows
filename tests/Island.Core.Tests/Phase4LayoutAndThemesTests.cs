using Island.Core.Contracts;
using Island.Core.Configuration;
using Island.Core.Domain;
using Island.Core.Theming;
using Island.Core.Windowing;
using Xunit;

namespace Island.Core.Tests;

public sealed class Phase4LayoutAndThemesTests
{
    [Fact]
    public void CustomAppearance_ChangesGlassSurfaceAndAccent_WithoutMakingSolidTranslucent()
    {
        var settings = AppSettingsDocument.CreateDefault() with
        {
            SurfaceOpacityPercent = 40,
            AccentColorHex = "#A78BFA",
            AnimationsEnabled = false
        };
        var glass = ThemeCustomization.Apply(BuiltInThemes.DefaultGlass, settings);
        var solid = ThemeCustomization.Apply(BuiltInThemes.DefaultSolid, settings);
        Assert.Equal("#660E1015", glass.Colors.BackgroundHex);
        Assert.Equal("#A78BFA", glass.Colors.AccentHex);
        Assert.Equal(AnimationProfile.Disabled, glass.AnimationProfile);
        Assert.Equal(BuiltInThemes.DefaultSolid.Colors.BackgroundHex, solid.Colors.BackgroundHex);
    }

    [Theory]
    [InlineData(0.1, 0.1)]
    [InlineData(0.9, 0.9)]
    public void FreeAnchor_KeepsBothSizesVisible_AndExpandsTowardTheInterior(double x, double y)
    {
        var monitor = new MonitorDisplayDescriptor("screen", true, new ScreenRectPx(-1920, 0, 1920, 1080),
            new ScreenRectPx(-1920, 0, 1920, 1040), 96, 96, 100);
        var compact = IslandLayoutCalculator.ComputeGeometry([monitor], "screen", IslandVisibility.Compact,
            BuiltInThemes.DefaultGlass, x, 12, y, 128, 28);
        var expanded = IslandLayoutCalculator.ComputeGeometry([monitor], "screen", IslandVisibility.Expanded,
            BuiltInThemes.DefaultGlass, x, 12, y, 420, 214);
        Assert.True(expanded.PillScreenBoundsPx.X >= monitor.WorkAreaPx.X);
        Assert.True(expanded.PillScreenBoundsPx.Right <= monitor.WorkAreaPx.Right);
        Assert.True(expanded.PillScreenBoundsPx.Y >= monitor.WorkAreaPx.Y);
        Assert.True(expanded.PillScreenBoundsPx.Bottom <= monitor.WorkAreaPx.Bottom);
        if (y > 0.5) Assert.Equal(compact.PillScreenBoundsPx.Bottom, expanded.PillScreenBoundsPx.Bottom);
        else Assert.Equal(compact.PillScreenBoundsPx.Y, expanded.PillScreenBoundsPx.Y);
    }

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
