using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>Кольцевой журнал диалога с движком.</summary>
public sealed class UciLogTests
{
    [Fact(DisplayName = "Журнал UCI: буфер, хвост и признак info")]
    public void Буфер()
    {
        var log = new UciLog(capacity: 3);
        var added = new List<UciLogEntry>();
        log.EntryAdded += (_, entry) => added.Add(entry);

        log.Add("> uci");
        log.Add("< uciok");
        log.Add("> go infinite");
        log.Add("< info depth 3 pv e2e4");

        var snapshot = log.Snapshot();
        Assert.Equal(3, snapshot.Length);  // старые записи вытеснены
        Assert.Equal("< uciok", snapshot[0].Text);  // первая уехала за границу буфера
        Assert.Equal(4, added.Count);  // подписчик видел все записи

        Assert.Equal(new[] { "> go infinite", "< info depth 3 pv e2e4" },
            log.Tail(2).Select(e => e.Text).ToArray());  // хвост для отчёта о зависании

        Assert.True(snapshot[2].IsInfo, "строка анализа распознана");
        Assert.False(snapshot[1].IsInfo, "команда — не анализ");
        Assert.StartsWith(snapshot[1].Time.ToString("HH:mm:ss"), snapshot[1].ToString());  // время в строке
        Assert.EndsWith("> go infinite", snapshot[1].ToString());  // и сама строка

        log.Clear();
        Assert.Empty(log.Snapshot());
    }

    [Fact(DisplayName = "Журнал UCI: подписка на движок")]
    public async Task ПодпискаНаДвижок()
    {
        var log = new UciLog();
        using var engine = new UciEngine();
        engine.LogReceived += log.Append;

        await engine.StartAsync(UciEngineTests.EnginePath, TestContext.Current.CancellationToken);

        var texts = log.Snapshot().Select(e => e.Text).ToArray();
        Assert.Contains("> uci", texts);  // отправленные команды
        Assert.Contains("< uciok", texts);  // и ответы движка
        Assert.True(Array.IndexOf(texts, "> uci") < Array.IndexOf(texts, "< uciok"),
            "порядок записей сохранён");
    }
}
