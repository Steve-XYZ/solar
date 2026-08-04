# Privacidad y seguridad

## Datos

La app no tiene cuenta, backend, analítica, anuncios ni telemetría externa. Guarda en el almacenamiento privado de la aplicación:

- identificador/nombre BLE y RSSI;
- snapshots muestreados;
- eventos y errores;
- perfil GATT;
- frames únicamente si el modo diagnóstico está activo.

Las exportaciones JSON/CSV se crean localmente. Pueden contener MAC, número de serie o patrones operativos; revíselas y anonimícelas antes de compartirlas. Desinstalar la app elimina normalmente su almacenamiento privado; archivos exportados por el usuario deben borrarse por separado.

## Modelo de seguridad

La única escritura BLE permitida es una consulta de lectura enumerada en `PaceExReadQuery`. No existe `SendRawCommand` ni UI de parámetros. No se soporta:

- cambiar protecciones, voltajes o corrientes;
- habilitar/deshabilitar carga, descarga o MOSFET;
- reset, calibración o cambio de celdas;
- fábrica, firmware, CAN/RS485 o direccionamiento.

Los datos BLE pueden ser incompletos, tardíos o incorrectos. Nunca deben accionar automáticamente contactores, cargas, cargadores ni protecciones. El BMS y las protecciones eléctricas independientes conservan toda responsabilidad de seguridad.

## Riesgos residuales

- Un UUID coincidente no prueba por sí solo semántica idéntica; por eso también se verifican propiedades y CCCD y sigue siendo necesaria validación de hardware.
- Android y el BMS pueden conservar estado GATT obsoleto tras una caída. Los intentos y timeouts limitan el impacto, pero puede requerirse enfriamiento.
- `neverForLocation` evita que la app derive ubicación y puede filtrar ciertos beacons según Android. El dispositivo `PC-*` debe verificarse en hardware.
- La autonomía es una estimación, se oculta con pocas muestras o consumo inestable y no debe usarse como garantía.
