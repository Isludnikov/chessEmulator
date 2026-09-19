using System.Diagnostics;
using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>
/// Perft — подсчёт числа листьев дерева ходов на заданной глубине.
/// Эталонные значения взяты из общепринятого набора позиций (Kiwipete и набор Мартина Седлака)
/// и сверены с ответами Stockfish на команду «go perft».
/// Любая ошибка в генерации ходов, рокировках, взятии на проходе или превращении ломает счёт.
/// </summary>
internal static class PerftTests
{
    private sealed record Case(string Name, string Fen, int Depth, long Nodes, bool Slow = false);

    private static readonly Case[] Cases =
    {
        // Классические позиции
        new("начальная, глубина 1", Position.StartFen, 1, 20),
        new("начальная, глубина 2", Position.StartFen, 2, 400),
        new("начальная, глубина 3", Position.StartFen, 3, 8902),
        new("начальная, глубина 4", Position.StartFen, 4, 197281),
        new("начальная, глубина 5", Position.StartFen, 5, 4865609, Slow: true),

        new("kiwipete, глубина 1", Kiwipete, 1, 48),
        new("kiwipete, глубина 2", Kiwipete, 2, 2039),
        new("kiwipete, глубина 3", Kiwipete, 3, 97862),
        new("kiwipete, глубина 4", Kiwipete, 4, 4085603, Slow: true),

        new("позиция 3, глубина 4", Pos3, 4, 43238),
        new("позиция 3, глубина 5", Pos3, 5, 674624, Slow: true),

        new("позиция 4, глубина 3", Pos4, 3, 9467),
        new("позиция 4, глубина 4", Pos4, 4, 422333, Slow: true),
        new("позиция 4 в зеркале, глубина 3", Pos4Mirror, 3, 9467),

        new("позиция 5, глубина 3", Pos5, 3, 62379),
        new("позиция 5, глубина 4", Pos5, 4, 2103487, Slow: true),

        new("позиция 6, глубина 3", Pos6, 3, 89890),
        new("позиция 6, глубина 4", Pos6, 4, 3894594, Slow: true),

        // Тонкие случаи правил
        new("взятие на проходе оставляет короля под шахом", "3k4/3p4/8/K1P4r/8/8/8/8 b - - 0 1", 5, 185429),
        new("взятие на проходе вскрывает слона", "8/8/4k3/8/2p5/8/B2P2K1/8 w - - 0 1", 5, 135655),
        new("взятие на проходе с шахом", "8/8/1k6/2b5/2pP4/8/5K2/8 b - d3 0 1", 5, 206379),
        new("короткая рокировка с шахом", "5k2/8/8/8/8/8/8/4K2R w K - 0 1", 5, 120330),
        new("длинная рокировка с шахом", "3k4/8/8/8/8/8/8/R3K3 w Q - 0 1", 5, 141077),
        new("права на рокировку", "r3k2r/1b4bq/8/8/8/8/7B/R3K2R w KQkq - 0 1", 3, 27826),
        new("рокировка запрещена шахом", "r3k2r/8/3Q4/8/8/5q2/8/R3K2R b KQkq - 0 1", 3, 50509),
        new("превращение уходом из-под шаха", "2K2r2/4P3/8/8/8/8/8/3k4 w - - 0 1", 4, 19174),
        new("вскрытый шах", "8/8/1P2K3/8/2n5/1q6/8/5k2 b - - 0 1", 4, 31961),
        new("превращение с шахом", "4k3/1P6/8/8/8/8/K7/8 w - - 0 1", 6, 217342),
        new("превращение в лёгкую фигуру с шахом", "8/P1k5/K7/8/8/8/8/8 w - - 0 1", 6, 92683),
        new("пат самому себе", "K1k5/8/P7/8/8/8/8/8 w - - 0 1", 6, 2217),
        new("пат и мат рядом", "8/k1P5/8/1K6/8/8/8/8 w - - 0 1", 6, 43261),
        new("мат в углу", "8/8/2k5/5q2/5n2/8/5K2/8 b - - 0 1", 4, 23527),

        // Глубокие прогоны тех же тонких случаев — только с ключом --full
        new("взятие на проходе оставляет короля под шахом, глубина 6", "3k4/3p4/8/K1P4r/8/8/8/8 b - - 0 1", 6, 1134888, Slow: true),
        new("взятие на проходе вскрывает слона, глубина 6", "8/8/4k3/8/2p5/8/B2P2K1/8 w - - 0 1", 6, 1015133, Slow: true),
        new("взятие на проходе с шахом, глубина 6", "8/8/1k6/2b5/2pP4/8/5K2/8 b - d3 0 1", 6, 1440467, Slow: true),
        new("короткая рокировка с шахом, глубина 6", "5k2/8/8/8/8/8/8/4K2R w K - 0 1", 6, 661072, Slow: true),
        new("длинная рокировка с шахом, глубина 6", "3k4/8/8/8/8/8/8/R3K3 w Q - 0 1", 6, 803711, Slow: true),
        new("права на рокировку, глубина 4", "r3k2r/1b4bq/8/8/8/8/7B/R3K2R w KQkq - 0 1", 4, 1274206, Slow: true),
        new("рокировка запрещена шахом, глубина 4", "r3k2r/8/3Q4/8/8/5q2/8/R3K2R b KQkq - 0 1", 4, 1720476, Slow: true),
        new("превращение уходом из-под шаха, глубина 6", "2K2r2/4P3/8/8/8/8/8/3k4 w - - 0 1", 6, 3821001, Slow: true),
        new("вскрытый шах, глубина 5", "8/8/1P2K3/8/2n5/1q6/8/5k2 b - - 0 1", 5, 1004658, Slow: true),
        new("пат и мат рядом, глубина 7", "8/k1P5/8/1K6/8/8/8/8 w - - 0 1", 7, 567584, Slow: true)
    };

    public const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    public const string Pos3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    public const string Pos4 = "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1";
    public const string Pos4Mirror = "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1";
    public const string Pos5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    public const string Pos6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    public static long Perft(Position position, int depth)
    {
        if (depth == 0) return 1;
        var moves = position.LegalMoves;
        if (depth == 1) return moves.Count;

        long total = 0;
        foreach (var move in moves) total += Perft(position.MakeMove(move), depth - 1);
        return total;
    }

    public static void Run()
    {
        var clock = Stopwatch.StartNew();
        long nodes = 0;
        var skipped = 0;

        Test.Suite("Правила: perft", () =>
        {
            foreach (var c in Cases)
            {
                if (c.Slow && !Test.Full)
                {
                    skipped++;
                    continue;
                }
                var actual = Perft(Position.FromFen(c.Fen), c.Depth);
                nodes += actual;
                Test.Check(c.Name, c.Nodes, actual);
            }
        });

        var seconds = Math.Max(0.001, clock.Elapsed.TotalSeconds);
        Console.WriteLine($"        {nodes:N0} узлов за {seconds:0.0} с ({nodes / seconds / 1000:N0} тыс. узлов/с)" +
                          (skipped > 0 ? $"; пропущено глубоких прогонов: {skipped} (см. --full)" : string.Empty));
    }
}
