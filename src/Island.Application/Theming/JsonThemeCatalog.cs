using System.Text.Json;
using System.Text.Json.Serialization;
using Island.Core.Diagnostics;
using Island.Core.Theming;

namespace Island.Application.Theming;

/// <summary>
/// Catálogo declarativo de temas JSON (Secciones 2, 4 y 5).
/// Exporta los temas de referencia a disco, carga archivos .json del directorio de temas
/// y valida estrictamente cada archivo con ThemeValidator (rechazando >64 KB, esquemas desconocidos o valores fuera de rango).
/// </summary>
public sealed class JsonThemeCatalog
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _themesDirectoryPath;
    private readonly IDiagnosticLogger _logger;
    private readonly Dictionary<string, ThemeDefinition> _themesById = new(StringComparer.OrdinalIgnoreCase);

    public JsonThemeCatalog(string themesDirectoryPath, IDiagnosticLogger logger)
    {
        if (string.IsNullOrWhiteSpace(themesDirectoryPath))
        {
            throw new ArgumentException("El directorio de temas no puede estar vacío.", nameof(themesDirectoryPath));
        }

        _themesDirectoryPath = Path.GetFullPath(themesDirectoryPath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        foreach (var builtIn in BuiltInThemes.All)
        {
            _themesById[builtIn.Id] = builtIn;
        }
    }

    public string ThemesDirectoryPath => _themesDirectoryPath;

    public IReadOnlyList<ThemeDefinition> GetAvailableThemes() =>
        _themesById.Values.OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToList();

    public ThemeDefinition GetByIdOrFallback(string? themeId)
    {
        if (!string.IsNullOrWhiteSpace(themeId) && _themesById.TryGetValue(themeId, out var theme))
        {
            return theme;
        }

        return BuiltInThemes.DefaultSolid;
    }

    /// <summary>
    /// Asegura que los temas declarativos integrados estén exportados en disco como referencia
    /// y carga cualquier tema adicional válido del directorio.
    /// </summary>
    public int SyncAndLoadFromDisk()
    {
        Directory.CreateDirectory(_themesDirectoryPath);

        ExportThemeFileIfMissing("solid-dark.json", BuiltInThemes.DefaultSolid);
        ExportThemeFileIfMissing("glass-dark.json", BuiltInThemes.DefaultGlass);
        ExportThemeFileIfMissing("mica-dark.json", BuiltInThemes.DefaultMica);

        int loadedValidCount = 0;

        foreach (string filePath in Directory.EnumerateFiles(_themesDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > ThemeGeometryLimits.MaxThemeFileBytes)
                {
                    _logger.Log(
                        DiagnosticLogLevel.Warning,
                        "Theming",
                        "THEME_FILE_TOO_LARGE",
                        $"Se ignoró el archivo de tema '{Path.GetFileName(filePath)}' por exceder 64 KB ({fileInfo.Length} bytes).");
                    continue;
                }

                byte[] rawBytes = File.ReadAllBytes(filePath);
                var validation = ThemeValidator.ValidateJsonBytes(rawBytes);
                if (!validation.IsValid || validation.ValidatedTheme is null)
                {
                    string errors = string.Join(",", validation.Errors);
                    _logger.Log(
                        DiagnosticLogLevel.Warning,
                        "Theming",
                        "THEME_FILE_INVALID_IGNORED",
                        $"Se rechazó el archivo de tema '{Path.GetFileName(filePath)}': {errors}.");
                    continue;
                }

                _themesById[validation.ValidatedTheme.Id] = validation.ValidatedTheme;
                loadedValidCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Log(
                    DiagnosticLogLevel.Warning,
                    "Theming",
                    "THEME_FILE_READ_ERROR",
                    $"No se pudo leer el archivo de tema '{Path.GetFileName(filePath)}': {ex.GetType().Name}.");
            }
        }

        return loadedValidCount;
    }

    private void ExportThemeFileIfMissing(string fileName, ThemeDefinition theme)
    {
        string path = Path.Combine(_themesDirectoryPath, fileName);
        try
        {
            // Mantenemos los 3 archivos de referencia sincronizados con las definiciones integradas actuales
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(theme, WriteOptions);
            File.WriteAllBytes(path, jsonBytes);
        }
        catch (IOException)
        {
        }
    }
}
