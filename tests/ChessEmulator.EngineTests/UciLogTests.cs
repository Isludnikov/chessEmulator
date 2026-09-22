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
        Assert.StartsWith(snapshot[1].Time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), snapshot[1].ToString());  // время в строке
        Assert.EndsWith("> go infinite", snapshot[1].ToString());  // и сама строка

        log.Clear();
        Assert.Empty(log.Snapshot());
    }

    [Fact(DisplayName = "Журнал UCI: хвост на границах")]
    public void ХвостНаГраницах()
    {
        var log = new UciLog(capacity: 3);
        log.Add("один");
        log.Add("два");

        Assert.Empty(log.Tail(0));  // нулевой хвост пуст
        Assert.Empty(log.Tail(-5));  // отрицательный хвост тоже, и без исключения
        Assert.Equal(2, log.Tail(int.MaxValue).Length);  // просьба о лишнем даёт всё, что есть
        Assert.Equal("два", log.Tail(1)[0].Text);  // хвост берётся с конца

        Assert.Equal(1, new UciLog(capacity: 0).Capacity);  // нулевая вместимость поднимается до одной
        Assert.Equal(1, new UciLog(capacity: -7).Capacity);  // отрицательная тоже

        var tiny = new UciLog(capacity: 1);
        tiny.Add("первая");
        tiny.Add("вторая");
        Assert.Equal(new[] { "вторая" }, tiny.Snapshot().Select(e => e.Text).ToArray());  // буфер на одну запись
    }

    [Fact(DisplayName = "Журнал UCI: порядок записей при записи из двух потоков")]
    public async Task ПорядокПриЗаписиИзДвухПотоков()
    {
        // Движок пишет в журнал из двух потоков сразу — из stdout и stderr. Если событие
        // поднимать вне блокировки, окно журнала увидит не тот порядок, что в буфере,
        // а по порядку записей и разбирают зависания.
        var log = new UciLog(capacity: 1000);
        var notified = new List<string>();
        log.EntryAdded += (_, entry) => notified.Add(entry.Text);

        var ready = new ManualResetEventSlim();
        var writers = Enumerable.Range(0, 2).Select(w => Task.Run(() =>
        {
            ready.Wait();
            for (var i = 0; i < 200; i++) log.Add($"поток {w} строка {i}");
        })).ToArray();

        ready.Set();
        await Task.WhenAll(writers);

        var buffered = log.Snapshot().Select(e => e.Text).ToArray();
        Assert.Equal(400, buffered.Length);  // ни одна запись не потерялась
        Assert.Equal(buffered, notified.ToArray());  // и подписчик увидел тот же порядок
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
