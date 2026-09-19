using System.Text;
using ChessEmulator.Chess;
using Xunit;

namespace ChessEmulator.CoreTests;

/// <summary>Строитель позиции — основа редактора: расстановка, автокоррекция, проверка.</summary>
public class PositionBuilderTests
{
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

    [Fact(DisplayName = "Строитель: расстановка")]
    public void Building()
    {
        Assert.Equal("8/8/8/8/8/8/8/8 w - - 0 1", new PositionBuilder().ToFen());  // пустая доска
        Assert.Equal(Position.StartFen, PositionBuilder.StartPosition().ToFen());  // начальная позиция

        var manual = new PositionBuilder();
        manual[Sq.Parse("e1")] = new Piece(PieceColor.White, PieceType.King);
        manual[Sq.Parse("e8")] = new Piece(PieceColor.Black, PieceType.King);
        manual[Sq.Parse("d4")] = new Piece(PieceColor.White, PieceType.Queen);
        manual.SideToMove = PieceColor.Black;
        manual.FullmoveNumber = 7;
        Assert.Equal("4k3/8/8/8/3Q4/8/8/4K3 b - - 0 7", manual.ToFen());  // ручная расстановка
        Assert.Equal("Q", manual[Sq.Parse("d4")].ToFenChar().ToString());  // чтение клетки
        Assert.True(manual[Sq.Parse("a1")].IsEmpty, "пустая клетка читается");
        Assert.True(manual.ToPosition().LegalMoves.Count > 0, "собранная позиция играбельна");

        Assert.Equal(1, manual.CountPieces(PieceColor.White, PieceType.Queen));  // подсчёт фигур: ферзь
        Assert.Equal(0, manual.CountPieces(PieceColor.White, PieceType.Pawn));  // подсчёт фигур: пешки
        // подсчёт пешек в начальной позиции
        Assert.Equal(8, PositionBuilder.StartPosition().CountPieces(PieceColor.Black, PieceType.Pawn));
        Assert.Equal(Sq.Parse("e1"), manual.FindKing(PieceColor.White));  // поиск короля
        Assert.Equal(Sq.None, new PositionBuilder().FindKing(PieceColor.Black));  // короля нет

        foreach (var fen in new[] { Position.StartFen, PerftTests.Kiwipete, PerftTests.Pos5 })
            Assert.Equal(fen, PositionBuilder.FromFen(fen).ToFen());

        var fromPosition = PositionBuilder.FromPosition(Position.FromFen(PerftTests.Pos3));
        Assert.Equal(PerftTests.Pos3, fromPosition.ToFen());  // сборка из позиции

        // Стирание и перенос фигур
        var dragged = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        dragged.MovePiece(Sq.Parse("h1"), Sq.Parse("h5"));
        Assert.Equal("4k3/8/8/7R/8/8/8/4K3 w K - 0 1", dragged.ToFen());  // фигура перенесена
        dragged.MovePiece(Sq.Parse("h5"), Sq.Parse("h5"));
        Assert.Equal("4k3/8/8/7R/8/8/8/4K3 w K - 0 1", dragged.ToFen());  // перенос на ту же клетку ничего не делает
        dragged[Sq.Parse("h5")] = Piece.Empty;
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w K - 0 1", dragged.ToFen());  // фигура стёрта

        var cleared = PositionBuilder.FromFen(PerftTests.Kiwipete);
        cleared.Clear();
        Assert.Equal("8/8/8/8/8/8/8/8 w - - 0 1", cleared.ToFen());  // очистка доски
        Assert.Equal(CastlingRights.None, cleared.Castling);  // очистка сбрасывает права
        Assert.Equal(PieceColor.White, cleared.SideToMove);  // очистка сбрасывает очередь хода

        cleared.SetStartPosition();
        Assert.Equal(Position.StartFen, cleared.ToFen());  // возврат к начальной позиции

        // Счётчики зажимаются при сборке позиции
        var counters = PositionBuilder.StartPosition();
        counters.HalfmoveClock = 500;
        counters.FullmoveNumber = 0;
        Assert.Equal(100, counters.ToPosition().HalfmoveClock);  // счётчик полуходов ограничен сотней
        Assert.Equal(1, counters.ToPosition().FullmoveNumber);  // номер хода не меньше единицы
    }

    [Fact(DisplayName = "Строитель: автокоррекция")]
    public void Normalization()
    {
        var noBlackRooks = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        noBlackRooks.Normalize();
        Assert.Equal("KQ", CastlingText(noBlackRooks.Castling));  // без ладей чёрных их права сняты

        var noKingSquare = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/R4K1R w KQ - 0 1");
        noKingSquare.Normalize();
        Assert.Equal("-", CastlingText(noKingSquare.Castling));  // король не на e1 — прав нет

        var oneRook = PositionBuilder.FromFen("r3k3/8/8/8/8/8/8/4K2R w KQkq - 0 1");
        oneRook.Normalize();
        Assert.Equal("Kq", CastlingText(oneRook.Castling));  // остались права по стоящим ладьям

        var full = PositionBuilder.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        full.Normalize();
        Assert.Equal("KQkq", CastlingText(full.Castling));  // полные права сохраняются

        // Перенос ладьи снимает право только после нормализации
        var moved = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K2R w K - 0 1");
        moved.MovePiece(Sq.Parse("h1"), Sq.Parse("h5"));
        Assert.Equal("K", CastlingText(moved.Castling));  // до нормализации право ещё на месте
        moved.Normalize();
        Assert.Equal("-", CastlingText(moved.Castling));  // после нормализации право снято

        // Поле взятия на проходе
        var epGood = PositionBuilder.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        epGood.Normalize();
        Assert.Equal("d6", Sq.Name(epGood.EnPassant));  // корректное поле сохранено

        var epNoPawn = PositionBuilder.FromFen("4k3/8/8/4P3/8/8/8/4K3 w - d6 0 1");
        epNoPawn.Normalize();
        Assert.Equal("-", Sq.Name(epNoPawn.EnPassant));  // нет пешки — поле сброшено

        var epWrongSide = PositionBuilder.FromFen("4k3/8/8/3pP3/8/8/8/4K3 b - d6 0 1");
        epWrongSide.Normalize();
        Assert.Equal("-", Sq.Name(epWrongSide.EnPassant));  // поле не по очереди хода сброшено

        var epOccupied = PositionBuilder.FromFen("4k3/8/3B4/3pP3/8/8/8/4K3 w - d6 0 1");
        epOccupied.Normalize();
        Assert.Equal("-", Sq.Name(epOccupied.EnPassant));  // занятое поле сброшено

        var epBusyStart = PositionBuilder.FromFen("4k3/3r4/8/3pP3/8/8/8/4K3 w - d6 0 1");
        epBusyStart.Normalize();
        Assert.Equal("-", Sq.Name(epBusyStart.EnPassant));  // исходная клетка пешки занята — поле сброшено

        var epBlack = PositionBuilder.FromFen("4k3/8/8/8/3Pp3/8/8/4K3 b - d3 0 1");
        epBlack.Normalize();
        Assert.Equal("d3", Sq.Name(epBlack.EnPassant));  // поле для чёрных сохранено

        // Список допустимых полей
        var available = PositionBuilder.FromFen("4k3/8/8/2ppP3/8/8/8/4K3 w - - 0 1");
        Assert.Equal(2, available.AvailableEnPassantSquares().Count);  // два поля на выбор
        // поля названы верно
        Assert.Equal("c6 d6", string.Join(" ", available.AvailableEnPassantSquares().Select(Sq.Name)));
        // без пешек полей нет
        Assert.Empty(PositionBuilder.StartPosition().AvailableEnPassantSquares());
        // для чёрных поля на 3-й горизонтали
        Assert.Equal("d3", string.Join(" ", PositionBuilder.FromFen("4k3/8/8/8/3Pp3/8/8/4K3 b - - 0 1")
                .AvailableEnPassantSquares().Select(Sq.Name)));

        var counters = PositionBuilder.StartPosition();
        counters.HalfmoveClock = -5;
        counters.FullmoveNumber = -1;
        counters.Normalize();
        Assert.Equal(0, counters.HalfmoveClock);  // отрицательный счётчик обнулён
        Assert.Equal(1, counters.FullmoveNumber);  // отрицательный номер хода поднят
    }

    [Fact(DisplayName = "Строитель: проверка позиции")]
    public void Validation()
    {
        // начальная позиция корректна
        Assert.Empty(PositionBuilder.FromFen(Position.StartFen).Validate());

        var noKings = new PositionBuilder();
        Assert.Equal(2, noKings.Validate().Count);  // нет обоих королей — две ошибки

        var noBlackKing = new PositionBuilder();
        noBlackKing[Sq.Parse("e1")] = new Piece(PieceColor.White, PieceType.King);
        Assert.Single(noBlackKing.Validate());  // нет чёрного короля
        Assert.Equal("На доске нет чёрного короля.", noBlackKing.Validate()[0]);  // текст ошибки

        var twoWhiteKings = PositionBuilder.FromFen("4k3/8/8/8/8/8/8/K3K3 w - - 0 1");
        Assert.Single(twoWhiteKings.Validate());  // два белых короля
        Assert.Equal("У белых больше одного короля.", twoWhiteKings.Validate()[0]);  // текст про лишнего короля

        // пешка на 8-й горизонтали
        Assert.Single(PositionBuilder.FromFen("4k2P/8/8/8/8/8/8/4K3 w - - 0 1").Validate());
        // пешка на 1-й горизонтали
        Assert.Single(PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K2p w - - 0 1").Validate());
        // две пешки на краю — одна ошибка
        Assert.Single(PositionBuilder.FromFen("4k2P/8/8/8/8/8/8/4K2p w - - 0 1").Validate());
        // пешка на 2-й горизонтали допустима
        Assert.Empty(PositionBuilder.FromFen("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1").Validate());

        // сторона не на ходу под шахом
        Assert.Single(PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1").Validate());
        // текст про шах
        Assert.Equal("Чёрные под шахом, но сейчас ход белых.", PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1").Validate()[0]);
        // текст про шах белым
        Assert.Equal("Белые под шахом, но сейчас ход чёрных.", PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4K1r1 b - - 0 1").Validate()[0]);
        // шах стороне на ходу допустим
        Assert.Empty(PositionBuilder.FromFen("4k3/8/8/8/8/8/8/4R1K1 b - - 0 1").Validate());

        // Мат и пат — не ошибки, такую позицию можно поставить
        // мат можно поставить
        Assert.Empty(PositionBuilder.FromFen("6kR/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1").Validate());
        // пат можно поставить
        Assert.Empty(PositionBuilder.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1").Validate());

        // Полный сценарий редактора: собрать позицию и начать с неё партию
        var builder = new PositionBuilder();
        builder[Sq.Parse("e1")] = new Piece(PieceColor.White, PieceType.King);
        builder[Sq.Parse("a1")] = new Piece(PieceColor.White, PieceType.Rook);
        builder[Sq.Parse("h8")] = new Piece(PieceColor.Black, PieceType.King);
        builder.Castling = CastlingRights.All;
        builder.Normalize();
        Assert.Equal("Q", CastlingText(builder.Castling));  // осталось только реальное право на длинную рокировку
        Assert.Empty(builder.Validate());  // позиция корректна

        var game = new Game(builder.ToFen());
        Assert.Equal(builder.ToFen(), game.StartFen);  // партия начинается с расставленной позиции
        Assert.True(game.TryAddSan("Ra8+", out var node), "ход из расставленной позиции делается");
        Assert.Equal("Ra8+", node!.San);  // ход записан в нотации
        Assert.Equal("1", game.Headers["SetUp"]);  // заголовок SetUp
    }
}
