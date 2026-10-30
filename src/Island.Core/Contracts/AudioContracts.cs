namespace Island.Core.Contracts;

/// <summary>
/// Instantánea de una sesión individual de audio WASAPI (IAudioSessionControl2).
/// Se modela por separado de GSMTC porque Windows no garantiza correspondencia 1:1 (Sección 6).
/// </summary>
public sealed record AudioProcessSessionInfo(
    uint ProcessId,
    string SessionIdentifier,
    string DisplayName,
    float VolumeScalar,
    bool IsMuted,
    bool IsActive);

/// <summary>
/// Estado del dispositivo de salida de audio predeterminado y sus sesiones por proceso.
/// </summary>
public sealed record AudioEndpointSnapshot(
    IntegrationCapabilityReport CapabilityReport,
    bool HasDefaultRenderDevice,
    float MasterVolumeScalar,
    bool IsMasterMuted,
    IReadOnlyList<AudioProcessSessionInfo> ProcessSessions,
    DateTimeOffset CapturedAtUtc);

public interface IAudioEndpointInspector
{
    AudioEndpointSnapshot CaptureAudioSnapshot();
    bool TrySetMasterVolume(float volumeScalar);
    bool TrySetMasterMute(bool isMuted);
}
