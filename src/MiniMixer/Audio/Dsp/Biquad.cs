namespace MiniMixer.Audio.Dsp;

/// <summary>
/// Filtro biquad estéreo (RBJ Audio EQ Cookbook), forma directa II transpuesta.
/// Los coeficientes se recalculan desde el hilo de audio; el estado es por canal.
/// </summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private readonly double[] _z1 = new double[2];
    private readonly double[] _z2 = new double[2];

    public void SetPeaking(double fs, double freq, double q, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * freq / fs;
        double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        Set(1 + alpha * a, -2 * cos, 1 - alpha * a,
            1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    public void SetLowShelf(double fs, double freq, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * freq / fs;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / 2 * Math.Sqrt(2); // pendiente S = 1
        double s = 2 * Math.Sqrt(a) * alpha;
        Set(a * ((a + 1) - (a - 1) * cos + s),
            2 * a * ((a - 1) - (a + 1) * cos),
            a * ((a + 1) - (a - 1) * cos - s),
            (a + 1) + (a - 1) * cos + s,
            -2 * ((a - 1) + (a + 1) * cos),
            (a + 1) + (a - 1) * cos - s);
    }

    public void SetHighShelf(double fs, double freq, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * freq / fs;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / 2 * Math.Sqrt(2);
        double s = 2 * Math.Sqrt(a) * alpha;
        Set(a * ((a + 1) + (a - 1) * cos + s),
            -2 * a * ((a - 1) + (a + 1) * cos),
            a * ((a + 1) + (a - 1) * cos - s),
            (a + 1) - (a - 1) * cos + s,
            2 * ((a - 1) - (a + 1) * cos),
            (a + 1) - (a - 1) * cos - s);
    }

    public void SetHighPass(double fs, double freq, double q = 0.7071)
    {
        double w0 = 2 * Math.PI * freq / fs;
        double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        Set((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void SetLowPass(double fs, double freq, double q = 0.7071)
    {
        double w0 = 2 * Math.PI * freq / fs;
        double cos = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q);
        Set((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
        _a1 = a1 / a0; _a2 = a2 / a0;
    }

    public void Reset()
    {
        Array.Clear(_z1);
        Array.Clear(_z2);
    }

    public float Process(int ch, float x)
    {
        double y = _b0 * x + _z1[ch];
        _z1[ch] = _b1 * x - _a1 * y + _z2[ch];
        _z2[ch] = _b2 * x - _a2 * y;
        return (float)y;
    }
}
