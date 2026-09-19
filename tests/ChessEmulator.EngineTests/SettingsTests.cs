using System.Text.Json;
using ChessEmulator.App;
using ChessEmulator.Engine;
using ChessEmulator.TestKit;

namespace ChessEmulator.EngineTests;

/// <summary>Настройки приложения и поиск исполняемого файла движка.</summary>
internal static class SettingsTests
{
    public static void Run()
    {
        Settings();
        Locator();
    }

    private static void Settings()
    {
        Test.Suite("Настройки: значения и хранение", () =>
        {
            var defaults = new AppSettings();
            Test.Check("движок не выбран", null, defaults.EnginePath);
            Test.Check("вариантов анализа по умолчанию", 3, defaults.MultiPv);
            Test.Check("уровень игры по умолчанию", 20, defaults.SkillLevel);
            Test.Check("размер хеша по умолчанию", 256, defaults.HashMb);
            Test.Check("рейтинг по умолчанию", 1600, defaults.EloRating);
            Test.False("сила не ограничена", defaults.LimitStrength);
            Test.Check("время на ход движка", 1000, defaults.EngineMoveTimeMs);
            Test.Check("время на позицию при разборе партии", 500, defaults.GameAnalysisMoveTimeMs);
            Test.Check("глубина анализа не ограничена", 0, defaults.AnalysisDepthLimit);
            Test.True("потоков хотя бы один", defaults.Threads >= 1);
            Test.True("потоков не больше числа ядер", defaults.Threads <= Environment.ProcessorCount);
            Test.True("координаты показываются", defaults.ShowCoordinates);
            Test.True("подсказки ходов включены", defaults.ShowLegalMoveHints);
            Test.True("стрелка лучшего хода включена", defaults.ShowBestMoveArrow);
            Test.True("анализ включается сам", defaults.AutoAnalyze);
            Test.False("доска не перевёрнута", defaults.BoardFlipped);

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

            Test.Check("путь к движку", saved.EnginePath, loaded.EnginePath);
            Test.Check("потоки", 6, loaded.Threads);
            Test.Check("хеш", 512, loaded.HashMb);
            Test.Check("варианты", 5, loaded.MultiPv);
            Test.Check("уровень", 12, loaded.SkillLevel);
            Test.True("ограничение силы", loaded.LimitStrength);
            Test.Check("рейтинг", 2200, loaded.EloRating);
            Test.Check("предел глубины", 24, loaded.AnalysisDepthLimit);
            Test.Check("время на ход", 2500, loaded.EngineMoveTimeMs);
            Test.Check("время на разбор", 750, loaded.GameAnalysisMoveTimeMs);
            Test.True("перевёрнутая доска", loaded.BoardFlipped);
            Test.False("координаты выключены", loaded.ShowCoordinates);
            Test.False("подсказки выключены", loaded.ShowLegalMoveHints);
            Test.False("стрелка выключена", loaded.ShowBestMoveArrow);
            Test.False("автоанализ выключен", loaded.AutoAnalyze);
            Test.Check("папка партий", saved.LastPgnDirectory, loaded.LastPgnDirectory);

            Test.False("служебный путь не попадает в файл", json.Contains("SettingsPath"));
            Test.True("путь к файлу настроек оканчивается на settings.json",
                AppSettings.SettingsPath.EndsWith(Path.Combine("ChessEmulator", "settings.json"),
                    StringComparison.OrdinalIgnoreCase));

            // Испорченный файл не должен ронять приложение: Load ловит ошибку разбора
            Test.Throws<JsonException>("мусор вместо JSON не разбирается",
                () => JsonSerializer.Deserialize<AppSettings>("{это не json"));
            Test.NoThrow("чтение настроек не падает", () => AppSettings.Load());
            Test.True("чтение настроек возвращает объект", AppSettings.Load() != null);
        });
    }

    private static void Locator()
    {
        Test.Suite("Поиск движка", () =>
        {
            List<string> found = new();
            Test.NoThrow("поиск не падает", () => found = EngineLocator.FindAll());

            Test.True("все найденные файлы существуют", found.All(File.Exists));
            Test.Check("повторов нет", found.Count,
                found.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Test.True("все пути абсолютные", found.All(Path.IsPathRooted));
            Test.True("все файлы похожи на движок",
                found.All(p => Path.GetFileName(p).StartsWith("stockfish", StringComparison.OrdinalIgnoreCase)));

            var first = EngineLocator.FindFirst();
            Test.Check("первый найденный совпадает со списком",
                found.Count > 0 ? found[0] : null, first);

            Console.WriteLine(found.Count > 0
                ? $"        найдено движков: {found.Count}, первый — {first}"
                : "        движок не найден (это нормально: он не хранится в репозитории)");
        });
    }
}
