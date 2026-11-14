using NAudio.Wave;

namespace MiniMixer.Audio.Sources;

/// <summary>
/// Fuente de audio "push": entrega bloques de bytes en su formato nativo desde su propio hilo.
/// El buffer sólo es válido durante la llamada al evento.
/// </summary>
public interface IAudioSource : IDisposable
{
    string Name { get; }
    WaveFormat WaveFormat { get; }
    event Action<IAudioSource, byte[], int>? DataAvailable;
    void Start();
}
