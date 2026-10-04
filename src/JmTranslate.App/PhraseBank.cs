using System.Text.Encodings.Web;
using System.Text.Json;

namespace JmTranslate.App;

/// <summary>Una frase (o respuesta) preparada: lo que se dirá en inglés, con su versión en español como referencia.</summary>
public sealed class Phrase
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string Es { get; set; } = "";
    public string En { get; set; } = "";
}

/// <summary>
/// Banco de frases y respuestas preparadas (data/phrases.json, fuera de git). Las primeras nueve se disparan con
/// Ctrl+Alt+Numpad1…9. Se renderizan una vez con la voz elegida y quedan en caché, así suenan al instante.
/// </summary>
internal sealed class PhraseBank
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path = Path.Combine(Paths.Data, "phrases.json");

    public List<Phrase> Items { get; private set; } = new();

    public PhraseBank()
    {
        try
        {
            if (File.Exists(_path))
                Items = JsonSerializer.Deserialize<List<Phrase>>(File.ReadAllText(_path), Json) ?? new();
        }
        catch { Items = new(); }

        if (Items.Count == 0)
        {
            Items = Seeds();
            Save();
        }
    }

    public void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(Items, Json));

    /// <summary>Frases de relleno y de apoyo para entrevistas; se pueden editar o borrar.</summary>
    private static List<Phrase> Seeds() => new()
    {
        new() { Label = "Déjame pensar",     Es = "Déjame pensarlo un segundo.",                          En = "Let me think about that for a second." },
        new() { Label = "Repetir pregunta",  Es = "¿Podría repetir la pregunta, por favor?",              En = "Could you please repeat the question?" },
        new() { Label = "Buena pregunta",    Es = "Es una buena pregunta.",                               En = "That's a good question." },
        new() { Label = "No te escuché",     Es = "Perdón, no te escuché bien. ¿Puedes repetir?",         En = "Sorry, I didn't catch that. Could you say it again?" },
        new() { Label = "Más despacio",      Es = "¿Podrías hablar un poco más despacio, por favor?",     En = "Could you speak a little more slowly, please?" },
        new() { Label = "Saludo",            Es = "Hola, mucho gusto. Gracias por la oportunidad.",       En = "Hi, nice to meet you. Thank you for the opportunity." },
        new() { Label = "Gracias",           Es = "Muchas gracias por tu tiempo.",                        En = "Thank you so much for your time." },
        new() { Label = "Problema de audio", Es = "Parece que tengo un problema de conexión. ¿Me escuchas bien?", En = "It seems I'm having a connection issue. Can you hear me okay?" },
        new() { Label = "Prueba de audio",   Es = "Prueba de audio, uno, dos, tres.",                     En = "Audio check, one, two, three. Can you hear me clearly?" },
    };
}
