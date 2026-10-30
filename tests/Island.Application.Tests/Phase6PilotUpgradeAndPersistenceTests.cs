using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Application.Configuration;
using Island.Application.Diagnostics;
using Island.Application.Theming;
using Island.Core.Configuration;
using Island.Core.Diagnostics;
using Island.Core.Theming;
using Xunit;

namespace Island.Application.Tests;

public sealed class Phase6PilotUpgradeAndPersistenceTests
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void UpgradeSimulation_FromV01ToV02_PreservesUserCustomizedSettingsAndCustomThemesWithoutDataLoss()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"IslandPilotUpgradeTest_{Guid.NewGuid():N}");
        try
        {
            string settingsPath = Path.Combine(tempRoot, "config", "settings.json");
            string themesDir = Path.Combine(tempRoot, "themes");
            string logPath = Path.Combine(tempRoot, "logs", "diagnostics.jsonl");

            // 1. Simular instalación v0.1.0.0 donde el usuario personalizó posición, tema personalizado y lista de apps
            var loggerV1 = new BoundedDiagnosticLogger(logPath);
            var storeV1 = new JsonSettingsStore(settingsPath, loggerV1);
            var catalogV1 = new JsonThemeCatalog(themesDir, loggerV1);
            catalogV1.SyncAndLoadFromDisk();

            var customPilotTheme = BuiltInThemes.DefaultGlass with
            {
                Id = "user.pilot.emerald",
                DisplayName = "Pilot Emerald Glass",
                CornerRadius = 20.0,
                Colors = BuiltInThemes.DefaultGlass.Colors with
                {
                    AccentHex = "#FF10B981"
                }
            };
            string customThemePath = Path.Combine(themesDir, "user-pilot-emerald.json");
            File.WriteAllBytes(customThemePath, JsonSerializer.SerializeToUtf8Bytes(customPilotTheme, WriteOptions));

            var customizedSettings = AppSettingsDocument.CreateDefault() with
            {
                SelectedThemeId = "user.pilot.emerald",
                RequestedMaterialOverride = MaterialKind.Glass,
                AnchorHorizontalRatio = 0.65,
                AnchorTopOffsetDip = 18.0,
                AllowedNotificationAppIds =
                [
                    "5319275A.WhatsAppDesktop_cv1g1gvanyjgm",
                    "CustomApp.Pilot_12345"
                ]
            };
            storeV1.SaveAtomically(customizedSettings);

            // 2. Simular actualización en el lugar a v0.2.0.0 sobre el mismo directorio de datos de usuario
            var loggerV2 = new BoundedDiagnosticLogger(logPath);
            var storeV2 = new JsonSettingsStore(settingsPath, loggerV2);
            var loadV2 = storeV2.LoadOrRecover();

            var catalogV2 = new JsonThemeCatalog(themesDir, loggerV2);
            int themesLoadedV2 = catalogV2.SyncAndLoadFromDisk();
            var resolvedThemeV2 = catalogV2.GetByIdOrFallback(loadV2.Settings.SelectedThemeId);

            // Verificar que ninguna preferencia ni tema del usuario fue sobrescrito ni perdido
            Assert.False(loadV2.WasRecoveredFromError);
            Assert.Equal("LOADED_OK", loadV2.DiagnosticCode);
            Assert.Equal("user.pilot.emerald", loadV2.Settings.SelectedThemeId);
            Assert.Equal(MaterialKind.Glass, loadV2.Settings.RequestedMaterialOverride);
            Assert.Equal(0.65, loadV2.Settings.AnchorHorizontalRatio, precision: 3);
            Assert.Equal(18.0, loadV2.Settings.AnchorTopOffsetDip, precision: 3);
            Assert.Equal(2, loadV2.Settings.AllowedNotificationAppIds.Count);

            Assert.Equal(4, themesLoadedV2); // 3 integrados + 1 del usuario
            Assert.Equal("user.pilot.emerald", resolvedThemeV2.Id);
            Assert.Equal("#FF10B981", resolvedThemeV2.Colors.AccentHex);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void UpgradeSimulation_WhenFutureIncompatibleSchemaEncountered_QuarantinesOriginalFileWithoutDeletingUserBackup()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"IslandPilotSchemaTest_{Guid.NewGuid():N}");
        try
        {
            string settingsPath = Path.Combine(tempRoot, "config", "settings.json");
            string logPath = Path.Combine(tempRoot, "logs", "diagnostics.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

            var futureDoc = AppSettingsDocument.CreateDefault() with
            {
                SchemaVersion = 99,
                SelectedThemeId = "future.theme"
            };
            File.WriteAllBytes(settingsPath, JsonSerializer.SerializeToUtf8Bytes(futureDoc, WriteOptions));

            var logger = new BoundedDiagnosticLogger(logPath);
            var store = new JsonSettingsStore(settingsPath, logger);
            var result = store.LoadOrRecover();

            Assert.True(result.WasRecoveredFromError);
            Assert.StartsWith("UNSUPPORTED_SCHEMA_VERSION:99", result.DiagnosticCode);
            Assert.NotNull(result.QuarantinedFilePath);
            Assert.True(File.Exists(result.QuarantinedFilePath!));
            Assert.Contains("future.theme", File.ReadAllText(result.QuarantinedFilePath!));
            Assert.Equal(BuiltInThemes.DefaultSolidThemeId, result.Settings.SelectedThemeId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DiagnosticLogRotation_UnderSustainedPilotUsage_RotatesWithinBudgetAndNeverLeaksPrivateText()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"IslandPilotLogRotationTest_{Guid.NewGuid():N}");
        try
        {
            string logPath = Path.Combine(tempRoot, "logs", "diagnostics.jsonl");
            var logger = new BoundedDiagnosticLogger(
                logFilePath: logPath,
                maxEntries: 50,
                maxFileBytes: 8192,
                includePrivateContent: false);

            for (int i = 0; i < 120; i++)
            {
                logger.Log(
                    DiagnosticLogLevel.Info,
                    "Notifications",
                    "PILOT_MSG_RECEIVED",
                    $"Notificacion procesada #{i}",
                    new Dictionary<string, string?>
                    {
                        ["Sender"] = "Contacto Secreto",
                        ["Body"] = "Texto privado confidencial 12345"
                    });
            }

            Assert.Equal(50, logger.GetRecentEntries().Count);
            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(logPath + ".1"));
            Assert.True(new FileInfo(logPath).Length <= 8192);

            string diskContent = File.ReadAllText(logPath) + File.ReadAllText(logPath + ".1");
            Assert.DoesNotContain("Contacto Secreto", diskContent);
            Assert.DoesNotContain("Texto privado confidencial 12345", diskContent);
            Assert.Contains("[REDACTED]", diskContent);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
