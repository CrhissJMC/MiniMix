using NAudio.Wave;

namespace MiniMixer.Audio;

internal static class SampleConversion
{
    private static readonly Guid IeeeFloatSubType = new("00000003-0000-0010-8000-00aa00389b71");

    public static bool IsFloat(WaveFormat f) =>
        f.Encoding == WaveFormatEncoding.IeeeFloat ||
        (f is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubType);

    /// <summary>Convierte bytes PCM/float de cualquier profundidad a float intercalado. Devuelve nº de muestras.</summary>
    public static int ToFloat(byte[] src, int bytes, WaveFormat fmt, ref float[] dest)
    {
        int bytesPerSample = fmt.BitsPerSample / 8;
        int n = bytes / bytesPerSample;
        if (dest.Length < n) dest = new float[n];

        if (bytesPerSample == 4 && IsFloat(fmt))
        {
            Buffer.BlockCopy(src, 0, dest, 0, n * 4);
            return n;
        }

        switch (fmt.BitsPerSample)
        {
            case 16:
                for (int i = 0; i < n; i++)
                    dest[i] = BitConverter.ToInt16(src, i * 2) / 32768f;
                break;
            case 24:
                for (int i = 0, j = 0; i < n; i++, j += 3)
                    dest[i] = (src[j] | (src[j + 1] << 8) | ((sbyte)src[j + 2] << 16)) / 8388608f;
                break;
            case 32: // PCM entero de 32 bits (o 24 bits válidos en contenedor de 32)
                for (int i = 0; i < n; i++)
                    dest[i] = BitConverter.ToInt32(src, i * 4) / 2147483648f;
                break;
            default:
                throw new NotSupportedException($"Formato no soportado: {fmt}");
        }
        return n;
    }
}

/// <summary>Adapta cualquier número de canales a estéreo (mono → duplicado, N canales → L/R frontales).</summary>
internal sealed class StereoAdapter : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _tmp = Array.Empty<float>();

    public StereoAdapter(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_channels == 2) return _source.Read(buffer, offset, count);

        int frames = count / 2;
        int need = frames * _channels;
        if (_tmp.Length < need) _tmp = new float[need];

        int got = _source.Read(_tmp, 0, need) / _channels;
        for (int f = 0; f < got; f++)
        {
            if (_channels == 1)
            {
                float v = _tmp[f];
                buffer[offset + 2 * f] = v;
                buffer[offset + 2 * f + 1] = v;
            }
            else
            {
                buffer[offset + 2 * f] = _tmp[f * _channels];
                buffer[offset + 2 * f + 1] = _tmp[f * _channels + 1];
            }
        }
        return got * 2;
    }
}
