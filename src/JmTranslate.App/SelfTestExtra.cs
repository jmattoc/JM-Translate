using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SherpaOnnx;

namespace JmTranslate.App;

/// <summary>Pruebas automáticas adicionales. Usan el cable virtual para reproducir, así no suenan por tus altavoces.</summary>
internal static partial class SelfTest
{
    private static MMDevice? Cable() =>
        new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

    /// <summary>Dispositivo de salida sin altavoces (HDMI) para que la voz de prueba no suene; si no hay, el cable.</summary>
    private static MMDevice SilentOutput() =>
        new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
                              || d.FriendlyName.Contains("HDMI", StringComparison.OrdinalIgnoreCase))
        ?? Cable() ?? throw new InvalidOperationException("No hay dispositivo de salida de prueba.");

    private static int CableWaveOutNumber()
    {
        for (var i = 0; i < WaveOut.DeviceCount; i++)
            if (WaveOut.GetCapabilities(i).ProductName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Reproduce un WAV en el cable virtual y espera a que termine.</summary>
    private static async Task PlayOnCableAsync(string wav)
    {
        using var reader = new AudioFileReader(wav);
        using var output = new WaveOutEvent { DeviceNumber = CableWaveOutNumber() };
        output.Init(reader);
        output.Play();
        await Task.Delay(reader.TotalTime + TimeSpan.FromMilliseconds(200));
    }

    private static OfflineRecognizer CreateRecognizer()
    {
        var w = Path.Combine(Paths.Models, "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8");
        var cfg = new OfflineRecognizerConfig();
        cfg.ModelConfig.Transducer.Encoder = Path.Combine(w, "encoder.int8.onnx");
        cfg.ModelConfig.Transducer.Decoder = Path.Combine(w, "decoder.int8.onnx");
        cfg.ModelConfig.Transducer.Joiner = Path.Combine(w, "joiner.int8.onnx");
        cfg.ModelConfig.Tokens = Path.Combine(w, "tokens.txt");
        cfg.ModelConfig.ModelType = "nemo_transducer";
        cfg.ModelConfig.NumThreads = 4;
        cfg.ModelConfig.Provider = "cpu";
        return new OfflineRecognizer(cfg);
    }

    private static string Transcribe(OfflineRecognizer stt, float[] samples, int rate)
    {
        using var stream = stt.CreateStream();
        stream.AcceptWaveform(rate, samples);
        stt.Decode(stream);
        return stream.Result.Text.Trim();
    }

    private static async Task<int> Harness(string reportPath, Func<Action<string>, Task> body)
    {
        var report = new StringBuilder();
        void Log(string s) { lock (report) report.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}"); }
        try { await body(Log); return 0; }
        catch (Exception ex) { Log("ERROR: " + ex); return 1; }
        finally { File.WriteAllText(reportPath, report.ToString()); }
    }

    // ─────────────────────────── pronunciación de siglas ───────────────────────────

    /// <summary>Sintetiza frases con siglas, con y sin la regla de pronunciación, y comprueba qué entiende el reconocimiento.</summary>
    public static Task<int> PronunciationAsync(string reportPath) => Harness(reportPath, async log =>
    {
        await Task.Yield();
        var cases = new (string Text, string[] Terms)[]
        {
            ("We use CQRS and gRPC with Kubernetes.", new[] { "CQRS", "gRPC", "Kubernetes" }),
            ("I have ten years of experience with .NET and C#.", new[] { ".NET", "C#" }),
            ("We deploy on AWS with CI/CD pipelines.", new[] { "AWS", "CI/CD" }),
            ("I worked with SQL Server and PostgreSQL.", new[] { "SQL Server", "PostgreSQL" }),
            ("The API returns JSON over HTTPS.", new[] { "API", "JSON", "HTTPS" }),
            ("I know OAuth and JWT for authentication.", new[] { "OAuth", "JWT" }),
            ("We use RabbitMQ and Redis for messaging.", new[] { "RabbitMQ", "Redis" }),
            ("I follow TDD and DDD principles.", new[] { "TDD", "DDD" }),
            ("The YAML file configures nginx and Docker.", new[] { "YAML", "nginx", "Docker" }),
        };

        using var stt = CreateRecognizer();
        foreach (var spec in new[]
        {
            new TtsSpec("Piper john", "piper", "vits-piper-en_US-john-medium", "en_US-john-medium", 0),
            new TtsSpec("Kokoro adam", "kokoro", "kokoro-multi-lang-v1_0", "", 11),
        })
        {
            using var tts = TtsFactory.Create(spec);
            int rawHits = 0, mapHits = 0, total = 0;
            log($"=== {spec.Label} ===");
            foreach (var (text, terms) in cases)
            {
                var raw = Transcribe(stt, Generate(tts, text, spec, out var rate1), rate1);
                var mapped = Transcribe(stt, Generate(tts, Pronunciation.ForEnglish(text, spec.Kind), spec, out var rate2), rate2);
                var rawOk = terms.Count(t => Contains(raw, t));
                var mapOk = terms.Count(t => Contains(mapped, t));
                rawHits += rawOk; mapHits += mapOk; total += terms.Length;
                log($"{text}\n      sin regla ({rawOk}/{terms.Length}): {raw}\n      con regla ({mapOk}/{terms.Length}): {mapped}");
            }
            log($"RESUMEN {spec.Label}: términos entendidos sin regla {rawHits}/{total}, con regla {mapHits}/{total}");
        }
    });

    private static float[] Generate(OfflineTts tts, string text, TtsSpec spec, out int rate)
    {
        var audio = tts.Generate(text, 1.0f, spec.Sid);
        rate = audio.SampleRate;
        return audio.Samples;
    }

    private static bool Contains(string haystack, string term) =>
        Regex.Replace(haystack, @"[\s\-]", "").Contains(Regex.Replace(term, @"[\s\-]", ""), StringComparison.OrdinalIgnoreCase);

    // ─────────────────────────── banco de frases ───────────────────────────

    /// <summary>Prepara las frases del banco (voz estándar y con tu timbre) y comprueba que se entienden.</summary>
    public static Task<int> PhrasesAsync(string reportPath) => Harness(reportPath, async log =>
    {
        using var mt = new MtSidecar();
        await mt.StartAsync(CancellationToken.None);
        using var audio = new PhraseAudio(mt);
        var bank = new PhraseBank();
        var john = new TtsSpec("Piper john", "piper", "vits-piper-en_US-john-medium", "en_US-john-medium", 0);
        using var stt = CreateRecognizer();

        foreach (var clone in new[] { false, true }.Where(c => !c || Paths.VoiceSample is not null))
        {
            log($"=== frases con Piper john{(clone ? " + mi voz" : "")} ===");
            foreach (var p in bank.Items.Where(x => x.En.Length > 0))
            {
                var wasReady = audio.IsReady(p, john, clone);
                var sw = Stopwatch.StartNew();
                var (samples, rate) = await audio.GetAsync(p, john, clone);
                var ms = sw.ElapsedMilliseconds;
                var heard = Transcribe(stt, samples, rate);
                log($"{p.Label,-18} {samples.Length / (double)rate,4:F1} s de audio, {(wasReady ? "desde caché" : "preparada")} en {ms} ms\n      dicho: {p.En}\n      oído : {heard}");
            }
        }
    });

    // ─────────────────────────── pulsar para hablar ───────────────────────────

    /// <summary>Comprueba que sin la tecla no se captura nada y que al soltarla la frase se envía enseguida.</summary>
    public static Task<int> PushToTalkAsync(string wavPath, string reportPath) => Harness(reportPath, async log =>
    {
        using var mt = new MtSidecar();
        await mt.StartAsync(CancellationToken.None);
        var cable = Cable() ?? throw new InvalidOperationException("Falta el cable virtual.");
        var spanish = new TtsSpec("Piper es", "piper", "vits-piper-es_MX-ald-medium", "es_MX-ald-medium", 0);

        var held = false;
        var heard = new List<(DateTime At, string Text)>();
        using var pipeline = await Task.Run(() => new TranslationPipeline(mt,
            new PipelineOptions(cable, true, "en-es", spanish, Array.Empty<MMDevice>(), PushToTalk: () => held)));
        pipeline.Heard += u => { lock (heard) heard.Add((DateTime.Now, u.Original)); };
        pipeline.Start();

        log("A) Tecla NO pulsada mientras suena el audio: no debe oírse nada.");
        held = false;
        await PlayOnCableAsync(wavPath);
        await Task.Delay(4000);
        lock (heard) log($"   resultado: {heard.Count} frases captadas (esperado 0) → {(heard.Count == 0 ? "OK" : "FALLA")}");

        log("B) Tecla pulsada durante el audio y soltada al terminar: debe enviarse de inmediato.");
        lock (heard) heard.Clear();
        held = true;
        await PlayOnCableAsync(wavPath);
        var released = DateTime.Now;
        held = false;
        for (var i = 0; i < 80 && heard.Count == 0; i++) await Task.Delay(100);
        lock (heard)
        {
            if (heard.Count == 0) log("   FALLA: no llegó ninguna frase.");
            else log($"   OK: la primera frase llegó {(heard[0].At - released).TotalSeconds:F1} s después de soltar la tecla: «{heard[0].Text}»");
        }
    });

    // ─────────────────────────── estabilidad (soak) ───────────────────────────

    /// <summary>
    /// Repite una frase en español durante N minutos por el pipeline completo "yo → ellos" (Kokoro y tu voz si existe)
    /// y registra latencia, memoria y CPU. La voz sale por un dispositivo sin altavoces.
    /// </summary>
    public static Task<int> SoakAsync(int minutes, string wavPath, string reportPath) => Harness(reportPath, async log =>
    {
        using var mt = new MtSidecar();
        await mt.StartAsync(CancellationToken.None);
        var cable = Cable() ?? throw new InvalidOperationException("Falta el cable virtual.");
        var silent = SilentOutput();
        var clone = Paths.VoiceSample is not null;
        log($"Prueba de {minutes} min. Entrada: {cable.FriendlyName}. Salida de voz: {silent.FriendlyName}. Mi voz: {clone}.");

        var firstAudio = new List<double>();
        var drops = 0;
        using var pipeline = await Task.Run(() => new TranslationPipeline(mt, new PipelineOptions(cable, true, "es-en",
            new TtsSpec("Kokoro adam", "kokoro", "kokoro-multi-lang-v1_0", "", 11), new[] { silent }, CloneVoice: () => clone)));
        pipeline.Status += s =>
        {
            var m = Regex.Match(s, @"La voz empieza a los ([\d.,]+) s");
            if (m.Success) lock (firstAudio) firstAudio.Add(double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture));
            else if (s.StartsWith("Atrasado") || s.StartsWith("Error") || s.StartsWith("No se pudo")) { Interlocked.Increment(ref drops); log("ALERTA: " + s); }
        };
        var utterances = 0;
        pipeline.Heard += _ => Interlocked.Increment(ref utterances);
        pipeline.Start();

        var proc = Process.GetCurrentProcess();
        long PythonMb() => Process.GetProcessesByName("python").Sum(p => { try { return p.WorkingSet64; } catch { return 0L; } }) / 1_048_576;
        var startMem = proc.WorkingSet64 / 1_048_576;
        var startPy = PythonMb();
        var lastCpu = proc.TotalProcessorTime;
        var lastAt = Stopwatch.GetTimestamp();
        var end = DateTime.Now.AddMinutes(minutes);
        var nextSample = DateTime.Now.AddSeconds(30);

        while (DateTime.Now < end)
        {
            await PlayOnCableAsync(wavPath);
            await Task.Delay(2500);
            if (DateTime.Now >= nextSample)
            {
                proc.Refresh();
                var cpu = (proc.TotalProcessorTime - lastCpu).TotalSeconds / Stopwatch.GetElapsedTime(lastAt).TotalSeconds / Environment.ProcessorCount * 100;
                lastCpu = proc.TotalProcessorTime; lastAt = Stopwatch.GetTimestamp();
                double avg; lock (firstAudio) avg = firstAudio.Count == 0 ? 0 : firstAudio.TakeLast(5).Average();
                log($"muestra: frases={utterances}, memoria app={proc.WorkingSet64 / 1_048_576} MB, python={PythonMb()} MB, CPU app={cpu:F0}%, voz empieza a (últimas 5)={avg:F1} s");
                nextSample = DateTime.Now.AddSeconds(30);
            }
        }
        await Task.Delay(8000);

        proc.Refresh();
        double[] sorted; lock (firstAudio) sorted = firstAudio.OrderBy(x => x).ToArray();
        double Pct(double q) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        log($"RESUMEN: frases={utterances}, voces generadas={sorted.Length}, alertas={drops}");
        log($"   tiempo hasta que empieza la voz: mediana {Pct(0.5):F1} s, p95 {Pct(0.95):F1} s, máximo {Pct(1.0):F1} s");
        log($"   memoria app: {startMem} → {proc.WorkingSet64 / 1_048_576} MB; python: {startPy} → {PythonMb()} MB");
    });
}

internal static partial class SelfTest
{
    // ─────────────────────────── comandos de voz y sugerencias ───────────────────────────

    /// <summary>Prueba con el banco real: interpretación de comandos de voz y sugerencias a partir de preguntas.</summary>
    public static Task<int> MatchAsync(string reportPath) => Harness(reportPath, async log =>
    {
        var bank = new PhraseBank();
        log($"Banco: {bank.Items.Count} entradas.");

        log("=== Comandos de voz ===");
        // (lo que reconocería el micrófono, entrada esperada o null si no es comando / no existe)
        var commands = new (string Heard, string? Expect)[]
        {
            ("Banco, saludo.", "Saludo"),
            ("Banco translate saludo", "Saludo"),
            ("Banco, preséntate.", "Preséntate"),
            ("Banco presentación", "Preséntate"),
            ("Banco, nube.", "Nube y pipelines"),
            ("Banco, devops", "DevOps"),
            ("Banco, SQL.", "SQL Server"),
            ("Banco, disponibilidad", "Disponibilidad inmediata"),
            ("Banco, dame la disponibilidad", "Disponibilidad inmediata"),
            ("Banco, proyecto OCR", "Proyecto destacado (OCR y PDF)"),
            ("Banco, salario", "Expectativa salarial"),
            ("Frase gracias", "Gracias"),
            ("Respuesta, zona horaria", "Zona horaria"),
            ("Banco, inglés", "Nivel de inglés"),
            ("Banco, no te escuche", "No te escuché"),
            ("Banco, cinco", "#5"),
            ("Banco, ocho.", "#8"),
            ("Banco, cosa que no existe", "(sin coincidencia)"),
            ("Banco de datos es una tecnología muy usada en las empresas grandes.", null),   // frase normal: se traduce
            ("Trabajo con SQL Server y .NET.", null),                                         // frase normal
        };
        int ok = 0;
        foreach (var (heard, expect) in commands)
        {
            var cmd = VoiceCommands.Parse(heard, bank.Items);
            var got = !cmd.IsCommand ? null : cmd.Phrase?.Label ?? (cmd.Number is int n ? $"#{n}" : "(sin coincidencia)");
            var pass = got == expect;
            if (pass) ok++;
            log($"{(pass ? "OK " : "XX ")} «{heard}»  → {(got ?? "se traduce normal")}   (esperado: {expect ?? "se traduce normal"})");
        }
        log($"Comandos: {ok}/{commands.Length} correctos.");

        log("=== Sugerencias a partir de la pregunta del entrevistador ===");
        using var mt = new MtSidecar();
        await mt.StartAsync(CancellationToken.None);
        var questions = new (string Q, string? Expect)[]
        {
            ("So, could you tell me a bit about yourself?", "Preséntate"),
            ("What would you say are your strongest skills?", "Fortalezas"),
            ("How long have you been using .NET?", ".NET y arquitectura limpia"),
            ("Which cloud platforms have you worked on?", "Nube y pipelines"),
            ("Do you have experience with DevOps and automation?", "DevOps"),
            ("How many years have you been working as a developer?", "Años de experiencia"),
            ("Have you ever worked on a document scanning or OCR project?", "Proyecto destacado (OCR y PDF)"),
            ("When would you be able to start?", "Disponibilidad inmediata"),
            ("How do you secure your web APIs?", "APIs REST y JWT"),
            ("Do you also know Java?", "Java y Spring Boot"),
            ("How would you speed up a slow database query?", "Consulta lenta"),
            ("How good is your English?", "Nivel de inglés"),
            ("What are you looking for in your next role?", "Por qué este puesto"),
            ("What's your expected salary?", "Expectativa salarial"),
            ("Do you have any questions for me?", "Preguntas para ellos"),
            ("Thanks for joining the call today.", null),
            ("Let me share my screen for a second.", null),
            ("Our team is based in Austin and in Berlin.", null),
        };
        int good = 0;
        foreach (var (q, expect) in questions)
        {
            var ranked = await PhraseMatcher.SuggestAsync(mt, bank, q);
            var top = ranked.FirstOrDefault();
            var shown = top is not null && top.Score >= PhraseMatcher.SuggestMin ? top : null;
            var got = shown?.Phrase.Label;
            var pass = got == expect;
            if (pass) good++;
            var margin = ranked.Count > 1 && top is not null ? top.Score - ranked[1].Score : 0;
            var auto = shown is not null && shown.Score >= PhraseMatcher.AutoMin && margin >= PhraseMatcher.AutoMargin;
            log($"{(pass ? "OK " : "XX ")} «{q}»\n       → {(got ?? "sin sugerencia")}" +
                (top is null ? "" : $"  ({top.Score:P0}, margen {margin:P0}){(auto ? "  [se enviaría sola]" : "")}") +
                $"   (esperado: {expect ?? "sin sugerencia"})");
        }
        log($"Sugerencias: {good}/{questions.Length} correctas.");
    });
}
