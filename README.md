# Isla Dinámica para Windows 11 (Dynamic Island Widget)

Un widget flotante moderno y modular para Windows 11 inspirado en la interacción dinámica de smartphones ("Dynamic Island"), desarrollado con **C# .NET 10**, **WinUI 3** y **Windows App SDK**.

---

## 🌟 Características Principales

- **Arquitectura Limpia & Modular en 4 Capas**:
  - `Island.Core`: Entidades de dominio, contratos (`IMediaSessionProvider`, `IVolumeController`, `INotificationMonitor`, `IThemeCatalog`) y lógica pura sin dependencias de UI.
  - `Island.Application`: Casos de uso, servicios de orquestación, políticas de interacción y gestión de temas.
  - `Island.Windows`: Implementación de contratos con APIs nativas Win32/COM (`GSMTC`, `CoreAudio` Endpoint Volume, `DwmSetWindowAttribute`, `SHQueryUserNotificationState`, multi-monitor DPI).
  - `Island.App`: Capa de presentación WinUI 3 (XAML nativo, animaciones fluidas, materiales visuales y gestos de ratón).

- **Interacción en 3 Niveles (Mobile-like)**:
  - **Nivel 1 (Cápsula Mínima / Reposo)**: Micro-píldora compacta (`128x28 DIPs`) discreta en el borde superior de la pantalla con indicador de estado.
  - **Nivel 2 (Compacto / Hover Preview)**: Expansión reactiva al pasar el cursor (`260x40 DIPs`) con marquesina horizontal continua de título/artista musical y botón de acción rápida.
  - **Nivel 3 (Expandido / Centro de Control)**: Expansión completa al hacer clic (`420x214 DIPs`) con controles de reproducción multimedia (Prev, Play/Pausa, Next, Spotify/Deezer), control de volumen maestro con CoreAudio, monitor de notificaciones y selector de temas.

- **Detección Fuera de Foco (Click Outside)**:
  - Detección precisa de clics fuera de la isla mediante sondeo Win32 (`GetAsyncKeyState`) y coordenadas HWND para replegar automáticamente al nivel de reposo.

- **Estética & Materiales Modernos Windows 11**:
  - Soporte para **Glass (Acrylic translúcido al 30%)**, **Mica** y **Sólido**.
  - Esquinas redondeadas por hardware mediante atributos nativos DWM (`DWMWCP_ROUND`), eliminando bordes dentados y artefactos visuales.
  - Iconografía nativa Segoe Fluent Icons.

- **Resiliencia & Compatibilidad**:
  - Detección de pantalla completa y compatibilidad con Wallpaper Engine (no colapsa indebidamente ante fondos dinámicos `QUNS_BUSY`).
  - Posicionamiento multi-monitor centrado horizontalmente en la parte superior del monitor primario.

---

## 🏗️ Estructura de la Solución

```text
├── docs/                      # Documentación completa y bitácoras de diseño
│   ├── 00-bitacora-proyecto.md
│   ├── 01-matriz-tecnologica.md
│   ├── 02-decisiones-arquitectura.md
│   ├── 03-matriz-compatibilidad.md
│   ├── 04-registro-validaciones.md
│   ├── 05-mediciones-rendimiento.md
│   └── 06-guia-piloto-y-diagnostico.md
├── scripts/                   # Scripts PowerShell de compilación, empaquetado y tests
├── src/
│   ├── Island.Core/           # Capa de Dominio (Modelos e Interfaces)
│   ├── Island.Application/    # Capa de Aplicación (Casos de uso y Orquestadores)
│   ├── Island.Windows/        # Capa de Infraestructura Nativa (Win32, COM, GSMTC)
│   └── Island.App/            # Capa UI WinUI 3 (Vistas XAML, Dispatchers, Animaciones)
├── tests/
│   ├── Island.Core.Tests/
│   ├── Island.Application.Tests/
│   └── Island.Windows.IntegrationTests/
├── Island.sln
└── Directory.Build.props
```

---

## 🚀 Requisitos y Compilación

### Requisitos
- Windows 11 (Build 22000 o superior).
- SDK .NET 10 o .NET 8 / 9 con soporte para `net10.0-windows10.0.19041.0`.
- Windows App SDK 1.6 / 2.6.

### Compilar y Ejecutar Pruebas
```powershell
# Restaurar dependencias y compilar
dotnet build Island.sln -c Release

# Ejecutar suite de pruebas automatizadas (51 pruebas)
dotnet test Island.sln -c Release
```

### Publicación Autónoma (Unpackaged)
```powershell
dotnet publish src/Island.App/Island.App.csproj -c Release -r win-x64 --self-contained true -o ./publish
```

---

## 📄 Licencia

Este proyecto se distribuye bajo los términos acordados para uso personal y demostración técnica en Windows 11.
