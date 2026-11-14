using System.Windows.Threading;
using MiniMixer.Audio;
using MiniMixer.Audio.Sources;
using MiniMixer.Services;
using MiniMixer.Sessions;
using NAudio.CoreAudioApi;

namespace MiniMixer.ViewModels;

/// <summary>
/// Fila de aplicación.
///  - Volumen % / Mute → ISimpleAudioVolume de TODAS las sesiones del proceso (es el mismo control que el
///    mezclador de volumen de Windows; afecta también a lo que se captura y se envía a los buses).
///  - A1 / A2 / A3 / B1 → captura por proceso (Process Loopback) enviada a los buses elegidos.
///  - El volumen maestro de MixSalidas (= volumen de Windows) se aplica a la captura.
/// </summary>
public sealed class AppStripViewModel : StripViewModelBase, IDisposable
{
    private readonly AudioEngine _engine;
    private readonly Dispatcher _dispatcher;
    private IReadOnlyList<AudioSessionControl> _sessions = Array.Empty<AudioSessionControl>();
    private InputChannel? _channel;
    private bool _syncing;
    private bool _isExpanded = true;
    private string _deviceText = "";
    private bool _playsOnMixSalidas;
    private float _masterDb;
    private bool _masterMute;

    public AppStripViewModel(AudioEngine engine, AppAudioGroup group, Dispatcher dispatcher,
                             string? mixSalidasId, IReadOnlyList<bool> defaultRoutes, float masterDb, bool masterMute)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _masterDb = masterDb;
        _masterMute = masterMute;
        Pid = group.ProcessId;
        Title = group.DisplayName;
        IsSystemSounds = group.IsSystemSounds;
        AppKey = group.AppKey;

        // Lo que se usó la última vez que se abrió esta app (por ejecutable)
        var memory = AppSettings.Current.GetApp(AppKey);

        UpdateSessions(group, mixSalidasId);

        if (memory != null)
        {
            // Volumen y mute de la última vez → se escriben en Windows (todas las sesiones de la app)
            Gain = memory.Volume;
            IsMuted = memory.Mute;
            Status = "";
        }

        // Ruteo: el recordado; si no hay, las apps que suenan en MixSalidas usan el ruteo por defecto.
        bool useDefaults = PlaysOnMixSalidas && !IsSystemSounds;
        for (int i = 0; i < engine.Buses.Count; i++)
        {
            bool initial = memory != null && memory.Routes.Length == engine.Buses.Count
                ? memory.Routes[i] && !IsSystemSounds
                : useDefaults && i < defaultRoutes.Count && defaultRoutes[i];
            Routes.Add(new RouteViewModel(engine.Buses[i].Name, i, initial: false, OnRouteChanged));
            if (initial) Routes[i].IsOn = true;
        }

        _initialized = true;
        Remember();
    }

    /// <summary>Clave estable de la app (nombre del ejecutable) para recordar su configuración.</summary>
    public string AppKey { get; }

    /// <summary>true al terminar el constructor: antes no se guarda nada (no pisar lo recordado).</summary>
    private bool _initialized;

    /// <summary>Sesiones ya vistas (las nuevas de la misma app reciben el volumen actual de la fila).</summary>
    private readonly HashSet<string> _knownSessions = new();

    private void Remember()
    {
        if (!_initialized || AppKey.StartsWith("pid", StringComparison.Ordinal)) return;
        AppSettings.Current.RememberApp(AppKey, Gain, IsMuted, Routes.Select(r => r.IsOn));
    }

    public uint Pid { get; }
    public bool IsSystemSounds { get; }
    public override bool ShowSolo => false;

    /// <summary>false = fila minimizada (sólo nombre y %).</summary>
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    /// <summary>Dispositivo(s) de Windows en los que suena la app.</summary>
    public string DeviceText { get => _deviceText; private set => SetProperty(ref _deviceText, value); }

    /// <summary>true si la app suena en MixSalidas (si no, suena directa en su dispositivo y rutearla la duplica).</summary>
    public bool PlaysOnMixSalidas
    {
        get => _playsOnMixSalidas;
        private set { if (SetProperty(ref _playsOnMixSalidas, value)) OnPropertyChanged(nameof(ShowDirectWarning)); }
    }

    public bool ShowDirectWarning => !PlaysOnMixSalidas && Routes.Any(r => r.IsOn);

    /// <summary>
    /// Refresca las sesiones. Las sesiones NUEVAS de la app (p.ej. el navegador abre otra al reproducir un
    /// vídeo, o la app se reinicia su audio) reciben el volumen/mute de la fila; de las ya conocidas se
    /// lee el volumen de Windows por si el usuario lo cambió desde el mezclador de Windows.
    /// </summary>
    public void UpdateSessions(AppAudioGroup group, string? mixSalidasId)
    {
        _sessions = group.Sessions;
        PlaysOnMixSalidas = mixSalidasId != null && group.DeviceIds.Contains(mixSalidasId);
        DeviceText = "→ " + string.Join(", ", group.DeviceNames);

        if (_sessions.Count == 0) return;

        AudioSessionControl? known = null;
        var fresh = new List<AudioSessionControl>();
        foreach (var s in _sessions)
        {
            string id;
            try { id = s.GetSessionInstanceIdentifier; } catch { continue; }
            if (_knownSessions.Add(id)) fresh.Add(s);
            else known ??= s;
        }

        if (_initialized)
        {
            // Sesiones nuevas de una app ya listada: igualarlas a lo que muestra la fila
            foreach (var s in fresh)
                try { s.SimpleAudioVolume.Volume = (float)(Gain / 100.0); s.SimpleAudioVolume.Mute = IsMuted; } catch { }
        }
        else
        {
            known = _sessions[0]; // primera vez: punto de partida = lo que tiene Windows
        }

        if (known == null) return;
        try
        {
            _syncing = true;
            var volume = known.SimpleAudioVolume;
            double g = Math.Round(volume.Volume * 100, 1);
            bool m = volume.Mute;
            bool changed = Math.Abs(g - Gain) > 0.05 || m != IsMuted;
            Gain = g;
            IsMuted = m;
            if (changed) Remember(); // cambiado desde el mezclador de Windows: también se recuerda
        }
        catch { /* la sesión murió entre sondeos */ }
        finally { _syncing = false; }
    }

    /// <summary>Volumen maestro de MixSalidas (el de Windows), aplicado a lo capturado de la app.</summary>
    public void ApplyMaster(float db, bool mute)
    {
        _masterDb = db;
        _masterMute = mute;
        if (_channel == null) return;
        _channel.Processor.GainDb = db;
        _channel.Processor.Mute = mute;
    }

    protected override void OnGainChanged(double value)
    {
        if (_syncing) return;
        foreach (var s in _sessions)
            try { s.SimpleAudioVolume.Volume = (float)(value / 100.0); } catch { }
        Remember();
    }

    protected override void OnMuteChanged(bool value)
    {
        if (_syncing) return;
        foreach (var s in _sessions)
            try { s.SimpleAudioVolume.Mute = value; } catch { }
        Remember();
    }

    public override void UpdateMeters()
    {
        // El medidor de la app lo da Windows (IAudioMeterInformation), sin coste de DSP propio.
        float peak = 0;
        foreach (var s in _sessions)
            try { peak = Math.Max(peak, s.AudioMeterInformation.MasterPeakValue); } catch { }
        PushLevels(peak, peak);
    }

    private void OnRouteChanged(int bus, bool on)
    {
        OnPropertyChanged(nameof(ShowDirectWarning));
        Remember();

        if (on && _channel == null)
        {
            if (IsSystemSounds)
            {
                Status = "Los sonidos del sistema no se pueden capturar por proceso";
                return;
            }

            Status = "";
            var source = new ProcessLoopbackSource(Pid, Title);
            source.Faulted += ex => _dispatcher.InvokeAsync(() => Status = "Captura: " + ex.Message);

            _channel = _engine.CreateChannel(Title);
            _channel.Processor.GainDb = _masterDb;
            _channel.Processor.Mute = _masterMute;
            try
            {
                _channel.SetSource(source);
            }
            catch (Exception ex)
            {
                Status = ex.Message;
                _engine.RemoveChannel(_channel);
                _channel = null;
                return;
            }
        }

        _channel?.SetRoute(bus, on);

        if (_channel != null && !Routes.Any(r => r.IsOn))
        {
            _engine.RemoveChannel(_channel);
            _channel = null;
        }
    }

    public void Dispose()
    {
        if (_channel != null)
        {
            _engine.RemoveChannel(_channel);
            _channel = null;
        }
    }
}
