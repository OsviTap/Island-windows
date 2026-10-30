# 05. Método y Registro de Mediciones de Rendimiento

**Fecha de actualización:** 29 de octubre de 2026  
**Fase actual:** Fase 5 — Robustez y consumo (Completada y Comprobada)  
**Fuente empírica:** [`artifacts/runtime-verification/phase5-performance-report.json`](file:///d:/Proyectos/Widget%20windows/artifacts/runtime-verification/phase5-performance-report.json)

> [!IMPORTANT]
> Todas las cifras registradas en este documento provienen de la ejecución real del binario compilado en configuración `Release` (`win-x64`), ejecutado fuera del depurador (`IsDebuggerAttached = false`) sobre escenarios reproducibles.

---

## 1. Entorno de referencia comprobado

- **Sistema Operativo:** Microsoft Windows 10.0.26300 (`win-x64`)
- **Hardware del equipo de pruebas:**
  - **Procesadores lógicos:** `16` núcleos lógicos
  - **Memoria física total (RAM):** `24,501 MB` (~24 GB)
- **Configuración de compilación:** `Release`, arquitectura `win-x64`, `SelfContained = true`, sin depurador adjunto (`IsDebuggerAttached = false`).
- **Tiempo de arranque en frío hasta primera ventana/región activa (`ColdStartupToFirstFrameMs`):** **`402.01 ms`** (~0.40 segundos).

---

## 2. Resultados comprobados en los 6 escenarios obligatorios

| ID Escenario | Nombre del escenario | Estado | CPU (%) | Private Bytes (MB) | Working Set (MB) | Managed Heap (MB) | Handles / GDI / User | Evidencia / Observaciones |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **PERF-01** | **Oculta y sin actividad (Idle)** | **Comprobada** | `0.444%` (asentamiento inicial) | `62.07 MB` | `126.62 MB` | `1.63 MB` | `889` / `55` / `30` | Ventana oculta (`SW_HIDE`) y región vaciada (`0x0`), cero temporizadores activos. |
| **PERF-02** | **Compacta con música activa (Event-driven)** | **Comprobada** | **`0.000%`** (`0 ms` de CPU adicional en `1,506 ms`) | `61.99 MB` | `127.69 MB` | `1.63 MB` | `897` / `55` / `32` | Actualización 100% dirigida por eventos sin *polling* en segundo plano. |
| **PERF-03-SOLID** | **Expandida — Material `Solid`** | **Comprobada** | `0.121%` | `63.80 MB` | `129.67 MB` | `1.66 MB` | `898` / `55` / `31` | Línea base de panel expandido (`390x196` px) sin backdrop de composición. |
| **PERF-03-GLASS** | **Expandida — Material `Glass` (`DesktopAcrylic`)** | **Comprobada** | **`0.000%`** | `64.27 MB` (`+0.47 MB` vs Solid) | `129.99 MB` | `1.67 MB` | `907` / `55` / `32` | `DesktopAcrylicBackdrop` activo; sobrecosto de memoria privada inferior a `0.5 MB`. |
| **PERF-03-MICA** | **Expandida — Material `Mica`** | **Comprobada** | **`0.000%`** | `64.48 MB` (`+0.68 MB` vs Solid) | `130.37 MB` | `1.70 MB` | `908` / `55` / `32` | `MicaBackdrop` activo; sin consumo de CPU en reposo visual. |
| **PERF-04** | **Ráfaga de 20 notificaciones consecutivas** | **Comprobada** | **`0.000%`** (procesadas en `6.29 ms`) | `64.48 MB` | `130.47 MB` | `1.72 MB` | `905` / `55` / `31` | Las 20 notificaciones de WhatsApp se agruparon en ráfaga (`CoalescedBurstCount = 20`, `QueueRemaining = 0`). |
| **PERF-05** | **Uso prolongado / Estrés (500 ciclos continuos)** | **Comprobada** | `8.115%` durante la ráfaga de 500 ciclos (`878 ms` totales) | `66.61 MB` -> `72.68 MB` | `131.79 MB` -> `140.89 MB` | `0.84 MB` -> `1.10 MB` (`+0.26 MB`) | Handles: `910 -> 910`<br>GDI: **`55 -> 55` (`Δ = 0`)**<br>User: **`32 -> 32` (`Δ = 0`)** | **Cero fugas** de objetos GDI (`CreateRoundRectRgn`/`SetWindowRgn`), objetos User o Handles tras 500 cambios de estado y pista. |
| **PERF-06** | **Suspensión / Reanudación y cambio de monitor** | **Comprobada** | **`0.000%`** | `72.94 MB` | `141.05 MB` | `1.13 MB` | `910` / `55` / `32` | Suspensión contrajo a `Paused`; reanudación recuperó en **`4.708 ms`** con fallback al monitor primario (`UsedPrimaryFallback = true`); cambio de monitor recuperó en **`2.613 ms`**. |

---

## 3. Evaluación de Native AOT (`Fase 5`)

- **Comprobación ejecutada:** Se compilaron `Island.Core`, `Island.Application`, `Island.Windows` e `Island.App` en modo `Release` con `-p:IsAotCompatible=true` (y se migró `MainViewModel` desde la Fase 1 a propiedades parciales `[ObservableProperty] public partial ...` para cumplir `MVVMTK0045` de CsWinRT/AOT).
- **Resultado (`Comprobado`):**
  - Los 4 proyectos compilan con **0 advertencias y 0 errores** bajo `-p:IsAotCompatible=true`.
  - Con un tiempo de arranque en frío ya medido en **`402.01 ms`** y un heap administrado de **`1.10 MB - 1.72 MB`** en ejecución `Release` estándar (`R2R` / `TieredPGO`), el empaquetado `SelfContained` estándar cumple holgadamente los objetivos de arranque sub-segundo y bajo consumo sin introducir riesgos de recorte (trimming) en el compilador XAML de `WindowsAppSDK 2.6.4`.

---

## 4. Reglas de diseño orientadas al rendimiento (Verificadas)

1. **Arquitectura dirigida por eventos (`Comprobada` en `PERF-01` y `PERF-02`):** Cero temporizadores activos en estado `Hidden` o `Compact` pasivo (`0.000%` CPU en reproducción pasiva).
2. **Transferencia limpia de propiedad de regiones GDI (`Comprobada` en `PERF-05`):** Cuando `SetWindowRgn` tiene éxito, el sistema operativo asume la propiedad del `HRGN` y lo libera al reemplazarlo; solo si `SetWindowRgn` devuelve `0` se invoca `DeleteObject(hRgn)`. Esto garantizó `GdiObjectsDelta = 0` tras 500 ciclos.
3. **Respeto a las preferencias del sistema y energía (`Comprobado` en `PERF-06`):** En suspensión o pantalla completa (`SHQueryUserNotificationState`), el coordinador pasa a `Paused` cancelando inmediatamente cualquier temporizador transitorio.
