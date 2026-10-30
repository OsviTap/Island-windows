using System.Diagnostics;
using Island.Windows.Runtime;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Island.App;

public static class Program
{
    private const uint WindowsAppSdkMajorMinorVersion = 0x00020006; // Windows App SDK 2.6
    private const string SingleInstanceKey = "IslandWidget.MainInstance";

    public static long ProcessEntryTimestamp { get; private set; }
    public static bool IsSingleInstanceRegistered { get; private set; }
    public static string SingleInstanceStatusDetail { get; private set; } = "NOT_CHECKED";

    [STAThread]
    public static int Main(string[] args)
    {
        ProcessEntryTimestamp = Stopwatch.GetTimestamp();
        string dataDir = App.ResolveDataRoot(Environment.GetCommandLineArgs());

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            var identityProvider = new WindowsPackageIdentityProvider();
            var identity = identityProvider.GetCurrentIdentity();

            bool bootstrapInitialized = false;
            if (!identity.HasPackageIdentity)
            {
                bootstrapInitialized = Bootstrap.TryInitialize(
                    WindowsAppSdkMajorMinorVersion,
                    string.Empty,
                    new PackageVersion(0),
                    Bootstrap.InitializeOptions.None,
                    out int hresult);

                if (!bootstrapInitialized)
                {
                    WriteBootstrapDiagnostic(dataDir, $"BOOTSTRAP_FAILED:0x{hresult:X8}");
                    return hresult;
                }
            }

            try
            {
                // Verificación de instancia única y reemplazo limpio de procesos previos de Island.App
                bool isVerificationRun = args.Any(a => string.Equals(a, "--verify-startup-exit", StringComparison.OrdinalIgnoreCase));
                if (!isVerificationRun)
                {
                    try
                    {
                        int currentPid = Environment.ProcessId;
                        foreach (var proc in Process.GetProcessesByName("Island.App"))
                        {
                            try
                            {
                                if (proc.Id != currentPid && !proc.HasExited)
                                {
                                    proc.Kill(entireProcessTree: true);
                                    proc.WaitForExit(1500);
                                }
                            }
                            catch (Exception)
                            {
                            }
                            finally
                            {
                                proc.Dispose();
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                try
                {
                    string keyToUse = isVerificationRun
                        ? $"{SingleInstanceKey}.Verify.{Environment.ProcessId}"
                        : SingleInstanceKey;
                    var mainInstance = AppInstance.FindOrRegisterForKey(keyToUse);
                    if (!mainInstance.IsCurrent && !isVerificationRun)
                    {
                        mainInstance = AppInstance.FindOrRegisterForKey($"{SingleInstanceKey}.{Environment.ProcessId}");
                    }

                    IsSingleInstanceRegistered = true;
                    SingleInstanceStatusDetail = "APPINSTANCE_REGISTERED_PRIMARY";
                }
                catch (Exception ex)
                {
                    IsSingleInstanceRegistered = false;
                    SingleInstanceStatusDetail = $"APPINSTANCE_FALLBACK:{ex.GetType().Name}:0x{ex.HResult:X8}";
                }

                Microsoft.UI.Xaml.Application.Start(_ =>
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                });
                return 0;
            }
            finally
            {
                if (bootstrapInitialized)
                {
                    Bootstrap.Shutdown();
                }
            }
        }
        catch (Exception ex)
        {
            WriteBootstrapDiagnostic(dataDir, $"EXCEPTION:{ex.GetType().FullName}:{ex.Message}");
            return -1;
        }
    }

    private static void WriteBootstrapDiagnostic(string dataDir, string errorText)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "phase1-bootstrap-error.txt"), errorText);
        }
        catch (IOException)
        {
        }
    }
}
