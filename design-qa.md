# Design QA

## Alcance y entorno

- Referencia visual original: imagen externa no versionada; se conserva como dirección visual, pero no se publica una ruta local del equipo.
- Emulador: `Medium_Phone_API_36.1`, Android API 36.1, ARM64.
- Captura: 1024 × 2342 px a 420 dpi, equivalente aproximado al viewport objetivo de 390 × 844 dp después de las barras del sistema.
- Artefacto probado: APK `Release` autocontenido con firma de desarrollo. No es un artefacto de producción ni de Play Store.
- Privacidad: las capturas no contienen MAC, números de serie, telemetría ni identificadores BLE reales.

## Evidencia accesible

| Estado | Resultado | Evidencia |
| --- | --- | --- |
| Bluetooth desactivado | El error permanente se muestra inmediatamente y no inicia reintentos. No se observan recortes. | [Captura](docs/qa/emulator-bluetooth-disabled.png) |
| Límite automático | Tras tres escaneos sin dispositivos `PC-*`, la interfaz deja de reintentar y ofrece una instrucción recuperable. | [Captura](docs/qa/emulator-retry-limit.png) |
| Menú secundario | Las cuatro rutas técnicas son visibles, legibles y caben en el viewport. | [Captura](docs/qa/emulator-overflow-menu.png) |

## Resultado de la revisión

- Aprobado en emulador: arranque, permiso de dispositivos cercanos, escaneo automático, Bluetooth desactivado, límite de tres intentos con esperas de 2 s y 4 s, mensaje de recuperación y menú secundario.
- Aprobado por validación automática: la cancelación de la página llega al escaneo activo; los errores permanentes no se reintentan; `Disconnecting` conserva la presentación conectada y `Disconnected` termina la conexión una sola vez; el indicador de frescura usa verde para datos recientes, rojo para datos obsoletos y color neutro sin telemetría.
- No verificado en emulador: Resumen con telemetría fresca/obsoleta. La aplicación no incluye datos simulados y no se puede alcanzar ese estado sin un BMS.
- No verificado en pantalla: el rediseño del Resumen (cabecera sin métricas repetidas, tarjetas de salud y equilibrio, temperatura máxima, punto de precisión y banner de alarmas). Compila sin advertencias en `Debug` y `Release`, pero no se ha renderizado: esta máquina no tiene el paquete `emulator` del SDK de Android. Queda pendiente abrirlo con la vista previa de depuración y comprobar recortes en la altura fija.
- Pendiente de hardware real: perfil GATT, UUID, MTU, write type, ocho celdas, escalas, signo de corriente, recepción de telemetría y comparación con BMS-TOOL.

## Superficies visuales revisadas

- Tipografía e iconos: Open Sans, Font Awesome y la marca se renderizan correctamente en la pantalla de conexión.
- Espaciado: el encabezado, los estados de error, las ondas y el pie permanecen dentro del viewport objetivo.
- Copia: los estados permanentes y el agotamiento de reintentos son distinguibles y accionables.
- Menú: Celdas, Histórico, Diagnóstico e Información son visibles sin scroll ni truncamiento.

## Vista previa de depuración

Las compilaciones `Debug` incluyen una lectura de ejemplo para revisar el Resumen sin batería: tres toques sobre la marca de la pantalla de conexión cargan el ejemplo y abren el Resumen. No se persiste en SQLite y el bloque está entre `#if DEBUG`, así que no existe en `Release`; la aplicación publicada sigue sin datos simulados. Sirve para detectar recortes y desbordes en una pantalla de altura fija, no para validar escalas ni signos, que solo confirma el hardware.

## Seguimiento de hardware

1. Cerrar BMS-TOOL antes de conectar la aplicación.
2. Confirmar el perfil GATT y la telemetría con una MUST LP16-24300 real.
3. Capturar Resumen con datos recientes y después de más de 15 s sin actualización para comprobar verde/rojo en dispositivo.
4. Confirmar cadencia de animación y nitidez de la marca en una pantalla física de 60 Hz.

final result: emulator pass; hardware pending
