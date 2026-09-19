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
}
