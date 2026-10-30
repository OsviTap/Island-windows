using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Island.Core.Theming;

namespace Island.Application.Theming;

public sealed record ThemeValidationResult(
    bool IsValid,
    ThemeDefinition? ValidatedTheme,
    IReadOnlyList<string> Errors);

/// <summary>
/// Validador declarativo de temas (Sección 5).
/// Garantiza que los temas sean exclusivamente datos dentro de límites seguros,
/// sin XAML arbitrario, scripts ni referencias ejecutables.
/// </summary>
public static partial class ThemeValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly string[] ForbiddenContentTokens =
    [
        "<ResourceDictionary",
        "x:Class",
        "http://schemas.microsoft.com/winfx",
        "<script",
        "javascript:",
        "powershell",
        "cmd.exe",
        ".dll"
    ];

    [GeneratedRegex(@"^[a-zA-Z0-9._\-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeThemeIdRegex();

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    public static string Serialize(ThemeDefinition theme) =>
        JsonSerializer.Serialize(theme, SerializerOptions);

    public static ThemeValidationResult ValidateJsonBytes(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length == 0)
        {
            return new ThemeValidationResult(false, null, ["THEME_EMPTY_PAYLOAD"]);
        }

        if (utf8Json.Length > ThemeGeometryLimits.MaxThemeFileBytes)
        {
            return new ThemeValidationResult(false, null, [$"THEME_FILE_TOO_LARGE:{utf8Json.Length}"]);
        }

        string rawText;
        try
        {
            rawText = Encoding.UTF8.GetString(utf8Json);
        }
        catch (Exception)
        {
            return new ThemeValidationResult(false, null, ["THEME_INVALID_UTF8"]);
        }

        foreach (string token in ForbiddenContentTokens)
        {
            if (rawText.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return new ThemeValidationResult(false, null, [$"THEME_FORBIDDEN_EXECUTABLE_OR_XAML_TOKEN:{token}"]);
            }
        }

        ThemeDefinition? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ThemeDefinition>(utf8Json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            return new ThemeValidationResult(false, null, [$"THEME_INVALID_JSON_SCHEMA:{ex.Path ?? "root"}"]);
        }

        if (parsed is null)
        {
            return new ThemeValidationResult(false, null, ["THEME_NULL_DOCUMENT"]);
        }

        return ValidateDefinition(parsed);
    }

    public static ThemeValidationResult ValidateDefinition(ThemeDefinition? theme)
    {
        if (theme is null)
        {
            return new ThemeValidationResult(false, null, ["THEME_NULL_INSTANCE"]);
        }

        var errors = new List<string>();

        if (theme.SchemaVersion != ThemeGeometryLimits.CurrentSchemaVersion)
        {
            errors.Add($"THEME_UNSUPPORTED_SCHEMA_VERSION:{theme.SchemaVersion}");
        }

        if (string.IsNullOrWhiteSpace(theme.Id) || !SafeThemeIdRegex().IsMatch(theme.Id))
        {
            errors.Add("THEME_INVALID_ID");
        }

        if (string.IsNullOrWhiteSpace(theme.DisplayName) || theme.DisplayName.Length > 80)
        {
            errors.Add("THEME_INVALID_DISPLAY_NAME");
        }

        if (!Enum.IsDefined(theme.RequestedMaterial))
        {
            errors.Add("THEME_INVALID_REQUESTED_MATERIAL");
        }

        if (!Enum.IsDefined(theme.ColorScheme))
        {
            errors.Add("THEME_INVALID_COLOR_SCHEME");
        }

        if (!Enum.IsDefined(theme.Density))
        {
            errors.Add("THEME_INVALID_DENSITY");
        }

        if (!Enum.IsDefined(theme.AnimationProfile))
        {
            errors.Add("THEME_INVALID_ANIMATION_PROFILE");
        }

        ValidateRange(theme.CornerRadius, ThemeGeometryLimits.MinCornerRadius, ThemeGeometryLimits.MaxCornerRadius, "THEME_CORNER_RADIUS_OUT_OF_RANGE", errors);
        ValidateRange(theme.CompactWidth, ThemeGeometryLimits.MinCompactWidth, ThemeGeometryLimits.MaxCompactWidth, "THEME_COMPACT_WIDTH_OUT_OF_RANGE", errors);
        ValidateRange(theme.CompactHeight, ThemeGeometryLimits.MinCompactHeight, ThemeGeometryLimits.MaxCompactHeight, "THEME_COMPACT_HEIGHT_OUT_OF_RANGE", errors);
        ValidateRange(theme.ExpandedWidth, ThemeGeometryLimits.MinExpandedWidth, ThemeGeometryLimits.MaxExpandedWidth, "THEME_EXPANDED_WIDTH_OUT_OF_RANGE", errors);
        ValidateRange(theme.ExpandedHeight, ThemeGeometryLimits.MinExpandedHeight, ThemeGeometryLimits.MaxExpandedHeight, "THEME_EXPANDED_HEIGHT_OUT_OF_RANGE", errors);

        if (theme.ExpandedWidth <= theme.CompactWidth)
        {
            errors.Add("THEME_EXPANDED_WIDTH_MUST_EXCEED_COMPACT_WIDTH");
        }

        if (theme.ExpandedHeight <= theme.CompactHeight)
        {
            errors.Add("THEME_EXPANDED_HEIGHT_MUST_EXCEED_COMPACT_HEIGHT");
        }

        if (theme.Colors is null)
        {
            errors.Add("THEME_COLORS_MISSING");
        }
        else
        {
            ValidateHexColor(theme.Colors.BackgroundHex, "THEME_INVALID_BACKGROUND_HEX", errors);
            ValidateHexColor(theme.Colors.ForegroundHex, "THEME_INVALID_FOREGROUND_HEX", errors);
            ValidateHexColor(theme.Colors.BorderHex, "THEME_INVALID_BORDER_HEX", errors);
            ValidateHexColor(theme.Colors.AccentHex, "THEME_INVALID_ACCENT_HEX", errors);
            ValidateHexColor(theme.Colors.HighContrastBackgroundHex, "THEME_INVALID_HC_BACKGROUND_HEX", errors);
            ValidateHexColor(theme.Colors.HighContrastForegroundHex, "THEME_INVALID_HC_FOREGROUND_HEX", errors);
            ValidateHexColor(theme.Colors.HighContrastBorderHex, "THEME_INVALID_HC_BORDER_HEX", errors);
            ValidateHexColor(theme.Colors.HighContrastAccentHex, "THEME_INVALID_HC_ACCENT_HEX", errors);
        }

        return errors.Count == 0
            ? new ThemeValidationResult(true, theme, [])
            : new ThemeValidationResult(false, null, errors);
    }

    private static void ValidateRange(double value, double min, double max, string errorCode, List<string> errors)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            errors.Add(errorCode);
        }
    }

    private static void ValidateHexColor(string? hex, string errorCode, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(hex) || !HexColorRegex().IsMatch(hex))
        {
            errors.Add(errorCode);
        }
    }
}
