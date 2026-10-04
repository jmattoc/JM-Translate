using System.Windows;
using System.Windows.Controls;

namespace JmTranslate.App;

public partial class PhraseBankWindow : Window
{
    private readonly MainWindow _main;
    private Phrase? _current;
    private bool _loading;

    internal PhraseBankWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Refresh(select: 0);
    }

    private PhraseBank Bank => _main.Bank;

    /// <summary>Vuelve a dibujar la lista: "1 · Nombre ✔" (✔ = ya preparada con la voz actual).</summary>
    private void Refresh(int? select = null)
    {
        _loading = true;
        var keep = select ?? Math.Max(0, List.SelectedIndex);
        List.Items.Clear();
        for (var i = 0; i < Bank.Items.Count; i++)
        {
            var p = Bank.Items[i];
            var key = i < 9 ? $"{i + 1}" : "·";
            List.Items.Add($"{key}  {p.Label}{(_main.IsPhraseReady(p) ? "  ✔" : "")}");
        }
        _loading = false;
        if (Bank.Items.Count > 0) List.SelectedIndex = Math.Min(keep, Bank.Items.Count - 1);
        else ShowPhrase(null);
    }

    private void ShowPhrase(Phrase? p)
    {
        _current = p;
        LabelBox.Text = p?.Label ?? "";
        EsBox.Text = p?.Es ?? "";
        EnBox.Text = p?.En ?? "";
        UpdateReadyText();
    }

    private void UpdateReadyText()
    {
        if (_current is null) { ReadyText.Text = ""; return; }
        var (voice, clone) = _main.CurrentVoice();
        ReadyText.Text = _main.IsPhraseReady(_current)
            ? $"✔ Preparada con «{voice.Label}»{(clone ? " y tu voz" : "")}: suena al instante."
            : $"Sin preparar con «{voice.Label}»{(clone ? " y tu voz" : "")}: la primera vez tardará unos segundos.";
    }

    private void SaveFields()
    {
        if (_current is null) return;
        _current.Label = LabelBox.Text.Trim();
        _current.Es = EsBox.Text.Trim();
        _current.En = EnBox.Text.Trim();
        Bank.Save();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveFields();
        var i = List.SelectedIndex;
        ShowPhrase(i >= 0 && i < Bank.Items.Count ? Bank.Items[i] : null);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        Bank.Items.Add(new Phrase { Label = "Nueva respuesta" });
        Bank.Save();
        Refresh(select: Bank.Items.Count - 1);
        LabelBox.Focus();
        LabelBox.SelectAll();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (MessageBox.Show(this, $"¿Eliminar «{_current.Label}»?", "Banco de frases", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var index = List.SelectedIndex;
        Bank.Items.Remove(_current);
        _current = null;
        Bank.Save();
        Refresh(select: Math.Max(0, index - 1));
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        Refresh();
        StatusText.Text = "Guardado.";
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        var spanish = EsBox.Text.Trim();
        if (spanish.Length == 0) { StatusText.Text = "Escribe primero la frase en español."; return; }
        TranslateButton.IsEnabled = false;
        StatusText.Text = "Traduciendo…";
        try
        {
            EnBox.Text = await _main.TranslateToEnglishAsync(spanish);
            StatusText.Text = "Traducido. Revisa el inglés y corrígelo si hace falta.";
        }
        catch (Exception ex) { StatusText.Text = "No se pudo traducir: " + ex.Message; }
        finally { TranslateButton.IsEnabled = true; }
    }

    private async void PrepareButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (_current is null || _current.En.Length == 0) { StatusText.Text = "Falta el texto en inglés."; return; }
        PrepareButton.IsEnabled = false;
        StatusText.Text = "Preparando el audio…";
        try
        {
            await _main.PreparePhraseAsync(_current);
            StatusText.Text = "Audio preparado.";
        }
        catch (Exception ex) { StatusText.Text = "No se pudo preparar: " + ex.Message; }
        finally { PrepareButton.IsEnabled = true; Refresh(); }
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (_current is null || _current.En.Length == 0) { StatusText.Text = "Falta el texto en inglés."; return; }
        await _main.PlayPhraseAsync(_current);
        Refresh();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (_current is null || _current.En.Length == 0) { StatusText.Text = "Falta el texto en inglés."; return; }
        Clipboard.SetText(_current.En);
        StatusText.Text = "Inglés copiado: pégalo en el chat de la reunión.";
    }

    private async void PrepareAllButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        PrepareAllButton.IsEnabled = false;
        try
        {
            var pending = Bank.Items.Where(p => p.En.Length > 0 && !_main.IsPhraseReady(p)).ToList();
            for (var i = 0; i < pending.Count; i++)
            {
                StatusText.Text = $"Preparando {i + 1} de {pending.Count}: {pending[i].Label}…";
                await _main.PreparePhraseAsync(pending[i]);
                Refresh();
            }
            StatusText.Text = pending.Count == 0 ? "Todas estaban preparadas." : $"Listo: {pending.Count} frases preparadas.";
        }
        catch (Exception ex) { StatusText.Text = "Se detuvo: " + ex.Message; }
        finally { PrepareAllButton.IsEnabled = true; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveFields();
        base.OnClosing(e);
    }
}
