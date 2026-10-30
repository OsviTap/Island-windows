# 00. Bitácora General del Proyecto — Isla Flotante para Windows 11

## Cambios de apariencia y posición — 30/10/2026

- Se redujo la opacidad de la superficie que cubre Glass y Mica, y se resaltó el tema seleccionado. Cuando Windows exige un material sólido se usa un fondo opaco.
- Desde el panel abierto se puede arrastrar el encabezado para cambiar la posición. La configuración guarda monitor y posición vertical; al expandirse cerca del borde inferior, el panel crece hacia arriba. El botón Centrar restaura la posición inicial.
- Se añadieron casos de geometría para posiciones altas y bajas. Estos cambios se prepararon en Linux sin SDK .NET: compilación, pruebas, apariencia real del material, arrastre, escalas y multimonitor quedan pendientes de validación en Windows.

**Proyecto:** `Island.sln` (`d:\Proyectos\Widget windows`)  
**Objetivo:** Construcción paso a paso de un widget de escritorio tipo isla flotante para Windows 11 con control multimedia (`GSMTC` / `CoreAudio`), avisos filtrados (`UserNotificationListener`), temas declarativos JSON y bajo consumo comprobado.

---

## Índice de Bitácoras y Registros Activos en el Repositorio

1. **Bitácora general cronológica (este archivo):** [`docs/00-bitacora-proyecto.md`](file:///d:/Proyectos/Widget%20windows/docs/00-bitacora-proyecto.md)
2. **Matriz tecnológica y versiones fijadas:** [`docs/01-matriz-tecnologica.md`](file:///d:/Proyectos/Widget%20windows/docs/01-matriz-tecnologica.md)
3. **Decisiones de arquitectura y patrones comprobados:** [`docs/02-decisiones-arquitectura.md`](file:///d:/Proyectos/Widget%20windows/docs/02-decisiones-arquitectura.md)
4. **Matriz de compatibilidad real por aplicación:** [`docs/03-matriz-compatibilidad.md`](file:///d:/Proyectos/Widget%20windows/docs/03-matriz-compatibilidad.md)
5. **Registro formal de validaciones y pruebas (`V-001` a `V-015`):** [`docs/04-registro-validaciones.md`](file:///d:/Proyectos/Widget%20windows/docs/04-registro-validaciones.md)
6. **Metodología y registro de mediciones de rendimiento:** [`docs/05-mediciones-rendimiento.md`](file:///d:/Proyectos/Widget%20windows/docs/05-mediciones-rendimiento.md)
7. **Bitácora de ejecución en tiempo real de la aplicación (JSONL con rotación y privacidad):** [`artifacts/runtime-verification/logs/diagnostics.jsonl`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/logs/diagnostics.jsonl)
8. **Guía operativa, de instalación MSIX y diagnóstico del piloto:** [`docs/06-guia-piloto-y-diagnostico.md`](file:///d:/Proyectos/Widget%20windows/docs/06-guia-piloto-y-diagnostico.md)
9. **Evidencias JSON generadas por las sondas reales:**
   - [`artifacts/runtime-verification/phase1-startup-receipt.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase1-startup-receipt.json)
   - [`artifacts/runtime-verification/phase2-viability-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase2-viability-report.json)
   - [`artifacts/runtime-verification/phase4-ui-themes-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase4-ui-themes-report.json)
   - [`artifacts/runtime-verification/phase5-performance-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase5-performance-report.json)

---

## Entrada 1 — Fase 0: Investigación, Decisiones e Instalación de Herramientas (29/10/2026)

- **Estado inicial del equipo (`V-001` a `V-005`):**
  - Sistema operativo: Windows 11 Build `10.0.26300.0` (`win-x64`).
  - Aplicaciones detectadas en el sistema:
    - `Microsoft.WindowsAppRuntime.2`: `2.6.4.0`
    - `SpotifyAB.SpotifyMusic`: `1.306.1921.0` (`SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`)
    - `AppleInc.AppleMusicWin`: `1.1792.39682.0`
    - `5319275A.WhatsAppDesktop`: `2.2641.101.0` (`5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App`)
  - Herramientas de Windows SDK disponibles en `C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64` (`makeappx.exe`, `signtool.exe`).
- **Incidente registrado y resolución (`V-006`, `V-007`):**
  - *Problema:* `winget search` bloqueó pidiendo confirmación interactiva `[Y/N]` de `msstore`, y el instalador `.exe` de `winget install Microsoft.DotNet.SDK.10` devolvió código `0x32` en modo silencioso o quedó bloqueado esperando elevación UAC.
  - *Solución aplicada:* Se instaló `.NET SDK 10.0.403` (Runtime `10.0.14`) directamente dentro del espacio de trabajo (`d:\Proyectos\Widget windows\.dotnet`) mediante el script oficial `dotnet-install.ps1` sin requerir UAC global, fijando [`global.json`](file:///d:/Proyectos/Widget%20windows/global.json) a `10.0.403` y [`nuget.config`](file:///d:/Proyectos/Widget%20windows/nuget.config) con caché local en `.nuget/packages`.

---

## Entrada 2 — Fase 1: Base Estructural y Empaquetado MSIX (29/10/2026)

- **Acciones ejecutadas (`V-008` a `V-011`):**
  - Se creó la solución [`Island.sln`](file:///d:/Proyectos/Widget%20windows/Island.sln) con 4 proyectos de código (`Island.Core`, `Island.Application`, `Island.Windows`, `Island.App`) y 3 proyectos de pruebas (`Island.Core.Tests`, `Island.Application.Tests`, `Island.Windows.IntegrationTests`).
  - Se fijaron las versiones exactas de paquetes: `Microsoft.WindowsAppSDK 2.6.4`, `CommunityToolkit.Mvvm 8.4.2`, `xunit 2.9.3`.
  - Se corrigieron las 6 advertencias `MVVMTK0045` migrando [`MainViewModel.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/ViewModels/MainViewModel.cs) a propiedades parciales (`[ObservableProperty] public partial ...`), logrando `0 Advertencias / 0 Errores`.
  - Se implementaron persistencia atómica y recuperación ante corrupción ([`JsonSettingsStore.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Configuration/JsonSettingsStore.cs)), validador declarativo de temas ([`ThemeValidator.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Core/Theming/ThemeValidator.cs)) y bitácora rotativa con privacidad por defecto ([`BoundedDiagnosticLogger.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Diagnostics/BoundedDiagnosticLogger.cs)).
  - Se creó [`scripts/package-msix.ps1`](file:///d:/Proyectos/Widget%20windows/scripts/package-msix.ps1), generando y firmando `artifacts/IslandWidget.Dev_0.1.0.0_x64.msix` con certificado `CN=IslandWidgetDev` (`Thumbprint: 4D3DC3503A092870A6B3FA54B8A9E490342CE7C2`).
- **Resultado:** 18/18 pruebas superadas y recibo de arranque real en [`phase1-startup-receipt.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase1-startup-receipt.json).

---

## Entrada 3 — Fase 2: Viabilidad Nativa en Vivo (29/10/2026)

- **Acciones ejecutadas (`V-012`, `V-013`):**
  - Se construyeron los adaptadores nativos en `Island.Windows`:
    - [`GsmtcMediaSessionProvider.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Media/GsmtcMediaSessionProvider.cs)
    - [`CoreAudioEndpointInspector.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Audio/CoreAudioEndpointInspector.cs)
    - [`WindowsNotificationListenerProbe.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Notifications/WindowsNotificationListenerProbe.cs)
    - [`WindowsDisplayAndWindowProber.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/WindowsDisplayAndWindowProber.cs)
- **Hallazgos empíricos comprobados en [`phase2-viability-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase2-viability-report.json):**
  1. **Ventana y Click-Through:** Con región redondeada (`CreateRoundRectRgn` + `SetWindowRgn`), `WindowFromPoint` fuera de la isla atraviesa hacia la ventana subyacente (`ExteriorTransparentClickThroughPassed = true`), mientras que dentro de la isla impacta en `Microsoft.UI.Content.DesktopChildSiteBridge` (`InteriorIslandHitTestPassed = true`), preservando el foco de la ventana activa (`PassiveShowPreservedForegroundFocus = true`).
  2. **Spotify y GSMTC:** Se leyeron en vivo 2 sesiones (`SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify` reproduciendo `"striptease"` de `"carwash"` con carátula de `155,257` bytes y `Brave`).
  3. **CoreAudio:** Volumen maestro al `70%` y 6 sesiones WASAPI frente a 2 sesiones GSMTC (confirmado que no existe mapeo 1:1).
  4. **WhatsApp y `UserNotificationListener` (#6172):** `GetAccessStatus()` devolvió `Allowed` y `GetNotificationsAsync(Toast)` leyó 30 notificaciones activas, incluyendo **5 notificaciones reales de WhatsApp** (`5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App`). Se confirmó empíricamente que suscribirse a `NotificationChanged` sin identidad MSIX lanza `COMException 0x80070490` (incidencia `#6172`), diseñándose el modo híbrido.
- **Resultado:** 27/27 pruebas superadas.

---

## Entrada 4 — Fase 3: Coordinación, Colas, Reloj Simulado y Concurrencia (29/10/2026)

- **Incidente externo:** Corte de energía eléctrica en la zona del usuario justo después de escribir los archivos de la Fase 3. Al restablecerse la energía, se verificó que todos los archivos quedaron íntegros en disco.
- **Acciones ejecutadas (`V-014`, `V-015`):**
  - Se implementaron [`CoordinationModels.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Core/Domain/CoordinationModels.cs), [`MediaSessionCoordinator.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Coordination/MediaSessionCoordinator.cs), [`NotificationQueueCoordinator.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Coordination/NotificationQueueCoordinator.cs), [`IslandStructuralCoordinator.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Coordination/IslandStructuralCoordinator.cs) (con protección generacional `ActiveGenerationId`) y [`HybridNotificationMonitor.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Notifications/HybridNotificationMonitor.cs).
  - Se compilaron los 7 proyectos en `Release` con `0 Advertencias / 0 Errores`.
  - Se ejecutaron las **35 pruebas automatizadas** (`5` en `Island.Core.Tests`, `19` en `Island.Application.Tests` con `SimulatedTimeProvider`, `11` en `Island.Windows.IntegrationTests`), pasando el 100%.

---

## Entrada 5 — Fase 4: Interfaz Interactiva, Temas Declarativos JSON y Multi-DPI (29/10/2026)

- **Acciones ejecutadas (`V-016` a `V-018`):**
  - Se amplió [`ThemeModels.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Core/Theming/ThemeModels.cs) con el tema integrado `DefaultMica` (`builtin.mica.dark`) junto a `DefaultSolid` y `DefaultGlass`.
  - Se construyó [`IslandLayoutCalculator.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Core/Windowing/IslandLayoutCalculator.cs) para calcular geometrías físicas (`ScreenRectPx`) y lógicas (`DIPs`) a través de múltiples monitores y escalas DPI (`100%`, `125%`, `150%`, `200%`), con respaldo automático al monitor primario si el monitor preferido se desconecta y vaciado de área (`0x0`) cuando el estado es `Hidden`.
  - Se construyó [`JsonThemeCatalog.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Theming/JsonThemeCatalog.cs), que exporta a disco los temas declarativos (`solid-dark.json`, `glass-dark.json`, `mica-dark.json`) y valida cualquier tema personalizado con [`ThemeValidator.ValidateJsonBytes`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Theming/ThemeValidator.cs#L50-L96).
  - Se construyó [`IslandWindowController.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/IslandWindowController.cs) y la interfaz compacta/expandida en [`MainWindow.xaml`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml), [`MainWindow.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml.cs) y [`MainViewModel.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/ViewModels/MainViewModel.cs), con soporte de teclado (`TabIndex`, `AutomationProperties.Name`, `Escape` para contraer/desanclar) y atajo global de recuperación (`Alt+Shift+I`, `RecoveryHotKeyRegistered = true`).
- **Evidencia real registrada en [`phase4-ui-themes-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase4-ui-themes-report.json):**
  - `CatalogThemesLoadedFromDisk = 3` (`builtin.glass.dark`, `builtin.mica.dark`, `builtin.solid.dark`).
  - `RecoveryHotKeyRegistered = true`.
  - Materiales efectivos comprobados: `Solid`, `DesktopAcrylic`, `Mica` y `HighContrastSolid` (`HIGH_CONTRAST_ENFORCED`, animaciones desactivadas).
  - Ciclo interactivo comprobado (`Compact` -> `Expanded` `390x196` -> `PinnedOpen` -> `Escape` de regreso a `Compact` `210x38` y `Passive`).
  - Sincronización en vivo en el `MainViewModel`: detectó Spotify reproduciendo `"SPEED DEMON — Justin Bieber"`, volumen maestro `Vol: 70%` y marca de agua de notificaciones activa (`"Sin avisos pendientes"`).
- **Resultado de pruebas (`V-018`):** **45 de 45 pruebas superadas** (`Island.Core.Tests`: 12/12, `Island.Application.Tests`: 22/22, `Island.Windows.IntegrationTests`: 11/11).

---

## Entrada 6 — Fase 5: Robustez, Resiliencia y Medición de Consumo Real en `Release` (29/10/2026)

- **Acciones ejecutadas (`V-019` a `V-022`):**
  - Se implementó [`ProcessPerformanceProbe.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Runtime/ProcessPerformanceProbe.cs) para capturar métricas reales del proceso (`PrivateBytesMb`, `WorkingSetMb`, `ManagedHeapMb`, `CpuUsagePercent`, `HandleCount`, `GdiObjectCount`, `UserObjectCount` mediante `GetGuiResources` y resumen de hardware con `GlobalMemoryStatusEx`).
  - Se implementó [`SystemLifecycleResilienceMonitor.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Runtime/SystemLifecycleResilienceMonitor.cs) para manejar suspensión/reanudación de energía (`OnPowerSuspending` / `OnPowerResumed`), reconexión/desconexión de monitores y cambios de escala DPI (`OnDisplayOrEnvironmentChanged`) y detección de pantalla completa (`EvaluateFullScreenPolicy`).
  - Se ejecutó el arnés de los 6 escenarios obligatorios (`PERF-01` a `PERF-06`) en compilación `Release` sin depurador (`IsDebuggerAttached = false`), generando [`phase5-performance-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase5-performance-report.json) y actualizando [`docs/05-mediciones-rendimiento.md`](file:///d:/Proyectos/Widget%20windows/docs/05-mediciones-rendimiento.md):
    - **Arranque en frío (`ColdStartupToFirstFrameMs`):** `402.01 ms`.
    - **PERF-01 (Oculta / Idle):** `62.07 MB` Private Bytes, `1.63 MB` Managed Heap, `0.444%` CPU inicial.
    - **PERF-02 (Compacta con música activa):** **`0.000%` CPU** (`0 ms` de CPU adicional en `1,506 ms`, 100% conducida por eventos sin *polling*).
    - **PERF-03 (`Solid` vs `Glass`/`DesktopAcrylic` vs `Mica`):** `63.80 MB` (`Solid`), `64.27 MB` (`Glass`, `+0.47 MB`), `64.48 MB` (`Mica`, `+0.68 MB`), `0.000%` CPU.
    - **PERF-04 (Ráfaga de 20 notificaciones):** Procesada en `6.29 ms`, agrupando las 20 notificaciones sin desbordar la cola.
    - **PERF-05 (Uso prolongado — 500 ciclos de cambios de pista y apertura/cierre de regiones):** **`GdiObjectsDelta = 0`** (`55 -> 55`), **`UserObjectsDelta = 0`** (`32 -> 32`), **`Handles = 910 -> 910`**, `ManagedHeapDelta = +0.26 MB` (`0.84 MB -> 1.10 MB`).
    - **PERF-06 (Suspensión/Reanudación y desconexión de monitor):** Recuperación tras suspensión en **`4.708 ms`** con fallback automático al monitor primario; recuperación tras cambio de monitor en **`2.613 ms`**.
  - **Evaluación de Native AOT (`V-021`):** Los 4 proyectos (`Island.Core`, `Island.Application`, `Island.Windows`, `Island.App`) compilaron bajo `-p:IsAotCompatible=true` con **0 Advertencias y 0 Errores**.
- **Resultado de pruebas (`V-022`):** **48 de 48 pruebas superadas** (`Island.Core.Tests`: 12/12, `Island.Application.Tests`: 22/22, `Island.Windows.IntegrationTests`: 14/14).

---

## Entrada 7 — Fase 6: Distribución Piloto, Actualización MSIX y Guía Operativa (29/10/2026)

- **Acciones ejecutadas (`V-023` a `V-025`):**
  - Se actualizaron [`scripts/package-msix.ps1`](file:///d:/Proyectos/Widget%20windows/scripts/package-msix.ps1) y [`scripts/trust-and-install-msix.ps1`](file:///d:/Proyectos/Widget%20windows/scripts/trust-and-install-msix.ps1) para soportar versionado explícito, generando y firmando el paquete piloto actualizado `artifacts/IslandWidget.Dev_0.2.0.0_x64.msix` (246 archivos, firmado con `CN=IslandWidgetDev`, Thumbprint `4D3DC3503A092870A6B3FA54B8A9E490342CE7C2`) junto al paquete inicial `0.1.0.0`.
  - Se creó [`Phase6PilotUpgradeAndPersistenceTests.cs`](file:///d:/Proyectos/Widget%20windows/tests/Island.Application.Tests/Phase6PilotUpgradeAndPersistenceTests.cs) para comprobar la preservación intacta de configuraciones (`settings.json`) y temas personalizados del usuario (`user-pilot-emerald.json`) durante una actualización de `v0.1.0.0` a `v0.2.0.0`, el aislamiento seguro ante esquemas futuros incompatibles (`UNSUPPORTED_SCHEMA_VERSION`) sin borrar el respaldo del usuario y la rotación automática del registro diagnóstico (`diagnostics.jsonl` / `.1`) sin filtrar texto privado.
  - Se redactó [`docs/06-guia-piloto-y-diagnostico.md`](file:///d:/Proyectos/Widget%20windows/docs/06-guia-piloto-y-diagnostico.md) con instrucciones completas de instalación MSIX, permisos de Windows 11, atajos de teclado, creación de temas JSON y diagnóstico.
- **Resultado final de pruebas (`V-025`):** **51 de 51 pruebas superadas** (`Island.Core.Tests`: 12/12 en 55 ms, `Island.Application.Tests`: 25/25 en 182 ms, `Island.Windows.IntegrationTests`: 14/14 en 2 s).

---

## Entrada 8 — Verificación en Escritorio en Vivo y Compatibilidad con Wallpaper Engine (29/10/2026)

- **Diagnóstico de visibilidad reportado por el usuario:**
  - El usuario reportó no ver inicialmente la isla en su pantalla y consultó si **Wallpaper Engine** podía estar afectándola.
- **Hallazgos técnicos comprobados:**
  1. **Efecto del entorno aislado sobre `%LOCALAPPDATA%`, `GSMTC` y `UserNotificationListener`:**
     - Al lanzarse inicialmente sin `--data-dir` dentro del entorno aislado por defecto, el proceso no tenía permiso de escritura en `C:\Users\johan\AppData\Local\IslandWidget` (`UnauthorizedAccessException`) ni acceso al bus `WinRT` de `GSMTC` (`GSMTC_ERROR_UnauthorizedAccessException`) o `UserNotificationListener` (`MONITOR_ACCESS_Denied`).
     - Al lanzarse fuera del aislamiento con `--data-dir "d:\Proyectos\Widget windows\artifacts\live-desktop"`, todos los subsistemas se conectaron en vivo (`EffectiveMaterial=DesktopAcrylic`, `GSMTC_OPERATIONAL` con Spotify detectado, `COREAUDIO_ENDPOINT_OK` y `MONITOR_WATERMARK_SYNC_ACTIVE`).
  2. **Compatibilidad con Wallpaper Engine (`SHQueryUserNotificationState` = `QUNS_BUSY`):**
     - Wallpaper Engine mantiene una superficie Direct3D activa en el fondo del escritorio, haciendo que `SHQueryUserNotificationState` devuelva `QUNS_BUSY` (`2`) permanentemente durante el uso normal del escritorio.
     - En [`WindowsDisplayAndWindowProber.CaptureFullScreenState`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/WindowsDisplayAndWindowProber.cs#L85-L131), se ajustó `ShouldPauseAutomaticExpansions` para que solo pause expansiones automáticas ante pantalla completa exclusiva Direct3D (`QUNS_RUNNING_D3D_FULL_SCREEN = 3`), modo presentación (`QUNS_PRESENTATION_MODE = 4`) o app exclusiva (`QUNS_APP = 7`), ignorando `QUNS_BUSY` (`2`) de Wallpaper Engine.
  3. **Alineación 1:1 del `HWND` y región recortada (`SetWindowRgn`):**
     - En [`IslandWindowController.ApplyLayoutAndRegion`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/IslandWindowController.cs#L81-L139) y [`MainWindow.xaml`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml#L14-L24), se sincronizó el tamaño físico del `HWND` directamente con `PillScreenBoundsPx` (`390x196` en modo expandido, `210x38` en modo compacto) y recorte `(0, 0, Width, Height)`, eliminando cualquier desfase con `DesktopChildSiteBridge`.
     - Además, en el arranque interactivo de escritorio ([`App.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/App.xaml.cs#L237-L254)), la isla se abre inicialmente en modo **Expandido** con tema **Glass (`DesktopAcrylic`)** y borde de acento `#FF38BDF8` en la parte superior central del monitor primario (`\\.\DISPLAY2`, coordenadas `X=765, Y=12, 390x196`).
- **Evidencia en vivo:** Registrada en [`artifacts/live-desktop/logs/diagnostics.jsonl`](file:///d:/Proyectos/Widget%20windows/artifacts/live-desktop/logs/diagnostics.jsonl) (`HWND=0x6CF0BE4`, `Monitor=\\.\DISPLAY2`, `Bounds=(765,12 390x196)`, `Visibility=Expanded`).

---

## Entrada 9 — Rediseño UX/UI de la Isla Dinámica, Anti-Aliasing DWM y Transparencia Glass/Acrylic Real (29/10/2026)

- **Observación visual del usuario (con capturas reales en modo Expandido y Compacto):**
  - El usuario ejecutó directamente la aplicación y envió dos capturas mostrando artefactos escalonados blancos en las esquinas, opacidad excesiva que tapaba el efecto `DesktopAcrylic` (`Glass`), texto técnico de depuración (`DesktopAcrylic (REQUESTED_MATERI...`) y botones grises genéricos de texto (`+`, `-`, `Prev`, `Next`).
- **Causas técnicas diagnosticadas y corregidas:**
  1. **Eliminación de dientes de sierra y halos blancos en las esquinas (`SetWindowRgn` GDI vs `DwmSetWindowAttribute`):**
     - `CreateRoundRectRgn` + `SetWindowRgn` es una máscara GDI de 1 bit sin anti-aliasing que entraba en conflicto con el marco de composición DWM de Windows 11.
     - En [`WindowsDisplayAndWindowProber.ApplyIslandWindowRegionAndStyles`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/WindowsDisplayAndWindowProber.cs#L136-L228), cuando la ventana de la isla está ajustada 1:1 a la píldora (`isFullWindowPill`), se limpia la región GDI (`SetWindowRgn(hwnd, 0, true)`) y se aplica nativamente `DwmSetWindowAttribute` con `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (2)` y `DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE (0xFFFFFFFE)`, logrando bordes redondeados con anti-aliasing por hardware sin halos blancos.
  2. **Transparencia real para `Glass` (`DesktopAcrylic`) y `Mica`:**
     - En [`ThemeModels.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Core/Theming/ThemeModels.cs#L117-L163) y [`JsonThemeCatalog.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Theming/JsonThemeCatalog.cs#L115-L128), se redujo la opacidad del tinte interior de `#CC18181B` (`80%` opaco) a `#4D0E1015` (`30%` de tinte traslúcido) con borde especular `#3DFFFFFF` (`1px`) para que el desenfoque real de `DesktopAcrylicBackdrop` y `MicaBackdrop` respire sobre el fondo de pantalla.
  3. **Rediseño integral de UX/UI en [`MainWindow.xaml`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml), [`MainWindow.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml.cs) y [`MainViewModel.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/ViewModels/MainViewModel.cs):**
     - Se reemplazaron los botones grises genéricos por estilos personalizados (`PillSubtleButtonStyle`, `IconCircleButtonStyle`, `AccentCircleButtonStyle`) e iconografía nativa `Segoe Fluent Icons` (glifos de Play/Pause, pista anterior/siguiente, emblema musical, volumen/silencio, pin de fijación y chevrons de expansión/contracción).
     - Se limpió la cabecera reemplazando las cadenas de depuración por etiquetas limpias (`Isla • Glass`, `Isla • Mica`, `Isla • Sólida`, `Normal` / `Privado` / `Pausado`).
     - Se ampliaron las dimensiones de la píldora compacta a `260 x 40 DIPs` (con punto luminoso e insignia `PLAY`/`PAUSA`) y la expandida a `420 x 214 DIPs`.
  4. **Publicación lista para abrir directamente por el usuario:**
     - Se publicó la aplicación compilada en [`d:\Proyectos\Widget windows\AppListaParaUsar\Island.App.exe`](file:///d:/Proyectos/Widget%20windows/AppListaParaUsar/Island.App.exe).

---

## Entrada 10 — Interacción Dinámica en 3 Niveles (Hover, Clic y Clic Exterior) y Ticker Musical Marquee (29/10/2026)

- **Solicitudes de experiencia de usuario (UX) móvil:**
  - Implementar **3 niveles de visualización** guiados por el cursor y clics como en la *Dynamic Island* de celulares:
    1. **Nivel 1 (Cápsula cerrada mínima en reposo: `128 x 28 DIPs`):** Estado discreto superior con punto luminoso e icono musical.
    2. **Nivel 2 (Píldora intermedia al acercar el cursor / Hover: `260 x 40 DIPs`):** Se abre sola al pasar el cursor por encima sin necesidad de hacer clic, mostrando la insignia `PLAY`/`PAUSA` y el título de la canción desplazándose horizontalmente.
    3. **Nivel 3 (Panel expandido completo al hacer clic: `420 x 214 DIPs`):** Se abre al hacer clic sobre la píldora y **se contrae automáticamente al hacer clic en cualquier otro lugar fuera de la isla** (salvo que el usuario pulse `Fijar`).
  - Añadir **desplazamiento horizontal continuo (*marquee*)** para los títulos largos de canciones tanto en la vista intermedia como en la expandida.
  - Garantizar que al abrir el nuevo ejecutable se cierren automáticamente instancias anteriores en ejecución para que los estilos nuevos siempre se apliquen de inmediato.
- **Cambios implementados:**
  - En [`MainViewModel.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/ViewModels/MainViewModel.cs#L73-L85) y [`IslandWindowController.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/IslandWindowController.cs#L81-L135): soporte para los 3 niveles geométricos (`IsMiniClosedMode`, `IsCompactMode`, `IsExpandedMode`), `SetCursorHover(bool)` y `CollapseFromOutsideClick()`.
  - En [`WindowsDisplayAndWindowProber.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/WindowsDisplayAndWindowProber.cs#L242-L256) y [`App.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/App.xaml.cs#L201-L236): detector ligero de cursor (`IsCursorInsideHwnd`) y clic exterior (`IsLeftMouseButtonPressed`).
  - En [`MainWindow.xaml`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml) y [`MainWindow.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml.cs#L88-L157): animación de ticker horizontal (*marquee*) a ~30 FPS con pausa en los extremos y reinicio automático al cambiar de pista.
  - Publicado en [`d:\Proyectos\Widget windows\IslaDinamica_v2\Island.App.exe`](file:///d:/Proyectos/Widget%20windows/IslaDinamica_v2/Island.App.exe).

---

## Entrada 11 — Estabilización de Timers GC, Publicación v3 y Preparación de Repositorio GitHub (30/10/2026)

- **Estabilización de Ciclo de Vida de Timers WinUI 3:**
  - Se identificó que las instancias de `DispatcherQueueTimer` no deben crearse como variables locales dentro de métodos de inicio para evitar recolección prematura por el Garbage Collector (`GC`). Se mantuvieron como campos privados permanentes de clase en [`App.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/App.xaml.cs) y [`MainWindow.xaml.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml.cs).
  - Se configuró la transición fluida de redimensionamiento con interpolación suave (`_resizeAnimationTimer`).
  - Se publicó la compilación validada final en [`d:\Proyectos\Widget windows\IslaDinamica_v3\Island.App.exe`](file:///d:/Proyectos/Widget%20windows/IslaDinamica_v3/Island.App.exe).
- **Preparación de Repositorio Git:**
  - Inicialización de repositorio Git con `.gitignore` exhaustivo (excluyendo cachés locales `.dotnet`, `.nuget`, salidas `bin`/`obj`, paquetes `artifacts` y carpetas de pruebas locales `AppListaParaUsar/`, `IslaDinamica_v*`).
  - Creación de [`README.md`](file:///d:/Proyectos/Widget%20windows/README.md) con documentación técnica en español, arquitectura en 4 capas, guía de compilación y matriz de interacción en 3 niveles.
  - Preparación de commits y verificación de credenciales remotas para GitHub.
  - Repositorio remoto vinculado con éxito: [`https://github.com/OsviTap/Island-windows.git`](https://github.com/OsviTap/Island-windows.git).
  - Push completado exitosamente en rama `main` rastreando `origin/main`.

---

## Entrada 12 — Cierre Total, Ocultación Automática en Juegos/Pantalla Completa y Corrección de los 3 Estados (30/10/2026)

- **Cierre por completo de la aplicación:**
  - Se agregó un botón rojo de apagado/cierre en la barra superior de la vista expandida ([`MainWindow.xaml`](file:///d:/Proyectos/Widget%20windows/src/Island.App/MainWindow.xaml)).
  - Implementación de [`ExitApplicationCompletely`](file:///d:/Proyectos/Widget%20windows/src/Island.App/App.xaml.cs) que detiene todos los `DispatcherQueueTimer` (`_cursorMonitorTimer`, `_resizeAnimationTimer`, `_notificationDiffTimer`, `_fullScreenMonitorTimer`), libera recursos de GSMTC y notificaciones, cierra la ventana y finaliza el proceso de forma limpia.
- **Ocultación total en juegos y aplicaciones a pantalla completa:**
  - Nueva función nativa [`WindowsDisplayAndWindowProber.IsForegroundWindowFullScreenOnIslandMonitor`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/WindowsDisplayAndWindowProber.cs) que detecta si la aplicación en primer plano cubre todo el monitor (juegos en modo pantalla completa exclusiva o ventana sin bordes, vídeos a pantalla completa, F11), distinguiéndola del escritorio de Windows y Wallpaper Engine.
  - Al detectar pantalla completa o cuando el usuario pulsa el nuevo botón "Ocultar" / atajo global `Alt+Shift+I`, la isla se oculta por completo (`SW_HIDE` con `IsSuppressed = true` en [`IslandWindowController.cs`](file:///d:/Proyectos/Widget%20windows/src/Island.Windows/Windowing/IslandWindowController.cs)) para que no estorbe durante partidas ni consuma GPU.
  - Al salir del juego o pulsar de nuevo `Alt+Shift+I`, la isla se restaura automáticamente en su posición y tamaño correctos.
- **Diferenciación estricta de las 3 fases:**
  - **Nivel 1 (Reposo - Cápsula cerrada):** `128 x 28 DIPs` cuando no hay actividad de ratón ni eventos transitorios.
  - **Nivel 2 (Compacto - Vista previa con marquesina):** `260 x 40 DIPs` al pasar el cursor por encima (*hover*) o ante eventos transitorios automáticos (cambio de canción o aviso entrante).
  - **Nivel 3 (Expandido - Centro de control):** `420 x 214 DIPs` **únicamente** cuando el usuario hace clic directo en la píldora. Se repliega automáticamente al hacer clic en cualquier lugar fuera de la isla (salvo si está fijada).
- **Publicación:** Compilado y publicado en [`d:\Proyectos\Widget windows\IslaDinamica_v4\`](file:///d:/Proyectos/Widget%20windows/IslaDinamica_v4/Island.App.exe) y sincronizado en [`d:\Proyectos\Widget windows\AppListaParaUsar\`](file:///d:/Proyectos/Widget%20windows/AppListaParaUsar/Island.App.exe).


