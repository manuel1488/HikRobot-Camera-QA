# ADR-001: Usar el SDK oficial de Hikrobot en lugar de implementar GigE Vision directamente

- **Estado**: Aceptado
- **Fecha**: 2026-03-15
- **Contexto**: Integración con cámara MV-SC3050M-08M-WBN

---

## Contexto

La cámara MV-SC3050M-08M-WBN implementa el estándar abierto **GigE Vision**, lo que en principio permite comunicarse con ella desde cualquier librería compatible (eBUS SDK, Stemmer Common Vision Blox, implementaciones open-source, etc.) o incluso implementando los comandos GVCP/GVSP directamente sobre UDP/TCP.

Hikrobot provee además un **SDK propietario** (`MvVisionSensorControl.dll` / `MvVSControlSDK.Net.dll`) que encapsula toda la comunicación y expone una API de alto nivel orientada a esta familia de sensores de visión (serie SC3000).

## Decisión

**Usar el SDK oficial de Hikrobot** (`MvVSControlSDK.Net.dll`, namespace `MvVSControlSDKNet`).

## Razones

1. **Chunk Data propietario**: Los resultados de inspección (OK/NG, datos de módulos) se entregan en un formato de Chunk Data con IDs propietarios (`60005537`, `60005536`) y estructura específica de Hikrobot. El SDK gestiona este parseo de forma transparente.

2. **Integración con SCMVS**: La cámara se programa y configura desde SCMVS (Smart Camera Machine Vision Software). El SDK está diseñado para interactuar con las soluciones creadas en SCMVS, incluyendo parámetros específicos como `ModuleID`, `AcquisitionMode` con valores propios, y acceso a archivos del dispositivo.

3. **Disponibilidad y soporte**: El SDK está instalado en la máquina de desarrollo (`C:\Program Files (x86)\SCMVS\Development\`), incluye ejemplos funcionales en C# y documentación XML para IntelliSense.

4. **Menor complejidad**: Implementar GigE Vision (GVCP para control + GVSP para streaming) desde cero requiere manejo de UDP, ordenamiento de paquetes, retransmisión, y decodificación de formatos de pixel — trabajo no diferenciador para este proyecto.

5. **Mantenibilidad**: Al usar el SDK del fabricante, actualizaciones de firmware de la cámara tienen mayor probabilidad de ser compatibles hacia adelante.

## Alternativas consideradas

| Alternativa | Motivo de descarte |
|---|---|
| Stemmer CVB / eBUS SDK | Licencias de pago; sin soporte garantizado para Chunk Data propietario de Hikrobot |
| Implementación directa GVCP/GVSP | Complejidad elevada; no agrega valor al proyecto |
| Aravis (open-source, Linux) | Solo Linux; el objetivo es Windows |
| Emgu CV / OpenCV | No cubren la capa de adquisición GigE Vision |

## Consecuencias

- **Positivo**: API de alto nivel, demos funcionales disponibles, soporte de Chunk Data sin trabajo adicional.
- **Negativo**: Dependencia de un SDK propietario de Windows (`MvVisionSensorControl.dll`); la aplicación no es portable a Linux/macOS sin cambios en la capa de adquisición.
- **Restricción**: El SDK es un wrapper P/Invoke sobre DLLs nativas; requiere que las DLLs estén en el PATH o en el directorio de salida del ejecutable.
