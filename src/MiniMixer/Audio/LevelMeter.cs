namespace MiniMixer.Audio;

public readonly record struct MeterReading(float PeakL, float PeakR, float RmsL, float RmsR);

/// <summary>
/// Acumula pico y RMS desde el hilo de audio; la UI lo lee (y reinicia) ~30 veces por segundo.
/// </summary>
public sealed class LevelMeter
{
    private readonly object _lock = new();
    private float _peakL, _peakR;
    private double _sumL, _sumR;
    private int _frames;

    /// <summary>Procesa audio estéreo intercalado.</summary>
    public void Process(float[] buffer, int offset, int samples)
    {
        float pl = 0, pr = 0;
        double sl = 0, sr = 0;
        int end = offset + samples - 1;
        for (int i = offset; i < end; i += 2)
        {
            float l = buffer[i], r = buffer[i + 1];
            float al = Math.Abs(l), ar = Math.Abs(r);
            if (al > pl) pl = al;
            if (ar > pr) pr = ar;
            sl += l * l;
            sr += r * r;
        }

        lock (_lock)
        {
            if (pl > _peakL) _peakL = pl;
            if (pr > _peakR) _peakR = pr;
            _sumL += sl;
            _sumR += sr;
            _frames += samples / 2;
        }
    }

    public MeterReading Read()
    {
        lock (_lock)
        {
            var reading = _frames == 0
                ? new MeterReading(_peakL, _peakR, 0, 0)
                : new MeterReading(_peakL, _peakR,
                    (float)Math.Sqrt(_sumL / _frames), (float)Math.Sqrt(_sumR / _frames));
            _peakL = _peakR = 0;
            _sumL = _sumR = 0;
            _frames = 0;
            return reading;
        }
    }
}
