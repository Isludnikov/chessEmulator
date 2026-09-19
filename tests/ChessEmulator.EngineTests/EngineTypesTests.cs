using System.Globalization;
using ChessEmulator.Engine;
using ChessEmulator.TestKit;

namespace ChessEmulator.EngineTests;

/// <summary>Типы движка без запуска процесса: оценки, ограничения поиска, результат.</summary>
internal static class EngineTypesTests
{
    public static void Run()
    {
        Scores();
        Limits();
        Results();
    }

    private static void Scores()
    {
        Test.Suite("Движок: оценки", () =>
        {
            // Точка как разделитель — не зависит от языка системы
            var russian = new CultureInfo("ru-RU");
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = russian;
            Test.Check("разделитель не зависит от локали", "+0.31", EngineInfo.FormatPawns(31));
            Thread.CurrentThread.CurrentCulture = previous;

            Test.Check("перевес белых", "+1.25", EngineInfo.FormatPawns(125));
            Test.Check("перевес чёрных", "-1.25", EngineInfo.FormatPawns(-125));
            Test.Check("равенство", "0.00", EngineInfo.FormatPawns(0));
            Test.Check("округление до сотых", "+0.07", EngineInfo.FormatPawns(7));
            Test.Check("большой перевес", "+92.50", EngineInfo.FormatPawns(9250));

            var info = new EngineInfo { ScoreCp = 45 };
            Test.Check("оценка белых при ходе белых", 45, info.WhiteCp(true));
            Test.Check("оценка белых при ходе чёрных", -45, info.WhiteCp(false));
            Test.Check("текст при ходе белых", "+0.45", info.ScoreText(true));
            Test.Check("текст при ходе чёрных", "-0.45", info.ScoreText(false));

            var mate = new EngineInfo { ScoreMate = 3 };
            Test.Check("мат при ходе белых", 3, mate.WhiteMate(true));
            Test.Check("мат при ходе чёрных", -3, mate.WhiteMate(false));
            Test.Check("текст мата за белых", "#3", mate.ScoreText(true));
            Test.Check("текст мата за чёрных", "#-3", mate.ScoreText(false));
            Test.Check("мат в минус", "#-2", new EngineInfo { ScoreMate = -2 }.ScoreText(true));

            var empty = new EngineInfo();
            Test.Check("нет оценки в сантипешках", null, empty.WhiteCp(true));
            Test.Check("нет оценки мата", null, empty.WhiteMate(true));
            Test.Check("текст без оценки", "—", empty.ScoreText(true));

            // Мат важнее оценки в сантипешках
            Test.Check("мат перекрывает сантипешки", "#1",
                new EngineInfo { ScoreCp = 500, ScoreMate = 1 }.ScoreText(true));

            Test.Check("номер варианта по умолчанию", 1, new EngineInfo().MultiPv);
            Test.Check("пустой главный вариант", 0, new EngineInfo().Pv.Length);
        });
    }

    private static void Limits()
    {
        Test.Suite("Движок: ограничения поиска", () =>
        {
            Test.Check("по глубине", "go depth 12", SearchLimits.ByDepth(12).ToGoCommand());
            Test.Check("по времени", "go movetime 500", SearchLimits.ByTime(500).ToGoCommand());
            Test.Check("бесконечный", "go infinite", SearchLimits.AsInfinite().ToGoCommand());
            Test.Check("по узлам", "go nodes 100000", new SearchLimits { Nodes = 100000 }.ToGoCommand());
            Test.Check("глубина и время вместе", "go depth 20 movetime 1000",
                new SearchLimits { Depth = 20, MoveTimeMs = 1000 }.ToGoCommand());
            Test.Check("все ограничения сразу", "go depth 20 movetime 1000 nodes 5000",
                new SearchLimits { Depth = 20, MoveTimeMs = 1000, Nodes = 5000 }.ToGoCommand());
            Test.Check("без ограничений — глубина по умолчанию", "go depth 18", new SearchLimits().ToGoCommand());
            Test.Check("бесконечный важнее остальных", "go infinite",
                new SearchLimits { Infinite = true, Depth = 5 }.ToGoCommand());
        });
    }

    private static void Results()
    {
        Test.Suite("Движок: результат поиска", () =>
        {
            var result = new SearchResult { BestMove = "e2e4" };
            Test.Check("пустой результат не даёт лучшей строки", null, result.Best);

            result.Lines[2] = new EngineInfo { MultiPv = 2, ScoreCp = 10 };
            Test.Check("при отсутствии первой строки берётся любая", 10, result.Best!.ScoreCp);

            result.Lines[1] = new EngineInfo { MultiPv = 1, ScoreCp = 50 };
            Test.Check("главная строка — первая", 50, result.Best!.ScoreCp);
            Test.Check("лучший ход сохранён", "e2e4", result.BestMove);
            Test.Check("ход для обдумывания пуст", null, result.Ponder);

            var option = new UciOption { Name = "Threads", Type = "spin", Default = "1", Min = "1", Max = "512" };
            Test.Check("имя параметра", "Threads", option.Name);
            Test.Check("тип параметра", "spin", option.Type);
            Test.Check("список значений пуст", 0, option.Vars.Count);
        });
    }
}
