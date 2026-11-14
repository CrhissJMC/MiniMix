using System.Runtime.InteropServices;

namespace MiniMixer.Audio.Interop;

// Definiciones COM mínimas para la captura por proceso (Process Loopback).
// NAudio no expone ActivateAudioInterfaceAsync con AUDIOCLIENT_ACTIVATION_PARAMS,
// así que se declara aquí directamente.

internal static class NativeMethods
{
    /// <summary>Dispositivo virtual que representa "el audio de un proceso".</summary>
    public const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams, // PROPVARIANT* (VT_BLOB → AUDIOCLIENT_ACTIVATION_PARAMS)
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);
}

internal static class AudioClientConstants
{
    public const int ShareModeShared = 0;

    public const uint StreamFlagsLoopback = 0x00020000;
    public const uint StreamFlagsEventCallback = 0x00040000;
    public const uint StreamFlagsAutoConvertPcm = 0x80000000;
    public const uint StreamFlagsSrcDefaultQuality = 0x08000000;

    public const uint BufferFlagsSilent = 0x2;

    public const int ActivationTypeProcessLoopback = 1;
    public const int LoopbackModeIncludeTargetProcessTree = 0;
    public const int LoopbackModeExcludeTargetProcessTree = 1;

    public const ushort VtBlob = 65;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientActivationParams
{
    public int ActivationType;      // AUDIOCLIENT_ACTIVATION_TYPE
    public uint TargetProcessId;    // AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS.TargetProcessId
    public int ProcessLoopbackMode; // PROCESS_LOOPBACK_MODE
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
}

/// <summary>Marca el handler como "agile" (requisito de ActivateAudioInterfaceAsync).</summary>
[ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject
{
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint bufferSize);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint numFramesRead);
    [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
}

/// <summary>Recibe el resultado de ActivateAudioInterfaceAsync en un hilo MTA del sistema.</summary>
internal sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    public TaskCompletionSource<IAudioClient> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
    {
        try
        {
            activateOperation.GetActivateResult(out int hr, out object iface);
            if (hr < 0) Completion.TrySetException(Marshal.GetExceptionForHR(hr) ?? new COMException("Activate", hr));
            else Completion.TrySetResult((IAudioClient)iface);
        }
        catch (Exception ex)
        {
            Completion.TrySetException(ex);
        }
    }
}
