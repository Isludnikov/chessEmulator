using ChessEmulator.Chess;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>
/// Проверки главного правила протокола UCI: пока идёт поиск, движку нельзя посылать ничего,
/// кроме stop. Настоящий Stockfish на такой команде замирает навсегда — поддельный движок в
/// режиме test-mode strict ведёт себя точно так же.
/// </summary>
public sealed class UciEngineBlockingTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(10);

    private static async Task<UciEngine> StartAsync(string mode, List<string>? log = null)
    {
        // Без контекста синхронизации события приходят сразу, поэтому журнал команд и ответов
        // сохраняет настоящий порядок — на нём держится проверка инварианта протокола.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var engine = new UciEngine();
        SynchronizationContext.SetSynchronizationContext(previous);

        if (log != null) engine.LogReceived += (_, text) => { lock (log) log.Add(text); };
        await engine.StartAsync(UciEngineTests.EnginePath, TestContext.Current.CancellationToken);
        engine.Send("test-mode " + mode);
        return engine;
    }

    /// <summary>Ждёт, пока движок начнёт отдавать анализ, — значит поиск точно идёт.</summary>
    private static async Task WaitForSearchAsync(UciEngine engine)
    {
        for (var i = 0; i < 200 && !engine.IsSearching; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(engine.IsSearching, "поиск начался");
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "UCI: новая партия во время бесконечного поиска не вешает движок", Timeout = 30000)]
    public async Task НоваяПартияВоВремяПоиска()
    {
        var log = new List<string>();
        using var engine = await StartAsync("strict", log);

        var infinite = engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
            TestContext.Current.CancellationToken);
        await WaitForSearchAsync(engine);

        await engine.NewGameAsync(TestContext.Current.CancellationToken).WaitAsync(Short, TestContext.Current.CancellationToken);
        await infinite.WaitAsync(Short, TestContext.Current.CancellationToken);

        var next = await engine.GoAsync(Position.StartFen, new[] { "e2e4" }, SearchLimits.ByDepth(3),
            TestContext.Current.CancellationToken).WaitAsync(Short, TestContext.Current.CancellationToken);

        Assert.Equal("e2e4", next.BestMove);  // движок жив и отвечает
        Assert.False(engine.IsWedged, "движок не завис");

        // stop обязан уйти раньше ucinewgame — иначе настоящий Stockfish замер бы навсегда.
        var sent = SentCommands(log);
        var stopIndex = sent.IndexOf("stop");
        var newGameIndex = sent.IndexOf("ucinewgame");
        Assert.True(stopIndex >= 0 && stopIndex < newGameIndex, "stop отправлен перед ucinewgame");
    }

    [Fact(DisplayName = "UCI: параметры во время поиска не вешают движок", Timeout = 30000)]
    public async Task ПараметрыВоВремяПоиска()
    {
        var log = new List<string>();
        using var engine = await StartAsync("strict", log);

        var infinite = engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
            TestContext.Current.CancellationToken);
        await WaitForSearchAsync(engine);

        await engine.ApplyOptionsAsync(
            new[]
            {
                new KeyValuePair<string, string>("Hash", "32"),
                new KeyValuePair<string, string>("Такого параметра нет", "1")
            },
            TestContext.Current.CancellationToken).WaitAsync(Short, TestContext.Current.CancellationToken);
        await infinite.WaitAsync(Short, TestContext.Current.CancellationToken);

        var sent = SentCommands(log);
        Assert.Contains("setoption name Hash value 32", sent);  // поддерживаемый параметр выставлен
        Assert.DoesNotContain(sent, c => c.Contains("Такого параметра нет"));  // неизвестный пропущен
        Assert.True(sent.IndexOf("stop") < sent.IndexOf("setoption name Hash value 32"),
            "stop отправлен перед setoption");
        Assert.False(engine.IsWedged, "движок не завис");
    }

    [Fact(DisplayName = "UCI: очередь команд не пропускает чужие команды в идущий поиск", Timeout = 30000)]
    public async Task ОчередьКоманд()
    {
        var log = new List<string>();
        using var engine = await StartAsync("strict", log);

        var infinite = engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
            TestContext.Current.CancellationToken);
        await WaitForSearchAsync(engine);

        var newGame = engine.NewGameAsync(TestContext.Current.CancellationToken);
        var options = engine.ApplyOptionsAsync(
            new[] { new KeyValuePair<string, string>("MultiPV", "2") }, TestContext.Current.CancellationToken);
        var move = engine.GoAsync(Position.StartFen, null, SearchLimits.ByTime(50),
            TestContext.Current.CancellationToken);

        await Task.WhenAll(infinite, newGame, options, move).WaitAsync(Short, TestContext.Current.CancellationToken);

        Assert.Equal("e2e4", (await move).BestMove);  // последний поиск довёлся до конца
        Assert.False(engine.IsWedged, "движок не завис");

        // Главный инвариант: между go и его bestmove наружу уходит только stop.
        var timeline = Timeline(log);
        var searching = false;
        foreach (var entry in timeline)
        {
            if (entry.StartsWith("< bestmove", StringComparison.Ordinal)) searching = false;
            else if (entry.StartsWith("> go", StringComparison.Ordinal)) searching = true;
            else if (searching && entry.StartsWith("> ", StringComparison.Ordinal))
                Assert.Equal("> stop", entry);  // во время поиска допустим только stop
        }
    }

    [Fact(DisplayName = "UCI: сырая команда во время поиска вешает движок", Timeout = 30000)]
    public async Task СыраяКомандаВоВремяПоиска()
    {
        // Так вело себя приложение до исправления: ucinewgame уходил прямо в идущий анализ.
        // Проверяем, что поддельный движок в строгом режиме воспроизводит поведение Stockfish
        // (замирает навсегда), а обёртка это замечает и не ждёт ответа вечно.
        using var engine = await StartAsync("strict");
        engine.StopTimeout = TimeSpan.FromMilliseconds(500);
        engine.SilenceTimeout = TimeSpan.FromSeconds(2);

        var infinite = engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
            TestContext.Current.CancellationToken);
        await WaitForSearchAsync(engine);

        engine.Send("ucinewgame");   // запрещённый приём — движок больше не читает ввод

        await Assert.ThrowsAsync<EngineUnresponsiveException>(
            () => infinite.WaitAsync(Short, TestContext.Current.CancellationToken));
        Assert.True(engine.IsWedged, "сторож заметил зависший движок");
    }

    [Fact(DisplayName = "UCI: молчащий движок не вешает интерфейс", Timeout = 30000)]
    public async Task МолчащийДвижок()
    {
        using var engine = await StartAsync("wedge");
        engine.SilenceTimeout = TimeSpan.FromMilliseconds(400);
        engine.StopTimeout = TimeSpan.FromMilliseconds(300);

        string? reason = null;
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Unresponsive += (_, text) => { reason = text; fired.TrySetResult(true); };

        await Assert.ThrowsAsync<EngineUnresponsiveException>(() =>
            engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
                TestContext.Current.CancellationToken).WaitAsync(Short, TestContext.Current.CancellationToken));

        await fired.Task.WaitAsync(Short, TestContext.Current.CancellationToken);

        Assert.True(engine.IsWedged, "движок помечен как зависший");
        Assert.False(engine.IsRunning, "зависший процесс снят");
        Assert.False(string.IsNullOrWhiteSpace(reason), "причина отказа названа");

        // После отказа команды не уходят в мёртвый процесс, а честно бросают исключение.
        await Assert.ThrowsAsync<EngineUnresponsiveException>(() =>
            engine.NewGameAsync(TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "UCI: остановка освобождает движок", Timeout = 30000)]
    public async Task ОстановкаОсвобождаетДвижок()
    {
        using var engine = await StartAsync("strict");

        var infinite = engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
            TestContext.Current.CancellationToken);
        await WaitForSearchAsync(engine);

        await engine.StopSearchAsync().WaitAsync(Short, TestContext.Current.CancellationToken);
        await infinite.WaitAsync(Short, TestContext.Current.CancellationToken);

        Assert.False(engine.IsSearching, "после остановки поиск завершён");
        await engine.IsReadyAsync(TestContext.Current.CancellationToken).WaitAsync(Short, TestContext.Current.CancellationToken);
        Assert.False(engine.IsWedged, "движок не завис");
    }

    private static List<string> Timeline(List<string> log)
    {
        lock (log) return new List<string>(log);
    }

    private static List<string> SentCommands(List<string> log)
    {
        lock (log)
        {
            return log.Where(l => l.StartsWith("> ", StringComparison.Ordinal))
                      .Select(l => l[2..])
                      .ToList();
        }
    }
}
