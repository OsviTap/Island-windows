using System.Text.Json;
using Island.App.ViewModels;
using Island.Application.Configuration;
using Island.Application.Coordination;
using Island.Application.Diagnostics;
using Island.Application.Theming;
using Island.Core.Contracts;
using Island.Core.Diagnostics;
using Island.Core.Domain;
using Island.Core.Theming;
using Island.Core.Windowing;
using Island.Windows.Audio;
using Island.Windows.Media;
using Island.Windows.Notifications;
using Island.Windows.Runtime;
using Island.Windows.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Island.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;
    private IslandWindowController? _windowController;
    private GsmtcMediaSessionProvider? _mediaProvider;
    private HybridNotificationMonitor? _notificationMonitor;

    // IMPORTANTE: los DispatcherQueueTimer deben mantenerse como campos. Si se guardan en variables
    // locales, el recolector de basura los libera y dejan de dispararse silenciosamente.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _cursorMonitorTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _resizeAnimationTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _notificationDiffTimer;

    private double _animCurrentWidthDip;
    private double _animCurrentHeightDip;
    private double _animTargetWidthDip;
    private double _animTargetHeightDip;
    private bool _hasAppliedInitialSize;
    private long _lastCursorInsideTimestamp;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        string appDataRoot = ResolveDataRoot(cmdArgs);

        Directory.CreateDirectory(appDataRoot);

        try
        {
            await RunLaunchedCoreAsync(cmdArgs, appDataRoot);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(appDataRoot, "phase4-startup-error.txt"),
                    $"{ex.GetType().FullName}: {ex.Message} (0x{ex.HResult:X8})\nInner: {ex.InnerException?.Message}\n{ex.StackTrace}");
            }
            catch (IOException)
            {
            }

            _window?.Close();
            Exit();
        }
    }

    private async Task RunLaunchedCoreAsync(string[] cmdArgs, string appDataRoot)
    {
        string logFilePath = Path.Combine(appDataRoot, "logs", "diagnostics.jsonl");
        string settingsFilePath = Path.Combine(appDataRoot, "config", "settings.json");
        string themesDirPath = Path.Combine(appDataRoot, "themes");

        var logger = new BoundedDiagnosticLogger(logFilePath: logFilePath, includePrivateContent: false);
        var settingsStore = new JsonSettingsStore(settingsFilePath, logger);
        var settingsResult = settingsStore.LoadOrRecover();
        logger.IncludePrivateContent = settingsResult.Settings.DiagnosticsIncludePrivateContent;

        var themeCatalog = new JsonThemeCatalog(themesDirPath, logger);
        int loadedThemesCount = themeCatalog.SyncAndLoadFromDisk();

        var identityProvider = new WindowsPackageIdentityProvider();
        var identitySnapshot = identityProvider.GetCurrentIdentity();

        var envInspector = new WindowsSystemEnvironmentInspector();
        var envSnapshot = envInspector.CaptureSnapshot();

        var themeService = new ThemeService(logger);
        var coordinator = new IslandStructuralCoordinator(
            themeService,
            logger,
            envSnapshot);

        coordinator.SetAllowedNotificationApps(settingsResult.Settings.AllowedNotificationAppIds);

        var selectedTheme = themeCatalog.GetByIdOrFallback(settingsResult.Settings.SelectedThemeId);
        var appliedTheme = coordinator.ApplyTheme(
            selectedTheme,
            envSnapshot,
            settingsResult.Settings.RequestedMaterialOverride);

        coordinator.ReportModuleCapability(new IntegrationCapabilityReport(
            IntegrationModuleId.Environment,
            IntegrationCapabilityState.Available,
            "ENV_VERIFIED",
            $"OS {envSnapshot.OsVersionString} ({envSnapshot.ProcessArchitecture}), TemasJSON={loadedThemesCount}",
            ["CaptureSnapshot", "DeclarativeJsonThemes"],
            DateTimeOffset.UtcNow));

        logger.Log(
            DiagnosticLogLevel.Info,
            "Startup",
            "PHASE4_STARTUP_OK",
            $"Arranque completado. HasPackageIdentity={identitySnapshot.HasPackageIdentity}, EffectiveMaterial={appliedTheme.EffectiveMaterial}, ThemesLoaded={loadedThemesCount}");

        WriteVerificationReceiptIfRequested(appDataRoot, identitySnapshot, envSnapshot, appliedTheme, settingsResult.DiagnosticCode);

        _mediaProvider = new GsmtcMediaSessionProvider(logger);
        var audioInspector = new CoreAudioEndpointInspector(logger);
        _notificationMonitor = new HybridNotificationMonitor(coordinator, logger);

        var viewModel = new MainViewModel(
            coordinator,
            envInspector,
            identitySnapshot,
            settingsStore,
            themeCatalog,
            settingsResult.Settings,
            _mediaProvider,
            audioInspector);

        bool isAutomatedExit = cmdArgs.Any(a => string.Equals(a, "--verify-startup-exit", StringComparison.OrdinalIgnoreCase));
        bool runPhase2Probes = cmdArgs.Any(a => string.Equals(a, "--run-phase2-probes", StringComparison.OrdinalIgnoreCase));
        bool runPhase4Verification = cmdArgs.Any(a => string.Equals(a, "--run-phase4-verification", StringComparison.OrdinalIgnoreCase));
        bool runPhase5Benchmarks = cmdArgs.Any(a => string.Equals(a, "--run-phase5-benchmarks", StringComparison.OrdinalIgnoreCase));
        bool isInteractiveDesktopRun = !isAutomatedExit && !runPhase2Probes && !runPhase4Verification && !runPhase5Benchmarks;

        // Creamos y activamos MainWindow de forma síncrona ANTES de cualquier await en OnLaunched,
        // evitando que el bucle de mensajes de WinUI 3 se cierre prematuramente al no encontrar ventanas activas.
        _window = new MainWindow(viewModel);
        viewModel.AttachDispatcherQueue(_window.DispatcherQueue);
        ConfigureBorderlessFloatingWindow(_window);
        _window.Closed += (_, _) =>
        {
            logger.Log(
                DiagnosticLogLevel.Info,
                "Windowing",
                "MAIN_WINDOW_CLOSED",
                $"MainWindow cerrada (PID={Environment.ProcessId}).");
        };
        _window.Activate();

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var displayProber = new WindowsDisplayAndWindowProber(logger);
        _windowController = new IslandWindowController(hwnd, displayProber, logger);
        _windowController.TryRegisterRecoveryHotKey();

        var resilienceMonitor = new SystemLifecycleResilienceMonitor(
            coordinator,
            envInspector,
            displayProber,
            logger);

        EffectiveMaterialKind? lastAppliedMaterial = null;

        void ApplyWindowSize(double widthDip, double heightDip)
        {
            _windowController?.ApplyLayoutAndRegion(
                coordinator.CurrentState,
                coordinator.CurrentThemeState.Theme,
                viewModel.PreferredMonitorDeviceName,
                viewModel.CurrentSettings.AnchorHorizontalRatio,
                viewModel.CurrentSettings.AnchorTopOffsetDip,
                widthDip,
                heightDip);
        }

        // Animación de tamaño con suavizado exponencial (~60 FPS) para que la isla "crezca" y "se encoja"
        // de forma fluida entre los 3 niveles, como en la Dynamic Island de los celulares.
        _resizeAnimationTimer = _window.DispatcherQueue.CreateTimer();
        _resizeAnimationTimer.Interval = TimeSpan.FromMilliseconds(15);
        _resizeAnimationTimer.Tick += (_, _) =>
        {
            const double easing = 0.32;
            _animCurrentWidthDip += (_animTargetWidthDip - _animCurrentWidthDip) * easing;
            _animCurrentHeightDip += (_animTargetHeightDip - _animCurrentHeightDip) * easing;

            bool done = Math.Abs(_animTargetWidthDip - _animCurrentWidthDip) < 0.75 &&
                        Math.Abs(_animTargetHeightDip - _animCurrentHeightDip) < 0.75;
            if (done)
            {
                _animCurrentWidthDip = _animTargetWidthDip;
                _animCurrentHeightDip = _animTargetHeightDip;
                _resizeAnimationTimer?.Stop();
            }

            ApplyWindowSize(_animCurrentWidthDip, _animCurrentHeightDip);
        };

        viewModel.LayoutOrThemeUpdated += (_, _) =>
        {
            if (_window is null || _windowController is null)
            {
                return;
            }

            var material = coordinator.CurrentThemeState.EffectiveMaterial;
            if (lastAppliedMaterial != material)
            {
                lastAppliedMaterial = material;
                ApplyEffectiveSystemBackdrop(_window, material);
            }

            _animTargetWidthDip = viewModel.PillWidthDip;
            _animTargetHeightDip = viewModel.PillHeightDip;

            bool animationsAllowed = isInteractiveDesktopRun &&
                                     coordinator.CurrentThemeState.EffectiveAnimationProfile == AnimationProfile.Normal;

            if (!_hasAppliedInitialSize || !animationsAllowed)
            {
                _hasAppliedInitialSize = true;
                _animCurrentWidthDip = _animTargetWidthDip;
                _animCurrentHeightDip = _animTargetHeightDip;
                ApplyWindowSize(_animCurrentWidthDip, _animCurrentHeightDip);
                return;
            }

            if (Math.Abs(_animTargetWidthDip - _animCurrentWidthDip) >= 0.75 ||
                Math.Abs(_animTargetHeightDip - _animCurrentHeightDip) >= 0.75)
            {
                _resizeAnimationTimer?.Start();
            }
            else
            {
                ApplyWindowSize(_animCurrentWidthDip, _animCurrentHeightDip);
            }
        };

        if (isInteractiveDesktopRun)
        {
            if (coordinator.CurrentThemeState.RequestedMaterial == MaterialKind.Solid &&
                string.Equals(settingsResult.DiagnosticCode, "CREATED_DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                viewModel.SwitchToGlassThemeCommand.Execute(null);
            }

            // En modo escritorio, la isla arranca en su estado natural compacto/cerrado (Nivel 1: 128x28),
            // abriéndose a Nivel 2 (260x40) al acercar el cursor y a Nivel 3 (420x214) al hacer clic.
            coordinator.SetUserInteraction(IslandInteractionState.Passive, IslandVisibility.Compact);
        }

        ApplyEffectiveSystemBackdrop(_window, coordinator.CurrentThemeState.EffectiveMaterial);
        _windowController.ApplyLayoutAndRegion(
            coordinator.CurrentState,
            coordinator.CurrentThemeState.Theme,
            viewModel.PreferredMonitorDeviceName,
            viewModel.CurrentSettings.AnchorHorizontalRatio,
            viewModel.CurrentSettings.AnchorTopOffsetDip,
            viewModel.PillWidthDip,
            viewModel.PillHeightDip);

        if (isInteractiveDesktopRun && _window.DispatcherQueue is not null)
        {
            bool wasLeftMouseDown = false;
            _cursorMonitorTimer = _window.DispatcherQueue.CreateTimer();
            _cursorMonitorTimer.Interval = TimeSpan.FromMilliseconds(40);
            _cursorMonitorTimer.Tick += (_, _) =>
            {
                if (_window is null || resilienceMonitor.IsSuspended)
                {
                    return;
                }

                bool isCursorInside = WindowsDisplayAndWindowProber.IsCursorInsideHwnd(hwnd, paddingPx: 4);
                bool isLeftDown = WindowsDisplayAndWindowProber.IsLeftMouseButtonPressed();
                bool isNewPress = isLeftDown && !wasLeftMouseDown;
                wasLeftMouseDown = isLeftDown;

                if (isCursorInside)
                {
                    _lastCursorInsideTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                }

                // Nivel 3 -> Nivel 1: clic en cualquier lugar fuera de la isla (salvo que esté fijada).
                if (!isCursorInside && isNewPress)
                {
                    viewModel.CollapseFromOutsideClick();
                    return;
                }

                if (coordinator.CurrentState.Visibility != IslandVisibility.Compact)
                {
                    return;
                }

                // Nivel 1 -> Nivel 2: el cursor entra en la cápsula (apertura inmediata).
                if (isCursorInside)
                {
                    viewModel.SetCursorHover(true);
                    return;
                }

                // Nivel 2 -> Nivel 1: el cursor salió hace más de 350 ms (evita parpadeos al rozar el borde).
                double msSinceInside = System.Diagnostics.Stopwatch
                    .GetElapsedTime(_lastCursorInsideTimestamp).TotalMilliseconds;
                if (viewModel.IsHoverPreviewMode && msSinceInside > 350)
                {
                    viewModel.SetCursorHover(false);
                }
            };
            _cursorMonitorTimer.Start();
        }

        if (isInteractiveDesktopRun)
        {
            var liveBounds = _windowController.LastAppliedGeometry?.PillScreenBoundsPx;
            bool isWinVisible = WindowsDisplayAndWindowProber.IsHwndVisible(hwnd);
            var winRect = WindowsDisplayAndWindowProber.GetHwndScreenRect(hwnd);
            logger.Log(
                DiagnosticLogLevel.Info,
                "Windowing",
                "LIVE_ISLAND_SHOWN",
                $"Isla mostrada en pantalla. PID={Environment.ProcessId}, HWND=0x{hwnd:X}, IsVisible={isWinVisible}, ActualRect=({winRect.X},{winRect.Y} {winRect.Width}x{winRect.Height}), Monitor={_windowController.LastAppliedGeometry?.TargetMonitorDeviceName}, Bounds=({liveBounds?.X},{liveBounds?.Y} {liveBounds?.Width}x{liveBounds?.Height}), Visibility={coordinator.CurrentState.Visibility}");
        }

        double coldStartupMs = Math.Round(
            System.Diagnostics.Stopwatch.GetElapsedTime(Program.ProcessEntryTimestamp).TotalMilliseconds,
            2);

        // Sincronizaci\u00F3n inicial de reproducci\u00F3n multimedia y notificaciones en vivo
        try
        {
            using var initMediaCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var initialMedia = await _mediaProvider.ProbeSessionsAsync(initMediaCts.Token);
            coordinator.ReportModuleCapability(initialMedia.CapabilityReport);
            viewModel.UpdateMediaSessionsSnapshot(initialMedia.Sessions);
        }
        catch (OperationCanceledException)
        {
        }

        _mediaProvider.SessionsChanged += (_, updatedSessions) =>
        {
            _window?.DispatcherQueue?.TryEnqueue(() =>
            {
                viewModel.UpdateMediaSessionsSnapshot(updatedSessions);
            });
        };

        var initialAudio = audioInspector.CaptureAudioSnapshot();
        coordinator.ReportModuleCapability(initialAudio.CapabilityReport);
        viewModel.RefreshAudioStatus();

        try
        {
            using var initNotifCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _notificationMonitor.InitializeAndSyncWatermarkAsync(initNotifCts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        if (!_notificationMonitor.IsPushEventActive && _window.DispatcherQueue is not null)
        {
            _notificationDiffTimer = _window.DispatcherQueue.CreateTimer();
            _notificationDiffTimer.Interval = TimeSpan.FromSeconds(2.5);
            _notificationDiffTimer.Tick += async (_, _) =>
            {
                if (_notificationMonitor is not null && !resilienceMonitor.IsSuspended)
                {
                    await _notificationMonitor.SyncDifferentialNotificationsAsync();
                }
            };
            _notificationDiffTimer.Start();
        }

        if (runPhase2Probes)
        {
            await ExecutePhase2ViabilityProbesAsync(appDataRoot, _window, identityProvider, coordinator, logger);
        }

        if (runPhase4Verification)
        {
            ExecutePhase4UiAndThemesVerification(
                appDataRoot,
                _window,
                viewModel,
                coordinator,
                themeCatalog,
                _windowController,
                displayProber,
                loadedThemesCount);
        }

        if (runPhase5Benchmarks)
        {
            await ExecutePhase5PerformanceAndRobustnessBenchmarksAsync(
                appDataRoot,
                coldStartupMs,
                viewModel,
                coordinator,
                _windowController,
                resilienceMonitor);
        }

        if (isAutomatedExit)
        {
            await _notificationMonitor.DisposeAsync();
            await _mediaProvider.DisposeAsync();
            _windowController.Dispose();
            _window.Close();
        }
    }

    private static void ApplyEffectiveSystemBackdrop(Window window, EffectiveMaterialKind effectiveMaterial)
    {
        try
        {
            window.SystemBackdrop = effectiveMaterial switch
            {
                EffectiveMaterialKind.DesktopAcrylic when DesktopAcrylicController.IsSupported() => new DesktopAcrylicBackdrop(),
                EffectiveMaterialKind.Mica when MicaController.IsSupported() => new MicaBackdrop(),
                _ => null
            };
        }
        catch (Exception)
        {
            window.SystemBackdrop = null;
        }
    }

    private static void ConfigureBorderlessFloatingWindow(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
        }
        catch (Exception)
        {
        }
    }

    private static void ExecutePhase4UiAndThemesVerification(
        string appDataRoot,
        MainWindow window,
        MainViewModel viewModel,
        IslandStructuralCoordinator coordinator,
        JsonThemeCatalog themeCatalog,
        IslandWindowController windowController,
        WindowsDisplayAndWindowProber displayProber,
        int loadedThemesCount)
    {
        // 1. Verificar conmutaci\u00F3n en vivo entre los 3 temas declarativos (Solid, Glass/Acrylic, Mica)
        viewModel.SwitchToSolidThemeCommand.Execute(null);
        var solidThemeState = coordinator.CurrentThemeState;

        viewModel.SwitchToGlassThemeCommand.Execute(null);
        var glassThemeState = coordinator.CurrentThemeState;

        viewModel.SwitchToMicaThemeCommand.Execute(null);
        var micaThemeState = coordinator.CurrentThemeState;

        // 2. Verificar modo de Alto Contraste (resoluci\u00F3n determinista sobre BuiltInThemes.DefaultGlass)
        var highContrastEnv = new SystemEnvironmentSnapshot(
            OsVersionString: "10.0.26300.0",
            OsBuildNumber: 26300,
            ProcessArchitecture: "X64",
            IsHighContrastEnabled: true,
            HighContrastSchemeName: "High Contrast Black",
            AreTransparencyEffectsEnabled: true,
            AreAnimationsEnabled: false,
            CapturedAtUtc: DateTimeOffset.UtcNow);
        var highContrastState = coordinator.ApplyTheme(BuiltInThemes.DefaultGlass, highContrastEnv);

        // Restaurar tema Glass normal tras la verificaci\u00F3n de Alto Contraste
        viewModel.SwitchToGlassThemeCommand.Execute(null);

        // 3. Verificar ciclo interactivo: Compact -> Expandir -> Anclar -> Escape (Contraer/Desanclar) -> Recuperar posici\u00F3n
        var initialVisibility = coordinator.CurrentState.Visibility;
        viewModel.ToggleExpandedCommand.Execute(null);
        var expandedState = coordinator.CurrentState;
        var expandedGeometry = windowController.LastAppliedGeometry;

        viewModel.TogglePinCommand.Execute(null);
        var pinnedState = coordinator.CurrentState;

        viewModel.CollapseOnEscapeCommand.Execute(null);
        var afterEscapeState = coordinator.CurrentState;
        var compactGeometry = windowController.LastAppliedGeometry;

        viewModel.ResetPositionToPrimaryMonitorCommand.Execute(null);
        var afterResetGeometry = windowController.LastAppliedGeometry;
        viewModel.CollapseOnEscapeCommand.Execute(null);

        // 4. Verificar c\u00E1lculo geom\u00E9trico en escalas DPI 100%, 125%, 150% y 200%
        int[] dpiScales = [96, 120, 144, 192];
        var dpiLayoutChecks = dpiScales.Select(dpi =>
        {
            int scalePct = (int)Math.Round(dpi * 100.0 / 96.0);
            var simulatedMonitor = new MonitorDisplayDescriptor(
                DeviceName: $"DISPLAY_DPI_{scalePct}",
                IsPrimary: true,
                BoundsPx: new ScreenRectPx(0, 0, 1920, 1080),
                WorkAreaPx: new ScreenRectPx(0, 0, 1920, 1032),
                DpiX: (uint)dpi,
                DpiY: (uint)dpi,
                ScalePercent: scalePct);

            var compactGeo = IslandLayoutCalculator.ComputeGeometry(
                [simulatedMonitor],
                simulatedMonitor.DeviceName,
                IslandVisibility.Compact,
                BuiltInThemes.DefaultGlass);

            var expandedGeo = IslandLayoutCalculator.ComputeGeometry(
                [simulatedMonitor],
                simulatedMonitor.DeviceName,
                IslandVisibility.Expanded,
                BuiltInThemes.DefaultGlass);

            bool insideWorkArea =
                compactGeo.PillScreenBoundsPx.X >= simulatedMonitor.WorkAreaPx.X &&
                compactGeo.PillScreenBoundsPx.Right <= simulatedMonitor.WorkAreaPx.Right &&
                expandedGeo.PillScreenBoundsPx.X >= simulatedMonitor.WorkAreaPx.X &&
                expandedGeo.PillScreenBoundsPx.Right <= simulatedMonitor.WorkAreaPx.Right;

            return new
            {
                Dpi = dpi,
                ScalePercent = scalePct,
                CompactPillPx = compactGeo.PillScreenBoundsPx,
                ExpandedPillPx = expandedGeo.PillScreenBoundsPx,
                InsideWorkArea = insideWorkArea
            };
        }).ToList();

        var report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            CatalogThemesLoadedFromDisk = loadedThemesCount,
            AvailableThemeIds = themeCatalog.GetAvailableThemes().Select(t => t.Id).ToList(),
            RecoveryHotKeyRegistered = windowController.IsRecoveryHotKeyRegistered,
            ThemeVerifications = new
            {
                Solid = new { solidThemeState.RequestedMaterial, solidThemeState.EffectiveMaterial, solidThemeState.StatusReasonCode },
                Glass = new { glassThemeState.RequestedMaterial, glassThemeState.EffectiveMaterial, glassThemeState.StatusReasonCode },
                Mica = new { micaThemeState.RequestedMaterial, micaThemeState.EffectiveMaterial, micaThemeState.StatusReasonCode },
                HighContrastSimulation = new
                {
                    highContrastState.RequestedMaterial,
                    highContrastState.EffectiveMaterial,
                    highContrastState.EffectiveAnimationProfile,
                    highContrastState.IsHighContrastActive,
                    highContrastState.StatusReasonCode
                }
            },
            InteractionCycle = new
            {
                InitialVisibility = initialVisibility.ToString(),
                ExpandedVisibility = expandedState.Visibility.ToString(),
                PinnedInteraction = pinnedState.Interaction.ToString(),
                AfterEscapeVisibility = afterEscapeState.Visibility.ToString(),
                AfterEscapeInteraction = afterEscapeState.Interaction.ToString(),
                ExpandedPillBoundsPx = expandedGeometry?.PillScreenBoundsPx,
                CompactPillBoundsPx = compactGeometry?.PillScreenBoundsPx,
                ResetPositionTargetMonitor = afterResetGeometry?.TargetMonitorDeviceName
            },
            MultiDpiGeometryChecks = dpiLayoutChecks,
            LiveViewModelSnapshot = new
            {
                viewModel.CompactBadgeText,
                viewModel.CompactHeadlineText,
                viewModel.MediaTitleText,
                viewModel.MediaArtistText,
                viewModel.MasterVolumeText,
                viewModel.NotificationHeadlineText
            }
        };

        string reportPath = Path.Combine(appDataRoot, "phase4-ui-themes-report.json");
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json);
    }

    private static async Task ExecutePhase5PerformanceAndRobustnessBenchmarksAsync(
        string appDataRoot,
        double coldStartupMs,
        MainViewModel viewModel,
        IslandStructuralCoordinator coordinator,
        IslandWindowController windowController,
        SystemLifecycleResilienceMonitor resilienceMonitor)
    {
        var hardware = ProcessPerformanceProbe.CaptureHardwareSummary();
        using var currentProc = System.Diagnostics.Process.GetCurrentProcess();

        // PERF-01: Oculta y sin actividad (Idle) durante ventana de observación real sin temporizadores activos
        coordinator.SetUserInteraction(IslandInteractionState.Passive, IslandVisibility.Hidden);
        windowController.ApplyLayoutAndRegion(
            coordinator.CurrentState,
            coordinator.CurrentThemeState.Theme,
            viewModel.PreferredMonitorDeviceName,
            viewModel.CurrentSettings.AnchorHorizontalRatio,
            viewModel.CurrentSettings.AnchorTopOffsetDip);

        currentProc.Refresh();
        var cpuStart01 = currentProc.TotalProcessorTime;
        long stamp01 = System.Diagnostics.Stopwatch.GetTimestamp();
        await Task.Delay(1500);
        var perf01 = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-01",
            "Oculta y sin actividad (Idle)",
            cpuStart01,
            stamp01);

        // PERF-02: Compacta con música activa (actualización dirigida por eventos sin polling)
        coordinator.SetUserInteraction(IslandInteractionState.Passive, IslandVisibility.Compact);
        windowController.ApplyLayoutAndRegion(
            coordinator.CurrentState,
            coordinator.CurrentThemeState.Theme,
            viewModel.PreferredMonitorDeviceName,
            viewModel.CurrentSettings.AnchorHorizontalRatio,
            viewModel.CurrentSettings.AnchorTopOffsetDip);

        currentProc.Refresh();
        var cpuStart02 = currentProc.TotalProcessorTime;
        long stamp02 = System.Diagnostics.Stopwatch.GetTimestamp();
        await Task.Delay(1500);
        var perf02 = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-02",
            "Compacta con musica activa (Event-driven)",
            cpuStart02,
            stamp02);

        // PERF-03A & PERF-03B: Expandida con material Solid vs Glass (DesktopAcrylic) vs Mica
        coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse, IslandVisibility.Expanded);
        viewModel.SwitchToSolidThemeCommand.Execute(null);
        currentProc.Refresh();
        var cpuStart03Solid = currentProc.TotalProcessorTime;
        long stamp03Solid = System.Diagnostics.Stopwatch.GetTimestamp();
        await Task.Delay(800);
        var perf03Solid = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-03-SOLID",
            "Expandida con material Solid",
            cpuStart03Solid,
            stamp03Solid);

        viewModel.SwitchToGlassThemeCommand.Execute(null);
        currentProc.Refresh();
        var cpuStart03Glass = currentProc.TotalProcessorTime;
        long stamp03Glass = System.Diagnostics.Stopwatch.GetTimestamp();
        await Task.Delay(800);
        var perf03Glass = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-03-GLASS",
            "Expandida con material Glass (DesktopAcrylic)",
            cpuStart03Glass,
            stamp03Glass);

        viewModel.SwitchToMicaThemeCommand.Execute(null);
        currentProc.Refresh();
        var cpuStart03Mica = currentProc.TotalProcessorTime;
        long stamp03Mica = System.Diagnostics.Stopwatch.GetTimestamp();
        await Task.Delay(800);
        var perf03Mica = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-03-MICA",
            "Expandida con material Mica",
            cpuStart03Mica,
            stamp03Mica);

        // PERF-04: Ráfaga de 20 notificaciones consecutivas en menos de 1 segundo
        coordinator.SetUserInteraction(IslandInteractionState.Passive, IslandVisibility.Compact);
        currentProc.Refresh();
        var cpuStart04 = currentProc.TotalProcessorTime;
        long stamp04 = System.Diagnostics.Stopwatch.GetTimestamp();

        uint baseNotifId = 900_000;
        for (uint i = 1; i <= 20; i++)
        {
            coordinator.ProcessIncomingNotification(new IncomingNotificationEvent(
                NotificationId: baseNotifId + i,
                AppUserModelId: "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App",
                AppDisplayName: "WhatsApp",
                Title: i <= 10 ? "Contacto Rafaga A" : $"Contacto {i}",
                Body: $"Mensaje de prueba de rafaga #{i}",
                TimestampUtc: DateTimeOffset.UtcNow));
        }

        var perf04 = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-04",
            "Rafaga de 20 notificaciones consecutivas",
            cpuStart04,
            stamp04);

        int queueAfterBurst = coordinator.PendingNotificationCount;
        int burstCountActive = coordinator.ActiveNotificationView?.BurstCount ?? 0;

        // PERF-05: Uso prolongado / Estrés de 500 ciclos (cambios de pista, aperturas/cierres, temas y avisos)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeStress = ProcessPerformanceProbe.CaptureSnapshot("PERF-05-BEFORE", "Antes de 500 ciclos de estres");

        currentProc.Refresh();
        var cpuStart05 = currentProc.TotalProcessorTime;
        long stamp05 = System.Diagnostics.Stopwatch.GetTimestamp();

        var dummyCapabilities = new MediaTransportCapabilities(true, true, true, true, true, true, false);
        var dummyTimeline = new MediaTimelineSnapshot(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(180), TimeSpan.Zero, TimeSpan.FromSeconds(180), DateTimeOffset.UtcNow, true);

        for (int cycle = 0; cycle < 500; cycle++)
        {
            var session = new MediaSessionSnapshot(
                SourceAppUserModelId: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify",
                ProviderKind: MediaProviderKind.Spotify,
                IsCurrentSystemSession: true,
                Title: $"Track Stress #{cycle % 25}",
                Artist: "Stress Artist",
                AlbumTitle: "Stress Album",
                PlaybackState: (cycle % 2 == 0) ? MediaPlaybackState.Playing : MediaPlaybackState.Paused,
                Capabilities: dummyCapabilities,
                Timeline: dummyTimeline,
                HasThumbnailStream: false,
                ThumbnailByteCount: 0,
                CapturedAtUtc: DateTimeOffset.UtcNow);

            coordinator.ProcessMediaSessions([session]);

            if (cycle % 5 == 0)
            {
                coordinator.SetUserInteraction(IslandInteractionState.ActiveInUse, IslandVisibility.Expanded);
            }
            else if (cycle % 5 == 2)
            {
                coordinator.SetUserInteraction(IslandInteractionState.Passive, IslandVisibility.Compact);
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var perf05After = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-05-AFTER",
            "Despues de 500 ciclos de estres (con GC)",
            cpuStart05,
            stamp05);

        int gdiDelta = (int)perf05After.GdiObjectCount - (int)beforeStress.GdiObjectCount;
        int userDelta = (int)perf05After.UserObjectCount - (int)beforeStress.UserObjectCount;
        double managedHeapDeltaMb = Math.Round(perf05After.ManagedHeapMb - beforeStress.ManagedHeapMb, 2);

        // PERF-06: Suspensión / Reanudación y desconexión de monitor preferido / cambio de entorno
        var suspendState = resilienceMonitor.OnPowerSuspending();
        var resumeResult = resilienceMonitor.OnPowerResumed(
            windowController,
            preferredMonitorDeviceName: @"\\.\DISPLAY99_UNPLUGGED",
            anchorHorizontalRatio: 0.5,
            anchorTopOffsetDip: 12.0);
        var displayChangeResult = resilienceMonitor.OnDisplayOrEnvironmentChanged(
            windowController,
            preferredMonitorDeviceName: @"\\.\DISPLAY99_UNPLUGGED",
            anchorHorizontalRatio: 0.5,
            anchorTopOffsetDip: 12.0);
        var fullScreenSnapshot = resilienceMonitor.EvaluateFullScreenPolicy();

        var perf06 = ProcessPerformanceProbe.CaptureSnapshot(
            "PERF-06",
            "Post suspension/reanudacion y cambio de monitor");

        var phase5Report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Hardware = hardware,
            ColdStartupToFirstFrameMs = coldStartupMs,
            Scenarios = new
            {
                Perf01_IdleHidden = perf01,
                Perf02_CompactActiveMusic = perf02,
                Perf03_MaterialComparison = new
                {
                    Solid = perf03Solid,
                    GlassDesktopAcrylic = perf03Glass,
                    Mica = perf03Mica
                },
                Perf04_NotificationBurst = new
                {
                    Metrics = perf04,
                    NotificationsSent = 20,
                    CoalescedBurstCountOnFirstSender = burstCountActive,
                    BoundedQueueRemainingCount = queueAfterBurst
                },
                Perf05_Endurance500Cycles = new
                {
                    Before = beforeStress,
                    After = perf05After,
                    GdiObjectsDelta = gdiDelta,
                    UserObjectsDelta = userDelta,
                    ManagedHeapDeltaMb = managedHeapDeltaMb
                },
                Perf06_LifecycleResilience = new
                {
                    Metrics = perf06,
                    SuspendedPolicyState = suspendState.Policy.ToString(),
                    PowerResumeLatencyMs = resumeResult.RecoveryLatencyMs,
                    PowerResumeUsedPrimaryFallback = resumeResult.UsedPrimaryMonitorFallback,
                    DisplayChangeLatencyMs = displayChangeResult.RecoveryLatencyMs,
                    FullScreenDetectionState = fullScreenSnapshot.NotificationStateName
                }
            }
        };

        string reportPath = Path.Combine(appDataRoot, "phase5-performance-report.json");
        string json = JsonSerializer.Serialize(phase5Report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json);
    }

    private static async Task ExecutePhase2ViabilityProbesAsync(
        string appDataRoot,
        Window window,
        IPackageIdentityProvider identityProvider,
        IslandStructuralCoordinator coordinator,
        IDiagnosticLogger logger)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        bool acrylicSupported = false;
        bool micaSupported = false;
        bool systemBackdropElementPresent = false;
        var backdropNotes = new List<string>();

        try
        {
            acrylicSupported = DesktopAcrylicController.IsSupported();
            micaSupported = MicaController.IsSupported();

            Type? sbeType = typeof(Window).Assembly.GetType("Microsoft.UI.Xaml.Controls.SystemBackdropElement");
            systemBackdropElementPresent = sbeType is not null;
            backdropNotes.Add($"DesktopAcrylicController.IsSupported={acrylicSupported}");
            backdropNotes.Add($"MicaController.IsSupported={micaSupported}");
            backdropNotes.Add($"SystemBackdropElement.TypeFound={systemBackdropElementPresent}");

            if (acrylicSupported)
            {
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                backdropNotes.Add("Window.SystemBackdrop=DesktopAcrylicBackdrop applied");
            }
        }
        catch (Exception ex)
        {
            backdropNotes.Add($"BackdropProbeException:{ex.GetType().Name}:{ex.Message}");
        }

        var windowProber = new WindowsDisplayAndWindowProber(logger);
        var windowResult = windowProber.ProbeWindowAndBackdropViability(
            hwnd,
            acrylicSupported,
            micaSupported,
            systemBackdropElementPresent,
            string.Join("; ", backdropNotes));

        coordinator.ReportModuleCapability(windowResult.CapabilityReport);

        await using var mediaProvider = new GsmtcMediaSessionProvider(logger);
        var mediaResult = await mediaProvider.ProbeSessionsAsync();
        coordinator.ReportModuleCapability(mediaResult.CapabilityReport);

        var audioInspector = new CoreAudioEndpointInspector(logger);
        var audioResult = audioInspector.CaptureAudioSnapshot();
        coordinator.ReportModuleCapability(audioResult.CapabilityReport);

        var notificationProbe = new WindowsNotificationListenerProbe(identityProvider, logger);
        var notificationResult = await notificationProbe.ProbeAsync(requestPermissionIfUnspecified: false);
        coordinator.ReportModuleCapability(notificationResult.CapabilityReport);

        var phase2Report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            SingleInstance = new
            {
                Program.IsSingleInstanceRegistered,
                Program.SingleInstanceStatusDetail
            },
            WindowAndDisplay = windowResult,
            MediaGsmtc = mediaResult,
            CoreAudio = audioResult,
            Notifications = notificationResult,
            CoordinatorCapabilities = coordinator.GetCapabilitiesSnapshot()
        };

        string reportPath = Path.Combine(appDataRoot, "phase2-viability-report.json");
        string json = JsonSerializer.Serialize(phase2Report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json);
    }

        internal static string ResolveDataRoot(string[] cmdArgs)
    {
        for (int i = 0; i < cmdArgs.Length - 1; i++)
        {
            if (string.Equals(cmdArgs[i], "--data-dir", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(cmdArgs[i + 1]))
            {
                return Path.GetFullPath(cmdArgs[i + 1]);
            }
        }

        string localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IslandWidget");

        try
        {
            Directory.CreateDirectory(localAppData);
            return localAppData;
        }
        catch (Exception)
        {
            // Respaldo automático al directorio junto al ejecutable si %LOCALAPPDATA% no es accesible
            return Path.Combine(AppContext.BaseDirectory, "IslandData");
        }
    }

    private static void WriteVerificationReceiptIfRequested(
        string appDataRoot,
        PackageIdentitySnapshot identity,
        SystemEnvironmentSnapshot env,
        AppliedThemeState themeState,
        string settingsStatus)
    {
        try
        {
            string receiptPath = Path.Combine(appDataRoot, "phase1-startup-receipt.json");
            var receipt = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                identity.HasPackageIdentity,
                identity.PackageFullName,
                identity.Win32ErrorCode,
                env.OsVersionString,
                env.OsBuildNumber,
                env.ProcessArchitecture,
                env.IsHighContrastEnabled,
                env.AreTransparencyEffectsEnabled,
                env.AreAnimationsEnabled,
                RequestedMaterial = themeState.RequestedMaterial.ToString(),
                EffectiveMaterial = themeState.EffectiveMaterial.ToString(),
                ThemeReason = themeState.StatusReasonCode,
                SettingsStatus = settingsStatus
            };

            string json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(receiptPath, json);
        }
        catch (IOException)
        {
        }
    }
}
