# ADR-005: C# .NET como plataforma de integración

- **Estado**: Aceptado
- **Fecha**: 2026-03-15
- **Contexto**: Integración con cámara MV-SC3050M-08M-WBN

---

## Contexto

El SDK de Hikrobot provee soporte para dos plataformas:

- **C / C++**: Headers nativos + `MvVisionSensorControl.dll`
- **C# (.NET)**: Wrapper managed `MvVSControlSDK.Net.dll` (namespace `MvVSControlSDKNet`)

Adicionalmente, el resto de los proyectos del desarrollador (DA, Inventario) usan **.NET 8 / Blazor Server**, por lo que existe contexto y convenciones establecidas en ese ecosistema.

## Decisión

**Usar C# con .NET 8** y el wrapper managed `MvVSControlSDK.Net.dll`.

Variante de arquitectura a definir cuando se conozca el tipo de aplicación consumidora:
- Worker Service (background service, sin UI)
- Blazor Server (UI web integrada)
- WinForms / WPF (UI de escritorio)
- Librería de clase reutilizable

## Razones

1. **SDK managed disponible**: Hikrobot provee `MvVSControlSDK.Net.dll` en variantes AnyCpu, win32 y win64, con documentación XML (IntelliSense).

2. **Coherencia tecnológica**: El ecosistema del desarrollador ya es .NET; mismas herramientas, patrones y convenciones.

3. **Interop simplificado**: El wrapper managed elimina la necesidad de escribir P/Invoke manual. Los `Marshal.Copy` necesarios están bien acotados al parseo de Chunk Data.

4. **Demos disponibles en C#**: Los ejemplos de referencia de Hikrobot (`GrabImage`, `BasicDemo`) están en C#, lo que reduce la fricción durante la implementación.

5. **Ecosistema**: JSON parsing (System.Text.Json), logging (Microsoft.Extensions.Logging), DI (Microsoft.Extensions.DependencyInjection) disponibles de forma nativa.

## Configuración del proyecto .csproj

Para que el SDK nativo funcione, las DLLs nativas deben estar en el directorio de salida. Configuración mínima:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Platforms>x64</Platforms>   <!-- SDK nativo es win64 -->
  </PropertyGroup>

  <ItemGroup>
    <!-- Referencia al wrapper managed -->
    <Reference Include="MvVSControlSDK.Net">
      <HintPath>C:\Program Files (x86)\SCMVS\Development\SDK\C#\win64\MvVSControlSDK.Net.dll</HintPath>
    </Reference>
  </ItemGroup>

  <!-- DLLs nativas que deben copiarse al output -->
  <ItemGroup>
    <None Include="C:\Program Files (x86)\SCMVS\Development\SDK\C\dll\win64\*.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

> Alternativa más robusta: copiar las DLLs al repositorio en una carpeta `native\win64\` para no depender de la ruta de instalación de SCMVS en producción.

## Restricciones de plataforma

| Restricción | Detalle |
|---|---|
| **Solo Windows** | `MvVisionSensorControl.dll` es una DLL nativa de Windows (x86/x64). No hay soporte Linux/macOS. |
| **Arquitectura x64 recomendada** | El SDK provee win32 y win64; en .NET 8 preferir x64 (AnyCpu con preferencia x64 o directamente x64). |
| **Thread-safety** | Cada instancia de `CDevice`/`CStream` no es thread-safe internamente; usar un hilo dedicado para `GetResultData`. |
| **Tiempo de vida** | `DestroyHandle` debe llamarse incluso en caso de error; usar try/finally o patrón IDisposable. |

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| C++ nativo | Mayor complejidad de gestión de memoria; sin ventaja funcional para este caso |
| Python (ctypes) | No hay wrapper oficial; requiere binding manual del C SDK |
| Java / Kotlin | Sin SDK oficial; misma situación que Python |
| Node.js / Electron | Sin SDK oficial para Node; overhead innecesario |

## Consecuencias

- **Positivo**: Mismo lenguaje y toolchain que el resto de proyectos del desarrollador.
- **Positivo**: Fácil integración futura con Blazor Server si se necesita UI web.
- **Negativo**: Dependencia de Windows para ejecución; no aplica containerización Linux estándar.
- **Acción requerida**: Definir si la integración se implementa como Worker Service standalone, como librería de clase, o embebida en la aplicación consumidora.
