using ChessEmulator.Chess;
using ChessEmulator.UI;
using Xunit;

namespace ChessEmulator.UiTests;

/// <summary>
/// Доска: подсветки, которые появляются от мыши, — выбранная фигура, доступные ходы,
/// клетка под курсором и перетаскиваемая фигура.
/// </summary>
public class BoardControlHighlightTests
{
    // Те же размеры, что и в BoardControlTests: доска шире, чем выше, поле стоит по центру.
    private static readonly Size ControlSize = new(600, 520);
    private const int Square = 65;
    private static readonly Point Origin = new((600 - Square * 8) / 2, 0);

    private static BoardControl NewBoard(string fen) =>
        new() { ClientSize = ControlSize, Position = Position.FromFen(fen) };

    /// <summary>Клетка целиком, в пикселях.</summary>
    private static Rectangle Cell(string name)
    {
        var square = Sq.Parse(name);
        return new Rectangle(
            Origin.X + Sq.File(square) * Square, Origin.Y + (7 - Sq.Rank(square)) * Square, Square, Square);
    }

    private static Point Center(string name)
    {
        var cell = Cell(name);
        return new Point(cell.X + Square / 2, cell.Y + Square / 2);
    }

    [WinFormsFact(DisplayName = "Доска: выбранная фигура и доступные ходы подсвечены")]
    public void ВыборИЦели()
    {
        // Пешка e4 может пойти на e5 и взять на d5.
        const string fen = "4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1";
        using var board = NewBoard(fen);
        Assert.Equal(fen, board.Position.ToFen());
        using var plain = UiHarness.Render(board);

        UiHarness.Click(board, Center("e4"));
        using var selected = UiHarness.Render(board);

        Assert.True(UiHarness.Difference(plain, selected, Cell("e4")) > 1000, "выбранная клетка окрашена");
        Assert.True(UiHarness.Difference(plain, selected, Cell("e5")) > 50, "свободная клетка помечена точкой");
        Assert.True(UiHarness.Difference(plain, selected, Cell("d5")) > 50, "взятие обведено кольцом");
        Assert.Equal(0, UiHarness.Difference(plain, selected, Cell("f5")));  // туда пешка не ходит

        // Точки и кольца — подсказка, её можно выключить; выбор при этом виден по-прежнему.
        board.ShowLegalMoveHints = false;
        using var withoutHints = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(plain, withoutHints, Cell("e5")));
        Assert.Equal(0, UiHarness.Difference(plain, withoutHints, Cell("d5")));
        Assert.True(UiHarness.Difference(plain, withoutHints, Cell("e4")) > 1000, "выбранная клетка окрашена");
        board.ShowLegalMoveHints = true;

        // Правая кнопка снимает выбор вместе с пометками.
        UiHarness.Click(board, Center("a1"), MouseButtons.Right);
        using var cleared = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(plain, cleared));
    }

    [WinFormsFact(DisplayName = "Доска: взятие на проходе помечено как взятие")]
    public void ВзятиеНаПроходе()
    {
        using var board = NewBoard("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
        using var plain = UiHarness.Render(board);

        UiHarness.Click(board, Center("e5"));
        using var selected = UiHarness.Render(board);

        // Клетка d6 пуста, но ход на неё — взятие: там кольцо, середина клетки свободна.
        var d6 = Center("d6");
        Assert.True(UiHarness.Difference(plain, selected, Cell("d6")) > 50, "кольцо нарисовано");
        Assert.Equal(plain.GetPixel(d6.X, d6.Y).ToArgb(), selected.GetPixel(d6.X, d6.Y).ToArgb());

        // А на e6 обычный ход — точка посередине клетки.
        var e6 = Center("e6");
        Assert.NotEqual(plain.GetPixel(e6.X, e6.Y).ToArgb(), selected.GetPixel(e6.X, e6.Y).ToArgb());
    }

    [WinFormsFact(DisplayName = "Доска: клетка под курсором подсвечена")]
    public void КлеткаПодКурсором()
    {
        using var board = NewBoard(Position.StartFen);
        using var plain = UiHarness.Render(board);

        UiHarness.MouseMove(board, Center("e4"), MouseButtons.None);
        using var hovered = UiHarness.Render(board);
        Assert.True(UiHarness.Difference(plain, hovered, Cell("e4")) > 1000, "клетка под курсором светлее");
        // Подсвечена только она; сглаживание задевает по пикселю от соседних клеток.
        Assert.Equal(UiHarness.Difference(plain, hovered),
            UiHarness.Difference(plain, hovered, Rectangle.Inflate(Cell("e4"), 1, 1)));

        UiHarness.MouseLeave(board);
        using var left = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(plain, left));  // курсор ушёл — подсветка погасла

        // Запертая доска на курсор не откликается: ходить всё равно нельзя.
        board.InteractionEnabled = false;
        UiHarness.MouseMove(board, Center("d4"), MouseButtons.None);
        using var locked = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(plain, locked));
    }

    [WinFormsFact(DisplayName = "Доска: перетаскиваемая фигура рисуется под курсором")]
    public void ФигураПодКурсором()
    {
        using var board = NewBoard(Position.StartFen);
        var moves = new List<Move>();
        board.MoveMade += (_, e) => moves.Add(e.Move);

        // Пешка взята, курсор уже над e5, но кнопка ещё не считается перетаскиванием.
        UiHarness.MouseDown(board, Center("e2"));
        UiHarness.MouseMove(board, Center("e5"), MouseButtons.None);
        using var picked = UiHarness.Render(board);

        UiHarness.MouseMove(board, Center("e5"));
        using var dragging = UiHarness.Render(board);

        Assert.True(UiHarness.Difference(picked, dragging, Cell("e2")) > 200, "с исходной клетки фигура снята");
        Assert.True(UiHarness.Difference(picked, dragging, Cell("e5")) > 200, "и нарисована под курсором");
        // больше на доске ничего не изменилось
        Assert.Equal(UiHarness.Difference(picked, dragging),
            UiHarness.Difference(picked, dragging, Cell("e2")) + UiHarness.Difference(picked, dragging, Cell("e5")));

        // Пешка с e2 на e5 не ходит: бросок мимо цели возвращает её на место.
        UiHarness.MouseUp(board, Center("e5"));
        Assert.Empty(moves);
        using var dropped = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(picked, dropped));
    }

    [WinFormsFact(DisplayName = "Доска: превращение в недопустимую фигуру хода не даёт")]
    public void НедопустимоеПревращение()
    {
        using var board = NewBoard("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
        var moves = new List<Move>();
        board.MoveMade += (_, e) => moves.Add(e.Move);
        board.PromotionNeeded += (_, e) => e.Selected = PieceType.King;

        UiHarness.Click(board, Center("a7"));
        UiHarness.Click(board, Center("a8"));
        Assert.Empty(moves);  // пешка в короля не превращается

        // Выбор снят: следующий щелчок по a8 уже не считается ходом.
        UiHarness.Click(board, Center("a8"));
        Assert.Empty(moves);
    }

    [WinFormsFact(DisplayName = "Доска: дрожание мыши в редакторе остаётся щелчком")]
    public void ДрожаниеМыши()
    {
        using var board = NewBoard(Position.StartFen);
        board.Mode = BoardMode.Edit;
        var clicks = new List<int>();
        var drags = 0;
        board.EditSquareClicked += (_, e) => clicks.Add(e.Square);
        board.EditPieceDragged += (_, _) => drags++;

        // Сдвиг на пиксель меньше системного порога перетаскивания.
        var at = Center("e2");
        var nearby = new Point(at.X + 1, at.Y + 1);
        UiHarness.MouseDown(board, at);
        UiHarness.MouseMove(board, nearby);
        UiHarness.MouseUp(board, nearby);

        Assert.Equal(0, drags);  // перетаскивания не было
        Assert.Equal(Sq.Parse("e2"), Assert.Single(clicks));  // это щелчок по исходной клетке
    }
}
