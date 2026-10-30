using System.Text;
using Island.Application.Theming;
using Island.Core.Contracts;
using Island.Core.Theming;
using Island.Application.Diagnostics;

namespace Island.Application.Tests;

public sealed class ThemeValidatorAndServiceTests
{
    private static SystemEnvironmentSnapshot CreateEnvironment(
        bool highContrast = false,
        bool transparencyEnabled = true,
        bool animationsEnabled = true) =>
        new(
            OsVersionString: "10.0.26300.0",
            OsBuildNumber: 26300,
            ProcessArchitecture: "X64",
            IsHighContrastEnabled: highContrast,
            HighContrastSchemeName: highContrast ? "High Contrast Black" : string.Empty,
            AreTransparencyEffectsEnabled: transparencyEnabled,
            AreAnimationsEnabled: animationsEnabled,
            CapturedAtUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void BuiltInThemes_PassDeclarativeValidation()
    {
        var solidResult = ThemeValidator.ValidateDefinition(BuiltInThemes.DefaultSolid);
        var glassResult = ThemeValidator.ValidateDefinition(BuiltInThemes.DefaultGlass);

        Assert.True(solidResult.IsValid);
        Assert.True(glassResult.IsValid);
    }

    [Theory]
    [InlineData("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">")]
    [InlineData("{\"id\": \"bad\", \"plugin\": \"custom.dll\"}")]
    [InlineData("{\"id\": \"bad\", \"cmd\": \"powershell -enc\"}")]
    public void ValidateJsonBytes_RejectsExecutableOrXamlPayloads(string rawPayload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(rawPayload);
        var result = ThemeValidator.ValidateJsonBytes(bytes);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("THEME_FORBIDDEN_EXECUTABLE_OR_XAML_TOKEN:", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveTheme_SeparatesRequestedAndEffectiveMaterial_WhenTransparencyDisabled()
    {
        var logger = new BoundedDiagnosticLogger();
        var service = new ThemeService(logger);
        var envWithoutTransparency = CreateEnvironment(highContrast: false, transparencyEnabled: false);

        var state = service.ResolveTheme(BuiltInThemes.DefaultGlass, envWithoutTransparency);

        Assert.Equal(MaterialKind.Glass, state.RequestedMaterial);
        Assert.Equal(EffectiveMaterialKind.Solid, state.EffectiveMaterial);
        Assert.Equal("SYSTEM_TRANSPARENCY_DISABLED_FALLBACK_SOLID", state.StatusReasonCode);
    }

    [Fact]
    public void ResolveTheme_EnforcesHighContrast_EvenWhenGlassRequested()
    {
        var logger = new BoundedDiagnosticLogger();
        var service = new ThemeService(logger);
        var hcEnv = CreateEnvironment(highContrast: true, transparencyEnabled: true);

        var state = service.ResolveTheme(BuiltInThemes.DefaultGlass, hcEnv);

        Assert.Equal(MaterialKind.Glass, state.RequestedMaterial);
        Assert.Equal(EffectiveMaterialKind.HighContrastSolid, state.EffectiveMaterial);
        Assert.True(state.IsHighContrastActive);
    }

    [Fact]
    public void ResolveTheme_RecoversLastValidTheme_WhenCandidateIsInvalid()
    {
        var logger = new BoundedDiagnosticLogger();
        var service = new ThemeService(logger);
        var env = CreateEnvironment();

        // Primero aplicamos el tema Glass válido
        service.ResolveTheme(BuiltInThemes.DefaultGlass, env);

        // Luego intentamos aplicar un tema con radio de esquina fuera de límites
        var invalidTheme = BuiltInThemes.DefaultSolid with { CornerRadius = 999.0 };
        var recoveredState = service.ResolveTheme(invalidTheme, env);

        Assert.True(recoveredState.UsedFallbackTheme);
        Assert.Equal(BuiltInThemes.DefaultGlassThemeId, recoveredState.Theme.Id);
    }
}
