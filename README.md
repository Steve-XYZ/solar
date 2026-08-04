# Solar BMS Monitor

MVP Android-first en .NET 10 MAUI para observar localmente por BLE una batería residencial MUST LP16-24300. La aplicación es estrictamente de solo lectura: escanea anuncios `PC-*`, conecta como cliente GATT, exporta el perfil descubierto y solo habilita consultas PaceEX cuando verifica el perfil esperado.

## Estado

- Implementado: permisos Android 12+, escaneo limitado a 12 s, filtro `PC-*`, conexión con tres intentos como máximo, backoff/enfriamiento, descubrimiento GATT, enumeración de descriptores, verificación `FFF0/FFF1/FFF2 + CCCD`, notificaciones, frames TX/RX, JSON diagnóstico y liberación de `BluetoothGatt`.
- Implementado: parser PaceEX candidato, CRC Modbus, ensamblado incremental, cuatro consultas de lectura en lista blanca, telemetría, cálculos, SQLite con muestreo y exportación CSV.
- Privacidad por defecto: la captura y persistencia de frames crudos está desactivada hasta que el usuario active el modo diagnóstico.
- Validado automáticamente: solución Android completa sin advertencias y 18 pruebas unitarias.
- Pendiente de hardware: confirmar UUID, write type, fragmentación y todos los campos con una MUST LP16-24300 de 8 celdas. Hasta entonces el soporte no se declara completo.

## Requisitos

- macOS con .NET SDK 10.0.301 o una revisión compatible de .NET 10.
- JDK 21. El toolchain Android de .NET 10 no admite el JDK 26 que estaba activo originalmente en este equipo.
- Workload Android de MAUI 10:

```bash
dotnet workload install maui-android
```

- Android SDK y un dispositivo físico Android 8.0/API 26 o superior con BLE.
- Depuración USB habilitada para instalación local.

No baje a .NET 9. Para validar este workspace se instalaron localmente el workload `maui-android` 10.0.20 y Homebrew `openjdk@21` 21.0.12. No se enlazó ni sustituyó el JDK global del equipo.

## Compilar y probar

Desde la raíz:

```bash
export JAVA_HOME=/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home
dotnet workload list
dotnet restore SolarBmsMonitor.sln
dotnet build SolarBmsMonitor.sln --no-restore
dotnet test tests/SolarBmsMonitor.Tests/SolarBmsMonitor.Tests.csproj --no-build
dotnet format SolarBmsMonitor.sln --verify-no-changes
```

Para compilar solo Android:

```bash
dotnet build src/SolarBmsMonitor.App/SolarBmsMonitor.App.csproj -f net10.0-android
```

La instalación en teléfono debe hacerse únicamente con autorización del propietario del dispositivo. Tras instalar desde Visual Studio, Rider o `dotnet build -t:Run`, acepte el permiso de “Dispositivos cercanos”. En Android 11 o anterior también se solicita ubicación durante el uso porque el sistema la exige para escanear BLE.

## Probar con la batería

1. Cierre completamente BMS-TOOL y cualquier otra aplicación conectada al BMS.
2. Encienda la batería y acerque el teléfono; como referencia inicial, procure RSSI mejor que -75 dBm.
3. Abra **Dispositivos**, pulse **Escanear 12 s** y seleccione el dispositivo `PC-*`.
4. Abra **Diagnóstico** y compruebe servicios, características, propiedades, CCCD y MTU.
5. Solo si la aplicación muestra “canal PaceEX verificado”, pulse **Actualizar telemetría**.
6. Compare SOC, voltaje, corriente, celdas y temperaturas con BMS-TOOL en sesiones separadas.
7. Exporte el JSON diagnóstico antes de interpretar campos que no coincidan.

Consulte [validación de hardware](docs/hardware-validation.md) para el procedimiento completo.

## Seguridad y limitaciones

No hay comandos de configuración, control de MOSFET, calibración, reset, firmware ni una API `SendRawCommand`. Los valores no deben controlar automáticamente contactores, cargadores, cargas o protecciones. Esta aplicación no sustituye las protecciones del BMS ni instrumentación certificada.

La convención observada en la referencia es corriente positiva al cargar y negativa al descargar. La UI usa esa convención provisionalmente, pero debe confirmarse con frames reales de esta batería.

Todos los datos quedan locales. Consulte [privacidad y seguridad](docs/privacy-and-safety.md), [arquitectura](docs/architecture.md) y [descubrimiento del protocolo](docs/protocol-discovery.md).
