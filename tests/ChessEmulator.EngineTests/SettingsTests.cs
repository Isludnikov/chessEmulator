using System.Text.Json;
using ChessEmulator.App;
using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>Настройки приложения и поиск исполняемого файла движка.</summary>
public class SettingsTests(ITestOutputHelper output)
{
    [Fact(DisplayName = "Настройки: значения и хранение")]
    public void Settings()
    {
        var defaults = new AppSettings();
        Assert.Null(defaults.EnginePath);  // движок не выбран
        Assert.Equal(3, defaults.MultiPv);  // вариантов анализа по умолчанию
        Assert.Equal(20, defaults.SkillLevel);  // уровень игры по умолчанию
        Assert.Equal(256, defaults.HashMb);  // размер хеша по умолчанию
        Assert.Equal(1600, defaults.EloRating);  // рейтинг по умолчанию
        Assert.False(defaults.LimitStrength, "сила не ограничена");
        Assert.Equal(1000, defaults.EngineMoveTimeMs);  // время на ход движка
        Assert.Equal(500, defaults.GameAnalysisMoveTimeMs);  // время на позицию при разборе партии
        Assert.Equal(0, defaults.AnalysisDepthLimit);  // глубина анализа не ограничена
        Assert.True(defaults.Threads >= 1, "потоков хотя бы один");
        Assert.True(defaults.Threads <= Environment.ProcessorCount, "потоков не больше числа ядер");
        Assert.True(defaults.ShowCoordinates, "координаты показываются");
        Assert.True(defaults.ShowLegalMoveHints, "подсказки ходов включены");
        Assert.True(defaults.ShowBestMoveArrow, "стрелка лучшего хода включена");
        Assert.True(defaults.AutoAnalyze, "анализ включается сам");
        Assert.True(defaults.ShowEngineHints, "подсказки движка включены");
        Assert.True(defaults.AnalysisRuns, "по умолчанию анализ идёт");
        Assert.False(defaults.BoardFlipped, "доска не перевёрнута");

        // Полный круг через JSON — именно так настройки ложатся на диск
        var saved = new AppSettings
        {
            EnginePath = @"C:\движки\stockfish.exe",
            Threads = 6,
            HashMb = 512,
            MultiPv = 5,
            SkillLevel = 12,
            LimitStrength = true,
            EloRating = 2200,
            AnalysisDepthLimit = 24,
            EngineMoveTimeMs = 2500,
            GameAnalysisMoveTimeMs = 750,
            BoardFlipped = true,
            ShowCoordinates = false,
            ShowLegalMoveHints = false,
            ShowBestMoveArrow = false,
            AutoAnalyze = false,
            ShowEngineHints = false,
            LastPgnDirectory = @"C:\партии"
        };

        var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
        var loaded = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(saved.EnginePath, loaded.EnginePath);  // путь к движку
        Assert.Equal(6, loaded.Threads);  // потоки
        Assert.Equal(512, loaded.HashMb);  // хеш
        Assert.Equal(5, loaded.MultiPv);  // варианты
        Assert.Equal(12, loaded.SkillLevel);  // уровень
        Assert.True(loaded.LimitStrength, "ограничение силы");
        Assert.Equal(2200, loaded.EloRating);  // рейтинг
        Assert.Equal(24, loaded.AnalysisDepthLimit);  // предел глубины
        Assert.Equal(2500, loaded.EngineMoveTimeMs);  // время на ход
        Assert.Equal(750, loaded.GameAnalysisMoveTimeMs);  // время на разбор
        Assert.True(loaded.BoardFlipped, "перевёрнутая доска");
        Assert.False(loaded.ShowCoordinates, "координаты выключены");
        Assert.False(loaded.ShowLegalMoveHints, "подсказки выключены");
        Assert.False(loaded.ShowBestMoveArrow, "стрелка выключена");
        Assert.False(loaded.AutoAnalyze, "автоанализ выключен");
        Assert.False(loaded.ShowEngineHints, "подсказки движка выключены");
        Assert.Equal(saved.LastPgnDirectory, loaded.LastPgnDirectory);  // папка партий

        Assert.False(json.Contains("SettingsPath"), "служебный путь не попадает в файл");
        Assert.False(json.Contains("AnalysisRuns"), "производный флаг не попадает в файл");
        Assert.True(AppSettings.SettingsPath.EndsWith(Path.Combine("ChessEmulator", "settings.json"),
                StringComparison.OrdinalIgnoreCase), "путь к файлу настроек оканчивается на settings.json");

        // Испорченный файл не должен ронять приложение: Load ловит ошибку разбора
        // мусор вместо JSON не разбирается
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppSettings>("{это не json"));
        Assert.Null(Record.Exception(() => AppSettings.Load()));  // чтение настроек не падает
        Assert.True(AppSettings.Load() != null, "чтение настроек возвращает объект");
    }

    [Fact(DisplayName = "Настройки: анализ идёт только при включённых подсказках")]
    public void АнализТребуетОбоихПереключателей()
    {
        // Показывать результат некуда — считать его незачем, иначе выключенные подсказки
        // молча жгли бы процессор.
        Assert.True(new AppSettings { AutoAnalyze = true, ShowEngineHints = true }.AnalysisRuns);
        Assert.False(new AppSettings { AutoAnalyze = true, ShowEngineHints = false }.AnalysisRuns);
        Assert.False(new AppSettings { AutoAnalyze = false, ShowEngineHints = true }.AnalysisRuns);
        Assert.False(new AppSettings { AutoAnalyze = false, ShowEngineHints = false }.AnalysisRuns);
    }

    /// <summary>Выполняет действие, направив настройки во временный файл.</summary>
    private static void WithSettingsFile(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ChessEmulatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, path);
        try
        {
            body(path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppSettings.PathOverrideVariable, null);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact(DisplayName = "Настройки: запись и чтение файла")]
    public void SaveAndLoadRoundTrip()
    {
        WithSettingsFile(path =>
        {
            Assert.Equal(path, AppSettings.SettingsPath);  // путь переопределяется переменной среды
            Assert.Equal(3, AppSettings.Load().MultiPv);  // файла нет — значения по умолчанию

            var saved = new AppSettings { EnginePath = @"C:\движки\stockfish.exe", MultiPv = 4, HashMb = 64 };
            saved.Save();
            Assert.True(File.Exists(path), "файл настроек создан");

            var loaded = AppSettings.Load();
            Assert.Equal(saved.EnginePath, loaded.EnginePath);  // путь к движку пережил круг
            Assert.Equal(4, loaded.MultiPv);  // варианты пережили круг
            Assert.Equal(64, loaded.HashMb);  // хеш пережил круг
        });
    }

    [Fact(DisplayName = "Настройки: битый файл не ломает запуск")]
    public void CorruptFileFallsBackToDefaults()
    {
        WithSettingsFile(path =>
        {
            File.WriteAllText(path, "{это не json");
            Assert.Equal(3, AppSettings.Load().MultiPv);  // мусор — значения по умолчанию

            File.WriteAllText(path, "null");
            Assert.Equal(3, AppSettings.Load().MultiPv);  // null — тоже значения по умолчанию

            File.WriteAllText(path, "{\"Threads\": null}");
            Assert.Equal(3, AppSettings.Load().MultiPv);  // неверный тип поля — не падаем
        });
    }

    [Fact(DisplayName = "Настройки: значения вне диапазона поджимаются")]
    public void OutOfRangeValuesAreClamped()
    {
        WithSettingsFile(path =>
        {
            File.WriteAllText(path,
                "{\"Threads\": -4, \"HashMb\": 0, \"MultiPv\": 0, \"SkillLevel\": 99, " +
                "\"EloRating\": 1, \"EngineMoveTimeMs\": -1, \"GameAnalysisMoveTimeMs\": 0, " +
                "\"AnalysisDepthLimit\": -3}");

            var loaded = AppSettings.Load();
            Assert.True(loaded.Threads >= 1, "потоков хотя бы один");
            Assert.True(loaded.HashMb >= 1, "хеш хотя бы мегабайт");
            Assert.True(loaded.MultiPv >= 1, "хотя бы одна линия анализа");
            Assert.InRange(loaded.SkillLevel, 0, 20);  // уровень игры в пределах UCI
            Assert.InRange(loaded.EloRating, 500, 4000);  // рейтинг в разумных пределах
            Assert.True(loaded.EngineMoveTimeMs >= 1, "время на ход положительное");
            Assert.True(loaded.GameAnalysisMoveTimeMs >= 1, "время на разбор положительное");
            Assert.True(loaded.AnalysisDepthLimit >= 0, "предел глубины не отрицательный");
        });
    }

    [Fact(DisplayName = "Поиск движка: подставной каталог")]
    public void LocatorFindsFileInFolder()
    {
        // На чистой машине основной тест поиска проходит вхолостую: список пуст, и все
        // утверждения о нём истинны. Здесь кладём файл в каталог из PATH и проверяем находку.
        var dir = Path.Combine(Path.GetTempPath(), "ChessEmulatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "stockfish-windows-x86-64-avx2.exe");
        File.WriteAllText(exe, string.Empty);

        var previousPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            // Кавычки вокруг записи PATH — законная запись в Windows.
            Environment.SetEnvironmentVariable("PATH", $"\"{dir}\"{Path.PathSeparator}{previousPath}");

            var found = EngineLocator.FindAll();
            Assert.Contains(exe, found, StringComparer.OrdinalIgnoreCase);  // файл найден
            // повторов нет даже при совпадении двух механизмов поиска
            Assert.Equal(found.Count, found.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previousPath);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact(DisplayName = "Поиск движка")]
    public void Locator()
    {
        List<string> found = new();
        Assert.Null(Record.Exception(() => found = EngineLocator.FindAll()));  // поиск не падает

        Assert.True(found.All(File.Exists), "все найденные файлы существуют");
        // повторов нет
        Assert.Equal(found.Count, found.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(found.All(Path.IsPathRooted), "все пути абсолютные");
        Assert.True(found.All(p => Path.GetFileName(p).StartsWith("stockfish", StringComparison.OrdinalIgnoreCase)), "все файлы похожи на движок");

        var first = EngineLocator.FindFirst();
        // первый найденный совпадает со списком
        Assert.Equal(found.Count > 0 ? found[0] : null, first);

        output.WriteLine(found.Count > 0
            ? $"найдено движков: {found.Count}, первый — {first}"
            : "движок не найден (это нормально: он не хранится в репозитории)");
    }
}
