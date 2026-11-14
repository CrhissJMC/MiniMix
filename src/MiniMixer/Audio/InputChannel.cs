using MiniMixer.Audio.Sources;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MiniMixer.Audio;

/// <summary>
/// Un canal de entrada del mezclador (micrófono, entrada virtual o aplicación).
///
/// Flujo (en el hilo de captura de la fuente, modelo "push"):
///   bytes nativos → float → FIFO → estéreo → remuestreo a 48 kHz → DSP (una sola vez)
///   → se copia a la FIFO de cada bus A1..An al que está ruteado.
///
/// Cada bus (WASAPI out) consume su FIFO con su propio reloj, así un canal
/// puede alimentar varias salidas físicas a la vez.
/// </summary>
public sealed class InputChannel : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly bool[] _routes;
    private readonly object _sync = new();

    private IAudioSource? _source;
    private BufferedWaveProvider? _fifo;
    private ISampleProvider? _chain;

    private float[] _converted = new float[4096];
    private byte[] _bytes = new byte[16384];
    private readonly float[] _work = new float[AudioEngine.SampleRate / 10 * 2]; // 100 ms estéreo

    internal InputChannel(AudioEngine engine, string name)
    {
        _engine = engine;
        Name = name;
        _routes = new bool[engine.Buses.Count];
    }

    public string Name { get; }
    public ChannelProcessor Processor { get; } = new();
    public string? SourceName => _source?.Name;

    public bool GetRoute(int bus) => _routes[bus];

    public void SetRoute(int bus, bool enabled)
    {
        _routes[bus] = enabled;
        if (!enabled) _engine.Buses[bus].Detach(this);
    }

    /// <summary>Cambia la fuente del canal. null = sin fuente.</summary>
    public void SetSource(IAudioSource? source)
    {
        IAudioSource? old;
        lock (_sync)
        {
            old = _source;
            if (old != null) old.DataAvailable -= OnData;

            _source = source;
            _fifo = null;
            _chain = null;

            if (source != null)
            {
                BuildChain(source.WaveFormat);
                source.DataAvailable += OnData;
            }
        }

        // Fuera del lock: Dispose() espera al hilo de captura, que podría estar esperando este lock.
        old?.Dispose();
        foreach (var bus in _engine.Buses) bus.Detach(this);

        if (source == null) return;
        try
        {
            source.Start();
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_source, source))
                {
                    source.DataAvailable -= OnData;
                    _source = null; _fifo = null; _chain = null;
                }
            }
            source.Dispose();
            throw;
        }
    }

    private void BuildChain(WaveFormat native)
    {
        _fifo = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(native.SampleRate, native.Channels))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1)
        };

        ISampleProvider chain = new StereoAdapter(_fifo.ToSampleProvider());
        if (native.SampleRate != AudioEngine.SampleRate)
            chain = new WdlResamplingSampleProvider(chain, AudioEngine.SampleRate);
        _chain = chain;
    }

    private void OnData(IAudioSource sender, byte[] buffer, int bytes)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _source) || _fifo == null || _chain == null) return;

            int n = SampleConversion.ToFloat(buffer, bytes, sender.WaveFormat, ref _converted);
            int byteCount = n * 4;
            if (_bytes.Length < byteCount) _bytes = new byte[byteCount];
            Buffer.BlockCopy(_converted, 0, _bytes, 0, byteCount);
            _fifo.AddSamples(_bytes, 0, byteCount);

            bool solo = _engine.SoloActive;
            int read;
            while ((read = _chain.Read(_work, 0, _work.Length)) > 0)
            {
                Processor.Process(_work, read, solo);
                for (int b = 0; b < _routes.Length; b++)
                    if (_routes[b]) _engine.Buses[b].Write(this, _work, read);
            }
        }
    }

    public void Dispose()
    {
        SetSource(null);
        foreach (var bus in _engine.Buses) bus.Detach(this);
    }
}
