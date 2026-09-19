using ChessEmulator.Chess;
using Xunit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>Доска: попадание мышью по клеткам, ходы, режим редактирования, отрисовка.</summary>
public class BoardControlTests
{
    // Доска шире, чем выше: так проверяется и центрирование поля на контроле.
    private static readonly Size ControlSize = new(600, 520);
    private const int Square = 65;
    private static readonly Point Origin = new((600 - Square * 8) / 2, 0);

    private static BoardControl NewBoard(string? fen = null, bool flipped = false)
    {
        var board = new BoardControl { ClientSize = ControlSize, Flipped = flipped };
        if (fen != null) board.Position = Position.FromFen(fen);
        return board;
    }

    /// <summary>Центр клетки в пикселях — так же, как его считает сама доска.</summary>
    private static Point Center(int square, bool flipped)
    {
        int file = Sq.File(square), rank = Sq.Rank(square);
        var col = flipped ? 7 - file : file;
        var row = flipped ? rank : 7 - rank;
        return new Point(Origin.X + col * Square + Square / 2, Origin.Y + row * Square + Square / 2);
    }

    [WinFormsFact(DisplayName = "Доска: попадание по клеткам")]
    public void Geometry()
    {
        foreach (var flipped in new[] { false, true })
        {
            using var board = NewBoard(Position.StartFen, flipped);
            board.Mode = BoardMode.Edit;
            var last = Sq.None;
            board.EditSquareClicked += (_, e) => last = e.Square;

            var misses = 0;
            for (var square = 0; square < 64; square++)
            {
                last = Sq.None;
                UiHarness.Click(board, Center(square, flipped));
                if (last != square) misses++;
            }
            Assert.Equal(0, misses);

            // Углы доски
            last = Sq.None;
            UiHarness.Click(board, new Point(Origin.X + 1, Origin.Y + 1));
            Assert.Equal(flipped ? "h1" : "a8", Sq.Name(last));

            last = Sq.None;
            UiHarness.Click(board, new Point(Origin.X + Square * 8 - 2, Origin.Y + Square * 8 - 2));
            Assert.Equal(flipped ? "a8" : "h1", Sq.Name(last));

            // Мимо доски
            last = Sq.None;
            UiHarness.Click(board, new Point(2, 10));
            Assert.Equal(Sq.None, last);  // щелчок мимо доски
            UiHarness.Click(board, new Point(Origin.X + Square * 8 + 10, 10));
            Assert.Equal(Sq.None, last);  // щелчок правее доски
        }
    }

    [WinFormsFact(DisplayName = "Доска: режим редактирования")]
    public void Editing()
    {
        using var board = NewBoard(Position.StartFen);
        var clicks = new List<(int Square, MouseButtons Button)>();
        var drags = new List<(int From, int To)>();
        board.EditSquareClicked += (_, e) => clicks.Add((e.Square, e.Button));
        board.EditPieceDragged += (_, e) => drags.Add((e.From, e.To));

        // В обычном режиме событий редактора нет
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        Assert.Empty(clicks);  // в режиме игры щелчки редактора не приходят

        board.Mode = BoardMode.Edit;
        UiHarness.Click(board, Center(Sq.Parse("d4"), false));
        Assert.Single(clicks);  // левый щелчок пришёл
        Assert.Equal("d4", Sq.Name(clicks[0].Square));  // клетка щелчка
        Assert.Equal(MouseButtons.Left, clicks[0].Button);  // кнопка щелчка

        UiHarness.Click(board, Center(Sq.Parse("e7"), false), MouseButtons.Right);
        Assert.Equal(2, clicks.Count);  // правый щелчок пришёл
        Assert.Equal(MouseButtons.Right, clicks[1].Button);  // кнопка правого щелчка
        Assert.Equal("e7", Sq.Name(clicks[1].Square));  // клетка правого щелчка

        // Перетаскивание фигуры
        clicks.Clear();
        UiHarness.Drag(board, Center(Sq.Parse("e2"), false), Center(Sq.Parse("e4"), false));
        Assert.Single(drags);  // перетаскивание пришло
        Assert.Equal("e2", Sq.Name(drags[0].From));  // откуда
        Assert.Equal("e4", Sq.Name(drags[0].To));  // куда
        Assert.Empty(clicks);  // перетаскивание не считается щелчком

        // Сброс мимо доски удаляет фигуру
        drags.Clear();
        UiHarness.Drag(board, Center(Sq.Parse("d2"), false), new Point(4, 10));
        Assert.Single(drags);  // сброс мимо доски пришёл
        Assert.Equal(Sq.None, drags[0].To);  // цель — не клетка

        // Пустую клетку перетащить нельзя — это обычный щелчок
        drags.Clear();
        clicks.Clear();
        UiHarness.Drag(board, Center(Sq.Parse("d4"), false), Center(Sq.Parse("d5"), false));
        Assert.Empty(drags);  // пустая клетка не перетаскивается
        Assert.Single(clicks);  // пустая клетка даёт щелчок
        Assert.Equal("d4", Sq.Name(clicks[0].Square));  // щелчок по исходной клетке

        // В режиме редактирования ходы не делаются
        var moved = false;
        board.MoveMade += (_, _) => moved = true;
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.False(moved, "ход в режиме редактирования не делается");

        Assert.True(board.EditBrush.IsEmpty, "кисть по умолчанию пуста");
        board.EditBrush = new Piece(PieceColor.Black, PieceType.Queen);
        Assert.Equal("q", board.EditBrush.ToFenChar().ToString());  // кисть запоминается
    }

    [WinFormsFact(DisplayName = "Доска: ходы мышью")]
    public void Playing()
    {
        using var board = NewBoard(Position.StartFen);
        var moves = new List<Chess.Move>();
        board.MoveMade += (_, e) => moves.Add(e.Move);

        // Щёлкнул фигуру — щёлкнул клетку
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.Single(moves);  // ход сделан щелчками
        Assert.Equal("e2e4", moves[0].ToUci());  // это e2-e4

        // Перетаскиванием
        moves.Clear();
        UiHarness.Drag(board, Center(Sq.Parse("g1"), false), Center(Sq.Parse("f3"), false));
        Assert.Single(moves);  // ход сделан перетаскиванием
        Assert.Equal("g1f3", moves[0].ToUci());  // это g1-f3

        // Чужая фигура не берётся
        moves.Clear();
        UiHarness.Click(board, Center(Sq.Parse("e7"), false));
        UiHarness.Click(board, Center(Sq.Parse("e5"), false));
        Assert.Empty(moves);  // фигурой соперника не ходим

        // Нелегальный ход не проходит
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        UiHarness.Click(board, Center(Sq.Parse("e5"), false));
        Assert.Empty(moves);  // нелегальный ход не проходит

        // Правая кнопка снимает выделение
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        UiHarness.Click(board, Center(Sq.Parse("a1"), false), MouseButtons.Right);
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.Empty(moves);  // после снятия выделения хода нет

        // Запрет взаимодействия
        board.InteractionEnabled = false;
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.Empty(moves);  // при запрете ходов их нет
        board.InteractionEnabled = true;

        // Превращение пешки
        using var promo = NewBoard("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
        var promoted = new List<Chess.Move>();
        var asked = 0;
        promo.MoveMade += (_, e) => promoted.Add(e.Move);
        promo.PromotionNeeded += (_, e) => { asked++; e.Selected = PieceType.Rook; };

        UiHarness.Click(promo, Center(Sq.Parse("a7"), false));
        UiHarness.Click(promo, Center(Sq.Parse("a8"), false));
        Assert.Equal(1, asked);  // спросили про превращение
        Assert.Single(promoted);  // ход с превращением сделан
        Assert.Equal("a7a8r", promoted[0].ToUci());  // выбрана ладья
        Assert.Equal(PieceColor.White, new PromotionEventArgs(PieceColor.White).Color);  // цвет в запросе

        // Отказ от превращения отменяет ход
        using var cancelled = NewBoard("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
        var moveAfterCancel = false;
        cancelled.PromotionNeeded += (_, e) => e.Cancelled = true;
        cancelled.MoveMade += (_, _) => moveAfterCancel = true;
        UiHarness.Click(cancelled, Center(Sq.Parse("a7"), false));
        UiHarness.Click(cancelled, Center(Sq.Parse("a8"), false));
        Assert.False(moveAfterCancel, "отказ отменяет ход");

        // Смена позиции сбрасывает выделение
        moves.Clear();
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        board.Position = Position.FromFen(Position.StartFen);
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.Empty(moves);  // после смены позиции выделение снято

        // Смена режима тоже сбрасывает выделение
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        board.Mode = BoardMode.Edit;
        board.Mode = BoardMode.Play;
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.Empty(moves);  // после смены режима выделение снято
    }

    [WinFormsFact(DisplayName = "Доска: отрисовка")]
    public void Drawing()
    {
        using var start = NewBoard(Position.StartFen);
        using var empty = NewBoard("8/8/8/8/8/8/8/8 w - - 0 1");

        using var startImage = UiHarness.Render(start);
        using var emptyImage = UiHarness.Render(empty);

        Assert.Equal(ControlSize, startImage.Size);  // картинка по размеру контрола
        Assert.True(UiHarness.Difference(startImage, emptyImage) > 5000, "фигуры нарисованы");

        // Фигуры именно там, где стоят: на e1 есть фигура, на e4 — нет
        var e1 = new Rectangle(Center(Sq.Parse("e1"), false).X - 20, Center(Sq.Parse("e1"), false).Y - 20, 40, 40);
        var e4 = new Rectangle(Center(Sq.Parse("e4"), false).X - 20, Center(Sq.Parse("e4"), false).Y - 20, 40, 40);
        Assert.True(UiHarness.Difference(startImage, emptyImage, e1) > 200, "на e1 нарисован король");
        Assert.Equal(0, UiHarness.Difference(startImage, emptyImage, e4));  // на e4 пусто

        using var flipped = NewBoard(Position.StartFen, flipped: true);
        using var flippedImage = UiHarness.Render(flipped);
        Assert.True(UiHarness.Difference(startImage, flippedImage) > 5000, "перевёрнутая доска выглядит иначе");

        // Подсветка последнего хода
        using var highlighted = NewBoard(Position.StartFen);
        highlighted.LastMove = Chess.Move.FromUci("e2e4");
        using var highlightedImage = UiHarness.Render(highlighted);
        Assert.True(UiHarness.Difference(startImage, highlightedImage) > 100, "последний ход подсвечен");

        // Стрелки
        using var arrows = NewBoard(Position.StartFen);
        arrows.SetArrows(new[] { new BoardArrow(Sq.Parse("e2"), Sq.Parse("e4"), Color.LimeGreen) });
        using var arrowImage = UiHarness.Render(arrows);
        Assert.True(UiHarness.Difference(startImage, arrowImage) > 500, "стрелка нарисована");

        arrows.ClearArrows();
        using var withoutArrows = UiHarness.Render(arrows);
        // после очистки стрелок картинка прежняя
        Assert.Equal(0, UiHarness.Difference(startImage, withoutArrows));

        // Координаты
        using var noCoordinates = NewBoard(Position.StartFen);
        noCoordinates.ShowCoordinates = false;
        using var noCoordinatesImage = UiHarness.Render(noCoordinates);
        Assert.True(UiHarness.Difference(startImage, noCoordinatesImage) > 50, "координаты можно убрать");

        // Рамка редактора
        using var editing = NewBoard(Position.StartFen);
        editing.Mode = BoardMode.Edit;
        using var editingImage = UiHarness.Render(editing);
        Assert.True(UiHarness.Difference(startImage, editingImage) > 100, "в режиме редактора доска обведена рамкой");

        // Шах подсвечивается: сравниваем именно клетку короля, ладья стоит в другом месте
        using var check = NewBoard("4k3/8/8/8/8/8/8/4R1K1 b - - 0 1");
        using var checkImage = UiHarness.Render(check);
        using var noCheck = NewBoard("4k3/8/8/8/8/8/8/5RK1 b - - 0 1");
        using var noCheckImage = UiHarness.Render(noCheck);
        var e8 = new Rectangle(Center(Sq.Parse("e8"), false).X - 25, Center(Sq.Parse("e8"), false).Y - 25, 50, 50);
        Assert.True(UiHarness.Difference(checkImage, noCheckImage, e8) > 200, "клетка короля под шахом окрашена");

        // крошечная доска рисуется без ошибок
        Assert.Null(Record.Exception(() =>
        {
            using var tiny = new BoardControl { ClientSize = new Size(64, 64) };
            using var image = UiHarness.Render(tiny);
        }));
    }

    [WinFormsFact(DisplayName = "Доска: плашка с результатом")]
    public void ResultBanner()
    {
        var boardRect = new Rectangle(Origin.X, Origin.Y, Square * 8, Square * 8);
        var band = new Rectangle(Origin.X, Origin.Y + Square * 3, Square * 8, Square * 2);
        var style = new BoardBannerStyle(Color.FromArgb(26, 26, 28), Color.Gainsboro, Color.FromArgb(190, 190, 196));

        using var board = NewBoard(Position.StartFen);
        Assert.False(board.HasResultBanner);
        using var plain = UiHarness.Render(board);

        board.ShowResultBanner("МАТ · ПОБЕДА ЧЁРНЫХ", "0–1", style);
        Assert.True(board.HasResultBanner, "плашка показана");
        using var withBanner = UiHarness.Render(board);
        Assert.True(UiHarness.Difference(plain, withBanner, band) > 5000, "плашка закрывает середину доски");

        // Всё нарисованное осталось внутри доски
        Assert.Equal(UiHarness.Difference(plain, withBanner), UiHarness.Difference(plain, withBanner, boardRect));

        // Доска лишь притенена, а не закрашена: светлая клетка осталась светлее тёмной
        var light = withBanner.GetPixel(Center(Sq.Parse("b3"), false).X, Center(Sq.Parse("b3"), false).Y);
        var dark = withBanner.GetPixel(Center(Sq.Parse("a3"), false).X, Center(Sq.Parse("a3"), false).Y);
        Assert.True(light.R > dark.R, "клетки под плашкой ещё различимы");

        // Повторный показ того же результата ничего не меняет
        board.ShowResultBanner("МАТ · ПОБЕДА ЧЁРНЫХ", "0–1", style);
        using var again = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(withBanner, again));

        // Другой результат выглядит иначе
        board.ShowResultBanner("НИЧЬЯ · ПАТ", "½–½", new BoardBannerStyle(
            Color.FromArgb(34, 34, 38), Color.Gainsboro, Color.FromArgb(220, 180, 90)));
        using var draw = UiHarness.Render(board);
        Assert.True(UiHarness.Difference(withBanner, draw) > 1000, "у ничьей своя плашка");

        board.ClearResultBanner();
        Assert.False(board.HasResultBanner);
        using var cleared = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(plain, cleared));  // после снятия плашки картинка прежняя

        // Длинный заголовок ужимается и тоже остаётся внутри доски
        board.ShowResultBanner("НИЧЬЯ · НЕДОСТАТОЧНО МАТЕРИАЛА", "½–½", style);
        using var longHeadline = UiHarness.Render(board);
        Assert.Equal(UiHarness.Difference(plain, longHeadline), UiHarness.Difference(plain, longHeadline, boardRect));

        // Крошечная доска с плашкой рисуется без ошибок
        Assert.Null(Record.Exception(() =>
        {
            using var tiny = new BoardControl { ClientSize = new Size(64, 64) };
            tiny.ShowResultBanner("НИЧЬЯ · ТРОЕКРАТНОЕ ПОВТОРЕНИЕ", "½–½", style);
            using var image = UiHarness.Render(tiny);
            Assert.True(new Rectangle(0, 0, 64, 64).Contains(UiHarness.InkBounds(image, tiny.BackColor)),
                "плашка не вылезает за контрол");
        }));
    }

    [WinFormsFact(DisplayName = "Доска: плашка гаснет от щелчка")]
    public void ResultBannerDismiss()
    {
        var style = new BoardBannerStyle(Color.FromArgb(26, 26, 28), Color.Gainsboro, Color.FromArgb(190, 190, 196));

        using var board = NewBoard(Position.StartFen);
        var dismissed = 0;
        board.ResultBannerDismissed += (_, _) => dismissed++;
        var moves = new List<Chess.Move>();
        board.MoveMade += (_, e) => moves.Add(e.Move);

        using var plain = UiHarness.Render(board);
        board.ShowResultBanner("МАТ · ПОБЕДА БЕЛЫХ", "1–0", style);

        // Щелчок по пустой клетке только гасит плашку — доска возвращается в прежний вид
        UiHarness.Click(board, Center(Sq.Parse("e5"), false));
        Assert.Equal(1, dismissed);
        Assert.False(board.HasResultBanner, "щелчок убирает плашку");
        using var cleared = UiHarness.Render(board);
        Assert.Equal(0, UiHarness.Difference(plain, cleared));

        UiHarness.Click(board, Center(Sq.Parse("e5"), false));
        Assert.Equal(1, dismissed);  // погашенная плашка больше о себе не сообщает

        // Щелчок не съеден: он же выбирает фигуру, и ход доходит до владельца
        board.ShowResultBanner("МАТ · ПОБЕДА БЕЛЫХ", "1–0", style);
        UiHarness.Click(board, Center(Sq.Parse("e2"), false));
        Assert.Equal(2, dismissed);
        UiHarness.Click(board, Center(Sq.Parse("e4"), false));
        Assert.Single(moves);
        Assert.Equal("e2e4", moves[0].ToUci());

        // Редактор гасит плашку молча
        board.ShowResultBanner("МАТ · ПОБЕДА БЕЛЫХ", "1–0", style);
        board.Mode = BoardMode.Edit;
        Assert.False(board.HasResultBanner, "в редакторе плашки нет");
        Assert.Equal(2, dismissed);
    }
}
