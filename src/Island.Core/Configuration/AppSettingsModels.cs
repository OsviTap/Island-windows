using Island.Core.Contracts;
using Island.Core.Theming;

namespace Island.Core.Configuration;

/// <summary>
/// Documento de configuración persistente versionado (JSON).
/// </summary>
public sealed record AppSettingsDocument(
    int SchemaVersion,
    string SelectedThemeId,
    MaterialKind? RequestedMaterialOverride,
    ColorSchemePreference ColorScheme,
    VisualDensity Density,
    AnimationProfile AnimationProfile,
    double AnchorHorizontalRatio,
    double AnchorTopOffsetDip,
    IReadOnlyList<IntegrationModuleId> EnabledModules,
    IReadOnlyList<string> AllowedNotificationAppIds,
    bool DiagnosticsIncludePrivateContent)
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettingsDocument CreateDefault() => new(
        SchemaVersion: CurrentSchemaVersion,
        SelectedThemeId: BuiltInThemes.DefaultSolidThemeId,
        RequestedMaterialOverride: null,
        ColorScheme: ColorSchemePreference.System,
        Density: VisualDensity.Compact,
        AnimationProfile: AnimationProfile.Normal,
        AnchorHorizontalRatio: 0.5,
        AnchorTopOffsetDip: 8.0,
        EnabledModules:
        [
            IntegrationModuleId.Environment,
            IntegrationModuleId.Windowing,
            IntegrationModuleId.Media,
            IntegrationModuleId.Notifications
        ],
        AllowedNotificationAppIds:
        [
            "5319275A.WhatsAppDesktop_cv1g1gvanyjgm"
        ],
        DiagnosticsIncludePrivateContent: false);
}

/// <summary>
/// Resultado de cargar y validar la configuración desde disco.
/// </summary>
public sealed record SettingsLoadResult(
    AppSettingsDocument Settings,
    bool WasRecoveredFromError,
    string DiagnosticCode,
    string? QuarantinedFilePath);
