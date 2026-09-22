using ChessEmulator.Engine;
using Xunit;

namespace ChessEmulator.EngineTests;

/// <summary>Таблица уровней соперника и параметры UCI, которые из неё получаются.</summary>
public class DifficultyTests
{
    private static DifficultyDefaults Defaults(int skill = 20, bool limit = false,
        int elo = 1600, int moveTime = 1000, int multiPv = 3) =>
        new(skill, limit, elo, moveTime, multiPv);

    [Fact(DisplayName = "Сложность: таблица уровней")]
    public void ТаблицаУровней()
    {
        var all = Difficulty.All;
        Assert.Equal(6, all.Count);
        Assert.Equal(
            new[]
            {
                DifficultyLevel.Beginner, DifficultyLevel.Amateur, DifficultyLevel.Club,
                DifficultyLevel.Expert, DifficultyLevel.Maximum, DifficultyLevel.Custom
            },
            all.Select(p => p.Level));

        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Title), "у уровня есть надпись"));
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Hint), "у уровня есть пояснение"));
        Assert.Equal(all.Count, all.Select(p => p.Title).Distinct().Count());
        Assert.All(all, p => Assert.InRange(p.RandomMoveChance, 0.0, 1.0));
        Assert.All(all, p => Assert.True(p.MaxLossCp >= 0, "перила не отрицательны"));

        // От «Новичка» к «Эксперту» соперник обязан усиливаться по каждому рычагу сразу,
        // иначе список уровней перестаёт быть списком уровней.
        var ladder = all.Take(4).ToList();
        for (var i = 1; i < ladder.Count; i++)
        {
            var weak = ladder[i - 1];
            var strong = ladder[i];
            Assert.True(strong.Elo >= weak.Elo, $"{strong.Title}: рейтинг не ниже");
            Assert.True(strong.SkillLevel >= weak.SkillLevel, $"{strong.Title}: уровень не ниже");
            Assert.True(strong.MoveTimeMs >= weak.MoveTimeMs, $"{strong.Title}: времени не меньше");
            Assert.True(strong.DepthLimit >= weak.DepthLimit, $"{strong.Title}: глубина не меньше");
            Assert.True(strong.CandidateCount <= weak.CandidateCount, $"{strong.Title}: кандидатов не больше");
            Assert.True(strong.TemperatureCp <= weak.TemperatureCp, $"{strong.Title}: разброс не больше");
            Assert.True(strong.MaxLossCp <= weak.MaxLossCp, $"{strong.Title}: уступка не больше");
            Assert.True(strong.RandomMoveChance <= weak.RandomMoveChance, $"{strong.Title}: наугад не чаще");
        }

        // Ослабление — только у первых четырёх уровней.
        Assert.All(ladder, p => Assert.False(p.IsFullStrength, $"{p.Title} ослаблен"));
    }

    [Fact(DisplayName = "Сложность: «Максимум» не ослабляет движок")]
    public void МаксимумНеОслабляет()
    {
        var profile = Difficulty.Resolve(DifficultyLevel.Maximum, Defaults(moveTime: 2500));

        Assert.True(profile.IsFullStrength, "движку нечего ослаблять");
        Assert.Null(profile.Elo);
        Assert.Equal(20, profile.SkillLevel);
        Assert.Equal(0, profile.DepthLimit);
        Assert.Equal(1, profile.CandidateCount);
        Assert.Equal(2500, profile.MoveTimeMs);  // время берётся из настроек
    }

    [Fact(DisplayName = "Сложность: «Своя» берёт значения из настроек движка")]
    public void СвояБерётНастройки()
    {
        var limited = Difficulty.Resolve(DifficultyLevel.Custom,
            Defaults(skill: 7, limit: true, elo: 1900, moveTime: 1234));
        Assert.Equal(7, limited.SkillLevel);
        Assert.Equal(1900, limited.Elo);
        Assert.Equal(1234, limited.MoveTimeMs);
        Assert.False(limited.IsFullStrength, "уровень игры занижен");

        var withoutElo = Difficulty.Resolve(DifficultyLevel.Custom, Defaults(skill: 7, limit: false, elo: 1900));
        Assert.Null(withoutElo.Elo);  // без флажка рейтинг не применяется

        // Максимальные ручные настройки — это тоже полная сила: лишних команд движку не будет.
        Assert.True(Difficulty.Resolve(DifficultyLevel.Custom, Defaults()).IsFullStrength);

        // Уровень из руками правленого файла поджимается к пределам UCI.
        Assert.Equal(20, Difficulty.Resolve(DifficultyLevel.Custom, Defaults(skill: 99)).SkillLevel);
        Assert.Equal(0, Difficulty.Resolve(DifficultyLevel.Custom, Defaults(skill: -5)).SkillLevel);
    }

    [Fact(DisplayName = "Сложность: время на ход не превышает настройку")]
    public void ВремяНеПревышаетНастройку()
    {
        // Настройка пользователя — верхний предел: пресет вправе думать меньше, но не дольше.
        Assert.Equal(200, Difficulty.Resolve(DifficultyLevel.Expert, Defaults(moveTime: 200)).MoveTimeMs);
        Assert.Equal(700, Difficulty.Resolve(DifficultyLevel.Expert, Defaults(moveTime: 5000)).MoveTimeMs);
        Assert.True(Difficulty.Resolve(DifficultyLevel.Beginner, Defaults(moveTime: 0)).MoveTimeMs >= 1,
            "время на ход положительное даже при нулевой настройке");
    }

    [Fact(DisplayName = "Сложность: неизвестный уровень не роняет выбор")]
    public void НеизвестныйУровень()
    {
        // Ослабление должно быть осознанным, поэтому мусор в файле — это полная сила.
        Assert.Equal(DifficultyLevel.Maximum, Difficulty.For((DifficultyLevel)42).Level);
        Assert.Equal(Difficulty.IndexOf(DifficultyLevel.Maximum), Difficulty.IndexOf((DifficultyLevel)42));
        Assert.True(Difficulty.Resolve((DifficultyLevel)42, Defaults()).IsFullStrength);
    }

    [Fact(DisplayName = "Сложность: рейтинг поджимается к пределам движка")]
    public void РейтингПоджимается()
    {
        // Значение spin вне объявленного диапазона Stockfish молча игнорирует: ошибки нет,
        // но и ослабления тоже — поэтому поджимаем сами.
        var option = new UciOption { Name = "UCI_Elo", Min = "1320", Max = "3190" };
        Assert.Equal(1320, Difficulty.ClampElo(option, 500));
        Assert.Equal(3190, Difficulty.ClampElo(option, 4000));
        Assert.Equal(1800, Difficulty.ClampElo(option, 1800));

        Assert.Equal(500, Difficulty.ClampElo(null, 500));  // движок про рейтинг не знает
        Assert.Equal(500, Difficulty.ClampElo(new UciOption { Name = "UCI_Elo" }, 500));  // пределов нет
    }

    [Fact(DisplayName = "Сложность: параметры хода соперника не трогают потоки и хеш")]
    public void ПараметрыХодаСоперника()
    {
        var option = new UciOption { Name = "UCI_Elo", Min = "1320", Max = "3190" };
        var weak = Difficulty.OpponentOptions(Difficulty.For(DifficultyLevel.Beginner), option);

        Assert.Equal("5", Value(weak, "MultiPV"));
        Assert.Equal("0", Value(weak, "Skill Level"));
        Assert.Equal("true", Value(weak, "UCI_LimitStrength"));
        Assert.Equal("1320", Value(weak, "UCI_Elo"));

        // Перевыставление хеша чистит таблицу перестановок, потоков — пересоздаёт пул.
        // Дважды на каждый ход соперника это заметная просадка и потеря кеша поиска.
        Assert.DoesNotContain(weak, p => p.Key == "Threads");
        Assert.DoesNotContain(weak, p => p.Key == "Hash");

        var full = Difficulty.OpponentOptions(Difficulty.For(DifficultyLevel.Maximum), option);
        Assert.Equal("false", Value(full, "UCI_LimitStrength"));
        Assert.DoesNotContain(full, p => p.Key == "UCI_Elo");
    }

    [Fact(DisplayName = "Сложность: возврат к полной силе снимает все ограничения")]
    public void ВозвратКПолнойСиле()
    {
        var options = Difficulty.FullStrengthOptions(3);

        Assert.Equal(3, options.Count);
        Assert.Equal("3", Value(options, "MultiPV"));
        Assert.Equal("20", Value(options, "Skill Level"));
        // Снятый флажок делает залежавшийся рейтинг безвредным, сбрасывать его незачем.
        Assert.Equal("false", Value(options, "UCI_LimitStrength"));

        Assert.Equal("1", Value(Difficulty.FullStrengthOptions(0), "MultiPV"));  // нулевой MultiPV ломает анализ
    }

    private static string Value(List<KeyValuePair<string, string>> options, string name) =>
        options.Single(p => p.Key == name).Value;
}
