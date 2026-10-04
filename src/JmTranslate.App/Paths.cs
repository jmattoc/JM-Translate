namespace JmTranslate.App;

/// <summary>Ubica la raíz del proyecto (la carpeta que contiene models y tools).</summary>
internal static class Paths
{
    public static readonly string Root = FindRoot();
    public static string Models => Path.Combine(Root, "models");
    public static string Tools => Path.Combine(Root, "tools");

    /// <summary>Datos personales locales (ajustes, banco de frases, audios en caché). Está fuera de git.</summary>
    public static string Data => Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
    public static string Cache => Directory.CreateDirectory(Path.Combine(Root, "data", "cache")).FullName;

    /// <summary>Muestra de mi voz (models/mi-voz.*) o null si no existe.</summary>
    public static string? VoiceSample =>
        new[] { "wav", "mp3", "m4a", "ogg", "flac" }
            .Select(ext => Path.Combine(Models, $"mi-voz.{ext}"))
            .FirstOrDefault(File.Exists);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "models")) && Directory.Exists(Path.Combine(dir.FullName, "tools")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("No se encontró la carpeta raíz de JM-Translate (con models y tools).");
    }
}
