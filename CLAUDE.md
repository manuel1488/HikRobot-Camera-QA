# CLAUDE.md — Hikrobot Vision Sensor Integration

## Contexto del proyecto

**Fabricante / Cliente:** Two Rockets

Integración con la cámara de visión artificial **Hikrobot MV-SC3050M-08M-WBN** para obtener resultados de inspección (OK/NG) e imágenes. El SDK oficial está instalado en la máquina de desarrollo.

## Rutas importantes

```
C:\repos\TRVisionAI\                          ← raíz del proyecto
C:\repos\TRVisionAI\docs\hikrobot\            ← manuales PDF del fabricante
C:\repos\TRVisionAI\docs\adr\                 ← decisiones de arquitectura

C:\Program Files (x86)\SCMVS\Development\  ← SDK oficial instalado
  SDK\C#\AnyCpu\MvVSControlSDK.Net.dll      ← referencia .NET principal
  SDK\C#\AnyCpu\MvVSControlSDK.Net.XML      ← documentación IntelliSense
  Demo\C#\GrabImage\GrabImage\Program.cs    ← demo consola (referencia clave)
  Demo\C#\Winform\BasicDemo\Form1.cs        ← demo WinForms (referencia clave)
```

## SDK: clases y su uso

```
Namespace: MvVSControlSDKNet

CSystem   → EnumDevices()                   Descubrir cámaras en red
CDevice   → CreateHandle / Login / Logout / DestroyHandle
CParam    → Get/Set IntValue, EnumValue, BoolValue, FloatValue, StringValue
CStream   → StartRun / GetResultData / ReleaseResultData / StopRun
```

### Parámetros de configuración conocidos

| Parámetro | Tipo | Valor típico | Descripción |
|---|---|---|---|
| `CommandImageMode` | Bool | `false` | false = modo trigger/continuo |
| `AcquisitionMode` | Enum | `0`=Single, `2`=Continuous | Modo de adquisición |
| `ModuleID` | Int | `0` | 0 = imagen completa con dimensiones reales |

## Estructura de datos por frame (MV_VS_DATA)

```
pImage / nImageLen     → bytes de la imagen (JPEG por defecto)
nImageWidth/Height     → dimensiones (solo válidas con ModuleID=0)
pChunkData / nChunkDataLen → resultados en Chunk Data al final del frame
```

## Chunk Data: cómo parsear

Los chunks se leen **de atrás hacia adelante** en el buffer `pChunkData`.
Cada chunk: `[datos][ChunkID: 4B big-endian][ChunkLen: 4B big-endian]`

Chunk IDs:
- `60005537` (`CHUNK_RESULT_PORT`) → JSON con resultado OK/NG
- `60005536` (`CHUNK_MASK_IMAGE_PORT`) → imagen de máscara del módulo

El demo de referencia está en `Program.cs` líneas 35-90.

## Convenciones de este proyecto

- **Código en inglés**: variables, métodos, clases, propiedades
- **UI en español**: textos visibles al usuario
- **Tecnología**: .NET 8, C#
- **Patrón**: el SDK no es thread-safe por dispositivo — usar un hilo dedicado para `GetResultData`
- **Siempre liberar**: llamar `ReleaseResultData` después de cada `GetResultData`, incluso en error

## Lo que aún no está definido

- Estructura exacta del JSON de resultado (depende de la solución configurada en SCMVS — capturar en crudo al conectar)
- Tecnología de la aplicación consumidora (Blazor, Worker Service, WinForms, etc.)
- Credenciales de la cámara

## Archivos de referencia para leer antes de escribir código

1. `C:\Program Files (x86)\SCMVS\Development\Demo\C#\GrabImage\GrabImage\Program.cs`
2. `C:\Program Files (x86)\SCMVS\Development\Demo\C#\Winform\BasicDemo\Form1.cs`
3. `C:\Program Files (x86)\SCMVS\Development\SDK\C#\AnyCpu\MvVSControlSDK.Net.XML`
4. `docs\adr\` — leer los ADRs relevantes antes de proponer alternativas

## ADRs en este proyecto

| ADR | Decisión |
|---|---|
| ADR-001 | Usar SDK oficial en lugar de implementar GigE Vision directo |
| ADR-002 | GigE Vision sobre Ethernet como protocolo de red |
| ADR-003 | Modo de adquisición según caso de uso |
| ADR-004 | Chunk Data JSON para extracción de resultados OK/NG |
| ADR-005 | C# .NET como plataforma de integración |
