namespace ChessEmulator.Chess;

public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6
}

public enum PieceColor : byte
{
    White = 0,
    Black = 1
}

[Flags]
public enum CastlingRights
{
    None = 0,
    WhiteKing = 1,
    WhiteQueen = 2,
    BlackKing = 4,
    BlackQueen = 8,
    All = WhiteKing | WhiteQueen | BlackKing | BlackQueen
}

/// <summary>Фигура, упакованная в один байт: 0 — пусто, иначе (цвет * 8) | тип.</summary>
public readonly struct Piece : IEquatable<Piece>
{
    public readonly byte Value;

    public Piece(byte value) => Value = value;

    public Piece(PieceColor color, PieceType type) => Value = (byte)(((byte)color << 3) | (byte)type);

    public static readonly Piece Empty = new(0);

    public bool IsEmpty => Value == 0;
    public PieceType Type => (PieceType)(Value & 7);
    public PieceColor Color => (PieceColor)((Value >> 3) & 1);

    public bool Is(PieceColor color, PieceType type) => !IsEmpty && Color == color && Type == type;

    public char ToFenChar()
    {
        if (IsEmpty) return '.';
        var c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => '.'
        };
        return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
    }

    public static Piece FromFenChar(char c)
    {
        var color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
        var type = char.ToLowerInvariant(c) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => PieceType.None
        };
        return type == PieceType.None ? Empty : new Piece(color, type);
    }

    public bool Equals(Piece other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Piece p && Equals(p);
    public override int GetHashCode() => Value;
    public static bool operator ==(Piece a, Piece b) => a.Value == b.Value;
    public static bool operator !=(Piece a, Piece b) => a.Value != b.Value;
}

/// <summary>Работа с индексами клеток: 0 = a1, 7 = h1, 56 = a8, 63 = h8.</summary>
public static class Sq
{
    public const int None = -1;

    public static int File(int square) => square & 7;
    public static int Rank(int square) => square >> 3;
    public static int Of(int file, int rank) => (rank << 3) | file;
    public static bool IsValid(int square) => square >= 0 && square < 64;
    public static bool IsValid(int file, int rank) => file >= 0 && file < 8 && rank >= 0 && rank < 8;

    public static string Name(int square) =>
        IsValid(square) ? $"{(char)('a' + File(square))}{(char)('1' + Rank(square))}" : "-";

    public static int Parse(string name)
    {
        if (name.Length < 2) return None;
        var file = char.ToLowerInvariant(name[0]) - 'a';
        var rank = name[1] - '1';
        return IsValid(file, rank) ? Of(file, rank) : None;
    }
}

/// <summary>Ход: откуда, куда и (при превращении) новая фигура.</summary>
public readonly struct Move : IEquatable<Move>
{
    public readonly int From;
    public readonly int To;
    public readonly PieceType Promotion;

    public Move(int from, int to, PieceType promotion = PieceType.None)
    {
        From = from;
        To = to;
        Promotion = promotion;
    }

    public static readonly Move None = new(0, 0);

    public bool IsNone => From == 0 && To == 0;

    public string ToUci()
    {
        var s = Sq.Name(From) + Sq.Name(To);
        return Promotion switch
        {
            PieceType.Queen => s + "q",
            PieceType.Rook => s + "r",
            PieceType.Bishop => s + "b",
            PieceType.Knight => s + "n",
            _ => s
        };
    }

    public static Move FromUci(string uci)
    {
        if (uci.Length < 4) return None;
        var from = Sq.Parse(uci[..2]);
        var to = Sq.Parse(uci.Substring(2, 2));
        if (from == Sq.None || to == Sq.None) return None;
        var promo = PieceType.None;
        if (uci.Length >= 5)
        {
            promo = char.ToLowerInvariant(uci[4]) switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => PieceType.None
            };
        }
        return new Move(from, to, promo);
    }

    public bool Equals(Move other) => From == other.From && To == other.To && Promotion == other.Promotion;
    public override bool Equals(object? obj) => obj is Move m && Equals(m);
    public override int GetHashCode() => From | (To << 6) | ((int)Promotion << 12);
    public static bool operator ==(Move a, Move b) => a.Equals(b);
    public static bool operator !=(Move a, Move b) => !a.Equals(b);
    public override string ToString() => ToUci();
}

public enum GameResultState
{
    InProgress,
    WhiteWins,
    BlackWins,
    Draw
}

public enum GameEndReason
{
    None,
    Checkmate,
    Stalemate,
    InsufficientMaterial,
    FiftyMoveRule,
    ThreefoldRepetition
}
