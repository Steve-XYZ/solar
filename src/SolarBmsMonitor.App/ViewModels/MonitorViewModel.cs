using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using SolarBmsMonitor.Core.Calculations;
using SolarBmsMonitor.Core.Models;
using SolarBmsMonitor.Core.Services;
using BatteryDeviceInfo = SolarBmsMonitor.Core.Models.DeviceInfo;

namespace SolarBmsMonitor.App.ViewModels;

public sealed class MonitorViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan TelemetryRefreshInterval = TimeSpan.FromSeconds(5);
    private readonly IBleMonitorService _bleService;
    private readonly IBatteryRepository _repository;
    private readonly IExportService _exportService;
    private readonly List<double> _recentPower = [];
    private readonly Timer _staleTimer;
    private readonly object _operationLock = new();
    private readonly object _telemetryPollingLock = new();
    private CancellationTokenSource? _activeOperation;
    private CancellationTokenSource? _telemetryPollingCancellation;
    private BatterySnapshot? _snapshot;
    private BatterySnapshot? _previousSnapshot;
    private BleDevice? _selectedDevice;
    private BleDevice? _featuredDevice;
    private BleScanOutcome? _lastScanOutcome;
    private GattProfile? _gattProfile;
    private string _statusMessage = "Listo para escanear.";
    private string _lastExportPath = string.Empty;
    private bool _diagnosticsEnabled;
    private bool _isScanning;
    private bool _isConnecting;
    private bool _isConnected;

    public MonitorViewModel(
        IBleMonitorService bleService,
        IBatteryRepository repository,
        IExportService exportService)
    {
        _bleService = bleService;
        _repository = repository;
        _exportService = exportService;
        _bleService.CaptureDiagnosticFrames = _diagnosticsEnabled;
        Devices = [];
        Diagnostics = [];
        History = [];

        ScanCommand = new AsyncCommand(_ => ScanAsync());
        ConnectCommand = new AsyncCommand(ConnectAsync, parameter => parameter is BleDevice);
        DisconnectCommand = new AsyncCommand(_ => DisconnectAsync());
        RefreshTelemetryCommand = new AsyncCommand(_ => RefreshTelemetryAsync());
        ExportDiagnosticsCommand = new AsyncCommand(_ => ExportDiagnosticsAsync());
        CopyDiagnosticsCommand = new AsyncCommand(_ => CopyDiagnosticsAsync());
        ExportHistoryCommand = new AsyncCommand(_ => ExportHistoryAsync());

        _bleService.DeviceDiscovered += OnDeviceDiscovered;
        _bleService.ConnectionStateChanged += OnConnectionStateChanged;
        _bleService.DiagnosticEntryReceived += OnDiagnosticEntryReceived;
        _bleService.SnapshotReceived += OnSnapshotReceived;
        _staleTimer = new Timer(
            _ => MainThread.BeginInvokeOnMainThread(NotifyFreshnessProperties),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    public ObservableCollection<BleDevice> Devices { get; }
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; }
    public ObservableCollection<BatterySnapshot> History { get; }
    public event EventHandler? ConnectionSucceeded;
    public event EventHandler? ConnectionEnded;
    public ICommand ScanCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshTelemetryCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand ExportHistoryCommand { get; }

    public BatterySnapshot? Snapshot
    {
        get => _snapshot;
        private set
        {
            if (SetProperty(ref _snapshot, value))
            {
                NotifySnapshotProperties();
            }
        }
    }

    public BleDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(ConnectedDeviceText));
            }
        }
    }

    public BleDevice? FeaturedDevice
    {
        get => _featuredDevice;
        private set
        {
            if (SetProperty(ref _featuredDevice, value))
            {
                OnPropertyChanged(nameof(HasFeaturedDevice));
                OnPropertyChanged(nameof(ConnectionHeadline));
                OnPropertyChanged(nameof(ConnectionSubheadline));
                OnPropertyChanged(nameof(ConnectionActionText));
                OnPropertyChanged(nameof(SignalStrengthText));
                OnPropertyChanged(nameof(SignalProgress));
            }
        }
    }

    public GattProfile? GattProfile
    {
        get => _gattProfile;
        private set => SetProperty(ref _gattProfile, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastExportPath
    {
        get => _lastExportPath;
        private set => SetProperty(ref _lastExportPath, value);
    }

    public bool DiagnosticsEnabled
    {
        get => _diagnosticsEnabled;
        set
        {
            if (SetProperty(ref _diagnosticsEnabled, value))
            {
                _bleService.CaptureDiagnosticFrames = value;
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(ConnectionHeadline));
                OnPropertyChanged(nameof(ConnectionSubheadline));
            }
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        private set
        {
            if (SetProperty(ref _isConnecting, value))
            {
                OnPropertyChanged(nameof(ConnectionHeadline));
                OnPropertyChanged(nameof(ConnectionSubheadline));
                OnPropertyChanged(nameof(ConnectionActionText));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public bool HasSnapshot => Snapshot is not null;

    public bool HasFeaturedDevice => FeaturedDevice is not null;

    public string ConnectionHeadline => IsConnecting
        ? "Conectando con tu batería"
        : HasFeaturedDevice
            ? "Batería encontrada"
            : IsScanning
                ? "Buscando tu batería"
                : LastScanOutcome switch
                {
                    BleScanOutcome.PermissionDenied => "Permiso necesario",
                    BleScanOutcome.BluetoothDisabled => "Activa Bluetooth",
                    BleScanOutcome.Unsupported => "Bluetooth no disponible",
                    BleScanOutcome.RetryLimitReached => "No encontramos tu batería",
                    BleScanOutcome.TransientFailure => "No pudimos buscar",
                    _ => "Buscando tu batería",
                };

    public string ConnectionSubheadline => IsConnecting
        ? "Verificando el canal seguro de solo lectura"
        : HasFeaturedDevice
            ? "La encontramos cerca de ti"
            : IsScanning
                ? "El escaneo comenzó automáticamente"
                : LastScanOutcome switch
                {
                    BleScanOutcome.PermissionDenied => "Concede el permiso de dispositivos cercanos",
                    BleScanOutcome.BluetoothDisabled => "Enciéndelo y vuelve a esta pantalla",
                    BleScanOutcome.Unsupported => "Este dispositivo no ofrece Bluetooth Low Energy",
                    BleScanOutcome.RetryLimitReached => "Acércate al BMS y vuelve a esta pantalla para reintentar",
                    BleScanOutcome.TransientFailure => "Reintentaremos con una espera gradual",
                    BleScanOutcome.NoDeviceFound => "No detectamos ningún dispositivo PC-* cercano",
                    _ => "Preparando un nuevo escaneo",
                };

    private BleScanOutcome? LastScanOutcome
    {
        get => _lastScanOutcome;
        set
        {
            if (_lastScanOutcome == value)
            {
                return;
            }

            _lastScanOutcome = value;
            OnPropertyChanged(nameof(ConnectionHeadline));
            OnPropertyChanged(nameof(ConnectionSubheadline));
        }
    }

    public string ConnectionActionText => IsConnecting ? "Conectando…" : "Toca para conectar";

    public string SignalStrengthText => FeaturedDevice?.Rssi switch
    {
        >= -60 => "Señal fuerte",
        >= -75 => "Buena señal",
        null => "Buscando señal",
        _ => "Señal débil",
    };

    public double SignalProgress => FeaturedDevice is null
        ? 0
        : Math.Clamp((FeaturedDevice.Rssi + 100d) / 50d, 0.12d, 1d);

    public string ConnectedDeviceText => SelectedDevice?.Name ?? "BMS conectado";

    public string StateOfChargeText => Snapshot?.StateOfChargePercent is { } stateOfCharge
        ? $"{stateOfCharge:F0}%"
        : "--%";

    public double StateOfChargeProgress => Math.Clamp((Snapshot?.StateOfChargePercent ?? 0) / 100d, 0, 1);

    public string VoltageText => Snapshot?.PackVoltageVolts is { } voltage ? $"{voltage:F2} V" : "-- V";

    public string CurrentText => Snapshot?.CurrentAmps is { } current ? $"{current:F2} A" : "-- A";

    public string RemainingEnergyText => EnergyEstimate is { } estimate
        ? $"{estimate.RemainingKilowattHours:F2} kWh"
        : "-- kWh";

    public string CycleCountText => Snapshot?.CycleCount is { } cycleCount
        ? $"{cycleCount:N0}"
        : "--";

    /// <summary>
    /// Health is reported as state of health only while the BMS returns a
    /// plausible value; otherwise the same card shows the capacity pair, which
    /// this battery always reports.
    /// </summary>
    private BatteryHealthSummary HealthSummary => BatteryHealth.Summarize(
        Snapshot?.StateOfHealthPercent,
        Snapshot?.RemainingCapacityAh,
        Snapshot?.FullCapacityAh ?? Snapshot?.DesignedCapacityAh,
        Snapshot?.CycleCount);

    public string HealthTitle => HealthSummary.Mode == BatteryHealthMode.StateOfHealth
        ? "Salud"
        : "Capacidad";

    public string HealthValueText => HealthSummary switch
    {
        { Mode: BatteryHealthMode.StateOfHealth, StateOfHealthPercent: { } soh } => $"{soh:F0} %",
        { RemainingCapacityAh: { } remaining } => $"{remaining:F0} Ah",
        _ => "sin datos",
    };

    public string HealthDetailText
    {
        get
        {
            var summary = HealthSummary;
            var parts = new List<string>(2);
            if (summary.Mode == BatteryHealthMode.StateOfHealth)
            {
                if (summary is { RemainingCapacityAh: { } remaining, ReferenceCapacityAh: { } reference })
                {
                    parts.Add($"{remaining:F0} / {reference:F0} Ah");
                }
            }
            else if (summary.ReferenceCapacityAh is { } capacity)
            {
                parts.Add($"de {capacity:F0} Ah");
            }

            if (summary.CycleCount is { } cycles)
            {
                parts.Add(cycles == 1 ? "1 ciclo" : $"{cycles:N0} ciclos");
            }

            return string.Join(" · ", parts);
        }
    }

    private CellBalanceLevel CellBalanceLevelValue => CellBalance.Evaluate(Snapshot?.CellDeltaMillivolts);

    public string CellBalanceStatusText => CellBalanceLevelValue switch
    {
        CellBalanceLevel.Balanced => "Equilibrado",
        CellBalanceLevel.Acceptable => "Aceptable",
        CellBalanceLevel.Review => "Conviene revisar",
        _ => "Sin lectura de celdas",
    };

    public bool CellBalanceIsUnknown => CellBalanceLevelValue == CellBalanceLevel.Unknown;

    public bool CellBalanceIsAcceptable => CellBalanceLevelValue == CellBalanceLevel.Acceptable;

    public bool CellBalanceNeedsReview => CellBalanceLevelValue == CellBalanceLevel.Review;

    /// <summary>
    /// True only when the estimate card is showing an actual duration; the
    /// precision dot must not appear next to "En reposo" or "Calculando…".
    /// </summary>
    private bool PrimaryEstimateShowsDuration => Snapshot?.ChargeState switch
    {
        ChargeState.Charging => Snapshot.StateOfChargePercent is not >= 99.5 &&
            EnergyEstimate?.ChargeTimeHours is not null,
        ChargeState.Discharging => Snapshot.StateOfChargePercent is not <= 0.5 &&
            EnergyEstimate?.RuntimeHours is not null,
        _ => false,
    };

    public bool HasEstimatePrecision => PrimaryEstimateShowsDuration &&
        EnergyEstimate?.Precision is EstimatePrecision.Stable or EstimatePrecision.Approximate;

    public bool EstimateIsApproximate => HasEstimatePrecision &&
        EnergyEstimate?.Precision == EstimatePrecision.Approximate;

    public bool HasActiveAlarms => Snapshot?.Alarms.Any(alarm => alarm.IsActive) == true;

    public string ActiveAlarmsText => Snapshot is null
        ? string.Empty
        : string.Join(" · ", Snapshot.Alarms.Where(alarm => alarm.IsActive).Select(alarm => alarm.Description));

    public string PowerFlowTitle => Snapshot?.ChargeState switch
    {
        ChargeState.Charging => "Entrando ahora",
        ChargeState.Discharging => "Consumo ahora",
        _ => "Potencia neta",
    };

    public string PowerFlowText
    {
        get
        {
            if (Snapshot?.PowerWatts is not { } powerWatts)
            {
                return "-- W";
            }

            var absoluteWatts = Math.Abs(powerWatts);
            return absoluteWatts >= 1_000 ? $"{absoluteWatts / 1_000:F2} kW" : $"{absoluteWatts:F0} W";
        }
    }

    public string PrimaryEstimateTitle => Snapshot?.ChargeState switch
    {
        ChargeState.Charging => "Carga completa",
        ChargeState.Discharging => "Autonomía",
        _ => "Estimación",
    };

    public string PrimaryEstimateText => Snapshot?.ChargeState switch
    {
        ChargeState.Charging => ChargeTimeText,
        ChargeState.Discharging => RuntimeText,
        ChargeState.Idle => "En reposo",
        _ => "Sin datos",
    };

    public string LastUpdatedText => Snapshot is null
        ? "Esperando datos"
        : $"Actualizado {Snapshot.Timestamp.ToLocalTime():HH:mm:ss}";

    /// <summary>
    /// Freshness is already carried by the coloured dot, so the wording only
    /// appears when something is wrong and the reader has to act on it.
    /// </summary>
    public string StaleStatusText => Snapshot switch
    {
        null => "Sin telemetría",
        { IsStale: true } => "Datos obsoletos",
        { } value when DateTimeOffset.UtcNow - value.Timestamp > TimeSpan.FromSeconds(15) => "Datos obsoletos",
        _ => string.Empty,
    };

    public bool HasFreshTelemetry => Snapshot is { IsStale: false } value &&
        DateTimeOffset.UtcNow - value.Timestamp <= TimeSpan.FromSeconds(15);

    public bool HasStaleTelemetry => Snapshot is not null && !HasFreshTelemetry;

    public string ChargeStateText => Snapshot?.ChargeState switch
    {
        ChargeState.Charging => "Cargando",
        ChargeState.Discharging => "Descargando",
        ChargeState.Idle => "Reposo",
        _ => "Sin datos",
    };

    public string RuntimeText => EnergyEstimate switch
    {
        _ when Snapshot?.StateOfChargePercent is <= 0.5 => "Batería agotada",
        { RuntimeHours: not null } estimate => FormatDuration(estimate.RuntimeHours.Value),
        { Confidence: "inestable" } when Snapshot?.ChargeState == ChargeState.Discharging => "Consumo inestable",
        _ when Snapshot?.ChargeState == ChargeState.Discharging => $"Calculando… ({Math.Min(_recentPower.Count, 5)}/5)",
        _ when Snapshot?.ChargeState == ChargeState.Charging => "No aplica mientras carga",
        _ when Snapshot?.ChargeState == ChargeState.Idle => "En reposo",
        _ => "Sin datos",
    };

    public string ChargeTimeText => EnergyEstimate switch
    {
        _ when Snapshot?.StateOfChargePercent is >= 99.5 => "Carga completa",
        { ChargeTimeHours: not null } estimate => FormatDuration(estimate.ChargeTimeHours.Value),
        { Confidence: "inestable" } when Snapshot?.ChargeState == ChargeState.Charging => "Carga inestable",
        _ when Snapshot?.ChargeState == ChargeState.Charging => $"Calculando… ({Math.Min(_recentPower.Count, 5)}/5)",
        _ when Snapshot?.ChargeState == ChargeState.Discharging => "No está cargando",
        _ when Snapshot?.ChargeState == ChargeState.Idle => "En reposo",
        _ => "Sin datos",
    };

    public string ChargingPowerText
    {
        get
        {
            var watts = Math.Max(0, Snapshot?.PowerWatts ?? 0);
            return watts > 1_000 ? $"{watts / 1_000:F2} kW" : $"{watts:F0} W";
        }
    }

    public string CellDeltaText => Snapshot?.CellDeltaMillivolts is { } delta
        ? $"{delta:F0} mV"
        : "sin datos";

    /// <summary>
    /// The pack exposes several probes and the average hides the one that
    /// matters: a single hot sensor is what a reader needs to see.
    /// </summary>
    public string MaximumTemperatureText => Snapshot?.TemperaturesCelsius.Count > 0
        ? $"{Snapshot.TemperaturesCelsius.Max():F1} °C"
        : "sin datos";

    public string StateOfHealthText => Snapshot?.StateOfHealthPercent is { } stateOfHealth
        ? $"{stateOfHealth:F0} %"
        : "sin dato del BMS";

    public string CapacityText => Snapshot switch
    {
        { RemainingCapacityAh: { } remaining, DesignedCapacityAh: { } designed } =>
            $"{remaining:F1} de {designed:F0} Ah",
        { RemainingCapacityAh: { } remaining } => $"{remaining:F1} Ah",
        _ => "sin datos",
    };

    public string CellCountText => Snapshot?.CellVoltages.Count > 0
        ? $"{Snapshot.CellVoltages.Count} celdas"
        : "sin datos";

    public string DataQualityText => Snapshot?.DataQuality switch
    {
        DataQuality.Valid => "Sistema y celdas",
        DataQuality.Partial => "Solo sistema, sin trama de celdas",
        DataQuality.Invalid => "Lectura descartada",
        _ => "Sin telemetría",
    };

    public string MinimumCellText => Snapshot?.CellVoltages.Count > 0
        ? $"{Snapshot.CellVoltages.Min():F3} V"
        : "sin datos";

    public string MaximumCellText => Snapshot?.CellVoltages.Count > 0
        ? $"{Snapshot.CellVoltages.Max():F3} V"
        : "sin datos";

    /// <summary>
    /// Projects the raw cell voltages into numbered readings and flags the
    /// extremes, so the cells page can identify a card without the reader
    /// having to count tiles or compare three-decimal figures by eye.
    /// </summary>
    public IReadOnlyList<CellReading> CellReadings
    {
        get
        {
            var voltages = Snapshot?.CellVoltages;
            if (voltages is not { Count: > 0 })
            {
                return [];
            }

            var minimum = voltages.Min();
            var maximum = voltages.Max();

            // A pack whose cells are perfectly matched has no meaningful
            // extreme; badging every card would be noise.
            var hasSpread = maximum - minimum > 0.0005;

            return [.. voltages.Select((voltage, index) => new CellReading(
                index + 1,
                $"{voltage:F3} V",
                hasSpread && voltage <= minimum,
                hasSpread && voltage >= maximum))];
        }
    }

    public EnergyEstimate? EnergyEstimate => Snapshot is null || Snapshot.PackVoltageVolts is null
        ? null
        : BatteryCalculations.EstimateEnergy(
            Snapshot.RemainingCapacityAh,
            Snapshot.FullCapacityAh ?? Snapshot.DesignedCapacityAh,
            Snapshot.StateOfChargePercent,
            Snapshot.PackVoltageVolts.Value,
            _recentPower);

    public async Task<BleScanOutcome> ScanAsync(CancellationToken cancellationToken = default)
    {
        var operation = BeginOperation(cancellationToken);
        IsScanning = true;
        try
        {
            LastScanOutcome = null;
            Devices.Clear();
            FeaturedDevice = null;
            StatusMessage = "Buscando baterías PC-* cercanas…";
            if (_bleService.Availability == BleAvailability.Unsupported)
            {
                StatusMessage = "Este dispositivo no soporta Bluetooth Low Energy.";
                return SetScanOutcome(BleScanOutcome.Unsupported);
            }

            if (_bleService.Availability == BleAvailability.Disabled)
            {
                StatusMessage = "Bluetooth está desactivado; actívelo para buscar la batería.";
                return SetScanOutcome(BleScanOutcome.BluetoothDisabled);
            }

            if (!await _bleService.EnsurePermissionsAsync(operation.Token))
            {
                StatusMessage = "Permisos de dispositivos cercanos denegados.";
                return SetScanOutcome(BleScanOutcome.PermissionDenied);
            }

            await _bleService.ScanAsync(TimeSpan.FromSeconds(12), operation.Token);
            var outcome = Devices.Count == 0 ? BleScanOutcome.NoDeviceFound : BleScanOutcome.DeviceFound;
            StatusMessage = outcome == BleScanOutcome.DeviceFound
                ? "Batería encontrada."
                : "No encontramos una batería cercana.";
            return SetScanOutcome(outcome);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // Connecting or closing the app normally cancels an in-progress scan.
            return SetScanOutcome(BleScanOutcome.Canceled);
        }
        catch (NotSupportedException exception)
        {
            StatusMessage = $"Bluetooth LE no está disponible: {exception.Message}";
            return SetScanOutcome(BleScanOutcome.Unsupported);
        }
        catch (InvalidOperationException exception) when (_bleService.Availability == BleAvailability.Disabled)
        {
            StatusMessage = $"Bluetooth está desactivado: {exception.Message}";
            return SetScanOutcome(BleScanOutcome.BluetoothDisabled);
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo escanear: {exception.Message}";
            return SetScanOutcome(BleScanOutcome.TransientFailure);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void MarkAutomaticScanLimitReached()
    {
        LastScanOutcome = BleScanOutcome.RetryLimitReached;
        StatusMessage = "No encontramos una batería tras tres intentos automáticos.";
    }

    private BleScanOutcome SetScanOutcome(BleScanOutcome outcome)
    {
        LastScanOutcome = outcome;
        return outcome;
    }

    private async Task ConnectAsync(object? parameter)
    {
        if (parameter is not BleDevice device)
        {
            return;
        }

        var operation = BeginOperation();
        IsConnecting = true;
        try
        {
            SelectedDevice = device;
            ResetLiveSessionMetrics();
            StatusMessage = $"Conectando con {device.Name}…";
            await _bleService.ConnectAsync(device, operation.Token);
            GattProfile = _bleService.CurrentProfile;
            StatusMessage = GattProfile?.NotificationsEnabled == true
                ? "Conectado; canal PaceEX verificado y notificaciones activas."
                : "Conectado; perfil GATT capturado, canal PaceEX aún no verificado.";
            if (GattProfile?.NotificationsEnabled == true)
            {
                StartTelemetryPolling();
            }

            IsConnected = true;
            ConnectionSucceeded?.Invoke(this, EventArgs.Empty);

            var connectedAt = DateTimeOffset.UtcNow;
            try
            {
                await _repository.InitializeAsync(operation.Token);
                await _repository.SaveDeviceAsync(
                    new BatteryDeviceInfo(device.Name, device.DeviceId, device.Rssi, null, null, null, 300, connectedAt, connectedAt),
                    operation.Token);
                await _repository.BeginSessionAsync(device.DeviceId, connectedAt, operation.Token);
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            {
                // Disconnecting or closing the app cancels session persistence normally.
            }
            catch (Exception exception)
            {
                StatusMessage = $"Conectado, pero no se pudo iniciar el histórico: {exception.Message}";
            }
        }
        catch (Exception exception)
        {
            IsConnected = false;
            StatusMessage = $"Error de conexión: {exception.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task DisconnectAsync()
    {
        StopTelemetryPolling();
        CancelActiveOperation();
        try
        {
            await _bleService.DisconnectAsync(CancellationToken.None);
            StatusMessage = "Desconectado.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo desconectar limpiamente: {exception.Message}";
        }
    }

    private async Task RefreshTelemetryAsync()
    {
        var operation = BeginOperation();
        try
        {
            StatusMessage = "Consultando telemetría de solo lectura…";
            await _bleService.QueryTelemetryAsync(operation.Token);
            StatusMessage = "Telemetría actualizada.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se obtuvo telemetría: {exception.Message}";
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        var operation = BeginOperation();
        try
        {
            LastExportPath = await _exportService.ExportDiagnosticsJsonAsync(
                _bleService.CreateDiagnosticReport(), operation.Token);
            StatusMessage = $"Diagnóstico exportado en {LastExportPath}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo exportar el diagnóstico: {exception.Message}";
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _bleService.CreateDiagnosticReport(),
                IndentedJsonOptions);
            await Clipboard.Default.SetTextAsync(json);
            StatusMessage = "Informe diagnóstico copiado.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo copiar el diagnóstico: {exception.Message}";
        }
    }

    private async Task ExportHistoryAsync()
    {
        var operation = BeginOperation();
        try
        {
            LastExportPath = await _exportService.ExportHistoryCsvAsync(History.ToArray(), operation.Token);
            StatusMessage = $"Histórico exportado en {LastExportPath}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo exportar el histórico: {exception.Message}";
        }
    }

    private void OnDeviceDiscovered(object? sender, BleDevice device) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var existing = Devices.FirstOrDefault(item => item.DeviceId == device.DeviceId);
        if (existing is not null)
        {
            Devices.Remove(existing);
        }

        Devices.Add(device);
        if (FeaturedDevice is null || device.Rssi > FeaturedDevice.Rssi)
        {
            FeaturedDevice = device;
        }
    });

    private void OnConnectionStateChanged(object? sender, BleConnectionState state) =>
        MainThread.BeginInvokeOnMainThread(() => ApplyConnectionState(state));

    private void ApplyConnectionState(BleConnectionState state)
    {
        StatusMessage = $"Estado BLE: {state}.";
        var transition = BleConnectionStateReducer.Apply(
            new BleConnectionPresentationState(IsConnecting, IsConnected),
            state);
        IsConnecting = transition.State.IsConnecting;
        IsConnected = transition.State.IsConnected;
        if (state is BleConnectionState.Disconnected or BleConnectionState.Error)
        {
            StopTelemetryPolling();
            if (transition.ConnectionEnded)
            {
                var reason = state == BleConnectionState.Error
                    ? "Conexión BLE finalizada por error"
                    : "Conexión BLE finalizada";
                _ = EndSelectedSessionAsync(reason);
                ConnectionEnded?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnDiagnosticEntryReceived(object? sender, DiagnosticEntry entry)
    {
        if (!DiagnosticsEnabled)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Diagnostics.Insert(0, entry);
            while (Diagnostics.Count > 500)
            {
                Diagnostics.RemoveAt(Diagnostics.Count - 1);
            }
        });
        _ = PersistDiagnosticAsync(entry);
    }

    private void OnSnapshotReceived(object? sender, BatterySnapshot snapshot) =>
        MainThread.BeginInvokeOnMainThread(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(BatterySnapshot snapshot)
    {
        if (_previousSnapshot is not null && _previousSnapshot.ChargeState != snapshot.ChargeState)
        {
            _recentPower.Clear();
        }

        _previousSnapshot = snapshot;
        if (snapshot.PowerWatts is not null)
        {
            _recentPower.Add(snapshot.PowerWatts.Value);
            if (_recentPower.Count > 30)
            {
                _recentPower.RemoveAt(0);
            }
        }

        Snapshot = snapshot;
        _ = PersistSnapshotAsync(snapshot);
    }

    private async Task PersistSnapshotAsync(BatterySnapshot snapshot)
    {
        try
        {
            await _repository.InitializeAsync(CancellationToken.None);
            if (await _repository.SaveSnapshotIfSignificantAsync(snapshot, CancellationToken.None))
            {
                MainThread.BeginInvokeOnMainThread(() => History.Insert(0, snapshot));
            }
        }
        catch (Exception exception)
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"No se guardó la muestra: {exception.Message}");
        }
    }

    private async Task EndSelectedSessionAsync(string reason)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        try
        {
            await _repository.EndSessionAsync(
                SelectedDevice.DeviceId,
                DateTimeOffset.UtcNow,
                reason,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"No se cerró la sesión histórica: {exception.Message}");
        }
    }

    private async Task PersistDiagnosticAsync(DiagnosticEntry entry)
    {
        try
        {
            await _repository.SaveDiagnosticAsync(entry, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MainThread.BeginInvokeOnMainThread(() => StatusMessage = $"No se guardó un evento diagnóstico: {exception.Message}");
        }
    }

    private void StartTelemetryPolling()
    {
        lock (_telemetryPollingLock)
        {
            _telemetryPollingCancellation?.Cancel();
            _telemetryPollingCancellation = new CancellationTokenSource();
            _ = PollTelemetryAsync(_telemetryPollingCancellation);
        }
    }

    private void StopTelemetryPolling()
    {
        lock (_telemetryPollingLock)
        {
            var cancellation = _telemetryPollingCancellation;
            _telemetryPollingCancellation = null;
            cancellation?.Cancel();
        }
    }

    private async Task PollTelemetryAsync(CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            while (true)
            {
                try
                {
                    await _bleService.QueryTelemetryAsync(cancellationToken);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (StatusMessage.StartsWith("No se actualizó la telemetría", StringComparison.Ordinal))
                        {
                            StatusMessage = "Conectado; actualización automática activa.";
                        }
                    });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    if (_bleService.ConnectionState == BleConnectionState.Connected)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                            StatusMessage = $"No se actualizó la telemetría; reintentando: {exception.Message}");
                    }
                }

                await Task.Delay(TelemetryRefreshInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when disconnecting, reconnecting, or closing the app.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void ResetLiveSessionMetrics()
    {
        _recentPower.Clear();
        _previousSnapshot = null;
        Snapshot = null;
        OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(ChargeTimeText));
        OnPropertyChanged(nameof(ChargingPowerText));
        OnPropertyChanged(nameof(CellDeltaText));
    }

    private void NotifySnapshotProperties()
    {
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(EnergyEstimate));
        OnPropertyChanged(nameof(StateOfChargeText));
        OnPropertyChanged(nameof(StateOfChargeProgress));
        OnPropertyChanged(nameof(ChargeStateText));
        OnPropertyChanged(nameof(VoltageText));
        OnPropertyChanged(nameof(CurrentText));
        OnPropertyChanged(nameof(RemainingEnergyText));
        OnPropertyChanged(nameof(CycleCountText));
        OnPropertyChanged(nameof(PowerFlowTitle));
        OnPropertyChanged(nameof(PowerFlowText));
        OnPropertyChanged(nameof(PrimaryEstimateTitle));
        OnPropertyChanged(nameof(PrimaryEstimateText));
        OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(ChargeTimeText));
        OnPropertyChanged(nameof(ChargingPowerText));
        OnPropertyChanged(nameof(CellDeltaText));
        OnPropertyChanged(nameof(CellBalanceStatusText));
        OnPropertyChanged(nameof(CellBalanceIsUnknown));
        OnPropertyChanged(nameof(CellBalanceIsAcceptable));
        OnPropertyChanged(nameof(CellBalanceNeedsReview));
        OnPropertyChanged(nameof(HealthTitle));
        OnPropertyChanged(nameof(HealthValueText));
        OnPropertyChanged(nameof(HealthDetailText));
        OnPropertyChanged(nameof(HasEstimatePrecision));
        OnPropertyChanged(nameof(EstimateIsApproximate));
        OnPropertyChanged(nameof(HasActiveAlarms));
        OnPropertyChanged(nameof(ActiveAlarmsText));
        OnPropertyChanged(nameof(MaximumTemperatureText));
        OnPropertyChanged(nameof(StateOfHealthText));
        OnPropertyChanged(nameof(CapacityText));
        OnPropertyChanged(nameof(CellCountText));
        OnPropertyChanged(nameof(DataQualityText));
        OnPropertyChanged(nameof(MinimumCellText));
        OnPropertyChanged(nameof(MaximumCellText));
        OnPropertyChanged(nameof(CellReadings));
        OnPropertyChanged(nameof(LastUpdatedText));
        NotifyFreshnessProperties();
    }

    private void NotifyFreshnessProperties()
    {
        OnPropertyChanged(nameof(StaleStatusText));
        OnPropertyChanged(nameof(HasFreshTelemetry));
        OnPropertyChanged(nameof(HasStaleTelemetry));
    }

#if DEBUG
    /// <summary>
    /// Debug-only fixture. The dashboard cannot be reached on an emulator
    /// because no BMS answers there, and a fixed layout has to be checked for
    /// clipping before it reaches hardware. It never persists and it is
    /// compiled out of Release, so the shipped app still has no simulated data.
    /// </summary>
    public void ApplyPreviewSnapshot()
    {
        double[] cells = [3.336, 3.341, 3.338, 3.344, 3.335, 3.340, 3.339, 3.342];
        _recentPower.Clear();
        _recentPower.AddRange([168, 172, 170, 169, 171, 170]);
        SelectedDevice ??= new BleDevice("PC-5C0A", "PREVIEW", -62, DateTimeOffset.UtcNow);
        Snapshot = new BatterySnapshot(
            DateTimeOffset.UtcNow,
            "PREVIEW",
            93,
            98,
            26.71,
            6.35,
            BatteryCalculations.PowerWatts(26.71, 6.35),
            279,
            null,
            300,
            5,
            cells,
            [29.5, 31.5, 30.2],
            BatteryCalculations.CellDeltaMillivolts(cells),
            ChargeState.Charging,
            [],
            DataQuality.Valid,
            false);
    }
#endif

    private static string FormatDuration(double hours)
    {
        if (!double.IsFinite(hours) || hours < 0)
        {
            return "Sin estimación";
        }

        var totalMinutes = Math.Max(1, (int)Math.Round(TimeSpan.FromHours(hours).TotalMinutes));
        var wholeHours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return wholeHours == 0 ? $"{minutes} min" : $"{wholeHours} h {minutes} min";
    }

    private CancellationTokenSource BeginOperation(CancellationToken cancellationToken = default)
    {
        lock (_operationLock)
        {
            _activeOperation?.Cancel();
            _activeOperation?.Dispose();
            _activeOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return _activeOperation;
        }
    }

    public void CancelActiveOperation()
    {
        lock (_operationLock)
        {
            var operation = _activeOperation;
            _activeOperation = null;
            operation?.Cancel();
            operation?.Dispose();
        }

        StopTelemetryPolling();
    }

    public void Dispose()
    {
        _bleService.DeviceDiscovered -= OnDeviceDiscovered;
        _bleService.ConnectionStateChanged -= OnConnectionStateChanged;
        _bleService.DiagnosticEntryReceived -= OnDiagnosticEntryReceived;
        _bleService.SnapshotReceived -= OnSnapshotReceived;
        _staleTimer.Dispose();
        StopTelemetryPolling();
        CancelActiveOperation();
    }
}
