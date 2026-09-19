using ChessEmulator.Chess;

namespace ChessEmulator.UI;

/// <summary>
/// Формулировки результата партии. Одно место на всю программу: строка состояния
/// и плашка поверх доски не должны разъехаться в словах.
/// </summary>
internal static class GameResultText
{
    public static bool IsFinished(GameResultState state) => state != GameResultState.InProgress;

    /// <summary>
    /// Нужна ли на этом ходу плашка с результатом. Только в конце линии: посреди партии
    /// и в незаконченном варианте она закрывала бы доску без повода.
    /// </summary>
    public static bool ShouldShowBanner(GameResultState state, MoveNode node) =>
        IsFinished(state) && node.Children.Count == 0;

    /// <summary>Причина окончания: «Мат», «Пат», «Правило 50 ходов». Для незаконченной партии — пусто.</summary>
    public static string Reason(GameEndReason reason) => reason switch
    {
        GameEndReason.Checkmate => "Мат",
        GameEndReason.Stalemate => "Пат",
        GameEndReason.InsufficientMaterial => "Недостаточно материала",
        GameEndReason.FiftyMoveRule => "Правило 50 ходов",
        GameEndReason.ThreefoldRepetition => "Троекратное повторение",
        _ => string.Empty
    };

    /// <summary>Заголовок плашки прописными: «МАТ · ПОБЕДА БЕЛЫХ», «НИЧЬЯ · ПАТ».</summary>
    public static string Headline(GameResultState state, GameEndReason reason)
    {
        var outcome = state switch
        {
            GameResultState.WhiteWins => "Победа белых",
            GameResultState.BlackWins => "Победа чёрных",
            GameResultState.Draw => "Ничья",
            _ => string.Empty
        };
        if (outcome.Length == 0) return string.Empty;

        var why = Reason(reason);
        if (why.Length == 0) return outcome.ToUpperInvariant();

        // У победы главное — мат, у ничьей — сам факт ничьей; причина идёт второй.
        var text = state == GameResultState.Draw ? outcome + " · " + why : why + " · " + outcome;
        return text.ToUpperInvariant();
    }

    /// <summary>Счёт партии: «1–0», «0–1», «½–½». У незаконченной партии счёта нет.</summary>
    public static string Score(GameResultState state) => state switch
    {
        GameResultState.WhiteWins => "1–0",
        GameResultState.BlackWins => "0–1",
        GameResultState.Draw => "½–½",
        _ => string.Empty
    };

    /// <summary>Надпись в строке состояния внизу окна.</summary>
    public static string StatusLine(GameResultState state, GameEndReason reason, Position position) => state switch
    {
        GameResultState.WhiteWins or GameResultState.BlackWins =>
            (reason == GameEndReason.Checkmate ? "Мат. " : string.Empty) +
            (state == GameResultState.WhiteWins ? "Победа белых (1–0)" : "Победа чёрных (0–1)"),
        GameResultState.Draw => Reason(reason) is { Length: > 0 } why ? why + " — ничья" : "Ничья",
        _ => position.IsInCheck()
            ? position.SideToMove == PieceColor.White ? "Шах белому королю" : "Шах чёрному королю"
            : position.SideToMove == PieceColor.White ? "Ход белых" : "Ход чёрных"
    };

    /// <summary>
    /// Цвет надписи в строке состояния. Тёмная полоса внизу окна не терпит цвета плашки
    /// (белая плашка на ней сливается, чёрная пропадает), поэтому у всякого финала
    /// он один — янтарный: «партия окончена, смотрите на доску».
    /// </summary>
    public static Color StatusColor(GameResultState state) =>
        IsFinished(state) ? Color.FromArgb(220, 180, 90) : Color.Gainsboro;

    /// <summary>
    /// Оформление плашки: цвет самой плашки показывает, кто выиграл, — белая у белых,
    /// почти чёрная у чёрных, тёмная с янтарной каймой у ничьей.
    /// </summary>
    public static BoardBannerStyle BannerStyle(GameResultState state) => state switch
    {
        GameResultState.WhiteWins => new BoardBannerStyle(
            Color.FromArgb(237, 233, 224), Color.FromArgb(30, 30, 32), Color.FromArgb(140, 105, 70)),
        GameResultState.BlackWins => new BoardBannerStyle(
            Color.FromArgb(26, 26, 28), Color.FromArgb(235, 235, 238), Color.FromArgb(190, 190, 196)),
        _ => new BoardBannerStyle(
            Color.FromArgb(34, 34, 38), Color.FromArgb(235, 235, 238), Color.FromArgb(220, 180, 90))
    };
}
