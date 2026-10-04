namespace JmTranslate.App;

/// <summary>
/// Nivela el volumen de la voz sintética antes de enviarla. Teams, Zoom y Meet aplican control automático de
/// ganancia y supresión de ruido; una voz muy baja o con picos muy distintos entre frases se recorta o se atenúa.
/// </summary>
internal static class AudioLevel
{
    private const double TargetRms = 0.12;   // ≈ -18 dBFS, nivel típico de una voz hablada
    private const double MaxPeak = 0.9;

    public static float[] Normalize(float[] samples)
    {
        if (samples.Length == 0) return samples;

        double sum = 0;
        float peak = 0;
        foreach (var s in samples)
        {
            sum += s * s;
            var a = MathF.Abs(s);
            if (a > peak) peak = a;
        }
        var rms = Math.Sqrt(sum / samples.Length);
        if (peak < 1e-4 || rms < 1e-5) return samples;

        var gain = Math.Clamp(Math.Min(MaxPeak / peak, TargetRms / rms), 0.5, 5.0);
        if (Math.Abs(gain - 1.0) < 0.03) return samples;

        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++) result[i] = (float)(samples[i] * gain);
        return result;
    }
}
