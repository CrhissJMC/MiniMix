using System.Runtime.InteropServices;
using NAudio.Wave;
using static MiniMixer.Audio.Interop.AudioClientConstants;

namespace MiniMixer.Audio.Interop;

/// <summary>
/// Captura del audio de un proceso concreto (y su árbol de hijos) con la API
/// "Application Loopback" de Windows 10 2004+ (ActivateAudioInterfaceAsync + VAD\Process_Loopback).
///
/// Todo (activación, inicialización y lectura) corre en un hilo MTA dedicado
/// para no bloquear ni depender del STA de WPF.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    private const int BlockAlign = Channels * BitsPerSample / 8;

    /// <summary>El loopback por proceso no admite GetMixFormat: el formato lo elegimos nosotros.</summary>
    public static WaveFormat Format { get; } = new(SampleRate, BitsPerSample, Channels);

    private readonly uint _processId;
    private readonly bool _includeTree;
    private Thread? _thread;
    private volatile bool _stop;

    public ProcessLoopbackCapture(uint processId, bool includeProcessTree = true)
    {
        _processId = processId;
        _includeTree = includeProcessTree;
    }

    public event Action<byte[], int>? DataAvailable;
    public event Action<Exception>? Faulted;

    public void Start()
    {
        if (_thread != null) return;
        _stop = false;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = $"ProcessLoopback {_processId}",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Run()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr formatPtr = IntPtr.Zero;
        using var dataReady = new AutoResetEvent(false);

        try
        {
            client = Activate(_processId, _includeTree);

            var wfx = new WaveFormatEx
            {
                wFormatTag = 1, // WAVE_FORMAT_PCM
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = BitsPerSample,
                nBlockAlign = BlockAlign,
                nAvgBytesPerSec = SampleRate * BlockAlign,
                cbSize = 0
            };
            formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            Marshal.StructureToPtr(wfx, formatPtr, false);

            Check(client.Initialize(ShareModeShared,
                StreamFlagsLoopback | StreamFlagsEventCallback | StreamFlagsAutoConvertPcm | StreamFlagsSrcDefaultQuality,
                200_000, // 20 ms (unidades de 100 ns)
                0, formatPtr, IntPtr.Zero));
            Check(client.SetEventHandle(dataReady.SafeWaitHandle.DangerousGetHandle()));
            Check(client.GetService(typeof(IAudioCaptureClient).GUID, out object service));
            capture = (IAudioCaptureClient)service;
            Check(client.Start());

            var buffer = new byte[SampleRate / 10 * BlockAlign];
            while (!_stop)
            {
                if (!dataReady.WaitOne(200)) continue; // la app puede estar en silencio: no llegan paquetes

                while (!_stop)
                {
                    Check(capture.GetNextPacketSize(out uint packetFrames));
                    if (packetFrames == 0) break;

                    Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    int bytes = (int)frames * BlockAlign;
                    if (buffer.Length < bytes) buffer = new byte[bytes];

                    if ((flags & BufferFlagsSilent) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);

                    Check(capture.ReleaseBuffer(frames));
                    DataAvailable?.Invoke(buffer, bytes);
                }
            }

            client.Stop();
        }
        catch (Exception ex)
        {
            if (!_stop) Faulted?.Invoke(ex);
        }
        finally
        {
            if (formatPtr != IntPtr.Zero) Marshal.FreeHGlobal(formatPtr);
            if (capture != null) Marshal.ReleaseComObject(capture);
            if (client != null) Marshal.ReleaseComObject(client);
        }
    }

    private static IAudioClient Activate(uint processId, bool includeTree)
    {
        var activation = new AudioClientActivationParams
        {
            ActivationType = ActivationTypeProcessLoopback,
            TargetProcessId = processId,
            ProcessLoopbackMode = includeTree ? LoopbackModeIncludeTargetProcessTree : LoopbackModeExcludeTargetProcessTree
        };

        int paramsSize = Marshal.SizeOf<AudioClientActivationParams>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
        IntPtr propVariant = Marshal.AllocHGlobal(24); // sizeof(PROPVARIANT) en x64
        try
        {
            Marshal.StructureToPtr(activation, paramsPtr, false);

            // PROPVARIANT { VARTYPE vt = VT_BLOB; ...; BLOB { ULONG cbSize; BYTE* pBlobData; } }
            Marshal.Copy(new byte[24], 0, propVariant, 24);
            Marshal.WriteInt16(propVariant, 0, (short)VtBlob);
            Marshal.WriteInt32(propVariant, 8, paramsSize);
            Marshal.WriteIntPtr(propVariant, 16, paramsPtr);

            var handler = new ActivationCompletionHandler();
            NativeMethods.ActivateAudioInterfaceAsync(
                NativeMethods.VirtualAudioDeviceProcessLoopback,
                typeof(IAudioClient).GUID,
                propVariant,
                handler,
                out var operation);

            // Estamos en nuestro hilo MTA: bloquear aquí es seguro.
            var client = handler.Completion.Task.GetAwaiter().GetResult();
            GC.KeepAlive(operation);
            return client;
        }
        finally
        {
            Marshal.FreeHGlobal(propVariant);
            Marshal.FreeHGlobal(paramsPtr);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        _stop = true;
        var t = _thread;
        if (t != null && t != Thread.CurrentThread) t.Join(1000);
        _thread = null;
    }
}
