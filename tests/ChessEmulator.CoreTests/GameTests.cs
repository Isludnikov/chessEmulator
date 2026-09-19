using ChessEmulator.Chess;
using ChessEmulator.TestKit;

namespace ChessEmulator.CoreTests;

/// <summary>Партия: дерево ходов, навигация, варианты, определение результата.</summary>
internal static class GameTests
{
    public static void Run()
    {
        Nodes();
        Navigation();
        Variations();
        Editing();
        Results();
        Setup();
    }

    private static Game GameWith(params string[] sans)
    {
        var game = new Game();
        foreach (var san in sans)
            if (!game.TryAddSan(san, out _))
                throw new InvalidOperationException($"Ход {san} не разобрался.");
        return game;
    }

    private static void Nodes()
    {
        Test.Suite("Партия: узлы дерева", () =>
        {
            var game = GameWith("e4", "e5", "Nf3", "Nc6");
            var line = game.MainLine();

            Test.Check("длина основной линии", 4, line.Count);
            Test.Check("записи ходов", "e4 e5 Nf3 Nc6", string.Join(" ", line.Select(n => n.San)));
            Test.Check("номер первого хода", 1, line[0].MoveNumber);
            Test.Check("номер третьего полухода", 2, line[2].MoveNumber);
            Test.True("первый ход — белых", line[0].IsWhiteMove);
            Test.False("второй ход — чёрных", line[1].IsWhiteMove);
            Test.Check("цвет второго хода", PieceColor.Black, line[1].SideMoved);
            Test.Check("полуход корня", 0, game.Root.Ply);
            Test.Check("полуход последнего хода", 4, line[3].Ply);
            Test.True("корень — корень", game.Root.IsRoot);
            Test.False("ход — не корень", line[0].IsRoot);
            Test.True("родитель первого хода — корень", ReferenceEquals(line[0].Parent, game.Root));
            Test.Check("позиция после e4 e5 Nf3 Nc6",
                "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3", line[3].Position.ToFen());
            Test.Check("путь от корня", 4, line[3].PathFromRoot().Count());
            Test.Check("ходы в UCI до текущего", "e2e4 e7e5 g1f3 b8c6",
                string.Join(" ", game.UciMovesToCurrent()));

            Test.False("несуществующий ход не добавляется", game.TryAddSan("Qz9", out var missing));
            Test.Check("узел не создан", null, missing);
            Test.Check("дерево не изменилось", 4, game.MainLine().Count);

            // Повторное добавление того же хода не создаёт второй узел
            game.GoToStart();
            game.TryAddSan("e4", out _);
            Test.Check("повтор хода не ветвится", 1, game.Root.Children.Count);
            Test.Check("после повтора мы на этом ходу", "e4", game.Current.San);
        });
    }

    private static void Navigation()
    {
        Test.Suite("Партия: навигация", () =>
        {
            var game = GameWith("e4", "e5", "Nf3");
            Test.Check("после добавления мы в конце", "Nf3", game.Current.San);

            game.GoToStart();
            Test.True("в начале — корень", game.Current.IsRoot);
            Test.Check("позиция начала", Position.StartFen, game.CurrentPosition.ToFen());
            Test.False("назад из корня нельзя", game.GoBack());

            Test.True("шаг вперёд", game.GoForward());
            Test.Check("после шага вперёд", "e4", game.Current.San);
            game.GoToEnd();
            Test.Check("в конце партии", "Nf3", game.Current.San);
            Test.False("вперёд из конца нельзя", game.GoForward());
            Test.True("шаг назад", game.GoBack());
            Test.Check("после шага назад", "e5", game.Current.San);

            game.GoTo(game.MainLine()[0]);
            Test.Check("переход к узлу", "e4", game.Current.San);

            var changes = 0;
            game.Changed += (_, _) => changes++;
            game.GoToEnd();
            game.GoBack();
            game.TryAddSan("Nc3", out _);
            Test.Check("событие Changed приходит на каждое действие", 3, changes);
        });
    }

    private static void Variations()
    {
        Test.Suite("Партия: варианты", () =>
        {
            var game = GameWith("e4", "e5", "Nf3");
            game.GoToStart();
            game.GoForward();              // после 1.e4
            game.TryAddSan("c5", out _);   // вариант к 1...e5
            game.TryAddSan("Nf3", out _);

            var first = game.Root.MainChild!;
            Test.Check("у хода e4 два продолжения", 2, first.Children.Count);
            Test.Check("основная линия осталась прежней", "e5", first.MainChild!.San);
            Test.Check("вариант добавлен вторым", "c5", first.Children[1].San);
            Test.Check("основная линия не удлинилась", 3, game.MainLine().Count);

            // Поднимаем вариант в основную линию
            game.GoTo(first.Children[1]);
            Test.True("вариант поднят", game.PromoteToMainLine());
            Test.Check("вариант стал основным", "c5", first.MainChild!.San);
            Test.Check("прежняя основная линия стала вариантом", "e5", first.Children[1].San);
            Test.False("повторное поднятие ничего не меняет", game.PromoteToMainLine());

            // Вложенный вариант: поднимается вся цепочка
            var deep = GameWith("d4", "d5", "c4");
            deep.GoToStart();
            deep.GoForward();
            deep.TryAddSan("Nf6", out _);   // вариант на первом ходу
            deep.TryAddSan("c4", out _);
            deep.TryAddSan("e6", out _);    // продолжение внутри варианта
            Test.True("вложенный вариант поднимается", deep.PromoteToMainLine());
            Test.Check("новая основная линия", "d4 Nf6 c4 e6",
                string.Join(" ", deep.MainLine().Select(n => n.San)));
        });
    }

    private static void Editing()
    {
        Test.Suite("Партия: удаление ходов", () =>
        {
            var game = GameWith("e4", "e5", "Nf3", "Nc6", "Bb5");
            game.GoTo(game.MainLine()[2]);   // на 2.Nf3
            Test.True("обрезка после текущего", game.TruncateAfterCurrent());
            Test.Check("линия обрезана", 3, game.MainLine().Count);
            Test.False("обрезать нечего", game.TruncateAfterCurrent());

            Test.True("текущий ход удалён", game.DeleteCurrent());
            Test.Check("после удаления мы на родителе", "e5", game.Current.San);
            Test.Check("линия стала короче", 2, game.MainLine().Count);

            game.GoToStart();
            Test.False("корень удалить нельзя", game.DeleteCurrent());

            // Удаление варианта не трогает основную линию
            var withVar = GameWith("e4", "e5");
            withVar.GoTo(withVar.Root.MainChild!);
            withVar.TryAddSan("c5", out var variation);
            withVar.GoTo(variation!);
            Test.True("вариант удалён", withVar.DeleteCurrent());
            Test.Check("у хода e4 снова одно продолжение", 1, withVar.Root.MainChild!.Children.Count);
            Test.Check("основная линия цела", "e4 e5",
                string.Join(" ", withVar.MainLine().Select(n => n.San)));

            // Комментарии и оценки живут на узле
            var node = withVar.MainLine()[1];
            node.Comment = "открытый дебют";
            node.Glyph = "!?";
            node.EvalCp = 24;
            node.BestReply = "g1f3";
            Test.Check("комментарий сохранён", "открытый дебют", node.Comment);
            Test.Check("знак сохранён", "!?", node.Glyph);
            Test.Check("оценка сохранена", 24, node.EvalCp);
            Test.Check("лучший ответ сохранён", "g1f3", node.BestReply);
        });
    }

    private static void Results()
    {
        Test.Suite("Партия: результат", () =>
        {
            var game = new Game();
            Test.Check("партия начинается без результата", "*", game.Headers["Result"]);
            Test.Check("состояние в начале", GameResultState.InProgress, game.EvaluateState(game.Root).State);

            // Детский мат
            var mate = GameWith("f3", "e5", "g4", "Qh4");
            var (state, reason) = mate.EvaluateState(mate.Current);
            Test.Check("мат: победа чёрных", GameResultState.BlackWins, state);
            Test.Check("мат: причина", GameEndReason.Checkmate, reason);
            Test.Check("заголовок результата", "0-1", mate.Headers["Result"]);
            Test.Check("последний ход записан с решёткой", "Qh4#", mate.Current.San);

            // Мат белыми
            var whiteMate = GameWith("e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7");
            Test.Check("мат белыми", GameResultState.WhiteWins, whiteMate.EvaluateState(whiteMate.Current).State);
            Test.Check("заголовок 1-0", "1-0", whiteMate.Headers["Result"]);

            // Пат
            var stalemate = new Game("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
            var patState = stalemate.EvaluateState(stalemate.Root);
            Test.Check("пат: ничья", GameResultState.Draw, patState.State);
            Test.Check("пат: причина", GameEndReason.Stalemate, patState.Reason);

            // Недостаток материала
            var material = new Game("4k3/8/8/8/8/8/8/4KB2 w - - 0 1");
            Test.Check("недостаток материала", GameEndReason.InsufficientMaterial,
                material.EvaluateState(material.Root).Reason);

            // Правило 50 ходов
            var fifty = new Game("4k3/8/8/8/8/8/R7/4K3 w - - 100 80");
            Test.Check("правило 50 ходов", GameEndReason.FiftyMoveRule,
                fifty.EvaluateState(fifty.Root).Reason);
            var almost = new Game("4k3/8/8/8/8/8/R7/4K3 w - - 99 80");
            Test.Check("99 полуходов — партия идёт", GameResultState.InProgress,
                almost.EvaluateState(almost.Root).State);

            // Троекратное повторение
            var repetition = GameWith("Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8");
            Test.True("троекратное повторение найдено", repetition.IsThreefoldRepetition(repetition.Current));
            Test.Check("причина ничьей", GameEndReason.ThreefoldRepetition,
                repetition.EvaluateState(repetition.Current).Reason);

            var twofold = GameWith("Nf3", "Nf6", "Ng1", "Ng8");
            Test.False("двукратного повторения мало", twofold.IsThreefoldRepetition(twofold.Current));
        });
    }

    private static void Setup()
    {
        Test.Suite("Партия: старт с расставленной позиции", () =>
        {
            const string fen = "4k3/8/8/8/3Q4/8/8/4K3 b - - 0 7";
            var game = new Game(fen);

            Test.Check("стартовая позиция сохранена", fen, game.StartFen);
            Test.Check("заголовок FEN", fen, game.Headers["FEN"]);
            Test.Check("заголовок SetUp", "1", game.Headers["SetUp"]);
            Test.Check("позиция корня", fen, game.Root.Position.ToFen());
            Test.True("ходы находятся", game.TryAddSan("Ke7", out _));
            Test.Check("номер хода берётся из FEN", 7, game.MainLine()[0].MoveNumber);

            var plain = new Game();
            Test.False("у обычной партии нет заголовка FEN", plain.Headers.ContainsKey("FEN"));

            // Сброс к начальной позиции убирает заголовки расстановки
            game.Reset();
            Test.Check("после сброса позиция начальная", Position.StartFen, game.StartFen);
            Test.False("после сброса нет FEN", game.Headers.ContainsKey("FEN"));
            Test.False("после сброса нет SetUp", game.Headers.ContainsKey("SetUp"));
            Test.Check("после сброса дерево пусто", 0, game.MainLine().Count);
            Test.Check("после сброса результат сброшен", "*", game.Headers["Result"]);

            game.Reset(fen);
            Test.Check("сброс в позицию возвращает заголовки", "1", game.Headers["SetUp"]);

            Test.Check("пустая строка FEN — начальная позиция", Position.StartFen, new Game("   ").StartFen);
        });
    }
}
