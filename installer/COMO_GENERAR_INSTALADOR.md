# Cómo generar el instalador

## Requisitos (máquina de desarrollo)

1. **Inno Setup 6** — https://jrsoftware.org/isinfo.php

## Pasos

### 1. Compilar la app en Release

```bash
dotnet publish TRVisionAI.Desktop -c Release -r win-x64 --no-self-contained
```

O desde Visual Studio: Build → Publish → Folder (Release / win-x64)

### 2. Generar el instalador

Abre `installer\TRVisionAI.iss` con Inno Setup Compiler y presiona **Build → Compile** (F9).

El instalador quedará en:
```
installer\Output\TRVisionAI_Setup_v1.0.0.exe
```

---

## Requisitos en el equipo destino

| Requisito | Cómo verificar |
|---|---|
| Windows 10/11 x64 | — |
| .NET 8 Desktop Runtime | `dotnet --list-runtimes` |

El instalador avisa si falta .NET 8 y muestra el link de descarga.

> Las DLLs nativas de Hikrobot (VC++ 2013) ya van incluidas dentro del instalador.
