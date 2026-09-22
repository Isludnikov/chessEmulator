using ChessEmulator.Chess;
using Xunit;

namespace ChessEmulator.CoreTests;

/// <summary>Чтение и запись PGN: заголовки, варианты, комментарии, знаки оценки.</summary>
public class PgnTests
{
    private static string Line(string pgn) =>
        string.Join(" ", pgn.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("[")));

    [Fact(DisplayName = "PGN: запись")]
    public void Writing()
    {
        var game = new Game();
        game.Headers["Event"] = "Проверка";
        game.Headers["White"] = "Белые";
        game.Headers["Black"] = "Чёрные";
        foreach (var san in new[] { "e4", "e5", "Nf3", "Nc6" }) game.TryAddSan(san, out _);

        var pgn = Pgn.Write(game);
        Assert.True(pgn.Contains("[Event \"Проверка\"]"), "заголовок Event на месте");
        Assert.True(pgn.Contains("[White \"Белые\"]"), "заголовок White на месте");
        Assert.True(pgn.Contains("[Result \"*\"]"), "заголовок Result на месте");
        Assert.Equal("1. e4 e5 2. Nf3 Nc6 *", Line(pgn));  // текст партии
        Assert.True(pgn.IndexOf("[Event", StringComparison.Ordinal) <
                                             pgn.IndexOf("[White", StringComparison.Ordinal), "Event идёт раньше White");

        // Кавычки и обратные слэши в заголовках экранируются
        var quoted = new Game();
        quoted.Headers["White"] = "Игрок \"Ник\"";
        quoted.Headers["Site"] = @"C:\партии";
        var quotedPgn = Pgn.Write(quoted);
        Assert.True(quotedPgn.Contains("[White \"Игрок \\\"Ник\\\"\"]"), "кавычки экранированы");
        Assert.True(quotedPgn.Contains(@"[Site ""C:\\партии""]"), "обратный слэш экранирован");
        Assert.Equal("Игрок \"Ник\"", Pgn.Read(quotedPgn).Headers["White"]);  // экранирование переживает чтение
        Assert.Equal(@"C:\партии", Pgn.Read(quotedPgn).Headers["Site"]);  // слэш переживает чтение

        // Комментарии и знаки оценки
        var annotated = new Game();
        annotated.TryAddSan("e4", out var e4);
        annotated.TryAddSan("e5", out var e5);
        e4!.Glyph = "!";
        e4.Comment = "лучший первый ход";
        e5!.Glyph = "?!";
        var text = Line(Pgn.Write(annotated));
        Assert.True(text.Contains("e4!"), "знак записан рядом с ходом");
        Assert.True(text.Contains("{лучший первый ход}"), "комментарий записан в фигурных скобках");
        Assert.True(text.Contains("e5?!"), "знак второго хода записан");

        // Нестандартный заголовок тоже попадает в файл
        var custom = new Game();
        custom.Headers["Variant"] = "Standard";
        Assert.True(Pgn.Write(custom).Contains("[Variant \"Standard\"]"), "нестандартный заголовок записан");

        // Длинные партии переносятся по строкам
        var longGame = new Game();
        var rng = new Random(7);
        for (var i = 0; i < 60 && longGame.CurrentPosition.LegalMoves.Count > 0; i++)
        {
            var moves = longGame.CurrentPosition.LegalMoves;
            longGame.AddMove(moves[rng.Next(moves.Count)]);
        }
        var wrapped = Pgn.Write(longGame);
        var longest = wrapped.Split('\n').Max(l => l.TrimEnd('\r').Length);
        Assert.True(longest <= 80, $"строки не длиннее 80 символов (самая длинная {longest})");
        Assert.True(Pgn.Read(wrapped).MainLine().Count == longGame.MainLine().Count, "длинная партия читается обратно");
    }

    [Fact(DisplayName = "PGN: чтение")]
    public void Reading()
    {
        var game = Pgn.Read("[Event \"Тест\"]\n[White \"А\"]\n\n1. e4 e5 2. Nf3 Nc6 1/2-1/2");
        Assert.Equal("Тест", game.Headers["Event"]);  // заголовок прочитан
        Assert.Equal("1/2-1/2", game.Headers["Result"]);  // результат прочитан
        // ходы прочитаны
        Assert.Equal("e4 e5 Nf3 Nc6", string.Join(" ", game.MainLine().Select(n => n.San)));
        Assert.True(game.Current.IsRoot, "после чтения мы в начале партии");

        Assert.Equal(2, Pgn.Read("1. d4 d5 *").MainLine().Count);  // партия без заголовков
        // номера с точками после хода чёрных
        Assert.Equal(3, Pgn.Read("1. e4 e5 2... Nf3 *").MainLine().Count);
        // лишние пробелы и переносы
        Assert.Equal(4, Pgn.Read("1.e4\n  e5\n2.Nf3\tNc6 *").MainLine().Count);

        // Комментарии
        var commented = Pgn.Read("1. e4 {хороший ход} e5 {ответ} *");
        Assert.Equal("хороший ход", commented.MainLine()[0].Comment);  // комментарий у первого хода
        Assert.Equal("ответ", commented.MainLine()[1].Comment);  // комментарий у второго хода
        // два комментария подряд склеиваются
        Assert.Equal("первый второй", Pgn.Read("1. e4 {первый} {второй} *").MainLine()[0].Comment);
        // комментарий до первого хода отбрасывается
        Assert.Single(Pgn.Read("{вступление} 1. e4 *").MainLine());
        // незакрытый комментарий не ломает разбор
        Assert.Single(Pgn.Read("1. e4 {без закрывающей скобки *").MainLine());
        Assert.Single(Pgn.Read("1. e4 ; остаток строки\n").MainLine());  // комментарий до конца строки
        Assert.Equal(2, Pgn.Read("% служебная строка\n1. e4 e5 *").MainLine().Count);  // строки с процентом игнорируются

        // Знаки оценки
        Assert.Equal("!?", Pgn.Read("1. e4!? *").MainLine()[0].Glyph);  // знак после хода
        Assert.Equal("??", Pgn.Read("1. e4?? *").MainLine()[0].Glyph);  // двойной знак
        (string Nag, string Symbol)[] nags =
        {
            ("$1", "!"), ("$2", "?"), ("$3", "!!"), ("$4", "??"), ("$5", "!?"), ("$6", "?!")
        };
        foreach (var (nag, symbol) in nags)
            Assert.Equal(symbol, Pgn.Read($"1. e4 {nag} *").MainLine()[0].Glyph);  // числовой знак {nag}
        Assert.Null(Pgn.Read("1. e4 $99 *").MainLine()[0].Glyph);  // неизвестный числовой знак

        // Варианты
        var withVariation = Pgn.Read("1. e4 e5 (1... c5 2. Nf3) 2. Nf3 *");
        // основная линия при варианте
        Assert.Equal("e4 e5 Nf3", string.Join(" ", withVariation.MainLine().Select(n => n.San)));
        var afterE4 = withVariation.Root.MainChild!;
        Assert.Equal(2, afterE4.Children.Count);  // вариант добавлен
        Assert.Equal("c5", afterE4.Children[1].San);  // первый ход варианта
        Assert.Equal("Nf3", afterE4.Children[1].MainChild!.San);  // вариант продолжается

        var nested = Pgn.Read("1. e4 e5 (1... c5 2. Nf3 d6 (2... Nc6 3. d4)) 2. Nf3 *");
        var sicilian = nested.Root.MainChild!.Children[1];
        Assert.Equal(2, sicilian.MainChild!.Children.Count);  // вложенный вариант на месте
        Assert.Equal("Nc6", sicilian.MainChild!.Children[1].San);  // ход вложенного варианта

        Assert.Single(Pgn.Read("1. e4 Zz9 *").MainLine());  // нелегальные ходы пропускаются

        // Несколько партий в одном файле
        const string two = "[Event \"Первая\"]\n\n1. e4 e5 1-0\n\n[Event \"Вторая\"]\n\n1. d4 d5 0-1\n";
        var all = Pgn.ReadAll(two);
        Assert.Equal(2, all.Count);  // прочитано две партии
        Assert.Equal("Первая", all[0].Headers["Event"]);  // заголовок первой
        Assert.Equal("Вторая", all[1].Headers["Event"]);  // заголовок второй
        Assert.Equal("d4 d5", string.Join(" ", all[1].MainLine().Select(n => n.San)));  // ходы второй партии
        Assert.Single(Pgn.ReadAll(two, 1));  // ограничение количества партий
        Assert.Equal("Первая", Pgn.Read(two).Headers["Event"]);  // Read берёт первую партию

        Assert.Throws<FormatException>(() => Pgn.Read(""));  // пустой текст отвергается
        Assert.Throws<FormatException>(() => Pgn.Read("   \n  \n"));  // пробелы отвергаются

        // Партия с расставленной позиции
        var setup = Pgn.Read("[SetUp \"1\"]\n[FEN \"4k3/8/8/8/8/8/4P3/4K3 w - - 0 1\"]\n\n1. e4 *");
        Assert.Equal("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", setup.StartFen);  // стартовая позиция из заголовка
        Assert.Equal("e4", setup.MainLine()[0].San);  // ход от расставленной позиции
        // испорченный FEN не ломает чтение
        Assert.Equal(Position.StartFen, Pgn.Read("[FEN \"мусор\"]\n\n1. e4 *").StartFen);
    }

    [Fact(DisplayName = "PGN: комментарий до конца строки, а не до конца партии")]
    public void SemicolonCommentStopsAtLineEnd()
    {
        var game = Pgn.Read("1. e4 ; заметка к первому ходу\n1... e5 2. Nf3 Nc6 *");
        // партия продолжается после строки с точкой с запятой
        Assert.Equal("e4 e5 Nf3 Nc6", string.Join(" ", game.MainLine().Select(n => n.San)));

        // Точка с запятой в последней строке по-прежнему съедает остаток строки.
        Assert.Single(Pgn.Read("1. e4 ; остаток строки\n").MainLine());
    }

    [Fact(DisplayName = "PGN: комментарий с фигурными скобками не рвёт файл")]
    public void CommentBracesAreSanitized()
    {
        var game = new Game();
        Assert.True(game.TryAddSan("e4", out var node), "ход добавляется");
        node!.Comment = "смотри вариант {a} и запись}";

        var back = Pgn.Read(Pgn.Write(game));

        Assert.Single(back.MainLine());  // ходы не потерялись
        Assert.Equal("e4", back.MainLine()[0].San);  // ход разобрался
        Assert.DoesNotContain("{", back.MainLine()[0].Comment ?? "");  // открывающая скобка убрана
        Assert.DoesNotContain("}", back.MainLine()[0].Comment ?? "");  // закрывающая тоже
        Assert.Contains("смотри вариант", back.MainLine()[0].Comment ?? "");  // текст сохранился

        // Перевод строки внутри комментария не должен превращаться в разрыв записи.
        var multiline = new Game();
        Assert.True(multiline.TryAddSan("d4", out var d4), "ход добавляется");
        d4!.Comment = "первая строка\nвторая строка";
        Assert.Single(Pgn.Read(Pgn.Write(multiline)).MainLine());  // ход на месте
    }

    [Fact(DisplayName = "PGN: строка комментария в квадратных скобках не разрывает партию")]
    public void BracketLineInsideCommentIsNotHeader()
    {
        // Так экспортирует Lichess: комментарий переносится, и строка начинается с [%eval ...].
        const string pgn = "[Event \"Проба\"]\n\n1. e4 { белые играют\n[%eval 0.24]\nкоролевскую пешку } e5 2. Nf3 *\n";

        var game = Pgn.Read(pgn);
        // партия цела
        Assert.Equal("e4 e5 Nf3", string.Join(" ", game.MainLine().Select(n => n.San)));
        Assert.Single(Pgn.ReadAll(pgn));  // это одна партия, а не две
    }

    [Fact(DisplayName = "PGN: длинный комментарий не ломает перенос строк")]
    public void LongTokenWrapping()
    {
        var game = new Game();
        Assert.True(game.TryAddSan("e4", out var node), "ход добавляется");
        node!.Comment = new string('ы', 100);

        var text = Pgn.Write(game);
        var body = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .SkipWhile(l => l.StartsWith("[") || l.Trim().Length == 0)
            .ToList();

        // пустых строк внутри записи ходов быть не должно
        Assert.DoesNotContain(body.Take(Math.Max(0, body.Count - 1)), l => l.Trim().Length == 0);
        Assert.Single(Pgn.Read(text).MainLine());  // и всё ещё читается обратно
    }

    [Fact(DisplayName = "PGN: неизвестный знак не стирает разобранный")]
    public void UnknownNagKeepsGlyph()
    {
        // $14 («у белых чуть лучше») мы не умеем показывать, но это не повод терять «!?».
        Assert.Equal("!?", Pgn.Read("1. e4!? $14 *").MainLine()[0].Glyph);  // знак с хода сохранён
        Assert.Equal("!", Pgn.Read("1. e4 $1 $14 *").MainLine()[0].Glyph);  // известный знак сохранён
        Assert.Null(Pgn.Read("1. e4 $14 *").MainLine()[0].Glyph);  // знака не было — и не появилось
    }

    [Fact(DisplayName = "PGN: неразобранный заголовок FEN не попадает в запись")]
    public void BadFenHeaderIsDropped()
    {
        var game = Pgn.Read("[SetUp \"1\"]\n[FEN \"мусор\"]\n\n1. e4 e5 *");

        Assert.Equal(Position.StartFen, game.StartFen);  // партия идёт от начальной позиции
        Assert.False(game.Headers.ContainsKey("FEN"), "противоречивый заголовок FEN убран");
        Assert.False(game.Headers.ContainsKey("SetUp"), "и SetUp вместе с ним");
        Assert.DoesNotContain("мусор", Pgn.Write(game));  // запись не противоречит ходам
    }

    [Fact(DisplayName = "PGN: полный круг")]
    public void RoundTrip()
    {
        var game = new Game();
        game.Headers["Event"] = "Круговая проверка";
        game.TryAddSan("e4", out _);
        game.TryAddSan("c5", out var c5);
        c5!.Comment = "Сицилианская защита";
        c5.Glyph = "!?";
        game.TryAddSan("Nf3", out _);
        game.TryAddSan("d6", out _);
        game.GoToStart();
        game.GoForward();
        game.TryAddSan("e5", out _);      // вариант
        game.TryAddSan("Nf3", out _);
        game.TryAddSan("Nc6", out _);

        var pgn = Pgn.Write(game);
        var back = Pgn.Read(pgn);

        Assert.Equal("Круговая проверка", back.Headers["Event"]);  // заголовок сохранился
        // основная линия сохранилась
        Assert.Equal("e4 c5 Nf3 d6", string.Join(" ", back.MainLine().Select(n => n.San)));
        Assert.Equal("Сицилианская защита", back.MainLine()[1].Comment);  // комментарий сохранился
        Assert.Equal("!?", back.MainLine()[1].Glyph);  // знак сохранился

        var branch = back.Root.MainChild!;
        Assert.Equal(2, branch.Children.Count);  // вариант сохранился
        Assert.Equal("e5 Nf3 Nc6", string.Join(" ", Walk(branch.Children[1])));  // ходы варианта

        // Повторная запись даёт тот же текст
        Assert.Equal(pgn, Pgn.Write(back));  // вторая запись совпадает с первой
    }

    private static IEnumerable<string> Walk(MoveNode node)
    {
        for (var n = node; n != null; n = n.MainChild) yield return n.San;
    }

    [Fact(DisplayName = "PGN: партия из папки samples")]
    public void SampleFile()
    {
        var path = FindSample("opera-game.pgn");
        Assert.NotNull(path);  // не найден файл samples/opera-game.pgn

        var game = Pgn.Read(File.ReadAllText(path));
        Assert.Equal("Paul Morphy", game.Headers["White"]);  // игрок белыми
        Assert.Equal("1-0", game.Headers["Result"]);  // результат
        Assert.Equal(33, game.MainLine().Count);  // длина партии
        Assert.Equal("Rd8#", game.MainLine()[^1].San);  // последний ход — мат
        // комментарий из файла прочитан
        Assert.Equal("Чёрные связывают коня, но теряют время.", game.MainLine()[5].Comment);
        // состояние партии
        Assert.Equal(GameResultState.WhiteWins, game.EvaluateState(game.MainLine()[^1]).State);

        var back = Pgn.Read(Pgn.Write(game));
        // партия переживает запись и чтение
        Assert.Equal(string.Join(" ", game.MainLine().Select(n => n.San)), string.Join(" ", back.MainLine().Select(n => n.San)));
    }

    /// <summary>Ищет файл примера, поднимаясь от папки сборки к корню репозитория.</summary>
    private static string? FindSample(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
