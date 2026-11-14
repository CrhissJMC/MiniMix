using System.IO;
using System.Text.Json;

namespace MiniMixer.Services;

/// <summary>
/// Configuración interna persistente: %APPDATA%\MiniMixer\settings.json
/// </summary>
public sealed class AppSettings
{
    /// <summary>Tope superior permitido para el nivel máximo de una salida.</summary>
    public const double MaxAllowedDb = 1.5;
    public const double MinAllowedDb = -40;

    /// <summary>Arrancar con Windows (directo a la bandeja). Se sincroniza con el registro en cada arranque.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Secciones minimizadas en la ventana.</summary>
    public bool VirtualInputsVisible { get; set; } = true;
    public bool OutputEqVisible { get; set; } = true;
    public bool AppsCompact { get; set; }

    /// <summary>Frecuencia interna de mezcla (Hz). Requiere reiniciar Mini-Mixer.</summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>
    /// Nivel (dB) que entrega cada bus con el fader al 100 %: A1, A2, A3, B1.
    /// Es un tope interno: el fader nunca supera este valor ("doble control").
    /// </summary>
    public Dictionary<string, double> BusMaxDb { get; set; } = new()
    {
        ["A1"] = 0, ["A2"] = 0, ["A3"] = 0, ["B1"] = 0
    };

    /// <summary>Posición (%) del fader de cada salida, para que se mantenga entre reinicios.</summary>
    public Dictionary<string, double> BusGain { get; set; } = new();

    /// <summary>Versión del formato de settings.json (para migraciones).</summary>
    public int SettingsVersion { get; set; }
    public const int CurrentSettingsVersion = 2;

    /// <summary>
    /// Dispositivo elegido por cada fila ("MixSalidas", "A1".."B1", "Micrófono", "Virtual 1"...).
    /// Valor: id del endpoint, con prefijo "loop|" si es loopback, o "none" si se eligió "ninguno".
    /// </summary>
    public Dictionary<string, string> Devices { get; set; } = new();

    public const string NoneKey = "none";

    public static string KeyFor(Audio.DeviceOption? option) =>
        option?.DeviceId == null ? NoneKey : (option.IsLoopback ? "loop|" : "") + option.DeviceId;

    /// <summary>
    /// Opción guardada para una fila. Devuelve false si no hay nada guardado o el dispositivo ya no existe.
    /// </summary>
    public bool TryGetDevice(string slot, IReadOnlyList<Audio.DeviceOption> options, out Audio.DeviceOption? option)
    {
        option = null;
        if (!Devices.TryGetValue(slot, out var key)) return false;
        option = options.FirstOrDefault(o => KeyFor(o) == key);
        return option != null;
    }

    public void SetDevice(string slot, Audio.DeviceOption? option)
    {
        Devices[slot] = KeyFor(option);
        Save();
    }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniMixer", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    /// <summary>
    /// false hasta que se llama a <see cref="Load"/>. Evita que una instancia que sólo pasaba a avisar
    /// a la principal (y se cierra al instante) sobrescriba la configuración con valores por defecto.
    /// </summary>
    private static bool _loaded;

    public static void Load()
    {
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                Current.Migrate();
            }
            else
            {
                Current = new AppSettings { SettingsVersion = CurrentSettingsVersion };
            }
        }
        catch
        {
            Current = new AppSettings(); // archivo dañado: valores por defecto
        }
    }

    public void Save()
    {
        _saveTimer?.Stop();
        if (!_loaded) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* sin acceso al disco: se ignora */ }
    }

    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    /// <summary>
    /// Guardado diferido (0.8 s después del último cambio). Para cambios continuos como arrastrar un fader,
    /// que si no escribirían el archivo decenas de veces por segundo. Usar desde el hilo de UI.
    /// </summary>
    public void SaveSoon()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _saveTimer.Tick += (_, _) => Save();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ===================== Memoria por aplicación =====================

    /// <summary>
    /// Último volumen / mute / ruteo de cada aplicación, por nombre de ejecutable ("brave", "discord"...).
    /// Al volver a abrir la app se le aplica automáticamente.
    /// </summary>
    public Dictionary<string, AppMemory> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AppMemory? GetApp(string key) => Apps.TryGetValue(key, out var m) ? m : null;

    public void RememberApp(string key, double volume, bool mute, IEnumerable<bool> routes)
    {
        Apps[key] = new AppMemory { Volume = Math.Round(volume, 1), Mute = mute, Routes = routes.ToArray() };
        SaveSoon();
    }

    /// <summary>
    /// v1 → v2: el tope de -20 dB de A1–A3 sólo tapaba un fallo del mezclador (ya corregido) y pasa a 0 dB.
    /// Para que no suene de golpe 20 dB más fuerte, el fader de esas salidas se baja al 32 % (= -20 dB).
    /// </summary>
    private void Migrate()
    {
        if (SettingsVersion >= CurrentSettingsVersion) return;
        foreach (var bus in new[] { "A1", "A2", "A3" })
        {
            if (BusMaxDb.TryGetValue(bus, out var max) && max < 0)
            {
                double fader = BusGain.TryGetValue(bus, out var g) ? g : 100;
                // mismo nivel final: fader% nuevo = fader% · 10^(max/40) (ley cuadrática del fader)
                BusGain[bus] = Math.Round(fader * Math.Pow(10, max / 40), 1);
                BusMaxDb[bus] = 0;
            }
        }
        SettingsVersion = CurrentSettingsVersion;
        Save();
    }

    public double GetBusMaxDb(string bus, double fallback) =>
        BusMaxDb.TryGetValue(bus, out var v) ? Math.Clamp(v, MinAllowedDb, MaxAllowedDb) : fallback;
}

/// <summary>Lo que se recuerda de una aplicación.</summary>
public sealed class AppMemory
{
    /// <summary>Volumen en % (0..100), el mismo del mezclador de Windows.</summary>
    public double Volume { get; set; } = 100;
    public bool Mute { get; set; }
    /// <summary>Botones A1, A2, A3, B1.</summary>
    public bool[] Routes { get; set; } = Array.Empty<bool>();
}
