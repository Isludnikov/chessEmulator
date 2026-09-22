namespace ChessEmulator.Engine;

/// <summary>Уровень соперника. Порядок членов — порядок в списке на экране.</summary>
public enum DifficultyLevel
{
    Beginner,
    Amateur,
    Club,
    Expert,
    Maximum,
    Custom
}

/// <summary>Ручные настройки силы: ими пользуются уровни «Максимум» и «Своя».</summary>
public readonly record struct DifficultyDefaults(
    int SkillLevel, bool LimitStrength, int EloRating, int MoveTimeMs, int MultiPv);

/// <summary>
/// Как соперник ослабляется на один свой ход. Всё ослабление живёт только внутри хода
/// соперника: шкала оценки, стрелки, подсказки и разбор партии идут на полной силе.
/// </summary>
public sealed record DifficultyProfile
{
    public required DifficultyLevel Level { get; init; }

    /// <summary>Надпись в списке.</summary>
    public required string Title { get; init; }

    /// <summary>Пояснение: подпись под списком и строка состояния.</summary>
    public required string Hint { get; init; }

    /// <summary>UCI_Elo; null — не ограничивать силу рейтингом.</summary>
    public int? Elo { get; init; }

    /// <summary>
    /// Skill Level. Нужен движкам без UCI_Elo: Stockfish при UCI_LimitStrength выводит
    /// уровень из рейтинга и это поле игнорирует.
    /// </summary>
    public int SkillLevel { get; init; } = 20;

    /// <summary>Время на ход соперника, мс. 0 — брать из настроек.</summary>
    public int MoveTimeMs { get; init; }

    /// <summary>Потолок глубины поиска соперника. 0 — без потолка.</summary>
    public int DepthLimit { get; init; }

    /// <summary>Сколько линий MultiPV рассматривать при выборе хода. 1 — только лучшую.</summary>
    public int CandidateCount { get; init; } = 1;

    /// <summary>Разброс выбора, сотые доли пешки. 0 — всегда лучший из допустимых.</summary>
    public int TemperatureCp { get; init; }

    /// <summary>Насколько кандидат вправе уступать лучшему. Сверх этого линия не рассматривается.</summary>
    public int MaxLossCp { get; init; }

    /// <summary>Доля ходов, сделанных наугад из законных.</summary>
    public double RandomMoveChance { get; init; }

    /// <summary>Нижний предел «раздумий»: мгновенный ответ выглядит поломкой, а не игрой.</summary>
    public int MinThinkMs { get; init; }

    /// <summary>
    /// Ослаблять нечего: параметры движка трогать не нужно и ход берётся как есть.
    /// «Своя» с максимальными ручными настройками попадает сюда сама.
    /// </summary>
    public bool IsFullStrength =>
        SkillLevel >= 20 && Elo is null && DepthLimit <= 0 && CandidateCount <= 1
        && TemperatureCp <= 0 && RandomMoveChance <= 0;
}

/// <summary>Таблица уровней и сборка параметров UCI для хода соперника.</summary>
public static class Difficulty
{
    /// <summary>Уровни в порядке показа: индекс в списке равен индексу в выпадающем списке.</summary>
    public static IReadOnlyList<DifficultyProfile> All { get; } = new[]
    {
        new DifficultyProfile
        {
            Level = DifficultyLevel.Beginner,
            Title = "Новичок",
            Hint = "Часто ошибается и ходит быстро",
            Elo = 1320,
            SkillLevel = 0,
            MoveTimeMs = 150,
            DepthLimit = 4,
            CandidateCount = 5,
            TemperatureCp = 250,
            MaxLossCp = 400,
            RandomMoveChance = 0.12,
            MinThinkMs = 350
        },
        new DifficultyProfile
        {
            Level = DifficultyLevel.Amateur,
            Title = "Любитель",
            Hint = "Знает дебюты, зевает под давлением",
            Elo = 1500,
            SkillLevel = 4,
            MoveTimeMs = 250,
            DepthLimit = 6,
            CandidateCount = 4,
            TemperatureCp = 140,
            MaxLossCp = 250,
            RandomMoveChance = 0.05,
            MinThinkMs = 350
        },
        new DifficultyProfile
        {
            Level = DifficultyLevel.Club,
            Title = "Клубный",
            Hint = "Ровная игра клубного уровня",
            Elo = 1800,
            SkillLevel = 9,
            MoveTimeMs = 400,
            DepthLimit = 10,
            CandidateCount = 3,
            TemperatureCp = 70,
            MaxLossCp = 150,
            RandomMoveChance = 0.015,
            MinThinkMs = 300
        },
        new DifficultyProfile
        {
            Level = DifficultyLevel.Expert,
            Title = "Эксперт",
            Hint = "Сильный соперник, ошибается редко",
            Elo = 2200,
            SkillLevel = 14,
            MoveTimeMs = 700,
            DepthLimit = 14,
            CandidateCount = 2,
            TemperatureCp = 30,
            MaxLossCp = 60,
            RandomMoveChance = 0,
            MinThinkMs = 250
        },
        new DifficultyProfile
        {
            Level = DifficultyLevel.Maximum,
            Title = "Максимум",
            Hint = "Движок на полной силе"
        },
        new DifficultyProfile
        {
            Level = DifficultyLevel.Custom,
            Title = "Своя",
            Hint = "По полям в настройках движка"
        }
    };

    /// <summary>Профиль уровня. Неизвестный уровень — полная сила: ослабление должно быть осознанным.</summary>
    public static DifficultyProfile For(DifficultyLevel level)
    {
        foreach (var profile in All)
        {
            if (profile.Level == level) return profile;
        }
        return All[IndexOf(DifficultyLevel.Maximum)];
    }

    /// <summary>Индекс уровня в списке. Неизвестный уровень — индекс «Максимума».</summary>
    public static int IndexOf(DifficultyLevel level)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Level == level) return i;
        }
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Level == DifficultyLevel.Maximum) return i;
        }
        return 0;
    }

    /// <summary>Подставляет ручные настройки в «Свою» и поджимает время на ход.</summary>
    public static DifficultyProfile Resolve(DifficultyLevel level, DifficultyDefaults defaults)
    {
        var preset = For(level);

        // Настройка пользователя — верхний предел: пресет вправе думать меньше, но не дольше.
        var userTime = Math.Max(1, defaults.MoveTimeMs);
        var moveTime = preset.MoveTimeMs <= 0 ? userTime : Math.Min(preset.MoveTimeMs, userTime);

        return preset.Level == DifficultyLevel.Custom
            ? preset with
            {
                MoveTimeMs = moveTime,
                SkillLevel = Math.Clamp(defaults.SkillLevel, 0, 20),
                Elo = defaults.LimitStrength ? defaults.EloRating : null
            }
            : preset with { MoveTimeMs = moveTime };
    }

    /// <summary>
    /// Рейтинг в пределах, объявленных движком. Значение spin вне диапазона Stockfish молча
    /// игнорирует: параметр остаётся прежним, ошибки нет, но и ослабления тоже.
    /// </summary>
    public static int ClampElo(UciOption? option, int elo)
    {
        if (option is null) return elo;
        if (int.TryParse(option.Min, out var min)) elo = Math.Max(min, elo);
        if (int.TryParse(option.Max, out var max)) elo = Math.Min(max, elo);
        return elo;
    }

    /// <summary>
    /// Параметры на один ход соперника. Threads и Hash сюда не входят намеренно: у обоих
    /// в Stockfish есть обработчик изменения — хеш пересоздаёт и чистит таблицу перестановок,
    /// потоки пересоздают пул. Слать их на каждый ход нельзя.
    /// </summary>
    public static List<KeyValuePair<string, string>> OpponentOptions(
        DifficultyProfile profile, UciOption? eloOption)
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("MultiPV", Math.Max(1, profile.CandidateCount).ToString()),
            new("Skill Level", profile.SkillLevel.ToString()),
            new("UCI_LimitStrength", profile.Elo.HasValue ? "true" : "false")
        };

        if (profile.Elo is { } elo)
        {
            options.Add(new KeyValuePair<string, string>("UCI_Elo", ClampElo(eloOption, elo).ToString()));
        }

        return options;
    }

    /// <summary>
    /// Возврат к полной силе после хода соперника. Рейтинг не сбрасываем: снятый
    /// UCI_LimitStrength делает залежавшееся значение безвредным.
    /// </summary>
    public static List<KeyValuePair<string, string>> FullStrengthOptions(int multiPv) => new()
    {
        new("MultiPV", Math.Max(1, multiPv).ToString()),
        new("Skill Level", "20"),
        new("UCI_LimitStrength", "false")
    };
}
