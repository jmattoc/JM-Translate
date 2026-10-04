using System.Text.Json;

namespace JmTranslate.App;

/// <summary>Ajustes recordados entre sesiones (data/settings.json, fuera de git). Guardan nombres de dispositivos, no identificadores.</summary>
internal sealed class Settings
{
    public string? Capture { get; set; }
    public string? Speak { get; set; }
    public string? Mic { get; set; }
    public string? Send { get; set; }
    public string? Monitor { get; set; }
    public string? Voice { get; set; }
    public bool SpanishVoice { get; set; } = true;
    public bool ShowOriginal { get; set; } = true;
    public bool SendVoice { get; set; } = true;
    public bool Clone { get; set; }
    public bool PushToTalk { get; set; }
    public string? PushToTalkKey { get; set; }
    public bool Compact { get; set; }

    private static string FilePath => Path.Combine(Paths.Data, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* si no se puede guardar, la app sigue funcionando */ }
    }
}
