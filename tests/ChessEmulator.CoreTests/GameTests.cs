using ChessEmulator.Chess;
using Xunit;

namespace ChessEmulator.CoreTests;

/// <summary>Партия: дерево ходов, навигация, варианты, определение результата.</summary>
public class GameTests
{
    private static Game GameWith(params string[] sans)
    {
        var game = new Game();
        foreach (var san in sans)
            if (!game.TryAddSan(san, out _))
                throw new InvalidOperationException($"Ход {san} не разобрался.");
        return game;
    }

    [Fact(DisplayName = "Партия: узлы дерева")]
    public void Nodes()
    {
        var game = GameWith("e4", "e5", "Nf3", "Nc6");
        var line = game.MainLine();

        Assert.Equal(4, line.Count);  // длина основной линии
        Assert.Equal("e4 e5 Nf3 Nc6", string.Join(" ", line.Select(n => n.San)));  // записи ходов
        Assert.Equal(1, line[0].MoveNumber);  // номер первого хода
        Assert.Equal(2, line[2].MoveNumber);  // номер третьего полухода
        Assert.True(line[0].IsWhiteMove, "первый ход — белых");
        Assert.False(line[1].IsWhiteMove, "второй ход — чёрных");
        Assert.Equal(PieceColor.Black, line[1].SideMoved);  // цвет второго хода
        Assert.Equal(0, game.Root.Ply);  // полуход корня
        Assert.Equal(4, line[3].Ply);  // полуход последнего хода
        Assert.True(game.Root.IsRoot, "корень — корень");
        Assert.False(line[0].IsRoot, "ход — не корень");
        Assert.True(ReferenceEquals(line[0].Parent, game.Root), "родитель первого хода — корень");
        // позиция после e4 e5 Nf3 Nc6
        Assert.Equal("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3", line[3].Position.ToFen());
        Assert.Equal(4, line[3].PathFromRoot().Count());  // путь от корня
        // ходы в UCI до текущего
        Assert.Equal("e2e4 e7e5 g1f3 b8c6", string.Join(" ", game.UciMovesToCurrent()));

        Assert.False(game.TryAddSan("Qz9", out var missing), "несуществующий ход не добавляется");
        Assert.Null(missing);  // узел не создан
        Assert.Equal(4, game.MainLine().Count);  // дерево не изменилось

        // Повторное добавление того же хода не создаёт второй узел
        game.GoToStart();
        game.TryAddSan("e4", out _);
        Assert.Single(game.Root.Children);  // повтор хода не ветвится
        Assert.Equal("e4", game.Current.San);  // после повтора мы на этом ходу
    }

    [Fact(DisplayName = "Партия: навигация")]
    public void Navigation()
    {
        var game = GameWith("e4", "e5", "Nf3");
        Assert.Equal("Nf3", game.Current.San);  // после добавления мы в конце

        game.GoToStart();
        Assert.True(game.Current.IsRoot, "в начале — корень");
        Assert.Equal(Position.StartFen, game.CurrentPosition.ToFen());  // позиция начала
        Assert.False(game.GoBack(), "назад из корня нельзя");

        Assert.True(game.GoForward(), "шаг вперёд");
        Assert.Equal("e4", game.Current.San);  // после шага вперёд
        game.GoToEnd();
        Assert.Equal("Nf3", game.Current.San);  // в конце партии
        Assert.False(game.GoForward(), "вперёд из конца нельзя");
        Assert.True(game.GoBack(), "шаг назад");
        Assert.Equal("e5", game.Current.San);  // после шага назад

        game.GoTo(game.MainLine()[0]);
        Assert.Equal("e4", game.Current.San);  // переход к узлу

        var changes = 0;
        game.Changed += (_, _) => changes++;
        game.GoToEnd();
        game.GoBack();
        game.TryAddSan("Nc3", out _);
        Assert.Equal(3, changes);  // событие Changed приходит на каждое действие
    }

    [Fact(DisplayName = "Партия: варианты")]
    public void Variations()
    {
        var game = GameWith("e4", "e5", "Nf3");
        game.GoToStart();
        game.GoForward();              // после 1.e4
        game.TryAddSan("c5", out _);   // вариант к 1...e5
        game.TryAddSan("Nf3", out _);

        var first = game.Root.MainChild!;
        Assert.Equal(2, first.Children.Count);  // у хода e4 два продолжения
        Assert.Equal("e5", first.MainChild!.San);  // основная линия осталась прежней
        Assert.Equal("c5", first.Children[1].San);  // вариант добавлен вторым
        Assert.Equal(3, game.MainLine().Count);  // основная линия не удлинилась

        // Поднимаем вариант в основную линию
        game.GoTo(first.Children[1]);
        Assert.True(game.PromoteToMainLine(), "вариант поднят");
        Assert.Equal("c5", first.MainChild!.San);  // вариант стал основным
        Assert.Equal("e5", first.Children[1].San);  // прежняя основная линия стала вариантом
        Assert.False(game.PromoteToMainLine(), "повторное поднятие ничего не меняет");

        // Вложенный вариант: поднимается вся цепочка
        var deep = GameWith("d4", "d5", "c4");
        deep.GoToStart();
        deep.GoForward();
        deep.TryAddSan("Nf6", out _);   // вариант на первом ходу
        deep.TryAddSan("c4", out _);
        deep.TryAddSan("e6", out _);    // продолжение внутри варианта
        Assert.True(deep.PromoteToMainLine(), "вложенный вариант поднимается");
        // новая основная линия
        Assert.Equal("d4 Nf6 c4 e6", string.Join(" ", deep.MainLine().Select(n => n.San)));
    }

    [Fact(DisplayName = "Партия: удаление ходов")]
    public void Editing()
    {
        var game = GameWith("e4", "e5", "Nf3", "Nc6", "Bb5");
        game.GoTo(game.MainLine()[2]);   // на 2.Nf3
        Assert.True(game.TruncateAfterCurrent(), "обрезка после текущего");
        Assert.Equal(3, game.MainLine().Count);  // линия обрезана
        Assert.False(game.TruncateAfterCurrent(), "обрезать нечего");

        Assert.True(game.DeleteCurrent(), "текущий ход удалён");
        Assert.Equal("e5", game.Current.San);  // после удаления мы на родителе
        Assert.Equal(2, game.MainLine().Count);  // линия стала короче

        game.GoToStart();
        Assert.False(game.DeleteCurrent(), "корень удалить нельзя");

        // Удаление варианта не трогает основную линию
        var withVar = GameWith("e4", "e5");
        withVar.GoTo(withVar.Root.MainChild!);
        withVar.TryAddSan("c5", out var variation);
        withVar.GoTo(variation!);
        Assert.True(withVar.DeleteCurrent(), "вариант удалён");
        Assert.Single(withVar.Root.MainChild!.Children);  // у хода e4 снова одно продолжение
        // основная линия цела
        Assert.Equal("e4 e5", string.Join(" ", withVar.MainLine().Select(n => n.San)));

        // Комментарии и оценки живут на узле
        var node = withVar.MainLine()[1];
        node.Comment = "открытый дебют";
        node.Glyph = "!?";
        node.EvalCp = 24;
        node.BestReply = "g1f3";
        Assert.Equal("открытый дебют", node.Comment);  // комментарий сохранён
        Assert.Equal("!?", node.Glyph);  // знак сохранён
        Assert.Equal(24, node.EvalCp);  // оценка сохранена
        Assert.Equal("g1f3", node.BestReply);  // лучший ответ сохранён
    }

    [Fact(DisplayName = "Партия: результат")]
    public void Results()
    {
        var game = new Game();
        Assert.Equal("*", game.Headers["Result"]);  // партия начинается без результата
        Assert.Equal(GameResultState.InProgress, game.EvaluateState(game.Root).State);  // состояние в начале

        // Детский мат
        var mate = GameWith("f3", "e5", "g4", "Qh4");
        var (state, reason) = mate.EvaluateState(mate.Current);
        Assert.Equal(GameResultState.BlackWins, state);  // мат: победа чёрных
        Assert.Equal(GameEndReason.Checkmate, reason);  // мат: причина
        Assert.Equal("0-1", mate.Headers["Result"]);  // заголовок результата
        Assert.Equal("Qh4#", mate.Current.San);  // последний ход записан с решёткой

        // Мат белыми
        var whiteMate = GameWith("e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7");
        Assert.Equal(GameResultState.WhiteWins, whiteMate.EvaluateState(whiteMate.Current).State);  // мат белыми
        Assert.Equal("1-0", whiteMate.Headers["Result"]);  // заголовок 1-0

        // Пат
        var stalemate = new Game("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        var patState = stalemate.EvaluateState(stalemate.Root);
        Assert.Equal(GameResultState.Draw, patState.State);  // пат: ничья
        Assert.Equal(GameEndReason.Stalemate, patState.Reason);  // пат: причина

        // Недостаток материала
        var material = new Game("4k3/8/8/8/8/8/8/4KB2 w - - 0 1");
        // недостаток материала
        Assert.Equal(GameEndReason.InsufficientMaterial, material.EvaluateState(material.Root).Reason);

        // Правило 50 ходов
        var fifty = new Game("4k3/8/8/8/8/8/R7/4K3 w - - 100 80");
        // правило 50 ходов
        Assert.Equal(GameEndReason.FiftyMoveRule, fifty.EvaluateState(fifty.Root).Reason);
        var almost = new Game("4k3/8/8/8/8/8/R7/4K3 w - - 99 80");
        // 99 полуходов — партия идёт
        Assert.Equal(GameResultState.InProgress, almost.EvaluateState(almost.Root).State);

        // Троекратное повторение
        var repetition = GameWith("Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8");
        Assert.True(repetition.IsThreefoldRepetition(repetition.Current), "троекратное повторение найдено");
        // причина ничьей
        Assert.Equal(GameEndReason.ThreefoldRepetition, repetition.EvaluateState(repetition.Current).Reason);

        var twofold = GameWith("Nf3", "Nf6", "Ng1", "Ng8");
        Assert.False(twofold.IsThreefoldRepetition(twofold.Current), "двукратного повторения мало");
    }

    [Fact(DisplayName = "Партия: старт с расставленной позиции")]
    public void Setup()
    {
        const string fen = "4k3/8/8/8/3Q4/8/8/4K3 b - - 0 7";
        var game = new Game(fen);

        Assert.Equal(fen, game.StartFen);  // стартовая позиция сохранена
        Assert.Equal(fen, game.Headers["FEN"]);  // заголовок FEN
        Assert.Equal("1", game.Headers["SetUp"]);  // заголовок SetUp
        Assert.Equal(fen, game.Root.Position.ToFen());  // позиция корня
        Assert.True(game.TryAddSan("Ke7", out _), "ходы находятся");
        Assert.Equal(7, game.MainLine()[0].MoveNumber);  // номер хода берётся из FEN

        var plain = new Game();
        Assert.False(plain.Headers.ContainsKey("FEN"), "у обычной партии нет заголовка FEN");

        // Сброс к начальной позиции убирает заголовки расстановки
        game.Reset();
        Assert.Equal(Position.StartFen, game.StartFen);  // после сброса позиция начальная
        Assert.False(game.Headers.ContainsKey("FEN"), "после сброса нет FEN");
        Assert.False(game.Headers.ContainsKey("SetUp"), "после сброса нет SetUp");
        Assert.Empty(game.MainLine());  // после сброса дерево пусто
        Assert.Equal("*", game.Headers["Result"]);  // после сброса результат сброшен

        game.Reset(fen);
        Assert.Equal("1", game.Headers["SetUp"]);  // сброс в позицию возвращает заголовки

        Assert.Equal(Position.StartFen, new Game("   ").StartFen);  // пустая строка FEN — начальная позиция
    }
}
