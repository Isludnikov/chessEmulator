using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>Шахматная нотация: запись хода и разбор записи.</summary>
internal static class SanTests
{
    public static void Run()
    {
        Writing();
        Disambiguation();
        Parsing();
        RoundTrip();
    }

    /// <summary>Две белые ладьи на вертикали a — ход на a3 требует уточнения по горизонтали.</summary>
    private const string TwoRooks = "4k3/8/8/R7/8/8/8/R3K3 w - - 0 1";

    /// <summary>Ферзи a4, h4 и h1 бьют e4: для хода Qh4-e4 нужна полная клетка.</summary>
    private const string ThreeQueens = "1k6/8/8/8/Q6Q/8/8/6KQ w - - 0 1";

    private static string San(string fen, string uci)
    {
        var pos = Position.FromFen(fen);
        return pos.ToSan(Move.FromUci(uci));
    }

    private static void Writing()
    {
        Test.Suite("SAN: запись хода", () =>
        {
            Test.Check("ход пешкой", "e4", San(Position.StartFen, "e2e4"));
            Test.Check("ход конём", "Nf3", San(Position.StartFen, "g1f3"));
            Test.Check("взятие фигурой", "Bxf7+",
                San("rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR w KQkq - 0 3", "c4f7"));
            Test.Check("взятие пешкой", "exd5",
                San("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2", "e4d5"));
            Test.Check("взятие на проходе", "exd6",
                San("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "e5d6"));
            Test.Check("короткая рокировка", "O-O",
                San("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1"));
            Test.Check("длинная рокировка", "O-O-O",
                San("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1"));
            Test.Check("рокировка чёрных", "O-O",
                San("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1", "e8g8"));
            Test.Check("превращение", "a8=Q", San("8/P6k/8/8/8/8/8/4K3 w - - 0 1", "a7a8q"));
            Test.Check("превращение в коня", "a8=N", San("8/P6k/8/8/8/8/8/4K3 w - - 0 1", "a7a8n"));
            Test.Check("превращение со взятием и шахом", "axb8=Q+",
                San("1r5k/P7/8/8/8/8/8/4K3 w - - 0 1", "a7b8q"));
            Test.Check("шах", "Ra8+", San("6k1/5pp1/7p/8/8/8/5PPP/R5K1 w - - 0 1", "a1a8"));
            Test.Check("мат", "Ra8#", San("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", "a1a8"));
            Test.Check("ход королём", "Kd2", San("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "e1d2"));
            Test.Check("ход ферзём со взятием", "Qxd8+",
                San("3rk3/8/8/8/8/8/8/3QK3 w - - 0 1", "d1d8"));
            Test.Check("ход с пустой клетки отдаётся как UCI", "a3a4",
                San(Position.StartFen, "a3a4"));

            Test.Check("буква фигуры: конь", "N", Position.PieceLetter(PieceType.Knight).ToString());
            Test.Check("буква фигуры: пешка", "P", Position.PieceLetter(PieceType.Pawn).ToString());
            Test.Check("буква в тип", PieceType.Queen, Position.LetterToPiece('q'));
            Test.Check("неизвестная буква", PieceType.None, Position.LetterToPiece('z'));
        });
    }

    private static void Disambiguation()
    {
        Test.Suite("SAN: уточнение хода", () =>
        {
            // Два коня на b1 и f3 ходят на d2 — различает вертикаль
            Test.Check("уточнение по вертикали", "Nbd2",
                San("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1", "b1d2"));
            Test.Check("уточнение по вертикали, второй конь", "Nfd2",
                San("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1", "f3d2"));

            // Две ладьи на одной вертикали — различает горизонталь
            Test.Check("уточнение по горизонтали", "R1a3", San(TwoRooks, "a1a3"));
            Test.Check("уточнение по горизонтали, вторая ладья", "R5a3", San(TwoRooks, "a5a3"));

            // Три ферзя бьют одно поле: одна на той же вертикали, другая на той же
            // горизонтали — не хватает ни буквы, ни цифры, нужна полная клетка
            Test.Check("полное уточнение", "Qh4e4", San(ThreeQueens, "h4e4"));

            Test.Check("уточнение не нужно", "Nf3", San(Position.StartFen, "g1f3"));

            // Связанный конь не может пойти на d5, поэтому уточнение не нужно
            Test.Check("связанная фигура не требует уточнения", "Nd5",
                San("k3r3/8/8/8/8/2N1N3/8/4K3 w - - 0 1", "c3d5"));
        });
    }

    private static void Parsing()
    {
        Test.Suite("SAN: разбор записи", () =>
        {
            var start = Position.FromFen(Position.StartFen);
            Test.True("ход пешкой", start.TryParseSan("e4", out var e4));
            Test.Check("разобран верно", "e2e4", e4.ToUci());
            Test.True("ход конём", start.TryParseSan("Nf3", out var nf3));
            Test.Check("конь разобран верно", "g1f3", nf3.ToUci());

            var castle = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Test.True("рокировка буквами O", castle.TryParseSan("O-O", out var oo));
            Test.Check("короткая рокировка", "e1g1", oo.ToUci());
            Test.True("рокировка нулями", castle.TryParseSan("0-0-0", out var ooo));
            Test.Check("длинная рокировка", "e1c1", ooo.ToUci());

            var promo = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
            Test.True("превращение с равенством", promo.TryParseSan("a8=Q", out var pq));
            Test.Check("превращение разобрано", "a7a8q", pq.ToUci());
            Test.True("превращение без равенства", promo.TryParseSan("a8N", out var pn));
            Test.Check("превращение в коня разобрано", "a7a8n", pn.ToUci());

            var ep = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
            Test.True("пометка e.p. допускается", ep.TryParseSan("exd6 e.p.", out var epMove));
            Test.Check("взятие на проходе разобрано", "e5d6", epMove.ToUci());

            Test.True("знак ! отбрасывается", start.TryParseSan("Nf3!", out _));
            Test.True("знаки !? отбрасываются", start.TryParseSan("Nf3!?", out _));
            Test.True("знак ?? отбрасывается", start.TryParseSan("e4??", out _));
            Test.True("лишние пробелы", start.TryParseSan("  e4  ", out _));
            Test.True("длинная нотация с дефисом", start.TryParseSan("Ng1-f3", out var longMove));
            Test.Check("длинная нотация разобрана", "g1f3", longMove.ToUci());

            var check = Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1");
            Test.True("знак мата отбрасывается", check.TryParseSan("Ra8#", out var mateMove));
            Test.Check("ход мата разобран", "a1a8", mateMove.ToUci());
            Test.True("знак шаха отбрасывается", check.TryParseSan("Ra8+", out _));

            var two = Position.FromFen("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1");
            Test.True("уточнение по вертикали разбирается", two.TryParseSan("Nbd2", out var nbd2));
            Test.Check("выбран нужный конь", "b1d2", nbd2.ToUci());
            Test.True("уточнение по горизонтали разбирается",
                Position.FromFen(TwoRooks).TryParseSan("R1a3", out var r1a3));
            Test.Check("выбрана нужная ладья", "a1a3", r1a3.ToUci());
            Test.True("полное уточнение разбирается",
                Position.FromFen(ThreeQueens).TryParseSan("Qh4e4", out var qh4e4));
            Test.Check("выбран нужный ферзь", "h4e4", qh4e4.ToUci());

            Test.False("нелегальный ход не разбирается", start.TryParseSan("e5", out _));
            Test.False("мусор не разбирается", start.TryParseSan("привет", out _));
            Test.False("пустая строка не разбирается", start.TryParseSan("", out _));
            Test.False("пробелы не разбираются", start.TryParseSan("   ", out _));
            Test.False("рокировка без прав", start.TryParseSan("O-O", out _));
            Test.False("слишком короткая запись", start.TryParseSan("e", out _));
        });
    }

    /// <summary>
    /// Главная проверка нотации: для каждой позиции случайной партии каждый легальный ход
    /// записывается в SAN и разбирается обратно — должен получиться тот же ход.
    /// Это ловит любые ошибки уточнения (две ладьи, два коня, превращения, взятия).
    /// </summary>
    private static void RoundTrip()
    {
        Test.Suite("SAN: полный круг на случайных партиях", () =>
        {
            var rng = new Random(20260919);
            int positions = 0, moves = 0, bad = 0, ambiguous = 0;
            var badExamples = new List<string>();

            for (var game = 0; game < 40; game++)
            {
                var pos = Position.FromFen(Position.StartFen);
                for (var ply = 0; ply < 60 && pos.LegalMoves.Count > 0; ply++)
                {
                    positions++;
                    var sans = new Dictionary<string, Move>();
                    foreach (var move in pos.LegalMoves)
                    {
                        moves++;
                        var san = pos.ToSan(move);
                        if (sans.ContainsKey(san))
                        {
                            ambiguous++;
                            if (badExamples.Count < 3) badExamples.Add($"{pos.ToFen()} → две записи «{san}»");
                        }
                        else
                        {
                            sans[san] = move;
                        }

                        if (!pos.TryParseSan(san, out var parsed) || parsed != move)
                        {
                            bad++;
                            if (badExamples.Count < 3)
                                badExamples.Add($"{pos.ToFen()} → «{san}» вместо {move.ToUci()}");
                        }
                    }
                    pos = pos.MakeMove(pos.LegalMoves[rng.Next(pos.LegalMoves.Count)]);
                }
            }

            Test.True("проверено достаточно позиций", positions > 1500);
            Test.Check("записей, которые не разобрались обратно", 0, bad);
            Test.Check("неоднозначных записей", 0, ambiguous);
            if (badExamples.Count > 0) Test.Fail("примеры: " + string.Join("; ", badExamples));
            Console.WriteLine($"        разобрано {moves:N0} записей SAN в {positions:N0} позициях");
        });
    }
}
