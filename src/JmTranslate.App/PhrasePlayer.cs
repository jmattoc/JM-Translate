using NAudio.CoreAudioApi;

namespace JmTranslate.App;

/// <summary>Reproduce frases preparadas por los mismos dispositivos que la traducción (micrófono virtual y, si quieres, tus audífonos).</summary>
internal sealed class PhrasePlayer : IDisposable
{
    private readonly Dictionary<(string DeviceId, int Rate), SpeechPlayer> _players = new();

    public bool Muted { get; set; }

    /// <summary>Segundos de frase que aún están sonando o en cola.</summary>
    public double PendingSeconds
    {
        get { lock (_players) return _players.Values.Select(p => p.PendingSeconds).DefaultIfEmpty(0).Max(); }
    }

    public void Play(float[] samples, int rate, IEnumerable<MMDevice> devices)
    {
        if (Muted) return;
        foreach (var device in devices)
        {
            SpeechPlayer player;
            lock (_players)
            {
                if (!_players.TryGetValue((device.ID, rate), out player!))
                    _players[(device.ID, rate)] = player = new SpeechPlayer(device, rate);
            }
            player.Enqueue(samples);
        }
    }

    public void Clear()
    {
        lock (_players) foreach (var p in _players.Values) p.Clear();
    }

    public void Dispose()
    {
        lock (_players)
        {
            foreach (var p in _players.Values) p.Dispose();
            _players.Clear();
        }
    }
}
