using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace JmTranslate.App;

/// <summary>Arranca el servicio Python de traducción y lo consulta por localhost.</summary>
internal sealed class MtSidecar : IDisposable
{
    private const int Port = 5005;
    private readonly HttpClient _http = new() { BaseAddress = new Uri($"http://127.0.0.1:{Port}"), Timeout = TimeSpan.FromSeconds(60) };
    private Process? _process;

    public async Task StartAsync(CancellationToken ct)
    {
        if (await IsUpAsync()) return;

        var python = Path.Combine(Paths.Tools, ".venv", "Scripts", "python.exe");
        _process = Process.Start(new ProcessStartInfo
        {
            FileName = python,
            ArgumentList = { Path.Combine(Paths.Tools, "mt_server.py"), Port.ToString() },
            WorkingDirectory = Paths.Tools,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment = { ["PYTHONIOENCODING"] = "utf-8" },
        }) ?? throw new InvalidOperationException("No se pudo iniciar el servicio de traducción.");

        for (var i = 0; i < 120; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (_process.HasExited) throw new InvalidOperationException("El servicio de traducción se cerró al iniciar.");
            if (await IsUpAsync()) return;
            await Task.Delay(500, ct);
        }
        throw new TimeoutException("El servicio de traducción no respondió a tiempo.");
    }

    private async Task<bool> IsUpAsync()
    {
        try { return (await _http.GetAsync("/health")).IsSuccessStatusCode; }
        catch { return false; }
    }

    /// <param name="pair">"en-es" o "es-en".</param>
    public async Task<string> TranslateAsync(string pair, string text)
    {
        var body = new StringContent(JsonSerializer.Serialize(new { pair, text }), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/translate", body);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("text").GetString() ?? "";
    }

    /// <summary>Compara una pregunta con las preguntas típicas de cada candidato, por significado. Devuelve (id, parecido 0..1) de mayor a menor.</summary>
    public async Task<List<(string Id, double Score)>> MatchAsync(string query, IEnumerable<(string Id, IReadOnlyList<string> Texts)> candidates)
    {
        var payload = new { query, candidates = candidates.Select(c => new { id = c.Id, texts = c.Texts }) };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync("/match", body);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString() ?? "", r.GetProperty("score").GetDouble())).ToList();
    }

    /// <summary>Cambia el timbre de la voz sintética por el de models/mi-voz.*. Tarda ~0.65× la duración del audio.</summary>
    public async Task<float[]> CloneAsync(float[] samples, int sampleRate, string voiceId)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        using var content = new ByteArrayContent(bytes);
        content.Headers.Add("X-Sample-Rate", sampleRate.ToString());
        content.Headers.Add("X-Voice-Id", voiceId);
        using var resp = await _http.PostAsync("/clone", content);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadAsByteArrayAsync();
        var floats = new float[result.Length / sizeof(float)];
        Buffer.BlockCopy(result, 0, floats, 0, result.Length);
        return floats;
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { /* ya cerrado */ }
        _http.Dispose();
    }
}
