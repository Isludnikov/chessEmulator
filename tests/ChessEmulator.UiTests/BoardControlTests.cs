using ChessEmulator.Chess;
using ChessEmulator.TestKit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>Доска: попадание мышью по клеткам, ходы, режим редактирования, отрисовка.</summary>
internal static class BoardControlTests
{
    // Доска шире, чем выше: так проверяется и центрирование поля на контроле.
    private static readonly Size ControlSize = new(600, 520);
    private const int Square = 65;
    private static readonly Point Origin = new((600 - Square * 8) / 2, 0);

    public static void Run()
    {
        Geometry();
        Editing();
        Playing();
        Drawing();
    }

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

    private static void Geometry()
    {
        Test.Suite("Доска: попадание по клеткам", () =>
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
                Test.Check(flipped ? "все 64 клетки, доска перевёрнута" : "все 64 клетки", 0, misses);

                // Углы доски
                last = Sq.None;
                UiHarness.Click(board, new Point(Origin.X + 1, Origin.Y + 1));
                Test.Check(flipped ? "левый верхний угол перевёрнутой доски" : "левый верхний угол",
                    flipped ? "h1" : "a8", Sq.Name(last));

                last = Sq.None;
                UiHarness.Click(board, new Point(Origin.X + Square * 8 - 2, Origin.Y + Square * 8 - 2));
                Test.Check(flipped ? "правый нижний угол перевёрнутой доски" : "правый нижний угол",
                    flipped ? "a8" : "h1", Sq.Name(last));

                // Мимо доски
                last = Sq.None;
                UiHarness.Click(board, new Point(2, 10));
                Test.Check("щелчок мимо доски", Sq.None, last);
                UiHarness.Click(board, new Point(Origin.X + Square * 8 + 10, 10));
                Test.Check("щелчок правее доски", Sq.None, last);
            }
        });
    }

    private static void Editing()
    {
        Test.Suite("Доска: режим редактирования", () =>
        {
            using var board = NewBoard(Position.StartFen);
            var clicks = new List<(int Square, MouseButtons Button)>();
            var drags = new List<(int From, int To)>();
            board.EditSquareClicked += (_, e) => clicks.Add((e.Square, e.Button));
            board.EditPieceDragged += (_, e) => drags.Add((e.From, e.To));

            // В обычном режиме событий редактора нет
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            Test.Check("в режиме игры щелчки редактора не приходят", 0, clicks.Count);

            board.Mode = BoardMode.Edit;
            UiHarness.Click(board, Center(Sq.Parse("d4"), false));
            Test.Check("левый щелчок пришёл", 1, clicks.Count);
            Test.Check("клетка щелчка", "d4", Sq.Name(clicks[0].Square));
            Test.Check("кнопка щелчка", MouseButtons.Left, clicks[0].Button);

            UiHarness.Click(board, Center(Sq.Parse("e7"), false), MouseButtons.Right);
            Test.Check("правый щелчок пришёл", 2, clicks.Count);
            Test.Check("кнопка правого щелчка", MouseButtons.Right, clicks[1].Button);
            Test.Check("клетка правого щелчка", "e7", Sq.Name(clicks[1].Square));

            // Перетаскивание фигуры
            clicks.Clear();
            UiHarness.Drag(board, Center(Sq.Parse("e2"), false), Center(Sq.Parse("e4"), false));
            Test.Check("перетаскивание пришло", 1, drags.Count);
            Test.Check("откуда", "e2", Sq.Name(drags[0].From));
            Test.Check("куда", "e4", Sq.Name(drags[0].To));
            Test.Check("перетаскивание не считается щелчком", 0, clicks.Count);

            // Сброс мимо доски удаляет фигуру
            drags.Clear();
            UiHarness.Drag(board, Center(Sq.Parse("d2"), false), new Point(4, 10));
            Test.Check("сброс мимо доски пришёл", 1, drags.Count);
            Test.Check("цель — не клетка", Sq.None, drags[0].To);

            // Пустую клетку перетащить нельзя — это обычный щелчок
            drags.Clear();
            clicks.Clear();
            UiHarness.Drag(board, Center(Sq.Parse("d4"), false), Center(Sq.Parse("d5"), false));
            Test.Check("пустая клетка не перетаскивается", 0, drags.Count);
            Test.Check("пустая клетка даёт щелчок", 1, clicks.Count);
            Test.Check("щелчок по исходной клетке", "d4", Sq.Name(clicks[0].Square));

            // В режиме редактирования ходы не делаются
            var moved = false;
            board.MoveMade += (_, _) => moved = true;
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            UiHarness.Click(board, Center(Sq.Parse("e4"), false));
            Test.False("ход в режиме редактирования не делается", moved);

            Test.Check("кисть по умолчанию пуста", true, board.EditBrush.IsEmpty);
            board.EditBrush = new Piece(PieceColor.Black, PieceType.Queen);
            Test.Check("кисть запоминается", "q", board.EditBrush.ToFenChar().ToString());
        });
    }

    private static void Playing()
    {
        Test.Suite("Доска: ходы мышью", () =>
        {
            using var board = NewBoard(Position.StartFen);
            var moves = new List<Chess.Move>();
            board.MoveMade += (_, e) => moves.Add(e.Move);

            // Щёлкнул фигуру — щёлкнул клетку
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            UiHarness.Click(board, Center(Sq.Parse("e4"), false));
            Test.Check("ход сделан щелчками", 1, moves.Count);
            Test.Check("это e2-e4", "e2e4", moves[0].ToUci());

            // Перетаскиванием
            moves.Clear();
            UiHarness.Drag(board, Center(Sq.Parse("g1"), false), Center(Sq.Parse("f3"), false));
            Test.Check("ход сделан перетаскиванием", 1, moves.Count);
            Test.Check("это g1-f3", "g1f3", moves[0].ToUci());

            // Чужая фигура не берётся
            moves.Clear();
            UiHarness.Click(board, Center(Sq.Parse("e7"), false));
            UiHarness.Click(board, Center(Sq.Parse("e5"), false));
            Test.Check("фигурой соперника не ходим", 0, moves.Count);

            // Нелегальный ход не проходит
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            UiHarness.Click(board, Center(Sq.Parse("e5"), false));
            Test.Check("нелегальный ход не проходит", 0, moves.Count);

            // Правая кнопка снимает выделение
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            UiHarness.Click(board, Center(Sq.Parse("a1"), false), MouseButtons.Right);
            UiHarness.Click(board, Center(Sq.Parse("e4"), false));
            Test.Check("после снятия выделения хода нет", 0, moves.Count);

            // Запрет взаимодействия
            board.InteractionEnabled = false;
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            UiHarness.Click(board, Center(Sq.Parse("e4"), false));
            Test.Check("при запрете ходов их нет", 0, moves.Count);
            board.InteractionEnabled = true;

            // Превращение пешки
            using var promo = NewBoard("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
            var promoted = new List<Chess.Move>();
            var asked = 0;
            promo.MoveMade += (_, e) => promoted.Add(e.Move);
            promo.PromotionNeeded += (_, e) => { asked++; e.Selected = PieceType.Rook; };

            UiHarness.Click(promo, Center(Sq.Parse("a7"), false));
            UiHarness.Click(promo, Center(Sq.Parse("a8"), false));
            Test.Check("спросили про превращение", 1, asked);
            Test.Check("ход с превращением сделан", 1, promoted.Count);
            Test.Check("выбрана ладья", "a7a8r", promoted[0].ToUci());
            Test.Check("цвет в запросе", PieceColor.White, new PromotionEventArgs(PieceColor.White).Color);

            // Отказ от превращения отменяет ход
            using var cancelled = NewBoard("8/P6k/8/8/8/8/8/4K3 w - - 0 1");
            var moveAfterCancel = false;
            cancelled.PromotionNeeded += (_, e) => e.Cancelled = true;
            cancelled.MoveMade += (_, _) => moveAfterCancel = true;
            UiHarness.Click(cancelled, Center(Sq.Parse("a7"), false));
            UiHarness.Click(cancelled, Center(Sq.Parse("a8"), false));
            Test.False("отказ отменяет ход", moveAfterCancel);

            // Смена позиции сбрасывает выделение
            moves.Clear();
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            board.Position = Position.FromFen(Position.StartFen);
            UiHarness.Click(board, Center(Sq.Parse("e4"), false));
            Test.Check("после смены позиции выделение снято", 0, moves.Count);

            // Смена режима тоже сбрасывает выделение
            UiHarness.Click(board, Center(Sq.Parse("e2"), false));
            board.Mode = BoardMode.Edit;
            board.Mode = BoardMode.Play;
            UiHarness.Click(board, Center(Sq.Parse("e4"), false));
            Test.Check("после смены режима выделение снято", 0, moves.Count);
        });
    }

    private static void Drawing()
    {
        Test.Suite("Доска: отрисовка", () =>
        {
            using var start = NewBoard(Position.StartFen);
            using var empty = NewBoard("8/8/8/8/8/8/8/8 w - - 0 1");

            using var startImage = UiHarness.Render(start);
            using var emptyImage = UiHarness.Render(empty);

            Test.Check("картинка по размеру контрола", ControlSize, startImage.Size);
            Test.True("фигуры нарисованы", UiHarness.Difference(startImage, emptyImage) > 5000);

            // Фигуры именно там, где стоят: на e1 есть фигура, на e4 — нет
            var e1 = new Rectangle(Center(Sq.Parse("e1"), false).X - 20, Center(Sq.Parse("e1"), false).Y - 20, 40, 40);
            var e4 = new Rectangle(Center(Sq.Parse("e4"), false).X - 20, Center(Sq.Parse("e4"), false).Y - 20, 40, 40);
            Test.True("на e1 нарисован король", UiHarness.Difference(startImage, emptyImage, e1) > 200);
            Test.Check("на e4 пусто", 0, UiHarness.Difference(startImage, emptyImage, e4));

            using var flipped = NewBoard(Position.StartFen, flipped: true);
            using var flippedImage = UiHarness.Render(flipped);
            Test.True("перевёрнутая доска выглядит иначе",
                UiHarness.Difference(startImage, flippedImage) > 5000);

            // Подсветка последнего хода
            using var highlighted = NewBoard(Position.StartFen);
            highlighted.LastMove = Chess.Move.FromUci("e2e4");
            using var highlightedImage = UiHarness.Render(highlighted);
            Test.True("последний ход подсвечен", UiHarness.Difference(startImage, highlightedImage) > 100);

            // Стрелки
            using var arrows = NewBoard(Position.StartFen);
            arrows.SetArrows(new[] { new BoardArrow(Sq.Parse("e2"), Sq.Parse("e4"), Color.LimeGreen) });
            using var arrowImage = UiHarness.Render(arrows);
            Test.True("стрелка нарисована", UiHarness.Difference(startImage, arrowImage) > 500);

            arrows.ClearArrows();
            using var withoutArrows = UiHarness.Render(arrows);
            Test.Check("после очистки стрелок картинка прежняя", 0,
                UiHarness.Difference(startImage, withoutArrows));

            // Координаты
            using var noCoordinates = NewBoard(Position.StartFen);
            noCoordinates.ShowCoordinates = false;
            using var noCoordinatesImage = UiHarness.Render(noCoordinates);
            Test.True("координаты можно убрать",
                UiHarness.Difference(startImage, noCoordinatesImage) > 50);

            // Рамка редактора
            using var editing = NewBoard(Position.StartFen);
            editing.Mode = BoardMode.Edit;
            using var editingImage = UiHarness.Render(editing);
            Test.True("в режиме редактора доска обведена рамкой",
                UiHarness.Difference(startImage, editingImage) > 100);

            // Шах подсвечивается: сравниваем именно клетку короля, ладья стоит в другом месте
            using var check = NewBoard("4k3/8/8/8/8/8/8/4R1K1 b - - 0 1");
            using var checkImage = UiHarness.Render(check);
            using var noCheck = NewBoard("4k3/8/8/8/8/8/8/5RK1 b - - 0 1");
            using var noCheckImage = UiHarness.Render(noCheck);
            var e8 = new Rectangle(Center(Sq.Parse("e8"), false).X - 25, Center(Sq.Parse("e8"), false).Y - 25, 50, 50);
            Test.True("клетка короля под шахом окрашена",
                UiHarness.Difference(checkImage, noCheckImage, e8) > 200);

            Test.NoThrow("крошечная доска рисуется без ошибок", () =>
            {
                using var tiny = new BoardControl { ClientSize = new Size(64, 64) };
                using var image = UiHarness.Render(tiny);
            });
        });
    }
}
