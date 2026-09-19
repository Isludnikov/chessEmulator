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
        Assert.Equal(saved.LastPgnDirectory, loaded.LastPgnDirectory);  // папка партий

        Assert.False(json.Contains("SettingsPath"), "служебный путь не попадает в файл");
        Assert.True(AppSettings.SettingsPath.EndsWith(Path.Combine("ChessEmulator", "settings.json"),
                StringComparison.OrdinalIgnoreCase), "путь к файлу настроек оканчивается на settings.json");

        // Испорченный файл не должен ронять приложение: Load ловит ошибку разбора
        // мусор вместо JSON не разбирается
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppSettings>("{это не json"));
        Assert.Null(Record.Exception(() => AppSettings.Load()));  // чтение настроек не падает
        Assert.True(AppSettings.Load() != null, "чтение настроек возвращает объект");
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
