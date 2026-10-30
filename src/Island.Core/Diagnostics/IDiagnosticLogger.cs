namespace Island.Core.Diagnostics;

public enum DiagnosticLogLevel
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>
/// Entrada estructurada de diagnóstico local.
/// Por diseño no almacena contenido privado de notificaciones ni datos personales.
/// </summary>
public sealed record DiagnosticLogEntry(
    DateTimeOffset TimestampUtc,
    DiagnosticLogLevel Level,
    string Category,
    string EventCode,
    string Message,
    int RedactedPrivateFieldsCount);

/// <summary>
/// Contrato de registro local acotado y seguro en privacidad.
/// </summary>
public interface IDiagnosticLogger
{
    void Log(
        DiagnosticLogLevel level,
        string category,
        string eventCode,
        string message,
        IReadOnlyDictionary<string, string?>? sensitiveFields = null);

    IReadOnlyList<DiagnosticLogEntry> GetRecentEntries();
}
