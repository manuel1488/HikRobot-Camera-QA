# ADR-003: Modo de adquisición de imágenes

- **Estado**: Pendiente de decisión final (depende del caso de uso)
- **Fecha**: 2026-03-15
- **Contexto**: Integración con cámara MV-SC3050M-08M-WBN

---

## Contexto

El SDK expone el parámetro `AcquisitionMode` (tipo Enum) con al menos dos valores relevantes:

| Valor | Modo | Descripción |
|---|---|---|
| `0` | Single Frame | La cámara captura un frame por cada trigger (software o hardware) |
| `2` | Continuous | La cámara captura frames continuamente hasta que se detenga |

Adicionalmente, el parámetro `CommandImageMode` (Bool) controla si se usa un modo de imagen por comando (valor `true`) o el modo normal (`false`).

El parámetro `ModuleID` (Int) controla qué módulo se reporta:
- `ModuleID = 0`: imagen completa con dimensiones reales (nImageWidth/nImageHeight válidos)
- `ModuleID != 0`: imagen de un módulo específico; las dimensiones reportadas son 0

## Opciones evaluadas

### Opción A — Modo continuo (`AcquisitionMode = 2`)

```csharp
cParam.SetBoolValue("CommandImageMode", false);
cParam.SetEnumValue("AcquisitionMode", 2);
cStream.StartRun();
// hilo dedicado llamando GetResultData() en loop
```

**Características:**
- La cámara genera frames de forma autónoma según su configuración interna (trigger interno, externo o libre)
- `GetResultData(ref frame, timeoutMs)` bloquea hasta que hay un frame disponible o expira el timeout
- Requiere un hilo dedicado para no bloquear la aplicación
- Apropiado cuando la cámara ya tiene configurado su trigger (sensor de presencia, encoder, etc.) en SCMVS

### Opción B — Modo single frame con trigger por software (`AcquisitionMode = 0`)

```csharp
cParam.SetBoolValue("CommandImageMode", false);
cParam.SetEnumValue("AcquisitionMode", 0);
cStream.StartRun();
// cuando se necesita una imagen:
cParam.SetCommandValue("TriggerSoftware");  // dispara una captura
nRet = cStream.GetResultData(ref frame, timeoutMs);
```

**Características:**
- La aplicación controla exactamente cuándo se captura
- Apropiado cuando el proceso productivo es controlado desde el PC (no hay trigger externo)
- Más sencillo de depurar: captura a demanda

## Decisión provisional

**Usar Modo continuo (Opción A)** como punto de partida, ya que:

1. Los demos oficiales de Hikrobot (tanto `GrabImage` como `BasicDemo`) usan `AcquisitionMode = 2`.
2. En una línea de producción, la cámara normalmente tiene su trigger configurado en hardware (sensor de presencia conectado a la I/O digital), lo que hace que el modo continuo sea el natural.
3. El modo continuo permite que la cámara opere de forma autónoma; la aplicación simplemente consume los resultados.

**Esta decisión debe revisarse** cuando se conozca el caso de uso final y cómo está configurado el trigger en SCMVS.

## Patrón de threading obligatorio

Independientemente del modo elegido, `GetResultData` es **bloqueante** y debe ejecutarse en un hilo dedicado:

```csharp
// ✅ Correcto
var thread = new Thread(() => {
    while (!_cancellationToken.IsCancellationRequested)
    {
        int ret = _stream.GetResultData(ref frame, 1000);
        if (ret == CErrorCode.MV_VS_OK)
        {
            ProcessFrame(ref frame);
        }
        _stream.ReleaseResultData(ref frame); // siempre liberar
    }
});
thread.IsBackground = true;
thread.Start();

// ❌ Incorrecto — bloquea el hilo principal/UI
_stream.GetResultData(ref frame, 1000);
```

## Consecuencias

- **ReleaseResultData es obligatorio**: Debe llamarse después de cada `GetResultData`, incluso si el resultado fue error o timeout. No liberar el buffer provoca `MV_VS_E_BUFOVER`.
- **ModuleID = 0**: Siempre configurar antes de `StartRun()` para obtener dimensiones de imagen válidas.
- **Timeout**: Usar 1000 ms como valor por defecto; reducir a 100 ms si se necesita salida rápida del hilo de captura.
