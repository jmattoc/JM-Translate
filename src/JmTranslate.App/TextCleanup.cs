using System.Text.Json;
using System.Text.RegularExpressions;

namespace JmTranslate.App;

/// <summary>
/// Limpia el texto reconocido antes de traducirlo: quita muletillas y repeticiones (que el traductor
/// convierte en frases sin sentido) y corrige términos técnicos que el reconocimiento suele escribir mal.
/// Los términos se leen de tools/glossary.json (sección "stt"), así se pueden ampliar sin recompilar.
/// </summary>
internal static partial class TextCleanup
{
    // Muletillas: se quitan solas ("Eh, pues…"). "este" solo cuando va seguido de coma ("Este, la verdad…"),
    // porque en "este módulo" es un demostrativo legítimo.
    [GeneratedRegex(@"(?:,\s*)?\b(?:eh+|e+hm+|mm+h*|hmm+|ah+|uh+|um+|er)\b(?:\s*,)?", RegexOptions.IgnoreCase)]
    private static partial Regex FillersRegex();

    [GeneratedRegex(@"\beste\s*,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex EsteFillerRegex();

    // Repeticiones inmediatas: "pues, pues" → "pues", "que que" → "que".
    [GeneratedRegex(@"\b([\p{L}']+)(?:\s*,?\s+\1\b)+", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWordRegex();

    [GeneratedRegex(@"\b(?:dog ?net|dot ?net|don+et|dot-net)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetRegex();

    [GeneratedRegex(@"(?<=[a-z])\.(?:NET|net)\b")]
    private static partial Regex GluedDotNetRegex();

    private static readonly Lazy<(string Wrong, string Right)[]> Terms = new(LoadTerms);

    public static string Clean(string text)
    {
        text = FillersRegex().Replace(text, " ");
        text = EsteFillerRegex().Replace(text, " ");
        text = RepeatedWordRegex().Replace(text, "$1");

        text = DotNetRegex().Replace(text, ".NET");
        text = GluedDotNetRegex().Replace(text, " .NET"); // "con.NET" → "con .NET" (ASP.NET queda igual)
        foreach (var (wrong, right) in Terms.Value)
            text = Regex.Replace(text, @"\b" + Regex.Escape(wrong) + @"\b", right, RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"\s{2,}", " ");
        text = Regex.Replace(text, @"\s+([.,?!])(?=\s|$)", "$1"); // no toca " .NET"
        text = text.Trim().TrimStart(',', '.', ' ').TrimEnd(',', ' ');
        if (text.Length > 0) text = char.ToUpperInvariant(text[0]) + text[1..];
        return text;
    }

    private static (string, string)[] LoadTerms()
    {
        try
        {
            var path = Path.Combine(Paths.Tools, "glossary.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("stt", out var stt)) return Array.Empty<(string, string)>();
            return stt.EnumerateObject().Select(p => (p.Name, p.Value.GetString() ?? "")).ToArray();
        }
        catch
        {
            return Array.Empty<(string, string)>(); // sin glosario se sigue funcionando
        }
    }
}
