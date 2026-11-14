using MiniMixer.Audio;
using MiniMixer.Services;

namespace MiniMixer.ViewModels;

/// <summary>
/// Bus de salida (A1..A3 o B1): dispositivo físico, volumen en %, mute, medidor,
/// EQ / filtros / paneo / brillo y nivel máximo configurable (tope interno del 100 %).
/// </summary>
public sealed class BusViewModel : StripViewModelBase
{
    private readonly OutputBus _bus;
    private double _maxDb;
    private DeviceOption? _selectedDevice;
    private double _eqLow, _eqMid, _eqHigh, _highPass = 20, _lowPass = 20;
    private double _panX, _padY;

    /// <param name="maxDb">Nivel que corresponde al 100 % del fader (se puede cambiar en Configuración).</param>
    /// <param name="hint">Texto de ayuda bajo la fila.</param>
    public BusViewModel(OutputBus bus, string title, IReadOnlyList<DeviceOption> devices,
                        string? defaultDeviceId, double maxDb, string hint)
    {
        _bus = bus;
        _maxDb = maxDb;
        Title = title;
        Hint = hint;
        Devices = devices;
        Gain = 100;

        // 1º lo que eligió el usuario la última vez; si no, el valor automático
        SelectedDevice = AppSettings.Current.TryGetDevice(bus.Name, devices, out var saved)
            ? saved
            : devices.FirstOrDefault(d => defaultDeviceId != null && d.DeviceId == defaultDeviceId) ?? devices.FirstOrDefault();
        _loaded = true;
    }

    private readonly bool _loaded;

    public string BusName => _bus.Name;
    public string Hint { get; }
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public IReadOnlyList<DeviceOption> Devices { get; }
    public override bool ShowSolo => false;
    public string DeviceLabel => SelectedDevice?.DeviceId == null ? "sin dispositivo" : SelectedDevice.Label;

    // ---- Nivel máximo (Configuración) ----
    protected override double MaxDb => _maxDb;

    /// <summary>dB que entrega el fader al 100 %. Rango permitido: -40 … +1.5 dB.</summary>
    public double MaxLevelDb
    {
        get => _maxDb;
        set
        {
            value = Math.Round(Math.Clamp(value, AppSettings.MinAllowedDb, AppSettings.MaxAllowedDb), 1);
            if (!SetProperty(ref _maxDb, value)) return;
            OnPropertyChanged(nameof(MaxLevelText));
            OnGainChanged(Gain); // re-aplicar con el nuevo tope

            AppSettings.Current.BusMaxDb[BusName] = value;
            AppSettings.Current.SaveSoon();
        }
    }

    public string MaxLevelText => $"{_maxDb:+0.0;-0.0;0.0} dB";

    // ---- EQ / filtros / pad (igual que las entradas) ----
    public double EqLow { get => _eqLow; set { if (SetProperty(ref _eqLow, value)) _bus.Eq.LowDb = (float)value; } }
    public double EqMid { get => _eqMid; set { if (SetProperty(ref _eqMid, value)) _bus.Eq.MidDb = (float)value; } }
    public double EqHigh { get => _eqHigh; set { if (SetProperty(ref _eqHigh, value)) _bus.Eq.HighDb = (float)value; } }
    public double HighPass { get => _highPass; set { if (SetProperty(ref _highPass, value)) _bus.Eq.HighPassHz = (float)value; } }
    /// <summary>En kHz (2..20).</summary>
    public double LowPass { get => _lowPass; set { if (SetProperty(ref _lowPass, value)) _bus.Eq.LowPassHz = (float)(value * 1000); } }
    public double PanX { get => _panX; set { if (SetProperty(ref _panX, value)) _bus.Pan = (float)value; } }
    public double PadY { get => _padY; set { if (SetProperty(ref _padY, value)) _bus.Eq.TiltDb = (float)(value * 6); } }

    public DeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value)) return;
            OnPropertyChanged(nameof(DeviceLabel));
            Open(value);
            if (_loaded) AppSettings.Current.SetDevice(BusName, value); // recordar la elección del usuario
        }
    }

    private void Open(DeviceOption? option)
    {
        try
        {
            var device = option?.DeviceId == null ? null : DeviceCatalog.Get(option.DeviceId);
            _bus.SetDevice(device);
            Status = "";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    protected override void OnGainChanged(double value) => _bus.GainDb = PercentToDb(value);
    protected override void OnMuteChanged(bool value) => _bus.Mute = value;

    public override void UpdateMeters()
    {
        var m = _bus.Meter.Read();
        PushLevels(m.PeakL, m.PeakR);
    }
}
