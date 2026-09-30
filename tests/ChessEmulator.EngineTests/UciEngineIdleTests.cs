using ChessEmulator.Chess;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>
/// Обёртка без живого процесса: движок ещё не запускали либо он упал. Команды в таком
/// состоянии не должны ни бросать исключения, ни ждать ответа, которого некому дать.
/// </summary>
public sealed class UciEngineIdleTests
{
    /// <summary>Движок без контекста синхронизации: журнал пишется сразу, а не через очередь.</summary>
    private static UciEngine NewEngine(List<string> log)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        var engine = new UciEngine();
        SynchronizationContext.SetSynchronizationContext(previous);

        engine.LogReceived += (_, text) => { lock (log) log.Add(text); };
        return engine;
    }

    [Fact(DisplayName = "UCI: незапущенный движок молча пропускает команды")]
    public async Task КомандыБезДвижка()
    {
        var log = new List<string>();
        using var idle = NewEngine(log);

        idle.Send("isready");
        idle.SetOption("Threads", "2");
        idle.NewGame();
        idle.SetPosition(Position.StartFen);

        // Операции с подтверждением тоже не ждут ответа, которого некому дать.
        await idle.IsReadyAsync(TestContext.Current.CancellationToken);
        await idle.NewGameAsync(TestContext.Current.CancellationToken);
        await idle.ApplyOptionsAsync([new KeyValuePair<string, string>("Hash", "32")],
            TestContext.Current.CancellationToken);
        await idle.StopSearchAsync();

        lock (log) Assert.Empty(log);  // ни одна команда никуда не ушла
        Assert.False(idle.IsSearching, "поиска нет");
        Assert.False(idle.IsWedged, "отсутствие движка — не зависание");
    }

    [Fact(DisplayName = "UCI: команда упавшему движку никуда не уходит", Timeout = 30000)]
    public async Task КомандаУпавшемуДвижку()
    {
        var log = new List<string>();
        using var engine = NewEngine(log);
        await engine.StartAsync(UciEngineTests.EnginePath, TestContext.Current.CancellationToken);

        engine.Send("test-crash");
        for (var i = 0; i < 100 && engine.IsRunning; i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.False(engine.IsRunning, "движок упал");

        engine.Send("ucinewgame");
        lock (log) Assert.DoesNotContain("> ucinewgame", log);  // в закрытый поток не пишем

        // И не ждём подтверждения от процесса, которого уже нет.
        await engine.NewGameAsync(TestContext.Current.CancellationToken);
        await engine.IsReadyAsync(TestContext.Current.CancellationToken);
    }
}
