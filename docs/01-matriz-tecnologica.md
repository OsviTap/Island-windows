# 01. Matriz Tecnológica y de Versiones

**Fecha de actualización:** 29 de octubre de 2026  
**Fase actual:** Fase 0 — Decisiones y versiones

> [!IMPORTANT]
> Este documento distingue explícitamente entre versión del SDK de .NET, versión del runtime de .NET, versión del Windows SDK, versión de Windows App SDK y compilación mínima de Windows admitida. Ninguna numeración se copia ni asume equivalente entre componentes.

---

## 1. Clasificación de estados de evidencia

Cada fila en este documento y en los demás documentos de continuidad utiliza exclusivamente una de estas cuatro etiquetas:

- **Documentada:** Respaldada por documentación oficial de Microsoft, sin prueba de ejecución en este proyecto todavía.
- **Propuesta:** Decisión de diseño o arquitectura definida para el proyecto, sujeta a validación.
- **Comprobada:** Verificada mediante comando, compilación o ejecución real en el equipo de referencia.
- **Pendiente de comprobar:** Requiere ejecución, instalación o prueba específica antes de darse por válida.

---

## 2. Matriz de versiones de plataforma y herramientas

| Componente | Valor / Versión | Estado | Evidencia / Fuente |
| :--- | :--- | :--- | :--- |
| **Sistema Operativo del equipo de desarrollo** | Windows 11 Build `10.0.26300.0` (`win-x64`) | **Comprobada** | `[System.Environment]::OSVersion.Version` ejecutado el 29/10/2026. |
| **Compilación mínima de Windows admitida (`TargetPlatformMinVersion`)** | `10.0.22621.0` (Windows 11 22H2) | **Propuesta (Acordada)** | Definida en Fase 0 con el usuario para asegurar soporte de APIs modernas de ventana y materiales en Windows 11. |
| **Versión de Windows SDK objetivo (TFM `TargetPlatformVersion`)** | `10.0.22621.0` (`net10.0-windows10.0.22621.0`) | **Comprobada** | Compilado sin errores en `Island.Windows`, `Island.App` e `Island.Windows.IntegrationTests`. Herramientas `makeappx.exe` y `signtool.exe` (`10.0.19041.0\x64`) verificadas en empaquetado real. |
| **Versión del SDK de .NET** | `10.0.403` (MSBuild `18.9.15+5a2162bea`) | **Comprobada** | Instalado en `.dotnet\sdk\10.0.403`, fijado en `global.json` y verificado con `.\.dotnet\dotnet.exe --info` el 29/10/2026. |
| **Versión del Runtime de .NET** | `10.0.14` (`Microsoft.NETCore.App` y `Microsoft.WindowsDesktop.App`) | **Comprobada** | Verificado en `.dotnet\shared\` con `.\.dotnet\dotnet.exe --info` y empaquetado como `SelfContained=true` en `Island.App`. |
| **Versión de Windows App SDK (NuGet `Microsoft.WindowsAppSDK`)** | `2.6.4` | **Comprobada** | Restaurado desde NuGet, compilado con `0 Advertencias / 0 Errores` y ejecutado contra `Microsoft.WindowsAppRuntime.2` (`2.6.4.0`). |
| **Arquitectura primaria** | `win-x64` | **Comprobada / Acordada** | Equipo de desarrollo y validación local `x64`. |
| **Arquitectura secundaria** | `win-arm64` | **Pendiente de comprobar** | Se mantendrá como objetivo de compilación cruzada; sin hardware físico ARM64 disponible para pruebas en tiempo de ejecución. |

---

## 3. Dependencias y bibliotecas previstas

| Biblioteca / Tecnología | Uso previsto | Versión fijada | Estado | Condición para aprobar en Fase 1 |
| :--- | :--- | :--- | :--- | :--- |
| **Lenguaje C# (.NET 10 LTS)** | Lógica de dominio, coordinación, integraciones e interfaz | C# 14 / `.NET 10.0` | **Comprobada** | Compilación limpia (`0 Advertencias / 0 Errores`) con `<Nullable>enable</Nullable>` y separación estricta por proyectos. |
| **WinUI 3 (`Microsoft.WindowsAppSDK`)** | Interfaz gráfica nativa, ventanas, composición y materiales | `2.6.4` | **Comprobada** | Restauración NuGet exitosa, compilación XAML/PRI limpia y arranque real verificado en Fase 1. |
| **`CommunityToolkit.Mvvm`** | `ObservableObject` (con propiedades parciales compatibles con CsWinRT/AOT) y `RelayCommand` | `8.4.2` | **Comprobada** | Verificado sin advertencias `MVVMTK0045` en `Island.App`. |
| **`System.Text.Json`** | Configuración y temas declarativos JSON versionados | Incluido en `.NET 10.0.14` | **Comprobada** | Validación de esquemas, escritura atómica y cuarentena/recuperación ante archivos corruptos verificada con pruebas unitarias y ejecución real. |
| **Empaquetado MSIX** | Identidad de paquete, declaración de capacidades (`userNotificationListener`) e instalación | `makeappx.exe` + `signtool.exe` (`10.0.19041.0\x64`) | **Comprobada (empaquetado y firma) / Pendiente importar certificado raíz con Admin** | Paquete `artifacts\IslandWidget.Dev_0.1.0.0_x64.msix` generado (246 archivos) y firmado con `CN=IslandWidgetDev` (`4D3DC3503A092870A6B3FA54B8A9E490342CE7C2`). `Add-AppxPackage` validó el paquete y solicitó confianza en raíz (`0x800B0109`), automatizada en `scripts\trust-and-install-msix.ps1`. |
| **Native AOT** | Optimización potencial de arranque y huella | No requerido en Fase 1-4 | **Pendiente de comprobar (Fase 5)** | No se activa por defecto hasta evaluar compatibilidad real con la versión fijada de WinUI 3 / Windows App SDK y generadores CsWinRT. |
