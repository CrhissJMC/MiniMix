using NAudio.Wave;

namespace MiniMixer.Audio;

/// <summary>
/// Motor de mezcla. Formato interno fijo: 48 kHz, estéreo, float32.
///
///   [Fuentes] ──► InputChannel (DSP) ──┬──► A1 ──► WasapiOut (altavoces)
///                                      ├──► A2 ──► WasapiOut (auriculares)
///                                      ├──► A3 ──► WasapiOut (...)
///                                      └──► B1 ──► WasapiOut ("CABLE Input" → micrófono virtual MixMic)
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public static readonly int[] SupportedSampleRates = { 44100, 48000, 96000, 192000 };

    /// <summary>Frecuencia interna de mezcla. Se fija con <see cref="Configure"/> antes de crear el motor.</summary>
    public static int SampleRate { get; private set; } = 48000;
    public static WaveFormat MixFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>Cambia la frecuencia interna. Sólo es válido antes de crear canales y buses (al arrancar).</summary>
    public static void Configure(int sampleRate)
    {
        if (!SupportedSampleRates.Contains(sampleRate)) sampleRate = 48000;
        SampleRate = sampleRate;
        MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    /// <summary>Bus físico principal (A1) y bus del micrófono virtual (B1).</summary>
    public const int OutBus = 0;
    public const int MicBus = 3;

    private readonly List<InputChannel> _channels = new();
    private volatile bool _soloActive;

    public AudioEngine(params string[] busNames)
    {
        if (busNames.Length == 0) busNames = new[] { "A1", "A2", "A3", "B1" };
        Buses = busNames.Select(n => new OutputBus(n)).ToList();
    }

    public IReadOnlyList<OutputBus> Buses { get; }

    /// <summary>true si algún canal tiene Solo activo (los demás se silencian).</summary>
    public bool SoloActive => _soloActive;

    public InputChannel CreateChannel(string name)
    {
        var channel = new InputChannel(this, name);
        lock (_channels) _channels.Add(channel);
        return channel;
    }

    public void RemoveChannel(InputChannel channel)
    {
        lock (_channels) _channels.Remove(channel);
        channel.Dispose();
        UpdateSolo();
    }

    public void UpdateSolo()
    {
        lock (_channels) _soloActive = _channels.Any(c => c.Processor.Solo);
    }

    public void Dispose()
    {
        InputChannel[] channels;
        lock (_channels)
        {
            channels = _channels.ToArray();
            _channels.Clear();
        }
        foreach (var c in channels) c.Dispose();
        foreach (var b in Buses) b.Dispose();
    }
}
