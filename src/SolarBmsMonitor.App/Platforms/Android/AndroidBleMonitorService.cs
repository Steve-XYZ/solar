using Android;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using SolarBmsMonitor.Core.Models;
using SolarBmsMonitor.Core.Services;
using SolarBmsMonitor.Protocol.PaceEx;

namespace SolarBmsMonitor.App.Platforms.Android;

public sealed class AndroidBleMonitorService : IBleMonitorService
{
    private const string PaceServiceUuid = "0000fff0-0000-1000-8000-00805f9b34fb";
    private const string PaceNotificationUuid = "0000fff1-0000-1000-8000-00805f9b34fb";
    private const string PaceWriteUuid = "0000fff2-0000-1000-8000-00805f9b34fb";
    private const string CccdUuid = "00002902-0000-1000-8000-00805f9b34fb";
    private static readonly TimeSpan GattTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FragmentTimeout = TimeSpan.FromSeconds(2);

    private readonly Context _context;
    private readonly BluetoothAdapter? _adapter;
    private readonly ScanHandler _scanHandler;
    private readonly GattHandler _gattHandler;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _gattOperationGate = new(1, 1);
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private readonly Dictionary<string, BluetoothDevice> _discoveredDevices = new(StringComparer.Ordinal);
    private readonly List<DiagnosticEntry> _diagnostics = [];
    private readonly object _diagnosticLock = new();
    private readonly PaceExFrameAssembler _assembler = new();
    private BluetoothLeScanner? _scanner;
    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _writeCharacteristic;
    private BluetoothGattCharacteristic? _notificationCharacteristic;
    private BleDevice? _activeDevice;
    private TaskCompletionSource<bool>? _connectionCompletion;
    private TaskCompletionSource<GattStatus>? _servicesCompletion;
    private TaskCompletionSource<GattStatus>? _writeCompletion;
    private TaskCompletionSource<GattStatus>? _descriptorCompletion;
    private TaskCompletionSource<int>? _mtuCompletion;
    private TaskCompletionSource<PaceExFrame>? _responseCompletion;
    private byte[]? _expectedResponseType;
    private DateTimeOffset _cooldownUntil;
    private int _connectionAttempts;
    private int _timeouts;
    private int _reconnects;
    private int _mtu = 23;
    private bool _scanning;
    private bool _disposed;

    public AndroidBleMonitorService()
    {
        _context = global::Android.App.Application.Context;
        var manager = (BluetoothManager?)_context.GetSystemService(Context.BluetoothService);
        _adapter = manager?.Adapter;
        _scanHandler = new ScanHandler(this);
        _gattHandler = new GattHandler(this);
    }

    public BleAvailability Availability
    {
        get
        {
            if (!_context.PackageManager!.HasSystemFeature(PackageManager.FeatureBluetoothLe) || _adapter is null)
            {
                return BleAvailability.Unsupported;
            }

            if (!_adapter.IsEnabled)
            {
                return BleAvailability.Disabled;
            }

            return HasRuntimePermissions() ? BleAvailability.Ready : BleAvailability.PermissionRequired;
        }
    }

    public BleConnectionState ConnectionState { get; private set; } = BleConnectionState.Disconnected;
    public bool CaptureDiagnosticFrames { get; set; }
    public GattProfile? CurrentProfile { get; private set; }

    public event EventHandler<BleDevice>? DeviceDiscovered;
    public event EventHandler<BleConnectionState>? ConnectionStateChanged;
    public event EventHandler<DiagnosticEntry>? DiagnosticEntryReceived;
    public event EventHandler<BatterySnapshot>? SnapshotReceived;

    public async Task<bool> EnsurePermissionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HasRuntimePermissions())
        {
            return true;
        }

        var status = await Permissions.RequestAsync<Permissions.Bluetooth>().WaitAsync(cancellationToken);
        if (Build.VERSION.SdkInt <= BuildVersionCodes.R)
        {
            var location = await Permissions.RequestAsync<Permissions.LocationWhenInUse>().WaitAsync(cancellationToken);
            return status == PermissionStatus.Granted && location == PermissionStatus.Granted;
        }

        return status == PermissionStatus.Granted;
    }

    public async Task ScanAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "El escaneo debe durar entre 1 y 30 segundos.");
        }

        if (!await EnsurePermissionsAsync(cancellationToken))
        {
            throw new InvalidOperationException("Faltan permisos de Bluetooth.");
        }

        if (Availability == BleAvailability.Unsupported)
        {
            throw new NotSupportedException("Este dispositivo no soporta Bluetooth Low Energy.");
        }

        if (Availability == BleAvailability.Disabled)
        {
            throw new InvalidOperationException("Bluetooth está desactivado.");
        }

        await StopScanAsync();
        _scanner = _adapter!.BluetoothLeScanner
            ?? throw new InvalidOperationException("Android no proporcionó un escáner BLE.");
        _discoveredDevices.Clear();
        using var settingsBuilder = new ScanSettings.Builder();
        _ = settingsBuilder.SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency);
        _ = settingsBuilder.SetCallbackType(ScanCallbackType.AllMatches);
        var settings = settingsBuilder.Build()
            ?? throw new InvalidOperationException("Android no pudo crear la configuración de escaneo BLE.");

        _scanner.StartScan([], settings, _scanHandler);
        _scanning = true;
        AddDiagnostic(DiagnosticDirection.Event, $"Escaneo BLE iniciado por {duration.TotalSeconds:0} s.");

        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            await StopScanAsync();
        }
    }

    public async Task ConnectAsync(BleDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow < _cooldownUntil)
            {
                throw new InvalidOperationException($"El BMS está en enfriamiento hasta {_cooldownUntil:HH:mm:ss}.");
            }

            await StopScanAsync();
            await DisconnectCoreAsync(CancellationToken.None);

            Exception? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _connectionAttempts++;
                if (attempt > 1)
                {
                    _reconnects++;
                    await Task.Delay(TimeSpan.FromSeconds(attempt - 1), cancellationToken);
                }

                try
                {
                    await ConnectOnceAsync(device, cancellationToken);
                    return;
                }
                catch (Exception exception) when (exception is not System.OperationCanceledException)
                {
                    lastError = exception;
                    AddDiagnostic(DiagnosticDirection.Error, $"Intento {attempt}/3 falló: {exception.Message}");
                    await DisconnectCoreAsync(CancellationToken.None);
                }
            }

            _cooldownUntil = DateTimeOffset.UtcNow.AddSeconds(15);
            throw new InvalidOperationException(
                "No se pudo conectar tras tres intentos. Cierre BMS-TOOL, espere 15 segundos y vuelva a intentar.",
                lastError);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            await DisconnectCoreAsync(cancellationToken);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task QueryTelemetryAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_gatt is null || _writeCharacteristic is null || CurrentProfile?.NotificationsEnabled != true || _activeDevice is null)
        {
            throw new InvalidOperationException("No existe un canal PaceEX FFF0/FFF1/FFF2 verificado y conectado.");
        }

        await _queryGate.WaitAsync(cancellationToken);
        try
        {
            var systemFrame = await SendQueryAsync(PaceExReadQuery.SystemTelemetry, cancellationToken);
            if (!PaceExParser.TryParseSystem(systemFrame, out var system, out var systemError))
            {
                throw new InvalidDataException(systemError);
            }

            PaceExCellTelemetry? cells = null;
            try
            {
                var cellFrame = await SendQueryAsync(PaceExReadQuery.CellTelemetry, cancellationToken);
                if (!PaceExParser.TryParseCells(cellFrame, out cells, out var cellError))
                {
                    AddDiagnostic(DiagnosticDirection.Error, cellError ?? "No se pudo parsear la respuesta de celdas.");
                }
            }
            catch (TimeoutException exception)
            {
                AddDiagnostic(DiagnosticDirection.Error, $"Telemetría parcial: {exception.Message}");
            }

            var snapshot = PaceExParser.ToSnapshot(_activeDevice.DeviceId, DateTimeOffset.UtcNow, system!, cells);
            SnapshotReceived?.Invoke(this, snapshot);
        }
        finally
        {
            _queryGate.Release();
        }
    }

    public DiagnosticReport CreateDiagnosticReport()
    {
        lock (_diagnosticLock)
        {
            return new DiagnosticReport(
                AppInfo.Current.VersionString,
                _activeDevice,
                CurrentProfile,
                _diagnostics.ToArray(),
                _connectionAttempts,
                _timeouts,
                _reconnects,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task ConnectOnceAsync(BleDevice device, CancellationToken cancellationToken)
    {
        if (!_discoveredDevices.TryGetValue(device.DeviceId, out var nativeDevice))
        {
            nativeDevice = _adapter!.GetRemoteDevice(device.DeviceId)
                ?? throw new InvalidOperationException("Android no pudo resolver el dispositivo BLE seleccionado.");
        }

        _activeDevice = device with { ConnectionState = BleConnectionState.Connecting };
        SetConnectionState(BleConnectionState.Connecting);
        _connectionCompletion = NewCompletion<bool>();
        _gatt = nativeDevice.ConnectGatt(_context, false, _gattHandler, BluetoothTransports.Le)
            ?? throw new InvalidOperationException("connectGatt devolvió null.");

        try
        {
            await _connectionCompletion.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        }
        catch (TimeoutException)
        {
            _timeouts++;
            throw new TimeoutException("Timeout al conectar con el servidor GATT.");
        }

        await DiscoverServicesAsync(cancellationToken);
        await RequestMtuBestEffortAsync(cancellationToken);
        CurrentProfile = BuildGattProfile(false);
        AddDiagnostic(DiagnosticDirection.Event, $"Descubiertos {CurrentProfile.Services.Count} servicios GATT; MTU {_mtu}.");

        if (_notificationCharacteristic is not null && _writeCharacteristic is not null)
        {
            await EnableNotificationsAsync(cancellationToken);
            CurrentProfile = BuildGattProfile(true);
        }
        else
        {
            AddDiagnostic(DiagnosticDirection.Event, "Perfil PaceEX verificable no encontrado; solo se exportará diagnóstico.");
        }

        SetConnectionState(BleConnectionState.Connected);
        _activeDevice = device with { ConnectionState = BleConnectionState.Connected };
    }

    private async Task DiscoverServicesAsync(CancellationToken cancellationToken)
    {
        await _gattOperationGate.WaitAsync(cancellationToken);
        try
        {
            _servicesCompletion = NewCompletion<GattStatus>();
            if (_gatt?.DiscoverServices() != true)
            {
                throw new InvalidOperationException("Android rechazó el inicio del descubrimiento GATT.");
            }

            var status = await _servicesCompletion.Task.WaitAsync(GattTimeout, cancellationToken);
            if (status != GattStatus.Success)
            {
                throw new InvalidOperationException($"Descubrimiento GATT falló con estado {status}.");
            }
        }
        catch (TimeoutException)
        {
            _timeouts++;
            throw new TimeoutException("Timeout al descubrir servicios GATT.");
        }
        finally
        {
            _gattOperationGate.Release();
        }
    }

    private async Task RequestMtuBestEffortAsync(CancellationToken cancellationToken)
    {
        await _gattOperationGate.WaitAsync(cancellationToken);
        try
        {
            _mtuCompletion = NewCompletion<int>();
            if (_gatt?.RequestMtu(247) != true)
            {
                AddDiagnostic(DiagnosticDirection.Event, "El BMS no aceptó iniciar negociación de MTU; se conserva MTU 23.");
                return;
            }

            try
            {
                _mtu = await _mtuCompletion.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (TimeoutException)
            {
                _timeouts++;
                AddDiagnostic(DiagnosticDirection.Event, "Timeout de MTU; se conserva el último valor conocido.");
            }
        }
        finally
        {
            _gattOperationGate.Release();
        }
    }

    private GattProfile BuildGattProfile(bool notificationsEnabled)
    {
        _writeCharacteristic = null;
        _notificationCharacteristic = null;
        var services = new List<GattServiceInfo>();

        foreach (var service in _gatt?.Services ?? [])
        {
            var serviceUuid = Normalize(service.Uuid?.ToString());
            var characteristics = new List<GattCharacteristicInfo>();
            foreach (var characteristic in service.Characteristics ?? [])
            {
                var uuid = Normalize(characteristic.Uuid?.ToString());
                var capabilities = MapCapabilities(characteristic.Properties);
                var descriptors = (characteristic.Descriptors ?? [])
                    .Select(descriptor => new GattDescriptorInfo(Normalize(descriptor.Uuid?.ToString())))
                    .ToArray();
                var isPaceService = serviceUuid == PaceServiceUuid;
                var isWrite = isPaceService
                    && uuid == PaceWriteUuid
                    && (capabilities & (GattCharacteristicCapabilities.Write | GattCharacteristicCapabilities.WriteWithoutResponse)) != 0;
                var isNotification = isPaceService
                    && uuid == PaceNotificationUuid
                    && (capabilities & (GattCharacteristicCapabilities.Notify | GattCharacteristicCapabilities.Indicate)) != 0
                    && descriptors.Any(descriptor => descriptor.Uuid == CccdUuid);

                if (isWrite)
                {
                    _writeCharacteristic = characteristic;
                }

                if (isNotification)
                {
                    _notificationCharacteristic = characteristic;
                }

                characteristics.Add(new GattCharacteristicInfo(uuid, capabilities, descriptors, isWrite, isNotification));
            }

            services.Add(new GattServiceInfo(serviceUuid, characteristics));
        }

        return new GattProfile(
            DateTimeOffset.UtcNow,
            _mtu,
            services,
            _writeCharacteristic is null ? null : PaceWriteUuid,
            _notificationCharacteristic is null ? null : PaceNotificationUuid,
            _notificationCharacteristic is null ? null : CccdUuid,
            notificationsEnabled);
    }

    private async Task EnableNotificationsAsync(CancellationToken cancellationToken)
    {
        await _gattOperationGate.WaitAsync(cancellationToken);
        try
        {
            var gatt = _gatt ?? throw new InvalidOperationException("La conexión GATT ya no está disponible.");
            var characteristic = _notificationCharacteristic!;
            var descriptor = characteristic.Descriptors?.FirstOrDefault(item => Normalize(item.Uuid?.ToString()) == CccdUuid)
                ?? throw new InvalidOperationException("La característica FFF1 no expone CCCD 2902.");
            if (gatt.SetCharacteristicNotification(characteristic, true) != true)
            {
                throw new InvalidOperationException("Android no pudo activar notificaciones locales para FFF1.");
            }

            _descriptorCompletion = NewCompletion<GattStatus>();
            var descriptorValue = (characteristic.Properties & GattProperty.Indicate) != 0
                ? BluetoothGattDescriptor.EnableIndicationValue
                : BluetoothGattDescriptor.EnableNotificationValue;
            var value = descriptorValue?.ToArray()
                ?? throw new InvalidOperationException("Android no proporcionó el valor estándar para CCCD.");
            bool initiated;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                initiated = gatt.WriteDescriptor(descriptor, value) == 0;
            }
            else
            {
#pragma warning disable CS0618
                initiated = descriptor.SetValue(value) && gatt.WriteDescriptor(descriptor);
#pragma warning restore CS0618
            }

            if (!initiated)
            {
                throw new InvalidOperationException("Android rechazó la escritura del CCCD.");
            }

            var status = await _descriptorCompletion.Task.WaitAsync(GattTimeout, cancellationToken);
            if (status != GattStatus.Success)
            {
                throw new InvalidOperationException($"La escritura del CCCD falló con estado {status}.");
            }

            AddDiagnostic(DiagnosticDirection.Event, "Notificaciones FFF1 activadas mediante CCCD 2902.");
        }
        catch (TimeoutException)
        {
            _timeouts++;
            throw new TimeoutException("Timeout al activar notificaciones GATT.");
        }
        finally
        {
            _gattOperationGate.Release();
        }
    }

    private async Task<PaceExFrame> SendQueryAsync(PaceExReadQuery query, CancellationToken cancellationToken)
    {
        var gatt = _gatt ?? throw new InvalidOperationException("La conexión GATT ya no está disponible.");
        var bytes = PaceExCommands.Build(query);
        _expectedResponseType = bytes.AsSpan(1, 6).ToArray();
        _responseCompletion = NewCompletion<PaceExFrame>();

        await _gattOperationGate.WaitAsync(cancellationToken);
        try
        {
            _writeCompletion = NewCompletion<GattStatus>();
            AddDiagnostic(DiagnosticDirection.Tx, $"Consulta de solo lectura: {query}.", Convert.ToHexString(bytes));
            var writeType = (_writeCharacteristic!.Properties & GattProperty.Write) != 0
                ? GattWriteType.Default
                : GattWriteType.NoResponse;
            bool initiated;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                initiated = gatt.WriteCharacteristic(_writeCharacteristic, bytes, (int)writeType) == 0;
            }
            else
            {
#pragma warning disable CS0618
                _writeCharacteristic.WriteType = writeType;
                initiated = _writeCharacteristic.SetValue(bytes) && gatt.WriteCharacteristic(_writeCharacteristic);
#pragma warning restore CS0618
            }

            if (!initiated)
            {
                throw new InvalidOperationException("Android rechazó la escritura de la consulta PaceEX.");
            }

            var status = await _writeCompletion.Task.WaitAsync(GattTimeout, cancellationToken);
            if (status != GattStatus.Success)
            {
                throw new InvalidOperationException($"La consulta GATT falló con estado {status}.");
            }
        }
        catch (TimeoutException)
        {
            _timeouts++;
            throw new TimeoutException("Timeout al escribir la consulta GATT.");
        }
        finally
        {
            _gattOperationGate.Release();
        }

        try
        {
            return await _responseCompletion.Task.WaitAsync(ResponseTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _timeouts++;
            throw new TimeoutException($"Timeout esperando respuesta PaceEX a {query}.");
        }
        finally
        {
            _responseCompletion = null;
            _expectedResponseType = null;
        }
    }

    private async Task StopScanAsync()
    {
        if (!_scanning)
        {
            return;
        }

        try
        {
            _scanner?.StopScan(_scanHandler);
        }
        finally
        {
            _scanning = false;
            AddDiagnostic(DiagnosticDirection.Event, "Escaneo BLE detenido.");
        }

        await Task.CompletedTask;
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        _assembler.Reset();
        _responseCompletion?.TrySetCanceled(cancellationToken);
        _writeCharacteristic = null;
        _notificationCharacteristic = null;
        CurrentProfile = null;
        if (_gatt is null)
        {
            if (_activeDevice is not null)
            {
                _activeDevice = _activeDevice with { ConnectionState = BleConnectionState.Disconnected };
            }
            SetConnectionState(BleConnectionState.Disconnected);
            return;
        }

        SetConnectionState(BleConnectionState.Disconnecting);
        try
        {
            _gatt.Disconnect();
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }
        finally
        {
            _gatt.Close();
            _gatt.Dispose();
            _gatt = null;
            if (_activeDevice is not null)
            {
                _activeDevice = _activeDevice with { ConnectionState = BleConnectionState.Disconnected };
            }
            SetConnectionState(BleConnectionState.Disconnected);
            AddDiagnostic(DiagnosticDirection.Event, "BluetoothGatt desconectado y liberado.");
        }
    }

    private void HandleScanResult(ScanResult result)
    {
        var name = result.ScanRecord?.DeviceName ?? result.Device?.Name;
        var address = result.Device?.Address;
        if (string.IsNullOrWhiteSpace(name)
            || !name.StartsWith("PC-", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(address)
            || result.Device is null)
        {
            return;
        }

        _discoveredDevices[address] = result.Device;
        var device = new BleDevice(name, address, result.Rssi, DateTimeOffset.UtcNow);
        DeviceDiscovered?.Invoke(this, device);
    }

    private void HandleNotification(byte[] value)
    {
        AddDiagnostic(DiagnosticDirection.RxFragment, "Fragmento de notificación recibido.", Convert.ToHexString(value));
        var frames = _assembler.Append(value, DateTimeOffset.UtcNow, FragmentTimeout, message =>
            AddDiagnostic(DiagnosticDirection.Error, message));
        foreach (var frame in frames)
        {
            AddDiagnostic(DiagnosticDirection.RxFrame, "Frame PaceEX válido ensamblado.", Convert.ToHexString(frame.Bytes.Span));
            if (_expectedResponseType is not null && frame.Type.Span.SequenceEqual(_expectedResponseType))
            {
                _responseCompletion?.TrySetResult(frame);
            }
        }
    }

    private void AddDiagnostic(DiagnosticDirection direction, string message, string? hex = null)
    {
        if (!CaptureDiagnosticFrames
            && direction is DiagnosticDirection.Tx or DiagnosticDirection.RxFragment or DiagnosticDirection.RxFrame)
        {
            return;
        }

        var entry = new DiagnosticEntry(DateTimeOffset.UtcNow, direction, message, hex);
        lock (_diagnosticLock)
        {
            _diagnostics.Add(entry);
            if (_diagnostics.Count > 2_000)
            {
                _diagnostics.RemoveRange(0, 250);
            }
        }

        DiagnosticEntryReceived?.Invoke(this, entry);
    }

    private void SetConnectionState(BleConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, state);
    }

    private async Task CleanUpUnexpectedDisconnectAsync(GattStatus status)
    {
        try
        {
            AddDiagnostic(DiagnosticDirection.Error, $"El BMS cerró la conexión GATT: {status}.");
            await _sessionGate.WaitAsync();
            try
            {
                await DisconnectCoreAsync(CancellationToken.None);
            }
            finally
            {
                _sessionGate.Release();
            }
        }
        catch (Exception exception)
        {
            AddDiagnostic(DiagnosticDirection.Error, $"No se completó la limpieza de desconexión: {exception.Message}");
        }
    }

    private bool HasRuntimePermissions()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return _context.CheckSelfPermission(Manifest.Permission.BluetoothScan) == Permission.Granted
                && _context.CheckSelfPermission(Manifest.Permission.BluetoothConnect) == Permission.Granted;
        }

        return _context.CheckSelfPermission(Manifest.Permission.AccessFineLocation) == Permission.Granted;
    }

    private static GattCharacteristicCapabilities MapCapabilities(GattProperty properties)
    {
        var result = GattCharacteristicCapabilities.None;
        if ((properties & GattProperty.Read) != 0) result |= GattCharacteristicCapabilities.Read;
        if ((properties & GattProperty.Write) != 0) result |= GattCharacteristicCapabilities.Write;
        if ((properties & GattProperty.WriteNoResponse) != 0) result |= GattCharacteristicCapabilities.WriteWithoutResponse;
        if ((properties & GattProperty.Notify) != 0) result |= GattCharacteristicCapabilities.Notify;
        if ((properties & GattProperty.Indicate) != 0) result |= GattCharacteristicCapabilities.Indicate;
        return result;
    }

    private static string Normalize(string? uuid) => uuid?.ToLowerInvariant() ?? "desconocido";

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync(CancellationToken.None);
        _disposed = true;
        _scanHandler.Dispose();
        _gattHandler.Dispose();
        _sessionGate.Dispose();
        _gattOperationGate.Dispose();
        _queryGate.Dispose();
    }

    private sealed class ScanHandler(AndroidBleMonitorService owner) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result is not null)
            {
                owner.HandleScanResult(result);
            }
        }

        public override void OnScanFailed(ScanFailure errorCode) =>
            owner.AddDiagnostic(DiagnosticDirection.Error, $"Escaneo BLE falló: {errorCode}.");
    }

    private sealed class GattHandler(AndroidBleMonitorService owner) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
        {
            if (status == GattStatus.Success && newState == ProfileState.Connected)
            {
                owner._connectionCompletion?.TrySetResult(true);
                return;
            }

            if (newState == ProfileState.Disconnected)
            {
                var wasConnected = owner.ConnectionState == BleConnectionState.Connected;
                owner._connectionCompletion?.TrySetException(
                    new InvalidOperationException($"GATT desconectado con estado {status}."));
                owner.SetConnectionState(BleConnectionState.Disconnected);
                if (wasConnected)
                {
                    _ = owner.CleanUpUnexpectedDisconnectAsync(status);
                }
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status) =>
            owner._servicesCompletion?.TrySetResult(status);

        public override void OnCharacteristicWrite(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic,
            GattStatus status) => owner._writeCompletion?.TrySetResult(status);

        public override void OnDescriptorWrite(
            BluetoothGatt? gatt,
            BluetoothGattDescriptor? descriptor,
            GattStatus status) => owner._descriptorCompletion?.TrySetResult(status);

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
        {
            if (status == GattStatus.Success)
            {
                owner._mtuCompletion?.TrySetResult(mtu);
            }
            else
            {
                owner._mtuCompletion?.TrySetResult(23);
            }
        }

        public override void OnCharacteristicChanged(
            BluetoothGatt gatt,
            BluetoothGattCharacteristic characteristic,
            byte[] value) => owner.HandleNotification(value);

#pragma warning disable CS0618
        public override void OnCharacteristicChanged(
            BluetoothGatt? gatt,
            BluetoothGattCharacteristic? characteristic)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)
                && characteristic?.GetValue() is { } value)
            {
                owner.HandleNotification(value);
            }
        }
#pragma warning restore CS0618
    }
}
