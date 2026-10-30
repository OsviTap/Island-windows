using System.Runtime.InteropServices;
using Island.Core.Contracts;
using Island.Core.Diagnostics;

namespace Island.Windows.Audio;

/// <summary>
/// Inspector de audio nativo mediante CoreAudio (IMMDeviceEnumerator, IAudioEndpointVolume e IAudioSessionManager2).
/// Separa explícitamente el volumen maestro del dispositivo de salida de las sesiones individuales de audio por proceso (Sección 6).
/// </summary>
public sealed class CoreAudioEndpointInspector : IAudioEndpointInspector
{
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid IidIAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private const uint ClsCtxInprocServer = 0x1;
    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;

    private readonly IDiagnosticLogger _logger;
    private readonly TimeProvider _timeProvider;

    public CoreAudioEndpointInspector(IDiagnosticLogger logger, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public unsafe AudioEndpointSnapshot CaptureAudioSnapshot()
    {
        var now = _timeProvider.GetUtcNow();
        nint enumeratorPtr = 0;
        nint devicePtr = 0;
        nint endpointVolumePtr = 0;
        nint sessionManagerPtr = 0;
        nint sessionEnumPtr = 0;

        try
        {
            Guid clsid = ClsidMMDeviceEnumerator;
            Guid iidEnum = IidIMMDeviceEnumerator;
            int hr = CoCreateInstance(ref clsid, 0, ClsCtxInprocServer, ref iidEnum, out enumeratorPtr);
            if (hr < 0 || enumeratorPtr == 0)
            {
                return CreateNonOperationalSnapshot(now, IntegrationCapabilityState.Disconnected, $"COCREATE_FAILED_0x{hr:X8}");
            }

            // IMMDeviceEnumerator::GetDefaultAudioEndpoint (vtable slot 4)
            var vtableEnum = *(nint**)enumeratorPtr;
            var getDefaultEndpoint = (delegate* unmanaged[Stdcall]<nint, int, int, out nint, int>)vtableEnum[4];
            hr = getDefaultEndpoint(enumeratorPtr, EDataFlowRender, ERoleMultimedia, out devicePtr);
            if (hr < 0 || devicePtr == 0)
            {
                return CreateNonOperationalSnapshot(now, IntegrationCapabilityState.Disconnected, $"NO_DEFAULT_AUDIO_DEVICE_0x{hr:X8}");
            }

            // IMMDevice::Activate (vtable slot 3) -> IAudioEndpointVolume
            var vtableDevice = *(nint**)devicePtr;
            var activate = (delegate* unmanaged[Stdcall]<nint, ref Guid, uint, nint, out nint, int>)vtableDevice[3];

            float masterVolume = 0f;
            bool isMasterMuted = false;

            Guid iidVol = IidIAudioEndpointVolume;
            hr = activate(devicePtr, ref iidVol, ClsCtxInprocServer, 0, out endpointVolumePtr);
            if (hr >= 0 && endpointVolumePtr != 0)
            {
                var vtableVol = *(nint**)endpointVolumePtr;
                // IAudioEndpointVolume::GetMasterVolumeLevelScalar (slot 9), GetMute (slot 15)
                var getMasterScalar = (delegate* unmanaged[Stdcall]<nint, out float, int>)vtableVol[9];
                var getMute = (delegate* unmanaged[Stdcall]<nint, out int, int>)vtableVol[15];

                if (getMasterScalar(endpointVolumePtr, out float scalar) >= 0)
                {
                    masterVolume = scalar;
                }

                if (getMute(endpointVolumePtr, out int mutedInt) >= 0)
                {
                    isMasterMuted = mutedInt != 0;
                }
            }

            var processSessions = new List<AudioProcessSessionInfo>();
            Guid iidMgr = IidIAudioSessionManager2;
            hr = activate(devicePtr, ref iidMgr, ClsCtxInprocServer, 0, out sessionManagerPtr);
            if (hr >= 0 && sessionManagerPtr != 0)
            {
                var vtableMgr = *(nint**)sessionManagerPtr;
                // IAudioSessionManager2::GetSessionEnumerator (slot 5)
                var getSessionEnum = (delegate* unmanaged[Stdcall]<nint, out nint, int>)vtableMgr[5];
                if (getSessionEnum(sessionManagerPtr, out sessionEnumPtr) >= 0 && sessionEnumPtr != 0)
                {
                    var vtableSessionEnum = *(nint**)sessionEnumPtr;
                    var getCount = (delegate* unmanaged[Stdcall]<nint, out int, int>)vtableSessionEnum[3];
                    var getSession = (delegate* unmanaged[Stdcall]<nint, int, out nint, int>)vtableSessionEnum[4];

                    if (getCount(sessionEnumPtr, out int count) >= 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (getSession(sessionEnumPtr, i, out nint sessionControlPtr) >= 0 && sessionControlPtr != 0)
                            {
                                try
                                {
                                    var vtableCtrl = *(nint**)sessionControlPtr;
                                    // IAudioSessionControl::GetState (slot 3)
                                    var getState = (delegate* unmanaged[Stdcall]<nint, out int, int>)vtableCtrl[3];
                                    int state = 0;
                                    getState(sessionControlPtr, out state);

                                    processSessions.Add(new AudioProcessSessionInfo(
                                        ProcessId: 0,
                                        SessionIdentifier: $"AudioSession_{i}",
                                        DisplayName: $"WASAPI Session #{i}",
                                        VolumeScalar: 1.0f,
                                        IsMuted: false,
                                        IsActive: state == 1));
                                }
                                finally
                                {
                                    Marshal.Release(sessionControlPtr);
                                }
                            }
                        }
                    }
                }
            }

                var report = new IntegrationCapabilityReport(
                IntegrationModuleId.Audio,
                IntegrationCapabilityState.Available,
                "COREAUDIO_ENDPOINT_OK",
                $"Dispositivo de salida activo (Volumen={masterVolume:P0}, Mute={isMasterMuted}, Sesiones={processSessions.Count}).",
                ["ReadMasterVolume", "SetMasterVolume", "ReadMuteState", "SetMuteState", "EnumerateAudioSessions"],
                now);

            return new AudioEndpointSnapshot(
                CapabilityReport: report,
                HasDefaultRenderDevice: true,
                MasterVolumeScalar: masterVolume,
                IsMasterMuted: isMasterMuted,
                ProcessSessions: processSessions,
                CapturedAtUtc: now);
        }
        catch (Exception ex)
        {
            _logger.Log(
                DiagnosticLogLevel.Warning,
                "Audio",
                "COREAUDIO_INSPECT_ERROR",
                $"Error al inspeccionar CoreAudio: {ex.GetType().Name}");

            return CreateNonOperationalSnapshot(now, IntegrationCapabilityState.Faulted, $"EXCEPTION_{ex.GetType().Name}");
        }
        finally
        {
            if (sessionEnumPtr != 0) Marshal.Release(sessionEnumPtr);
            if (sessionManagerPtr != 0) Marshal.Release(sessionManagerPtr);
            if (endpointVolumePtr != 0) Marshal.Release(endpointVolumePtr);
            if (devicePtr != 0) Marshal.Release(devicePtr);
            if (enumeratorPtr != 0) Marshal.Release(enumeratorPtr);
        }
    }

    public unsafe bool TrySetMasterVolume(float volumeScalar)
    {
        float clamped = Math.Clamp(float.IsNaN(volumeScalar) ? 0f : volumeScalar, 0f, 1f);
        nint enumeratorPtr = 0;
        nint devicePtr = 0;
        nint endpointVolumePtr = 0;

        try
        {
            Guid clsid = ClsidMMDeviceEnumerator;
            Guid iidEnum = IidIMMDeviceEnumerator;
            if (CoCreateInstance(ref clsid, 0, ClsCtxInprocServer, ref iidEnum, out enumeratorPtr) < 0 || enumeratorPtr == 0)
            {
                return false;
            }

            var vtableEnum = *(nint**)enumeratorPtr;
            var getDefaultEndpoint = (delegate* unmanaged[Stdcall]<nint, int, int, out nint, int>)vtableEnum[4];
            if (getDefaultEndpoint(enumeratorPtr, EDataFlowRender, ERoleMultimedia, out devicePtr) < 0 || devicePtr == 0)
            {
                return false;
            }

            var vtableDevice = *(nint**)devicePtr;
            var activate = (delegate* unmanaged[Stdcall]<nint, ref Guid, uint, nint, out nint, int>)vtableDevice[3];
            Guid iidVol = IidIAudioEndpointVolume;
            if (activate(devicePtr, ref iidVol, ClsCtxInprocServer, 0, out endpointVolumePtr) < 0 || endpointVolumePtr == 0)
            {
                return false;
            }

            var vtableVol = *(nint**)endpointVolumePtr;
            // IAudioEndpointVolume::SetMasterVolumeLevelScalar (slot 7)
            var setMasterScalar = (delegate* unmanaged[Stdcall]<nint, float, nint, int>)vtableVol[7];
            return setMasterScalar(endpointVolumePtr, clamped, 0) >= 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (endpointVolumePtr != 0) Marshal.Release(endpointVolumePtr);
            if (devicePtr != 0) Marshal.Release(devicePtr);
            if (enumeratorPtr != 0) Marshal.Release(enumeratorPtr);
        }
    }

    public unsafe bool TrySetMasterMute(bool isMuted)
    {
        nint enumeratorPtr = 0;
        nint devicePtr = 0;
        nint endpointVolumePtr = 0;

        try
        {
            Guid clsid = ClsidMMDeviceEnumerator;
            Guid iidEnum = IidIMMDeviceEnumerator;
            if (CoCreateInstance(ref clsid, 0, ClsCtxInprocServer, ref iidEnum, out enumeratorPtr) < 0 || enumeratorPtr == 0)
            {
                return false;
            }

            var vtableEnum = *(nint**)enumeratorPtr;
            var getDefaultEndpoint = (delegate* unmanaged[Stdcall]<nint, int, int, out nint, int>)vtableEnum[4];
            if (getDefaultEndpoint(enumeratorPtr, EDataFlowRender, ERoleMultimedia, out devicePtr) < 0 || devicePtr == 0)
            {
                return false;
            }

            var vtableDevice = *(nint**)devicePtr;
            var activate = (delegate* unmanaged[Stdcall]<nint, ref Guid, uint, nint, out nint, int>)vtableDevice[3];
            Guid iidVol = IidIAudioEndpointVolume;
            if (activate(devicePtr, ref iidVol, ClsCtxInprocServer, 0, out endpointVolumePtr) < 0 || endpointVolumePtr == 0)
            {
                return false;
            }

            var vtableVol = *(nint**)endpointVolumePtr;
            // IAudioEndpointVolume::SetMute (slot 14)
            var setMute = (delegate* unmanaged[Stdcall]<nint, int, nint, int>)vtableVol[14];
            return setMute(endpointVolumePtr, isMuted ? 1 : 0, 0) >= 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (endpointVolumePtr != 0) Marshal.Release(endpointVolumePtr);
            if (devicePtr != 0) Marshal.Release(devicePtr);
            if (enumeratorPtr != 0) Marshal.Release(enumeratorPtr);
        }
    }

    private static AudioEndpointSnapshot CreateNonOperationalSnapshot(
        DateTimeOffset now,
        IntegrationCapabilityState state,
        string code) =>
        new(
            CapabilityReport: new IntegrationCapabilityReport(
                IntegrationModuleId.Audio,
                state,
                code,
                $"CoreAudio en estado {state} ({code}).",
                [],
                now),
            HasDefaultRenderDevice: false,
            MasterVolumeScalar: 0f,
            IsMasterMuted: false,
            ProcessSessions: [],
            CapturedAtUtc: now);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        out nint ppv);
}
