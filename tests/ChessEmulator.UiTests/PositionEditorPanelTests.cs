using ChessEmulator.Chess;
using Xunit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>
/// Панель редактора позиции: палитра, расстановка мышью, флаги позиции, проверка и применение.
/// Кнопки работы с буфером обмена намеренно не нажимаем — они трогают общий буфер системы.
/// </summary>
public class PositionEditorPanelTests
{
    private static PositionEditorPanel NewPanel(string? fen = null)
    {
        var panel = new PositionEditorPanel { ClientSize = new Size(360, 600) };
        panel.LoadFrom(Position.FromFen(fen ?? Position.StartFen));
        return panel;
    }

    private static string At(PositionEditorPanel panel, string square) =>
        panel.Builder[Sq.Parse(square)].ToFenChar().ToString();

    [WinFormsFact(DisplayName = "Редактор: расстановка фигур")]
    public void Placement()
    {
        using var panel = NewPanel();
        var changes = 0;
        panel.PositionChanged += (_, _) => changes++;

        Assert.Equal(Position.StartFen, panel.Builder.ToFen());  // загружена начальная позиция
        Assert.Equal("P", panel.Brush.ToFenChar().ToString());  // кисть по умолчанию — белая пешка

        panel.Builder.Clear();
        panel.SyncFromBuilder();

        panel.HandleSquareClick(Sq.Parse("e4"), MouseButtons.Left);
        Assert.Equal("P", At(panel, "e4"));  // щелчок ставит фигуру кисти
        Assert.True(changes > 0, "об изменении сообщили");

        panel.HandleSquareClick(Sq.Parse("e4"), MouseButtons.Right);
        Assert.True(panel.Builder[Sq.Parse("e4")].IsEmpty, "правая кнопка стирает");

        // Перетаскивание
        panel.Builder[Sq.Parse("a1")] = new Piece(PieceColor.White, PieceType.Rook);
        panel.HandlePieceDrag(Sq.Parse("a1"), Sq.Parse("h8"));
        Assert.Equal("R", At(panel, "h8"));  // фигура переехала
        Assert.True(panel.Builder[Sq.Parse("a1")].IsEmpty, "исходная клетка пуста");

        panel.HandlePieceDrag(Sq.Parse("h8"), Sq.None);
        Assert.True(panel.Builder[Sq.Parse("h8")].IsEmpty, "сброс мимо доски стирает фигуру");

        changes = 0;
        panel.HandleSquareClick(Sq.None, MouseButtons.Left);
        panel.HandlePieceDrag(Sq.None, Sq.Parse("e4"));
        Assert.Equal(0, changes);  // клетка вне доски игнорируется

        // FEN вводом
        Assert.Null(panel.SetFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1"));  // корректный FEN принят
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", panel.Builder.ToFen());  // позиция применена
        Assert.True(panel.SetFen("не-фен") != null, "испорченный FEN отвергнут");
        // позиция не изменилась после ошибки
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", panel.Builder.ToFen());
        Assert.True(panel.SetFen("   ") != null, "пустая строка отвергнута");
        Assert.True(panel.SetFen("  4k3/8/8/8/8/8/8/4K3 b - - 0 1  ") == null, "лишние пробелы вокруг FEN не мешают");
        Assert.Equal(PieceColor.Black, panel.Builder.SideToMove);  // очередь хода из FEN

        // Кнопки очистки и начальной позиции
        UiHarness.FindButton(panel, "Начальная позиция").PerformClick();
        Assert.Equal(Position.StartFen, panel.Builder.ToFen());  // кнопка возвращает начальную позицию
        UiHarness.FindButton(panel, "Очистить доску").PerformClick();
        Assert.Equal("8/8/8/8/8/8/8/8 w - - 0 1", panel.Builder.ToFen());  // кнопка очищает доску

        Assert.Null(Record.Exception(() => UiHarness.Render(panel).Dispose()));  // панель рисуется
    }

    [WinFormsFact(DisplayName = "Редактор: флаги позиции")]
    public void Flags()
    {
        using var panel = NewPanel("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        var blackToMove = UiHarness.ByText<RadioButton>(panel, "Ход чёрных");
        var whiteToMove = UiHarness.ByText<RadioButton>(panel, "Ход белых");
        Assert.True(whiteToMove.Checked, "отмечен ход белых");

        blackToMove.Checked = true;
        Assert.Equal(PieceColor.Black, panel.Builder.SideToMove);  // переключили очередь хода
        Assert.Equal("b", panel.Builder.ToFen().Split(' ')[1]);  // очередь хода видна в FEN
        whiteToMove.Checked = true;
        Assert.Equal(PieceColor.White, panel.Builder.SideToMove);  // вернули ход белым

        var whiteShort = UiHarness.ByText<CheckBox>(panel, "0-0 белые");
        var blackLong = UiHarness.ByText<CheckBox>(panel, "0-0-0 чёрные");
        Assert.True(whiteShort.Checked && blackLong.Checked, "права из позиции отмечены");

        whiteShort.Checked = false;
        Assert.Equal("Qkq", panel.Builder.ToFen().Split(' ')[2]);  // право снято
        whiteShort.Checked = true;
        Assert.Equal("KQkq", panel.Builder.ToFen().Split(' ')[2]);  // право возвращено

        // Невозможное право снимается само
        panel.HandleSquareClick(Sq.Parse("h1"), MouseButtons.Right);
        Assert.Equal("Qkq", panel.Builder.ToFen().Split(' ')[2]);  // без ладьи право исчезает
        Assert.False(whiteShort.Checked, "флажок снялся вслед за позицией");

        // Поле взятия на проходе
        using var ep = NewPanel("4k3/8/8/2ppP3/8/8/8/4K3 w - - 0 1");
        var combo = UiHarness.Find<ComboBox>(ep);
        Assert.Equal(3, combo.Items.Count);  // в списке поля и «нет»
        Assert.Equal("нет", combo.SelectedItem as string ?? "");  // по умолчанию поле не выбрано
        Assert.True(combo.Enabled, "список доступен");

        combo.SelectedItem = "d6";
        Assert.Equal("d6", Sq.Name(ep.Builder.EnPassant));  // поле взятия на проходе задано
        Assert.Equal("d6", ep.Builder.ToFen().Split(' ')[3]);  // поле попало в FEN
        combo.SelectedIndex = 0;
        Assert.Equal(Sq.None, ep.Builder.EnPassant);  // поле снято

        using var noEp = NewPanel();
        Assert.False(UiHarness.Find<ComboBox>(noEp).Enabled, "без подходящих пешек список выключен");
    }

    [WinFormsFact(DisplayName = "Редактор: проверка и применение")]
    public void ApplyAndCancel()
    {
        using var panel = NewPanel();
        string? applied = null;
        var cancelled = 0;
        panel.Applied += (_, e) => applied = e.Fen;
        panel.Cancelled += (_, _) => cancelled++;

        var apply = UiHarness.FindButton(panel, "Применить");
        var cancel = UiHarness.FindButton(panel, "Отмена");
        Assert.True(apply.Enabled, "корректную позицию можно применить");

        // Убираем белого короля — применять нельзя
        panel.HandleSquareClick(Sq.Parse("e1"), MouseButtons.Right);
        Assert.False(apply.Enabled, "без короля применить нельзя");
        apply.PerformClick();
        Assert.Null(applied);  // нажатие не проходит

        var status = UiHarness.All(panel).OfType<Label>()
            .FirstOrDefault(l => l.Text.Contains("корол", StringComparison.OrdinalIgnoreCase));
        Assert.True(status != null, "ошибка показана на панели");

        // Возвращаем короля
        panel.SetFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1");
        Assert.True(apply.Enabled, "после исправления применить можно");

        apply.PerformClick();
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", applied);  // применение отдаёт FEN

        cancel.PerformClick();
        Assert.Equal(1, cancelled);  // отмена сообщает владельцу

        // Применение отдаёт уже исправленную позицию
        using var normalize = NewPanel("4k3/8/8/8/8/8/8/4K2R w KQkq - 0 1");
        string? normalized = null;
        normalize.Applied += (_, e) => normalized = e.Fen;
        UiHarness.FindButton(normalize, "Применить").PerformClick();
        // невозможные права сняты при применении
        Assert.Equal("4k3/8/8/8/8/8/8/4K2R w K - 0 1", normalized);

        // Позиция с шахом стороне не на ходу применяться не должна
        using var illegal = NewPanel("4k3/8/8/8/8/8/8/4R1K1 w - - 0 1");
        Assert.False(UiHarness.FindButton(illegal, "Применить").Enabled, "шах стороне не на ходу блокирует применение");

        // А мат — вполне допустимая позиция для разбора
        using var mate = NewPanel("6kR/5ppp/8/8/8/8/5PPP/6K1 b - - 0 1");
        Assert.True(UiHarness.FindButton(mate, "Применить").Enabled, "мат можно применить");
    }

    [WinFormsFact(DisplayName = "Редактор: палитра фигур")]
    public void Palette()
    {
        using var panel = NewPanel();
        var palette = UiHarness.Find<PiecePalette>(panel);
        palette.ClientSize = new Size(330, 110);

        Assert.Equal("P", palette.Selected.ToFenChar().ToString());  // по умолчанию выбрана белая пешка

        var changes = 0;
        palette.SelectionChanged += (_, _) => changes++;
        palette.Selected = new Piece(PieceColor.Black, PieceType.Knight);
        Assert.Equal("n", panel.Brush.ToFenChar().ToString());  // выбор передан панели
        Assert.Equal(1, changes);  // о смене выбора сообщили

        panel.Builder.Clear();
        panel.SyncFromBuilder();
        panel.HandleSquareClick(Sq.Parse("g8"), MouseButtons.Left);
        Assert.Equal("n", At(panel, "g8"));  // кисть ставит выбранную фигуру

        // Ластик — пустая фигура
        palette.Selected = Piece.Empty;
        Assert.True(panel.Brush.IsEmpty, "ластик выбран");
        panel.HandleSquareClick(Sq.Parse("g8"), MouseButtons.Left);
        Assert.True(panel.Builder[Sq.Parse("g8")].IsEmpty, "ластик стирает левым щелчком");

        // Щелчки по палитре выбирают фигуру
        palette.Selected = new Piece(PieceColor.White, PieceType.Pawn);
        changes = 0;
        var cell = palette.ClientSize.Width / 7;
        UiHarness.Click(palette, new Point(cell / 2, cell / 2));
        Assert.True(changes > 0, "щелчок по палитре меняет выбор");
        Assert.Equal("K", palette.Selected.ToFenChar().ToString());  // выбран белый король

        UiHarness.Click(palette, new Point(cell / 2, cell + cell / 2));
        Assert.Equal("k", palette.Selected.ToFenChar().ToString());  // во втором ряду чёрные фигуры

        Assert.Null(Record.Exception(() => UiHarness.Render(palette).Dispose()));  // палитра рисуется
    }
}
