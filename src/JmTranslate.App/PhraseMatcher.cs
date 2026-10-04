using System.Globalization;
using System.Text;

namespace JmTranslate.App;

internal sealed record Suggestion(Phrase Phrase, double Score);

/// <summary>Resultado de interpretar lo que dijo el usuario como posible comando de voz.</summary>
internal sealed record VoiceCommand(bool IsCommand, Phrase? Phrase, int? Number, string Spoken);

internal static class PhraseMatcher
{
    /// <summary>Mínimo para mostrar una sugerencia (en pruebas: 0 falsos positivos con frases ajenas, que llegan a ~0.45).</summary>
    public const double SuggestMin = 0.50;
    /// <summary>Para enviar sola: parecido alto y una segunda opción claramente peor.</summary>
    public const double AutoMin = 0.75;
    public const double AutoMargin = 0.15;

    /// <summary>Ordena las respuestas del banco según lo bien que encajan con la pregunta del entrevistador.</summary>
    public static async Task<List<Suggestion>> SuggestAsync(MtSidecar mt, PhraseBank bank, string question)
    {
        var usable = bank.Items.Where(p => p.En.Length > 0 && p.Questions.Count > 0).ToList();
        if (usable.Count == 0) return new();
        var ranked = await mt.MatchAsync(question, usable.Select(p => (p.Id, (IReadOnlyList<string>)p.Questions)));
        return ranked
            .Select(r => new Suggestion(usable.First(p => p.Id == r.Id), r.Score))
            .ToList();
    }
}

/// <summary>
/// Comandos de voz: al principio de la frase, «banco» (o «frase», «respuesta») y el nombre de la respuesta.
/// Ejemplos: «banco, saludo», «banco translate nube», «banco cinco» (la tecla 5 de la página actual).
/// Si una frase empieza por «banco» pero no es un comando corto, se traduce como cualquier otra.
/// </summary>
internal static class VoiceCommands
{
    private static readonly HashSet<string> Wake = new() { "banco", "frase", "respuesta" };
    private static readonly HashSet<string> Filler = new()
    {
        "translate", "traductor", "traduce", "de", "del", "la", "el", "las", "los", "un", "una", "por", "favor",
        "dame", "di", "manda", "envia", "enviar", "pon", "mi", "me", "con",
    };
    private static readonly Dictionary<string, int> Numbers = new()
    {
        ["uno"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5,
        ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9,
        ["1"] = 1, ["2"] = 2, ["3"] = 3, ["4"] = 4, ["5"] = 5, ["6"] = 6, ["7"] = 7, ["8"] = 8, ["9"] = 9,
    };
    private const int MaxCommandWords = 5;

    public static VoiceCommand Parse(string text, IReadOnlyList<Phrase> bank)
    {
        var tokens = Tokenize(text);
        if (tokens.Count == 0 || !Wake.Contains(tokens[0])) return new(false, null, null, "");

        var rest = tokens.Skip(1).ToList();
        if (rest.Count > MaxCommandWords + 3) return new(false, null, null, ""); // es una frase normal que empieza por «banco»

        var spoken = string.Join(" ", rest);
        var core = rest.Where(t => !Filler.Contains(t) || Numbers.ContainsKey(t)).ToList();

        // «banco cinco» → posición 5 de la página actual
        if (core.Count == 1 && Numbers.TryGetValue(core[0], out var n)) return new(true, null, n, spoken);
        if (core.Count == 0) return new(true, null, null, spoken);
        if (core.Count > MaxCommandWords) return new(false, null, null, "");

        Phrase? best = null;
        var bestScore = 0.0;
        foreach (var p in bank)
        {
            var names = new List<string> { p.Label };
            names.AddRange(p.Aliases);
            foreach (var name in names)
            {
                var score = Dice(core, Tokenize(name).Where(t => !Filler.Contains(t)).ToList());
                if (score > bestScore) { bestScore = score; best = p; }
            }
        }
        return new(true, bestScore >= 0.5 ? best : null, null, spoken);
    }

    // ───────────────────────── utilidades ─────────────────────────

    /// <summary>En minúsculas, sin tildes ni puntuación.</summary>
    internal static List<string> Tokenize(string text)
    {
        var d = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>Coincidencia entre dos listas de palabras, tolerando una letra de diferencia en palabras largas (errores del reconocimiento).</summary>
    private static double Dice(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var used = new bool[b.Count];
        var matched = 0;
        foreach (var x in a)
        {
            for (var j = 0; j < b.Count; j++)
            {
                if (used[j] || !SimilarWord(x, b[j])) continue;
                used[j] = true;
                matched++;
                break;
            }
        }
        return 2.0 * matched / (a.Count + b.Count);
    }

    private static bool SimilarWord(string x, string y)
    {
        if (x == y) return true;
        if (Math.Min(x.Length, y.Length) < 5) return false;
        return Levenshtein(x, y) <= 1;
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }
}
