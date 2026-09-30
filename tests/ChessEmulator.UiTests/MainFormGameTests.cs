using ChessEmulator.App;
using ChessEmulator.Chess;
using ChessEmulator.UI;
using Xunit;
using static ChessEmulator.UiTests.MainFormHarness;

namespace ChessEmulator.UiTests;

/// <summary>
/// Главное окно: работа с партией — горячие клавиши, кнопки под записью ходов, комментарий,
/// редактор позиции. Окно не показывается и движок не запущен.
/// </summary>
public class MainFormGameTests
{
    private const string HintsOffText = "Подсказки движка выключены (H включает).";

    [WinFormsFact(DisplayName = "Главное окно: стрелки, Home и End листают партию")]
    public void КлавишиЛистаютПартию()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = Play(form, "e2e4", "e7e5", "g1f3");
            var fenBox = Field<TextBox>(form, "_fenBox");

            Assert.True(Key(form, Keys.Left), "стрелка обработана окном");
            Assert.Equal("e5", game.Current.San);  // стрелка влево — на ход назад
            Assert.Equal(game.CurrentPosition.ToFen(), fenBox.Text);  // и окно показывает эту позицию

            Key(form, Keys.Home);
            Assert.True(game.Current.IsRoot, "Home — в начало партии");
            Assert.Equal(Position.StartFen, fenBox.Text);

            Key(form, Keys.Right);
            Assert.Equal("e4", game.Current.San);  // стрелка вправо — на ход вперёд

            Key(form, Keys.End);
            Assert.Equal("Nf3", game.Current.San);  // End — в конец партии

            // На краях партии клавиши остаются на месте.
            Key(form, Keys.Right);
            Assert.Equal("Nf3", game.Current.San);
            Key(form, Keys.Home);
            Key(form, Keys.Left);
            Assert.True(game.Current.IsRoot, "левее начала идти некуда");
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: Delete удаляет ход вместе с продолжением")]
    public void DeleteУдаляетХод()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = Play(form, "e2e4", "e7e5", "g1f3");

            Key(form, Keys.Left);  // стоим на e5
            Assert.True(Key(form, Keys.Delete), "Delete обработан окном");
            Assert.Equal("e4", game.Current.San);  // текущим стал предыдущий ход
            Assert.Single(game.MainLine());  // e5 ушёл вместе с Nf3

            Key(form, Keys.Home);
            Key(form, Keys.Delete);
            Assert.Single(game.MainLine());  // в начале партии удалять нечего
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: F переворачивает доску и запоминает это")]
    public void FПереворачиваетДоску()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var board = Field<BoardControl>(form, "_board");
            Assert.False(board.Flipped, "сначала доска стоит белыми вниз");

            Assert.True(Key(form, Keys.F), "F обработана окном");
            Assert.True(board.Flipped, "доска перевёрнута");
            Assert.True(AppSettings.Load().BoardFlipped, "и это записано в настройки");

            Key(form, Keys.F);
            Assert.False(board.Flipped, "вторая F возвращает доску");
            Assert.False(AppSettings.Load().BoardFlipped, "настройка вернулась следом");
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: пробел переключает анализ, H — подсказки движка")]
    public void ПереключателиАнализаИПодсказок()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var advice = Field<RichTextBox>(form, "_adviceBox");
            var status = Field<ToolStripStatusLabel>(form, "_searchStatus");

            Assert.True(Key(form, Keys.Space), "пробел обработан окном");
            Assert.False(AppSettings.Load().AutoAnalyze, "пробел выключил постоянный анализ");
            Key(form, Keys.Space);
            Assert.True(AppSettings.Load().AutoAnalyze, "и включил обратно");

            Assert.True(Key(form, Keys.H), "H обработана окном");
            Assert.False(AppSettings.Load().ShowEngineHints, "H выключила подсказки");
            Assert.Equal(HintsOffText, advice.Text);  // панель советов объясняет, почему она пуста

            // Без подсказок анализ стоит: пробел не молчит, а объясняет почему.
            Key(form, Keys.Space);
            Assert.True(AppSettings.Load().AutoAnalyze, "настройка анализа не тронута");
            Assert.Equal(HintsOffText, status.Text);

            Key(form, Keys.H);
            Assert.True(AppSettings.Load().ShowEngineHints, "вторая H вернула подсказки");
            Assert.NotEqual(HintsOffText, advice.Text);
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: в редакторе позиции действуют только Esc и F")]
    public void КлавишиВРедакторе()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = Play(form, "e2e4", "e7e5");
            var board = Field<BoardControl>(form, "_board");

            EnterEditor(form);
            Assert.Equal(BoardMode.Edit, board.Mode);

            // Партию из-под редактора не листают и не правят.
            Key(form, Keys.Left);
            Assert.Equal("e5", game.Current.San);
            Key(form, Keys.Delete);
            Assert.Equal(2, game.MainLine().Count);

            Assert.True(Key(form, Keys.F), "F работает и в редакторе");
            Assert.True(board.Flipped, "доска перевёрнута");

            Assert.True(Key(form, Keys.Escape), "Esc обработан окном");
            Assert.Equal(BoardMode.Play, board.Mode);  // Esc выходит из редактора
            Assert.Equal("e5", game.Current.San);  // партия осталась прежней
            Assert.Equal(game.CurrentPosition.ToFen(), board.Position.ToFen());  // и снова видна на доске

            Key(form, Keys.Left);
            Assert.Equal("e4", game.Current.San);  // после выхода клавиши снова листают партию
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: позиция из редактора начинает новую партию")]
    public void ПозицияИзРедактора()
    {
        WithSettingsFile(() =>
        {
            const string fen = "4k3/8/8/8/8/8/8/4K2R w K - 0 1";

            using var form = new MainForm();
            var board = Field<BoardControl>(form, "_board");
            var editor = Field<PositionEditorPanel>(form, "_editorPanel");
            EnterEditor(form);

            Assert.Null(editor.SetFen(fen));
            Assert.Equal(fen, board.Position.ToFen());  // расстановка из редактора видна на доске
            Assert.Equal(fen, Field<TextBox>(form, "_fenBox").Text);  // и в поле FEN

            UiHarness.Press(UiHarness.FindButton(editor, "Применить"));

            Assert.Equal(BoardMode.Play, board.Mode);  // редактор закрыт
            var game = GameOf(form);
            Assert.Equal(fen, game.StartFen);  // партия начата с расставленной позиции
            Assert.True(game.Current.IsRoot, "ходов в новой партии ещё нет");
            Assert.Equal(fen, board.Position.ToFen());
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: кнопки под записью партии")]
    public void КнопкиНавигации()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = Play(form, "e2e4", "e7e5");

            Press(form, "|<");
            Assert.True(game.Current.IsRoot, "«|<» — в начало");
            Press(form, ">");
            Assert.Equal("e4", game.Current.San);  // «>» — ход вперёд
            Press(form, ">|");
            Assert.Equal("e5", game.Current.San);  // «>|» — в конец
            Press(form, "<");
            Assert.Equal("e4", game.Current.San);  // «<» — ход назад

            // Ход не в конце партии создаёт вариант, кнопка делает его основной линией.
            Play(form, "c7c5");
            Assert.Equal("e5", game.MainLine()[1].San);
            Press(form, "Вариант→");
            Assert.Equal("c5", game.MainLine()[1].San);

            Press(form, "Удалить");
            Assert.Equal("e4", game.Current.San);  // удалён ход, на котором стояли
            Assert.Equal("e5", game.MainLine()[1].San);  // основной линией снова стал прежний ответ

            Press(form, "Флип");
            Assert.True(Field<BoardControl>(form, "_board").Flipped, "«Флип» переворачивает доску");
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: комментарий записывается в текущий ход")]
    public void Комментарий()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = GameOf(form);
            var comment = Field<TextBox>(form, "_commentBox");

            comment.Text = "до первого хода";
            Assert.Null(game.Root.Comment);  // у начальной позиции комментария нет

            Play(form, "e2e4");
            comment.Text = "  сильный ход  ";
            Assert.Equal("сильный ход", game.Current.Comment);  // пробелы по краям срезаны

            comment.Text = "   ";
            Assert.Null(game.Current.Comment);  // одни пробелы — комментария нет

            // Переход по партии подставляет в поле комментарий другого хода; записаться
            // в чужой ход он при этом не должен.
            comment.Text = "первый";
            Play(form, "e7e5");
            Assert.Equal(string.Empty, comment.Text);
            Assert.Null(game.Current.Comment);

            Key(form, Keys.Left);
            Assert.Equal("первый", comment.Text);
            Assert.Equal("первый", game.Current.Comment);
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: имя файла партии собирается из имён игроков")]
    public void ИмяФайла()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = GameOf(form);

            game.Headers["White"] = "Иванов";
            game.Headers["Black"] = "Петров";
            Assert.Matches(@"^Иванов-Петров-\d{8}\.pgn$", (string)Call(form, "BuildFileName")!);

            // Имена по умолчанию («?») и прочие запретные знаки в имя файла не попадают.
            game.Headers["White"] = "?";
            game.Headers["Black"] = "A/B:C";
            var name = (string)Call(form, "BuildFileName")!;
            Assert.StartsWith("_-A_B_C-", name);
            Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        });
    }
}
