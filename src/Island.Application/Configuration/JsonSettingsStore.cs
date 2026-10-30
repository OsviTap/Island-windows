using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Configuration;
using Island.Core.Diagnostics;

namespace Island.Application.Configuration;

/// <summary>
/// Almacén de configuración JSON versionado con validación de esquema,
/// escritura atómica y recuperación ante corrupción de archivo (Sección 2 y 11).
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _settingsFilePath;
    private readonly IDiagnosticLogger _logger;

    public JsonSettingsStore(string settingsFilePath, IDiagnosticLogger logger)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            throw new ArgumentException("La ruta de configuración no puede estar vacía.", nameof(settingsFilePath));
        }

        _settingsFilePath = Path.GetFullPath(settingsFilePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string SettingsFilePath => _settingsFilePath;

    public SettingsLoadResult LoadOrRecover()
    {
        if (!File.Exists(_settingsFilePath))
        {
            var defaultDoc = AppSettingsDocument.CreateDefault();
            SaveAtomically(defaultDoc);
            _logger.Log(
                DiagnosticLogLevel.Info,
                "Configuration",
                "SETTINGS_CREATED_DEFAULT",
                "No existía archivo de configuración previo; se inicializó con valores predeterminados.");

            return new SettingsLoadResult(
                Settings: defaultDoc,
                WasRecoveredFromError: false,
                DiagnosticCode: "CREATED_DEFAULT",
                QuarantinedFilePath: null);
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(_settingsFilePath);
            var parsed = JsonSerializer.Deserialize<AppSettingsDocument>(bytes, SerializerOptions);
            string validationError = string.Empty;
            if (parsed is null || !IsValid(parsed, out validationError))
            {
                string reason = parsed is null ? "NULL_DOCUMENT" : validationError;
                return QuarantineAndRecover(reason);
            }

            return new SettingsLoadResult(
                Settings: parsed,
                WasRecoveredFromError: false,
                DiagnosticCode: "LOADED_OK",
                QuarantinedFilePath: null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return QuarantineAndRecover($"DESERIALIZE_ERROR:{ex.GetType().Name}");
        }
    }

    public void SaveAtomically(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsValid(document, out string validationError))
        {
            throw new InvalidOperationException($"No se puede guardar una configuración inválida: {validationError}");
        }

        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempFilePath = $"{_settingsFilePath}.{Guid.NewGuid():N}.tmp";
        byte[] utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);

        using (var fileStream = new FileStream(
            tempFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough))
        {
            fileStream.Write(utf8Bytes);
            fileStream.Flush(flushToDisk: true);
        }

        File.Move(tempFilePath, _settingsFilePath, overwrite: true);
    }

    public static bool IsValid(AppSettingsDocument document, out string errorCode)
    {
        if (document.SchemaVersion != AppSettingsDocument.CurrentSchemaVersion)
        {
            errorCode = $"UNSUPPORTED_SCHEMA_VERSION:{document.SchemaVersion}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.SelectedThemeId) || document.SelectedThemeId.Length > 64)
        {
            errorCode = "INVALID_SELECTED_THEME_ID";
            return false;
        }

        if (document.RequestedMaterialOverride.HasValue && !Enum.IsDefined(document.RequestedMaterialOverride.Value))
        {
            errorCode = "INVALID_MATERIAL_OVERRIDE";
            return false;
        }

        if (!Enum.IsDefined(document.ColorScheme) || !Enum.IsDefined(document.Density) || !Enum.IsDefined(document.AnimationProfile))
        {
            errorCode = "INVALID_ENUM_VALUE";
            return false;
        }

        if (double.IsNaN(document.AnchorHorizontalRatio) || document.AnchorHorizontalRatio < 0.05 || document.AnchorHorizontalRatio > 0.95)
        {
            errorCode = "ANCHOR_HORIZONTAL_RATIO_OUT_OF_RANGE";
            return false;
        }

        if (double.IsNaN(document.AnchorTopOffsetDip) || document.AnchorTopOffsetDip < 0.0 || document.AnchorTopOffsetDip > 120.0)
        {
            errorCode = "ANCHOR_TOP_OFFSET_OUT_OF_RANGE";
            return false;
        }

        if (document.EnabledModules is null || document.AllowedNotificationAppIds is null)
        {
            errorCode = "NULL_COLLECTIONS";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }

    private SettingsLoadResult QuarantineAndRecover(string diagnosticCode)
    {
        string? quarantinedPath = null;
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                quarantinedPath = $"{_settingsFilePath}.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
                File.Move(_settingsFilePath, quarantinedPath, overwrite: true);
            }
        }
        catch (IOException)
        {
            quarantinedPath = null;
        }

        var recovered = AppSettingsDocument.CreateDefault();
        SaveAtomically(recovered);

        _logger.Log(
            DiagnosticLogLevel.Warning,
            "Configuration",
            "SETTINGS_CORRUPT_RECOVERED",
            $"Se detectó configuración dañada ({diagnosticCode}). Se aisló el archivo y se restauró la configuración predeterminada.");

        return new SettingsLoadResult(
            Settings: recovered,
            WasRecoveredFromError: true,
            DiagnosticCode: diagnosticCode,
            QuarantinedFilePath: quarantinedPath);
    }
}
