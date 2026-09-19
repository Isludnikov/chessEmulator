using ChessEmulator.Chess;
using Xunit;

namespace ChessEmulator.CoreTests;

/// <summary>Базовые типы: клетки, фигуры, ходы.</summary>
public class TypesTests
{
    [Fact(DisplayName = "Типы: клетки")]
    public void Squares()
    {
        Assert.Equal(0, Sq.Parse("a1"));  // a1 — нулевая клетка
        Assert.Equal(7, Sq.Parse("h1"));  // h1
        Assert.Equal(56, Sq.Parse("a8"));  // a8
        Assert.Equal(63, Sq.Parse("h8"));  // h8
        Assert.Equal(Sq.Of(4, 3), Sq.Parse("e4"));  // e4

        // Полный круг: имя → индекс → имя для всех 64 клеток
        var mismatches = 0;
        for (var i = 0; i < 64; i++)
            if (Sq.Parse(Sq.Name(i)) != i) mismatches++;
        Assert.Equal(0, mismatches);  // имя и индекс совпадают на всех клетках

        Assert.Equal(4, Sq.File(Sq.Parse("e4")));  // вертикаль e4
        Assert.Equal(3, Sq.Rank(Sq.Parse("e4")));  // горизонталь e4
        Assert.Equal(Sq.Parse("e4"), Sq.Parse("E4"));  // заглавные буквы допустимы
        Assert.Equal(Sq.None, Sq.Parse("j9"));  // клетки вне доски
        Assert.Equal(Sq.None, Sq.Parse("e"));  // короткая строка
        Assert.Equal("-", Sq.Name(Sq.None));  // имя несуществующей клетки
        Assert.Equal("-", Sq.Name(64));  // имя за пределами массива
        Assert.False(Sq.IsValid(Sq.None), "клетка -1 недопустима");
        Assert.False(Sq.IsValid(64), "клетка 64 недопустима");
        Assert.True(Sq.IsValid(63), "клетка 63 допустима");
        Assert.False(Sq.IsValid(8, 0), "вертикаль 8 недопустима");
    }

    [Fact(DisplayName = "Типы: фигуры")]
    public void Pieces()
    {
        Assert.True(Piece.Empty.IsEmpty, "пустая фигура");
        Assert.Equal(".", Piece.Empty.ToFenChar().ToString());  // символ пустой клетки

        // Упаковка в байт и распаковка для всех 12 фигур
        var mismatches = 0;
        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            foreach (var type in new[]
                     {
                         PieceType.Pawn, PieceType.Knight, PieceType.Bishop,
                         PieceType.Rook, PieceType.Queen, PieceType.King
                     })
            {
                var piece = new Piece(color, type);
                if (piece.Type != type || piece.Color != color || piece.IsEmpty) mismatches++;
                if (Piece.FromFenChar(piece.ToFenChar()) != piece) mismatches++;
            }
        }
        Assert.Equal(0, mismatches);  // упаковка и символ FEN для всех 12 фигур

        Assert.Equal("P", new Piece(PieceColor.White, PieceType.Pawn).ToFenChar().ToString());  // белая пешка — P
        Assert.Equal("n", new Piece(PieceColor.Black, PieceType.Knight).ToFenChar().ToString());  // чёрный конь — n
        Assert.True(new Piece(PieceColor.Black, PieceType.Rook).Is(PieceColor.Black, PieceType.Rook), "Is проверяет цвет и тип");
        Assert.False(new Piece(PieceColor.White, PieceType.Rook).Is(PieceColor.Black, PieceType.Rook), "Is различает цвет");
        Assert.False(Piece.Empty.Is(PieceColor.White, PieceType.Pawn), "пустая фигура не Is");
        Assert.True(Piece.FromFenChar('x').IsEmpty, "неизвестный символ даёт пусто");
        Assert.True(new Piece(PieceColor.White, PieceType.Queen) == new Piece(PieceColor.White, PieceType.Queen), "равенство по значению");
        Assert.True(new Piece(PieceColor.White, PieceType.Queen) != new Piece(PieceColor.Black, PieceType.Queen), "неравенство разных цветов");
    }

    [Fact(DisplayName = "Типы: ходы")]
    public void Moves()
    {
        Assert.Equal("e2e4", new Move(Sq.Parse("e2"), Sq.Parse("e4")).ToUci());  // обычный ход в UCI
        // превращение в ферзя
        Assert.Equal("a7a8q", new Move(Sq.Parse("a7"), Sq.Parse("a8"), PieceType.Queen).ToUci());
        // превращение в коня
        Assert.Equal("b2b1n", new Move(Sq.Parse("b2"), Sq.Parse("b1"), PieceType.Knight).ToUci());

        var parsed = Move.FromUci("e7e8r");
        Assert.Equal(Sq.Parse("e7"), parsed.From);  // разбор UCI: откуда
        Assert.Equal(Sq.Parse("e8"), parsed.To);  // разбор UCI: куда
        Assert.Equal(PieceType.Rook, parsed.Promotion);  // разбор UCI: превращение

        Assert.Equal("g1f3", Move.FromUci("g1f3").ToUci());  // полный круг UCI
        Assert.Equal("h7h8b", Move.FromUci("h7h8b").ToUci());  // полный круг с превращением
        Assert.True(Move.FromUci("e2").IsNone, "короткая строка — пустой ход");
        Assert.True(Move.FromUci("zzzz").IsNone, "мусор — пустой ход");
        Assert.True(Move.FromUci("a7a8x").Promotion == PieceType.None, "неизвестная буква превращения отбрасывается");
        Assert.True(Move.FromUci("d2d4").ToString() == "d2d4", "ToString даёт UCI");

        var a = new Move(12, 28);
        var b = new Move(12, 28);
        var c = new Move(12, 28, PieceType.Queen);
        Assert.True(a == b, "равенство ходов");
        Assert.True(a != c, "превращение различает ходы");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());  // одинаковый хеш у равных ходов
        Assert.True(a.GetHashCode() != c.GetHashCode(), "хеши разных ходов различаются");
    }
}
