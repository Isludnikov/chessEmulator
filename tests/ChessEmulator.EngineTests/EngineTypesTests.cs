using System.Globalization;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>Типы движка без запуска процесса: оценки, ограничения поиска, результат.</summary>
public class EngineTypesTests
{
    [Fact(DisplayName = "Движок: оценки")]
    public void Scores()
    {
        // Точка как разделитель — не зависит от языка системы.
        // finally обязателен: раннер переиспользует потоки между тестами.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            Assert.Equal("+0.31", EngineInfo.FormatPawns(31));  // разделитель не зависит от локали
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }

        Assert.Equal("+1.25", EngineInfo.FormatPawns(125));  // перевес белых
        Assert.Equal("-1.25", EngineInfo.FormatPawns(-125));  // перевес чёрных
        Assert.Equal("0.00", EngineInfo.FormatPawns(0));  // равенство
        Assert.Equal("+0.07", EngineInfo.FormatPawns(7));  // округление до сотых
        Assert.Equal("+92.50", EngineInfo.FormatPawns(9250));  // большой перевес

        var info = new EngineInfo { ScoreCp = 45 };
        Assert.Equal(45, info.WhiteCp(true));  // оценка белых при ходе белых
        Assert.Equal(-45, info.WhiteCp(false));  // оценка белых при ходе чёрных
        Assert.Equal("+0.45", info.ScoreText(true));  // текст при ходе белых
        Assert.Equal("-0.45", info.ScoreText(false));  // текст при ходе чёрных

        var mate = new EngineInfo { ScoreMate = 3 };
        Assert.Equal(3, mate.WhiteMate(true));  // мат при ходе белых
        Assert.Equal(-3, mate.WhiteMate(false));  // мат при ходе чёрных
        Assert.Equal("#3", mate.ScoreText(true));  // текст мата за белых
        Assert.Equal("#-3", mate.ScoreText(false));  // текст мата за чёрных
        Assert.Equal("#-2", new EngineInfo { ScoreMate = -2 }.ScoreText(true));  // мат в минус

        var empty = new EngineInfo();
        Assert.Null(empty.WhiteCp(true));  // нет оценки в сантипешках
        Assert.Null(empty.WhiteMate(true));  // нет оценки мата
        Assert.Equal("—", empty.ScoreText(true));  // текст без оценки

        // Мат важнее оценки в сантипешках
        // мат перекрывает сантипешки
        Assert.Equal("#1", new EngineInfo { ScoreCp = 500, ScoreMate = 1 }.ScoreText(true));

        Assert.Equal(1, new EngineInfo().MultiPv);  // номер варианта по умолчанию
        Assert.Empty(new EngineInfo().Pv);  // пустой главный вариант
    }

    [Fact(DisplayName = "Движок: оценка мата в ноль")]
    public void MateInZero()
    {
        // Движок отдаёт «score mate 0» в позиции, где мат уже стоит на доске.
        var mated = new EngineInfo { ScoreMate = 0 };
        Assert.Equal("#0", mated.ScoreText(true));  // мат, а не «мат в минус ноль»
        Assert.Equal("#0", mated.ScoreText(false));  // и с точки зрения чёрных тоже

        Assert.Equal("#1", new EngineInfo { ScoreMate = 1 }.ScoreText(true));  // мат в один ход
        Assert.Equal("#-1", new EngineInfo { ScoreMate = 1 }.ScoreText(false));  // он же глазами чёрных
    }

    [Fact(DisplayName = "Движок: лучшая линия не зависит от порядка прихода")]
    public void BestLineOrder()
    {
        // Строки MultiPV приходят вперемешку, и первая пришедшая — не обязательно лучшая.
        var result = new SearchResult { BestMove = "e2e4" };
        result.Lines[3] = new EngineInfo { MultiPv = 3, ScoreCp = -40 };
        result.Lines[2] = new EngineInfo { MultiPv = 2, ScoreCp = 5 };

        Assert.Equal(2, result.Best!.MultiPv);  // без первой линии берём следующую по номеру

        result.Lines[1] = new EngineInfo { MultiPv = 1, ScoreCp = 60 };
        Assert.Equal(1, result.Best!.MultiPv);  // первая линия всегда главнее

        Assert.Null(new SearchResult().Best);  // без строк лучшей линии нет
    }

    [Fact(DisplayName = "Движок: ограничения поиска")]
    public void Limits()
    {
        Assert.Equal("go depth 12", SearchLimits.ByDepth(12).ToGoCommand());  // по глубине
        Assert.Equal("go movetime 500", SearchLimits.ByTime(500).ToGoCommand());  // по времени
        Assert.Equal("go infinite", SearchLimits.AsInfinite().ToGoCommand());  // бесконечный
        Assert.Equal("go nodes 100000", new SearchLimits { Nodes = 100000 }.ToGoCommand());  // по узлам
        // глубина и время вместе
        Assert.Equal("go depth 20 movetime 1000", new SearchLimits { Depth = 20, MoveTimeMs = 1000 }.ToGoCommand());
        // все ограничения сразу
        Assert.Equal("go depth 20 movetime 1000 nodes 5000", new SearchLimits { Depth = 20, MoveTimeMs = 1000, Nodes = 5000 }.ToGoCommand());
        Assert.Equal("go depth 18", new SearchLimits().ToGoCommand());  // без ограничений — глубина по умолчанию
        // бесконечный важнее остальных
        Assert.Equal("go infinite", new SearchLimits { Infinite = true, Depth = 5 }.ToGoCommand());
    }

    [Fact(DisplayName = "Движок: результат поиска")]
    public void Results()
    {
        var result = new SearchResult { BestMove = "e2e4" };
        Assert.Null(result.Best);  // пустой результат не даёт лучшей строки

        result.Lines[2] = new EngineInfo { MultiPv = 2, ScoreCp = 10 };
        Assert.Equal(10, result.Best!.ScoreCp);  // при отсутствии первой строки берётся любая

        result.Lines[1] = new EngineInfo { MultiPv = 1, ScoreCp = 50 };
        Assert.Equal(50, result.Best!.ScoreCp);  // главная строка — первая
        Assert.Equal("e2e4", result.BestMove);  // лучший ход сохранён
        Assert.Null(result.Ponder);  // ход для обдумывания пуст

        var option = new UciOption { Name = "Threads", Type = "spin", Default = "1", Min = "1", Max = "512" };
        Assert.Equal("Threads", option.Name);  // имя параметра
        Assert.Equal("spin", option.Type);  // тип параметра
        Assert.Empty(option.Vars);  // список значений пуст
    }
}
