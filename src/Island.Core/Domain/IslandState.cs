namespace Island.Core.Domain;

/// <summary>
/// Dimensión 1: Huella geométrica y visibilidad de la isla en el escritorio.
/// </summary>
public enum IslandVisibility
{
    Hidden = 0,
    Compact = 1,
    Expanded = 2
}

/// <summary>
/// Dimensión 2: Estado de interacción del usuario con la isla.
/// </summary>
public enum IslandInteractionState
{
    Passive = 0,
    ActiveInUse = 1,
    PinnedOpen = 2
}

/// <summary>
/// Dimensión 3: Tipo de contenido activo representado en la isla.
/// </summary>
public enum IslandContentKind
{
    Idle = 0,
    MediaSummary = 1,
    NotificationToast = 2,
    ControlCenter = 3
}

/// <summary>
/// Dimensión 4: Política operativa vigente para las transiciones y privacidad.
/// </summary>
public enum IslandPolicyMode
{
    Normal = 0,
    Paused = 1,
    Private = 2
}

/// <summary>
/// Estado inmutable multidimensional que gobierna qué muestra la interfaz.
/// Ninguna integración modifica la ventana directamente; el coordinador produce este estado.
/// </summary>
public sealed record IslandStateSnapshot(
    IslandVisibility Visibility,
    IslandInteractionState Interaction,
    IslandContentKind Content,
    IslandPolicyMode Policy,
    string ActiveReasonCode,
    DateTimeOffset UpdatedAtUtc)
{
    public static IslandStateSnapshot CreateInitial(DateTimeOffset nowUtc) =>
        new(
            Visibility: IslandVisibility.Compact,
            Interaction: IslandInteractionState.Passive,
            Content: IslandContentKind.Idle,
            Policy: IslandPolicyMode.Normal,
            ActiveReasonCode: "INITIAL_STARTUP",
            UpdatedAtUtc: nowUtc);

    /// <summary>
    /// Indica si la isla está siendo utilizada o fijada por el usuario, lo cual bloquea
    /// contracciones o reemplazos automáticos provocados por eventos de fondo.
    /// </summary>
    public bool IsUserProtected =>
        Interaction is IslandInteractionState.ActiveInUse or IslandInteractionState.PinnedOpen;
}
