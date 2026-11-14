using System.Collections.ObjectModel;

namespace MiniMixer.ViewModels;

/// <summary>
/// Propiedades comunes a toda tira con fader (entradas, apps y buses).
/// Todos los faders trabajan en % (0..100). Cada tira define qué nivel en dB es su 100 %.
/// </summary>
public abstract class StripViewModelBase : ObservableObject
{
    private string _title = "";
    private string _status = "";
    private double _gain;
    private bool _isMuted, _isSolo;
    private double _levelL, _levelR;

    public string Title { get => _title; protected set => SetProperty(ref _title, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    /// <summary>Volumen en % (0..100).</summary>
    public double Gain
    {
        get => _gain;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _gain, value)) return;
            OnPropertyChanged(nameof(GainText));
            OnGainChanged(value);
        }
    }

    public string GainText => $"{Gain:0}%";
    public double FaderMin => 0;
    public double FaderMax => 100;
    public double FaderDefault => 100;
    public virtual bool ShowSolo => true;

    /// <summary>Nivel en dB que corresponde al 100 % del fader.</summary>
    protected virtual double MaxDb => 0;

    /// <summary>
    /// % → dB con curva de audio (ley cuadrática): 100 % = MaxDb, 50 % = MaxDb − 12 dB, 0 % = silencio.
    /// </summary>
    protected float PercentToDb(double percent) =>
        percent <= 0.05 ? -100f : (float)(MaxDb + 40 * Math.Log10(percent / 100.0));

    public bool IsMuted
    {
        get => _isMuted;
        set { if (SetProperty(ref _isMuted, value)) OnMuteChanged(value); }
    }

    public bool IsSolo
    {
        get => _isSolo;
        set { if (SetProperty(ref _isSolo, value)) OnSoloChanged(value); }
    }

    public ObservableCollection<RouteViewModel> Routes { get; } = new();

    /// <summary>Nivel lineal 0..1 (el VuMeter lo convierte a dB).</summary>
    public double LevelL { get => _levelL; private set => SetProperty(ref _levelL, value); }
    public double LevelR { get => _levelR; private set => SetProperty(ref _levelR, value); }

    /// <summary>Caída suave estilo VU: sube instantáneo, baja ~20% por frame.</summary>
    protected void PushLevels(double l, double r)
    {
        LevelL = Math.Max(l, LevelL * 0.8);
        LevelR = Math.Max(r, LevelR * 0.8);
    }

    public abstract void UpdateMeters();

    protected virtual void OnGainChanged(double value) { }
    protected virtual void OnMuteChanged(bool value) { }
    protected virtual void OnSoloChanged(bool value) { }
}
