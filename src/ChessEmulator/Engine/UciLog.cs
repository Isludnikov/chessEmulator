namespace ChessEmulator.Engine;

/// <summary>Одна запись журнала: время и строка диалога с движком.</summary>
/// <remarks>
/// Префикс строки говорит о направлении: "&gt; " — команда движку, "&lt; " — ответ движка,
/// "! " — сообщение об ошибке, "* " — событие самого приложения.
/// </remarks>
public readonly record struct UciLogEntry(DateTime Time, string Text)
{
    /// <summary>Строка анализа. Их сотни в секунду, поэтому в окне они скрыты по умолчанию.</summary>
    public bool IsInfo => Text.StartsWith("< info", StringComparison.Ordinal);

    public override string ToString() => Time.ToString("HH:mm:ss.fff ") + Text;
}

/// <summary>
/// Кольцевой буфер последних строк диалога с движком. Нужен, чтобы сбой протокола было видно
/// сразу: в окне «Журнал UCI…» и в отчёте, который сохраняется при зависании движка.
/// </summary>
public sealed class UciLog
{
    private readonly object _gate = new();
    private readonly Queue<UciLogEntry> _entries = new();

    public UciLog(int capacity = 3000) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }

    public event EventHandler<UciLogEntry>? EntryAdded;

    /// <summary>Подписчик <see cref="UciEngine.LogReceived"/>.</summary>
    public void Append(object? sender, string line) => Add(line);

    public void Add(string text)
    {
        var entry = new UciLogEntry(DateTime.Now, text);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
        EntryAdded?.Invoke(this, entry);
    }

    public UciLogEntry[] Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    /// <summary>Последние <paramref name="count"/> записей — для отчёта о зависании.</summary>
    public UciLogEntry[] Tail(int count)
    {
        lock (_gate) return _entries.Skip(Math.Max(0, _entries.Count - count)).ToArray();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}
