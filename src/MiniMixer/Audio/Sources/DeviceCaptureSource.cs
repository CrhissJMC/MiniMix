using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MiniMixer.Audio.Sources;

/// <summary>
/// Captura WASAPI compartida de un dispositivo de entrada (micrófono, "CABLE Output"...)
/// o loopback de un dispositivo de salida (lo que suena por él).
/// </summary>
public sealed class DeviceCaptureSource : IAudioSource
{
    private readonly WasapiCapture _capture; // WasapiLoopbackCapture hereda de WasapiCapture

    private DeviceCaptureSource(WasapiCapture capture, string name)
    {
        _capture = capture;
        Name = name;
        _capture.DataAvailable += (_, e) => DataAvailable?.Invoke(this, e.Buffer, e.BytesRecorded);
    }

    public static DeviceCaptureSource Capture(MMDevice device) =>
        new(new WasapiCapture(device, false, 20), device.FriendlyName);

    public static DeviceCaptureSource Loopback(MMDevice device) =>
        new(new WasapiLoopbackCapture(device), "Loopback · " + device.FriendlyName);

    public string Name { get; }
    public WaveFormat WaveFormat => _capture.WaveFormat;
    public event Action<IAudioSource, byte[], int>? DataAvailable;

    public void Start() => _capture.StartRecording();

    public void Dispose()
    {
        try { _capture.StopRecording(); } catch { /* ya detenido */ }
        _capture.Dispose(); // espera a que termine el hilo de captura
    }
}
