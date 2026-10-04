using System.Windows;

namespace JmTranslate.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Autoprueba sin interfaz: JmTranslate.App.exe --selftest <audio.wav> <salida.txt>
        var args = e.Args;
        if (args.Length >= 2 && args[0] == "--cleantest")
        {
            var samples = new[]
            {
                "Eh, pues, pues, sí, yo trabajé con dot net y SQL sever.",
                "Este, la verdad, este módulo usa C sharp y el Rabat MQ.",
                "Um, we use micro services and, uh, Cubernets in production.",
                "Tengo que que revisar el el código.",
                "Hola, eh.",
                "Con.NET y ASP.NET funciona.",
                "Sistemas con.NET y la nube, with.NET and the cloud.",
            };
            File.WriteAllLines(args[1], samples.Select(s => $"{s}  =>  {TextCleanup.Clean(s)}"));
            Shutdown(0);
            return;
        }
        if (args.Length >= 2 && args[0] is "--pronunciation" or "--phrases")
        {
            Shutdown(await (args[0] == "--pronunciation" ? SelfTest.PronunciationAsync(args[1]) : SelfTest.PhrasesAsync(args[1])));
            return;
        }
        if (args.Length >= 3 && args[0] == "--ptttest")
        {
            Shutdown(await SelfTest.PushToTalkAsync(args[1], args[2]));
            return;
        }
        if (args.Length >= 4 && args[0] == "--soak")
        {
            Shutdown(await SelfTest.SoakAsync(int.Parse(args[1]), args[2], args[3]));
            return;
        }
        if (args.Length >= 3 && args[0] == "--selftest")
        {
            var code = await SelfTest.RunAsync(args[1], args[2], spanishToEnglish: args.Length > 3 && args[3] == "es");
            Shutdown(code);
            return;
        }
        base.OnStartup(e);
    }
}
