using System.Text;
using System.Text.Json;
using Island.Core.Diagnostics;

namespace Island.Application.Diagnostics;

/// <summary>
/// Registro local acotado en memoria y disco que redacta contenido privado por defecto (Sección 2 y 6).
/// </summary>
public sealed class BoundedDiagnosticLogger : IDiagnosticLogger
{
    private readonly object _sync = new();
    private readonly Queue<DiagnosticLogEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly int _maxFileBytes;
    private readonly string? _logFilePath;
    private readonly TimeProvider _timeProvider;
    private bool _includePrivateContent;

    public BoundedDiagnosticLogger(
        string? logFilePath = null,
        int maxEntries = 200,
        int maxFileBytes = 256 * 1024,
        bool includePrivateContent = false,
        TimeProvider? timeProvider = null)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        if (maxFileBytes < 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        }

        _logFilePath = string.IsNullOrWhiteSpace(logFilePath) ? null : Path.GetFullPath(logFilePath);
        _maxEntries = maxEntries;
        _maxFileBytes = maxFileBytes;
        _includePrivateContent = includePrivateContent;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IncludePrivateContent
    {
        get
        {
            lock (_sync)
            {
                return _includePrivateContent;
            }
        }
        set
        {
            lock (_sync)
            {
                _includePrivateContent = value;
            }
        }
    }

    public void Log(
        DiagnosticLogLevel level,
        string category,
        string eventCode,
        string message,
        IReadOnlyDictionary<string, string?>? sensitiveFields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventCode);

        string safeMessage = message ?? string.Empty;
        int redactedCount = 0;

        lock (_sync)
        {
            if (sensitiveFields is { Count: > 0 })
            {
                var builder = new StringBuilder(safeMessage);
                foreach (var kvp in sensitiveFields)
                {
                    if (_includePrivateContent)
                    {
                        builder.Append($" [{kvp.Key}={kvp.Value ?? "null"}]");
                    }
                    else
                    {
                        redactedCount++;
                        builder.Append($" [{kvp.Key}=[REDACTED]]");
                    }
                }

                safeMessage = builder.ToString();
            }

            var entry = new DiagnosticLogEntry(
                TimestampUtc: _timeProvider.GetUtcNow(),
                Level: level,
                Category: category,
                EventCode: eventCode,
                Message: safeMessage,
                RedactedPrivateFieldsCount: redactedCount);

            if (_entries.Count >= _maxEntries)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
            AppendToDiskIfConfigured(entry);
        }
    }

    public IReadOnlyList<DiagnosticLogEntry> GetRecentEntries()
    {
        lock (_sync)
        {
            return _entries.ToArray();
        }
    }

    private void AppendToDiskIfConfigured(DiagnosticLogEntry entry)
    {
        if (_logFilePath is null)
        {
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;
            byte[] bytes = Encoding.UTF8.GetBytes(jsonLine);

            if (File.Exists(_logFilePath))
            {
                var info = new FileInfo(_logFilePath);
                if (info.Length + bytes.Length > _maxFileBytes)
                {
                    string rotatedPath = _logFilePath + ".1";
                    File.Move(_logFilePath, rotatedPath, overwrite: true);
                }
            }

            File.AppendAllBytes(_logFilePath, bytes);
        }
        catch (IOException)
        {
            // Un error de disco en el registro de diagnóstico nunca debe abortar la aplicación.
        }
    }
}
