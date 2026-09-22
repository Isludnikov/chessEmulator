using ChessEmulator.Chess;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>
/// Выбор хода соперника. Движок здесь не запускается: результат поиска собирается руками,
/// поэтому поведение слабых уровней проверяется целиком и повторимо.
/// </summary>
public class OpponentMovePickerTests
{
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    /// <summary>Датчик, всегда отдающий одно и то же: так проверяется выбор без разброса.</summary>
    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
    }

    private static SearchResult Result(string bestMove, params (string Uci, int? Cp, int? Mate)[] lines)
    {
        var result = new SearchResult { BestMove = bestMove };
        for (var i = 0; i < lines.Length; i++)
        {
            result.Lines[i + 1] = new EngineInfo
            {
                MultiPv = i + 1,
                ScoreCp = lines[i].Cp,
                ScoreMate = lines[i].Mate,
                Pv = lines[i].Uci.Length == 0 ? Array.Empty<string>() : new[] { lines[i].Uci }
            };
        }
        return result;
    }

    private static DifficultyProfile Weak(int candidates = 3, int temperature = 100,
        int maxLoss = 300, double randomChance = 0) => new()
    {
        Level = DifficultyLevel.Club,
        Title = "Проверка",
        Hint = "Проверка",
        SkillLevel = 5,
        CandidateCount = candidates,
        TemperatureCp = temperature,
        MaxLossCp = maxLoss,
        RandomMoveChance = randomChance
    };

    [Fact(DisplayName = "Выбор хода: максимум играет лучший ход")]
    public void МаксимумИграетЛучший()
    {
        var position = Position.FromFen(Position.StartFen);
        var result = Result("e2e4", ("e2e4", 30, null), ("d2d4", 20, null));

        var choice = OpponentMovePicker.Pick(result, position,
            Difficulty.For(DifficultyLevel.Maximum), new Random(1));

        Assert.True(choice.Found);
        Assert.Equal("e2e4", choice.Move.ToUci());
        Assert.Equal(OpponentPickKind.Best, choice.Kind);
        Assert.Equal(0, choice.LossCp);
    }

    [Fact(DisplayName = "Выбор хода: ход соперника всегда законен")]
    public void ХодВсегдаЗаконен()
    {
        var position = Position.FromFen(Position.StartFen);
        // Среди линий есть заведомо незаконный ход: движок мог отстать от позиции.
        var result = Result("e2e4", ("a1a8", 60, null), ("e2e4", 30, null),
            ("d2d4", 20, null), ("g1f3", 10, null), ("b1c3", 0, null));
        var random = new Random(11);

        foreach (var profile in Difficulty.All)
        {
            var resolved = Difficulty.Resolve(profile.Level, new DifficultyDefaults(3, true, 1500, 500, 3));
            for (var i = 0; i < 400; i++)
            {
                var choice = OpponentMovePicker.Pick(result, position, resolved, random);
                Assert.True(choice.Found, $"{profile.Title}: ход найден");
                Assert.Contains(choice.Move, position.LegalMoves);
                Assert.NotEqual("a1a8", choice.Move.ToUci());
            }
        }
    }

    [Fact(DisplayName = "Выбор хода: слабый уровень ошибается чаще сильного")]
    public void СлабыйОшибаетсяЧаще()
    {
        var position = Position.FromFen(Position.StartFen);
        var result = Result("e2e4", ("e2e4", 0, null), ("d2d4", -40, null), ("g1f3", -90, null));

        var weak = Share(Difficulty.For(DifficultyLevel.Beginner), result, position, "e2e4");
        var strong = Share(Difficulty.For(DifficultyLevel.Expert), result, position, "e2e4");

        Assert.InRange(weak, 0.0, 1.0);
        Assert.InRange(strong, 0.0, 1.0);
        Assert.True(weak < strong, $"новичок берёт лучший ход реже ({weak:P0} против {strong:P0})");
        Assert.True(weak > 0, "новичок всё-таки иногда находит лучший ход");
    }

    [Fact(DisplayName = "Выбор хода: сильный уровень не берёт провальные варианты")]
    public void ПерилаОтсекаютПровалы()
    {
        var position = Position.FromFen(Position.StartFen);
        var result = Result("e2e4", ("e2e4", 0, null), ("d2d4", -400, null));
        var profile = Difficulty.For(DifficultyLevel.Expert);
        var random = new Random(5);

        for (var i = 0; i < 500; i++)
        {
            Assert.Equal("e2e4", OpponentMovePicker.Pick(result, position, profile, random).Move.ToUci());
        }
    }

    [Fact(DisplayName = "Выбор хода: мат не вытесняет остальные варианты")]
    public void МатНеВытесняетОстальные()
    {
        var position = Position.FromFen(Position.StartFen);
        var result = Result("e2e4", ("e2e4", null, 1), ("d2d4", 250, null));
        var profile = Difficulty.For(DifficultyLevel.Beginner);
        var random = new Random(3);

        // Без поджатия оценки матовая линия отставала бы от соседа на сто тысяч и в одиночку
        // выносила бы его за перила на любом уровне.
        var moves = new HashSet<string>();
        for (var i = 0; i < 500; i++)
        {
            moves.Add(OpponentMovePicker.Pick(result, position, profile, random).Move.ToUci());
        }
        Assert.Contains("e2e4", moves);
        Assert.Contains("d2d4", moves);

        // Порядок предпочтения: мат в нашу пользу > ровная позиция > мат нам.
        var ordered = Result("e2e4", ("e2e4", null, 1), ("d2d4", 0, null), ("g1f3", null, -2));
        var strict = Weak(candidates: 3, temperature: 0, maxLoss: 100_000);
        Assert.Equal("e2e4", OpponentMovePicker.Pick(ordered, position, strict, new Random(1)).Move.ToUci());

        // «mate 0» приходит в уже заматованной позиции: это худшее, а не лучшее.
        var mated = Result("d2d4", ("e2e4", null, 0), ("d2d4", -300, null));
        Assert.Equal("d2d4", OpponentMovePicker.Pick(mated, position, strict, new Random(1)).Move.ToUci());
    }

    [Fact(DisplayName = "Выбор хода: без линий берётся ход движка")]
    public void БезЛинийБерётсяХодДвижка()
    {
        // Так ведут себя движки, не поддерживающие MultiPV.
        var position = Position.FromFen(Position.StartFen);
        var choice = OpponentMovePicker.Pick(new SearchResult { BestMove = "e2e4" }, position,
            Difficulty.For(DifficultyLevel.Beginner), new FixedRandom(0.99));

        Assert.Equal("e2e4", choice.Move.ToUci());
        Assert.Equal(OpponentPickKind.Best, choice.Kind);
    }

    [Fact(DisplayName = "Выбор хода: негодный ход движка отбрасывается")]
    public void НегодныйХодОтбрасывается()
    {
        var position = Position.FromFen(Position.StartFen);
        var profile = Difficulty.For(DifficultyLevel.Beginner);

        foreach (var best in new[] { string.Empty, "(none)", "0000", "a1a8" })
        {
            var choice = OpponentMovePicker.Pick(new SearchResult { BestMove = best }, position,
                profile, new FixedRandom(0.99));
            Assert.False(choice.Found, $"«{best}» ходом не считается");
            Assert.True(choice.Move.IsNone);
        }
    }

    [Fact(DisplayName = "Выбор хода: ход наугад берётся из законных")]
    public void ХодНаугадБерётсяИзЗаконных()
    {
        var position = Position.FromFen(Position.StartFen);
        var result = Result("e2e4", ("e2e4", 30, null));
        var profile = Weak(randomChance: 1.0);
        var random = new Random(7);

        var moves = new HashSet<string>();
        for (var i = 0; i < 500; i++)
        {
            var choice = OpponentMovePicker.Pick(result, position, profile, random);
            Assert.Equal(OpponentPickKind.Random, choice.Kind);
            Assert.Contains(choice.Move, position.LegalMoves);
            moves.Add(choice.Move.ToUci());
        }
        Assert.True(moves.Count > 10, $"наугад значит наугад, а не один ход (вышло {moves.Count})");
    }

    [Fact(DisplayName = "Выбор хода: одно зерно — один ход")]
    public void ОдноЗерноОдинХод()
    {
        var position = Position.FromFen(Position.StartFen);
        var result = Result("e2e4", ("e2e4", 0, null), ("d2d4", -40, null), ("g1f3", -90, null));
        var profile = Difficulty.For(DifficultyLevel.Beginner);

        var first = Sequence(profile, result, position, new Random(7));
        var second = Sequence(profile, result, position, new Random(7));
        Assert.Equal(first, second);
    }

    [Fact(DisplayName = "Выбор хода: оценки читаются от лица ходящей стороны")]
    public void ОценкиОтЛицаХодящего()
    {
        // UCI отдаёт оценку от лица стороны, которая ходит, а ходит сейчас соперник.
        // Перевод к белым (WhiteCp) перевернул бы предпочтение.
        var position = Position.FromFen(AfterE4);
        Assert.Equal(PieceColor.Black, position.SideToMove);

        var result = Result("e7e5", ("e7e5", -50, null), ("d7d5", 30, null));
        var profile = Weak(temperature: 0, maxLoss: 100_000);

        var choice = OpponentMovePicker.Pick(result, position, profile, new Random(1));
        Assert.Equal("d7d5", choice.Move.ToUci());
        Assert.Equal(0, choice.LossCp);
    }

    [Fact(DisplayName = "Выбор хода: кривые линии пропускаются")]
    public void КривыеЛинииПропускаются()
    {
        var position = Position.FromFen(Position.StartFen);
        // Пустой вариант и линия без оценки — не кандидаты: нечитаемую оценку нельзя
        // молча считать нулём, иначе плохой ход становится ровным.
        var result = Result("e2e4", (string.Empty, 90, null), ("d2d4", null, null), ("e2e4", 10, null));
        var profile = Weak(temperature: 0, maxLoss: 100_000);

        var choice = OpponentMovePicker.Pick(result, position, profile, new Random(1));
        Assert.Equal("e2e4", choice.Move.ToUci());
    }

    /// <summary>Доля прогонов, в которых выбран указанный ход.</summary>
    private static double Share(DifficultyProfile profile, SearchResult result, Position position, string uci)
    {
        var random = new Random(2024);
        var hits = 0;
        const int runs = 1000;
        for (var i = 0; i < runs; i++)
        {
            if (OpponentMovePicker.Pick(result, position, profile, random).Move.ToUci() == uci) hits++;
        }
        return hits / (double)runs;
    }

    private static List<string> Sequence(DifficultyProfile profile, SearchResult result,
        Position position, Random random)
    {
        var moves = new List<string>();
        for (var i = 0; i < 200; i++)
        {
            moves.Add(OpponentMovePicker.Pick(result, position, profile, random).Move.ToUci());
        }
        return moves;
    }
}
