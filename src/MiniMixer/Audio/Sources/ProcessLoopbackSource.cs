using MiniMixer.Audio.Interop;
using NAudio.Wave;

namespace MiniMixer.Audio.Sources;

/// <summary>
/// Captura el audio de UNA aplicación (y sus procesos hijos) mediante
/// el Process Loopback de Windows 10 2004+ (build 19041).
/// </summary>
public sealed class ProcessLoopbackSource : IAudioSource
{
    private readonly ProcessLoopbackCapture _capture;

    public ProcessLoopbackSource(uint processId, string name)
    {
        Name = name;
        _capture = new ProcessLoopbackCapture(processId, includeProcessTree: true);
        _capture.DataAvailable += (buffer, bytes) => DataAvailable?.Invoke(this, buffer, bytes);
        _capture.Faulted += ex => Faulted?.Invoke(ex);
    }

    public string Name { get; }
    public WaveFormat WaveFormat => ProcessLoopbackCapture.Format;
    public event Action<IAudioSource, byte[], int>? DataAvailable;
    public event Action<Exception>? Faulted;

    public void Start() => _capture.Start();
    public void Dispose() => _capture.Dispose();
}
