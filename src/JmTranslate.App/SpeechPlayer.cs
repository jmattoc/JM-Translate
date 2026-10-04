using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace JmTranslate.App;

/// <summary>Reproduce voz sintetizada en un dispositivo de salida concreto, encolando frases.</summary>
internal sealed class SpeechPlayer : IDisposable
{
    private readonly WasapiOut _out;
    private readonly BufferedWaveProvider _buffer;

    public SpeechPlayer(MMDevice device, int sourceRate)
    {
        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 1), TimeSpan.FromSeconds(60))
        {
            DiscardOnBufferOverflow = true,
        };
        var mixRate = device.AudioClient.MixFormat.SampleRate;
        var resampled = new WdlResamplingSampleProvider(_buffer.ToSampleProvider(), mixRate);
        _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        _out.Init(new MonoToStereoSampleProvider(resampled).ToWaveProvider());
        _out.Play();
    }

    /// <summary>Segundos de voz pendientes de reproducir.</summary>
    public double PendingSeconds => _buffer.BufferedDuration.TotalSeconds;

    public void Enqueue(float[] samples)
    {
        samples = AudioLevel.Normalize(samples);
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        _buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Clear() => _buffer.ClearBuffer();

    public void Dispose()
    {
        _out.Stop();
        _out.Dispose();
    }
}
