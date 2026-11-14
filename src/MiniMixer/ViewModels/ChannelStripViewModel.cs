using MiniMixer.Audio;
using MiniMixer.Audio.Sources;
using MiniMixer.Services;

namespace MiniMixer.ViewModels;

/// <summary>Tira de entrada: micrófono (hardware) o entrada virtual.</summary>
public sealed class ChannelStripViewModel : StripViewModelBase
{
    private readonly AudioEngine _engine;
    private DeviceOption? _selectedSource;
    private double _eqLow, _eqMid, _eqHigh, _highPass = 20, _lowPass = 20;
    private double _panX, _padY, _gate, _denoiser, _comp;

    public ChannelStripViewModel(AudioEngine engine, string title, bool isMic,
                                 IReadOnlyList<DeviceOption> sources, string? defaultDeviceId)
    {
        _engine = engine;
        Title = title;
        IsMic = isMic;
        Sources = sources;
        Channel = engine.CreateChannel(title);

        // Micrófono → MIC (micrófono virtual para Meet/Discord). No va a OUT por defecto: provocaría acople.
        // Entradas virtuales → OUT.
        for (int i = 0; i < engine.Buses.Count; i++)
        {
            bool initial = isMic ? i == AudioEngine.MicBus : i == AudioEngine.OutBus;
            Routes.Add(new RouteViewModel(engine.Buses[i].Name, i, initial, Channel.SetRoute));
        }

        Gain = 100; // 100 % = 0 dB (ganancia unitaria)

        // 1º lo que eligió el usuario la última vez; si no, el valor automático
        SelectedSource = AppSettings.Current.TryGetDevice(title, sources, out var saved)
            ? saved
            : sources.FirstOrDefault(s => defaultDeviceId != null && s.DeviceId == defaultDeviceId && !s.IsLoopback)
              ?? sources.FirstOrDefault();
        _loaded = true;
    }

    private readonly bool _loaded;

    public InputChannel Channel { get; }
    private ChannelProcessor P => Channel.Processor;

    public bool IsMic { get; }
    public IReadOnlyList<DeviceOption> Sources { get; }

    public DeviceOption? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (!SetProperty(ref _selectedSource, value)) return;
            OpenSource(value);
            if (_loaded) AppSettings.Current.SetDevice(Title, value); // recordar la elección
        }
    }

    // ---- EQ ----
    public double EqLow { get => _eqLow; set { if (SetProperty(ref _eqLow, value)) P.Eq.LowDb = (float)value; } }
    public double EqMid { get => _eqMid; set { if (SetProperty(ref _eqMid, value)) P.Eq.MidDb = (float)value; } }
    public double EqHigh { get => _eqHigh; set { if (SetProperty(ref _eqHigh, value)) P.Eq.HighDb = (float)value; } }
    public double HighPass { get => _highPass; set { if (SetProperty(ref _highPass, value)) P.Eq.HighPassHz = (float)value; } }
    /// <summary>En kHz (2..20) para que el knob muestre "20" y no "20000".</summary>
    public double LowPass { get => _lowPass; set { if (SetProperty(ref _lowPass, value)) P.Eq.LowPassHz = (float)(value * 1000); } }

    // ---- Intellipan: X = paneo L/R, Y = brillo (±6 dB en agudos) ----
    public double PanX { get => _panX; set { if (SetProperty(ref _panX, value)) P.Pan = (float)value; } }
    public double PadY { get => _padY; set { if (SetProperty(ref _padY, value)) P.Eq.TiltDb = (float)(value * 6); } }

    // ---- Dinámica (knobs 0..10) ----
    public double Gate { get => _gate; set { if (SetProperty(ref _gate, value)) P.GateAmount = (float)value; } }
    public double Denoiser { get => _denoiser; set { if (SetProperty(ref _denoiser, value)) P.DenoiserAmount = (float)value; } }
    public double Comp { get => _comp; set { if (SetProperty(ref _comp, value)) P.CompAmount = (float)value; } }

    protected override void OnGainChanged(double value) => P.GainDb = PercentToDb(value);
    protected override void OnMuteChanged(bool value) => P.Mute = value;

    protected override void OnSoloChanged(bool value)
    {
        P.Solo = value;
        _engine.UpdateSolo();
    }

    public override void UpdateMeters()
    {
        var m = P.Meter.Read();
        PushLevels(m.PeakL, m.PeakR);
    }

    private void OpenSource(DeviceOption? option)
    {
        Status = "";
        if (option?.DeviceId == null)
        {
            Channel.SetSource(null);
            return;
        }

        try
        {
            var device = DeviceCatalog.Get(option.DeviceId)
                         ?? throw new InvalidOperationException("Dispositivo no disponible");
            IAudioSource source = option.IsLoopback
                ? DeviceCaptureSource.Loopback(device)
                : DeviceCaptureSource.Capture(device);
            Channel.SetSource(source);

            if (option.IsLoopback && _engine.Buses.Any(b => b.DeviceId == option.DeviceId))
                Status = "⚠ Realimentación: ese dispositivo es salida de un bus";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }
}
