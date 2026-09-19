using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessEmulator.App;

/// <summary>Настройки приложения, хранятся в %AppData%\ChessEmulator\settings.json.</summary>
public sealed class AppSettings
{
    public string? EnginePath { get; set; }
    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int HashMb { get; set; } = 256;
    public int MultiPv { get; set; } = 3;
    public int SkillLevel { get; set; } = 20;
    public bool LimitStrength { get; set; }
    public int EloRating { get; set; } = 1600;

    /// <summary>Ограничение глубины бесконечного анализа (0 — без ограничения).</summary>
    public int AnalysisDepthLimit { get; set; }

    /// <summary>Время на ход движка в режиме игры, мс.</summary>
    public int EngineMoveTimeMs { get; set; } = 1000;

    /// <summary>Время на позицию при анализе всей партии, мс.</summary>
    public int GameAnalysisMoveTimeMs { get; set; } = 500;

    public bool BoardFlipped { get; set; }
    public bool ShowCoordinates { get; set; } = true;
    public bool ShowLegalMoveHints { get; set; } = true;
    public bool ShowBestMoveArrow { get; set; } = true;
    public bool AutoAnalyze { get; set; } = true;
    public string? LastPgnDirectory { get; set; }

    [JsonIgnore]
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChessEmulator",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // Повреждённый файл настроек — начинаем со значений по умолчанию.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Настройки не критичны — молча игнорируем ошибки записи.
        }
    }
}
