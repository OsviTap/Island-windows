# 03. Matriz de Compatibilidad e Integraciones

**Fecha de actualización:** 29 de octubre de 2026  
**Fase actual:** Fase 2 — Viabilidad nativa (Completada)

---

## 1. Aplicaciones objetivo y estado comprobado en el equipo de referencia

Inventario y resultados empíricos obtenidos en `Windows 11 Build 10.0.26300.0 (win-x64)` mediante `phase2-viability-report.json`:

| Aplicación | Paquete (`Name`) | Versión instalada | `AppUserModelId` detectado en tiempo de ejecución | Estado de integración | Evidencia empírica (`phase2-viability-report.json`) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Spotify** | `SpotifyAB.SpotifyMusic` | `1.306.1921.0` | `SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify` | **Comprobada** | Sesión GSMTC detectada. Metadatos (`Title="striptease"`, `Artist="carwash"`, `AlbumTitle="striptease"`), estado (`Paused`), controles (`CanPlay`, `CanTogglePlayPause`, `CanStop`, `CanSkipNext`, `CanSkipPrevious`, `CanSeek`), `Timeline` válido (`00:01:49.180 / 00:02:28.701`, `73.4%`) y stream de portada (`155,257` bytes) leídos con éxito. |
| **Navegador (Brave / Chromium)** | `Brave` | Instalado | `Brave` | **Comprobada** | Sesión GSMTC concurrente detectada (`IsCurrentSystemSession=true`). Reporta `CanPlay=true`, `CanTogglePlayPause=true`, `CanSeek=true`, pero `CanSkipNext=false` y `CanSkipPrevious=false`, confirmando que los controles deben habilitarse dinámicamente según cada proveedor. |
| **Apple Music** | `AppleInc.AppleMusicWin` | `1.1792.39682.0` | *(Cerrada durante la sonda)* | **Pendiente de comprobar en reproducción activa** | El paquete MSIX está instalado; al estar cerrada la aplicación no publica sesión en GSMTC (`AppleMusicSessionDetected=false`). Se clasifica por `AppleInc.AppleMusicWin` / `AppleMusic` en `GsmtcMediaSessionProvider`. |
| **WhatsApp** | `5319275A.WhatsAppDesktop` | `2.2641.101.0` | `5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App` | **Comprobada** | `UserNotificationListener.GetNotificationsAsync(Toast)` leyó 30 notificaciones activas del sistema, identificando 5 notificaciones reales de WhatsApp con `AppDisplayName="WhatsApp"`, `HasToastGenericBinding=true` y `TextElementsCount=2` (remitente y cuerpo), sin persistir texto privado en disco. |
| **Windows App Runtime 2** | `Microsoft.WindowsAppRuntime.2` | `2.6.4.0` | `Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe` | **Comprobada** | Inicializado en tiempo de ejecución y verificado con `DesktopAcrylicController`, `MicaController`, `SystemBackdropElement` y `AppInstance`. |

---

## 2. Matriz de capacidades verificadas en Fase 2

| Área / Capacidad | API / Mecanismo probado | Clasificación tras Fase 2 | Resultado empírico y decisiones adoptadas |
| :--- | :--- | :--- | :--- |
| **Sesiones multimedia (GSMTC)** | `GlobalSystemMediaTransportControlsSessionManager` | **Comprobada** | Enumera múltiples sesiones simultáneas (`Spotify` + `Brave`), lee metadatos, carátula por stream (`IRandomAccessStreamWithContentType`), capacidades por botón y `TimelineProperties`. Nota: dentro de un sandbox AppContainer restringido lanza `UnauthorizedAccessException`, pero en ejecución de escritorio normal opera al 100% (`GSMTC_OPERATIONAL`). |
| **Control de volumen y sesiones de audio** | `CoreAudio` (`IMMDeviceEnumerator`, `IAudioEndpointVolume`, `IAudioSessionManager2`) | **Comprobada** | Dispositivo de salida predeterminado leído (`MasterVolumeScalar = 0.70`, `IsMasterMuted = false`). Se detectaron 6 sesiones WASAPI frente a 2 sesiones GSMTC, confirmando que el control de volumen en la interfaz debe actuar sobre el volumen maestro del dispositivo de salida sin asumir correspondencia 1:1 con GSMTC. |
| **Escucha de notificaciones y Riesgo #6172** | `UserNotificationListener` (`GetAccessStatus`, `GetNotificationsAsync`, `NotificationChanged`) | **Comprobada (con contingencia verificada para #6172)** | 1. `GetAccessStatus()` devolvió `Allowed` y `GetNotificationsAsync(NotificationKinds.Toast)` leyó las 30 notificaciones activas (5 de WhatsApp).<br>2. **Confirmación empírica del riesgo #6172:** En ejecución sin identidad MSIX (`HasPackageIdentity=false`), suscribirse a `NotificationChanged` lanza `COMException 0x80070490` (`Element not found`).<br>3. **Decisión documentada:** El módulo reporta `PartiallySupported` (`IsOperational=true`) y opera mediante **modo híbrido**: usa suscripción push `NotificationChanged` cuando el entorno MSIX lo admite y conmuta automáticamente a sincronización diferencial por marca de agua (`NotificationId` máximo visto al arrancar) sobre `GetNotificationsAsync` cuando `NotificationChanged` devuelve `0x80070490`. |
| **Ventana flotante, paso de clics (Click-through) y foco** | `OverlappedPresenter`, `WS_EX_TOPMOST`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `CreateRoundRectRgn` + `SetWindowRgn`, `WindowFromPoint` | **Comprobada** | `WINDOW_VIABILITY_VERIFIED`:<br>1. `ExteriorTransparentClickThroughPassed = true` (`WindowFromPoint` en la zona exterior transparente golpea la ventana testigo `Static` debajo y no la isla).<br>2. `InteriorIslandHitTestPassed = true` (`WindowFromPoint` dentro de la píldora golpea `Microsoft.UI.Content.DesktopChildSiteBridge`).<br>3. `PassiveShowPreservedForegroundFocus = true` (mostrar la isla con `SW_SHOWNOACTIVATE` + `WS_EX_NOACTIVATE` preserva el foco de la ventana activa). |
| **Materiales (`Acrylic` / `Mica` / `SystemBackdropElement`)** | `DesktopAcrylicController.IsSupported()`, `MicaController.IsSupported()`, `SystemBackdropElement` | **Comprobada** | Los tres mecanismos están disponibles y verificados en `Microsoft.WindowsAppSDK 2.6.4` sobre Windows 11 Build `26300`. Al usar recorte geométrico `SetWindowRgn` ajustado a la píldora activa, el material `DesktopAcrylicBackdrop` se confina a la región redondeada visible sin bloquear clics exteriores. |
| **Topología multimonitor, DPI y pantalla completa** | `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetDpiForMonitor`, `SHQueryUserNotificationState` | **Comprobada** | Detectados 2 monitores (`\\.\DISPLAY2` primario `0,0 1920x1080` y `\\.\DISPLAY1` secundario `1920,0 1920x1080`, ambos con `WorkArea` de `1920x1032` y `96 DPI / 100%`). `SHQueryUserNotificationState` operativo (`QUNS_BUSY`). |
| **Instancia única** | `Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey` | **Comprobada** | `APPINSTANCE_REGISTERED_PRIMARY` verificado en tiempo de ejecución. |
