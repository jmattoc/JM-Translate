using System.Windows;

namespace JmTranslate.App;

/// <summary>
/// Respuestas del banco sin tener que buscarlas:
///  · Sugerencia: al oír una pregunta que encaja con el banco, muestra la respuesta (Ctrl+Alt+Enter la envía, Ctrl+Alt+* ofrece otra).
///  · Automático: la envía sola tras una cuenta atrás que se cancela con Ctrl+Alt+*.
///  · Comando de voz: «banco, saludo» envía esa frase y no se traduce.
/// </summary>
public partial class MainWindow
{
    private const int HotkeyConfirm = 13, HotkeyNextSuggestion = 14;
    private const double CountdownSeconds = 2.5;

    private List<Suggestion> _suggestions = new();
    private int _suggestIndex;
    private string _suggestQuestion = "";
    private DateTime _suggestAt;

    private Phrase? _autoPhrase;          // respuesta que se enviará sola
    private DateTime _autoAt;             // cuándo
    private DateTime _autoLimit;          // hasta cuándo se espera si siguen hablando

    private int _answerMode;              // 0 manual, 1 sugerir, 2 automático
    private volatile bool _voiceCommandsOn = true;
    private readonly System.Windows.Threading.DispatcherTimer _suggestTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private void InitSuggestions()
    {
        ModeCombo.ItemsSource = new[] { "Manual (sin sugerencias)", "Sugerir (recomendado)", "Automático (con cuenta atrás)" };
        ModeCombo.SelectedIndex = _answerMode = Math.Clamp(_settings.AnswerMode, 0, 2);
        ModeCombo.SelectionChanged += (_, _) =>
        {
            _answerMode = Math.Max(0, ModeCombo.SelectedIndex);
            if (_answerMode == 0) ClearSuggestion();
        };

        VoiceCmdCheck.IsChecked = _voiceCommandsOn = _settings.VoiceCommands;
        VoiceCmdCheck.Click += (_, _) => _voiceCommandsOn = VoiceCmdCheck.IsChecked == true;

        _suggestTimer.Tick += (_, _) => SuggestionTick();
        _suggestTimer.Start();
    }

    // ───────────────────────── sugerencias a partir de la pregunta ─────────────────────────

    /// <summary>Se llama con cada frase que dicen ellos (ya reconocida).</summary>
    private void OnTheirUtterance(Utterance u)
    {
        if (_answerMode == 0 || _muted) return;
        if (u.Original.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3) return; // «Okay», «Thanks»…
        _ = SuggestAsync(u.Original);
    }

    private async Task SuggestAsync(string question)
    {
        try
        {
            var ranked = await PhraseMatcher.SuggestAsync(_mt, Bank, question);
            var good = ranked.Where(s => s.Score >= PhraseMatcher.SuggestMin).Take(3).ToList();
            if (good.Count == 0) return; // nada del banco encaja: se conserva lo anterior

            _suggestions = good;
            _suggestIndex = 0;
            _suggestQuestion = question;
            _suggestAt = DateTime.Now;
            _autoPhrase = null;
            ShowSuggestion();

            var clear = good.Count == 1 || good[0].Score - good[1].Score >= PhraseMatcher.AutoMargin;
            if (_answerMode == 2 && good[0].Score >= PhraseMatcher.AutoMin && clear && good[0].Phrase.AutoSend)
            {
                _autoPhrase = good[0].Phrase;
                _autoAt = DateTime.Now.AddSeconds(CountdownSeconds);
                _autoLimit = DateTime.Now.AddSeconds(10);
            }
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo sugerir una respuesta: " + ex.Message);
        }
    }

    private void ShowSuggestion()
    {
        if (_suggestions.Count == 0) { SuggestBorder.Visibility = Visibility.Collapsed; return; }
        var s = _suggestions[_suggestIndex];
        var asked = _suggestQuestion.Length > 90 ? _suggestQuestion[..90] + "…" : _suggestQuestion;
        var others = _suggestions.Count > 1 ? $" · Ctrl+Alt+* otra ({_suggestIndex + 1}/{_suggestions.Count})" : "";
        SuggestText.Text = $"Preguntaron: «{asked}»\nSugerencia: «{s.Phrase.Label}»  ({s.Score:P0})   —   Ctrl+Alt+Enter envía{others}";
        SuggestBorder.Visibility = Visibility.Visible;
    }

    private void ClearSuggestion()
    {
        _suggestions = new();
        _autoPhrase = null;
        SuggestBorder.Visibility = Visibility.Collapsed;
    }

    private void SuggestionTick()
    {
        if (_suggestions.Count == 0) return;
        if (DateTime.Now - _suggestAt > TimeSpan.FromSeconds(60)) { ClearSuggestion(); return; }
        if (_autoPhrase is null) return;

        // Si ya estás hablando tú, no se envía nada sola: queda como sugerencia manual.
        var mine = _talk?.Snapshot().State;
        if (mine is Activity.Hearing or Activity.Speaking)
        {
            _autoPhrase = null;
            ShowSuggestion();
            return;
        }

        var remaining = (_autoAt - DateTime.Now).TotalSeconds;
        // Si ellos siguen hablando (otra pregunta o aclaración), se espera un poco más.
        if (remaining <= 0 && _listen?.Snapshot().State == Activity.Hearing && DateTime.Now < _autoLimit)
        {
            _autoAt = DateTime.Now.AddSeconds(0.8);
            remaining = 0.8;
        }

        if (remaining > 0)
        {
            SuggestText.Text = $"Enviando «{_autoPhrase.Label}» en {remaining:F1} s…   Ctrl+Alt+* cancela  ·  Ctrl+Alt+Enter envía ya";
            return;
        }

        var phrase = _autoPhrase;
        ClearSuggestion();
        _ = PlayPhraseAsync(phrase);
    }

    private void ConfirmSuggestion()
    {
        if (_suggestions.Count == 0) { SetStatus("No hay ninguna sugerencia pendiente."); return; }
        var phrase = _autoPhrase ?? _suggestions[_suggestIndex].Phrase;
        ClearSuggestion();
        _ = PlayPhraseAsync(phrase);
    }

    private void NextSuggestion()
    {
        if (_suggestions.Count == 0) return;
        if (_autoPhrase is not null)
        {
            _autoPhrase = null; // cancela el envío automático
            ShowSuggestion();
            SetStatus("Envío automático cancelado.");
            return;
        }
        _suggestIndex = (_suggestIndex + 1) % _suggestions.Count;
        ShowSuggestion();
    }

    // ───────────────────────── comandos de voz ─────────────────────────

    /// <summary>Lo llama el pipeline «yo → ellos» con cada frase mía. true = era un comando y no se traduce.</summary>
    private bool HandleVoiceCommand(string text)
    {
        if (!_voiceCommandsOn) return false;
        return Dispatcher.Invoke(() =>
        {
            var cmd = VoiceCommands.Parse(text, Bank.Items);
            if (!cmd.IsCommand) return false;

            var phrase = cmd.Phrase;
            if (phrase is null && cmd.Number is int n)
            {
                var index = _phrasePage * PageSize + n - 1;
                if (index < Bank.Items.Count) phrase = Bank.Items[index];
            }
            if (phrase is null)
            {
                SetStatus($"Comando de voz «banco {cmd.Spoken}»: no encontré esa respuesta en el banco.");
                return true;
            }

            SetStatus($"Comando de voz: «{phrase.Label}»");
            _ = PlayPhraseAsync(phrase);
            return true;
        });
    }
}
