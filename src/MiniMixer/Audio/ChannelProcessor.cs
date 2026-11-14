using MiniMixer.Audio.Dsp;

namespace MiniMixer.Audio;

/// <summary>
/// Cadena DSP de un canal: Gate → Denoiser (expansor) → EQ → Compresor → Ganancia/Paneo → Medidor.
/// Todo en float estéreo intercalado a 48 kHz. Sin asignaciones en el hilo de audio.
/// </summary>
public sealed class ChannelProcessor
{
    private readonly float Fs = AudioEngine.SampleRate;

    public ChannelProcessor()
    {
        EnvRelease = Coef(0.050f);      // 50 ms
        GateOpen = 1f - Coef(0.001f);   // 1 ms
        GateClose = 1f - Coef(0.080f);  // 80 ms
        ExpSmooth = 1f - Coef(0.010f);  // 10 ms
        CompAttack = 1f - Coef(0.005f);
        CompRelease = 1f - Coef(0.150f);
    }

    public Equalizer Eq { get; } = new();
    public LevelMeter Meter { get; } = new();

    // Parámetros (escritos por la UI, leídos una vez por bloque por el hilo de audio)
    public float GainDb { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    /// <summary>-1 = izquierda, 0 = centro, +1 = derecha.</summary>
    public float Pan { get; set; }
    /// <summary>Knobs 0..10 al estilo Voicemeeter. 0 = desactivado.</summary>
    public float GateAmount { get; set; }
    public float DenoiserAmount { get; set; }
    public float CompAmount { get; set; }

    // Estado interno del hilo de audio
    private float _gain;          // ganancia lineal actual (con rampa anti-zipper)
    private float _env;           // envolvente para gate/expansor
    private float _gateGain = 1f;
    private float _expGain = 1f;
    private float _compEnv;

    // Coeficientes de envolvente: dependen de la frecuencia de muestreo configurada
    private readonly float EnvRelease, GateOpen, GateClose, ExpSmooth, CompAttack, CompRelease;

    private float Coef(float seconds) => MathF.Exp(-1f / (seconds * Fs));
    public static float DbToLin(float db) => db <= -59.9f ? 0f : MathF.Pow(10f, db / 20f);

    public void Process(float[] buf, int samples, bool soloActive)
    {
        int frames = samples / 2;

        float gate = GateAmount, dn = DenoiserAmount, comp = CompAmount;
        if (gate > 0.01f || dn > 0.01f) ApplyGateAndExpander(buf, frames, gate, dn);

        Eq.Process(buf, samples);

        if (comp > 0.01f) ApplyCompressor(buf, frames, comp);

        // Ganancia + paneo (ley de balance: el centro no atenúa)
        float target = Mute || (soloActive && !Solo) ? 0f : DbToLin(GainDb);
        float pan = Math.Clamp(Pan, -1f, 1f);
        float pl = pan <= 0 ? 1f : 1f - pan;
        float pr = pan >= 0 ? 1f : 1f + pan;

        float g = _gain;
        float step = frames > 0 ? (target - g) / frames : 0f;
        for (int f = 0; f < frames; f++)
        {
            g += step;
            buf[2 * f] *= g * pl;
            buf[2 * f + 1] *= g * pr;
        }
        _gain = target;

        Meter.Process(buf, 0, frames * 2);
    }

    private void ApplyGateAndExpander(float[] buf, int frames, float gateAmt, float dnAmt)
    {
        // Gate: knob 0..10 → umbral -75..-30 dBFS. Denoiser: expansor 1:3 con umbral -70..-35 dBFS.
        float gateThr = gateAmt > 0.01f ? DbToLin(-75f + gateAmt * 4.5f) : 0f;
        float dnThr = dnAmt > 0.01f ? DbToLin(-70f + dnAmt * 3.5f) : 0f;

        for (int f = 0; f < frames; f++)
        {
            float l = buf[2 * f], r = buf[2 * f + 1];
            float x = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            _env = x > _env ? x : _env * EnvRelease;

            float gTarget = _env >= gateThr ? 1f : 0f;
            _gateGain += (gTarget - _gateGain) * (gTarget > _gateGain ? GateOpen : GateClose);

            float eTarget = 1f;
            if (dnThr > 0f && _env < dnThr)
            {
                float ratio = _env / dnThr;
                eTarget = ratio * ratio; // (env/thr)^(R-1) con R = 3
            }
            _expGain += (eTarget - _expGain) * ExpSmooth;

            float k = _gateGain * _expGain;
            buf[2 * f] = l * k;
            buf[2 * f + 1] = r * k;
        }
    }

    private void ApplyCompressor(float[] buf, int frames, float amt)
    {
        // Knob 0..10 → umbral 0..-30 dB, ratio 1..4, make-up automático (mitad de la reducción)
        float thrDb = -amt * 3f;
        float ratio = 1f + amt * 0.3f;
        float slope = 1f - 1f / ratio;
        float makeupDb = -thrDb * slope * 0.5f;

        for (int f = 0; f < frames; f++)
        {
            float l = buf[2 * f], r = buf[2 * f + 1];
            float x = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            _compEnv += (x - _compEnv) * (x > _compEnv ? CompAttack : CompRelease);

            float envDb = 20f * MathF.Log10(_compEnv + 1e-9f);
            float over = envDb - thrDb;
            float grDb = over > 0 ? -over * slope : 0f;
            float k = MathF.Pow(10f, (grDb + makeupDb) / 20f);

            buf[2 * f] = l * k;
            buf[2 * f + 1] = r * k;
        }
    }
}
