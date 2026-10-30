namespace Island.Core.Theming;

/// <summary>
/// Material solicitado por el usuario o el tema (Sección 4).
/// </summary>
public enum MaterialKind
{
    Solid = 0,
    Glass = 1,
    Mica = 2
}

/// <summary>
/// Material efectivo realmente aplicado tras evaluar soporte de plataforma,
/// preferencias de transparencia del sistema y modo de Alto Contraste.
/// </summary>
public enum EffectiveMaterialKind
{
    Solid = 0,
    DesktopAcrylic = 1,
    Mica = 2,
    HighContrastSolid = 3
}

public enum ColorSchemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum VisualDensity
{
    Compact = 0,
    Comfortable = 1
}

public enum AnimationProfile
{
    Normal = 0,
    Reduced = 1,
    Disabled = 2
}

/// <summary>
/// Límites geométricos admitidos para evitar que un tema declarativo rompa la interfaz.
/// </summary>
public static class ThemeGeometryLimits
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxThemeFileBytes = 64 * 1024; // 64 KB máximo para un archivo de tema JSON
    public const double MinCornerRadius = 0.0;
    public const double MaxCornerRadius = 32.0;
    public const double MinCompactWidth = 120.0;
    public const double MaxCompactWidth = 360.0;
    public const double MinCompactHeight = 28.0;
    public const double MaxCompactHeight = 64.0;
    public const double MinExpandedWidth = 280.0;
    public const double MaxExpandedWidth = 560.0;
    public const double MinExpandedHeight = 96.0;
    public const double MaxExpandedHeight = 360.0;
}

/// <summary>
/// Colores semánticos en formato hexadecimal (#RRGGBB o #AARRGGBB), incluyendo paleta dedicada de Alto Contraste.
/// </summary>
public sealed record ThemeSemanticColors(
    string BackgroundHex,
    string ForegroundHex,
    string BorderHex,
    string AccentHex,
    string HighContrastBackgroundHex,
    string HighContrastForegroundHex,
    string HighContrastBorderHex,
    string HighContrastAccentHex);

/// <summary>
/// Definición declarativa de un tema (sin código ejecutable, scripts ni XAML arbitrario).
/// </summary>
public sealed record ThemeDefinition(
    int SchemaVersion,
    string Id,
    string DisplayName,
    MaterialKind RequestedMaterial,
    ColorSchemePreference ColorScheme,
    VisualDensity Density,
    AnimationProfile AnimationProfile,
    double CornerRadius,
    double CompactWidth,
    double CompactHeight,
    double ExpandedWidth,
    double ExpandedHeight,
    ThemeSemanticColors Colors);

/// <summary>
/// Resultado de aplicar un tema al estado actual del sistema, conservando por separado
/// el material solicitado y el material efectivo, así como el perfil de animación efectivo.
/// </summary>
public sealed record AppliedThemeState(
    ThemeDefinition Theme,
    MaterialKind RequestedMaterial,
    EffectiveMaterialKind EffectiveMaterial,
    AnimationProfile EffectiveAnimationProfile,
    bool IsHighContrastActive,
    bool UsedFallbackTheme,
    string StatusReasonCode);

/// <summary>
/// Temas integrados de referencia. El tema sólido integrado actúa como salvaguarda incondicional.
/// </summary>
public static class BuiltInThemes
{
    public const string DefaultSolidThemeId = "builtin.solid.dark";
    public const string DefaultGlassThemeId = "builtin.glass.dark";
    public const string DefaultMicaThemeId = "builtin.mica.dark";

    public static ThemeDefinition DefaultSolid { get; } = new(
        SchemaVersion: ThemeGeometryLimits.CurrentSchemaVersion,
        Id: DefaultSolidThemeId,
        DisplayName: "Isla S\u00F3lida (Predeterminado Seguro)",
        RequestedMaterial: MaterialKind.Solid,
        ColorScheme: ColorSchemePreference.Dark,
        Density: VisualDensity.Compact,
        AnimationProfile: AnimationProfile.Normal,
        CornerRadius: 16.0,
        CompactWidth: 260.0,
        CompactHeight: 40.0,
        ExpandedWidth: 420.0,
        ExpandedHeight: 214.0,
        Colors: new ThemeSemanticColors(
            BackgroundHex: "#FF111318",
            ForegroundHex: "#FFF4F4F5",
            BorderHex: "#33FFFFFF",
            AccentHex: "#FF38BDF8",
            HighContrastBackgroundHex: "#FF000000",
            HighContrastForegroundHex: "#FFFFFFFF",
            HighContrastBorderHex: "#FFFFFF00",
            HighContrastAccentHex: "#FF00FFFF"));

    public static ThemeDefinition DefaultGlass { get; } = DefaultSolid with
    {
        Id = DefaultGlassThemeId,
        DisplayName = "Isla Glass (Acrylic)",
        RequestedMaterial = MaterialKind.Glass,
        Colors = DefaultSolid.Colors with
        {
            // Let the native acrylic backdrop show through the XAML surface.
            BackgroundHex = "#100E1015",
            BorderHex = "#3DFFFFFF"
        }
    };

    public static ThemeDefinition DefaultMica { get; } = DefaultSolid with
    {
        Id = DefaultMicaThemeId,
        DisplayName = "Isla Mica (Windows 11)",
        RequestedMaterial = MaterialKind.Mica,
        Colors = DefaultSolid.Colors with
        {
            BackgroundHex = "#1813151B",
            BorderHex = "#33FFFFFF",
            AccentHex = "#FF60A5FA"
        }
    };

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        DefaultSolid,
        DefaultGlass,
        DefaultMica
    ];

    public static ThemeDefinition GetByIdOrDefault(string? themeId) =>
        All.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase))
        ?? DefaultSolid;
}
