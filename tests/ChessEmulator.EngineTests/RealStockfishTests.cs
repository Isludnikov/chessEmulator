using ChessEmulator.Chess;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>
/// Проверка на настоящем движке: именно он замирает навсегда, если послать ucinewgame или
/// setoption во время поиска. Если движок не найден, тест пропускается.
/// Путь можно задать переменной среды CHESS_TESTS_REAL_ENGINE.
/// </summary>
[Trait("Категория", "Интеграция")]
public sealed class RealStockfishTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private static string? RealEnginePath
    {
        get
        {
            var explicitPath = Environment.GetEnvironmentVariable("CHESS_TESTS_REAL_ENGINE");
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return File.Exists(explicitPath) ? explicitPath : null;

            return EngineLocator.FindAll().FirstOrDefault(p =>
                !Path.GetFileName(p).StartsWith("FakeUciEngine", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact(DisplayName = "Stockfish: новая партия и настройки во время анализа не вешают движок", Timeout = 120000)]
    public async Task НоваяПартияИНастройкиВоВремяАнализа()
    {
        var path = RealEnginePath;
        Assert.SkipWhen(path is null, "Настоящий движок не найден (engine/stockfish.exe).");

        using var engine = new UciEngine();
        await engine.StartAsync(path!, TestContext.Current.CancellationToken);

        // Бесконечный анализ, как в приложении при включённом «Анализе».
        var analysis = engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(),
            TestContext.Current.CancellationToken);
        for (var i = 0; i < 200 && !engine.IsSearching; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.True(engine.IsSearching, "анализ идёт");

        // Именно эта пара команд вешала движок намертво.
        await engine.NewGameAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Limit, TestContext.Current.CancellationToken);
        await engine.ApplyOptionsAsync(
                new[] { new KeyValuePair<string, string>("MultiPV", "2") },
                TestContext.Current.CancellationToken)
            .WaitAsync(Limit, TestContext.Current.CancellationToken);
        await analysis.WaitAsync(Limit, TestContext.Current.CancellationToken);

        // Движок обязан остаться живым и сходить.
        var move = await engine.GoAsync(Position.StartFen, new[] { "f2f4" }, SearchLimits.ByTime(300),
            TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);

        Assert.False(engine.IsWedged, "движок не завис");
        Assert.True(engine.IsRunning, "процесс движка жив");
        Assert.InRange(move.BestMove.Length, 4, 5);  // ответ в формате UCI
        Assert.NotNull(move.Best);  // анализ пришёл вместе с ходом
    }

    [Fact(DisplayName = "Stockfish: ослабление живёт ровно один ход соперника", Timeout = 120000)]
    public async Task ОслаблениеЖивётОдинХод()
    {
        var path = RealEnginePath;
        Assert.SkipWhen(path is null, "Настоящий движок не найден (engine/stockfish.exe).");

        var sent = new List<string>();
        using var engine = new UciEngine();
        engine.LogReceived += (_, text) =>
        {
            if (text.StartsWith("> ")) lock (sent) sent.Add(text[2..]);
        };

        await engine.StartAsync(path!, TestContext.Current.CancellationToken);

        // Постоянные настройки, как их выставляет приложение при запуске движка.
        await engine.ApplyOptionsAsync(
            new[]
            {
                new KeyValuePair<string, string>("Threads", "2"),
                new KeyValuePair<string, string>("Hash", "64")
            }.Concat(Difficulty.FullStrengthOptions(3)),
            TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);

        var profile = Difficulty.Resolve(DifficultyLevel.Beginner,
            new DifficultyDefaults(20, false, 1600, 1000, 3));
        var eloOption = engine.FindOption("UCI_Elo");
        Assert.NotNull(eloOption);  // настоящий Stockfish объявляет рейтинг

        lock (sent) sent.Clear();

        // Ровно то, что делает MainForm.SearchOpponentMoveAsync на ход соперника.
        await engine.ApplyOptionsAsync(Difficulty.OpponentOptions(profile, eloOption),
            TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);
        var move = await engine.GoAsync(Position.StartFen, null,
                new SearchLimits { MoveTimeMs = profile.MoveTimeMs, Depth = profile.DepthLimit },
                TestContext.Current.CancellationToken)
            .WaitAsync(Limit, TestContext.Current.CancellationToken);
        await engine.ApplyOptionsAsync(Difficulty.FullStrengthOptions(3),
            TestContext.Current.CancellationToken).WaitAsync(Limit, TestContext.Current.CancellationToken);

        List<string> commands;
        lock (sent) commands = sent.ToList();

        // Рейтинг поджат к объявленному движком минимуму: значение вне диапазона Stockfish
        // молча игнорирует, и ослабления бы не вышло.
        Assert.Contains($"setoption name UCI_Elo value {Difficulty.ClampElo(eloOption, profile.Elo!.Value)}",
            commands);
        Assert.Contains("setoption name UCI_LimitStrength value true", commands);

        // Хеш и потоки на ход соперника не трогаем: перевыставление чистит таблицу
        // перестановок и пересоздаёт пул потоков.
        Assert.DoesNotContain(commands, c => c.StartsWith("setoption name Hash "));
        Assert.DoesNotContain(commands, c => c.StartsWith("setoption name Threads "));

        // Последними ушли команды возврата: дальше анализ пойдёт на полной силе.
        var lastLimit = commands.FindLastIndex(c => c.StartsWith("setoption name UCI_LimitStrength"));
        Assert.Equal("setoption name UCI_LimitStrength value false", commands[lastLimit]);
        var lastMultiPv = commands.FindLastIndex(c => c.StartsWith("setoption name MultiPV"));
        Assert.Equal("setoption name MultiPV value 3", commands[lastMultiPv]);
        var lastSkill = commands.FindLastIndex(c => c.StartsWith("setoption name Skill Level"));
        Assert.Equal("setoption name Skill Level value 20", commands[lastSkill]);

        // Ослабленный поиск ограничен и глубиной, и временем.
        Assert.Contains(commands, c => c.StartsWith($"go depth {profile.DepthLimit}"));

        Assert.False(engine.IsWedged, "движок не завис");
        Assert.InRange(move.BestMove.Length, 4, 5);  // ход соперника есть

        // И движок продолжает работать на полной силе: анализ идёт вглубь.
        var deep = await engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(16),
                TestContext.Current.CancellationToken)
            .WaitAsync(Limit, TestContext.Current.CancellationToken);
        Assert.NotNull(deep.Best);
        Assert.True(deep.Best!.Depth >= 16, $"анализ на полной силе ушёл на глубину {deep.Best.Depth}");
        Assert.Equal(3, deep.Lines.Count);  // MultiPV вернулся к настройке
    }
}
