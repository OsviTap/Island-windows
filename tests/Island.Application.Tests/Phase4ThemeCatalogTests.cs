using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Application.Diagnostics;
using Island.Application.Theming;
using Island.Core.Theming;
using Xunit;

namespace Island.Application.Tests;

public sealed class Phase4ThemeCatalogTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void SyncAndLoadFromDisk_ExportsBuiltInThemes_AndLoadsValidCustomJsonTheme()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"IslandThemeCatalogTest_{Guid.NewGuid():N}");
        try
        {
            string logPath = Path.Combine(tempDir, "logs", "diag.jsonl");
            string themesDir = Path.Combine(tempDir, "themes");
            Directory.CreateDirectory(themesDir);

            var customTheme = BuiltInThemes.DefaultGlass with
            {
                Id = "custom.ocean.glass",
                DisplayName = "Ocean Glass Custom",
                Colors = BuiltInThemes.DefaultGlass.Colors with
                {
                    AccentHex = "#FF06B6D4"
                }
            };

            File.WriteAllBytes(
                Path.Combine(themesDir, "custom-ocean.json"),
                JsonSerializer.SerializeToUtf8Bytes(customTheme, WriteOptions));

            var logger = new BoundedDiagnosticLogger(logPath);
            var catalog = new JsonThemeCatalog(themesDir, logger);

            int loaded = catalog.SyncAndLoadFromDisk();

            Assert.Equal(4, loaded); // 3 exportados integrados + 1 personalizado
            Assert.True(File.Exists(Path.Combine(themesDir, "solid-dark.json")));
            Assert.True(File.Exists(Path.Combine(themesDir, "glass-dark.json")));
            Assert.True(File.Exists(Path.Combine(themesDir, "mica-dark.json")));

            var resolved = catalog.GetByIdOrFallback("custom.ocean.glass");
            Assert.Equal("custom.ocean.glass", resolved.Id);
            Assert.Equal("#FF06B6D4", resolved.Colors.AccentHex);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void SyncAndLoadFromDisk_RejectsInvalidAndOversizedJsonThemes_WhileKeepingValidThemes()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"IslandThemeCatalogInvalidTest_{Guid.NewGuid():N}");
        try
        {
            string logPath = Path.Combine(tempDir, "logs", "diag.jsonl");
            string themesDir = Path.Combine(tempDir, "themes");
            Directory.CreateDirectory(themesDir);

            // 1. Archivo JSON corrupto
            File.WriteAllText(Path.Combine(themesDir, "corrupt.json"), "{ invalid json ", Encoding.UTF8);

            // 2. Archivo JSON con esquema inválido (radio de esquina fuera de límites)
            var badGeometryTheme = BuiltInThemes.DefaultSolid with
            {
                Id = "bad.geometry",
                CornerRadius = 999.0
            };
            File.WriteAllBytes(
                Path.Combine(themesDir, "bad-geometry.json"),
                JsonSerializer.SerializeToUtf8Bytes(badGeometryTheme, WriteOptions));

            // 3. Archivo que excede el límite de 64 KB
            byte[] oversized = new byte[ThemeGeometryLimits.MaxThemeFileBytes + 1024];
            Array.Fill(oversized, (byte)' ');
            File.WriteAllBytes(Path.Combine(themesDir, "oversized.json"), oversized);

            var logger = new BoundedDiagnosticLogger(logPath);
            var catalog = new JsonThemeCatalog(themesDir, logger);

            int loaded = catalog.SyncAndLoadFromDisk();

            // Solo los 3 temas integrados válidos deben haberse cargado
            Assert.Equal(3, loaded);
            var fallback = catalog.GetByIdOrFallback("bad.geometry");
            Assert.Equal(BuiltInThemes.DefaultSolidThemeId, fallback.Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void BuiltInThemes_AllThreeThemesPassStrictValidation()
    {
        Assert.Equal(3, BuiltInThemes.All.Count);
        foreach (var theme in BuiltInThemes.All)
        {
            var result = ThemeValidator.ValidateDefinition(theme);
            Assert.True(result.IsValid, $"El tema integrado '{theme.Id}' fallo la validacion: {string.Join(", ", result.Errors)}");
        }
    }
}
