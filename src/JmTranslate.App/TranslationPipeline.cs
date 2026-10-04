using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;

namespace JmTranslate.App;

/// <param name="Timings">Desglose por etapa, para mostrar dónde se va el tiempo.</param>
public sealed record Utterance(string Original, string Translation, double LatencySeconds, string Timings);

/// <summary>Estado del pipeline de salida, para decirle al usuario cuándo puede seguir hablando.</summary>
public enum Activity { Ready, Hearing, Processing, Speaking }

/// <param name="Source">Dispositivo de origen: de salida (loopback) o un micrófono.</param>
/// <param name="SourceIsLoopback">true = capturar lo que suena en ese dispositivo.</param>
/// <param name="MtPair">Par de traducción: "en-es" o "es-en".</param>
/// <param name="Tts">Voz sintética del idioma destino.</param>
/// <param name="Outputs">Dispositivos por donde sale la voz traducida (vacío = solo subtítulos).</param>
/// <param name="CloneVoice">Se consulta en cada frase: true = pasar la voz por el timbre de mi-voz.</param>
/// <param name="PushToTalk">Si existe, solo se captura mientras devuelva true; al pasar a false se envía de inmediato lo dicho.</param>
/// <param name="Command">Si existe, recibe cada frase reconocida; si devuelve true era un comando de voz y no se traduce.</param>
internal sealed record PipelineOptions(
    MMDevice Source,
    bool SourceIsLoopback,
    string MtPair,
    TtsSpec Tts,
    IReadOnlyList<MMDevice> Outputs,
    Func<bool>? CloneVoice = null,
    Func<bool>? PushToTalk = null,
    Func<string, bool>? Command = null);

/// <summary>
/// Pipeline genérico: audio → VAD → tramos → voz→texto → traducción → [subtítulos] → síntesis → [mi voz] → salida.
/// Dos etapas en hilos separados (voz→texto+traducción, y síntesis+timbre) para que se solapen y la voz salga más seguida.
/// Se usa en ambas direcciones (reunión→yo con loopback, yo→reunión con micrófono). Todo en memoria.
/// </summary>
internal sealed class TranslationPipeline : IDisposable
{
    private const int VadRate = 16000;
    private const int Threads = 6;

    // Corte de frases: el VAD marca pausas desde 0.2 s; un tramo se envía cuando ya es lo bastante largo
    // para traducirse con sentido, o cuando la pausa es larga (fin de frase).
    private const float VadMinSilence = 0.2f;
    private const double MinChunkSeconds = 2.5;
    private const double EndpointSeconds = 0.5;
    private const int GapFillSamples = (int)(0.2 * VadRate);

    private readonly MtSidecar _mt;
    private readonly PipelineOptions _opt;

    private readonly OfflineRecognizer _stt;
    private readonly OfflineTts _tts;
    private readonly VoiceActivityDetector _vad;
    private readonly List<SpeechPlayer> _players = new();
    private readonly bool _echoRisk;

    private readonly IWaveIn _capture;
    private readonly WaveFormat _format;
    private readonly BufferedWaveProvider _monoBuffer;
    private readonly ISampleProvider _resampler;

    private sealed record Pending(string Translated, long Ticks, long SttMs, long MtMs);

    private readonly BlockingCollection<(float[] Samples, long Ticks)> _segments = new(boundedCapacity: 8);
    private readonly BlockingCollection<Pending> _synth = new(boundedCapacity: 8);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _recognizeThread;
    private Thread? _speakThread;
    private Timer? _idleTimer;

    // Estado del corte de frases; todo se toca bajo _vadLock.
    private readonly object _vadLock = new();
    private readonly List<float> _acc = new();
    private long _lastSegTicks;
    private long _lastPacketTicks = Stopwatch.GetTimestamp();
    private bool _disposed;
    private DateTime _gateUntil = DateTime.MinValue;

    public event Action<Utterance>? Heard;
    public event Action<string>? Status;

    public TranslationPipeline(MtSidecar mt, PipelineOptions opt)
    {
        _mt = mt;
        _opt = opt;
        // Si la voz sale por el mismo dispositivo que se captura, el loopback se oiría a sí mismo.
        _echoRisk = opt.SourceIsLoopback && opt.Outputs.Any(o => o.ID == opt.Source.ID);

        // Parakeet-TDT v3: multilingüe (detecta es/en solo), más rápido y más preciso que Whisper small en CPU.
        var w = Path.Combine(Paths.Models, "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8");
        var sttCfg = new OfflineRecognizerConfig();
        sttCfg.ModelConfig.Transducer.Encoder = Path.Combine(w, "encoder.int8.onnx");
        sttCfg.ModelConfig.Transducer.Decoder = Path.Combine(w, "decoder.int8.onnx");
        sttCfg.ModelConfig.Transducer.Joiner = Path.Combine(w, "joiner.int8.onnx");
        sttCfg.ModelConfig.Tokens = Path.Combine(w, "tokens.txt");
        sttCfg.ModelConfig.ModelType = "nemo_transducer";
        sttCfg.ModelConfig.NumThreads = Threads;
        sttCfg.ModelConfig.Provider = "cpu";
        _stt = new OfflineRecognizer(sttCfg);

        _tts = TtsFactory.Create(opt.Tts);

        var vadCfg = new VadModelConfig();
        vadCfg.SileroVad.Model = Path.Combine(Paths.Models, "silero_vad.onnx");
        vadCfg.SileroVad.Threshold = 0.5f;
        vadCfg.SileroVad.MinSilenceDuration = VadMinSilence;
        vadCfg.SileroVad.MinSpeechDuration = 0.25f;
        vadCfg.SileroVad.MaxSpeechDuration = 8f;
        vadCfg.SileroVad.WindowSize = 512;
        vadCfg.SampleRate = VadRate;
        vadCfg.NumThreads = 1;
        vadCfg.Provider = "cpu";
        _vad = new VoiceActivityDetector(vadCfg, 60);

        foreach (var device in opt.Outputs) _players.Add(new SpeechPlayer(device, _tts.SampleRate));

        _capture = opt.SourceIsLoopback ? new WasapiLoopbackCapture(opt.Source) : new WasapiCapture(opt.Source);
        _format = _capture.WaveFormat;
        _monoBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(_format.SampleRate, 1), TimeSpan.FromSeconds(10))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true,
        };
        _resampler = new WdlResamplingSampleProvider(_monoBuffer.ToSampleProvider(), VadRate);
        _capture.DataAvailable += OnAudio;
    }

    public void Start()
    {
        _recognizeThread = new Thread(RecognizeLoop) { IsBackground = true, Name = "stt-mt" };
        _speakThread = new Thread(SpeakLoop) { IsBackground = true, Name = "tts-clone" };
        _recognizeThread.Start();
        _speakThread.Start();
        _capture.StartRecording();
        // El loopback no entrega datos cuando no suena nada; este temporizador "rellena" el silencio
        // para que el VAD cierre la última frase y se envíe el tramo pendiente.
        _idleTimer = new Timer(_ => OnTick(), null, 60, 60);
        Status?.Invoke("Escuchando…");
    }

    private bool SpeakingNow => _players.Any(p => p.PendingSeconds > 0);

    // Estado para el indicador de la interfaz ("¿ya puedo hablar?").
    private int _inFlight;            // tramos que ya salieron del micrófono y aún no terminan de prepararse
    private volatile bool _hearing;   // hay voz entrando o un tramo acumulándose

    /// <summary>Qué está pasando ahora y, si la voz está sonando, cuántos segundos quedan.</summary>
    public (Activity State, double SecondsLeft) Snapshot()
    {
        var left = _players.Count == 0 ? 0 : _players.Max(p => p.PendingSeconds);
        if (left > 0.05) return (Activity.Speaking, left);
        if (Volatile.Read(ref _inFlight) > 0) return (Activity.Processing, 0);
        return (_hearing ? Activity.Hearing : Activity.Ready, 0);
    }

    /// <summary>Silencio de emergencia: mientras esté activo no se sintetiza ni se envía nada.</summary>
    public bool Muted { get; set; }

    /// <summary>Corta ahora mismo la voz que esté sonando o en cola.</summary>
    public void ClearPlayback()
    {
        foreach (var p in _players) p.Clear();
    }

    private bool _pttWasHeld;

    private void OnAudio(object? sender, WaveInEventArgs e)
    {
        // Pulsar para hablar: fuera de la tecla no se captura nada.
        if (_opt.PushToTalk is { } held && !held()) return;

        // Mientras suena nuestra propia voz por el mismo dispositivo, se ignora la captura.
        if (_echoRisk)
        {
            if (SpeakingNow) _gateUntil = DateTime.UtcNow.AddMilliseconds(400);
            if (DateTime.UtcNow < _gateUntil) return;
        }

        var mono = ToMonoFloat(e.Buffer, e.BytesRecorded);
        if (mono.Length == 0) return;

        var bytes = new byte[mono.Length * 4];
        Buffer.BlockCopy(mono, 0, bytes, 0, bytes.Length);
        _monoBuffer.AddSamples(bytes, 0, bytes.Length);

        var buf = new float[VadRate / 2];
        int n;
        while ((n = _resampler.Read(buf)) > 0)
        {
            lock (_vadLock)
            {
                if (_disposed) return;
                _lastPacketTicks = Stopwatch.GetTimestamp();
                _vad.AcceptWaveform(n == buf.Length ? buf : buf[..n]);
                DrainVadLocked();
                CheckFlushLocked();
            }
        }
    }

    private void OnTick()
    {
        lock (_vadLock)
        {
            if (_disposed) return;
            if (Stopwatch.GetElapsedTime(_lastPacketTicks).TotalMilliseconds > 250)
            {
                _vad.AcceptWaveform(new float[VadRate / 20]); // 50 ms de silencio
                DrainVadLocked();
            }
            CheckFlushLocked();

            // Al soltar la tecla de "pulsar para hablar" se envía de inmediato lo dicho, sin esperar pausas.
            if (_opt.PushToTalk is { } held)
            {
                var down = held();
                if (_pttWasHeld && !down)
                {
                    _vad.Flush();
                    DrainVadLocked();
                    CheckFlushLocked(force: true);
                }
                _pttWasHeld = down;
            }
        }
    }

    private void DrainVadLocked()
    {
        while (!_vad.IsEmpty())
        {
            var seg = _vad.Front();
            _vad.Pop();
            if (_acc.Count > 0) _acc.AddRange(new float[GapFillSamples]);
            _acc.AddRange(seg.Samples);
            _lastSegTicks = Stopwatch.GetTimestamp();
        }
    }

    private void CheckFlushLocked(bool force = false)
    {
        _hearing = _acc.Count > 0 || _vad.IsSpeechDetected();
        if (_acc.Count == 0) return;
        var accSeconds = _acc.Count / (double)VadRate;
        var silence = VadMinSilence + Stopwatch.GetElapsedTime(_lastSegTicks).TotalSeconds;
        if (force && accSeconds < 0.3) { _acc.Clear(); return; } // un toque accidental de la tecla
        if (!force && accSeconds < MinChunkSeconds && silence < EndpointSeconds) return;

        var chunk = _acc.ToArray();
        _acc.Clear();
        Interlocked.Increment(ref _inFlight);
        if (!_segments.TryAdd((chunk, _lastSegTicks)))
        {
            Interlocked.Decrement(ref _inFlight);
            Status?.Invoke("Atrasado: se descartó un tramo.");
        }
    }

    /// <summary>Convierte el buffer capturado (float32 o PCM de 16 bits, N canales) a mono float.</summary>
    private float[] ToMonoFloat(byte[] buffer, int count)
    {
        var channels = _format.Channels;
        var bytesPerSample = _format.BitsPerSample / 8;
        var frames = count / (bytesPerSample * channels);
        var mono = new float[frames];
        var isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat
                      || (_format is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        var span = new ReadOnlySpan<byte>(buffer, 0, count);

        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var s = span.Slice((i * channels + c) * bytesPerSample, bytesPerSample);
                sum += isFloat && bytesPerSample == 4 ? BitConverter.ToSingle(s)
                     : bytesPerSample == 2 ? BitConverter.ToInt16(s) / 32768f
                     : bytesPerSample == 4 ? BitConverter.ToInt32(s) / 2147483648f
                     : 0f;
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    /// <summary>Etapa 1: voz → texto → traducción. Publica los subtítulos en cuanto están listos.</summary>
    private void RecognizeLoop()
    {
        try
        {
            foreach (var (samples, ticks) in _segments.GetConsumingEnumerable(_cts.Token))
            {
                var forwarded = false; // si pasa a la etapa 2, ella marca el fin del trabajo pendiente
                try
                {
                    var queuedMs = (long)Stopwatch.GetElapsedTime(ticks).TotalMilliseconds;

                    var sttStart = Stopwatch.GetTimestamp();
                    using var stream = _stt.CreateStream();
                    stream.AcceptWaveform(VadRate, samples);
                    _stt.Decode(stream);
                    var original = TextCleanup.Clean(stream.Result.Text.Trim());
                    if (original.Length < 2) continue;
                    if (_opt.Command?.Invoke(original) == true) continue; // comando de voz («banco, saludo»): no se traduce
                    var sttMs = (long)Stopwatch.GetElapsedTime(sttStart).TotalMilliseconds;

                    var mtStart = Stopwatch.GetTimestamp();
                    var translated = _mt.TranslateAsync(_opt.MtPair, original).GetAwaiter().GetResult();
                    var mtMs = (long)Stopwatch.GetElapsedTime(mtStart).TotalMilliseconds;

                    Heard?.Invoke(new Utterance(original, translated, Stopwatch.GetElapsedTime(ticks).TotalSeconds,
                        $"espera {queuedMs} ms, voz→texto {sttMs} ms, traducción {mtMs} ms"));

                    if (_players.Count > 0)
                    {
                        _synth.Add(new Pending(translated, ticks, sttMs, mtMs), _cts.Token);
                        forwarded = true;
                    }
                }
                finally
                {
                    if (!forwarded) Interlocked.Decrement(ref _inFlight);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status?.Invoke("Error: " + ex.Message);
        }
    }

    /// <summary>Parte el texto en cláusulas (puntuación) de al menos ~25 caracteres para sintetizarlas por separado.</summary>
    private static List<string> SplitForSpeech(string text)
    {
        var pieces = new List<string>();
        var buffer = "";
        foreach (var part in System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?;:,])\s+"))
        {
            if (part.Trim().Length == 0) continue;
            buffer = buffer.Length == 0 ? part : buffer + " " + part;
            if (buffer.Length >= 25) { pieces.AddRange(SplitLong(buffer)); buffer = ""; }
        }
        if (buffer.Length > 0)
        {
            if (pieces.Count > 0 && buffer.Length < 12) pieces[^1] += " " + buffer;
            else pieces.AddRange(SplitLong(buffer));
        }
        return pieces;
    }

    private static readonly HashSet<string> BreakWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "with", "that", "which", "for", "to", "but", "because", "so", "in", "on", "when", "where", "while",
        "y", "con", "que", "para", "pero", "porque", "en", "cuando", "donde", "mientras", "como", "por",
    };

    /// <summary>Una cláusula sin puntuación de más de ~55 caracteres se parte antes de una palabra de enlace, para que empiece a sonar antes.</summary>
    private static IEnumerable<string> SplitLong(string clause)
    {
        const int MaxLen = 55, MinLen = 20;
        while (clause.Length > MaxLen)
        {
            var words = clause.Split(' ');
            var cut = -1;
            var pos = 0;
            for (var i = 0; i < words.Length; i++)
            {
                if (i > 0 && pos >= MinLen && pos <= MaxLen && BreakWords.Contains(words[i].Trim(',', '.'))) cut = pos;
                pos += words[i].Length + 1;
            }
            if (cut < 0) cut = clause.LastIndexOf(' ', Math.Min(clause.Length - 1, MaxLen - 10));
            if (cut < MinLen) break;
            yield return clause[..cut].Trim();
            clause = clause[cut..].Trim();
        }
        if (clause.Length > 0) yield return clause;
    }

    /// <summary>Etapa 2: síntesis de voz → (mi voz) → salida. Corre en paralelo con la etapa 1 del tramo siguiente.</summary>
    private void SpeakLoop()
    {
        try
        {
            foreach (var item in _synth.GetConsumingEnumerable(_cts.Token))
            {
                // Se sintetiza y reproduce cláusula por cláusula: la primera suena en cuanto está lista y las
                // siguientes se preparan mientras tanto, así no se espera a la frase completa ni quedan silencios largos.
                long ttsMs = 0, cloneMs = 0;
                double? firstAudioAt = null;
                var targetIsEnglish = _opt.MtPair == "es-en";
                foreach (var piece in SplitForSpeech(item.Translated))
                {
                    if (Muted) break;
                    var ttsStart = Stopwatch.GetTimestamp();
                    // Las siglas se "deletrean" para el motor de voz; los subtítulos conservan el texto original.
                    var audio = _tts.Generate(targetIsEnglish ? Pronunciation.ForEnglish(piece, _opt.Tts.Kind) : piece, 1.0f, _opt.Tts.Sid);
                    ttsMs += (long)Stopwatch.GetElapsedTime(ttsStart).TotalMilliseconds;
                    var voice = audio.Samples;

                    if (_opt.CloneVoice?.Invoke() == true)
                    {
                        var cloneStart = Stopwatch.GetTimestamp();
                        try { voice = _mt.CloneAsync(voice, audio.SampleRate, _opt.Tts.VoiceId).GetAwaiter().GetResult(); }
                        catch (Exception ex) { Status?.Invoke("No se pudo usar mi voz, se usa la voz estándar: " + ex.Message); }
                        cloneMs += (long)Stopwatch.GetElapsedTime(cloneStart).TotalMilliseconds;
                    }
                    if (Muted) break;
                    foreach (var p in _players) p.Enqueue(voice);
                    firstAudioAt ??= Stopwatch.GetElapsedTime(item.Ticks).TotalSeconds;
                }

                if (firstAudioAt is not null)
                    Status?.Invoke($"La voz empieza a los {firstAudioAt:F1} s " +
                                   $"(voz→texto {item.SttMs} ms, traducción {item.MtMs} ms, síntesis {ttsMs} ms" +
                                   (cloneMs > 0 ? $", mi voz {cloneMs} ms)" : ")"));
                Interlocked.Decrement(ref _inFlight);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status?.Invoke("Error: " + ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_vadLock) _disposed = true;
        _idleTimer?.Dispose();
        _cts.Cancel();
        try { _capture.StopRecording(); } catch { /* ya detenido */ }
        _capture.Dispose();
        _segments.CompleteAdding();
        _synth.CompleteAdding();
        _recognizeThread?.Join(2000);
        _speakThread?.Join(2000);
        foreach (var p in _players) p.Dispose();
        _vad.Dispose();
        _tts.Dispose();
        _stt.Dispose();
    }
}
