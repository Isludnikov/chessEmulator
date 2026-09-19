using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>Чтение и запись PGN: заголовки, варианты, комментарии, знаки оценки.</summary>
internal static class PgnTests
{
    public static void Run()
    {
        Writing();
        Reading();
        RoundTrip();
        SampleFile();
    }

    private static string Line(string pgn) =>
        string.Join(" ", pgn.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("[")));

    private static void Writing()
    {
        Test.Suite("PGN: запись", () =>
        {
            var game = new Game();
            game.Headers["Event"] = "Проверка";
            game.Headers["White"] = "Белые";
            game.Headers["Black"] = "Чёрные";
            foreach (var san in new[] { "e4", "e5", "Nf3", "Nc6" }) game.TryAddSan(san, out _);

            var pgn = Pgn.Write(game);
            Test.True("заголовок Event на месте", pgn.Contains("[Event \"Проверка\"]"));
            Test.True("заголовок White на месте", pgn.Contains("[White \"Белые\"]"));
            Test.True("заголовок Result на месте", pgn.Contains("[Result \"*\"]"));
            Test.Check("текст партии", "1. e4 e5 2. Nf3 Nc6 *", Line(pgn));
            Test.True("Event идёт раньше White", pgn.IndexOf("[Event", StringComparison.Ordinal) <
                                                 pgn.IndexOf("[White", StringComparison.Ordinal));

            // Кавычки и обратные слэши в заголовках экранируются
            var quoted = new Game();
            quoted.Headers["White"] = "Игрок \"Ник\"";
            quoted.Headers["Site"] = @"C:\партии";
            var quotedPgn = Pgn.Write(quoted);
            Test.True("кавычки экранированы", quotedPgn.Contains("[White \"Игрок \\\"Ник\\\"\"]"));
            Test.True("обратный слэш экранирован", quotedPgn.Contains(@"[Site ""C:\\партии""]"));
            Test.Check("экранирование переживает чтение", "Игрок \"Ник\"", Pgn.Read(quotedPgn).Headers["White"]);
            Test.Check("слэш переживает чтение", @"C:\партии", Pgn.Read(quotedPgn).Headers["Site"]);

            // Комментарии и знаки оценки
            var annotated = new Game();
            annotated.TryAddSan("e4", out var e4);
            annotated.TryAddSan("e5", out var e5);
            e4!.Glyph = "!";
            e4.Comment = "лучший первый ход";
            e5!.Glyph = "?!";
            var text = Line(Pgn.Write(annotated));
            Test.True("знак записан рядом с ходом", text.Contains("e4!"));
            Test.True("комментарий записан в фигурных скобках", text.Contains("{лучший первый ход}"));
            Test.True("знак второго хода записан", text.Contains("e5?!"));

            // Нестандартный заголовок тоже попадает в файл
            var custom = new Game();
            custom.Headers["Variant"] = "Standard";
            Test.True("нестандартный заголовок записан", Pgn.Write(custom).Contains("[Variant \"Standard\"]"));

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
            Test.True($"строки не длиннее 80 символов (самая длинная {longest})", longest <= 80);
            Test.True("длинная партия читается обратно",
                Pgn.Read(wrapped).MainLine().Count == longGame.MainLine().Count);
        });
    }

    private static void Reading()
    {
        Test.Suite("PGN: чтение", () =>
        {
            var game = Pgn.Read("[Event \"Тест\"]\n[White \"А\"]\n\n1. e4 e5 2. Nf3 Nc6 1/2-1/2");
            Test.Check("заголовок прочитан", "Тест", game.Headers["Event"]);
            Test.Check("результат прочитан", "1/2-1/2", game.Headers["Result"]);
            Test.Check("ходы прочитаны", "e4 e5 Nf3 Nc6",
                string.Join(" ", game.MainLine().Select(n => n.San)));
            Test.True("после чтения мы в начале партии", game.Current.IsRoot);

            Test.Check("партия без заголовков", 2, Pgn.Read("1. d4 d5 *").MainLine().Count);
            Test.Check("номера с точками после хода чёрных", 3,
                Pgn.Read("1. e4 e5 2... Nf3 *").MainLine().Count);
            Test.Check("лишние пробелы и переносы", 4,
                Pgn.Read("1.e4\n  e5\n2.Nf3\tNc6 *").MainLine().Count);

            // Комментарии
            var commented = Pgn.Read("1. e4 {хороший ход} e5 {ответ} *");
            Test.Check("комментарий у первого хода", "хороший ход", commented.MainLine()[0].Comment);
            Test.Check("комментарий у второго хода", "ответ", commented.MainLine()[1].Comment);
            Test.Check("два комментария подряд склеиваются", "первый второй",
                Pgn.Read("1. e4 {первый} {второй} *").MainLine()[0].Comment);
            Test.Check("комментарий до первого хода отбрасывается", 1,
                Pgn.Read("{вступление} 1. e4 *").MainLine().Count);
            Test.Check("незакрытый комментарий не ломает разбор", 1,
                Pgn.Read("1. e4 {без закрывающей скобки *").MainLine().Count);
            Test.Check("комментарий до конца строки", 1, Pgn.Read("1. e4 ; остаток строки\n").MainLine().Count);
            Test.Check("строки с процентом игнорируются", 2, Pgn.Read("% служебная строка\n1. e4 e5 *").MainLine().Count);

            // Знаки оценки
            Test.Check("знак после хода", "!?", Pgn.Read("1. e4!? *").MainLine()[0].Glyph);
            Test.Check("двойной знак", "??", Pgn.Read("1. e4?? *").MainLine()[0].Glyph);
            (string Nag, string Symbol)[] nags =
            {
                ("$1", "!"), ("$2", "?"), ("$3", "!!"), ("$4", "??"), ("$5", "!?"), ("$6", "?!")
            };
            foreach (var (nag, symbol) in nags)
                Test.Check($"числовой знак {nag}", symbol, Pgn.Read($"1. e4 {nag} *").MainLine()[0].Glyph);
            Test.Check("неизвестный числовой знак", null, Pgn.Read("1. e4 $99 *").MainLine()[0].Glyph);

            // Варианты
            var withVariation = Pgn.Read("1. e4 e5 (1... c5 2. Nf3) 2. Nf3 *");
            Test.Check("основная линия при варианте", "e4 e5 Nf3",
                string.Join(" ", withVariation.MainLine().Select(n => n.San)));
            var afterE4 = withVariation.Root.MainChild!;
            Test.Check("вариант добавлен", 2, afterE4.Children.Count);
            Test.Check("первый ход варианта", "c5", afterE4.Children[1].San);
            Test.Check("вариант продолжается", "Nf3", afterE4.Children[1].MainChild!.San);

            var nested = Pgn.Read("1. e4 e5 (1... c5 2. Nf3 d6 (2... Nc6 3. d4)) 2. Nf3 *");
            var sicilian = nested.Root.MainChild!.Children[1];
            Test.Check("вложенный вариант на месте", 2, sicilian.MainChild!.Children.Count);
            Test.Check("ход вложенного варианта", "Nc6", sicilian.MainChild!.Children[1].San);

            Test.Check("нелегальные ходы пропускаются", 1, Pgn.Read("1. e4 Zz9 *").MainLine().Count);

            // Несколько партий в одном файле
            const string two = "[Event \"Первая\"]\n\n1. e4 e5 1-0\n\n[Event \"Вторая\"]\n\n1. d4 d5 0-1\n";
            var all = Pgn.ReadAll(two);
            Test.Check("прочитано две партии", 2, all.Count);
            Test.Check("заголовок первой", "Первая", all[0].Headers["Event"]);
            Test.Check("заголовок второй", "Вторая", all[1].Headers["Event"]);
            Test.Check("ходы второй партии", "d4 d5", string.Join(" ", all[1].MainLine().Select(n => n.San)));
            Test.Check("ограничение количества партий", 1, Pgn.ReadAll(two, 1).Count);
            Test.Check("Read берёт первую партию", "Первая", Pgn.Read(two).Headers["Event"]);

            Test.Throws<FormatException>("пустой текст отвергается", () => Pgn.Read(""));
            Test.Throws<FormatException>("пробелы отвергаются", () => Pgn.Read("   \n  \n"));

            // Партия с расставленной позиции
            var setup = Pgn.Read("[SetUp \"1\"]\n[FEN \"4k3/8/8/8/8/8/4P3/4K3 w - - 0 1\"]\n\n1. e4 *");
            Test.Check("стартовая позиция из заголовка", "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", setup.StartFen);
            Test.Check("ход от расставленной позиции", "e4", setup.MainLine()[0].San);
            Test.Check("испорченный FEN не ломает чтение", Position.StartFen,
                Pgn.Read("[FEN \"мусор\"]\n\n1. e4 *").StartFen);
        });
    }

    private static void RoundTrip()
    {
        Test.Suite("PGN: полный круг", () =>
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

            Test.Check("заголовок сохранился", "Круговая проверка", back.Headers["Event"]);
            Test.Check("основная линия сохранилась", "e4 c5 Nf3 d6",
                string.Join(" ", back.MainLine().Select(n => n.San)));
            Test.Check("комментарий сохранился", "Сицилианская защита", back.MainLine()[1].Comment);
            Test.Check("знак сохранился", "!?", back.MainLine()[1].Glyph);

            var branch = back.Root.MainChild!;
            Test.Check("вариант сохранился", 2, branch.Children.Count);
            Test.Check("ходы варианта", "e5 Nf3 Nc6", string.Join(" ", Walk(branch.Children[1])));

            // Повторная запись даёт тот же текст
            Test.Check("вторая запись совпадает с первой", pgn, Pgn.Write(back));
        });
    }

    private static IEnumerable<string> Walk(MoveNode node)
    {
        for (var n = node; n != null; n = n.MainChild) yield return n.San;
    }

    private static void SampleFile()
    {
        Test.Suite("PGN: партия из папки samples", () =>
        {
            var path = FindSample("opera-game.pgn");
            if (path == null)
            {
                Test.Fail("не найден файл samples/opera-game.pgn");
                return;
            }

            var game = Pgn.Read(File.ReadAllText(path));
            Test.Check("игрок белыми", "Paul Morphy", game.Headers["White"]);
            Test.Check("результат", "1-0", game.Headers["Result"]);
            Test.Check("длина партии", 33, game.MainLine().Count);
            Test.Check("последний ход — мат", "Rd8#", game.MainLine()[^1].San);
            Test.Check("комментарий из файла прочитан", "Чёрные связывают коня, но теряют время.",
                game.MainLine()[5].Comment);
            Test.Check("состояние партии", GameResultState.WhiteWins,
                game.EvaluateState(game.MainLine()[^1]).State);

            var back = Pgn.Read(Pgn.Write(game));
            Test.Check("партия переживает запись и чтение",
                string.Join(" ", game.MainLine().Select(n => n.San)),
                string.Join(" ", back.MainLine().Select(n => n.San)));
        });
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
