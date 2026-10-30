using Island.Core.Configuration;

namespace Island.Core.Theming;

public static class ThemeCustomization
{
    public static ThemeDefinition Apply(ThemeDefinition theme, AppSettingsDocument settings)
    {
        var colors = theme.Colors;
        if (settings.SurfaceOpacityPercent is int opacity && theme.RequestedMaterial != MaterialKind.Solid)
        {
            var hex = colors.BackgroundHex.TrimStart('#');
            var rgb = hex.Length == 8 ? hex[2..] : hex;
            colors = colors with { BackgroundHex = $"#{(int)Math.Round(Math.Clamp(opacity, 0, 100) * 2.55):X2}{rgb}" };
        }
        if (settings.AccentColorHex is { Length: 7 } accent)
            colors = colors with { AccentHex = accent };
        return theme with
        {
            Colors = colors,
            AnimationProfile = settings.AnimationsEnabled ? AnimationProfile.Normal : AnimationProfile.Disabled
        };
    }
}
