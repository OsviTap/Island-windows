# 06. Guía de Instalación Piloto, Uso y Diagnóstico

**Versión del paquete piloto:** `0.2.0.0` (`win-x64`)  
**Fecha de publicación:** 29 de octubre de 2026

---

## 1. Formas de ejecución e instalación

### Opción A: Instalación MSIX firmada (Recomendada para identidad completa de Windows 11)
Los paquetes MSIX firmados se encuentran en `artifacts/`:
- Paquete base inicial (`v0.1.0.0`): `artifacts\IslandWidget.Dev_0.1.0.0_x64.msix`
- Paquete piloto actualizado (`v0.2.0.0`): `artifacts\IslandWidget.Dev_0.2.0.0_x64.msix`
- Certificado de firma (`CN=IslandWidgetDev`, Thumbprint `4D3DC3503A092870A6B3FA54B8A9E490342CE7C2`): `artifacts\IslandWidgetDev.cer`

Para confiar en el certificado de desarrollo e instalar/actualizar el paquete MSIX, abre una ventana de PowerShell **como Administrador** una sola vez y ejecuta:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\trust-and-install-msix.ps1 -Version "0.2.0.0"
```

### Opción B: Ejecución directa del binario compilado (`Release`)
También puedes ejecutar la aplicación directamente sin instalar el MSIX (activando automáticamente el `Bootstrap` de `WindowsAppRuntime 2.6` y el modo híbrido de notificaciones):

```powershell
.\src\Island.App\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\Island.App.exe
```

---

## 2. Permisos requeridos en Windows 11

1. **Acceso a notificaciones (`UserNotificationListener`):**
   - Ve a **Configuración de Windows > Privacidad y seguridad > Notificaciones** (`ms-settings:privacy-notifications`).
   - Asegúrate de que **"Acceso a las notificaciones"** esté **Activado** para las aplicaciones de escritorio / `Island Widget`.
   - Por diseño de privacidad, la isla sincroniza una marca de agua al arrancar (no vuelve a mostrar notificaciones antiguas acumuladas) y nunca borra tus notificaciones del Centro de Actividades de Windows al cerrarse el globo de la isla.
2. **Reproducción multimedia (`GSMTC`) y volumen (`CoreAudio`):**
   - No requieren permisos adicionales. La isla detecta automáticamente sesiones activas de **Spotify**, **Apple Music** y navegadores compatibles.

---

## 3. Controles, Anclaje y Atajos de Teclado

| Acción | Control en Interfaz | Atajo / Comportamiento |
| :--- | :--- | :--- |
| **Expandir / Contraer isla** | Botón `+` (en vista compacta) o `Cerrar` (en vista expandida) | `Tab` + `Enter` / `Espacio` |
| **Contraer y desanclar rápido** | — | Tecla **`Escape`** cuando la isla tiene foco |
| **Recuperar isla desde cualquier app** | Botón `Centrar` (en vista expandida) | Atajo global **`Alt + Shift + I`** |
| **Anclar abierta (`PinnedOpen`)** | Botón `Anclar` / `Desanclar` | Mientras está anclada, ningún temporizador automático la contrae |
| **Cambiar política de avisos** | Botón `Normal` / `Private` / `Paused` | `Private` oculta remitente y mensaje; `Paused` suspende expansiones automáticas |
| **Controlar música (`GSMTC`)** | Botones `Prev`, `Reproducir/Pausar`, `Next` y botón de origen (`Spotify`/`Browser`) | El botón de origen alterna y fija el reproductor activo cuando hay varios abiertos |
| **Controlar volumen maestro (`CoreAudio`)** | Botones `-`, `Vol: %` (Silenciar) y `+` | Controla el dispositivo de salida predeterminado de Windows |
| **Paso de clics (`Click-through`)** | Automático fuera del borde redondeado de la píldora | Todo clic fuera de la píldora visible atraviesa hacia la ventana inferior |

---

## 4. Personalización de Configuración y Temas Declarativos JSON

Los archivos de usuario se almacenan en `%LOCALAPPDATA%\IslandWidget\` (o en el directorio indicado mediante `--data-dir`):

- **Configuración general (`config\settings.json`):**
  - Se guarda mediante escritura atómica (`WriteThrough` + archivo temporal + reemplazo).
  - Si el archivo se corrompe manualmente, la aplicación lo aísla como `settings.json.corrupt.<timestamp>` sin borrarlo y restaura una configuración predeterminada funcional.
- **Temas declarativos (`themes\*.json`):**
  - Al iniciar, se exportan los 3 temas de referencia:
    - `solid-dark.json` (`builtin.solid.dark` — Material `Solid`)
    - `glass-dark.json` (`builtin.glass.dark` — Material `Glass` / `DesktopAcrylic`)
    - `mica-dark.json` (`builtin.mica.dark` — Material `Mica`)
  - Puedes crear un nuevo archivo `.json` en `themes\` con un `id` único (ej. `"custom.mi.tema"`). El validador [`ThemeValidator`](file:///d:/Proyectos/Widget%20windows/src/Island.Application/Theming/ThemeValidator.cs) verifica que no supere `64 KB`, que los colores estén en formato `#RRGGBB` o `#AARRGGBB` y que las dimensiones estén dentro de los límites seguros.

---

## 5. Diagnóstico y Bitácora Local

La bitácora operativa se escribe en `%LOCALAPPDATA%\IslandWidget\logs\diagnostics.jsonl`:
- **Límite de tamaño:** `256 KB` por archivo (rota automáticamente a `diagnostics.jsonl.1`).
- **Privacidad por defecto (`diagnosticsIncludePrivateContent = false`):** Nunca escribe el texto ni el remitente de tus mensajes privados de WhatsApp en disco (aparecen como `[REDACTED]`).
- **Comandos de diagnóstico integrados:**
  - Verificar sondas nativas (Fase 2):
    `Island.App.exe --data-dir "artifacts\runtime-verification" --run-phase2-probes --verify-startup-exit`
  - Verificar interfaz, temas, Alto Contraste y multi-DPI (Fase 4):
    `Island.App.exe --data-dir "artifacts\runtime-verification" --run-phase4-verification --verify-startup-exit`
  - Ejecutar benchmark completo de rendimiento y estrés de 500 ciclos (Fase 5):
    `Island.App.exe --data-dir "artifacts\runtime-verification" --run-phase5-benchmarks --verify-startup-exit`
