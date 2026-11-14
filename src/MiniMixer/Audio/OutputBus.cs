using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MiniMixer.Audio;

/// <summary>
/// Bus de salida físico (A1, A2, A3...). Cada bus tiene su propio WasapiOut en modo compartido
/// y una FIFO por cada canal que le envía audio. El hilo de WASAPI hace la mezcla final.
/// </summary>
public sealed class OutputBus : IDisposable
{
    private readonly int PrimeSamples = AudioEngine.SampleRate / 50 * 2;    // 20 ms de pre-buffer
    private static readonly TimeSpan MaxLatency = TimeSpan.FromMilliseconds(150);

    private sealed class BusInput
    {
        public required InputChannel Owner;
        public required BufferedWaveProvider Fifo;
        public required ISampleProvider Reader;
        public byte[] Bytes = new byte[16384];
        public bool Primed;
    }

    private readonly object _inputsLock = new();
    private volatile BusInput[] _inputs = Array.Empty<BusInput>(); // copy-on-write
    private WasapiOut? _out;
    private float[] _scratch = new float[4096];
    private float _gain = 1f;

    public OutputBus(string name) => Name = name;

    public string Name { get; }
    public string? DeviceId { get; private set; }
    public LevelMeter Meter { get; } = new();
    public float GainDb { get; set; }
    public bool Mute { get; set; }

    /// <summary>EQ + filtros del bus (mismo procesado que las entradas, aplicado a la mezcla final).</summary>
    public Dsp.Equalizer Eq { get; } = new();

    /// <summary>-1 = izquierda, 0 = centro, +1 = derecha.</summary>
    public float Pan { get; set; }

    /// <summary>Asigna el dispositivo físico del bus (null = desactivado).</summary>
    public void SetDevice(MMDevice? device)
    {
        if (_out != null)
        {
            _out.Stop();
            _out.Dispose();
            _out = null;
        }
        DeviceId = null;
        if (device == null) return;

        // Modo compartido + event-driven, ~30 ms. NAudio remuestrea si el formato de mezcla
        // del dispositivo no es 48 kHz float.
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
        output.Init(new BusMixer(this).ToWaveProvider());
        output.Play();
        _out = output;
        DeviceId = device.ID;
    }

    /// <summary>Llamado desde el hilo de captura de cada canal.</summary>
    internal void Write(InputChannel owner, float[] samples, int count)
    {
        var input = Find(owner) ?? Add(owner);

        // Deriva de reloj entre dispositivos: si la FIFO crece demasiado, se vacía (glitch corto).
        if (input.Fifo.BufferedDuration > MaxLatency) input.Fifo.ClearBuffer();

        int bytes = count * 4;
        if (input.Bytes.Length < bytes) input.Bytes = new byte[bytes];
        Buffer.BlockCopy(samples, 0, input.Bytes, 0, bytes);
        input.Fifo.AddSamples(input.Bytes, 0, bytes);
    }

    internal void Detach(InputChannel owner)
    {
        lock (_inputsLock)
        {
            if (Find(owner) == null) return;
            _inputs = _inputs.Where(i => i.Owner != owner).ToArray();
        }
    }

    private BusInput? Find(InputChannel owner)
    {
        foreach (var i in _inputs)
            if (i.Owner == owner) return i;
        return null;
    }

    private BusInput Add(InputChannel owner)
    {
        lock (_inputsLock)
        {
            var existing = Find(owner);
            if (existing != null) return existing;

            var fifo = new BufferedWaveProvider(AudioEngine.MixFormat)
            {
                ReadFully = true, // rellena con silencio en underrun
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(500)
            };
            var input = new BusInput { Owner = owner, Fifo = fifo, Reader = fifo.ToSampleProvider() };
            _inputs = _inputs.Append(input).ToArray();
            return input;
        }
    }

    /// <summary>Mezcla final. Se ejecuta en el hilo de render de WASAPI.</summary>
    internal int Mix(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        if (_scratch.Length < count) _scratch = new float[count];

        foreach (var input in _inputs)
        {
            if (!input.Primed)
            {
                if (input.Fifo.BufferedBytes < PrimeSamples * 4) continue;
                input.Primed = true;
            }

            int n = input.Reader.Read(_scratch, 0, count);
            for (int i = 0; i < n; i++) buffer[offset + i] += _scratch[i];

            if (input.Fifo.BufferedBytes == 0) input.Primed = false; // re-acumular tras underrun
        }

        // EQ / filtros / brillo del bus
        Eq.Process(buffer, offset, count);

        // Volumen (con rampa) + paneo (ley de balance) + limitador suave
        float target = Mute ? 0f : ChannelProcessor.DbToLin(GainDb);
        float pan = Math.Clamp(Pan, -1f, 1f);
        float pl = pan <= 0 ? 1f : 1f - pan;
        float pr = pan >= 0 ? 1f : 1f + pan;
        float g = _gain;
        int frames = count / 2;
        float step = frames > 0 ? (target - g) / frames : 0f;
        for (int f = 0; f < frames; f++)
        {
            g += step;
            int i = offset + 2 * f;
            buffer[i] = SoftLimit(buffer[i] * g * pl);
            buffer[i + 1] = SoftLimit(buffer[i + 1] * g * pr);
        }
        _gain = target;

        Meter.Process(buffer, offset, count);
        return count;
    }

    /// <summary>
    /// Limitador suave: lineal hasta 0.8 y curva tanh hasta 1.0. Evita el recorte duro
    /// (que suena a ruido/distorsión) cuando varias fuentes suman por encima de 0 dBFS.
    /// </summary>
    private static float SoftLimit(float x)
    {
        const float knee = 0.8f, room = 1f - knee;
        float a = MathF.Abs(x);
        if (a <= knee) return x;
        return MathF.CopySign(knee + room * MathF.Tanh((a - knee) / room), x);
    }

    public void Dispose() => SetDevice(null);

    private sealed class BusMixer : ISampleProvider
    {
        private readonly OutputBus _bus;
        public BusMixer(OutputBus bus) => _bus = bus;
        public WaveFormat WaveFormat => AudioEngine.MixFormat;
        public int Read(float[] buffer, int offset, int count) => _bus.Mix(buffer, offset, count);
    }
}
