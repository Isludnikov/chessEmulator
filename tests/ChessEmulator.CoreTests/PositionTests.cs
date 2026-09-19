using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>Позиция: FEN, атаки, применение хода, ничейный материал.</summary>
internal static class PositionTests
{
    public static void Run()
    {
        Fen();
        Attacks();
        MakeMove();
        Immutability();
        LegalMoves();
        Material();
    }

    // ------------------------------------------------------------------ FEN

    private static void Fen()
    {
        Test.Suite("Позиция: FEN", () =>
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
            Test.Check("полный круг FEN на девяти позициях", 0, mismatches);

            var pos = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 13 42");
            Test.Check("очередь хода", PieceColor.White, pos.SideToMove);
            Test.Check("противник", PieceColor.Black, pos.Opponent);
            Test.Check("поле взятия на проходе", "d6", Sq.Name(pos.EnPassant));
            Test.Check("счётчик полуходов", 13, pos.HalfmoveClock);
            Test.Check("номер хода", 42, pos.FullmoveNumber);
            Test.Check("фигура по индексу", "P", pos[Sq.Parse("e5")].ToFenChar().ToString());
            Test.Check("фигура по координатам", "p", pos.At(3, 4).ToFenChar().ToString());
            Test.True("пустая клетка", pos[Sq.Parse("a1")].IsEmpty);

            var rights = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w Qk - 0 1");
            Test.Check("разбор прав рокировки", CastlingRights.WhiteQueen | CastlingRights.BlackKing, rights.Castling);
            Test.Check("запись прав рокировки", "Qk", CastlingField(rights.ToFen()));
            Test.Check("нет прав рокировки",
                CastlingRights.None, Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").Castling);

            // Необязательные поля FEN
            var shortFen = Position.FromFen("4k3/8/8/8/8/8/8/4K3 b");
            Test.Check("FEN без прав рокировки: очередь хода", PieceColor.Black, shortFen.SideToMove);
            Test.Check("FEN без счётчиков: полуходы", 0, shortFen.HalfmoveClock);
            Test.Check("FEN без счётчиков: номер хода", 1, shortFen.FullmoveNumber);
            Test.Check("нулевой номер хода поднимается до 1", 1,
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 0").FullmoveNumber);
            Test.Check("нечисловые счётчики игнорируются", 0,
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - x y").HalfmoveClock);

            Test.Throws<FormatException>("строка без пробелов отвергается", () => Position.FromFen("не-фен"));
            Test.Throws<FormatException>("пустая строка отвергается", () => Position.FromFen(""));

            Test.Check("ToString даёт FEN", Position.StartFen, Position.FromFen(Position.StartFen).ToString());

            // Ключ повторения — FEN без двух счётчиков
            Test.Check("ключ повторения отбрасывает счётчики",
                "4k3/8/8/8/8/8/8/4K3 w - -",
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 13 42").RepetitionKey());
            Test.Check("ключ повторения не зависит от счётчиков",
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").RepetitionKey(),
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 55 99").RepetitionKey());
            Test.True("ключ повторения различает очередь хода",
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").RepetitionKey() !=
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 b - - 0 1").RepetitionKey());

            var clone = Position.FromFen(PerftTests.Kiwipete).Clone();
            Test.Check("копия совпадает по FEN", PerftTests.Kiwipete, clone.ToFen());
        });
    }

    /// <summary>Есть ли у белых короткая рокировка (ход королём e1-g1, а не ход ладьёй на g1).</summary>
    private static bool Castles(Position position) =>
        position.LegalMoves.Any(m => m.From == Sq.Parse("e1") && m.To == Sq.Parse("g1"));

    /// <summary>Третье поле FEN — права на рокировку.</summary>
    private static string CastlingField(string fen) => fen.Split(new[] { ' ' })[2];

    // ---------------------------------------------------------------- Атаки

    private static void Attacks()
    {
        Test.Suite("Позиция: атаки и шахи", () =>
        {
            var pos = Position.FromFen("4k3/8/8/3N4/8/8/8/4K3 w - - 0 1");
            Test.True("конь бьёт e7", pos.IsAttacked(Sq.Parse("e7"), PieceColor.White));
            Test.True("конь бьёт c7", pos.IsAttacked(Sq.Parse("c7"), PieceColor.White));
            Test.False("конь не бьёт d6", pos.IsAttacked(Sq.Parse("d6"), PieceColor.White));

            var pawns = Position.FromFen("7k/8/8/8/8/8/4P3/K7 w - - 0 1");
            Test.True("белая пешка бьёт d3", pawns.IsAttacked(Sq.Parse("d3"), PieceColor.White));
            Test.True("белая пешка бьёт f3", pawns.IsAttacked(Sq.Parse("f3"), PieceColor.White));
            Test.False("белая пешка не бьёт перед собой", pawns.IsAttacked(Sq.Parse("e3"), PieceColor.White));
            Test.False("белая пешка не бьёт назад", pawns.IsAttacked(Sq.Parse("d1"), PieceColor.White));

            var blackPawn = Position.FromFen("k7/4p3/8/8/8/8/8/7K b - - 0 1");
            Test.True("чёрная пешка бьёт вниз", blackPawn.IsAttacked(Sq.Parse("d6"), PieceColor.Black));
            Test.False("чёрная пешка не бьёт вверх", blackPawn.IsAttacked(Sq.Parse("d8"), PieceColor.Black));

            var sliders = Position.FromFen("4k3/8/8/8/8/2B5/8/R3K3 w - - 0 1");
            Test.True("ладья бьёт вдоль вертикали", sliders.IsAttacked(Sq.Parse("a8"), PieceColor.White));
            Test.True("слон бьёт по диагонали", sliders.IsAttacked(Sq.Parse("f6"), PieceColor.White));

            var blocked = Position.FromFen("4k3/8/8/8/8/8/3p4/R2QK3 w - - 0 1");
            Test.False("ферзь не бьёт сквозь пешку", blocked.IsAttacked(Sq.Parse("d8"), PieceColor.White));
            Test.True("ферзь бьёт саму пешку", blocked.IsAttacked(Sq.Parse("d2"), PieceColor.White));

            var king = Position.FromFen("8/8/8/8/8/8/8/4K2k w - - 0 1");
            Test.True("король бьёт соседнюю клетку", king.IsAttacked(Sq.Parse("d2"), PieceColor.White));
            Test.False("король не бьёт через клетку", king.IsAttacked(Sq.Parse("e4"), PieceColor.White));

            var check = Position.FromFen("4k3/8/8/8/8/8/8/4R1K1 b - - 0 1");
            Test.True("чёрный король под шахом", check.IsInCheck());
            Test.True("шах по цвету", check.IsInCheck(PieceColor.Black));
            Test.False("белый король не под шахом", check.IsInCheck(PieceColor.White));

            Test.Check("король найден", Sq.Parse("e1"),
                Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1").FindKing(PieceColor.White));
            Test.Check("нет короля — Sq.None", Sq.None,
                Position.FromFen("4k3/8/8/8/8/8/8/8 w - - 0 1").FindKing(PieceColor.White));
            Test.False("без короля шаха нет",
                Position.FromFen("4k3/8/8/8/8/8/8/8 w - - 0 1").IsInCheck(PieceColor.White));
        });
    }

    // -------------------------------------------------- Ход и его следствия

    private static void MakeMove()
    {
        Test.Suite("Позиция: применение хода", () =>
        {
            var start = Position.FromFen(Position.StartFen);
            var afterE4 = start.MakeMove(Move.FromUci("e2e4"));
            Test.Check("после 1.e4", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", afterE4.ToFen());
            Test.Check("двойной ход задаёт поле взятия на проходе", "e3", Sq.Name(afterE4.EnPassant));
            Test.Check("номер хода растёт после хода чёрных", 2,
                afterE4.MakeMove(Move.FromUci("e7e5")).FullmoveNumber);

            // Рокировки: все четыре
            var rook = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Test.Check("короткая рокировка белых", "r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1",
                rook.MakeMove(Move.FromUci("e1g1")).ToFen());
            Test.Check("длинная рокировка белых", "r3k2r/8/8/8/8/8/8/2KR3R b kq - 1 1",
                rook.MakeMove(Move.FromUci("e1c1")).ToFen());

            var rookBlack = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1");
            Test.Check("короткая рокировка чёрных", "r4rk1/8/8/8/8/8/8/R3K2R w KQ - 1 2",
                rookBlack.MakeMove(Move.FromUci("e8g8")).ToFen());
            Test.Check("длинная рокировка чёрных", "2kr3r/8/8/8/8/8/8/R3K2R w KQ - 1 2",
                rookBlack.MakeMove(Move.FromUci("e8c8")).ToFen());

            // Права на рокировку
            Test.Check("ход королём снимает оба права белых", "kq",
                CastlingField(rook.MakeMove(Move.FromUci("e1e2")).ToFen()));
            Test.Check("ход ладьёй h1 снимает короткую", "Qkq",
                CastlingField(rook.MakeMove(Move.FromUci("h1h2")).ToFen()));
            Test.Check("ход ладьёй a1 снимает длинную", "Kkq",
                CastlingField(rook.MakeMove(Move.FromUci("a1a2")).ToFen()));
            Test.Check("взятие ладьи на h8 снимает права обеих сторон", "Qq",
                CastlingField(rook.MakeMove(Move.FromUci("h1h8")).ToFen()));

            // Взятие на проходе
            var ep = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
            var afterEp = ep.MakeMove(Move.FromUci("e5d6"));
            Test.Check("взятие на проходе", "4k3/8/3P4/8/8/8/8/4K3 b - - 0 2", afterEp.ToFen());
            Test.True("взятие на проходе распознано", ep.IsEnPassantMove(Move.FromUci("e5d6")));
            Test.True("взятие на проходе — взятие", ep.IsCapture(Move.FromUci("e5d6")));
            Test.False("тихий ход не взятие", ep.IsCapture(Move.FromUci("e5e6")));

            var epBlack = Position.FromFen("4k3/8/8/8/3Pp3/8/8/4K3 b - d3 0 1");
            Test.Check("взятие на проходе чёрными", "4k3/8/8/8/8/3p4/8/4K3 w - - 0 2",
                epBlack.MakeMove(Move.FromUci("e4d3")).ToFen());

            // Превращение
            var promo = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
            Test.Check("превращение в ферзя", "Q7/7k/8/8/8/8/8/4K3 b - - 0 1",
                promo.MakeMove(Move.FromUci("a7a8q")).ToFen());
            Test.Check("превращение в коня", "N7/7k/8/8/8/8/8/4K3 b - - 0 1",
                promo.MakeMove(Move.FromUci("a7a8n")).ToFen());
            Test.True("ход считается превращением", promo.IsPromotionMove(Sq.Parse("a7"), Sq.Parse("a8")));
            Test.False("обычный ход не превращение", promo.IsPromotionMove(Sq.Parse("e1"), Sq.Parse("e2")));

            var promoCapture = Position.FromFen("1r5k/P7/8/8/8/8/8/4K3 w - - 0 1");
            Test.Check("превращение со взятием", "1Q5k/8/8/8/8/8/8/4K3 b - - 0 1",
                promoCapture.MakeMove(Move.FromUci("a7b8q")).ToFen());

            // Счётчик 50 ходов
            var clock = Position.FromFen("4k3/8/8/8/8/8/4P3/R3K3 w - - 17 30");
            Test.Check("тихий ход фигурой увеличивает счётчик", 18,
                clock.MakeMove(Move.FromUci("a1a2")).HalfmoveClock);
            Test.Check("ход пешкой обнуляет счётчик", 0,
                clock.MakeMove(Move.FromUci("e2e4")).HalfmoveClock);
            Test.Check("взятие обнуляет счётчик", 0,
                Position.FromFen("4k3/8/8/8/8/8/8/R2rK3 w - - 30 40").MakeMove(Move.FromUci("a1d1")).HalfmoveClock);

            Test.True("рокировка распознана", rook.IsCastlingMove(Move.FromUci("e1g1")));
            Test.False("обычный ход королём не рокировка", rook.IsCastlingMove(Move.FromUci("e1e2")));
        });
    }

    private static void Immutability()
    {
        Test.Suite("Позиция: неизменяемость", () =>
        {
            var start = Position.FromFen(Position.StartFen);
            var before = start.LegalMoves.Count;
            var next = start.MakeMove(Move.FromUci("e2e4"));

            Test.Check("исходная позиция не изменилась", Position.StartFen, start.ToFen());
            Test.Check("исходный список ходов не изменился", before, start.LegalMoves.Count);
            Test.True("новая позиция отличается", next.ToFen() != start.ToFen());
            Test.True("список ходов кэшируется", ReferenceEquals(start.LegalMoves, start.LegalMoves));

            var clone = start.Clone();
            clone.MakeMove(Move.FromUci("d2d4"));
            Test.Check("копия независима", Position.StartFen, clone.ToFen());
            Test.Check("оригинал независим от копии", Position.StartFen, start.ToFen());

            // Построенная позиция не делится массивом клеток со строителем
            var builder = PositionBuilder.FromFen(Position.StartFen);
            var built = builder.ToPosition();
            builder.Clear();
            Test.Check("строитель не влияет на выданную позицию", Position.StartFen, built.ToFen());
        });
    }

    private static void LegalMoves()
    {
        Test.Suite("Позиция: легальность ходов", () =>
        {
            var start = Position.FromFen(Position.StartFen);
            Test.Check("ходов в начальной позиции", 20, start.LegalMoves.Count);
            Test.True("e2-e4 легален", start.IsLegal(Move.FromUci("e2e4")));
            Test.False("e2-e5 нелегален", start.IsLegal(Move.FromUci("e2e5")));
            Test.True("есть ходы пешкой e2", start.HasMoveFrom(Sq.Parse("e2")));
            Test.False("нет ходов слоном c1", start.HasMoveFrom(Sq.Parse("c1")));
            Test.True("ход находится по клеткам",
                start.TryFindMove(Sq.Parse("g1"), Sq.Parse("f3"), PieceType.None, out _));
            Test.False("несуществующий ход не находится",
                start.TryFindMove(Sq.Parse("g1"), Sq.Parse("g3"), PieceType.None, out _));

            // Связанная фигура не может уйти с линии
            var pinned = Position.FromFen("k3r3/8/8/8/8/4N3/8/4K3 w - - 0 1");
            Test.False("связанный конь не ходит", pinned.HasMoveFrom(Sq.Parse("e3")));

            // Из-под шаха: только уход, взятие или перекрытие
            var inCheck = Position.FromFen("4k3/8/8/8/8/8/4r3/4K3 w - - 0 1");
            Test.True("под шахом король уходит с линии",
                inCheck.LegalMoves.Any(m => m.From == Sq.Parse("e1") && Sq.File(m.To) != 4));
            Test.True("под шахом можно взять шахующую фигуру",
                inCheck.LegalMoves.Any(m => m.To == Sq.Parse("e2")));

            // Двойной шах — ходит только король
            var doubleCheck = Position.FromFen("4k3/8/8/8/8/2n5/4r3/4K1R1 w - - 0 1");
            Test.True("при двойном шахе ходит только король",
                doubleCheck.LegalMoves.All(m => m.From == Sq.Parse("e1")));

            var mate = Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1").MakeMove(Move.FromUci("a1a8"));
            Test.True("мат распознан", mate.IsCheckmate);
            Test.False("мат — не пат", mate.IsStalemate);
            Test.Check("при мате ходов нет", 0, mate.LegalMoves.Count);

            var stalemate = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
            Test.True("пат распознан", stalemate.IsStalemate);
            Test.False("пат — не мат", stalemate.IsCheckmate);

            // Рокировка через битое поле запрещена
            var throughCheck = Position.FromFen("4k3/8/8/8/8/8/5q2/4K2R w K - 0 1");
            Test.False("рокировка через битое поле",
                throughCheck.LegalMoves.Any(m => m.From == Sq.Parse("e1") && m.To == Sq.Parse("g1")));
            var occupied = Position.FromFen("4k3/8/8/8/8/8/8/4KB1R w K - 0 1");
            Test.False("рокировка через занятое поле", Castles(occupied));
            var allowed = Position.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
            Test.True("рокировка разрешена", Castles(allowed));
            var noRight = Position.FromFen("4k3/8/8/8/8/8/8/4K2R w - - 0 1");
            Test.False("без права рокировки хода нет", Castles(noRight));

            // Длинная рокировка возможна, даже когда поле b1 под боем
            var b1Attacked = Position.FromFen("1r2k3/8/8/8/8/8/8/R3K3 w Q - 0 1");
            Test.True("длинная рокировка при битом b1",
                b1Attacked.LegalMoves.Any(m => m.From == Sq.Parse("e1") && m.To == Sq.Parse("c1")));

            // Четыре превращения на каждое поле
            var promo = Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
            Test.Check("четыре варианта превращения", 4,
                promo.LegalMoves.Count(m => m.From == Sq.Parse("a7")));
        });
    }

    private static void Material()
    {
        Test.Suite("Позиция: материал", () =>
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
                Test.Check("недостаток материала: " + c.Name, c.Insufficient,
                    Position.FromFen(c.Fen).HasInsufficientMaterial());

            Test.Check("баланс начальной позиции", 0, Position.FromFen(Position.StartFen).MaterialBalance());
            Test.Check("баланс без чёрного ферзя", 9,
                Position.FromFen("rnb1kbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1").MaterialBalance());
            Test.Check("баланс без белой ладьи", -5,
                Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/1NBQKBNR w Kkq - 0 1").MaterialBalance());
            Test.Check("баланс лишней пешки чёрных", -1,
                Position.FromFen("4k3/4p3/8/8/8/8/8/4K3 w - - 0 1").MaterialBalance());
        });
    }
}
