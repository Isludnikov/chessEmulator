using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>Базовые типы: клетки, фигуры, ходы.</summary>
internal static class TypesTests
{
    public static void Run()
    {
        Squares();
        Pieces();
        Moves();
    }

    private static void Squares()
    {
        Test.Suite("Типы: клетки", () =>
        {
            Test.Check("a1 — нулевая клетка", 0, Sq.Parse("a1"));
            Test.Check("h1", 7, Sq.Parse("h1"));
            Test.Check("a8", 56, Sq.Parse("a8"));
            Test.Check("h8", 63, Sq.Parse("h8"));
            Test.Check("e4", Sq.Of(4, 3), Sq.Parse("e4"));

            // Полный круг: имя → индекс → имя для всех 64 клеток
            var mismatches = 0;
            for (var i = 0; i < 64; i++)
                if (Sq.Parse(Sq.Name(i)) != i) mismatches++;
            Test.Check("имя и индекс совпадают на всех клетках", 0, mismatches);

            Test.Check("вертикаль e4", 4, Sq.File(Sq.Parse("e4")));
            Test.Check("горизонталь e4", 3, Sq.Rank(Sq.Parse("e4")));
            Test.Check("заглавные буквы допустимы", Sq.Parse("e4"), Sq.Parse("E4"));
            Test.Check("клетки вне доски", Sq.None, Sq.Parse("j9"));
            Test.Check("короткая строка", Sq.None, Sq.Parse("e"));
            Test.Check("имя несуществующей клетки", "-", Sq.Name(Sq.None));
            Test.Check("имя за пределами массива", "-", Sq.Name(64));
            Test.False("клетка -1 недопустима", Sq.IsValid(Sq.None));
            Test.False("клетка 64 недопустима", Sq.IsValid(64));
            Test.True("клетка 63 допустима", Sq.IsValid(63));
            Test.False("вертикаль 8 недопустима", Sq.IsValid(8, 0));
        });
    }

    private static void Pieces()
    {
        Test.Suite("Типы: фигуры", () =>
        {
            Test.True("пустая фигура", Piece.Empty.IsEmpty);
            Test.Check("символ пустой клетки", ".", Piece.Empty.ToFenChar().ToString());

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
            Test.Check("упаковка и символ FEN для всех 12 фигур", 0, mismatches);

            Test.Check("белая пешка — P", "P", new Piece(PieceColor.White, PieceType.Pawn).ToFenChar().ToString());
            Test.Check("чёрный конь — n", "n", new Piece(PieceColor.Black, PieceType.Knight).ToFenChar().ToString());
            Test.True("Is проверяет цвет и тип", new Piece(PieceColor.Black, PieceType.Rook).Is(PieceColor.Black, PieceType.Rook));
            Test.False("Is различает цвет", new Piece(PieceColor.White, PieceType.Rook).Is(PieceColor.Black, PieceType.Rook));
            Test.False("пустая фигура не Is", Piece.Empty.Is(PieceColor.White, PieceType.Pawn));
            Test.True("неизвестный символ даёт пусто", Piece.FromFenChar('x').IsEmpty);
            Test.True("равенство по значению",
                new Piece(PieceColor.White, PieceType.Queen) == new Piece(PieceColor.White, PieceType.Queen));
            Test.True("неравенство разных цветов",
                new Piece(PieceColor.White, PieceType.Queen) != new Piece(PieceColor.Black, PieceType.Queen));
        });
    }

    private static void Moves()
    {
        Test.Suite("Типы: ходы", () =>
        {
            Test.Check("обычный ход в UCI", "e2e4", new Move(Sq.Parse("e2"), Sq.Parse("e4")).ToUci());
            Test.Check("превращение в ферзя", "a7a8q",
                new Move(Sq.Parse("a7"), Sq.Parse("a8"), PieceType.Queen).ToUci());
            Test.Check("превращение в коня", "b2b1n",
                new Move(Sq.Parse("b2"), Sq.Parse("b1"), PieceType.Knight).ToUci());

            var parsed = Move.FromUci("e7e8r");
            Test.Check("разбор UCI: откуда", Sq.Parse("e7"), parsed.From);
            Test.Check("разбор UCI: куда", Sq.Parse("e8"), parsed.To);
            Test.Check("разбор UCI: превращение", PieceType.Rook, parsed.Promotion);

            Test.Check("полный круг UCI", "g1f3", Move.FromUci("g1f3").ToUci());
            Test.Check("полный круг с превращением", "h7h8b", Move.FromUci("h7h8b").ToUci());
            Test.True("короткая строка — пустой ход", Move.FromUci("e2").IsNone);
            Test.True("мусор — пустой ход", Move.FromUci("zzzz").IsNone);
            Test.True("неизвестная буква превращения отбрасывается",
                Move.FromUci("a7a8x").Promotion == PieceType.None);
            Test.True("ToString даёт UCI", Move.FromUci("d2d4").ToString() == "d2d4");

            var a = new Move(12, 28);
            var b = new Move(12, 28);
            var c = new Move(12, 28, PieceType.Queen);
            Test.True("равенство ходов", a == b);
            Test.True("превращение различает ходы", a != c);
            Test.Check("одинаковый хеш у равных ходов", a.GetHashCode(), b.GetHashCode());
            Test.True("хеши разных ходов различаются", a.GetHashCode() != c.GetHashCode());
        });
    }
}
