using ChessEmulator.Chess;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>
/// Обёртка над UCI-движком, проверенная на поддельном движке из tests/FakeUciEngine.
/// Каждый тест получает свой экземпляр класса, а значит и свежий процесс движка,
/// поэтому порядок выполнения значения не имеет.
/// </summary>
public sealed class UciEngineTests : IAsyncLifetime
{
    private readonly UciEngine _engine = new();
    private readonly List<string> _sent = new();
    private int _infoCount;

    /// <summary>
    /// Поддельный движок кладётся в выходную папку теста ссылкой на проект FakeUciEngine.
    /// Раньше путь можно было передать первым аргументом командной строки.
    /// </summary>
    internal static string EnginePath =>
        Environment.GetEnvironmentVariable("CHESS_TESTS_ENGINE")
        ?? Path.Combine(AppContext.BaseDirectory, "FakeUciEngine.exe");

    public async ValueTask InitializeAsync()
    {
        _engine.LogReceived += (_, text) => { if (text.StartsWith("> ")) lock (_sent) _sent.Add(text[2..]); };
        _engine.InfoReceived += (_, _) => Interlocked.Increment(ref _infoCount);
        await _engine.StartAsync(EnginePath, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _engine.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "UCI: рукопожатие")]
    public void Рукопожатие()
    {
        Assert.True(_engine.IsRunning, "движок запущен");
        Assert.Equal("FakeFish 1.2", _engine.Name);  // имя движка
        Assert.Equal("Tester", _engine.Author);  // автор движка
        Assert.Equal(EnginePath, _engine.ExecutablePath);  // путь сохранён
        Assert.Equal(8, _engine.Options.Count);  // разобрано параметров
        Assert.False(_engine.IsSearching, "поиск не идёт");

        var threads = _engine.FindOption("Threads")!;
        Assert.Equal("spin", threads.Type);  // тип spin
        Assert.Equal("1", threads.Min ?? "");  // минимум
        Assert.Equal("512", threads.Max ?? "");  // максимум
        Assert.Equal("1", threads.Default);  // значение по умолчанию

        Assert.Equal("Skill Level", _engine.FindOption("Skill Level")!.Name);  // имя из двух слов
        Assert.True(_engine.SupportsOption("skill level"), "поиск параметра без учёта регистра");
        Assert.False(_engine.SupportsOption("Ultra"), "несуществующий параметр");
        Assert.Null(_engine.FindOption("Ultra"));  // несуществующий параметр не находится

        Assert.Equal("check", _engine.FindOption("UCI_LimitStrength")!.Type);  // тип check
        Assert.Equal("false", _engine.FindOption("UCI_LimitStrength")!.Default);  // значение check

        var style = _engine.FindOption("Style")!;
        Assert.Equal("combo", style.Type);  // тип combo
        Assert.Equal("Normal", style.Default);  // значение combo по умолчанию
        Assert.Equal(2, style.Vars.Count);  // варианты combo
        Assert.Equal("Wild Attack", style.Vars[1]);  // вариант из двух слов

        Assert.Equal("button", _engine.FindOption("Clear Hash")!.Type);  // тип button
        Assert.Equal("", _engine.FindOption("Debug Log File")!.Default);  // параметр с пустым значением
        Assert.Equal("string", _engine.FindOption("Debug Log File")!.Type);  // тип string
    }

    [Fact(DisplayName = "UCI: отправляемые команды")]
    public void Команды()
    {
        void Clear() { lock (_sent) _sent.Clear(); }
        string Last() { lock (_sent) return _sent.Count > 0 ? _sent[^1] : ""; }

        Clear();
        _engine.SetOption("Threads", "4");
        Assert.Equal("setoption name Threads value 4", Last());  // установка параметра

        _engine.SetOption("Clear Hash", "");
        Assert.Equal("setoption name Clear Hash", Last());  // параметр без значения

        _engine.NewGame();
        Assert.Equal("ucinewgame", Last());  // новая партия

        _engine.SetPosition(Position.StartFen);
        Assert.Equal("position startpos", Last());  // начальная позиция передаётся коротко

        _engine.SetPosition(Position.StartFen, new[] { "e2e4", "e7e5" });
        Assert.Equal("position startpos moves e2e4 e7e5", Last());  // начальная позиция с ходами

        const string fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1";
        _engine.SetPosition(fen);
        Assert.Equal($"position fen {fen}", Last());  // произвольная позиция

        _engine.SetPosition(fen, Array.Empty<string>());
        Assert.Equal($"position fen {fen}", Last());  // пустой список ходов не добавляется

        Clear();
        _engine.Send("isready");
        Assert.Equal("isready", Last());  // произвольная команда уходит как есть
    }

    [Fact(DisplayName = "UCI: разбор анализа")]
    public async Task Анализ()
    {
        await _engine.IsReadyAsync(TestContext.Current.CancellationToken);
        var before = _infoCount;
        var result = await _engine.GoAsync(Position.StartFen, null, SearchLimits.ByTime(100), TestContext.Current.CancellationToken);

        Assert.Equal("e2e4", result.BestMove);  // лучший ход
        Assert.Equal("e7e5", result.Ponder ?? "");  // ход для обдумывания
        Assert.Equal(2, result.Lines.Count);  // строк анализа
        Assert.Equal(3, _infoCount - before);  // служебные строки пропущены

        var main = result.Lines[1];
        Assert.Equal(2, main.Depth);  // глубина
        Assert.Equal(3, main.SelDepth);  // выборочная глубина
        Assert.Equal(31, main.ScoreCp ?? 0);  // оценка
        Assert.Equal(200, main.Nodes);  // узлы
        Assert.Equal(30000, main.Nps);  // скорость
        Assert.Equal(7, main.TimeMs);  // время
        Assert.Equal("e2e4 e7e5 g1f3", string.Join(" ", main.Pv));  // главный вариант
        Assert.True(main.UpperBound, "верхняя граница отмечена");
        Assert.False(main.LowerBound, "нижняя граница не отмечена");
        Assert.Equal(main.ScoreCp, result.Best!.ScoreCp);  // главная строка — первая

        var second = result.Lines[2];
        Assert.Equal(2, second.MultiPv);  // номер второго варианта
        Assert.Equal(5, second.ScoreMate ?? 0);  // мат во второй строке
        Assert.Equal("#5", second.ScoreText(true));  // текст оценки второй строки

        Assert.False(_engine.IsSearching, "после поиска движок свободен");
    }

    [Fact(DisplayName = "UCI: особые ответы движка")]
    public async Task ОсобыеОтветы()
    {
        _engine.Send("test-scenario mate");
        var mate = await _engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(20), TestContext.Current.CancellationToken);

        _engine.Send("test-scenario multipv");
        var multi = await _engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(12), TestContext.Current.CancellationToken);

        _engine.Send("test-scenario silent");
        var silent = await _engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(5), TestContext.Current.CancellationToken);

        _engine.Send("test-scenario none");
        var none = await _engine.GoAsync("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", null, SearchLimits.ByDepth(5), TestContext.Current.CancellationToken);

        _engine.Send("test-scenario default");

        var info = mate.Best!;
        Assert.Equal(-3, info.ScoreMate ?? 0);  // мат за соперника
        Assert.Equal("#-3", info.ScoreText(true));  // текст мата
        Assert.Equal("#3", info.ScoreText(false));  // текст мата с точки зрения чёрных
        Assert.True(info.LowerBound, "нижняя граница разобрана");
        Assert.Equal(250, info.HashFull);  // заполнение хеша
        Assert.Equal(7, info.TbHits);  // попадания в таблицы
        Assert.Equal(26, info.SelDepth);  // выборочная глубина
        Assert.Equal("d1h5", mate.BestMove);  // ход мата
        Assert.Null(mate.Ponder);  // у хода мата нет обдумывания

        Assert.Equal(3, multi.Lines.Count);  // строки разложены по номерам
        Assert.Equal(45, multi.Lines[1].ScoreCp ?? 0);  // первая строка
        Assert.Equal(12, multi.Lines[2].ScoreCp ?? 0);  // вторая строка
        Assert.Equal(-15, multi.Lines[3].ScoreCp ?? 0);  // третья строка
        Assert.Equal("e2e4 e7e5", string.Join(" ", multi.Lines[1].Pv));  // порядок прихода строк не важен

        Assert.Empty(silent.Lines);  // без вариантов строк анализа нет
        Assert.Null(silent.Best);  // лучшая строка отсутствует
        Assert.Equal("e2e4", silent.BestMove);  // ход всё равно получен

        Assert.Equal("(none)", none.BestMove);  // в законченной позиции хода нет
        Assert.Empty(none.Lines);  // в законченной позиции анализа нет
    }

    [Fact(DisplayName = "UCI: остановка поиска")]
    public async Task Остановка()
    {
        var infinite = _engine.GoAsync(Position.StartFen, new[] { "e2e4" }, SearchLimits.AsInfinite(), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var searching = _engine.IsSearching;
        await _engine.StopSearchAsync();
        var stopped = await infinite;

        var again = await _engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(3), TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource(250);
        var cancelled = await _engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(), cts.Token);

        await _engine.StopSearchAsync();   // останавливать нечего

        Assert.True(searching, "бесконечный поиск идёт");
        Assert.Equal("e2e4", stopped.BestMove);  // остановка возвращает ход
        Assert.False(_engine.IsSearching, "после остановки поиск завершён");
        Assert.True(stopped.Lines.Count > 0, "анализ успел прийти");
        Assert.Equal("e2e4", again.BestMove);  // поиск запускается снова
        Assert.Equal("e2e4", cancelled.BestMove);  // отмена по токену возвращает ход
        Assert.Null(Record.Exception(() => _engine.StopSearchAsync().Wait(TestContext.Current.CancellationToken)));  // повторная остановка безопасна
    }
}

/// <summary>Поведение обёртки, когда движок падает или его вовсе нет.</summary>
public class UciEngineFailureTests
{
    [Fact(DisplayName = "UCI: ошибки")]
    public async Task Ошибки()
    {
        // Движок падает посреди поиска
        using var crashing = new UciEngine();
        await crashing.StartAsync(UciEngineTests.EnginePath, TestContext.Current.CancellationToken);
        crashing.Send("test-scenario default");
        var search = crashing.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        crashing.Send("test-crash");

        Exception? crashError = null;
        try
        {
            await search;
        }
        catch (Exception ex)
        {
            crashError = ex;
        }

        // Ожидание завершения процесса, чтобы IsRunning успел обновиться
        for (var i = 0; i < 50 && crashing.IsRunning; i++) await Task.Delay(20, TestContext.Current.CancellationToken);

        var missing = new UciEngine();
        Exception? startError = null;
        try
        {
            await missing.StartAsync(Path.Combine(AppContext.BaseDirectory, "нет-такого-движка.exe"), TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            startError = ex;
        }

        Exception? goError = null;
        try
        {
            await missing.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(1), TestContext.Current.CancellationToken);
        }
        catch (Exception ex)
        {
            goError = ex;
        }

        // падение движка прерывает поиск
        Assert.Equal("InvalidOperationException", crashError?.GetType().Name ?? "нет исключения");
        Assert.False(crashing.IsRunning, "после падения движок не запущен");
        Assert.Null(Record.Exception(() => crashing.Stop()));  // остановка упавшего движка безопасна
        Assert.Null(Record.Exception(() => crashing.Stop()));  // повторная остановка безопасна

        // отсутствующий файл
        Assert.Equal("FileNotFoundException", startError?.GetType().Name ?? "нет исключения");
        // поиск без движка
        Assert.Equal("InvalidOperationException", goError?.GetType().Name ?? "нет исключения");
        Assert.False(missing.IsRunning, "незапущенный движок не работает");
        Assert.Null(Record.Exception(() => missing.Dispose()));  // освобождение ресурсов
        Assert.Null(Record.Exception(() => missing.Dispose()));  // повторное освобождение
    }
}
