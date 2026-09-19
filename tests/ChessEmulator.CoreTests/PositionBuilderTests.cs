using System.Text;
using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>Строитель позиции — основа редактора: расстановка, автокоррекция, проверка.</summary>
internal static class PositionBuilderTests
{
    public static void Run()
    {
        Building();
        Normalization();
        Validation();
    }

    private static string CastlingText(CastlingRights rights)
    {
        if (rights == CastlingRights.None) return "-";
        var sb = new StringBuilder();
        if (rights.HasFlag(CastlingRights.WhiteKing)) sb.Append('K');
        if (rights.HasFlag(CastlingRights.WhiteQueen)) sb.Append('Q');
        if (rights.HasFlag(CastlingRights.BlackKing)) sb.Append('k');
        if (rights.HasFlag(CastlingRights.BlackQueen)) sb.Append('q');
        return sb.ToString();
    }

    private static void Building()
    {
        Test.Suite("Строитель: расстановка", () =>
        {
            Test.Check("пустая доска", "8/8/8/8/8/8/8/8 w - - 0 1", new PositionBuilder().ToFen());
            Test.Check("начальная позиция", Position.StartFen, PositionBuilder.StartPosition().ToFen());

            var manual = new PositionBuilder();
            manual[Sq.Parse("e1")] = new Piece(PieceColor.White, PieceType.King);
            manual[Sq.Parse("e8")] = new Piece(PieceColor.Black, PieceType.King);
            manual[Sq.Parse("d4")] = new Piece(PieceColor.White, PieceType.Queen);
            manual.SideToMove = PieceColor.Black;
            manual.FullmoveNumber = 7;
            Test.Check("ручная расстановка", "4k3/8/8/8/3Q4/8/8/4K3 b - - 0 7", manual.ToFen());
            Test.Check("чтение клетки", "Q", manual[Sq.Parse("d4")].ToFenChar().ToString());
            Test.True("пустая клетка читается", manual[Sq.Parse("a1")].IsEmpty);
            Test.True("собранная позиция играбельна", manual.ToPosition().LegalMoves.Count > 0);

            Test.Check("подсчёт фигур: ферзь", 1, manual.CountPieces(PieceColor.White, PieceType.Queen));
            Test.Check("подсчёт фигур: пешки", 0, manual.CountPieces(PieceColor.White, PieceType.Pawn));
            Test.Check("подсчёт пешек в начальной позиции", 8,
                PositionBuilder.StartPosition().CountPieces(PieceColor.Black, PieceType.Pawn));
            Test.Check("поиск короля", Sq.Parse("e1"), manual.FindKing(PieceColor.White));
            Test.Check("короля нет", Sq.None, new PositionBuilder().FindKing(PieceColor.Black));

            foreach (var fen in new[] { Position.StartFen, PerftTests.Kiwipete, PerftTests.Pos5 })
                Test.Check("полный круг: " + fen.Split(' ')[0][..12], fen, PositionBuilder.FromFen(fen).ToFen());

            var fromPosition = PositionBuilder.FromPosition(Position.FromFen(PerftTests.Pos3));
            Test.Check("сборка из позиции", PerftTests.Pos3, fromPosition.ToFen());

            // Стирание и перенос фигур
            var dragged = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
            dragged.MovePiece(Sq.Parse("h1"), Sq.Parse("h5"));
            Test.Check("фигура перенесена", "4k3/8/8/7R/8/8/8/4K3 w K - 0 1", dragged.ToFen());
            dragged.MovePiece(Sq.Parse("h5"), Sq.Parse("h5"));
            Test.Check("перенос на ту же клетку ничего не делает", "4k3/8/8/7R/8/8/8/4K3 w K - 0 1", dragged.ToFen());
            dragged[Sq.Parse("h5")] = Piece.Empty;
            Test.Check("фигура стёрта", "4k3/8/8/8/8/8/8/4K3 w K - 0 1", dragged.ToFen());

            var cleared = PositionBuilder.FromFen(PerftTests.Kiwipete);
            cleared.Clear();
            Test.Check("очистка доски", "8/8/8/8/8/8/8/8 w - - 0 1", cleared.ToFen());
            Test.Check("очистка сбрасывает права", CastlingRights.None, cleared.Castling);
            Test.Check("очистка сбрасывает очередь хода", PieceColor.White, cleared.SideToMove);

            cleared.SetStartPosition();
            Test.Check("возврат к начальной позиции", Position.StartFen, cleared.ToFen());

            // Счётчики зажимаются при сборке позиции
            var counters = PositionBuilder.StartPosition();
            counters.HalfmoveClock = 500;
            counters.FullmoveNumber = 0;
            Test.Check("счётчик полуходов ограничен сотней", 100, counters.ToPosition().HalfmoveClock);
            Test.Check("номер хода не меньше единицы", 1, counters.ToPosition().FullmoveNumber);
        });
    }

    private static void Normalization()
    {
        Test.Suite("Строитель: автокоррекция", () =>
        {
            var noBlackRooks = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            noBlackRooks.Normalize();
            Test.Check("без ладей чёрных их права сняты", "KQ", CastlingText(noBlackRooks.Castling));

            var noKingSquare = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/R4K1R w KQ - 0 1");
            noKingSquare.Normalize();
            Test.Check("король не на e1 — прав нет", "-", CastlingText(noKingSquare.Castling));

            var oneRook = PositionBuilder.FromFen("r3k3/8/8/8/8/8/8/4K2R w KQkq - 0 1");
            oneRook.Normalize();
            Test.Check("остались права по стоящим ладьям", "Kq", CastlingText(oneRook.Castling));

            var full = PositionBuilder.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            full.Normalize();
            Test.Check("полные права сохраняются", "KQkq", CastlingText(full.Castling));

            // Перенос ладьи снимает право только после нормализации
            var moved = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
            moved.MovePiece(Sq.Parse("h1"), Sq.Parse("h5"));
            Test.Check("до нормализации право ещё на месте", "K", CastlingText(moved.Castling));
            moved.Normalize();
            Test.Check("после нормализации право снято", "-", CastlingText(moved.Castling));

            // Поле взятия на проходе
            var epGood = PositionBuilder.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
            epGood.Normalize();
            Test.Check("корректное поле сохранено", "d6", Sq.Name(epGood.EnPassant));

            var epNoPawn = PositionBuilder.FromFen("4k3/8/8/4P3/8/8/8/4K3 w - d6 0 1");
            epNoPawn.Normalize();
            Test.Check("нет пешки — поле сброшено", "-", Sq.Name(epNoPawn.EnPassant));

            var epWrongSide = PositionBuilder.FromFen("4k3/8/8/3pP3/8/8/8/4K3 b - d6 0 1");
            epWrongSide.Normalize();
            Test.Check("поле не по очереди хода сброшено", "-", Sq.Name(epWrongSide.EnPassant));

            var epOccupied = PositionBuilder.FromFen("4k3/8/3B4/3pP3/8/8/8/4K3 w - d6 0 1");
            epOccupied.Normalize();
            Test.Check("занятое поле сброшено", "-", Sq.Name(epOccupied.EnPassant));

            var epBusyStart = PositionBuilder.FromFen("4k3/3r4/8/3pP3/8/8/8/4K3 w - d6 0 1");
            epBusyStart.Normalize();
            Test.Check("исходная клетка пешки занята — поле сброшено", "-", Sq.Name(epBusyStart.EnPassant));

            var epBlack = PositionBuilder.FromFen("4k3/8/8/8/3Pp3/8/8/4K3 b - d3 0 1");
            epBlack.Normalize();
            Test.Check("поле для чёрных сохранено", "d3", Sq.Name(epBlack.EnPassant));

            // Список допустимых полей
            var available = PositionBuilder.FromFen("4k3/8/8/2ppP3/8/8/8/4K3 w - - 0 1");
            Test.Check("два поля на выбор", 2, available.AvailableEnPassantSquares().Count);
            Test.Check("поля названы верно", "c6 d6",
                string.Join(" ", available.AvailableEnPassantSquares().Select(Sq.Name)));
            Test.Check("без пешек полей нет", 0,
                PositionBuilder.StartPosition().AvailableEnPassantSquares().Count);
            Test.Check("для чёрных поля на 3-й горизонтали", "d3",
                string.Join(" ", PositionBuilder.FromFen("4k3/8/8/8/3Pp3/8/8/4K3 b - - 0 1")
                    .AvailableEnPassantSquares().Select(Sq.Name)));

            var counters = PositionBuilder.StartPosition();
            counters.HalfmoveClock = -5;
            counters.FullmoveNumber = -1;
            counters.Normalize();
            Test.Check("отрицательный счётчик обнулён", 0, counters.HalfmoveClock);
            Test.Check("отрицательный номер хода поднят", 1, counters.FullmoveNumber);
        });
    }

    private static void Validation()
    {
        Test.Suite("Строитель: проверка позиции", () =>
        {
            Test.Check("начальная позиция корректна", 0,
                PositionBuilder.FromFen(Position.StartFen).Validate().Count);

            var noKings = new PositionBuilder();
            Test.Check("нет обоих королей — две ошибки", 2, noKings.Validate().Count);

            var noBlackKing = new PositionBuilder();
            noBlackKing[Sq.Parse("e1")] = new Piece(PieceColor.White, PieceType.King);
            Test.Check("нет чёрного короля", 1, noBlackKing.Validate().Count);
            Test.Check("текст ошибки", "На доске нет чёрного короля.", noBlackKing.Validate()[0]);

            var twoWhiteKings = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/K3K3 w - - 0 1");
            Test.Check("два белых короля", 1, twoWhiteKings.Validate().Count);
            Test.Check("текст про лишнего короля", "У белых больше одного короля.", twoWhiteKings.Validate()[0]);

            Test.Check("пешка на 8-й горизонтали", 1,
                PositionBuilder.FromFen("4k2P/8/8/8/8/8/8/4K3 w - - 0 1").Validate().Count);
            Test.Check("пешка на 1-й горизонтали", 1,
                PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K2p w - - 0 1").Validate().Count);
            Test.Check("две пешки на краю — одна ошибка", 1,
                PositionBuilder.FromFen("4k2P/8/8/8/8/8/8/4K2p w - - 0 1").Validate().Count);
            Test.Check("пешка на 2-й горизонтали допустима", 0,
                PositionBuilder.FromFen("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1").Validate().Count);

            Test.Check("сторона не на ходу под шахом", 1,
                PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1").Validate().Count);
            Test.Check("текст про шах", "Чёрные под шахом, но сейчас ход белых.",
                PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1").Validate()[0]);
            Test.Check("текст про шах белым", "Белые под шахом, но сейчас ход чёрных.",
                PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K1r1 b - - 0 1").Validate()[0]);
            Test.Check("шах стороне на ходу допустим", 0,
                PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4R1K1 b - - 0 1").Validate().Count);

            // Мат и пат — не ошибки, такую позицию можно поставить
            Test.Check("мат можно поставить", 0,
                PositionBuilder.FromFen("6kR/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1").Validate().Count);
            Test.Check("пат можно поставить", 0,
                PositionBuilder.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1").Validate().Count);

            // Полный сценарий редактора: собрать позицию и начать с неё партию
            var builder = new PositionBuilder();
            builder[Sq.Parse("e1")] = new Piece(PieceColor.White, PieceType.King);
            builder[Sq.Parse("a1")] = new Piece(PieceColor.White, PieceType.Rook);
            builder[Sq.Parse("h8")] = new Piece(PieceColor.Black, PieceType.King);
            builder.Castling = CastlingRights.All;
            builder.Normalize();
            Test.Check("осталось только реальное право на длинную рокировку", "Q", CastlingText(builder.Castling));
            Test.Check("позиция корректна", 0, builder.Validate().Count);

            var game = new Game(builder.ToFen());
            Test.Check("партия начинается с расставленной позиции", builder.ToFen(), game.StartFen);
            Test.True("ход из расставленной позиции делается", game.TryAddSan("Ra8+", out var node));
            Test.Check("ход записан в нотации", "Ra8+", node!.San);
            Test.Check("заголовок SetUp", "1", game.Headers["SetUp"]);
        });
    }
}
