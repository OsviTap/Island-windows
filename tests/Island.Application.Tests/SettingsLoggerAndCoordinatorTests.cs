using Island.Application.Configuration;
using Island.Application.Coordination;
using Island.Application.Diagnostics;
using Island.Application.Theming;
using Island.Core.Configuration;
using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Domain;
using Island.Core.Theming;

namespace Island.Application.Tests;

public sealed class SettingsLoggerAndCoordinatorTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsLoggerAndCoordinatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IslandTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void JsonSettingsStore_QuarantinesCorruptJson_AndRecoversDefaults()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, "{ invalid json content ///");

        var logger = new BoundedDiagnosticLogger();
        var store = new JsonSettingsStore(settingsPath, logger);

        var result = store.LoadOrRecover();

        Assert.True(result.WasRecoveredFromError);
        Assert.NotNull(result.QuarantinedFilePath);
        Assert.True(File.Exists(result.QuarantinedFilePath!));
        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, result.Settings.SchemaVersion);
        Assert.True(File.Exists(settingsPath));
    }

    [Fact]
    public void BoundedDiagnosticLogger_RedactsPrivateFieldsByDefault()
    {
        string logPath = Path.Combine(_tempDir, "diag.jsonl");
        var logger = new BoundedDiagnosticLogger(logFilePath: logPath, includePrivateContent: false);

        logger.Log(
            DiagnosticLogLevel.Info,
            "Notifications",
            "WHATSAPP_TOAST_RECEIVED",
            "Notificación recibida",
            new Dictionary<string, string?>
            {
                ["sender"] = "Juan Perez",
                ["body"] = "Mensaje privado confidencial"
            });

        var entries = logger.GetRecentEntries();
        Assert.Single(entries);
        Assert.Equal(2, entries[0].RedactedPrivateFieldsCount);
        Assert.DoesNotContain("Juan Perez", entries[0].Message);
        Assert.DoesNotContain("Mensaje privado confidencial", entries[0].Message);
        Assert.Contains("[REDACTED]", entries[0].Message);

        string diskContent = File.ReadAllText(logPath);
        Assert.DoesNotContain("Juan Perez", diskContent);
        Assert.DoesNotContain("Mensaje privado confidencial", diskContent);
    }

    [Fact]
    public void Coordinator_ChangingTheme_DoesNotResetMediaSelectionOrCapabilities()
    {
        var logger = new BoundedDiagnosticLogger();
        var themeService = new ThemeService(logger);
        var env = new SystemEnvironmentSnapshot("10.0.26300.0", 26300, "X64", false, "", true, true, DateTimeOffset.UtcNow);
        var coordinator = new IslandStructuralCoordinator(themeService, logger, env);

        coordinator.SelectMediaSource("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");
        coordinator.ReportModuleCapability(new IntegrationCapabilityReport(
            IntegrationModuleId.Media,
            IntegrationCapabilityState.Available,
            "GSMTC_READY",
            "Sesión activa",
            ["Play", "Pause", "Next"],
            DateTimeOffset.UtcNow));

        // Un fallo en notificaciones no debe inutilizar multimedia
        coordinator.ReportModuleCapability(new IntegrationCapabilityReport(
            IntegrationModuleId.Notifications,
            IntegrationCapabilityState.Faulted,
            "LISTENER_ERROR",
            "Error simulado de prueba",
            [],
            DateTimeOffset.UtcNow));

        // Cambiar el tema a Glass
        coordinator.ApplyTheme(BuiltInThemes.DefaultGlass, env);

        var capabilities = coordinator.GetCapabilitiesSnapshot();
        Assert.Equal("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", coordinator.SelectedMediaSourceId);
        Assert.True(capabilities[IntegrationModuleId.Media].IsOperational);
        Assert.Equal(IntegrationCapabilityState.Faulted, capabilities[IntegrationModuleId.Notifications].State);
    }

    [Fact]
    public void Coordinator_UserInteraction_BlocksAutomaticEventExpansion()
    {
        var logger = new BoundedDiagnosticLogger();
        var themeService = new ThemeService(logger);
        var env = new SystemEnvironmentSnapshot("10.0.26300.0", 26300, "X64", false, "", true, true, DateTimeOffset.UtcNow);
        var coordinator = new IslandStructuralCoordinator(themeService, logger, env);

        coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse);

        bool applied = coordinator.TryPresentAutomaticEvent(
            IslandContentKind.MediaSummary,
            "TRACK_CHANGED",
            out var stateAfterEvent);

        Assert.False(applied);
        Assert.Equal(IslandContentKind.ControlCenter, stateAfterEvent.Content);
        Assert.Equal(IslandInteractionState.ActiveInUse, stateAfterEvent.Interaction);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
