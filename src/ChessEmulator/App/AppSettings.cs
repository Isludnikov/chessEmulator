using System.Text.Json;
using System.Text.Json.Serialization;
using ChessEmulator.Engine;

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

    /// <summary>
    /// Уровень соперника в файле настроек. null означает файл, написанный версией без
    /// этой настройки, — по нему <see cref="Clamp"/> решает, что человек имел в виду.
    /// </summary>
    [JsonPropertyName("Difficulty")]
    [JsonConverter(typeof(JsonStringEnumConverter<DifficultyLevel>))]
    public DifficultyLevel? DifficultyRaw { get; set; }

    /// <summary>Уровень соперника. «Своя» отдаёт силу полям диалога настроек движка.</summary>
    [JsonIgnore]
    public DifficultyLevel Difficulty
    {
        get => DifficultyRaw ?? DifficultyLevel.Maximum;
        set => DifficultyRaw = value;
    }

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

    /// <summary>Показывать подсказки движка: панель советов, стрелки и список вариантов.</summary>
    public bool ShowEngineHints { get; set; } = true;

    /// <summary>
    /// Идёт ли постоянный анализ. Своим переключателем анализ только включают: при выключенных
    /// подсказках он всё равно стоит — показывать его результат некуда, а процессор он занимает
    /// по-настоящему.
    /// </summary>
    [JsonIgnore]
    public bool AnalysisRuns => AutoAnalyze && ShowEngineHints;

    public string? LastPgnDirectory { get; set; }

    /// <summary>Переменная среды, которой тесты уводят настройки во временный файл.</summary>
    public const string PathOverrideVariable = "CHESS_TESTS_SETTINGS";

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChessEmulator",
        "settings.json");

    [JsonIgnore]
    public static string SettingsPath
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(PathOverrideVariable);
            return string.IsNullOrWhiteSpace(overridden) ? DefaultPath : overridden;
        }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    settings.Clamp();
                    return settings;
                }
            }
        }
        catch
        {
            // Повреждённый файл настроек — начинаем со значений по умолчанию.
        }
        return new AppSettings();
    }

    /// <summary>
    /// Приводит значения в рабочие пределы. Файл настроек правят руками, а нулевой MultiPV
    /// или отрицательное время на ход уходят прямо в движок и ломают анализ без единого
    /// сообщения об ошибке.
    /// </summary>
    public void Clamp()
    {
        Threads = Math.Clamp(Threads, 1, Math.Max(1, Environment.ProcessorCount));
        HashMb = Math.Clamp(HashMb, 1, 65536);
        MultiPv = Math.Clamp(MultiPv, 1, 10);
        SkillLevel = Math.Clamp(SkillLevel, 0, 20);
        EloRating = Math.Clamp(EloRating, 500, 4000);
        AnalysisDepthLimit = Math.Clamp(AnalysisDepthLimit, 0, 99);
        EngineMoveTimeMs = Math.Clamp(EngineMoveTimeMs, 1, 600_000);
        GameAnalysisMoveTimeMs = Math.Clamp(GameAnalysisMoveTimeMs, 1, 600_000);

        // Файл без уровня написан версией, где Skill Level и рейтинг из диалога ослабляли
        // движок всегда. Раз человек их трогал — оставляем ему «Свою», иначе соперник после
        // обновления молча заиграл бы в полную силу.
        DifficultyRaw = DifficultyRaw is { } level && Enum.IsDefined(level)
            ? level
            : SkillLevel < 20 || LimitStrength ? DifficultyLevel.Custom : DifficultyLevel.Maximum;
    }

    public void Save()
    {
        try
        {
            var path = SettingsPath;
            var dir = Path.GetDirectoryName(path)!;
            if (dir.Length > 0) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            // Пишем через временный файл: обрыв записи не должен оставить обрезанный JSON,
            // из-за которого при следующем запуске потеряются все настройки сразу.
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // Настройки не критичны — молча игнорируем ошибки записи.
        }
    }
}
