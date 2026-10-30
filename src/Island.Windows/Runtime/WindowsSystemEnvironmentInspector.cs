using System.Runtime.InteropServices;
using Island.Core.Contracts;
using Windows.UI.ViewManagement;

namespace Island.Windows.Runtime;

/// <summary>
/// Inspecciona el estado real del entorno Windows (compilación del SO, arquitectura,
/// Alto Contraste, efectos de transparencia y animaciones) mediante APIs de Windows.
/// </summary>
public sealed class WindowsSystemEnvironmentInspector : ISystemEnvironmentInspector
{
    private readonly TimeProvider _timeProvider;

    public WindowsSystemEnvironmentInspector(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SystemEnvironmentSnapshot CaptureSnapshot()
    {
        Version osVersion = Environment.OSVersion.Version;
        string arch = RuntimeInformation.ProcessArchitecture.ToString();

        bool isHighContrast = false;
        string highContrastScheme = string.Empty;
        bool transparencyEnabled = true;
        bool animationsEnabled = true;

        try
        {
            var accessibility = new AccessibilitySettings();
            isHighContrast = accessibility.HighContrast;
            highContrastScheme = isHighContrast ? (accessibility.HighContrastScheme ?? string.Empty) : string.Empty;
        }
        catch (Exception)
        {
            // En caso de restricción del entorno, se mantiene el valor seguro por defecto.
        }

        try
        {
            var uiSettings = new UISettings();
            transparencyEnabled = uiSettings.AdvancedEffectsEnabled;
            animationsEnabled = uiSettings.AnimationsEnabled;
        }
        catch (Exception)
        {
            // En caso de restricción del entorno, se mantiene el valor seguro por defecto.
        }

        return new SystemEnvironmentSnapshot(
            OsVersionString: osVersion.ToString(),
            OsBuildNumber: osVersion.Build,
            ProcessArchitecture: arch,
            IsHighContrastEnabled: isHighContrast,
            HighContrastSchemeName: highContrastScheme,
            AreTransparencyEffectsEnabled: transparencyEnabled,
            AreAnimationsEnabled: animationsEnabled,
            CapturedAtUtc: _timeProvider.GetUtcNow());
    }
}
