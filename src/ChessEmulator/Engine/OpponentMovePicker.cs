using ChessEmulator.Chess;

namespace ChessEmulator.Engine;

/// <summary>Чем именно выбран ход соперника.</summary>
public enum OpponentPickKind
{
    Best,
    Weighted,
    Random
}

/// <summary>Ход соперника и то, как он выбран. LossCp — отставание от лучшей линии.</summary>
public readonly record struct OpponentChoice(Move Move, OpponentPickKind Kind, int LossCp)
{
    public static readonly OpponentChoice None = new(Move.None, OpponentPickKind.Best, 0);

    public bool Found => !Move.IsNone;
}

/// <summary>
/// Выбирает ход соперника из результата поиска. Чистый класс: ни движка, ни интерфейса,
/// ни времени — только результат, позиция, профиль и датчик случайных чисел. Поэтому
/// всё поведение слабых уровней проверяется тестами без запуска процесса.
/// </summary>
public static class OpponentMovePicker
{
    /// <summary>Мат в условных сотых долях пешки.</summary>
    public const int MateValue = 100_000;

    /// <summary>Потолок оценки: за десятью пешками разница уже не имеет игрового смысла.</summary>
    public const int ScoreClampCp = 1000;

    public static OpponentChoice Pick(SearchResult result, Position position,
        DifficultyProfile profile, Random random)
    {
        if (position.LegalMoves.Count == 0) return OpponentChoice.None;

        var engineMove = Legalize(result.BestMove, position);

        if (profile.IsFullStrength)
        {
            return engineMove.IsNone
                ? OpponentChoice.None
                : new OpponentChoice(engineMove, OpponentPickKind.Best, 0);
        }

        // Бросок делается всегда, когда уровень это допускает: так последовательность
        // случайных чисел не зависит от того, что прислал движок, и тесты повторимы.
        if (profile.RandomMoveChance > 0 && random.NextDouble() < profile.RandomMoveChance)
        {
            var moves = position.LegalMoves;
            return new OpponentChoice(moves[random.Next(moves.Count)], OpponentPickKind.Random, 0);
        }

        var candidates = Candidates(result, position, profile.CandidateCount);
        if (candidates.Count == 0)
        {
            return engineMove.IsNone
                ? OpponentChoice.None
                : new OpponentChoice(engineMove, OpponentPickKind.Best, 0);
        }

        var top = candidates[0].Score;
        foreach (var candidate in candidates)
        {
            if (candidate.Score > top) top = candidate.Score;
        }

        // Перила: лучший кандидат проходит всегда, поэтому список пустым не станет.
        var allowed = new List<Candidate>();
        foreach (var candidate in candidates)
        {
            if (top - candidate.Score <= profile.MaxLossCp) allowed.Add(candidate);
        }

        if (profile.TemperatureCp <= 0 || allowed.Count == 1)
        {
            // Порядок MultiPV — не закон: берём лучшего по оценке, а не первого по номеру.
            var best = allowed[0];
            foreach (var candidate in allowed)
            {
                if (candidate.Score > best.Score) best = candidate;
            }
            return new OpponentChoice(best.Move, OpponentPickKind.Best, top - best.Score);
        }

        // Вес лучшего кандидата ровно 1, остальных — в (0, 1]: переполнения не бывает.
        var weights = new double[allowed.Count];
        var sum = 0.0;
        for (var i = 0; i < allowed.Count; i++)
        {
            weights[i] = Math.Exp(-(top - allowed[i].Score) / (double)profile.TemperatureCp);
            sum += weights[i];
        }

        var roll = random.NextDouble() * sum;
        for (var i = 0; i < allowed.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
            {
                return new OpponentChoice(allowed[i].Move, OpponentPickKind.Weighted,
                    top - allowed[i].Score);
            }
        }

        var last = allowed[^1];
        return new OpponentChoice(last.Move, OpponentPickKind.Weighted, top - last.Score);
    }

    private readonly record struct Candidate(Move Move, int Score);

    private static List<Candidate> Candidates(SearchResult result, Position position, int limit)
    {
        var list = new List<Candidate>();
        var max = Math.Max(1, limit);

        foreach (var pair in result.Lines.OrderBy(p => p.Key))
        {
            // Движок вправе прислать больше линий, чем мы просили: Stockfish при ограничении
            // силы сам поднимает MultiPV. Поэтому режем по профилю, а не по числу линий.
            if (list.Count >= max) break;

            var info = pair.Value;
            if (info.Pv.Length == 0) continue;

            var move = Legalize(info.Pv[0], position);
            if (move.IsNone) continue;
            if (ScoreOf(info) is not { } score) continue;
            if (list.Any(c => c.Move.Equals(move))) continue;

            list.Add(new Candidate(move, score));
        }

        return list;
    }

    /// <summary>
    /// Оценка от лица стороны, которая ходит, — именно так её присылает движок, и именно
    /// эта сторона сейчас соперник. Переводить к белым (WhiteCp) здесь нельзя.
    /// </summary>
    private static int? ScoreOf(EngineInfo info)
    {
        if (info.ScoreMate is { } mate)
        {
            // «mate 0» приходит в уже заматованной позиции: это худшее, а не лучшее.
            var raw = mate switch
            {
                > 0 => MateValue - mate,
                0 => -MateValue,
                _ => -MateValue - mate
            };

            // Без поджатия матовая линия отставала бы от соседей на сто тысяч и в одиночку
            // выносила бы всё остальное за перила на любом уровне.
            return Math.Clamp(raw, -ScoreClampCp, ScoreClampCp);
        }

        return info.ScoreCp is { } cp ? Math.Clamp(cp, -ScoreClampCp, ScoreClampCp) : null;
    }

    private static Move Legalize(string? uci, Position position)
    {
        if (string.IsNullOrWhiteSpace(uci)) return Move.None;
        var raw = Move.FromUci(uci);
        return position.TryFindMove(raw.From, raw.To, raw.Promotion, out var move) ? move : Move.None;
    }
}
