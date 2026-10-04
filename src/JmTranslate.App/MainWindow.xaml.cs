using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
    private const int HotkeyMute = 10, HotkeyCompact = 11, HotkeyNextPage = 12;
    private const int PageSize = 9;
    private static readonly Brush TheirsColor = Brushes.White;
    private static readonly Brush MineColor = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7F, 0xD1, 0xFF));

    /// <summary>Teclas para "pulsar para hablar": se leen con el sistema aunque otra aplicación tenga el foco.</summary>
    private static readonly (string Label, int Vk)[] PttKeys =
        { ("Ctrl derecho", 0xA3), ("Alt derecho", 0xA5), ("F9", 0x78), ("F8", 0x77), ("F10", 0x79) };

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly ObservableCollection<Line> _lines = new();
    private readonly MtSidecar _mt = new();
    private readonly List<MMDevice> _renderDevices = new();
    private readonly List<MMDevice> _micDevices = new();
    private readonly List<TtsSpec> _voices = new();
    private readonly Settings _settings = Settings.Load();

    internal PhraseBank Bank { get; } = new();
    private readonly PhraseAudio _phraseAudio;
    private readonly PhrasePlayer _phrasePlayer = new();
    private PhraseBankWindow? _bankWindow;
    private HotkeyManager? _hotkeys;

    private TranslationPipeline? _listen;
    private TranslationPipeline? _talk;

    // Se leen desde el hilo de captura, por eso son campos simples y no controles.
    private volatile bool _pttEnabled;
    private volatile int _pttVk = PttKeys[0].Vk;

    private bool _muted;
    private bool _compact;
    private Size _normalSize;
    private int _phraseBusy;
    private double _speakPeak;
    private int _phrasePage;     // página de atajos del banco (0 = frases 1 a 9)

    public MainWindow()
    {
        InitializeComponent();
        Lines.ItemsSource = _lines;
        _phraseAudio = new PhraseAudio(_mt);

        _renderDevices.AddRange(_enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active));
        _micDevices.AddRange(_enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active));

        var renderNames = _renderDevices.Select(d => d.FriendlyName).ToList();
        var micNames = _micDevices.Select(d => d.FriendlyName).ToList();
        var defaultRender = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        var defaultMic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID;
        var cableIndex = _renderDevices.FindIndex(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

        // Ellos → yo
        CaptureCombo.ItemsSource = renderNames;
        Select(CaptureCombo, renderNames, _settings.Capture, Math.Max(0, _renderDevices.FindIndex(d => d.ID == defaultRender)));
        SpeakCombo.ItemsSource = renderNames;
        Select(SpeakCombo, renderNames, _settings.Speak, CaptureCombo.SelectedIndex);

        // Yo → ellos
        MicCombo.ItemsSource = micNames;
        Select(MicCombo, micNames, _settings.Mic, Math.Max(0, _micDevices.FindIndex(d => d.ID == defaultMic)));
        SendCombo.ItemsSource = renderNames;
        Select(SendCombo, renderNames, _settings.Send, Math.Max(0, cableIndex));
        var monitorOptions = new List<string> { "(ninguno)" };
        monitorOptions.AddRange(renderNames);
        MonitorCombo.ItemsSource = monitorOptions;
        Select(MonitorCombo, monitorOptions, _settings.Monitor, 0);

        // Voces en inglés: Piper (rápidas) y Kokoro (más naturales, ~0.5 s más lentas por cláusula). Por defecto, Piper john.
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
        var voiceNames = _voices.Select(v => v.Label).ToList();
        VoiceCombo.ItemsSource = voiceNames;
        Select(VoiceCombo, voiceNames, _settings.Voice, Math.Max(0, _voices.FindIndex(v => v.Label.Contains("john-medium"))));

        // Pulsar para hablar
        PttKeyCombo.ItemsSource = PttKeys.Select(k => k.Label).ToList();
        Select(PttKeyCombo, PttKeys.Select(k => k.Label).ToList(), _settings.PushToTalkKey, 0);
        _pttVk = PttKeys[PttKeyCombo.SelectedIndex].Vk;
        PttKeyCombo.SelectionChanged += (_, _) => _pttVk = PttKeys[Math.Max(0, PttKeyCombo.SelectedIndex)].Vk;
        PttCheck.IsChecked = _pttEnabled = _settings.PushToTalk;
        PttCheck.Click += (_, _) => _pttEnabled = PttCheck.IsChecked == true;

        VoiceCheck.IsChecked = _settings.SpanishVoice;
        OriginalCheck.IsChecked = _settings.ShowOriginal;
        SendVoiceCheck.IsChecked = _settings.SendVoice;
        InitSuggestions();

        if (Paths.VoiceSample is null)
        {
            CloneCheck.IsEnabled = false;
            CloneHint.Text = "(falta models/mi-voz.wav)";
        }
        else
        {
            CloneCheck.IsChecked = _settings.Clone;
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) => UpdateIndicator();
        timer.Start();
    }

    private static void Select(ComboBox box, IList<string> names, string? saved, int fallback)
    {
        var i = saved is null ? -1 : names.IndexOf(saved);
        box.SelectedIndex = i >= 0 ? i : Math.Clamp(fallback, 0, Math.Max(0, names.Count - 1));
    }

    // ───────────────────────── ventana, atajos, ajustes ─────────────────────────

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hotkeys = new HotkeyManager(this);
            const uint mods = NativeKeys.ModControl | NativeKeys.ModAlt;
            var failed = new List<string>();
            for (var i = 1; i <= 9; i++)
                if (!_hotkeys.Register(i, mods, (uint)(0x60 + i))) failed.Add($"Numpad{i}");
            if (!_hotkeys.Register(HotkeyMute, mods, 0x60)) failed.Add("Numpad0");
            if (!_hotkeys.Register(HotkeyCompact, mods, 0x6E)) failed.Add("Numpad.");
            if (!_hotkeys.Register(HotkeyNextPage, mods, 0x6B)) failed.Add("Numpad+");
            if (!_hotkeys.Register(HotkeyConfirm, mods, 0x0D)) failed.Add("Enter");
            if (!_hotkeys.Register(HotkeyNextSuggestion, mods, 0x6A)) failed.Add("Numpad*");
            _hotkeys.Pressed += id => Dispatcher.Invoke(() => OnHotkey(id));
            if (failed.Count > 0) SetStatus("Atajos ocupados por otra aplicación: Ctrl+Alt+" + string.Join(", ", failed));
        }
        catch (Exception ex)
        {
            SetStatus("No se pudieron registrar los atajos: " + ex.Message);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePageLabel();
        if (_settings.Compact) ToggleCompact();

        // Se calienta el servicio de traducción al abrir, para que el primer uso no tenga que esperarlo.
        try
        {
            SetStatus("Preparando el servicio de traducción…");
            await _mt.StartAsync(CancellationToken.None);
            SetStatus("Listo. Usa «Iniciar todo» o los botones Escuchar y Hablar.");
        }
        catch (Exception ex)
        {
            SetStatus("El servicio de traducción no arrancó: " + ex.Message);
        }
    }

    private void OnHotkey(int id)
    {
        if (id >= 1 && id <= 9) PlayPhraseAt(_phrasePage * PageSize + id - 1);
        else if (id == HotkeyMute) ToggleMute();
        else if (id == HotkeyCompact) ToggleCompact();
        else if (id == HotkeyNextPage) NextPage();
        else if (id == HotkeyConfirm) ConfirmSuggestion();
        else if (id == HotkeyNextSuggestion) NextSuggestion();
    }

    private void SaveSettings()
    {
        string? Name(ComboBox c) => c.SelectedItem as string;
        _settings.Capture = Name(CaptureCombo);
        _settings.Speak = Name(SpeakCombo);
        _settings.Mic = Name(MicCombo);
        _settings.Send = Name(SendCombo);
        _settings.Monitor = Name(MonitorCombo);
        _settings.Voice = Name(VoiceCombo);
        _settings.SpanishVoice = VoiceCheck.IsChecked == true;
        _settings.ShowOriginal = OriginalCheck.IsChecked == true;
        _settings.SendVoice = SendVoiceCheck.IsChecked == true;
        _settings.Clone = CloneCheck.IsChecked == true;
        _settings.PushToTalk = _pttEnabled;
        _settings.PushToTalkKey = Name(PttKeyCombo);
        _settings.Compact = _compact;
        _settings.AnswerMode = _answerMode;
        _settings.VoiceCommands = _voiceCommandsOn;
        _settings.Save();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        _hotkeys?.Dispose();
        _bankWindow?.Close();
        _listen?.Dispose();
        _talk?.Dispose();
        _phrasePlayer.Dispose();
        _phraseAudio.Dispose();
        _mt.Dispose();
    }

    // ───────────────────────── silencio y modo compacto ─────────────────────────

    private void MuteButton_Click(object sender, RoutedEventArgs e) => ToggleMute();

    /// <summary>Silencio de emergencia: corta lo que suena y bloquea todo envío hasta que se reanude.</summary>
    private void ToggleMute()
    {
        _muted = !_muted;
        _phrasePlayer.Muted = _muted;
        if (_talk is not null) _talk.Muted = _muted;
        if (_muted)
        {
            _phrasePlayer.Clear();
            _talk?.ClearPlayback();
            ClearSuggestion();
        }
        MuteButton.Content = _muted ? "Reanudar" : "Silenciar";
        SetStatus(_muted ? "SILENCIO: no se envía nada hasta que reanudes." : "Envío reanudado.");
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e) => ToggleCompact();

    private void ToggleCompact()
    {
        _compact = !_compact;
        if (_compact)
        {
            _normalSize = new Size(Width, Height);
            ConfigPanel.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;
            Width = 620;
            Height = 330;
            Opacity = 0.93;
            CompactButton.Content = "Normal";
        }
        else
        {
            ConfigPanel.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            if (_normalSize.Width > 0) { Width = _normalSize.Width; Height = _normalSize.Height; }
            Opacity = 1;
            CompactButton.Content = "Compacto";
        }
    }

    // ───────────────────────── indicador "¿ya puedo hablar?" ─────────────────────────

    private void UpdateIndicator()
    {
        static Brush Solid(byte r, byte g, byte b) => new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));

        if (_muted)
        {
            TalkBar.Value = 0;
            TalkIndicator.Background = Solid(0x45, 0x45, 0x45);
            TalkIndicatorText.Text = "SILENCIO — no se envía nada  (Ctrl+Alt+Numpad0 para reanudar)";
            return;
        }

        var phraseLeft = _phrasePlayer.PendingSeconds;
        if (_talk is null && phraseLeft <= 0.05 && _phraseBusy == 0)
        {
            _speakPeak = 0;
            TalkBar.Value = 0;
            TalkIndicator.Background = Solid(0x2C, 0x30, 0x38);
            TalkIndicatorText.Text = "Pulsa «Hablar» o «Iniciar todo» para empezar";
            return;
        }

        var (state, talkLeft) = _talk?.Snapshot() ?? (Activity.Ready, 0);
        var left = Math.Max(talkLeft, phraseLeft);
        if (left > 0.05) state = Activity.Speaking;
        else if (_phraseBusy > 0 && state != Activity.Processing) state = Activity.Processing;

        if (state == Activity.Speaking) _speakPeak = Math.Max(_speakPeak, left); else _speakPeak = 0;
        TalkBar.Value = state == Activity.Speaking && _speakPeak > 0 ? left / _speakPeak : 0;

        var pttHeld = _pttEnabled && NativeKeys.IsDown(_pttVk);
        var keyName = PttKeys[Math.Max(0, PttKeyCombo.SelectedIndex)].Label;

        switch (state)
        {
            case Activity.Speaking:
                TalkIndicator.Background = Solid(0x5A, 0x1E, 0x1B);
                TalkIndicatorText.Text = $"Ellos te están oyendo — espera {left:F1} s";
                break;
            case Activity.Processing:
                TalkIndicator.Background = Solid(0x6B, 0x4E, 0x00);
                TalkIndicatorText.Text = _phraseBusy > 0 ? "Preparando la frase… espera" : "Traduciendo… espera";
                break;
            default:
                if (_pttEnabled && pttHeld)
                {
                    TalkIndicator.Background = Solid(0x1F, 0x4F, 0x8A);
                    TalkIndicatorText.Text = "Te escucho… suelta la tecla al terminar";
                }
                else if (_pttEnabled)
                {
                    TalkIndicator.Background = Solid(0x1E, 0x6B, 0x38);
                    TalkIndicatorText.Text = $"✔ Mantén «{keyName}» para hablar";
                }
                else if (state == Activity.Hearing)
                {
                    TalkIndicator.Background = Solid(0x1F, 0x4F, 0x8A);
                    TalkIndicatorText.Text = "Te escucho…";
                }
                else
                {
                    TalkIndicator.Background = Solid(0x1E, 0x6B, 0x38);
                    TalkIndicatorText.Text = "✔ Puedes hablar";
                }
                break;
        }
    }

    // ───────────────────────── escuchar / hablar ─────────────────────────

    private async void ListenButton_Click(object sender, RoutedEventArgs e) => await ToggleListenAsync();

    private async void TalkButton_Click(object sender, RoutedEventArgs e) => await ToggleTalkAsync();

    private async void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        StartAllButton.IsEnabled = false;
        try
        {
            if (_listen is null) await ToggleListenAsync();
            if (_talk is null) await ToggleTalkAsync();
        }
        finally { StartAllButton.IsEnabled = true; }
    }

    private async Task ToggleListenAsync()
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

        _listen = await StartPipelineAsync(ListenButton, options, TheirsColor, OnTheirUtterance);
        if (_listen is not null)
        {
            ListenButton.Content = "Detener";
            CaptureCombo.IsEnabled = SpeakCombo.IsEnabled = VoiceCheck.IsEnabled = false;
        }
    }

    private async Task ToggleTalkAsync()
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
        var outputs = SendVoiceCheck.IsChecked == true ? CurrentOutputs() : new List<MMDevice>();

        var options = new PipelineOptions(mic, false, "es-en", _voices[VoiceCombo.SelectedIndex], outputs,
            CloneVoice: () => Dispatcher.Invoke(() => CloneCheck.IsChecked == true),
            PushToTalk: () => !_pttEnabled || NativeKeys.IsDown(_pttVk),
            Command: HandleVoiceCommand);

        _talk = await StartPipelineAsync(TalkButton, options, MineColor);
        if (_talk is not null)
        {
            _talk.Muted = _muted;
            TalkButton.Content = "Detener";
            MicCombo.IsEnabled = SendCombo.IsEnabled = MonitorCombo.IsEnabled = VoiceCombo.IsEnabled = SendVoiceCheck.IsEnabled = false;
        }
    }

    private async Task<TranslationPipeline?> StartPipelineAsync(Button button, PipelineOptions options, Brush color,
        Action<Utterance>? onUtterance = null)
    {
        button.IsEnabled = false;
        try
        {
            SetStatus("Iniciando servicio de traducción…");
            await _mt.StartAsync(CancellationToken.None);

            SetStatus("Cargando modelos de voz…");
            var pipeline = await Task.Run(() => new TranslationPipeline(_mt, options));
            pipeline.Heard += u => Dispatcher.Invoke(() => { OnHeard(u, color); onUtterance?.Invoke(u); });
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

    private void OnHeard(Utterance u, Brush color)
    {
        _lines.Add(new Line
        {
            Original = u.Original,
            Translation = u.Translation,
            Color = color,
            OriginalVisibility = OriginalCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
        });
        while (_lines.Count > MaxLines) _lines.RemoveAt(0);
        Scroller.ScrollToEnd();
        SetStatus($"Última frase: {u.LatencySeconds:F1} s en total  ({u.Timings})");
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // ───────────────────────── banco de frases ─────────────────────────

    private void BankButton_Click(object sender, RoutedEventArgs e)
    {
        if (_bankWindow is { IsLoaded: true }) { _bankWindow.Activate(); return; }
        _bankWindow = new PhraseBankWindow(this) { Owner = this };
        _bankWindow.Show();
    }

    private List<MMDevice> CurrentOutputs()
    {
        var outputs = new List<MMDevice> { _renderDevices[SendCombo.SelectedIndex] };
        if (MonitorCombo.SelectedIndex > 0) outputs.Add(_renderDevices[MonitorCombo.SelectedIndex - 1]);
        return outputs;
    }

    /// <summary>Voz base y uso de mi timbre que se aplican a las frases preparadas.</summary>
    internal (TtsSpec Voice, bool Clone) CurrentVoice() =>
        (_voices[Math.Max(0, VoiceCombo.SelectedIndex)], CloneCheck.IsChecked == true && Paths.VoiceSample is not null);

    internal bool IsPhraseReady(Phrase p) { var (v, c) = CurrentVoice(); return _phraseAudio.IsReady(p, v, c); }

    internal async Task PreparePhraseAsync(Phrase p)
    {
        var (v, c) = CurrentVoice();
        Interlocked.Increment(ref _phraseBusy);
        try { await _phraseAudio.GetAsync(p, v, c); }
        finally { Interlocked.Decrement(ref _phraseBusy); }
    }

    internal async Task<string> TranslateToEnglishAsync(string spanish)
    {
        await _mt.StartAsync(CancellationToken.None);
        return await _mt.TranslateAsync("es-en", spanish);
    }

    private void PlayPhraseAt(int index)
    {
        if (index < Bank.Items.Count) _ = PlayPhraseAsync(Bank.Items[index]);
        else SetStatus($"No hay frase en ese atajo (página {_phrasePage + 1}).");
    }

    private int PageCount => Math.Max(1, (Bank.Items.Count + PageSize - 1) / PageSize);

    /// <summary>Pasa a la página siguiente de atajos (Ctrl+Alt+Numpad+): la página 2 son las frases 10 a 18, y así.</summary>
    private void NextPage()
    {
        _phrasePage = (_phrasePage + 1) % PageCount;
        UpdatePageLabel();
        var first = _phrasePage * PageSize;
        var names = Bank.Items.Skip(first).Take(PageSize).Select((p, i) => $"{i + 1} {p.Label}");
        SetStatus($"Página {_phrasePage + 1} de {PageCount}:  " + string.Join("  ·  ", names));
    }

    private void UpdatePageLabel() => BankButton.Content = PageCount > 1 ? $"Frases p{_phrasePage + 1}/{PageCount}" : "Frases";

    /// <summary>Etiqueta del atajo de la frase en la posición dada: "p2·3" = página 2, tecla 3.</summary>
    internal static string KeyLabel(int index) => $"p{index / PageSize + 1}·{index % PageSize + 1}";

    /// <summary>Se llama al reordenar o editar el banco, para mantener la página actual dentro de rango.</summary>
    internal void BankChanged()
    {
        _phrasePage = Math.Min(_phrasePage, PageCount - 1);
        UpdatePageLabel();
    }

    /// <summary>Envía una frase preparada por el micrófono virtual (al instante si ya está en caché).</summary>
    internal async Task PlayPhraseAsync(Phrase p)
    {
        if (_muted) { SetStatus("Silencio activo: no se envía la frase."); return; }
        if (SendCombo.SelectedIndex < 0) { SetStatus("Elige el dispositivo de envío (micrófono virtual)."); return; }

        var (voice, clone) = CurrentVoice();
        var devices = CurrentOutputs();
        if (!_phraseAudio.IsReady(p, voice, clone)) SetStatus($"Preparando «{p.Label}»… la primera vez tarda unos segundos.");

        Interlocked.Increment(ref _phraseBusy);
        try
        {
            var (samples, rate) = await _phraseAudio.GetAsync(p, voice, clone);
            if (_muted) return;
            _phrasePlayer.Play(samples, rate, devices);
            SetStatus($"Frase enviada: «{p.Label}»");
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo enviar la frase: " + ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _phraseBusy);
        }
    }
}
