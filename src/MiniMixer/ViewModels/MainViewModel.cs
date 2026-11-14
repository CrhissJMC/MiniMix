using System.Collections.ObjectModel;
using System.Windows.Threading;
using MiniMixer.Audio;
using MiniMixer.Services;
using MiniMixer.Sessions;
using NAudio.CoreAudioApi;

namespace MiniMixer.ViewModels;

public sealed record SampleRateOption(int Hz)
{
    public override string ToString() => Hz switch
    {
        48000 => "48 kHz (recomendado)",
        _ => $"{Hz / 1000.0:0.#} kHz"
    };
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AudioSessionService _sessionService = new();
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _sessionTimer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _appsCompact;
    private bool _virtualInputsVisible = true;

    public MainViewModel()
    {
        var outputs = DeviceCatalog.Outputs();
        var busNames = Engine.Buses.Select(b => b.Name).ToList();

        // 1) MixSalidas: salida virtual de Windows (volumen enlazado con el de Windows)
        MixSalidas = new MixSalidasViewModel(outputs, Engine.Buses.Count, busNames, _dispatcher);
        MixSalidas.MasterChanged += ApplyMasterToApps;

        // 2) Salidas físicas A1..A3. El 100 % del fader entrega el nivel máximo de Configuración
        //    (por defecto -20 dB). A1 = salida predeterminada de Windows, salvo que sea virtual.
        var settings = AppSettings.Current;
        string? physicalDefault = DeviceCatalog.PickPhysicalDefault(outputs);
        for (int i = 0; i < 3; i++)
        {
            string name = Engine.Buses[i].Name;
            PhysicalOutputs.Add(new BusViewModel(Engine.Buses[i], name, outputs,
                i == 0 ? physicalDefault : null, maxDb: settings.GetBusMaxDb(name, -20), hint: ""));
        }

        // 3) B1 → micrófono virtual "MinMix-B1": se envía a "CABLE Input" (VB-Cable) y las demás apps
        //    graban del otro extremo, "CABLE Output", renombrado en Windows a "MinMix-B1".
        string? cableId = DeviceCatalog.FindVirtualCableId(outputs);
        VirtualMic = new BusViewModel(Engine.Buses[AudioEngine.MicBus], "B1 · MinMix-B1", outputs,
            cableId, maxDb: settings.GetBusMaxDb("B1", 0),
            hint: cableId != null
                ? "En Discord/Meet elige «MinMix-B1» como micrófono (es «CABLE Output» renombrado)."
                : "Necesita VB-Cable (gratis): instálalo, elige aquí «CABLE Input» y en Discord/Meet usa «MinMix-B1».");

        _virtualInputsVisible = settings.VirtualInputsVisible;
        _outputEqVisible = settings.OutputEqVisible;
        _appsCompact = settings.AppsCompact;

        // 4) Micrófono (entrada de hardware por defecto) → B1
        Mic = new ChannelStripViewModel(Engine, "Micrófono", isMic: true,
            DeviceCatalog.Inputs(includeLoopback: false), DeviceCatalog.DefaultId(DataFlow.Capture));

        // 5) Entradas virtuales: cualquier entrada o loopback de salida
        var virtualSources = DeviceCatalog.Inputs(includeLoopback: true);
        for (int i = 1; i <= 3; i++)
            VirtualInputs.Add(new ChannelStripViewModel(Engine, $"Virtual {i}", isMic: false, virtualSources, null));

        // 6) Timers: medidores a ~30 fps, sesiones de apps cada 1 s
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += (_, _) => RefreshApps();
        _sessionTimer.Start();
        RefreshApps();
    }

    public AudioEngine Engine { get; } = new("A1", "A2", "A3", "B1");
    public MixSalidasViewModel MixSalidas { get; }
    public ObservableCollection<BusViewModel> PhysicalOutputs { get; } = new();
    public BusViewModel VirtualMic { get; }
    public ChannelStripViewModel Mic { get; }
    public ObservableCollection<ChannelStripViewModel> VirtualInputs { get; } = new();
    public ObservableCollection<AppStripViewModel> Apps { get; } = new();

    public bool HasNoApps => Apps.Count == 0;

    /// <summary>true = todas las apps minimizadas (sólo nombre y %).</summary>
    public bool AppsCompact
    {
        get => _appsCompact;
        set
        {
            if (!SetProperty(ref _appsCompact, value)) return;
            foreach (var a in Apps) a.IsExpanded = !value;
            AppSettings.Current.AppsCompact = value;
            AppSettings.Current.Save();
        }
    }

    public bool VirtualInputsVisible
    {
        get => _virtualInputsVisible;
        set
        {
            if (!SetProperty(ref _virtualInputsVisible, value)) return;
            AppSettings.Current.VirtualInputsVisible = value;
            AppSettings.Current.Save();
        }
    }

    private bool _outputEqVisible = true;
    /// <summary>Sección "Procesado de salidas A1–A3" desplegada / minimizada.</summary>
    public bool OutputEqVisible
    {
        get => _outputEqVisible;
        set
        {
            if (!SetProperty(ref _outputEqVisible, value)) return;
            AppSettings.Current.OutputEqVisible = value;
            AppSettings.Current.Save();
        }
    }

    // ===================== Configuración =====================

    /// <summary>Todas las salidas (A1, A2, A3, B1) para la pestaña Configuración.</summary>
    public IReadOnlyList<BusViewModel> AllBuses => PhysicalOutputs.Append(VirtualMic).ToList();

    public double MaxAllowedDb => AppSettings.MaxAllowedDb;
    public double MinAllowedDb => AppSettings.MinAllowedDb;

    public IReadOnlyList<SampleRateOption> SampleRateOptions { get; } =
        AudioEngine.SupportedSampleRates.Select(r => new SampleRateOption(r)).ToList();

    /// <summary>Frecuencia guardada (se aplica al reiniciar).</summary>
    public SampleRateOption SelectedSampleRate
    {
        get => SampleRateOptions.FirstOrDefault(o => o.Hz == AppSettings.Current.SampleRate) ?? SampleRateOptions[1];
        set
        {
            if (value == null || value.Hz == AppSettings.Current.SampleRate) return;
            AppSettings.Current.SampleRate = value.Hz;
            AppSettings.Current.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsRestart));
        }
    }

    /// <summary>Frecuencia con la que está funcionando ahora el motor.</summary>
    public string RunningFormat => $"{AudioEngine.SampleRate / 1000.0:0.#} kHz · float32 · WASAPI";

    public bool NeedsRestart => AppSettings.Current.SampleRate != AudioEngine.SampleRate;

    /// <summary>Al encender el PC: abrir la ventana (true) o quedarse sólo en la bandeja con un aviso.</summary>
    public bool StartupShowWindow
    {
        get => AppSettings.Current.StartupShowWindow;
        set
        {
            AppSettings.Current.StartupShowWindow = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => AppSettings.Current.StartWithWindows;
        set
        {
            try { StartupRegistration.SetEnabled(value); } catch { }
            OnPropertyChanged();
        }
    }

    /// <summary>Activa/pausa el refresco de medidores (la ventana oculta no los necesita).</summary>
    public void SetMetersActive(bool active)
    {
        if (active) _meterTimer.Start();
        else _meterTimer.Stop();
    }

    private void ApplyMasterToApps()
    {
        foreach (var a in Apps) a.ApplyMaster(MixSalidas.MasterDb, MixSalidas.MasterMute);
    }

    private void UpdateMeters()
    {
        Mic.UpdateMeters();
        foreach (var v in VirtualInputs) v.UpdateMeters();
        foreach (var a in Apps) a.UpdateMeters();
        MixSalidas.UpdateMeters();
        foreach (var b in PhysicalOutputs) b.UpdateMeters();
        VirtualMic.UpdateMeters();
    }

    private void RefreshApps()
    {
        MixSalidas.UpdateHint();

        IReadOnlyDictionary<uint, AppAudioGroup> groups;
        try { groups = _sessionService.Poll(); }
        catch { return; }

        for (int i = Apps.Count - 1; i >= 0; i--)
        {
            if (groups.ContainsKey(Apps[i].Pid)) continue;
            Apps[i].Dispose();
            Apps.RemoveAt(i);
        }

        string? mixId = MixSalidas.DeviceId;
        foreach (var group in groups.Values.OrderBy(g => g.IsSystemSounds ? 0 : 1).ThenBy(g => g.DisplayName))
        {
            var existing = Apps.FirstOrDefault(a => a.Pid == group.ProcessId);
            if (existing != null)
            {
                existing.UpdateSessions(group, mixId);
            }
            else
            {
                Apps.Add(new AppStripViewModel(Engine, group, _dispatcher, mixId,
                    MixSalidas.DefaultRoutes, MixSalidas.MasterDb, MixSalidas.MasterMute)
                {
                    IsExpanded = !AppsCompact
                });
            }
        }

        OnPropertyChanged(nameof(HasNoApps));
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _sessionTimer.Stop();
        foreach (var a in Apps) a.Dispose();
        MixSalidas.Dispose();
        Engine.Dispose();
    }
}
