using ChessEmulator.Chess;
using ChessEmulator.UI;
using Xunit;

namespace ChessEmulator.UiTests;

/// <summary>Формулировки результата партии: строка состояния и текст плашки.</summary>
public class GameResultTextTests
{
    [Fact(DisplayName = "Результат: текст плашки")]
    public void Banner()
    {
        Assert.Equal("МАТ · ПОБЕДА БЕЛЫХ", GameResultText.Headline(GameResultState.WhiteWins, GameEndReason.Checkmate));
        Assert.Equal("МАТ · ПОБЕДА ЧЁРНЫХ", GameResultText.Headline(GameResultState.BlackWins, GameEndReason.Checkmate));
        Assert.Equal("НИЧЬЯ · ПАТ", GameResultText.Headline(GameResultState.Draw, GameEndReason.Stalemate));
        Assert.Equal("НИЧЬЯ · НЕДОСТАТОЧНО МАТЕРИАЛА",
            GameResultText.Headline(GameResultState.Draw, GameEndReason.InsufficientMaterial));
        Assert.Equal("НИЧЬЯ · ПРАВИЛО 50 ХОДОВ",
            GameResultText.Headline(GameResultState.Draw, GameEndReason.FiftyMoveRule));
        Assert.Equal("НИЧЬЯ · ТРОЕКРАТНОЕ ПОВТОРЕНИЕ",
            GameResultText.Headline(GameResultState.Draw, GameEndReason.ThreefoldRepetition));

        // Без причины остаётся один исход, заголовок не разваливается
        Assert.Equal("НИЧЬЯ", GameResultText.Headline(GameResultState.Draw, GameEndReason.None));

        Assert.Equal("1–0", GameResultText.Score(GameResultState.WhiteWins));
        Assert.Equal("0–1", GameResultText.Score(GameResultState.BlackWins));
        Assert.Equal("½–½", GameResultText.Score(GameResultState.Draw));

        // Незаконченной партии плашка не полагается
        Assert.False(GameResultText.IsFinished(GameResultState.InProgress));
        Assert.Equal(string.Empty, GameResultText.Headline(GameResultState.InProgress, GameEndReason.None));
        Assert.Equal(string.Empty, GameResultText.Score(GameResultState.InProgress));
        foreach (var state in new[] { GameResultState.WhiteWins, GameResultState.BlackWins, GameResultState.Draw })
            Assert.True(GameResultText.IsFinished(state));

        // Заголовок всегда прописными: плашка читается издалека
        foreach (var reason in Enum.GetValues<GameEndReason>())
        {
            var headline = GameResultText.Headline(GameResultState.Draw, reason);
            Assert.Equal(headline.ToUpperInvariant(), headline);
        }
    }

    [Fact(DisplayName = "Результат: цвета плашки")]
    public void BannerStyle()
    {
        var white = GameResultText.BannerStyle(GameResultState.WhiteWins);
        var black = GameResultText.BannerStyle(GameResultState.BlackWins);
        var draw = GameResultText.BannerStyle(GameResultState.Draw);

        // Кто выиграл, тот и красит плашку: у белых она светлая, у чёрных тёмная
        Assert.True(Brightness(white.Plate) > Brightness(black.Plate));
        Assert.NotEqual(draw.Accent, white.Accent);

        // Текст всегда читается на своей плашке
        foreach (var style in new[] { white, black, draw })
        {
            Assert.True(Math.Abs(Brightness(style.Plate) - Brightness(style.Text)) > 100, "текст виден на плашке");
            Assert.True(Math.Abs(Brightness(style.Plate) - Brightness(style.Accent)) > 60, "кайма видна на плашке");
        }

        // В тёмной строке состояния финал подсвечен одинаково, а идущая партия — нет
        var strip = Color.FromArgb(45, 45, 48);
        Assert.Equal(Color.Gainsboro, GameResultText.StatusColor(GameResultState.InProgress));
        foreach (var state in new[] { GameResultState.WhiteWins, GameResultState.BlackWins, GameResultState.Draw })
        {
            Assert.NotEqual(Color.Gainsboro, GameResultText.StatusColor(state));
            Assert.True(Brightness(GameResultText.StatusColor(state)) - Brightness(strip) > 60,
                "надпись о финале видна на полосе состояния");
        }
    }

    [Fact(DisplayName = "Результат: строка состояния")]
    public void StatusLine()
    {
        var start = Position.FromFen(Position.StartFen);

        Assert.Equal("Мат. Победа белых (1–0)",
            GameResultText.StatusLine(GameResultState.WhiteWins, GameEndReason.Checkmate, start));
        Assert.Equal("Мат. Победа чёрных (0–1)",
            GameResultText.StatusLine(GameResultState.BlackWins, GameEndReason.Checkmate, start));
        Assert.Equal("Пат — ничья",
            GameResultText.StatusLine(GameResultState.Draw, GameEndReason.Stalemate, start));
        Assert.Equal("Недостаточно материала — ничья",
            GameResultText.StatusLine(GameResultState.Draw, GameEndReason.InsufficientMaterial, start));
        Assert.Equal("Правило 50 ходов — ничья",
            GameResultText.StatusLine(GameResultState.Draw, GameEndReason.FiftyMoveRule, start));
        Assert.Equal("Троекратное повторение — ничья",
            GameResultText.StatusLine(GameResultState.Draw, GameEndReason.ThreefoldRepetition, start));
        Assert.Equal("Ничья", GameResultText.StatusLine(GameResultState.Draw, GameEndReason.None, start));

        // Пока партия идёт, строка говорит про очередь хода и шах
        Assert.Equal("Ход белых", GameResultText.StatusLine(GameResultState.InProgress, GameEndReason.None, start));
        var black = Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1");
        Assert.Equal("Ход чёрных", GameResultText.StatusLine(GameResultState.InProgress, GameEndReason.None, black));

        var whiteInCheck = Position.FromFen("4k3/8/8/8/8/8/8/4K2r w - - 0 1");
        Assert.Equal("Шах белому королю",
            GameResultText.StatusLine(GameResultState.InProgress, GameEndReason.None, whiteInCheck));
        var blackInCheck = Position.FromFen("4k2R/8/8/8/8/8/8/4K3 b - - 0 1");
        Assert.Equal("Шах чёрному королю",
            GameResultText.StatusLine(GameResultState.InProgress, GameEndReason.None, blackInCheck));
    }

    [Fact(DisplayName = "Результат: когда нужна плашка")]
    public void WhenToShow()
    {
        // Детский мат — плашка нужна
        var game = GameWith("e4", "e5", "Bc4", "Nc6", "Qh5", "Nf6", "Qxf7#");
        var mate = game.Current;
        Assert.True(Show(game, mate));

        // По дороге к мату плашки нет
        for (var node = mate.Parent; node != null; node = node.Parent)
            Assert.False(Show(game, node), "посреди партии плашки нет");

        // Вариант, который никуда не привёл, доску не закрывает
        game.GoTo(mate.Parent!.Parent!);
        Assert.True(game.TryAddSan("Nd4", out var side));
        Assert.False(Show(game, side!));

        // Мат в варианте — тоже финал
        game.GoTo(mate.Parent!);
        Assert.True(game.TryAddSan("Qxf7#", out var inVariation));
        Assert.True(Show(game, inVariation!));

        // Пат
        var stalemate = new Game("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        Assert.True(Show(stalemate, stalemate.Current));

        // Ничья по недостатку материала перестаёт показываться, когда партия на ней не кончилась
        var kings = new Game("7k/8/6K1/8/8/8/8/8 w - - 0 1");
        Assert.True(Show(kings, kings.Current));
        var root = kings.Current;
        Assert.True(kings.TryAddSan("Kf6", out _));
        Assert.False(Show(kings, root), "партия продолжилась — плашки на прежнем ходу нет");
        Assert.True(Show(kings, kings.Current));
    }

    private static bool Show(Game game, MoveNode node)
    {
        var (state, _) = game.EvaluateState(node);
        return GameResultText.ShouldShowBanner(state, node);
    }

    private static Game GameWith(params string[] sans)
    {
        var game = new Game();
        foreach (var san in sans)
            if (!game.TryAddSan(san, out _))
                throw new InvalidOperationException($"Ход {san} не разобрался.");
        return game;
    }

    private static int Brightness(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
}
