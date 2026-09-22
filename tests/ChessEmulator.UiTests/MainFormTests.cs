using ChessEmulator.App;
using ChessEmulator.Engine;
using ChessEmulator.UI;
using Xunit;

namespace ChessEmulator.UiTests;

/// <summary>
/// Главное окно — без показа. Окно не открывается, поэтому обработчик Shown не срабатывает
/// и движок не запускается: проверяем только то, что собирается в конструкторе.
/// </summary>
public class MainFormTests
{
    /// <summary>Выполняет действие, направив файл настроек во временную папку.</summary>
    private static void WithSettingsFile(Action body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ChessEmulatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, Path.Combine(dir, "settings.json"));
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

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
                .Invoke(form, new object[] { false });

            Assert.False(box.Enabled, "в редакторе уровень заперт");
        });
    }
}
