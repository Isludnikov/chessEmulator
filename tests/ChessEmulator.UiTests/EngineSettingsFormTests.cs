using ChessEmulator.App;
using ChessEmulator.UI;
using Xunit;
using static ChessEmulator.UiTests.MainFormHarness;

namespace ChessEmulator.UiTests;

/// <summary>Диалог настроек движка — без показа окна.</summary>
public class EngineSettingsFormTests
{
    /// <summary>Поле ввода числа, стоящее следом за подписью.</summary>
    private static NumericUpDown Numeric(Control root, string label)
    {
        var all = UiHarness.All(root).ToList();
        var index = all.FindIndex(c => c is Label && c.Text == label);
        return index < 0 ? throw new InvalidOperationException($"Подпись «{label}» не найдена.") : all.Skip(index + 1).OfType<NumericUpDown>().First();
    }

    [WinFormsFact(DisplayName = "Настройки движка: значения вне диапазона поджимаются")]
    public void ЗначенияПоджимаются()
    {
        // Файл настроек правят руками, и значение вне диапазона не должно ронять диалог
        // исключением NumericUpDown.
        var settings = new AppSettings
        {
            Threads = 9999,
            HashMb = 1,
            MultiPv = 42,
            AnalysisDepthLimit = -5,
            EngineMoveTimeMs = 0,
            GameAnalysisMoveTimeMs = 999999,
            SkillLevel = 99,
            EloRating = 10
        };

        using var form = new EngineSettingsForm(settings);

        var threads = Numeric(form, "Потоков (Threads):");
        Assert.InRange(threads.Value, threads.Minimum, threads.Maximum);  // потоки в пределах поля
        var multiPv = Numeric(form, "Линий анализа (MultiPV):");
        Assert.InRange(multiPv.Value, multiPv.Minimum, multiPv.Maximum);  // линии анализа в пределах поля
        var depth = Numeric(form, "Предел глубины (0 — нет):");
        Assert.InRange(depth.Value, depth.Minimum, depth.Maximum);  // предел глубины в пределах поля
        var elo = Numeric(form, "Рейтинг Эло (UCI_Elo):");
        Assert.InRange(elo.Value, elo.Minimum, elo.Maximum);  // рейтинг в пределах поля
    }

    [WinFormsFact(DisplayName = "Настройки движка: поле рейтинга следует за флажком")]
    public void РейтингСледуетЗаФлажком()
    {
        var settings = new AppSettings { LimitStrength = false };
        using var form = new EngineSettingsForm(settings);

        var limit = UiHarness.ByText<CheckBox>(form, "Ограничить силу движка рейтингом");
        var elo = Numeric(form, "Рейтинг Эло (UCI_Elo):");

        Assert.False(elo.Enabled, "без ограничения силы рейтинг не вводится");
        limit.Checked = true;
        Assert.True(elo.Enabled, "с ограничением силы рейтинг вводится");
        limit.Checked = false;
        Assert.False(elo.Enabled, "флажок сняли — поле снова заперто");
    }

    [WinFormsFact(DisplayName = "Настройки движка: поля силы помечены как относящиеся к «Своей»")]
    public void ПоляСилыПодписаны()
    {
        using var form = new EngineSettingsForm(new AppSettings());

        // Сами по себе Skill Level и рейтинг ничего не ослабляют: они работают только при
        // сложности «Своя», и без подписи это неочевидно.
        Assert.NotNull(UiHarness.ByText<Label>(form,
            "Уровень игры и рейтинг действуют, когда выбрана сложность «Своя»."));

        // Подпись не должна встать между другой подписью и её полем: поиск идёт по порядку обхода.
        Assert.Equal(20, Numeric(form, "Уровень игры (Skill Level):").Maximum);
        Assert.Equal(3200, Numeric(form, "Рейтинг Эло (UCI_Elo):").Maximum);
        Assert.Equal(10, Numeric(form, "Линий анализа (MultiPV):").Maximum);
    }

    [WinFormsFact(DisplayName = "Настройки движка: ОК сохраняет, Отмена не трогает")]
    public void СохранениеИОтмена()
    {
        WithSettingsFile(() =>
        {
            var settings = new AppSettings { MultiPv = 2, SkillLevel = 10, EnginePath = null };

            using (var form = new EngineSettingsForm(settings))
            {
                Numeric(form, "Линий анализа (MultiPV):").Value = 5;
                Numeric(form, "Уровень игры (Skill Level):").Value = 7;
                UiHarness.Press(UiHarness.ByText<Button>(form, "ОК"));
            }

            Assert.Equal(5, settings.MultiPv);  // ОК переносит значения в настройки
            Assert.Equal(7, settings.SkillLevel);  // и уровень игры тоже
            Assert.True(File.Exists(AppSettings.SettingsPath), "ОК записывает файл настроек");

            using (var form = new EngineSettingsForm(settings))
            {
                Numeric(form, "Линий анализа (MultiPV):").Value = 1;
                UiHarness.Press(UiHarness.ByText<Button>(form, "Отмена"));
            }

            Assert.Equal(5, settings.MultiPv);  // Отмена оставляет прежнее значение
        });
    }

    [WinFormsFact(DisplayName = "Настройки движка: автопоиск не затирает путь, введённый вручную")]
    public void АвтопоискНеЗатираетПуть()
    {
        // Подставной движок в каталоге из PATH: поиск наверняка что-то находит и на машине без
        // Stockfish — иначе кнопка показала бы модальное сообщение «движок не найден».
        var dir = Path.Combine(Path.GetTempPath(), "ChessEmulatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "stockfish-test.exe");
        File.WriteAllText(exe, string.Empty);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", $"{dir}{Path.PathSeparator}{previousPath}");
            const string own = @"C:\свой\движок.exe";

            using var form = new EngineSettingsForm(new AppSettings { EnginePath = own });
            var path = UiHarness.Find<ComboBox>(form);
            var detect = UiHarness.ByText<Button>(form, "Найти автоматически");
            Assert.Equal(own, path.Text);  // путь из настроек важнее найденного

            UiHarness.Press(detect);
            Assert.Equal(own, path.Text);  // повторный поиск его не трогает
            Assert.Contains(exe, path.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);  // находки — в списке
            Assert.Contains(UiHarness.All(form).OfType<Label>(),
                label => label.Text == $"Найдено вариантов движка: {path.Items.Count}.");

            // Пустое поле поиск заполняет первым найденным.
            path.Text = string.Empty;
            UiHarness.Press(detect);
            Assert.Equal((string)path.Items[0]!, path.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
