using SherpaOnnx;

namespace JmTranslate.App;

/// <param name="Label">Nombre para mostrar y para distinguir la voz base (el timbre clonado depende de ella).</param>
/// <param name="Kind">"piper" o "kokoro".</param>
/// <param name="Dir">Carpeta del modelo dentro de models.</param>
/// <param name="Model">Piper: nombre del .onnx sin extensión. Kokoro: no se usa.</param>
/// <param name="Sid">Piper: 0. Kokoro: número de voz.</param>
internal sealed record TtsSpec(string Label, string Kind, string Dir, string Model, int Sid)
{
    /// <summary>Identificador ASCII estable de la voz base (viaja en una cabecera HTTP y en el nombre de la caché).</summary>
    public string VoiceId => $"{Kind}-{Dir}-{Sid}";
}

internal static class TtsFactory
{
    public static OfflineTts Create(TtsSpec spec)
    {
        var dir = Path.Combine(Paths.Models, spec.Dir);
        var cfg = new OfflineTtsConfig();
        if (spec.Kind == "kokoro")
        {
            cfg.Model.Kokoro.Model = Path.Combine(dir, "model.onnx");
            cfg.Model.Kokoro.Voices = Path.Combine(dir, "voices.bin");
            cfg.Model.Kokoro.Tokens = Path.Combine(dir, "tokens.txt");
            cfg.Model.Kokoro.DataDir = Path.Combine(dir, "espeak-ng-data");
            cfg.Model.Kokoro.DictDir = Path.Combine(dir, "dict");
            cfg.Model.Kokoro.Lexicon = Path.Combine(dir, "lexicon-us-en.txt") + "," + Path.Combine(dir, "lexicon-zh.txt");
            cfg.Model.NumThreads = 4;
        }
        else
        {
            cfg.Model.Vits.Model = Path.Combine(dir, spec.Model + ".onnx");
            cfg.Model.Vits.Tokens = Path.Combine(dir, "tokens.txt");
            cfg.Model.Vits.DataDir = Path.Combine(dir, "espeak-ng-data");
            cfg.Model.NumThreads = 2;
        }
        cfg.Model.Provider = "cpu";
        return new OfflineTts(cfg);
    }
}
