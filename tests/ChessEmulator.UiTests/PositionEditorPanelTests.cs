using ChessEmulator.Chess;
using ChessEmulator.TestKit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>
/// Панель редактора позиции: палитра, расстановка мышью, флаги позиции, проверка и применение.
/// Кнопки работы с буфером обмена намеренно не нажимаем — они трогают общий буфер системы.
/// </summary>
internal static class PositionEditorPanelTests
{
    public static void Run()
    {
        Placement();
        Flags();
        ApplyAndCancel();
        Palette();
    }

    private static PositionEditorPanel NewPanel(string? fen = null)
    {
        var panel = new PositionEditorPanel { ClientSize = new Size(360, 600) };
        panel.LoadFrom(Position.FromFen(fen ?? Position.StartFen));
        return panel;
    }

    private static string At(PositionEditorPanel panel, string square) =>
        panel.Builder[Sq.Parse(square)].ToFenChar().ToString();

    private static void Placement()
    {
        Test.Suite("Редактор: расстановка фигур", () =>
        {
            using var panel = NewPanel();
            var changes = 0;
            panel.PositionChanged += (_, _) => changes++;

            Test.Check("загружена начальная позиция", Position.StartFen, panel.Builder.ToFen());
            Test.Check("кисть по умолчанию — белая пешка", "P", panel.Brush.ToFenChar().ToString());

            panel.Builder.Clear();
            panel.SyncFromBuilder();

            panel.HandleSquareClick(Sq.Parse("e4"), MouseButtons.Left);
            Test.Check("щелчок ставит фигуру кисти", "P", At(panel, "e4"));
            Test.True("об изменении сообщили", changes > 0);

            panel.HandleSquareClick(Sq.Parse("e4"), MouseButtons.Right);
            Test.True("правая кнопка стирает", panel.Builder[Sq.Parse("e4")].IsEmpty);

            // Перетаскивание
            panel.Builder[Sq.Parse("a1")] = new Piece(PieceColor.White, PieceType.Rook);
            panel.HandlePieceDrag(Sq.Parse("a1"), Sq.Parse("h8"));
            Test.Check("фигура переехала", "R", At(panel, "h8"));
            Test.True("исходная клетка пуста", panel.Builder[Sq.Parse("a1")].IsEmpty);

            panel.HandlePieceDrag(Sq.Parse("h8"), Sq.None);
            Test.True("сброс мимо доски стирает фигуру", panel.Builder[Sq.Parse("h8")].IsEmpty);

            changes = 0;
            panel.HandleSquareClick(Sq.None, MouseButtons.Left);
            panel.HandlePieceDrag(Sq.None, Sq.Parse("e4"));
            Test.Check("клетка вне доски игнорируется", 0, changes);

            // FEN вводом
            Test.Check("корректный FEN принят", null, panel.SetFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1"));
            Test.Check("позиция применена", "4k3/8/8/8/8/8/8/4K3 w - - 0 1", panel.Builder.ToFen());
            Test.True("испорченный FEN отвергнут", panel.SetFen("не-фен") != null);
            Test.Check("позиция не изменилась после ошибки",
                "4k3/8/8/8/8/8/8/4K3 w - - 0 1", panel.Builder.ToFen());
            Test.True("пустая строка отвергнута", panel.SetFen("   ") != null);
            Test.True("лишние пробелы вокруг FEN не мешают",
                panel.SetFen("  4k3/8/8/8/8/8/8/4K3 b - - 0 1  ") == null);
            Test.Check("очередь хода из FEN", PieceColor.Black, panel.Builder.SideToMove);

            // Кнопки очистки и начальной позиции
            UiHarness.FindButton(panel, "Начальная позиция").PerformClick();
            Test.Check("кнопка возвращает начальную позицию", Position.StartFen, panel.Builder.ToFen());
            UiHarness.FindButton(panel, "Очистить доску").PerformClick();
            Test.Check("кнопка очищает доску", "8/8/8/8/8/8/8/8 w - - 0 1", panel.Builder.ToFen());

            Test.NoThrow("панель рисуется", () => UiHarness.Render(panel).Dispose());
        });
    }

    private static void Flags()
    {
        Test.Suite("Редактор: флаги позиции", () =>
        {
            using var panel = NewPanel("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

            var blackToMove = UiHarness.ByText<RadioButton>(panel, "Ход чёрных");
            var whiteToMove = UiHarness.ByText<RadioButton>(panel, "Ход белых");
            Test.True("отмечен ход белых", whiteToMove.Checked);

            blackToMove.Checked = true;
            Test.Check("переключили очередь хода", PieceColor.Black, panel.Builder.SideToMove);
            Test.Check("очередь хода видна в FEN", "b", panel.Builder.ToFen().Split(' ')[1]);
            whiteToMove.Checked = true;
            Test.Check("вернули ход белым", PieceColor.White, panel.Builder.SideToMove);

            var whiteShort = UiHarness.ByText<CheckBox>(panel, "0-0 белые");
            var blackLong = UiHarness.ByText<CheckBox>(panel, "0-0-0 чёрные");
            Test.True("права из позиции отмечены", whiteShort.Checked && blackLong.Checked);

            whiteShort.Checked = false;
            Test.Check("право снято", "Qkq", panel.Builder.ToFen().Split(' ')[2]);
            whiteShort.Checked = true;
            Test.Check("право возвращено", "KQkq", panel.Builder.ToFen().Split(' ')[2]);

            // Невозможное право снимается само
            panel.HandleSquareClick(Sq.Parse("h1"), MouseButtons.Right);
            Test.Check("без ладьи право исчезает", "Qkq", panel.Builder.ToFen().Split(' ')[2]);
            Test.False("флажок снялся вслед за позицией", whiteShort.Checked);

            // Поле взятия на проходе
            using var ep = NewPanel("4k3/8/8/2ppP3/8/8/8/4K3 w - - 0 1");
            var combo = UiHarness.Find<ComboBox>(ep);
            Test.Check("в списке поля и «нет»", 3, combo.Items.Count);
            Test.Check("по умолчанию поле не выбрано", "нет", combo.SelectedItem as string ?? "");
            Test.True("список доступен", combo.Enabled);

            combo.SelectedItem = "d6";
            Test.Check("поле взятия на проходе задано", "d6", Sq.Name(ep.Builder.EnPassant));
            Test.Check("поле попало в FEN", "d6", ep.Builder.ToFen().Split(' ')[3]);
            combo.SelectedIndex = 0;
            Test.Check("поле снято", Sq.None, ep.Builder.EnPassant);

            using var noEp = NewPanel();
            Test.False("без подходящих пешек список выключен", UiHarness.Find<ComboBox>(noEp).Enabled);
        });
    }

    private static void ApplyAndCancel()
    {
        Test.Suite("Редактор: проверка и применение", () =>
        {
            using var panel = NewPanel();
            string? applied = null;
            var cancelled = 0;
            panel.Applied += (_, e) => applied = e.Fen;
            panel.Cancelled += (_, _) => cancelled++;

            var apply = UiHarness.FindButton(panel, "Применить");
            var cancel = UiHarness.FindButton(panel, "Отмена");
            Test.True("корректную позицию можно применить", apply.Enabled);

            // Убираем белого короля — применять нельзя
            panel.HandleSquareClick(Sq.Parse("e1"), MouseButtons.Right);
            Test.False("без короля применить нельзя", apply.Enabled);
            apply.PerformClick();
            Test.Check("нажатие не проходит", null, applied);

            var status = UiHarness.All(panel).OfType<Label>()
                .FirstOrDefault(l => l.Text.Contains("корол", StringComparison.OrdinalIgnoreCase));
            Test.True("ошибка показана на панели", status != null);

            // Возвращаем короля
            panel.SetFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
            Test.True("после исправления применить можно", apply.Enabled);

            apply.PerformClick();
            Test.Check("применение отдаёт FEN", "4k3/8/8/8/8/8/8/4K3 w - - 0 1", applied);

            cancel.PerformClick();
            Test.Check("отмена сообщает владельцу", 1, cancelled);

            // Применение отдаёт уже исправленную позицию
            using var normalize = NewPanel("4k3/8/8/8/8/8/8/4K2R w KQkq - 0 1");
            string? normalized = null;
            normalize.Applied += (_, e) => normalized = e.Fen;
            UiHarness.FindButton(normalize, "Применить").PerformClick();
            Test.Check("невозможные права сняты при применении",
                "4k3/8/8/8/8/8/8/4K2R w K - 0 1", normalized);

            // Позиция с шахом стороне не на ходу применяться не должна
            using var illegal = NewPanel("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1");
            Test.False("шах стороне не на ходу блокирует применение",
                UiHarness.FindButton(illegal, "Применить").Enabled);

            // А мат — вполне допустимая позиция для разбора
            using var mate = NewPanel("6kR/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1");
            Test.True("мат можно применить", UiHarness.FindButton(mate, "Применить").Enabled);
        });
    }

    private static void Palette()
    {
        Test.Suite("Редактор: палитра фигур", () =>
        {
            using var panel = NewPanel();
            var palette = UiHarness.Find<PiecePalette>(panel);
            palette.ClientSize = new Size(330, 110);

            Test.Check("по умолчанию выбрана белая пешка", "P", palette.Selected.ToFenChar().ToString());

            var changes = 0;
            palette.SelectionChanged += (_, _) => changes++;
            palette.Selected = new Piece(PieceColor.Black, PieceType.Knight);
            Test.Check("выбор передан панели", "n", panel.Brush.ToFenChar().ToString());
            Test.Check("о смене выбора сообщили", 1, changes);

            panel.Builder.Clear();
            panel.SyncFromBuilder();
            panel.HandleSquareClick(Sq.Parse("g8"), MouseButtons.Left);
            Test.Check("кисть ставит выбранную фигуру", "n", At(panel, "g8"));

            // Ластик — пустая фигура
            palette.Selected = Piece.Empty;
            Test.True("ластик выбран", panel.Brush.IsEmpty);
            panel.HandleSquareClick(Sq.Parse("g8"), MouseButtons.Left);
            Test.True("ластик стирает левым щелчком", panel.Builder[Sq.Parse("g8")].IsEmpty);

            // Щелчки по палитре выбирают фигуру
            palette.Selected = new Piece(PieceColor.White, PieceType.Pawn);
            changes = 0;
            var cell = palette.ClientSize.Width / 7;
            UiHarness.Click(palette, new Point(cell / 2, cell / 2));
            Test.True("щелчок по палитре меняет выбор", changes > 0);
            Test.Check("выбран белый король", "K", palette.Selected.ToFenChar().ToString());

            UiHarness.Click(palette, new Point(cell / 2, cell + cell / 2));
            Test.Check("во втором ряду чёрные фигуры", "k", palette.Selected.ToFenChar().ToString());

            Test.NoThrow("палитра рисуется", () => UiHarness.Render(palette).Dispose());
        });
    }
}
