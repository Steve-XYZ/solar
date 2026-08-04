# Guía del repositorio

## Estructura

- `src/SolarBmsMonitor.App`: MAUI, XAML/MVVM, SQLite y BLE Android nativo.
- `src/SolarBmsMonitor.Core`: modelos, contratos y cálculos sin plataforma.
- `src/SolarBmsMonitor.Protocol.PaceEx`: framing, CRC, lista blanca y parser sin Android/MAUI/SQLite.
- `tests/SolarBmsMonitor.Tests`: pruebas unitarias y fixtures anonimizados.

## Comandos

```bash
dotnet restore SolarBmsMonitor.sln
dotnet build SolarBmsMonitor.sln --no-restore
dotnet test tests/SolarBmsMonitor.Tests/SolarBmsMonitor.Tests.csproj
dotnet format SolarBmsMonitor.sln --verify-no-changes
```

El proyecto requiere .NET 10 y `maui-android`; no bajar de framework silenciosamente.

## Convenciones

- Nullable activo, modelos inmutables cuando sea práctico y unidades en nombres.
- Toda operación cancelable recibe `CancellationToken`.
- Una sola operación GATT a la vez; siempre timeout y liberación de `BluetoothGatt`.
- La capa de protocolo no depende de Android, MAUI ni SQLite.
- No inventar UUID, comandos, offsets, checksum ni escalas.

## Regla de solo lectura

Solo se permiten consultas enumeradas en `PaceExReadQuery`. No añadir envío raw, ajustes, control de MOSFET, reset, calibración ni firmware. Los datos nunca controlan protecciones o potencia.

## Terminado

Formato, build, tests y revisión del diff limpios; evidencia documentada; comportamiento Android probado en dispositivo físico; cualquier validación de hardware pendiente declarada explícitamente.
