# ADR-002: GigE Vision sobre Ethernet como protocolo de comunicación

- **Estado**: Aceptado (impuesto por hardware)
- **Fecha**: 2026-03-15
- **Contexto**: Integración con cámara MV-SC3050M-08M-WBN

---

## Contexto

El modelo MV-SC3050M-08M-WBN tiene una única interfaz de comunicación: **GigE Vision** (Gigabit Ethernet). No existe opción de USB, serial RS-232/485, ni ninguna otra interfaz de datos para streaming de imágenes.

La cámara dispone de salidas digitales de I/O (señales eléctricas para trigger y resultado) pero estas no transportan la imagen ni el JSON de resultados detallado.

## Decisión

Comunicación vía **GigE Vision sobre Ethernet**, gestionada íntegramente por el SDK (ver ADR-001). No se usarán las salidas de I/O digitales como canal principal de integración.

## Arquitectura de red

```
[PC de integración]
      NIC Gigabit
           |
    Switch Gigabit (o conexión directa)
           |
      NIC Gigabit
[MV-SC3050M-08M-WBN]
```

### Opciones de configuración IP

La cámara soporta tres modos (reportados en `MV_VS_DEVICE_INFO.nIpCfgOption`):

| Bit | Modo | Descripción |
|---|---|---|
| bit31 | Static | IP fija configurada manualmente |
| bit30 | DHCP | Asignación automática por servidor DHCP |
| bit29 | LLA | Link-Local Address (169.254.x.x) automática |

**Recomendación para entorno de producción**: IP estática para evitar cambios de dirección que interrumpan la conexión.

### Constantes SDK

```csharp
MV_VS_IP_CFG_STATIC  = 0x05000000
MV_VS_IP_CFG_DHCP    = 0x06000000
MV_VS_IP_CFG_LLA     = 0x04000000
```

## Protocolo interno (informativo)

GigE Vision usa:
- **GVCP** (GigE Vision Control Protocol) sobre UDP puerto 3956 → comandos de control y configuración de parámetros GenICam
- **GVSP** (GigE Vision Stream Protocol) sobre UDP → streaming de frames de imagen

El SDK abstrae completamente ambos protocolos. Esta información es relevante solo para diagnóstico de red (Wireshark, firewalls).

## Implicaciones de red

- La cámara y el PC deben estar en la **misma subred** (o con routing configurado para GVCP/GVSP).
- Configurar **Jumbo Frames** (MTU 9000) en la NIC del PC mejora el throughput y reduce la fragmentación de paquetes de imagen, especialmente a 5 MP.
- Deshabilitar el firewall de Windows para la interfaz de red conectada a la cámara, o crear reglas explícitas para UDP en los puertos GigE Vision.
- En Windows, instalar el **Hikrobot GigE Vision Filter Driver** (incluido con SCMVS) para mejor rendimiento en la NIC.

## Alternativas consideradas

| Alternativa | Evaluación |
|---|---|
| I/O digital (señales eléctricas) | Solo OK/NG como señal binaria; sin imagen ni detalle de resultado |
| USB | No disponible en este modelo |
| Serial RS-232 | No disponible en este modelo |

## Consecuencias

- **Positivo**: Alta velocidad de transferencia (Gigabit); estándar abierto bien documentado.
- **Positivo**: Una sola interfaz transporta imagen + resultados + control.
- **Negativo**: Requiere infraestructura de red Gigabit; sensible a configuración de red (IP, MTU, firewall).
- **Negativo**: Latencia variable en redes con tráfico compartido; para aplicaciones de tiempo real considerar red dedicada.
