# ADR-004: Extracción de resultados OK/NG mediante Chunk Data JSON

- **Estado**: Aceptado
- **Fecha**: 2026-03-15
- **Contexto**: Integración con cámara MV-SC3050M-08M-WBN

---

## Contexto

La cámara puede comunicar el resultado de inspección (OK/NG) de varias formas:

1. **Salidas digitales I/O** (señales eléctricas en los pines físicos de la cámara)
2. **Chunk Data** adjunto a cada frame de imagen, entregado por el SDK
3. **Polling de parámetros** via `CParam.GetStringValue()` / `GetIntValue()`

Se necesita determinar el mecanismo principal para consumir el resultado en la aplicación.

## Decisión

**Usar el Chunk Data JSON entregado con cada frame** como fuente primaria del resultado OK/NG.

Chunk ID: `60005537` (`CHUNK_RESULT_PORT`)

## Razones

1. **Sincronización garantizada**: El resultado JSON está atómicamente vinculado al frame de imagen que lo generó. No hay condición de carrera entre leer el resultado y leer la imagen.

2. **Riqueza de datos**: El JSON contiene no solo el veredicto global (OK/NG) sino también los resultados detallados por módulo de inspección configurado en SCMVS.

3. **Una sola conexión**: Imagen y resultado viajan juntos en el mismo stream GigE Vision; no se necesita un canal adicional.

4. **Patrón documentado**: Los demos oficiales de Hikrobot (`Program.cs`, `Form1.cs`) implementan exactamente este parseo.

## Estructura del Chunk Data

El buffer `pChunkData` se parsea **de atrás hacia adelante**. Cada chunk tiene un trailer de 8 bytes en big-endian:

```
Offset desde el final del buffer:
  [+0 .. +3]  ChunkLength  (uint32, big-endian) — longitud del contenido
  [+4 .. +7]  ChunkID      (uint32, big-endian) — identificador del chunk
  [+8 .. +8+ChunkLength-1]  Datos del chunk (indexado hacia atrás)
```

### Algoritmo de parseo (C#)

```csharp
uint offset = 0;
byte[] chunk = new byte[frame.nChunkDataLen];
Marshal.Copy(frame.pChunkData, chunk, 0, (int)frame.nChunkDataLen);
var endian = new byte[4];

while (frame.nChunkDataLen > offset)
{
    // Leer ChunkLen (big-endian)
    Array.Copy(chunk, (int)(frame.nChunkDataLen - offset - 4), endian, 0, 4);
    uint chunkLen = BitConverter.ToUInt32(endian.Reverse().ToArray(), 0);

    // Leer ChunkID (big-endian)
    Array.Copy(chunk, (int)(frame.nChunkDataLen - offset - 8), endian, 0, 4);
    uint chunkId = BitConverter.ToUInt32(endian.Reverse().ToArray(), 0);

    if (chunkLen == 0 || chunkLen > frame.nChunkDataLen - offset - 8)
        break; // datos corruptos o fin

    if (chunkId == CHUNK_RESULT_PORT) // 60005537
    {
        byte[] jsonBytes = new byte[chunkLen];
        Array.Copy(chunk, (int)(frame.nChunkDataLen - offset - 8 - chunkLen),
            jsonBytes, 0, (int)chunkLen);
        string json = Encoding.ASCII.GetString(jsonBytes);
        // → parsear json para extraer "Result": "OK" / "NG"
    }
    else if (chunkId == CHUNK_MASK_IMAGE_PORT) // 60005536
    {
        // Header de 16 bytes: ModuleID(4) + Format(4) + Width(4) + Height(4)
        // Resto: bytes de imagen JPEG de la máscara
    }

    offset += 8 + chunkLen;
}
```

### Chunk de imagen de máscara (60005536)

```
[ModuleID: 4B LE][Format: 4B LE][Width: 4B LE][Height: 4B LE][JPEG bytes...]
Format = 1 → JPEG
```

## Estructura JSON del resultado

**Schema confirmado** por el demo oficial `BasicDemo/Form1.cs` (líneas 341-362):

```json
{
  "ScDeviceCurrentSolutionName":   "NombreSolucion",
  "ScDeviceSolutionTotalNumber":   "123",
  "ScDeviceSolutionNgNumber":      "5",
  "ScDeviceSolutionRunningResult": "0"
}
```

| Campo | Tipo | Valores |
|---|---|---|
| `ScDeviceCurrentSolutionName` | string | Nombre de la solución activa en SCMVS |
| `ScDeviceSolutionTotalNumber` | string numérico | Contador acumulado de inspecciones |
| `ScDeviceSolutionNgNumber` | string numérico | Contador acumulado de resultados NG |
| `ScDeviceSolutionRunningResult` | string | **`"0"` = OK, `"1"` = NG** |

> `ScDeviceSolutionRunningResult` es `"0"`/`"1"`, **no** las cadenas `"OK"`/`"NG"`.

Otros campos confirmados:
- La propiedad de imagen en el struct C# es `pImageData` (no `pImage` como en el header C)
- Usar `Encoding.UTF8` (no ASCII) para decodificar el JSON, como hace el demo oficial

## Alternativas descartadas

| Alternativa | Motivo de descarte |
|---|---|
| I/O digital | Solo OK/NG binario; sin imagen ni detalle por módulo; requiere cableado adicional |
| Polling de parámetros (`CParam.GetStringValue`) | No sincronizado con el frame; posible race condition; mayor latencia |
| Protocolo propietario TCP adicional | No documentado ni necesario; el SDK ya lo resuelve |

## Consecuencias

- **Positivo**: Resultado y imagen sincronizados por diseño.
- **Positivo**: Acceso a resultados detallados por módulo de inspección.
- **Negativo**: El parseo de Chunk Data requiere manejo de big-endian y aritmética de offsets; debe encapsularse en una clase dedicada para no contaminar la lógica de negocio.
- **Acción requerida**: Capturar y logear el JSON en crudo durante la integración inicial para confirmar el esquema real del campo `Result` y la estructura de `ModuleResults`.
