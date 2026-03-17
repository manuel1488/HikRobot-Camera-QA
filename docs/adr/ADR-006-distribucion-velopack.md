# ADR-006: Velopack como sistema de instalación y actualización

- **Estado**: Aceptado
- **Fecha**: 2026-03-16

---

## Contexto

TR Vision AI es una aplicación de escritorio WPF distribuida en equipos de clientes industriales. Se necesita:

1. Un instalador profesional que no requiera instalar SCMVS ni el SDK de Hikrobot en el equipo destino.
2. Un mecanismo de actualización automática para poder entregar correcciones y mejoras sin desplazamientos físicos al cliente.

## Decisión

Usar **Velopack** como framework de instalación y auto-actualización.

Se descartó Inno Setup (también evaluado) porque no tiene soporte nativo de auto-actualización.

## Razones

| Criterio | Inno Setup | Velopack |
|---|---|---|
| Instalador `.exe` | ✅ | ✅ |
| Auto-actualización | ❌ | ✅ |
| Delta updates | ❌ | ✅ |
| Integración .NET | Externa | Nativa (NuGet) |
| UAC requerido | Admin | Per-user (sin UAC) |
| Servidor requerido | No | Sí (HTTP estático) |

## Implementación

### Dependencia NuGet

```xml
<PackageReference Include="Velopack" Version="0.0.1298" />
```

### Punto de entrada (`Program.cs`)

Velopack **debe** inicializarse antes que cualquier otra cosa, incluida la UI de WPF:

```csharp
[STAThread]
public static void Main(string[] args)
{
    VelopackApp.Build().Run();

    var app = new App();
    app.InitializeComponent();
    app.Run();
}
```

Requiere declarar el punto de entrada explícito en el `.csproj`:

```xml
<StartupObject>TRVisionAI.Desktop.Program</StartupObject>
```

Y reemplazar `StartupUri` en `App.xaml` por un evento `Startup`:

```xml
<Application ... Startup="OnStartup">
```

### Lógica de actualización (`App.xaml.cs`)

```csharp
private const string UpdateUrl = "https://releases.tworockets.com.mx/trvisionai";

private static async Task CheckForUpdatesAsync()
{
    var mgr = new UpdateManager(new SimpleWebSource(UpdateUrl));

    if (!mgr.IsInstalled) return;

    var update = await mgr.CheckForUpdatesAsync();
    if (update is null) return;

    var result = MessageBox.Show(
        $"Nueva versión: {update.TargetFullRelease.Version}\n¿Actualizar ahora?",
        "Actualización disponible",
        MessageBoxButton.YesNo, MessageBoxImage.Information);

    if (result == MessageBoxResult.Yes)
    {
        await mgr.DownloadUpdatesAsync(update);
        mgr.ApplyUpdatesAndRestart(update);
    }
}
```

La llamada a `CheckForUpdatesAsync` está envuelta en `try/catch` — si el servidor no está disponible, la app arranca normalmente sin error.

### Empaquetado (máquina de desarrollo)

Instalar la CLI de Velopack:

```bash
dotnet tool install -g vpk
```

Publicar y empaquetar:

```bash
dotnet publish TRVisionAI.Desktop -c Release -r win-x64 --self-contained false -o ./publish

vpk pack \
  --packId TRVisionAI \
  --packVersion 1.0.0 \
  --packDir ./publish \
  --icon TRVisionAI.Desktop/Assets/app.ico \
  --outputDir ./releases
```

Esto genera en `./releases/`:
- `TRVisionAI-win-Setup.exe` — instalador inicial
- `TRVisionAI-1.0.0-win-full.nupkg` — paquete completo
- `TRVisionAI-X.X.X-win-delta.nupkg` — delta (versiones posteriores)
- `releases.json` — índice que lee la app para detectar updates

## Requisitos en el equipo destino

| Requisito | Detalle |
|---|---|
| Windows 10/11 x64 | Mínimo |
| .NET 8 Desktop Runtime | Si no se publica self-contained |
| Red con acceso al servidor | Solo para recibir actualizaciones |

Las DLLs nativas de Hikrobot van incluidas en el paquete — no se requiere instalar SCMVS.

## Consecuencias

- **Positivo**: El cliente recibe actualizaciones automáticamente sin intervención manual.
- **Positivo**: Delta updates — solo se descarga lo que cambió, no el instalador completo.
- **Positivo**: Sin UAC en instalaciones per-user.
- **Pendiente**: Requiere servidor HTTP para servir los releases. Ver ADR-007.
