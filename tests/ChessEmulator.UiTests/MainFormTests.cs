using ChessEmulator.App;
using ChessEmulator.Engine;
using ChessEmulator.UI;
using Xunit;
using static ChessEmulator.UiTests.MainFormHarness;

namespace ChessEmulator.UiTests;

/// <summary>
/// Главное окно — без показа. Окно не открывается, поэтому обработчик Shown не срабатывает
/// и движок не запускается: проверяем только то, что собирается в конструкторе.
/// </summary>
public class MainFormTests
{
    private static ComboBox DifficultyBox(Control root) =>
        UiHarness.All(root).OfType<ComboBox>().Single(c => c.Name == "difficultyBox");

    [WinFormsFact(DisplayName = "Главное окно: список сложности повторяет таблицу уровней")]
    public void СписокСложностиПовторяетТаблицу()
    {
        WithSettingsFile(() =>
        {
            new AppSettings { Difficulty = DifficultyLevel.Club }.Save();

            using var form = new MainForm();
            var box = DifficultyBox(form);

            Assert.Equal(Difficulty.All.Count, box.Items.Count);
            Assert.Equal(Difficulty.All.Select(p => p.Title), box.Items.Cast<object>().Select(i => (string)i));
            Assert.Equal(Difficulty.IndexOf(DifficultyLevel.Club), box.SelectedIndex);
            Assert.Equal(ComboBoxStyle.DropDownList, box.DropDownStyle);
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: выбор сложности сохраняется")]
    public void ВыборСложностиСохраняется()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var box = DifficultyBox(form);

            UiHarness.Select(box, Difficulty.IndexOf(DifficultyLevel.Beginner));

            Assert.Equal(DifficultyLevel.Beginner, AppSettings.Load().Difficulty);

            // Повторный выбор того же уровня обработчик пропускает, файл не портится.
            UiHarness.Select(box, Difficulty.IndexOf(DifficultyLevel.Beginner));
            Assert.Equal(DifficultyLevel.Beginner, AppSettings.Load().Difficulty);
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: список сложности гаснет в редакторе позиции")]
    public void СписокГаснетВРедакторе()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var box = DifficultyBox(form);
            Assert.True(box.Enabled, "вне редактора уровень меняется");

            // Пока фигуры расставляют, соперника нет — менять его силу не по чему.
            typeof(MainForm)
                .GetMethod("SetPlayControlsEnabled",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(form, [false]);

            Assert.False(box.Enabled, "в редакторе уровень заперт");
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: ход человека не встаёт, пока движок ищет свой")]
    public void ХодЧеловекаПокаДвижокДумает()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var game = (Chess.Game)typeof(MainForm).GetField("_game", flags)!.GetValue(form)!;
            var applyUserMove = typeof(MainForm).GetMethod("ApplyUserMove", flags)!;

            var e4 = game.CurrentPosition.LegalMoves.First(m => m.ToUci() == "e2e4");
            var d4 = game.CurrentPosition.LegalMoves.First(m => m.ToUci() == "d2d4");

            // Двойной щелчок по строке анализа ведёт сюда в обход запертой доски: ход встал бы
            // в позицию, для которой движок уже ищет свой.
            typeof(MainForm).GetField("_engineBusyWithMove", flags)!.SetValue(form, true);
            applyUserMove.Invoke(form, [e4]);
            Assert.True(game.Current.IsRoot, "пока движок думает, ход не принят");

            typeof(MainForm).GetField("_engineBusyWithMove", flags)!.SetValue(form, false);
            applyUserMove.Invoke(form, [d4]);
            Assert.Equal("d2d4", game.Current.Move.ToUci());  // свободный движок ход пропускает
        });
    }
}
