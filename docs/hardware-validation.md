# Validación segura en hardware

## Preparación

1. Cierre BMS-TOOL por completo y desactive cualquier otra integración BLE. El BMS puede tolerar una sola conexión estable.
2. Encienda la MUST LP16-24300 y confirme que sus protecciones físicas funcionan independientemente de la app.
3. Use un teléfono autorizado, cargado y cerca de la batería. No trabaje mientras se modifican cables o protecciones.

## Captura GATT

1. Abra **Dispositivos**, conceda “Dispositivos cercanos” y escanee.
2. Confirme nombre `PC-*`, RSSI y MAC/identificador disponible.
3. Conecte una sola vez; espere el descubrimiento.
4. En **Diagnóstico**, exporte JSON con servicios, características, propiedades, descriptores, MTU, timestamps y errores.
5. Verifique que FFF1 expone Notify/Indicate + 2902 y FFF2 expone un write type. Si no, no consulte telemetría.

## Captura de telemetría

1. Con el canal verificado, active captura diagnóstica y pulse una vez **Actualizar telemetría**.
2. Exporte el JSON inmediatamente.
3. Desconecte y cierre la app.
4. Abra BMS-TOOL por separado y anote SOC, SOH, voltaje, corriente, Ah, ciclos, ocho celdas y temperaturas con timestamp.
5. Repita en reposo, carga conocida y descarga conocida. Nunca fuerce una condición insegura para generar una muestra.
6. Compare raw → valor y confirme signo, escala, conteo y orden. Anonimice MAC y número de serie antes de convertir capturas en fixtures versionados.

No declarar soporte completo hasta que al menos las tres condiciones produzcan frames válidos y valores coherentes, incluidas exactamente ocho celdas.

## Ante timeout o bloqueo BLE

1. No pulse conectar repetidamente. La app limita tres intentos y aplica enfriamiento.
2. Desconecte desde la app y espere 15–30 s.
3. Cierre tanto la app como BMS-TOOL.
4. Si el BMS continúa ocupado, siga únicamente el procedimiento normal del fabricante para apagar/encender la interfaz o batería. No envíe reset ni comandos de control desde esta app.
5. Vuelva a abrir una sola aplicación y conserve el log del fallo.

## Evidencia aún pendiente

- Arranque real del APK en teléfono.
- Permisos en Android 12+ y Android 11 o anterior.
- Repetición de conectar/desconectar sin recursos abiertos.
- Perfil GATT y MTU de LP16-24300.
- Frames de 8S y comparación completa con BMS-TOOL.
- Comportamiento tras salir a segundo plano.
