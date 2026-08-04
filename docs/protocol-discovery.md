# Descubrimiento PaceEX / PeiCheng

## Nivel de evidencia

La implementación es candidata, no soporte completo confirmado para la MUST LP16-24300. A 4 de agosto de 2026 se revisó `patman15/aiobmsble` en el commit `090e489c6c5034086957ea3dd0d9713d305e418e` y `patman15/BMS_BLE-HA` en `9c42c0ac61df046a26c6d5f1da54976684aa5f60`.

Fuentes primarias:

- [Android BLE overview](https://developer.android.com/develop/connectivity/bluetooth/ble/ble-overview), [permisos](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions), [escaneo](https://developer.android.com/develop/connectivity/bluetooth/ble/find-ble-devices), [conexión GATT](https://developer.android.com/develop/connectivity/bluetooth/ble/connect-gatt-server) y [transferencia](https://developer.android.com/develop/connectivity/bluetooth/ble/transfer-ble-data).
- [.NET MAUI 10: código específico de plataforma](https://learn.microsoft.com/dotnet/maui/platform-integration/invoke-platform-code?view=net-maui-10.0) y [SQLite local](https://learn.microsoft.com/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0).
- [`aiobmsble/pace_bms.py`](https://github.com/patman15/aiobmsble/blob/main/aiobmsble/bms/pace_bms.py), [pruebas PaceEX](https://github.com/patman15/aiobmsble/blob/main/tests/bms/test_pace_bms.py), [PR 59](https://github.com/patman15/aiobmsble/pull/59) e [issue 17](https://github.com/patman15/aiobmsble/issues/17).
- [MUST LP16-48100 observada como PC-9662](https://github.com/patman15/BMS_BLE-HA/issues/521). Es evidencia de familia MUST/PaceEX, no prueba del modelo LP16-24300.
- [Hard timeout de conexión en aiobmsble, PR 146](https://github.com/patman15/aiobmsble/pull/146). No se encontraron issues PaceEX recientes y específicos de Android; las medidas de estabilidad del MVP se basan además en las restricciones del encargo y el comportamiento general documentado por BMS_BLE-HA.

## Perfil candidato

| Elemento | UUID | Evidencia | Estado local |
|---|---|---|---|
| Servicio | `0000FFF0-0000-1000-8000-00805F9B34FB` | `pace_bms.py` | Debe descubrirse |
| Notificación | `0000FFF1-0000-1000-8000-00805F9B34FB` | `pace_bms.py` | Debe tener Notify/Indicate y CCCD |
| Escritura | `0000FFF2-0000-1000-8000-00805F9B34FB` | `pace_bms.py` | Debe tener Write/WriteWithoutResponse |
| CCCD | `00002902-0000-1000-8000-00805F9B34FB` | estándar GATT | Debe estar en FFF1 |

No se envía nada si el perfil real no satisface esas condiciones.

## Frame

`9A | tipo/comando (6 bytes) | longitud (1 byte) | payload (N) | CRC16 Modbus (2 bytes big-endian) | 9D`

Longitud total: `11 + N`. El CRC se calcula desde `9A` hasta el último byte del payload, sin incluir CRC ni `9D`. El ensamblador tolera fragmentos y múltiples frames por notificación, descarta basura previa a `9A`, aplica timeout y rechaza CRC inválido.

## Lista blanca de lectura

| Consulta | Frame TX de referencia |
|---|---|
| Sistema | `9A00000A0000000019519D` |
| Celdas/temperaturas | `9A00000A020000020101289C9D` |
| Número de serie | `9A00000002000000A0C89D` |
| Versiones | `9A00000001000000E4C89D` |

No hay API pública para construir otro comando. En el MVP solo Dashboard invoca sistema y celdas; serie/versiones quedan disponibles para una futura lectura best-effort de metadatos.

## Campos interpretados

Respuesta de sistema, offsets relativos al payload:

- pack count: byte 0;
- corriente: bytes 1–4, signed int32 BE / 100;
- voltaje: bytes 5–8, uint32 BE / 100;
- capacidad restante: bytes 9–12, uint32 BE / 100 Ah;
- capacidad de diseño: bytes 13–16, uint32 BE / 100 Ah;
- SOC: byte 21; SOH: byte 22; ciclos: bytes 23–26 uint32 BE.

Respuesta `0A02`: número de celdas en payload 3. Desde payload 4 se intercalan valores de 2 bytes con hueco de 2 bytes; las celdas se expresan en mV / 1000. Las primeras seis posiciones intercaladas se interpretan como temperaturas `(raw - 2731) / 10`; solo se conservan rangos plausibles. La prueba de referencia devuelve cuatro temperaturas válidas.

## Desconocido o no confirmado

- Que la LP16-24300 exponga exactamente esos UUID y comandos.
- Write con respuesta frente a WriteWithoutResponse en este módulo BLE.
- Convención de signo de corriente en esta batería concreta.
- Alarmas/problem code, capacidad completa, packs paralelos, firmware y hardware.
- Número/orden real de sensores de temperatura en el modelo 8S.
- Campos de energía de carga/descarga y sesiones por apagón.

La captura necesaria es un JSON GATT y pares TX/RX de sistema/celdas comparados con BMS-TOOL, con carga, descarga y reposo conocidos.
