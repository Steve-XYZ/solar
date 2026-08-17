# Arquitectura

## Decisiones

La solución conserva tres proyectos de producción y uno de pruebas. La dirección de dependencias es:

`App → Core + Protocol.PaceEx` y `Protocol.PaceEx → Core`.

`Core` contiene modelos con unidades explícitas, contratos de BLE/persistencia/exportación y cálculos. `Protocol.PaceEx` recibe bytes y produce modelos; no conoce Android, MAUI ni almacenamiento. `App` compone XAML/MVVM, la implementación Android y SQLite.

La navegación principal de `App` se reduce a Conexión y Resumen, ambas sin desplazamiento vertical. Las vistas técnicas se conservan como páginas secundarias, abiertas desde el menú de tres puntos o tocando la tarjeta del Resumen que cada una desarrolla. El escaneo, las ondas de radar y las transiciones son controles y animaciones nativas de MAUI; Font Awesome Free aporta una iconografía coherente sin añadir código ejecutable de terceros.

## Transporte BLE Android

`AndroidBleMonitorService` usa directamente `Android.Bluetooth` y `Android.Bluetooth.LE`. El flujo es escaneo limitado → `ConnectGatt(autoConnect: false)` → descubrimiento → MTU best-effort → perfil diagnóstico → selección verificable → CCCD → consultas. La pantalla enlaza su ciclo de vida con el escaneo activo y limita el arranque automático a tres intentos; solo la ausencia de dispositivos y los fallos transitorios se reintentan, con esperas de 2 s y 4 s. Permisos denegados, Bluetooth desactivado y BLE no soportado terminan inmediatamente con un estado visible.

Tres semáforos separan sesión, operación GATT y consulta. Ninguna escritura/descripción corre de forma concurrente. Hay timeouts de conexión, GATT, respuesta y fragmento. Cada conexión tiene como máximo tres intentos, backoff 1/2 s y enfriamiento de 15 s; no existe reconexión infinita. Al detenerse la ventana se desconecta.

Con el canal PaceEX verificado, el `MonitorViewModel` solicita telemetría de solo lectura al conectar y cada 5 s. El bucle se cancela al desconectar o cerrar la aplicación; el semáforo de consulta del transporte mantiene una única consulta GATT activa. Cinco muestras recientes permiten estimar autonomía o tiempo de carga, y la potencia positiva se integra entre muestras válidas para mostrar la energía añadida durante la sesión.

La selección del canal no se basa solo en UUID: exige servicio `FFF0`, RX `FFF1` con Notify/Indicate y CCCD `2902`, y TX `FFF2` con Write/WriteWithoutResponse. Un perfil distinto se exporta, pero no recibe consultas.

## Protocolo

El parser valida framing, longitud, CRC y tipo esperado antes de interpretar datos. `PaceExReadQuery` es la frontera de seguridad: la app no expone bytes arbitrarios. Celdas y temperaturas se ensamblan desde el patrón intercalado observado en las pruebas de referencia.

## Persistencia

`SqliteBatteryRepository` usa WAL y tablas locales para dispositivos, snapshots y diagnóstico. Evita filas por segundo: guarda al pasar 30 s o ante cambios significativos de SOC, voltaje, corriente, delta, alarma o estado. Frames se persisten únicamente mientras el modo diagnóstico de UI está activo.

## Dependencias externas

- `Microsoft.Maui.Controls`: framework UI oficial de MAUI 10.
- `Microsoft.Extensions.Logging.Debug`: logging de desarrollo del template oficial.
- `sqlite-net-pcl 1.11.285`: paquete recomendado por la documentación MAUI para SQLite local; publicación activa en julio de 2026 y compatibilidad calculada con `net10.0-android`.
- xUnit/Test SDK/coverlet: solo pruebas.

No se usa `Plugin.BLE`, CommunityToolkit ni librería de gráficos. Esto mantiene el transporte nativo y reduce superficie de dependencias.

## Restricciones del MVP

La conexión solo existe en primer plano. No hay servicio Android persistente, cuenta, nube o telemetría externa. La pantalla histórica inicial ofrece lista y CSV; visualizaciones agregadas y sesiones por apagón requieren primero datos reales suficientes.
