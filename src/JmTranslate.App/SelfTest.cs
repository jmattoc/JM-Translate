using System.Diagnostics;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace JmTranslate.App;

/// <summary>
/// Autoprueba de extremo a extremo (sin interfaz): reproduce un audio en inglés por la salida predeterminada,
/// lo captura con el pipeline "ellos → yo" y escribe lo que entendió, su traducción y los tiempos.
/// </summary>
internal static class SelfTest
{
    /// <param name="spanishToEnglish">true = prueba "yo → ellos": el audio es español y se sintetiza inglés (Kokoro, y tu voz si existe mi-voz).</param>
    public static async Task<int> RunAsync(string wavPath, string reportPath, bool spanishToEnglish = false)
    {
        var report = new StringBuilder();
        void Log(string s) { lock (report) report.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}"); }

        try
        {
            using var mt = new MtSidecar();
            await mt.StartAsync(CancellationToken.None);
            Log("Servicio de traducción listo.");

            var render = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var cable = new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
            var options = spanishToEnglish
                ? new PipelineOptions(render, true, "es-en",
                    new TtsSpec("Kokoro · adam (natural)", "kokoro", "kokoro-multi-lang-v1_0", "", 11),
                    new[] { cable ?? render }, CloneVoice: () => Paths.VoiceSample is not null)
                : new PipelineOptions(render, true, "en-es",
                    new TtsSpec("Piper · es_MX ald", "piper", "vits-piper-es_MX-ald-medium", "es_MX-ald-medium", 0),
                    Array.Empty<MMDevice>());
            using var pipeline = await Task.Run(() => new TranslationPipeline(mt, options));
            pipeline.Heard += u => Log($"SUBTÍTULO a los {u.LatencySeconds:F1} s | EN: {u.Original} | ES: {u.Translation} | {u.Timings}");
            pipeline.Status += s => Log("estado: " + s);
            pipeline.Start();
            Log("Pipeline iniciado; reproduciendo audio de prueba…");
            using var watchCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                Activity? last = null;
                while (!watchCts.IsCancellationRequested)
                {
                    var (state, left) = pipeline.Snapshot();
                    if (state != last) { Log($"INDICADOR → {state}" + (state == Activity.Speaking ? $" (quedan {left:F1} s)" : "")); last = state; }
                    await Task.Delay(50);
                }
            });

            using var reader = new AudioFileReader(wavPath);
            using var output = new WaveOutEvent();
            output.Init(reader);
            var played = Stopwatch.StartNew();
            output.Play();
            await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(6));
            watchCts.Cancel();
            Log($"Fin de la prueba ({played.Elapsed.TotalSeconds:F1} s).");
            return 0;
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            return 1;
        }
        finally
        {
            File.WriteAllText(reportPath, report.ToString());
        }
    }
}
