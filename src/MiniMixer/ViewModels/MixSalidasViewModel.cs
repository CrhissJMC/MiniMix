using System.Windows.Threading;
using MiniMixer.Audio;
using MiniMixer.Services;
using NAudio.CoreAudioApi;

namespace MiniMixer.ViewModels;

/// <summary>
/// "MixSalidas": dispositivo de reproducción virtual que se pone como salida predeterminada de Windows.
/// Las apps que suenan en él se capturan una a una y se envían a A1/A2/A3/B1.
///
/// Su volumen de Windows (icono de altavoz de la barra de tareas) está enlazado en ambos sentidos
/// con el fader de esta fila. Como el driver no aplica ese volumen a lo capturado (medido: 25 %
/// en Windows → mismo nivel capturado), el mixer lo aplica a todas las apps capturadas.
/// </summary>
public sealed class MixSalidasViewModel : StripViewModelBase, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private DeviceOption? _selectedDevice;
    private MMDevice? _device;
    private bool _syncing;
    private bool _isWindowsDefault;

    public MixSalidasViewModel(IReadOnlyList<DeviceOption> devices, int busCount, IReadOnlyList<string> busNames,
                               Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Title = "MixSalidas";
        Devices = devices;

        // Ruteo por defecto para las apps nuevas que suenen en MixSalidas: A1
        for (int i = 0; i < busCount; i++)
            Routes.Add(new RouteViewModel(busNames[i], i, initial: i == AudioEngine.OutBus, (_, _) => { }));

        Gain = 100;
        // Orden: lo guardado › un dispositivo llamado "MixSalidas" › la salida predeterminada de Windows
        // si es un dispositivo virtual de Voicemeeter (es la que se usa como MixSalidas) › ninguno.
        if (AppSettings.Current.TryGetDevice(SettingsSlot, devices, out var saved))
        {
            SelectedDevice = saved;
        }
        else
        {
            string? autoId = DeviceCatalog.FindMixSalidasId(devices);
            if (autoId == null)
            {
                string? def = DeviceCatalog.DefaultId(DataFlow.Render);
                var defOption = devices.FirstOrDefault(d => d.DeviceId != null && d.DeviceId == def);
                if (defOption != null && defOption.Label.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase))
                    autoId = def;
            }
            SelectedDevice = devices.FirstOrDefault(d => autoId != null && d.DeviceId == autoId) ?? devices.FirstOrDefault();
        }
        _loaded = true;
    }

    private const string SettingsSlot = "MixSalidas";
    private readonly bool _loaded;

    /// <summary>Se dispara cuando cambia el volumen/mute maestro (desde la app o desde Windows).</summary>
    public event Action? MasterChanged;

    public IReadOnlyList<DeviceOption> Devices { get; }
    public override bool ShowSolo => false;
    public string? DeviceId => _selectedDevice?.DeviceId;
    public IReadOnlyList<bool> DefaultRoutes => Routes.Select(r => r.IsOn).ToList();

    public bool IsWindowsDefault { get => _isWindowsDefault; private set => SetProperty(ref _isWindowsDefault, value); }

    private string _hint = "";
    public string Hint { get => _hint; private set => SetProperty(ref _hint, value); }

    /// <summary>Volumen maestro en dB (el mismo que aplicaría Windows) o silencio.</summary>
    public float MasterDb { get; private set; }
    public bool MasterMute => IsMuted;

    public DeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!SetProperty(ref _selectedDevice, value)) return;
            Open(value);
            if (_loaded) AppSettings.Current.SetDevice(SettingsSlot, value);
        }
    }

    private void Open(DeviceOption? option)
    {
        if (_device != null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnWindowsVolume; } catch { }
            _device = null;
        }
        Status = "";

        if (option?.DeviceId != null)
        {
            try
            {
                _device = DeviceCatalog.Get(option.DeviceId);
                if (_device != null)
                {
                    _device.AudioEndpointVolume.OnVolumeNotification += OnWindowsVolume;
                    SyncFromWindows();
                }
            }
            catch (Exception ex)
            {
                Status = ex.Message;
                _device = null;
            }
        }

        UpdateHint();
        RecomputeMaster();
    }

    /// <summary>Notificación de Windows (hilo COM) → hilo de UI.</summary>
    private void OnWindowsVolume(AudioVolumeNotificationData data) =>
        _dispatcher.InvokeAsync(SyncFromWindows);

    private void SyncFromWindows()
    {
        if (_device == null) return;
        try
        {
            _syncing = true;
            Gain = Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100, 1);
            IsMuted = _device.AudioEndpointVolume.Mute;
        }
        catch { }
        finally { _syncing = false; }
        RecomputeMaster();
    }

    protected override void OnGainChanged(double value)
    {
        if (!_syncing && _device != null)
            try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(value / 100.0); } catch { }
        RecomputeMaster();
    }

    protected override void OnMuteChanged(bool value)
    {
        if (!_syncing && _device != null)
            try { _device.AudioEndpointVolume.Mute = value; } catch { }
        RecomputeMaster();
    }

    private void RecomputeMaster()
    {
        float db;
        if (_device != null)
        {
            // Mismo valor en dB que muestra Windows para ese porcentaje
            try { db = Gain <= 0.05 ? -100f : _device.AudioEndpointVolume.MasterVolumeLevel; }
            catch { db = PercentToDb(Gain); }
        }
        else db = PercentToDb(Gain);

        MasterDb = db;
        MasterChanged?.Invoke();
    }

    /// <summary>Comprueba periódicamente si sigue siendo la salida predeterminada de Windows.</summary>
    public void UpdateHint()
    {
        IsWindowsDefault = DeviceId != null && DeviceCatalog.DefaultId(DataFlow.Render) == DeviceId;
        Hint = DeviceId == null
            ? "Elige el dispositivo virtual que hace de MixSalidas (renombra uno de Voicemeeter a «MixSalidas» para que se detecte solo)."
            : IsWindowsDefault
                ? "✓ Es la salida predeterminada de Windows: todas las apps pasan por aquí."
                : "⚠ Ponlo como salida predeterminada de Windows (Configuración › Sonido) para que las apps pasen por aquí.";
    }

    public override void UpdateMeters()
    {
        float peak = 0;
        try { if (_device != null) peak = _device.AudioMeterInformation.MasterPeakValue; } catch { }
        PushLevels(peak, peak);
    }

    public void Dispose()
    {
        if (_device != null)
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnWindowsVolume; } catch { }
    }
}
