using System.Diagnostics;
using SherpaOnnx;

// Spike: comprueba que Kokoro funciona desde C# con la misma configuración que usa la app.
var models = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models"));
var dir = Path.Combine(models, "kokoro-multi-lang-v1_0");

var cfg = new OfflineTtsConfig();
cfg.Model.Kokoro.Model = Path.Combine(dir, "model.onnx");
cfg.Model.Kokoro.Voices = Path.Combine(dir, "voices.bin");
cfg.Model.Kokoro.Tokens = Path.Combine(dir, "tokens.txt");
cfg.Model.Kokoro.DataDir = Path.Combine(dir, "espeak-ng-data");
cfg.Model.Kokoro.DictDir = Path.Combine(dir, "dict");
cfg.Model.Kokoro.Lexicon = Path.Combine(dir, "lexicon-us-en.txt") + "," + Path.Combine(dir, "lexicon-zh.txt");
cfg.Model.NumThreads = 4;
cfg.Model.Provider = "cpu";

using var tts = new OfflineTts(cfg);
Console.WriteLine($"Kokoro cargado: {tts.NumSpeakers} voces, {tts.SampleRate} Hz");

foreach (var text in new[]
{
    "Thanks for having me.",
    "I have ten years of experience designing distributed systems.",
    "We use dependency injection, CQRS and a message broker to keep the services decoupled.",
})
{
    var sw = Stopwatch.StartNew();
    var audio = tts.Generate(text, 1.0f, 11);
    var seconds = audio.Samples.Length / (double)audio.SampleRate;
    Console.WriteLine($"{sw.ElapsedMilliseconds,5} ms para {seconds:F1} s de audio ({sw.Elapsed.TotalSeconds / seconds:F2}x)  «{text}»");
}
