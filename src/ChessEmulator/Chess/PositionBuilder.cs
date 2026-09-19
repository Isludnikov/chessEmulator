namespace ChessEmulator.Chess;

/// <summary>
/// Изменяемая расстановка фигур для редактора позиции. <see cref="Position"/> неизменяем,
/// поэтому произвольная позиция собирается здесь и только затем превращается в позицию.
/// </summary>
public sealed class PositionBuilder
{
    private readonly byte[] _squares = new byte[64];

    public PieceColor SideToMove { get; set; } = PieceColor.White;
    public CastlingRights Castling { get; set; } = CastlingRights.None;
    public int EnPassant { get; set; } = Sq.None;
    public int HalfmoveClock { get; set; }
    public int FullmoveNumber { get; set; } = 1;

    public Piece this[int square]
    {
        get => new(_squares[square]);
        set => _squares[square] = value.Value;
    }

    public static PositionBuilder FromPosition(Position position)
    {
        var builder = new PositionBuilder
        {
            SideToMove = position.SideToMove,
            Castling = position.Castling,
            EnPassant = position.EnPassant,
            HalfmoveClock = position.HalfmoveClock,
            FullmoveNumber = position.FullmoveNumber
        };
        for (var i = 0; i < 64; i++) builder._squares[i] = position[i].Value;
        return builder;
    }

    public static PositionBuilder FromFen(string fen) => FromPosition(Position.FromFen(fen));

    public static PositionBuilder StartPosition()
    {
        var builder = new PositionBuilder();
        builder.SetStartPosition();
        return builder;
    }

    /// <summary>Убирает с доски все фигуры и сбрасывает признаки позиции.</summary>
    public void Clear()
    {
        Array.Clear(_squares);
        SideToMove = PieceColor.White;
        Castling = CastlingRights.None;
        EnPassant = Sq.None;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
    }

    public void SetStartPosition()
    {
        var start = Position.FromFen(Position.StartFen);
        for (var i = 0; i < 64; i++) _squares[i] = start[i].Value;
        SideToMove = PieceColor.White;
        Castling = CastlingRights.All;
        EnPassant = Sq.None;
        HalfmoveClock = 0;
        FullmoveNumber = 1;
    }

    /// <summary>Переносит фигуру с одной клетки на другую (перетаскивание в редакторе).</summary>
    public void MovePiece(int from, int to)
    {
        if (from == to) return;
        _squares[to] = _squares[from];
        _squares[from] = 0;
    }

    public int CountPieces(PieceColor color, PieceType type)
    {
        var target = new Piece(color, type).Value;
        var count = 0;
        for (var i = 0; i < 64; i++)
        {
            if (_squares[i] == target) count++;
        }
        return count;
    }

    public int FindKing(PieceColor color)
    {
        var target = new Piece(color, PieceType.King).Value;
        for (var i = 0; i < 64; i++)
        {
            if (_squares[i] == target) return i;
        }
        return Sq.None;
    }

    public Position ToPosition() =>
        Position.Build(_squares, SideToMove, Castling, EnPassant, HalfmoveClock, FullmoveNumber);

    public string ToFen() => ToPosition().ToFen();

    // ------------------------------------------------- Автокоррекция и проверка

    /// <summary>
    /// Тихо приводит в порядок то, что пользователь мог сломать расстановкой:
    /// невозможные права на рокировку и поле взятия на проходе.
    /// </summary>
    public void Normalize()
    {
        var rights = Castling;

        if (!IsPiece(4, PieceColor.White, PieceType.King))
            rights &= ~(CastlingRights.WhiteKing | CastlingRights.WhiteQueen);
        if (!IsPiece(7, PieceColor.White, PieceType.Rook)) rights &= ~CastlingRights.WhiteKing;
        if (!IsPiece(0, PieceColor.White, PieceType.Rook)) rights &= ~CastlingRights.WhiteQueen;

        if (!IsPiece(60, PieceColor.Black, PieceType.King))
            rights &= ~(CastlingRights.BlackKing | CastlingRights.BlackQueen);
        if (!IsPiece(63, PieceColor.Black, PieceType.Rook)) rights &= ~CastlingRights.BlackKing;
        if (!IsPiece(56, PieceColor.Black, PieceType.Rook)) rights &= ~CastlingRights.BlackQueen;

        Castling = rights;

        if (EnPassant != Sq.None && !IsEnPassantPossible(EnPassant)) EnPassant = Sq.None;

        HalfmoveClock = Math.Clamp(HalfmoveClock, 0, 100);
        FullmoveNumber = Math.Max(1, FullmoveNumber);
    }

    /// <summary>Поля, которые можно назначить полем взятия на проходе при текущей расстановке.</summary>
    public IReadOnlyList<int> AvailableEnPassantSquares()
    {
        var result = new List<int>();
        var rank = SideToMove == PieceColor.White ? 5 : 2;
        for (var file = 0; file < 8; file++)
        {
            var square = Sq.Of(file, rank);
            if (IsEnPassantPossible(square)) result.Add(square);
        }
        return result;
    }

    private bool IsEnPassantPossible(int square)
    {
        if (!Sq.IsValid(square)) return false;

        var rank = Sq.Rank(square);
        // Поле «за» пешкой, которая только что шагнула через клетку.
        if (SideToMove == PieceColor.White && rank != 5) return false;
        if (SideToMove == PieceColor.Black && rank != 2) return false;
        if (_squares[square] != 0) return false;

        var pawnColor = Position.Other(SideToMove);
        var pawnSquare = SideToMove == PieceColor.White ? square - 8 : square + 8;
        var startSquare = SideToMove == PieceColor.White ? square + 8 : square - 8;

        return IsPiece(pawnSquare, pawnColor, PieceType.Pawn) && _squares[startSquare] == 0;
    }

    private bool IsPiece(int square, PieceColor color, PieceType type) =>
        Sq.IsValid(square) && new Piece(_squares[square]).Is(color, type);

    /// <summary>
    /// Ошибки, из-за которых позицию нельзя поставить на доску.
    /// Пустой список — позиция годится для игры и анализа.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        var whiteKings = CountPieces(PieceColor.White, PieceType.King);
        var blackKings = CountPieces(PieceColor.Black, PieceType.King);

        if (whiteKings != 1)
            errors.Add(whiteKings == 0
                ? "На доске нет белого короля."
                : "У белых больше одного короля.");

        if (blackKings != 1)
            errors.Add(blackKings == 0
                ? "На доске нет чёрного короля."
                : "У чёрных больше одного короля.");

        for (var file = 0; file < 8; file++)
        {
            foreach (var rank in new[] { 0, 7 })
            {
                if (new Piece(_squares[Sq.Of(file, rank)]).Type == PieceType.Pawn)
                {
                    errors.Add("Пешка не может стоять на 1-й или 8-й горизонтали.");
                    file = 8;
                    break;
                }
            }
        }

        if (whiteKings == 1 && blackKings == 1)
        {
            var position = ToPosition();
            var waiting = Position.Other(SideToMove);
            if (position.IsInCheck(waiting))
            {
                errors.Add(waiting == PieceColor.White
                    ? "Белые под шахом, но сейчас ход чёрных."
                    : "Чёрные под шахом, но сейчас ход белых.");
            }
        }

        return errors;
    }
}
