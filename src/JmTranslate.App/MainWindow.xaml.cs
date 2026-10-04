using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using NAudio.CoreAudioApi;

namespace JmTranslate.App;

public sealed class Line
{
    public string Original { get; init; } = "";
    public string Translation { get; init; } = "";
    public Visibility OriginalVisibility { get; init; } = Visibility.Visible;
    public Brush Color { get; init; } = Brushes.White;
}

public partial class MainWindow : Window
{
    private const int MaxLines = 10;
    private static readonly Brush TheirsColor = Brushes.White;
    private static readonly Brush MineColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7F, 0xD1, 0xFF));

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ObservableCollection<Line> _lines = new();
    private readonly MtSidecar _mt = new();
    private readonly List<MMDevice> _renderDevices = new();
    private readonly List<MMDevice> _micDevices = new();
    private readonly List<TtsSpec> _voices = new();
    private TranslationPipeline? _listen;
    private TranslationPipeline? _talk;

    public MainWindow()
    {
        InitializeComponent();
        Lines.ItemsSource = _lines;

        _renderDevices.AddRange(_enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active));
        _micDevices.AddRange(_enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active));

        var renderNames = _renderDevices.Select(d => d.FriendlyName).ToList();
        var defaultRender = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        var defaultMic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID;

        // Ellos → yo
        CaptureCombo.ItemsSource = renderNames;
        CaptureCombo.SelectedIndex = Math.Max(0, _renderDevices.FindIndex(d => d.ID == defaultRender));
        SpeakCombo.ItemsSource = renderNames;
        SpeakCombo.SelectedIndex = CaptureCombo.SelectedIndex;

        // Yo → ellos
        MicCombo.ItemsSource = _micDevices.Select(d => d.FriendlyName).ToList();
        MicCombo.SelectedIndex = Math.Max(0, _micDevices.FindIndex(d => d.ID == defaultMic));
        SendCombo.ItemsSource = renderNames;
        SendCombo.SelectedIndex = Math.Max(0, _renderDevices.FindIndex(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)));
        var monitorOptions = new List<string> { "(ninguno)" };
        monitorOptions.AddRange(renderNames);
        MonitorCombo.ItemsSource = monitorOptions;
        MonitorCombo.SelectedIndex = 0;

        // Voces en inglés: Piper (rápidas) y Kokoro (más naturales, ~1 s más lentas). Por defecto, Piper john.
        foreach (var dir in Directory.GetDirectories(Paths.Models, "vits-piper-en_US-*").OrderBy(d => d))
        {
            var name = Path.GetFileName(dir)["vits-piper-en_US-".Length..];
            var model = Path.GetFileNameWithoutExtension(Directory.GetFiles(dir, "*.onnx").Single());
            _voices.Add(new TtsSpec($"Piper · {name} (rápida)", "piper", Path.GetFileName(dir), model, 0));
        }
        if (Directory.Exists(Path.Combine(Paths.Models, "kokoro-multi-lang-v1_0")))
        {
            foreach (var (name, sid) in new[] { ("adam", 11), ("michael", 16), ("eric", 13), ("liam", 15), ("onyx", 17) })
                _voices.Add(new TtsSpec($"Kokoro · {name} (natural)", "kokoro", "kokoro-multi-lang-v1_0", "", sid));
        }
        VoiceCombo.ItemsSource = _voices.Select(v => v.Label).ToList();
        VoiceCombo.SelectedIndex = Math.Max(0, _voices.FindIndex(v => v.Label.Contains("john-medium")));

        var indicatorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        indicatorTimer.Tick += (_, _) => UpdateTalkIndicator();
        indicatorTimer.Start();

        if (Paths.VoiceSample is null)
        {
            CloneCheck.IsEnabled = false;
            CloneHint.Text = "(falta models/mi-voz.wav)";
        }
    }

    private double _speakPeak;

    /// <summary>Semáforo "¿ya puedo hablar?": la barra se vacía mientras ellos oyen tu frase en inglés.</summary>
    private void UpdateTalkIndicator()
    {
        static Brush Solid(byte r, byte g, byte b) => new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));

        if (_talk is null)
        {
            _speakPeak = 0;
            TalkBar.Value = 0;
            TalkIndicator.Background = Solid(0x2C, 0x30, 0x38);
            TalkIndicatorText.Text = "Pulsa «Hablar» para empezar";
            return;
        }

        var (state, left) = _talk.Snapshot();
        if (state == Activity.Speaking) _speakPeak = Math.Max(_speakPeak, left); else _speakPeak = 0;

        TalkBar.Value = state == Activity.Speaking && _speakPeak > 0 ? left / _speakPeak : 0;
        switch (state)
        {
            case Activity.Speaking:
                TalkIndicator.Background = Solid(0x5A, 0x1E, 0x1B);
                TalkIndicatorText.Text = $"Ellos te están oyendo — espera {left:F1} s";
                break;
            case Activity.Processing:
                TalkIndicator.Background = Solid(0x6B, 0x4E, 0x00);
                TalkIndicatorText.Text = "Traduciendo… espera";
                break;
            case Activity.Hearing:
                TalkIndicator.Background = Solid(0x1F, 0x4F, 0x8A);
                TalkIndicatorText.Text = "Te escucho…";
                break;
            default:
                TalkIndicator.Background = Solid(0x1E, 0x6B, 0x38);
                TalkIndicatorText.Text = "✔ Puedes hablar";
                break;
        }
    }

    private async void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_listen is not null)
        {
            _listen.Dispose();
            _listen = null;
            ListenButton.Content = "Escuchar";
            CaptureCombo.IsEnabled = SpeakCombo.IsEnabled = VoiceCheck.IsEnabled = true;
            SetStatus("Escucha detenida.");
            return;
        }

        var capture = _renderDevices[CaptureCombo.SelectedIndex];
        var speak = _renderDevices[SpeakCombo.SelectedIndex];
        var outputs = VoiceCheck.IsChecked == true ? new List<MMDevice> { speak } : new List<MMDevice>();
        if (outputs.Count > 0 && speak.ID == capture.ID)
            SetStatus("Aviso: la voz sale por el mismo dispositivo que se captura; se pausa la captura mientras habla.");

        var spanishVoice = new TtsSpec("Piper · es_MX ald", "piper", "vits-piper-es_MX-ald-medium", "es_MX-ald-medium", 0);
        var options = new PipelineOptions(capture, true, "en-es", spanishVoice, outputs);

        _listen = await StartPipelineAsync(ListenButton, options, TheirsColor, showOriginal: () => OriginalCheck.IsChecked == true);
        if (_listen is not null)
        {
            ListenButton.Content = "Detener";
            CaptureCombo.IsEnabled = SpeakCombo.IsEnabled = VoiceCheck.IsEnabled = false;
        }
    }

    private async void TalkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_talk is not null)
        {
            _talk.Dispose();
            _talk = null;
            TalkButton.Content = "Hablar";
            MicCombo.IsEnabled = SendCombo.IsEnabled = MonitorCombo.IsEnabled = VoiceCombo.IsEnabled = SendVoiceCheck.IsEnabled = true;
            SetStatus("Micrófono detenido.");
            return;
        }

        var mic = _micDevices[MicCombo.SelectedIndex];
        // Sin "Enviar voz" solo se muestran los subtítulos en inglés (modo teleprompter): no hay síntesis ni retraso de voz.
        var outputs = new List<MMDevice>();
        if (SendVoiceCheck.IsChecked == true)
        {
            outputs.Add(_renderDevices[SendCombo.SelectedIndex]);
            if (MonitorCombo.SelectedIndex > 0) outputs.Add(_renderDevices[MonitorCombo.SelectedIndex - 1]);
        }

        var options = new PipelineOptions(mic, false, "es-en", _voices[VoiceCombo.SelectedIndex], outputs,
            CloneVoice: () => Dispatcher.Invoke(() => CloneCheck.IsChecked == true));

        _talk = await StartPipelineAsync(TalkButton, options, MineColor, showOriginal: () => OriginalCheck.IsChecked == true);
        if (_talk is not null)
        {
            TalkButton.Content = "Detener";
            MicCombo.IsEnabled = SendCombo.IsEnabled = MonitorCombo.IsEnabled = VoiceCombo.IsEnabled = SendVoiceCheck.IsEnabled = false;
        }
    }

    private async Task<TranslationPipeline?> StartPipelineAsync(System.Windows.Controls.Button button, PipelineOptions options, Brush color, Func<bool> showOriginal)
    {
        button.IsEnabled = false;
        try
        {
            SetStatus("Iniciando servicio de traducción (la primera vez tarda unos segundos)…");
            await _mt.StartAsync(CancellationToken.None);

            SetStatus("Cargando modelos de voz…");
            var pipeline = await Task.Run(() => new TranslationPipeline(_mt, options));
            pipeline.Heard += u => Dispatcher.Invoke(() => OnHeard(u, color, showOriginal()));
            pipeline.Status += s => Dispatcher.Invoke(() => SetStatus(s));
            pipeline.Start();
            return pipeline;
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
            return null;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OnHeard(Utterance u, Brush color, bool showOriginal)
    {
        _lines.Add(new Line
        {
            Original = u.Original,
            Translation = u.Translation,
            Color = color,
            OriginalVisibility = showOriginal ? Visibility.Visible : Visibility.Collapsed,
        });
        while (_lines.Count > MaxLines) _lines.RemoveAt(0);
        Scroller.ScrollToEnd();
        SetStatus($"Última frase: {u.LatencySeconds:F1} s en total  ({u.Timings})");
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _listen?.Dispose();
        _talk?.Dispose();
        _mt.Dispose();
    }
}
