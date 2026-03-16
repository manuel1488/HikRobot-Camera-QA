# TRVisionAI — Hikrobot Vision Sensor Integration

**Fabricante / Cliente:** Two Rockets

Integración con la cámara de visión artificial **Hikrobot MV-SC3050M-08M-WBN** (serie SC3000) para obtener resultados de inspección (OK/NG) e imágenes vía SDK oficial.

## Modelo de cámara

| Campo | Valor |
|---|---|
| Modelo | MV-SC3050M-08M-WBN |
| Serie | SC3000 |
| Resolución | 5 MP (2448 × 2048) |
| Sensor | Monocromático |
| Interfaz | GigE Vision (Ethernet) |
| Software | SCMVS (Smart Camera Machine Vision Software) |

## Protocolo de comunicación

La cámara expone una interfaz **GigE Vision** sobre Ethernet estándar. La comunicación se realiza exclusivamente a través del **SDK oficial de Hikrobot** (`MvVSControlSDK.Net.dll`), que abstrae el protocolo subyacente.

```
[Tu aplicación]
      |
      | MvVSControlSDKNet (C# .NET)
      |
[MvVisionSensorControl.dll]
      |
      | GigE Vision / TCP-IP
      |
[MV-SC3050M-08M-WBN]
```

## Estructura del proyecto

```
C:\repos\TRVisionAI\
├── docs\
│   ├── adr\                    # Registros de decisiones de arquitectura
│   │   ├── ADR-001-sdk-oficial.md
│   │   ├── ADR-002-protocolo-gigE.md
│   │   ├── ADR-003-modo-adquisicion.md
│   │   ├── ADR-004-chunk-data-resultados.md
│   │   └── ADR-005-plataforma-dotnet.md
│   └── hikrobot\               # Manuales y datasheets del fabricante
│       ├── SCMVS_User_Manual.pdf
│       ├── UD43382B_SC3000 Series Vision Sensor_Quick Start Guide_V1.2.0_...pdf
│       └── ...
├── CLAUDE.md                   # Guía para Claude Code
└── README.md                   # Este archivo
```

## SDK instalado

El SDK se instala junto con SCMVS en:

```
C:\Program Files (x86)\SCMVS\Development\
├── SDK\
│   ├── C#\
│   │   ├── AnyCpu\MvVSControlSDK.Net.dll   ← referencia principal
│   │   ├── win32\MvVSControlSDK.Net.dll
│   │   └── win64\MvVSControlSDK.Net.dll
│   └── C\
│       ├── include\MvVisionSensorControl.h
│       └── dll\MvVisionSensorControl.dll
└── Demo\
    ├── C#\GrabImage\            ← demo consola C#
    └── C#\Winform\BasicDemo\    ← demo WinForms C#
```

### Clases principales del SDK (.NET)

| Clase | Responsabilidad |
|---|---|
| `CSystem` | Enumeración de dispositivos en red |
| `CDevice` | Ciclo de vida del dispositivo (login/logout) |
| `CParam` | Lectura/escritura de parámetros GenICam |
| `CStream` | Adquisición de frames y resultados |

## Flujo de integración

```
1. CSystem.EnumDevices()
   └── Descubrir cámaras GigE en la red local

2. CDevice.CreateHandle(devInfo)
   └── Crear handle hacia la cámara seleccionada

3. CDevice.Login(usuario, contraseña)
   └── Autenticarse (credenciales configuradas en SCMVS)

4. CParam.SetBoolValue("CommandImageMode", false)
   CParam.SetEnumValue("AcquisitionMode", 2)   // 0=Single, 2=Continuous
   CParam.SetIntValue("ModuleID", 0)            // 0 = imagen completa con dimensiones reales
   └── Configurar modo de operación

5. CStream.StartRun()
   └── Iniciar adquisición

6. loop: CStream.GetResultData(ref frame, timeoutMs)
   ├── frame.pImage + frame.nImageLen  → bytes JPEG de la imagen
   └── frame.pChunkData                → resultados en Chunk Data (ver más abajo)

7. CStream.ReleaseResultData(ref frame)
   └── Liberar buffer interno (obligatorio en cada iteración)

8. CStream.StopRun() → CDevice.Logout() → CDevice.DestroyHandle()
```

## Estructura del Chunk Data (resultados)

Los Chunks se parsean **de atrás hacia adelante** dentro de `pChunkData`. Cada chunk tiene un header de 8 bytes (big-endian):

```
[... datos del chunk ...][ChunkID: 4B BE][ChunkLen: 4B BE]
```

### Chunk IDs relevantes

| ID | Constante | Contenido |
|---|---|---|
| `60005537` | `CHUNK_RESULT_PORT` | JSON con resultado OK/NG |
| `60005536` | `CHUNK_MASK_IMAGE_PORT` | Imagen de máscara del módulo |

### Estructura del Chunk de imagen de máscara

```
[ModuleID: 4B][Format: 4B][Width: 4B][Height: 4B][bytes JPEG...]
```

- `Format = 1` → JPEG

### JSON de resultado (ejemplo esperado)

```json
{
  "Result": "OK",
  "ModuleResults": [ ... ]
}
```

> El esquema exacto del JSON depende de la solución configurada en SCMVS. Capturar y logear el JSON en crudo durante la integración inicial para determinar la estructura real.

## Formatos de imagen soportados

La cámara puede entregar imágenes en varios formatos según configuración:

| Formato | Pixel Type | Notas |
|---|---|---|
| JPEG | `MVVS_PixelType_Jpeg` | Por defecto; comprimido |
| Mono8 | `MVVS_PixelType_Mono8` | Raw monocromático 8-bit |
| RGB8 Packed | `MVVS_PixelType_RGB8_Packed` | Color 24-bit |

## Modos de adquisición

| Valor | Modo | Uso |
|---|---|---|
| `0` | Single frame | Una imagen por trigger |
| `2` | Continuous | Flujo continuo de frames |

## Códigos de error comunes

| Código | Significado |
|---|---|
| `MV_VS_OK (0)` | Éxito |
| `MV_VS_E_HANDLE` | Handle inválido |
| `MV_VS_E_NODATA` | Sin datos (timeout) |
| `MV_VS_E_BUSY` | Dispositivo ocupado o desconectado |
| `MV_VS_E_NETER` | Error de red |
| `MV_VS_E_ACCESS_DENIED` | Sin permisos (credenciales incorrectas) |
| `MV_VS_E_ABNORMAL_IMAGE` | Imagen incompleta (pérdida de paquetes) |

## Prerrequisitos

- SCMVS instalado en `C:\Program Files (x86)\SCMVS\`
- Cámara en la misma subred que el PC (GigE Vision)
- Credenciales de acceso configuradas en SCMVS
- .NET 8+ (o .NET Framework 4.x según target)
- DLLs nativas copiadas al directorio de salida del proyecto

## Referencias

- Manuales: `docs\hikrobot\`
- Demo oficial C#: `C:\Program Files (x86)\SCMVS\Development\Demo\C#\`
- ADRs: `docs\adr\`
