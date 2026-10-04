using System.Text.Json;
using System.Text.RegularExpressions;

namespace JmTranslate.App;

/// <summary>
/// Ajusta cómo se leen en inglés algunas siglas y nombres técnicos ("CI/CD" → "C I C D", ".NET" → "dot net").
/// Solo cambia lo que se sintetiza, no los subtítulos. Las reglas son por motor de voz y solo incluyen casos
/// verificados sintetizando y volviendo a transcribir (tools/pron_variants.py): lo que arregla una voz puede
/// romper otra, y la mayoría de las siglas (API, JWT, SDK, UI…) ya se leen bien sin ayuda.
/// Se puede ampliar en tools/glossary.json, sección "tts-en" (vale para cualquier voz).
/// </summary>
internal static class Pronunciation
{
    private static readonly (string Term, string Spoken)[] Common =
    {
        (".NET", "dot net"), ("C#", "C sharp"), ("F#", "F sharp"), ("CI/CD", "C I C D"),
        ("SQL Server", "sequel server"), ("MySQL", "my sequel"), ("NoSQL", "no sequel"),
        ("AWS", "A.W.S."), ("TDD", "T.D.D."), ("DDD", "D.D.D."),
        ("k8s", "Kubernetes"), ("kubectl", "kube control"), ("SaaS", "sass"), ("CRUD", "crud"),
    };

    private static readonly (string Term, string Spoken)[] PiperOnly =
    {
        ("ASP.NET", "A S P dot net"), ("CQRS", "C Q R S"), ("Redis", "Red iss"), ("OAuth", "oh auth"),
        ("JSON", "jason"), ("PostgreSQL", "Postgres"),
    };

    private static readonly (string Term, string Spoken)[] KokoroOnly =
    {
        ("ASP.NET", "A.S.P. dot net"), ("Redis", "Redd-iss"), ("OAuth", "oh-auth"),
    };

    private static readonly Dictionary<string, (Regex Pattern, string Spoken)[]> Cache = new();

    /// <param name="engine">"piper" o "kokoro".</param>
    public static string ForEnglish(string text, string engine)
    {
        foreach (var (pattern, spoken) in RulesFor(engine)) text = pattern.Replace(text, spoken);
        return text;
    }

    private static (Regex, string)[] RulesFor(string engine)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(engine, out var rules)) return rules;

            var all = new List<(string Term, string Spoken)>(Common);
            all.AddRange(engine == "kokoro" ? KokoroOnly : PiperOnly);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.Tools, "glossary.json")));
                if (doc.RootElement.TryGetProperty("tts-en", out var extra))
                    all.InsertRange(0, extra.EnumerateObject().Select(p => (p.Name, p.Value.GetString() ?? "")));
            }
            catch { /* sin glosario se usan las reglas por defecto */ }

            // Los términos más largos primero ("ASP.NET" antes que ".NET"); sin letras pegadas a los lados.
            rules = all.OrderByDescending(t => t.Term.Length)
                .Select(t => (new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(t.Term) + @"(?![\p{L}\p{N}])", RegexOptions.Compiled), t.Spoken))
                .ToArray();
            return Cache[engine] = rules;
        }
    }
}
