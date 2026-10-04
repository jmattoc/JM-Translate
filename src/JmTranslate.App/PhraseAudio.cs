using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.Wave;
using SherpaOnnx;

namespace JmTranslate.App;

/// <summary>
/// Genera el audio de una frase preparada (texto → voz → [tu timbre] → nivelado) y lo guarda en data/cache.
/// La caché depende del texto, la voz base y si se usa tu timbre; si cambia algo, la frase se vuelve a preparar.
/// </summary>
internal sealed class PhraseAudio : IDisposable
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly MtSidecar _mt;
    private readonly Dictionary<string, OfflineTts> _tts = new();
    private readonly object _ttsLock = new();

    public PhraseAudio(MtSidecar mt) => _mt = mt;

    private static string CachePath(Phrase p, TtsSpec voice, bool clone)
    {
        var key = $"{p.En}|{voice.VoiceId}|{(clone ? "clone" : "plain")}|{Pronunciation.ForEnglish(p.En, voice.Kind)}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(Paths.Cache, $"{hash}.wav");
    }

    public bool IsReady(Phrase p, TtsSpec voice, bool clone) => File.Exists(CachePath(p, voice, clone));

    /// <summary>Devuelve el audio de la frase; si no está en caché, lo genera (puede tardar unos segundos).</summary>
    public async Task<(float[] Samples, int Rate)> GetAsync(Phrase p, TtsSpec voice, bool clone)
    {
        var path = CachePath(p, voice, clone);
        if (File.Exists(path)) return Read(path);

        if (clone) await _mt.StartAsync(CancellationToken.None);
        var (samples, rate) = await Task.Run(() => Render(p.En, voice, clone));
        samples = AudioLevel.Normalize(samples);
        Write(path, samples, rate);
        return (samples, rate);
    }

    private (float[] Samples, int Rate) Render(string text, TtsSpec voice, bool clone)
    {
        OfflineTts tts;
        lock (_ttsLock)
        {
            if (!_tts.TryGetValue(voice.VoiceId, out tts!))
                _tts[voice.VoiceId] = tts = TtsFactory.Create(voice);
        }

        var all = new List<float>();
        var rate = tts.SampleRate;
        foreach (var sentence in SentenceSplit.Split(text.Trim()).Where(s => s.Trim().Length > 0))
        {
            float[] samples;
            int sourceRate;
            lock (_ttsLock)
            {
                var audio = tts.Generate(Pronunciation.ForEnglish(sentence, voice.Kind), 1.0f, voice.Sid);
                samples = audio.Samples;
                sourceRate = audio.SampleRate;
            }
            if (clone) samples = _mt.CloneAsync(samples, sourceRate, voice.VoiceId).GetAwaiter().GetResult();
            all.AddRange(samples);
            all.AddRange(new float[(int)(rate * 0.25)]); // pausa breve entre oraciones
        }
        return (all.ToArray(), rate);
    }

    private static (float[] Samples, int Rate) Read(string path)
    {
        using var reader = new AudioFileReader(path);
        var samples = new float[reader.Length / sizeof(float)];
        var read = ((ISampleProvider)reader).Read(samples.AsSpan());
        return (samples[..read], reader.WaveFormat.SampleRate);
    }

    private static void Write(string path, float[] samples, int rate)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        writer.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        lock (_ttsLock)
        {
            foreach (var t in _tts.Values) t.Dispose();
            _tts.Clear();
        }
    }
}
