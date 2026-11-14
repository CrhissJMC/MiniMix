namespace MiniMixer.Audio.Dsp;

/// <summary>
/// EQ de 3 bandas (Low shelf 120 Hz, Peaking 1 kHz, High shelf 8 kHz)
/// + pasa-altos y pasa-bajos de 12 dB/oct. Opera sobre audio estéreo intercalado.
/// La UI escribe parámetros; el hilo de audio reconstruye coeficientes al inicio del bloque.
/// </summary>
public sealed class Equalizer
{
    private readonly Biquad _low = new(), _mid = new(), _high = new(), _hp = new(), _lp = new();

    private float _lowDb, _midDb, _highDb, _tiltDb;
    private float _hpHz = 20f, _lpHz = 20000f;
    private volatile bool _dirty = true;

    // Estado leído sólo por el hilo de audio
    private bool _flat = true, _hpOn, _lpOn;

    public float LowDb { get => _lowDb; set { _lowDb = value; _dirty = true; } }
    public float MidDb { get => _midDb; set { _midDb = value; _dirty = true; } }
    public float HighDb { get => _highDb; set { _highDb = value; _dirty = true; } }

    /// <summary>Inclinación extra en agudos (la usa el pad "brillo" del Intellipan).</summary>
    public float TiltDb { get => _tiltDb; set { _tiltDb = value; _dirty = true; } }

    /// <summary>Corte del pasa-altos. ≤ 21 Hz = desactivado.</summary>
    public float HighPassHz { get => _hpHz; set { _hpHz = value; _dirty = true; } }

    /// <summary>Corte del pasa-bajos. ≥ 19.9 kHz = desactivado.</summary>
    public float LowPassHz { get => _lpHz; set { _lpHz = value; _dirty = true; } }

    public void Process(float[] buffer, int samples) => Process(buffer, 0, samples);

    public void Process(float[] buffer, int offset, int samples)
    {
        if (_dirty) Rebuild();
        if (_flat && !_hpOn && !_lpOn) return;

        int end = offset + samples;
        for (int i = offset; i + 1 < end; i += 2)
        {
            for (int ch = 0; ch < 2; ch++)
            {
                float x = buffer[i + ch];
                if (_hpOn) x = _hp.Process(ch, x);
                if (!_flat)
                {
                    x = _low.Process(ch, x);
                    x = _mid.Process(ch, x);
                    x = _high.Process(ch, x);
                }
                if (_lpOn) x = _lp.Process(ch, x);
                buffer[i + ch] = x;
            }
        }
    }

    private void Rebuild()
    {
        _dirty = false; // si la UI cambia algo durante el rebuild, se vuelve a marcar
        double fs = AudioEngine.SampleRate;

        float high = _highDb + _tiltDb;
        _low.SetLowShelf(fs, 120, _lowDb);
        _mid.SetPeaking(fs, 1000, 0.8, _midDb);
        _high.SetHighShelf(fs, 8000, high);
        _flat = Math.Abs(_lowDb) < 0.05f && Math.Abs(_midDb) < 0.05f && Math.Abs(high) < 0.05f;

        bool hpOn = _hpHz > 21f;
        if (hpOn) _hp.SetHighPass(fs, _hpHz);
        else if (_hpOn) _hp.Reset();
        _hpOn = hpOn;

        bool lpOn = _lpHz < 19900f && _lpHz < fs * 0.45;
        if (lpOn) _lp.SetLowPass(fs, _lpHz);
        else if (_lpOn) _lp.Reset();
        _lpOn = lpOn;
    }
}
