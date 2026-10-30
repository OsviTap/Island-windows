namespace Island.Core.Contracts;

/// <summary>
/// Identificadores de los módulos internos del sistema.
/// </summary>
public enum IntegrationModuleId
{
    Environment = 0,
    Windowing = 1,
    Media = 2,
    Notifications = 3,
    Audio = 4
}

/// <summary>
/// Estado real declarado por cada integración (Sección 3).
/// Un fallo en un módulo no debe inutilizar los demás.
/// </summary>
public enum IntegrationCapabilityState
{
    Available = 0,
    PermissionRequired = 1,
    PermissionDenied = 2,
    PartiallySupported = 3,
    Disconnected = 4,
    Faulted = 5
}

/// <summary>
/// Informe inmutable de capacidad de un módulo de integración.
/// </summary>
public sealed record IntegrationCapabilityReport(
    IntegrationModuleId ModuleId,
    IntegrationCapabilityState State,
    string StatusCode,
    string DiagnosticDetail,
    IReadOnlyList<string> SupportedOperations,
    DateTimeOffset ReportedAtUtc)
{
    public bool IsOperational =>
        State is IntegrationCapabilityState.Available or IntegrationCapabilityState.PartiallySupported;
}

/// <summary>
/// Instantánea de la identidad de paquete MSIX del proceso en ejecución.
/// </summary>
public sealed record PackageIdentitySnapshot(
    bool HasPackageIdentity,
    string PackageFullName,
    int Win32ErrorCode,
    DateTimeOffset CheckedAtUtc);

/// <summary>
/// Contrato para inspeccionar si el proceso cuenta con identidad de paquete MSIX real.
/// </summary>
public interface IPackageIdentityProvider
{
    PackageIdentitySnapshot GetCurrentIdentity();
}

/// <summary>
/// Instantánea de las capacidades visuales y de accesibilidad del entorno operativo.
/// </summary>
public sealed record SystemEnvironmentSnapshot(
    string OsVersionString,
    int OsBuildNumber,
    string ProcessArchitecture,
    bool IsHighContrastEnabled,
    string HighContrastSchemeName,
    bool AreTransparencyEffectsEnabled,
    bool AreAnimationsEnabled,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Contrato para consultar el estado real del sistema sin acoplar el dominio a APIs de Windows.
/// </summary>
public interface ISystemEnvironmentInspector
{
    SystemEnvironmentSnapshot CaptureSnapshot();
}
