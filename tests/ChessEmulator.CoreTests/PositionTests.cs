using ChessEmulator.Chess;
using Xunit;

namespace ChessEmulator.CoreTests;

/// <summary>Позиция: FEN, атаки, применение хода, ничейный материал.</summary>
public class PositionTests
{
    // ------------------------------------------------------------------ FEN

    [Fact(DisplayName = "Позиция: FEN")]
    public void Fen()
    {
        string[] samples =
        {
            Position.StartFen,
            PerftTests.Kiwipete,
            PerftTests.Pos3,
            PerftTests.Pos4,
            PerftTests.Pos5,
            PerftTests.Pos6,
            "8/8/8/8/8/8/8/8 w - - 0 1",
            "4k3/8/8/3pP3/8/8/8/4K3 w - d6 13 42",
            "r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 99 250"
        };
        var mismatches = 0;
        foreach (var fen in samples)
            if (Position.FromFen(fen).ToFen() != fen) mismatches++;
        Assert.Equal(0, mismatches);  // полный круг FEN на девяти позициях

        var pos = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 13 42");
        Assert.Equal(PieceColor.White, pos.SideToMove);  // очередь хода
        Assert.Equal(PieceColor.Black, pos.Opponent);  // противник
        Assert.Equal("d6", Sq.Name(pos.EnPassant));  // поле взятия на проходе
        Assert.Equal(13, pos.HalfmoveClock);  // счётчик полуходов
        Assert.Equal(42, pos.FullmoveNumber);  // номер хода
        Assert.Equal("P", pos[Sq.Parse("e5")].ToFenChar().ToString());  // фигура по индексу
        Assert.Equal("p", pos.At(3, 4).ToFenChar().ToString());  // фигура по координатам
        Assert.True(pos[Sq.Parse("a1")].IsEmpty, "пустая клетка");

        var rights = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w Qk - 0 1");
        Assert.Equal(CastlingRights.WhiteQueen | CastlingRights.BlackKing, rights.Castling);  // разбор прав рокировки
        Assert.Equal("Qk", CastlingField(rights.ToFen()));  // запись прав рокировки
        // нет прав рокировки
        Assert.Equal(CastlingRights.None, Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").Castling);

        // Необязательные поля FEN
        var shortFen = Position.FromFen("4k3/8/8/8/8/8/8/4K3 b");
        Assert.Equal(PieceColor.Black, shortFen.SideToMove);  // FEN без прав рокировки: очередь хода
        Assert.Equal(0, shortFen.HalfmoveClock);  // FEN без счётчиков: полуходы
        Assert.Equal(1, shortFen.FullmoveNumber);  // FEN без счётчиков: номер хода
        // нулевой номер хода поднимается до 1
        Assert.Equal(1, Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 0").FullmoveNumber);
        // нечисловые счётчики игнорируются
        Assert.Equal(0, Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - x y").HalfmoveClock);

        Assert.Throws<FormatException>(() => Position.FromFen("не-фен"));  // строка без пробелов отвергается
        Assert.Throws<FormatException>(() => Position.FromFen(""));  // пустая строка отвергается

        Assert.Equal(Position.StartFen, Position.FromFen(Position.StartFen).ToString());  // ToString даёт FEN

        // Ключ повторения — FEN без двух счётчиков
        // ключ повторения отбрасывает счётчики
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - -", Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 13 42").RepetitionKey());
        // ключ повторения не зависит от счётчиков
        Assert.Equal(Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").RepetitionKey(), Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 55 99").RepetitionKey());
        Assert.True(Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").RepetitionKey() !=
            Position.FromFen("4k3/8/8/8/8/8/8/4K3 b - - 0 1").RepetitionKey(), "ключ повторения различает очередь хода");

        var clone = Position.FromFen(PerftTests.Kiwipete).Clone();
        Assert.Equal(PerftTests.Kiwipete, clone.ToFen());  // копия совпадает по FEN
    }

    /// <summary>Есть ли у белых короткая рокировка (ход королём e1-g1, а не ход ладьёй на g1).</summary>
    private static bool Castles(Position position) =>
        position.LegalMoves.Any(m => m.From == Sq.Parse("e1") && m.To == Sq.Parse("g1"));

    /// <summary>Третье поле FEN — права на рокировку.</summary>
    private static string CastlingField(string fen) => fen.Split(new[] { ' ' })[2];

    // ---------------------------------------------------------------- Атаки

    [Fact(DisplayName = "Позиция: атаки и шахи")]
    public void Attacks()
    {
        var pos = Position.FromFen("4k3/8/8/3N4/8/8/8/4K3 w - - 0 1");
        Assert.True(pos.IsAttacked(Sq.Parse("e7"), PieceColor.White), "конь бьёт e7");
        Assert.True(pos.IsAttacked(Sq.Parse("c7"), PieceColor.White), "конь бьёт c7");
        Assert.False(pos.IsAttacked(Sq.Parse("d6"), PieceColor.White), "конь не бьёт d6");

        var pawns = Position.FromFen("7k/8/8/8/8/8/4P3/K7 w - - 0 1");
        Assert.True(pawns.IsAttacked(Sq.Parse("d3"), PieceColor.White), "белая пешка бьёт d3");
        Assert.True(pawns.IsAttacked(Sq.Parse("f3"), PieceColor.White), "белая пешка бьёт f3");
        Assert.False(pawns.IsAttacked(Sq.Parse("e3"), PieceColor.White), "белая пешка не бьёт перед собой");
        Assert.False(pawns.IsAttacked(Sq.Parse("d1"), PieceColor.White), "белая пешка не бьёт назад");

        var blackPawn = Position.FromFen("k7/4p3/8/8/8/8/8/7K b - - 0 1");
        Assert.True(blackPawn.IsAttacked(Sq.Parse("d6"), PieceColor.Black), "чёрная пешка бьёт вниз");
        Assert.False(blackPawn.IsAttacked(Sq.Parse("d8"), PieceColor.Black), "чёрная пешка не бьёт вверх");

        var sliders = Position.FromFen("4k3/8/8/8/8/2B5/8/R3K3 w - - 0 1");
        Assert.True(sliders.IsAttacked(Sq.Parse("a8"), PieceColor.White), "ладья бьёт вдоль вертикали");
        Assert.True(sliders.IsAttacked(Sq.Parse("f6"), PieceColor.White), "слон бьёт по диагонали");

        var blocked = Position.FromFen("4k3/8/8/8/8/8/3p4/R2QK3 w - - 0 1");
        Assert.False(blocked.IsAttacked(Sq.Parse("d8"), PieceColor.White), "ферзь не бьёт сквозь пешку");
        Assert.True(blocked.IsAttacked(Sq.Parse("d2"), PieceColor.White), "ферзь бьёт саму пешку");

        var king = Position.FromFen("8/8/8/8/8/8/8/4K2k w - - 0 1");
        Assert.True(king.IsAttacked(Sq.Parse("d2"), PieceColor.White), "король бьёт соседнюю клетку");
        Assert.False(king.IsAttacked(Sq.Parse("e4"), PieceColor.White), "король не бьёт через клетку");

        var check = Position.FromFen("4k3/8/8/8/8/8/8/4R1K1 b - - 0 1");
        Assert.True(check.IsInCheck(), "чёрный король под шахом");
        Assert.True(check.IsInCheck(PieceColor.Black), "шах по цвету");
        Assert.False(check.IsInCheck(PieceColor.White), "белый король не под шахом");

        // король найден
        Assert.Equal(Sq.Parse("e1"), Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").FindKing(PieceColor.White));
        // нет короля — Sq.None
        Assert.Equal(Sq.None, Position.FromFen("4k3/8/8/8/8/8/8/8 w - - 0 1").FindKing(PieceColor.White));
        Assert.False(Position.FromFen("4k3/8/8/8/8/8/8/8 w - - 0 1").IsInCheck(PieceColor.White), "без короля шаха нет");
    }

    // -------------------------------------------------- Ход и его следствия

    [Fact(DisplayName = "Позиция: применение хода")]
    public void MakeMove()
    {
        var start = Position.FromFen(Position.StartFen);
        var afterE4 = start.MakeMove(Move.FromUci("e2e4"));
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", afterE4.ToFen());  // после 1.e4
        Assert.Equal("e3", Sq.Name(afterE4.EnPassant));  // двойной ход задаёт поле взятия на проходе
        // номер хода растёт после хода чёрных
        Assert.Equal(2, afterE4.MakeMove(Move.FromUci("e7e5")).FullmoveNumber);

        // Рокировки: все четыре
        var rook = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        // короткая рокировка белых
        Assert.Equal("r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1", rook.MakeMove(Move.FromUci("e1g1")).ToFen());
        // длинная рокировка белых
        Assert.Equal("r3k2r/8/8/8/8/8/8/2KR3R b kq - 1 1", rook.MakeMove(Move.FromUci("e1c1")).ToFen());

        var rookBlack = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1");
        // короткая рокировка чёрных
        Assert.Equal("r4rk1/8/8/8/8/8/8/R3K2R w KQ - 1 2", rookBlack.MakeMove(Move.FromUci("e8g8")).ToFen());
        // длинная рокировка чёрных
        Assert.Equal("2kr3r/8/8/8/8/8/8/R3K2R w KQ - 1 2", rookBlack.MakeMove(Move.FromUci("e8c8")).ToFen());

        // Права на рокировку
        // ход королём снимает оба права белых
        Assert.Equal("kq", CastlingField(rook.MakeMove(Move.FromUci("e1e2")).ToFen()));
        // ход ладьёй h1 снимает короткую
        Assert.Equal("Qkq", CastlingField(rook.MakeMove(Move.FromUci("h1h2")).ToFen()));
        // ход ладьёй a1 снимает длинную
        Assert.Equal("Kkq", CastlingField(rook.MakeMove(Move.FromUci("a1a2")).ToFen()));
        // взятие ладьи на h8 снимает права обеих сторон
        Assert.Equal("Qq", CastlingField(rook.MakeMove(Move.FromUci("h1h8")).ToFen()));

        // Взятие на проходе
        var ep = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
        var afterEp = ep.MakeMove(Move.FromUci("e5d6"));
        Assert.Equal("4k3/8/3P4/8/8/8/8/4K3 b - - 0 2", afterEp.ToFen());  // взятие на проходе
        Assert.True(ep.IsEnPassantMove(Move.FromUci("e5d6")), "взятие на проходе распознано");
        Assert.True(ep.IsCapture(Move.FromUci("e5d6")), "взятие на проходе — взятие");
        Assert.False(ep.IsCapture(Move.FromUci("e5e6")), "тихий ход не взятие");

        var epBlack = Position.FromFen("4k3/8/8/8/3Pp3/8/8/4K3 b - d3 0 1");
        // взятие на проходе чёрными
        Assert.Equal("4k3/8/8/8/8/3p4/8/4K3 w - - 0 2", epBlack.MakeMove(Move.FromUci("e4d3")).ToFen());

        // Превращение
        var promo = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
        // превращение в ферзя
        Assert.Equal("Q7/7k/8/8/8/8/8/4K3 b - - 0 1", promo.MakeMove(Move.FromUci("a7a8q")).ToFen());
        // превращение в коня
        Assert.Equal("N7/7k/8/8/8/8/8/4K3 b - - 0 1", promo.MakeMove(Move.FromUci("a7a8n")).ToFen());
        Assert.True(promo.IsPromotionMove(Sq.Parse("a7"), Sq.Parse("a8")), "ход считается превращением");
        Assert.False(promo.IsPromotionMove(Sq.Parse("e1"), Sq.Parse("e2")), "обычный ход не превращение");

        var promoCapture = Position.FromFen("1r5k/P7/8/8/8/8/8/4K3 w - - 0 1");
        // превращение со взятием
        Assert.Equal("1Q5k/8/8/8/8/8/8/4K3 b - - 0 1", promoCapture.MakeMove(Move.FromUci("a7b8q")).ToFen());

        // Счётчик 50 ходов
        var clock = Position.FromFen("4k3/8/8/8/8/8/4P3/R3K3 w - - 17 30");
        // тихий ход фигурой увеличивает счётчик
        Assert.Equal(18, clock.MakeMove(Move.FromUci("a1a2")).HalfmoveClock);
        // ход пешкой обнуляет счётчик
        Assert.Equal(0, clock.MakeMove(Move.FromUci("e2e4")).HalfmoveClock);
        // взятие обнуляет счётчик
        Assert.Equal(0, Position.FromFen("4k3/8/8/8/8/8/8/R2rK3 w - - 30 40").MakeMove(Move.FromUci("a1d1")).HalfmoveClock);

        Assert.True(rook.IsCastlingMove(Move.FromUci("e1g1")), "рокировка распознана");
        Assert.False(rook.IsCastlingMove(Move.FromUci("e1e2")), "обычный ход королём не рокировка");
    }

    [Fact(DisplayName = "Позиция: неизменяемость")]
    public void Immutability()
    {
        var start = Position.FromFen(Position.StartFen);
        var before = start.LegalMoves.Count;
        var next = start.MakeMove(Move.FromUci("e2e4"));

        Assert.Equal(Position.StartFen, start.ToFen());  // исходная позиция не изменилась
        Assert.Equal(before, start.LegalMoves.Count);  // исходный список ходов не изменился
        Assert.True(next.ToFen() != start.ToFen(), "новая позиция отличается");
        Assert.True(ReferenceEquals(start.LegalMoves, start.LegalMoves), "список ходов кэшируется");

        var clone = start.Clone();
        clone.MakeMove(Move.FromUci("d2d4"));
        Assert.Equal(Position.StartFen, clone.ToFen());  // копия независима
        Assert.Equal(Position.StartFen, start.ToFen());  // оригинал независим от копии

        // Построенная позиция не делится массивом клеток со строителем
        var builder = PositionBuilder.FromFen(Position.StartFen);
        var built = builder.ToPosition();
        builder.Clear();
        Assert.Equal(Position.StartFen, built.ToFen());  // строитель не влияет на выданную позицию
    }

    [Fact(DisplayName = "Позиция: легальность ходов")]
    public void LegalMoves()
    {
        var start = Position.FromFen(Position.StartFen);
        Assert.Equal(20, start.LegalMoves.Count);  // ходов в начальной позиции
        Assert.True(start.IsLegal(Move.FromUci("e2e4")), "e2-e4 легален");
        Assert.False(start.IsLegal(Move.FromUci("e2e5")), "e2-e5 нелегален");
        Assert.True(start.HasMoveFrom(Sq.Parse("e2")), "есть ходы пешкой e2");
        Assert.False(start.HasMoveFrom(Sq.Parse("c1")), "нет ходов слоном c1");
        Assert.True(start.TryFindMove(Sq.Parse("g1"), Sq.Parse("f3"), PieceType.None, out _), "ход находится по клеткам");
        Assert.False(start.TryFindMove(Sq.Parse("g1"), Sq.Parse("g3"), PieceType.None, out _), "несуществующий ход не находится");

        // Связанная фигура не может уйти с линии
        var pinned = Position.FromFen("k3r3/8/8/8/8/4N3/8/4K3 w - - 0 1");
        Assert.False(pinned.HasMoveFrom(Sq.Parse("e3")), "связанный конь не ходит");

        // Из-под шаха: только уход, взятие или перекрытие
        var inCheck = Position.FromFen("4k3/8/8/8/8/8/4r3/4K3 w - - 0 1");
        Assert.True(inCheck.LegalMoves.Any(m => m.From == Sq.Parse("e1") && Sq.File(m.To) != 4), "под шахом король уходит с линии");
        Assert.True(inCheck.LegalMoves.Any(m => m.To == Sq.Parse("e2")), "под шахом можно взять шахующую фигуру");

        // Двойной шах — ходит только король
        var doubleCheck = Position.FromFen("4k3/8/8/8/8/2n5/4r3/4K1R1 w - - 0 1");
        Assert.True(doubleCheck.LegalMoves.All(m => m.From == Sq.Parse("e1")), "при двойном шахе ходит только король");

        var mate = Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1").MakeMove(Move.FromUci("a1a8"));
        Assert.True(mate.IsCheckmate, "мат распознан");
        Assert.False(mate.IsStalemate, "мат — не пат");
        Assert.Empty(mate.LegalMoves);  // при мате ходов нет

        var stalemate = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        Assert.True(stalemate.IsStalemate, "пат распознан");
        Assert.False(stalemate.IsCheckmate, "пат — не мат");

        // Рокировка через битое поле запрещена
        var throughCheck = Position.FromFen("4k3/8/8/8/8/8/5q2/4K2R w K - 0 1");
        Assert.False(throughCheck.LegalMoves.Any(m => m.From == Sq.Parse("e1") && m.To == Sq.Parse("g1")), "рокировка через битое поле");
        var occupied = Position.FromFen("4k3/8/8/8/8/8/8/4KB1R w K - 0 1");
        Assert.False(Castles(occupied), "рокировка через занятое поле");
        var allowed = Position.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        Assert.True(Castles(allowed), "рокировка разрешена");
        var noRight = Position.FromFen("4k3/8/8/8/8/8/8/4K2R w - - 0 1");
        Assert.False(Castles(noRight), "без права рокировки хода нет");

        // Длинная рокировка возможна, даже когда поле b1 под боем
        var b1Attacked = Position.FromFen("1r2k3/8/8/8/8/8/8/R3K3 w Q - 0 1");
        Assert.True(b1Attacked.LegalMoves.Any(m => m.From == Sq.Parse("e1") && m.To == Sq.Parse("c1")), "длинная рокировка при битом b1");

        // Четыре превращения на каждое поле
        var promo = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
        // четыре варианта превращения
        Assert.Equal(4, promo.LegalMoves.Count(m => m.From == Sq.Parse("a7")));
    }

    [Fact(DisplayName = "Позиция: материал")]
    public void Material()
    {
        (string Fen, bool Insufficient, string Name)[] cases =
        {
            ("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true, "король против короля"),
            ("4k3/8/8/8/8/8/8/4KN2 w - - 0 1", true, "король с конём"),
            ("4k3/8/8/8/8/8/8/4KB2 w - - 0 1", true, "король со слоном"),
            ("4kb2/8/8/8/8/8/8/2B1K3 w - - 0 1", true, "слоны одного цвета"),
            ("4kb2/8/8/8/8/8/8/3BK3 w - - 0 1", false, "слоны разных цветов"),
            ("4k3/8/8/8/8/8/8/3NKN2 w - - 0 1", false, "два коня"),
            ("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", false, "есть пешка"),
            ("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", false, "есть ладья"),
            ("4k3/8/8/8/8/8/8/3QK3 w - - 0 1", false, "есть ферзь"),
            (Position.StartFen, false, "начальная позиция")
        };
        foreach (var c in cases)
            Assert.Equal(c.Insufficient, Position.FromFen(c.Fen).HasInsufficientMaterial());

        Assert.Equal(0, Position.FromFen(Position.StartFen).MaterialBalance());  // баланс начальной позиции
        // баланс без чёрного ферзя
        Assert.Equal(9, Position.FromFen("rnb1kbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1").MaterialBalance());
        // баланс без белой ладьи
        Assert.Equal(-5, Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/1NBQKBNR w Kkq - 0 1").MaterialBalance());
        // баланс лишней пешки чёрных
        Assert.Equal(-1, Position.FromFen("4k3/4p3/8/8/8/8/8/4K3 w - - 0 1").MaterialBalance());
    }
}
