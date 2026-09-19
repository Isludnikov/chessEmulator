using ChessEmulator.Chess;
using ChessEmulator.Engine;
using ChessEmulator.TestKit;

namespace ChessEmulator.EngineTests;

/// <summary>Обёртка над UCI-движком, проверенная на поддельном движке из tests/FakeUciEngine.</summary>
internal static class UciEngineTests
{
    public static async Task RunAsync(string enginePath)
    {
        using var engine = new UciEngine();
        var sent = new List<string>();
        var infoCount = 0;
        engine.LogReceived += (_, text) => { if (text.StartsWith("> ")) lock (sent) sent.Add(text[2..]); };
        engine.InfoReceived += (_, _) => Interlocked.Increment(ref infoCount);

        await Handshake(engine, enginePath);
        Commands(engine, sent);
        await Analysis(engine, () => infoCount);
        await ParsingInfo(engine);
        await StopAndCancel(engine);
        await Failures(enginePath);
    }

    private static async Task Handshake(UciEngine engine, string enginePath)
    {
        await engine.StartAsync(enginePath);

        Test.Suite("UCI: рукопожатие", () =>
        {
            Test.True("движок запущен", engine.IsRunning);
            Test.Check("имя движка", "FakeFish 1.2", engine.Name);
            Test.Check("автор движка", "Tester", engine.Author);
            Test.Check("путь сохранён", enginePath, engine.ExecutablePath);
            Test.Check("разобрано параметров", 8, engine.Options.Count);
            Test.False("поиск не идёт", engine.IsSearching);

            var threads = engine.FindOption("Threads")!;
            Test.Check("тип spin", "spin", threads.Type);
            Test.Check("минимум", "1", threads.Min ?? "");
            Test.Check("максимум", "512", threads.Max ?? "");
            Test.Check("значение по умолчанию", "1", threads.Default);

            Test.Check("имя из двух слов", "Skill Level", engine.FindOption("Skill Level")!.Name);
            Test.True("поиск параметра без учёта регистра", engine.SupportsOption("skill level"));
            Test.False("несуществующий параметр", engine.SupportsOption("Ultra"));
            Test.Check("несуществующий параметр не находится", null, engine.FindOption("Ultra"));

            Test.Check("тип check", "check", engine.FindOption("UCI_LimitStrength")!.Type);
            Test.Check("значение check", "false", engine.FindOption("UCI_LimitStrength")!.Default);

            var style = engine.FindOption("Style")!;
            Test.Check("тип combo", "combo", style.Type);
            Test.Check("значение combo по умолчанию", "Normal", style.Default);
            Test.Check("варианты combo", 2, style.Vars.Count);
            Test.Check("вариант из двух слов", "Wild Attack", style.Vars[1]);

            Test.Check("тип button", "button", engine.FindOption("Clear Hash")!.Type);
            Test.Check("параметр с пустым значением", "", engine.FindOption("Debug Log File")!.Default);
            Test.Check("тип string", "string", engine.FindOption("Debug Log File")!.Type);
        });
    }

    private static void Commands(UciEngine engine, List<string> sent)
    {
        Test.Suite("UCI: отправляемые команды", () =>
        {
            void Clear() { lock (sent) sent.Clear(); }
            string Last() { lock (sent) return sent.Count > 0 ? sent[^1] : ""; }

            Clear();
            engine.SetOption("Threads", "4");
            Test.Check("установка параметра", "setoption name Threads value 4", Last());

            engine.SetOption("Clear Hash", "");
            Test.Check("параметр без значения", "setoption name Clear Hash", Last());

            engine.NewGame();
            Test.Check("новая партия", "ucinewgame", Last());

            engine.SetPosition(Position.StartFen);
            Test.Check("начальная позиция передаётся коротко", "position startpos", Last());

            engine.SetPosition(Position.StartFen, new[] { "e2e4", "e7e5" });
            Test.Check("начальная позиция с ходами", "position startpos moves e2e4 e7e5", Last());

            const string fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1";
            engine.SetPosition(fen);
            Test.Check("произвольная позиция", $"position fen {fen}", Last());

            engine.SetPosition(fen, Array.Empty<string>());
            Test.Check("пустой список ходов не добавляется", $"position fen {fen}", Last());

            Clear();
            engine.Send("isready");
            Test.Check("произвольная команда уходит как есть", "isready", Last());
        });
    }

    private static async Task Analysis(UciEngine engine, Func<int> infoCount)
    {
        await engine.IsReadyAsync();
        var before = infoCount();
        var result = await engine.GoAsync(Position.StartFen, null, SearchLimits.ByTime(100));

        Test.Suite("UCI: разбор анализа", () =>
        {
            Test.Check("лучший ход", "e2e4", result.BestMove);
            Test.Check("ход для обдумывания", "e7e5", result.Ponder ?? "");
            Test.Check("строк анализа", 2, result.Lines.Count);
            Test.Check("служебные строки пропущены", 3, infoCount() - before);

            var main = result.Lines[1];
            Test.Check("глубина", 2, main.Depth);
            Test.Check("выборочная глубина", 3, main.SelDepth);
            Test.Check("оценка", 31, main.ScoreCp ?? 0);
            Test.Check("узлы", 200, main.Nodes);
            Test.Check("скорость", 30000, main.Nps);
            Test.Check("время", 7, main.TimeMs);
            Test.Check("главный вариант", "e2e4 e7e5 g1f3", string.Join(" ", main.Pv));
            Test.True("верхняя граница отмечена", main.UpperBound);
            Test.False("нижняя граница не отмечена", main.LowerBound);
            Test.Check("главная строка — первая", main.ScoreCp, result.Best!.ScoreCp);

            var second = result.Lines[2];
            Test.Check("номер второго варианта", 2, second.MultiPv);
            Test.Check("мат во второй строке", 5, second.ScoreMate ?? 0);
            Test.Check("текст оценки второй строки", "#5", second.ScoreText(true));

            Test.False("после поиска движок свободен", engine.IsSearching);
        });
    }

    private static async Task ParsingInfo(UciEngine engine)
    {
        engine.Send("test-scenario mate");
        var mate = await engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(20));

        engine.Send("test-scenario multipv");
        var multi = await engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(12));

        engine.Send("test-scenario silent");
        var silent = await engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(5));

        engine.Send("test-scenario none");
        var none = await engine.GoAsync("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", null, SearchLimits.ByDepth(5));

        engine.Send("test-scenario default");

        Test.Suite("UCI: особые ответы движка", () =>
        {
            var info = mate.Best!;
            Test.Check("мат за соперника", -3, info.ScoreMate ?? 0);
            Test.Check("текст мата", "#-3", info.ScoreText(true));
            Test.Check("текст мата с точки зрения чёрных", "#3", info.ScoreText(false));
            Test.True("нижняя граница разобрана", info.LowerBound);
            Test.Check("заполнение хеша", 250, info.HashFull);
            Test.Check("попадания в таблицы", 7, info.TbHits);
            Test.Check("выборочная глубина", 26, info.SelDepth);
            Test.Check("ход мата", "d1h5", mate.BestMove);
            Test.Check("у хода мата нет обдумывания", null, mate.Ponder);

            Test.Check("строки разложены по номерам", 3, multi.Lines.Count);
            Test.Check("первая строка", 45, multi.Lines[1].ScoreCp ?? 0);
            Test.Check("вторая строка", 12, multi.Lines[2].ScoreCp ?? 0);
            Test.Check("третья строка", -15, multi.Lines[3].ScoreCp ?? 0);
            Test.Check("порядок прихода строк не важен", "e2e4 e7e5", string.Join(" ", multi.Lines[1].Pv));

            Test.Check("без вариантов строк анализа нет", 0, silent.Lines.Count);
            Test.Check("лучшая строка отсутствует", null, silent.Best);
            Test.Check("ход всё равно получен", "e2e4", silent.BestMove);

            Test.Check("в законченной позиции хода нет", "(none)", none.BestMove);
            Test.Check("в законченной позиции анализа нет", 0, none.Lines.Count);
        });
    }

    private static async Task StopAndCancel(UciEngine engine)
    {
        var infinite = engine.GoAsync(Position.StartFen, new[] { "e2e4" }, SearchLimits.AsInfinite());
        await Task.Delay(300);
        var searching = engine.IsSearching;
        await engine.StopSearchAsync();
        var stopped = await infinite;

        var again = await engine.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(3));

        using var cts = new CancellationTokenSource(250);
        var cancelled = await engine.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite(), cts.Token);

        await engine.StopSearchAsync();   // останавливать нечего

        Test.Suite("UCI: остановка поиска", () =>
        {
            Test.True("бесконечный поиск идёт", searching);
            Test.Check("остановка возвращает ход", "e2e4", stopped.BestMove);
            Test.False("после остановки поиск завершён", engine.IsSearching);
            Test.True("анализ успел прийти", stopped.Lines.Count > 0);
            Test.Check("поиск запускается снова", "e2e4", again.BestMove);
            Test.Check("отмена по токену возвращает ход", "e2e4", cancelled.BestMove);
            Test.NoThrow("повторная остановка безопасна", () => engine.StopSearchAsync().Wait());
        });
    }

    private static async Task Failures(string enginePath)
    {
        // Движок падает посреди поиска
        using var crashing = new UciEngine();
        await crashing.StartAsync(enginePath);
        crashing.Send("test-scenario default");
        var search = crashing.GoAsync(Position.StartFen, null, SearchLimits.AsInfinite());
        await Task.Delay(200);
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
        for (var i = 0; i < 50 && crashing.IsRunning; i++) await Task.Delay(20);

        var missing = new UciEngine();
        Exception? startError = null;
        try
        {
            await missing.StartAsync(Path.Combine(AppContext.BaseDirectory, "нет-такого-движка.exe"));
        }
        catch (Exception ex)
        {
            startError = ex;
        }

        Exception? goError = null;
        try
        {
            await missing.GoAsync(Position.StartFen, null, SearchLimits.ByDepth(1));
        }
        catch (Exception ex)
        {
            goError = ex;
        }

        Test.Suite("UCI: ошибки", () =>
        {
            Test.Check("падение движка прерывает поиск", "InvalidOperationException",
                crashError?.GetType().Name ?? "нет исключения");
            Test.False("после падения движок не запущен", crashing.IsRunning);
            Test.NoThrow("остановка упавшего движка безопасна", () => crashing.Stop());
            Test.NoThrow("повторная остановка безопасна", () => crashing.Stop());

            Test.Check("отсутствующий файл", "FileNotFoundException",
                startError?.GetType().Name ?? "нет исключения");
            Test.Check("поиск без движка", "InvalidOperationException",
                goError?.GetType().Name ?? "нет исключения");
            Test.False("незапущенный движок не работает", missing.IsRunning);
            Test.NoThrow("освобождение ресурсов", () => missing.Dispose());
            Test.NoThrow("повторное освобождение", () => missing.Dispose());
        });
    }
}
