using ChessEmulator.Chess;
using Xunit;

namespace ChessEmulator.CoreTests;

/// <summary>Шахматная нотация: запись хода и разбор записи.</summary>
public class SanTests(ITestOutputHelper output)
{
    /// <summary>Две белые ладьи на вертикали a — ход на a3 требует уточнения по горизонтали.</summary>
    private const string TwoRooks = "4k3/8/8/R7/8/8/8/R3K3 w - - 0 1";

    /// <summary>Ферзи a4, h4 и h1 бьют e4: для хода Qh4-e4 нужна полная клетка.</summary>
    private const string ThreeQueens = "1k6/8/8/8/Q6Q/8/8/6KQ w - - 0 1";

    private static string San(string fen, string uci)
    {
        var pos = Position.FromFen(fen);
        return pos.ToSan(Move.FromUci(uci));
    }

    [Fact(DisplayName = "SAN: запись хода")]
    public void Writing()
    {
        Assert.Equal("e4", San(Position.StartFen, "e2e4"));  // ход пешкой
        Assert.Equal("Nf3", San(Position.StartFen, "g1f3"));  // ход конём
        // взятие фигурой
        Assert.Equal("Bxf7+", San("rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR w KQkq - 0 3", "c4f7"));
        // взятие пешкой
        Assert.Equal("exd5", San("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2", "e4d5"));
        // взятие на проходе
        Assert.Equal("exd6", San("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "e5d6"));
        // короткая рокировка
        Assert.Equal("O-O", San("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1"));
        // длинная рокировка
        Assert.Equal("O-O-O", San("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1"));
        // рокировка чёрных
        Assert.Equal("O-O", San("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1", "e8g8"));
        Assert.Equal("a8=Q", San("8/P6k/8/8/8/8/8/4K3 w - - 0 1", "a7a8q"));  // превращение
        Assert.Equal("a8=N", San("8/P6k/8/8/8/8/8/4K3 w - - 0 1", "a7a8n"));  // превращение в коня
        // превращение со взятием и шахом
        Assert.Equal("axb8=Q+", San("1r5k/P7/8/8/8/8/8/4K3 w - - 0 1", "a7b8q"));
        Assert.Equal("Ra8+", San("6k1/5pp1/7p/8/8/8/5PPP/R5K1 w - - 0 1", "a1a8"));  // шах
        Assert.Equal("Ra8#", San("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1", "a1a8"));  // мат
        Assert.Equal("Kd2", San("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "e1d2"));  // ход королём
        // ход ферзём со взятием
        Assert.Equal("Qxd8+", San("3rk3/8/8/8/8/8/8/3QK3 w - - 0 1", "d1d8"));
        // ход с пустой клетки отдаётся как UCI
        Assert.Equal("a3a4", San(Position.StartFen, "a3a4"));

        Assert.Equal("N", Position.PieceLetter(PieceType.Knight).ToString());  // буква фигуры: конь
        Assert.Equal("P", Position.PieceLetter(PieceType.Pawn).ToString());  // буква фигуры: пешка
        Assert.Equal(PieceType.Queen, Position.LetterToPiece('q'));  // буква в тип
        Assert.Equal(PieceType.None, Position.LetterToPiece('z'));  // неизвестная буква
    }

    [Fact(DisplayName = "SAN: уточнение хода")]
    public void Disambiguation()
    {
        // Два коня на b1 и f3 ходят на d2 — различает вертикаль
        // уточнение по вертикали
        Assert.Equal("Nbd2", San("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1", "b1d2"));
        // уточнение по вертикали, второй конь
        Assert.Equal("Nfd2", San("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1", "f3d2"));

        // Две ладьи на одной вертикали — различает горизонталь
        Assert.Equal("R1a3", San(TwoRooks, "a1a3"));  // уточнение по горизонтали
        Assert.Equal("R5a3", San(TwoRooks, "a5a3"));  // уточнение по горизонтали, вторая ладья

        // Три ферзя бьют одно поле: одна на той же вертикали, другая на той же
        // горизонтали — не хватает ни буквы, ни цифры, нужна полная клетка
        Assert.Equal("Qh4e4", San(ThreeQueens, "h4e4"));  // полное уточнение

        Assert.Equal("Nf3", San(Position.StartFen, "g1f3"));  // уточнение не нужно

        // Связанный конь не может пойти на d5, поэтому уточнение не нужно
        // связанная фигура не требует уточнения
        Assert.Equal("Nd5", San("k3r3/8/8/8/8/2N1N3/8/4K3 w - - 0 1", "c3d5"));
    }

    [Fact(DisplayName = "SAN: разбор записи")]
    public void Parsing()
    {
        var start = Position.FromFen(Position.StartFen);
        Assert.True(start.TryParseSan("e4", out var e4), "ход пешкой");
        Assert.Equal("e2e4", e4.ToUci());  // разобран верно
        Assert.True(start.TryParseSan("Nf3", out var nf3), "ход конём");
        Assert.Equal("g1f3", nf3.ToUci());  // конь разобран верно

        var castle = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Assert.True(castle.TryParseSan("O-O", out var oo), "рокировка буквами O");
        Assert.Equal("e1g1", oo.ToUci());  // короткая рокировка
        Assert.True(castle.TryParseSan("0-0-0", out var ooo), "рокировка нулями");
        Assert.Equal("e1c1", ooo.ToUci());  // длинная рокировка

        var promo = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
        Assert.True(promo.TryParseSan("a8=Q", out var pq), "превращение с равенством");
        Assert.Equal("a7a8q", pq.ToUci());  // превращение разобрано
        Assert.True(promo.TryParseSan("a8N", out var pn), "превращение без равенства");
        Assert.Equal("a7a8n", pn.ToUci());  // превращение в коня разобрано

        var ep = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
        Assert.True(ep.TryParseSan("exd6 e.p.", out var epMove), "пометка e.p. допускается");
        Assert.Equal("e5d6", epMove.ToUci());  // взятие на проходе разобрано

        Assert.True(start.TryParseSan("Nf3!", out _), "знак ! отбрасывается");
        Assert.True(start.TryParseSan("Nf3!?", out _), "знаки !? отбрасываются");
        Assert.True(start.TryParseSan("e4??", out _), "знак ?? отбрасывается");
        Assert.True(start.TryParseSan("  e4  ", out _), "лишние пробелы");
        Assert.True(start.TryParseSan("Ng1-f3", out var longMove), "длинная нотация с дефисом");
        Assert.Equal("g1f3", longMove.ToUci());  // длинная нотация разобрана

        var check = Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1");
        Assert.True(check.TryParseSan("Ra8#", out var mateMove), "знак мата отбрасывается");
        Assert.Equal("a1a8", mateMove.ToUci());  // ход мата разобран
        Assert.True(check.TryParseSan("Ra8+", out _), "знак шаха отбрасывается");

        var two = Position.FromFen("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1");
        Assert.True(two.TryParseSan("Nbd2", out var nbd2), "уточнение по вертикали разбирается");
        Assert.Equal("b1d2", nbd2.ToUci());  // выбран нужный конь
        Assert.True(Position.FromFen(TwoRooks).TryParseSan("R1a3", out var r1a3), "уточнение по горизонтали разбирается");
        Assert.Equal("a1a3", r1a3.ToUci());  // выбрана нужная ладья
        Assert.True(Position.FromFen(ThreeQueens).TryParseSan("Qh4e4", out var qh4e4), "полное уточнение разбирается");
        Assert.Equal("h4e4", qh4e4.ToUci());  // выбран нужный ферзь

        Assert.False(start.TryParseSan("e5", out _), "нелегальный ход не разбирается");
        Assert.False(start.TryParseSan("привет", out _), "мусор не разбирается");
        Assert.False(start.TryParseSan("", out _), "пустая строка не разбирается");
        Assert.False(start.TryParseSan("   ", out _), "пробелы не разбираются");
        Assert.False(start.TryParseSan("O-O", out _), "рокировка без прав");
        Assert.False(start.TryParseSan("e", out _), "слишком короткая запись");
    }

    /// <summary>
    /// Главная проверка нотации: для каждой позиции случайной партии каждый легальный ход
    /// записывается в SAN и разбирается обратно — должен получиться тот же ход.
    /// Это ловит любые ошибки уточнения (две ладьи, два коня, превращения, взятия).
    /// </summary>
    [Fact(DisplayName = "SAN: полный круг на случайных партиях")]
    public void RoundTrip()
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

        Assert.True(positions > 1500, "проверено достаточно позиций");
        Assert.Equal(0, bad);  // записей, которые не разобрались обратно
        Assert.Equal(0, ambiguous);  // неоднозначных записей
        if (badExamples.Count > 0) Assert.Fail("примеры: " + string.Join("; ", badExamples));
        output.WriteLine($"разобрано {moves:N0} записей SAN в {positions:N0} позициях");
    }
}
