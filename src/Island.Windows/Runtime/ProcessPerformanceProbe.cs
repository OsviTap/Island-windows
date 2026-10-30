using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Island.Windows.Runtime;

public sealed record HardwareEnvironmentSummary(
    string OsDescription,
    string ProcessArchitecture,
    int LogicalProcessorCount,
    double TotalPhysicalMemoryMb,
    bool IsDebuggerAttached);

public sealed record ProcessMetricsSnapshot(
    string ScenarioId,
    string ScenarioName,
    double ElapsedWindowMs,
    double CpuUsagePercent,
    double TotalProcessorTimeMs,
    double PrivateBytesMb,
    double WorkingSetMb,
    double ManagedHeapMb,
    int HandleCount,
    uint GdiObjectCount,
    uint UserObjectCount,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Sonda de rendimiento real del proceso actual (Release, sin depurador) para la Fase 5 (Sección 9).
/// Mide Private Bytes, Working Set, Managed Heap, uso porcentual de CPU y conteo de objetos GDI/User/Handles
/// para detectar fugas de regiones o recursos nativos.
/// </summary>
public static class ProcessPerformanceProbe
{
    private const uint GrGdiObjects = 0;
    private const uint GrUserObjects = 1;

    public static HardwareEnvironmentSummary CaptureHardwareSummary()
    {
        double totalRamMb = 0;
        var memStatus = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            totalRamMb = Math.Round(memStatus.ullTotalPhys / (1024.0 * 1024.0), 1);
        }

        return new HardwareEnvironmentSummary(
            OsDescription: RuntimeInformation.OSDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalProcessorCount: Environment.ProcessorCount,
            TotalPhysicalMemoryMb: totalRamMb,
            IsDebuggerAttached: Debugger.IsAttached);
    }

    public static ProcessMetricsSnapshot CaptureSnapshot(
        string scenarioId,
        string scenarioName,
        TimeSpan? cpuSampleStartProcessorTime = null,
        long? cpuSampleStartTimestamp = null)
    {
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();

        double elapsedMs = 0;
        double cpuPercent = 0;
        TimeSpan currentCpu = proc.TotalProcessorTime;

        if (cpuSampleStartProcessorTime.HasValue && cpuSampleStartTimestamp.HasValue)
        {
            TimeSpan wallElapsed = Stopwatch.GetElapsedTime(cpuSampleStartTimestamp.Value);
            elapsedMs = Math.Round(wallElapsed.TotalMilliseconds, 2);
            if (wallElapsed.TotalMilliseconds > 1.0)
            {
                double cpuDeltaMs = (currentCpu - cpuSampleStartProcessorTime.Value).TotalMilliseconds;
                double totalAvailableCpuMs = wallElapsed.TotalMilliseconds * Math.Max(1, Environment.ProcessorCount);
                cpuPercent = Math.Round(Math.Max(0.0, (cpuDeltaMs / totalAvailableCpuMs) * 100.0), 3);
            }
        }

        uint gdiCount = 0;
        uint userCount = 0;
        try
        {
            gdiCount = GetGuiResources(proc.Handle, GrGdiObjects);
            userCount = GetGuiResources(proc.Handle, GrUserObjects);
        }
        catch (Exception)
        {
        }

        double privateMb = Math.Round(proc.PrivateMemorySize64 / (1024.0 * 1024.0), 2);
        double workingSetMb = Math.Round(proc.WorkingSet64 / (1024.0 * 1024.0), 2);
        double managedMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0), 2);

        return new ProcessMetricsSnapshot(
            ScenarioId: scenarioId,
            ScenarioName: scenarioName,
            ElapsedWindowMs: elapsedMs,
            CpuUsagePercent: cpuPercent,
            TotalProcessorTimeMs: Math.Round(currentCpu.TotalMilliseconds, 2),
            PrivateBytesMb: privateMb,
            WorkingSetMb: workingSetMb,
            ManagedHeapMb: managedMb,
            HandleCount: proc.HandleCount,
            GdiObjectCount: gdiCount,
            UserObjectCount: userCount,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetGuiResources(nint hProcess, uint uiFlags);
}
