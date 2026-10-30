using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Theming;

namespace Island.Application.Theming;

/// <summary>
/// Evalúa y aplica temas declarativos separando estrictamente el material solicitado
/// del material efectivo y garantizando recuperación ante errores (Secciones 4 y 5).
/// </summary>
public sealed class ThemeService
{
    private readonly IDiagnosticLogger _logger;
    private ThemeDefinition _lastValidTheme = BuiltInThemes.DefaultSolid;

    public ThemeService(IDiagnosticLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ThemeDefinition LastValidTheme => _lastValidTheme;

    /// <summary>
    /// Evalúa un tema candidato frente al estado actual del entorno del sistema.
    /// Si el tema candidato es inválido, recupera automáticamente el último tema válido
    /// (o el tema sólido integrado si no hubiera otro).
    /// </summary>
    public AppliedThemeState ResolveTheme(
        ThemeDefinition? candidateTheme,
        SystemEnvironmentSnapshot environment,
        MaterialKind? requestedMaterialOverride = null,
        bool isBackdropMaterialSupportedByRuntime = true)
    {
        ArgumentNullException.ThrowIfNull(environment);

        bool usedFallbackTheme = false;
        ThemeDefinition activeTheme;

        var validation = ThemeValidator.ValidateDefinition(candidateTheme);
        if (validation.IsValid && validation.ValidatedTheme is not null)
        {
            activeTheme = validation.ValidatedTheme;
            _lastValidTheme = activeTheme;
        }
        else
        {
            usedFallbackTheme = true;
            activeTheme = _lastValidTheme;
            string errorSummary = string.Join(",", validation.Errors);
            _logger.Log(
                DiagnosticLogLevel.Warning,
                "Theming",
                "THEME_FALLBACK_RECOVERED",
                $"Se rechazó un tema inválido ({errorSummary}); se recuperó el tema '{activeTheme.Id}'.");
        }

        MaterialKind requestedMaterial = requestedMaterialOverride ?? activeTheme.RequestedMaterial;
        EffectiveMaterialKind effectiveMaterial;
        string statusReasonCode;

        if (environment.IsHighContrastEnabled)
        {
            effectiveMaterial = EffectiveMaterialKind.HighContrastSolid;
            statusReasonCode = "HIGH_CONTRAST_ENFORCED";
        }
        else if (requestedMaterial == MaterialKind.Solid)
        {
            effectiveMaterial = EffectiveMaterialKind.Solid;
            statusReasonCode = usedFallbackTheme ? "FALLBACK_THEME_SOLID" : "REQUESTED_SOLID";
        }
        else if (!environment.AreTransparencyEffectsEnabled)
        {
            effectiveMaterial = EffectiveMaterialKind.Solid;
            statusReasonCode = "SYSTEM_TRANSPARENCY_DISABLED_FALLBACK_SOLID";
        }
        else if (!isBackdropMaterialSupportedByRuntime)
        {
            effectiveMaterial = EffectiveMaterialKind.Solid;
            statusReasonCode = "RUNTIME_BACKDROP_UNSUPPORTED_FALLBACK_SOLID";
        }
        else
        {
            effectiveMaterial = requestedMaterial switch
            {
                MaterialKind.Glass => EffectiveMaterialKind.DesktopAcrylic,
                MaterialKind.Mica => EffectiveMaterialKind.Mica,
                _ => EffectiveMaterialKind.Solid
            };
            statusReasonCode = usedFallbackTheme ? "FALLBACK_THEME_APPLIED" : "REQUESTED_MATERIAL_ACTIVE";
        }

        AnimationProfile effectiveAnimation = !environment.AreAnimationsEnabled
            ? AnimationProfile.Disabled
            : activeTheme.AnimationProfile;

        return new AppliedThemeState(
            Theme: activeTheme,
            RequestedMaterial: requestedMaterial,
            EffectiveMaterial: effectiveMaterial,
            EffectiveAnimationProfile: effectiveAnimation,
            IsHighContrastActive: environment.IsHighContrastEnabled,
            UsedFallbackTheme: usedFallbackTheme,
            StatusReasonCode: statusReasonCode);
    }
}
