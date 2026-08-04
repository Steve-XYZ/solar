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
    private readonly IBleMonitorService _bleService;
    private readonly IBatteryRepository _repository;
    private readonly IExportService _exportService;
    private readonly List<double> _recentPower = [];
    private readonly Timer _staleTimer;
    private readonly object _operationLock = new();
    private CancellationTokenSource? _activeOperation;
    private BatterySnapshot? _snapshot;
    private BleDevice? _selectedDevice;
    private GattProfile? _gattProfile;
    private string _statusMessage = "Listo para escanear.";
    private string _lastExportPath = string.Empty;
    private bool _diagnosticsEnabled;

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
            _ => MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(StaleStatusText))),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    public ObservableCollection<BleDevice> Devices { get; }
    public ObservableCollection<DiagnosticEntry> Diagnostics { get; }
    public ObservableCollection<BatterySnapshot> History { get; }
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
                OnPropertyChanged(nameof(EnergyEstimate));
                OnPropertyChanged(nameof(HasSnapshot));
            }
        }
    }

    public BleDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
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

    public bool HasSnapshot => Snapshot is not null;

    public string StaleStatusText => Snapshot switch
    {
        null => "Sin telemetría",
        { IsStale: true } => "Datos obsoletos",
        { } value when DateTimeOffset.UtcNow - value.Timestamp > TimeSpan.FromSeconds(15) => "Datos obsoletos",
        _ => "Datos recientes",
    };

    public string ChargeStateText => Snapshot?.ChargeState switch
    {
        ChargeState.Charging => "Cargando",
        ChargeState.Discharging => "Descargando",
        ChargeState.Idle => "Reposo",
        _ => "Sin datos",
    };

    public string RuntimeText => EnergyEstimate switch
    {
        { RuntimeHours: not null } estimate => $"{estimate.RuntimeHours:F1} h",
        { Confidence: "inestable" } => "corriente inestable",
        _ => "calculando",
    };

    public string AverageTemperatureText => Snapshot?.TemperaturesCelsius.Count > 0
        ? $"{Snapshot.TemperaturesCelsius.Average():F1} °C"
        : "sin datos";

    public string MinimumCellText => Snapshot?.CellVoltages.Count > 0
        ? $"{Snapshot.CellVoltages.Min():F3} V"
        : "sin datos";

    public string MaximumCellText => Snapshot?.CellVoltages.Count > 0
        ? $"{Snapshot.CellVoltages.Max():F3} V"
        : "sin datos";

    public EnergyEstimate? EnergyEstimate => Snapshot is null || Snapshot.PackVoltageVolts is null
        ? null
        : BatteryCalculations.EstimateEnergy(
            Snapshot.RemainingCapacityAh,
            Snapshot.FullCapacityAh,
            Snapshot.StateOfChargePercent,
            Snapshot.PackVoltageVolts.Value,
            _recentPower);

    private async Task ScanAsync()
    {
        var operation = BeginOperation();
        try
        {
            Devices.Clear();
            StatusMessage = "Escaneando dispositivos PC-* durante 12 segundos…";
            if (!await _bleService.EnsurePermissionsAsync(operation.Token))
            {
                StatusMessage = "Permisos de dispositivos cercanos denegados.";
                return;
            }

            await _bleService.ScanAsync(TimeSpan.FromSeconds(12), operation.Token);
            StatusMessage = Devices.Count == 0 ? "No se encontraron baterías PC-*." : "Escaneo terminado.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"No se pudo escanear: {exception.Message}";
        }
    }

    private async Task ConnectAsync(object? parameter)
    {
        if (parameter is not BleDevice device)
        {
            return;
        }

        var operation = BeginOperation();
        try
        {
            SelectedDevice = device;
            StatusMessage = $"Conectando con {device.Name}…";
            await _bleService.ConnectAsync(device, operation.Token);
            GattProfile = _bleService.CurrentProfile;
            var connectedAt = DateTimeOffset.UtcNow;
            await _repository.InitializeAsync(operation.Token);
            await _repository.SaveDeviceAsync(
                new BatteryDeviceInfo(device.Name, device.DeviceId, device.Rssi, null, null, null, 300, connectedAt, connectedAt),
                operation.Token);
            await _repository.BeginSessionAsync(device.DeviceId, connectedAt, operation.Token);
            StatusMessage = GattProfile?.NotificationsEnabled == true
                ? "Conectado; canal PaceEX verificado y notificaciones activas."
                : "Conectado; perfil GATT capturado, canal PaceEX aún no verificado.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Error de conexión: {exception.Message}";
        }
    }

    private async Task DisconnectAsync()
    {
        CancelActiveOperation();
        try
        {
            await _bleService.DisconnectAsync(CancellationToken.None);
            await EndSelectedSessionAsync("Desconexión solicitada por el usuario");
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
    });

    private void OnConnectionStateChanged(object? sender, BleConnectionState state) =>
        MainThread.BeginInvokeOnMainThread(() => ApplyConnectionState(state));

    private void ApplyConnectionState(BleConnectionState state)
    {
        StatusMessage = $"Estado BLE: {state}.";
        if (state == BleConnectionState.Disconnected)
        {
            _ = EndSelectedSessionAsync("Conexión BLE finalizada");
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
        Snapshot = snapshot;
        if (snapshot.PowerWatts is not null)
        {
            _recentPower.Add(snapshot.PowerWatts.Value);
            if (_recentPower.Count > 30)
            {
                _recentPower.RemoveAt(0);
            }
        }

        _ = PersistSnapshotAsync(snapshot);
        OnPropertyChanged(nameof(EnergyEstimate));
        OnPropertyChanged(nameof(ChargeStateText));
        OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(AverageTemperatureText));
        OnPropertyChanged(nameof(MinimumCellText));
        OnPropertyChanged(nameof(MaximumCellText));
        OnPropertyChanged(nameof(StaleStatusText));
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

    private CancellationTokenSource BeginOperation()
    {
        lock (_operationLock)
        {
            _activeOperation?.Cancel();
            _activeOperation?.Dispose();
            _activeOperation = new CancellationTokenSource();
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
    }

    public void Dispose()
    {
        _bleService.DeviceDiscovered -= OnDeviceDiscovered;
        _bleService.ConnectionStateChanged -= OnConnectionStateChanged;
        _bleService.DiagnosticEntryReceived -= OnDiagnosticEntryReceived;
        _bleService.SnapshotReceived -= OnSnapshotReceived;
        _staleTimer.Dispose();
        CancelActiveOperation();
    }
}
