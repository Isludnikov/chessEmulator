using ChessEmulator.Chess;
using ChessEmulator.UI;
using Xunit;

namespace ChessEmulator.UiTests;

/// <summary>Выбор фигуры при превращении пешки — без показа окна.</summary>
public class PromotionDialogTests
{
    private const int Cell = 68;

    /// <summary>Середина клетки с фигурой номер index.</summary>
    private static Point Center(int index) => new(index * Cell + Cell / 2, Cell / 2);

    [WinFormsFact(DisplayName = "Превращение: выбор мышью")]
    public void Выбор()
    {
        (int Index, PieceType Expected)[] cases =
        {
            (0, PieceType.Queen), (1, PieceType.Rook), (2, PieceType.Bishop), (3, PieceType.Knight)
        };

        foreach (var (index, expected) in cases)
        {
            using var dialog = new PromotionDialog(PieceColor.White);
            UiHarness.MouseClick(dialog, Center(index));
            Assert.Equal(expected, dialog.Selected);  // выбрана фигура из клетки {index}
            Assert.Equal(DialogResult.OK, dialog.DialogResult);  // выбор принят
        }
    }

    [WinFormsFact(DisplayName = "Превращение: щелчок мимо фигур ничего не выбирает")]
    public void ЩелчокМимо()
    {
        // Правая и средняя кнопки — не выбор: превращение необратимо, случайный щелчок
        // не должен ставить ферзя.
        using var rightClick = new PromotionDialog(PieceColor.White);
        UiHarness.MouseClick(rightClick, Center(1), MouseButtons.Right);
        Assert.Equal(DialogResult.None, rightClick.DialogResult);  // правая кнопка не выбирает

        using var middleClick = new PromotionDialog(PieceColor.White);
        UiHarness.MouseClick(middleClick, Center(1), MouseButtons.Middle);
        Assert.Equal(DialogResult.None, middleClick.DialogResult);  // средняя тоже

        // Точка левее доски: целочисленное деление округляет к нулю, и без проверки
        // она попала бы в первую клетку, то есть в ферзя.
        using var leftOfBoard = new PromotionDialog(PieceColor.White);
        UiHarness.MouseClick(leftOfBoard, new Point(-10, Cell / 2));
        Assert.Equal(DialogResult.None, leftOfBoard.DialogResult);  // слева от доски выбора нет

        using var belowBoard = new PromotionDialog(PieceColor.White);
        UiHarness.MouseClick(belowBoard, new Point(Cell / 2, Cell + 20));
        Assert.Equal(DialogResult.None, belowBoard.DialogResult);  // ниже доски тоже

        using var rightOfBoard = new PromotionDialog(PieceColor.White);
        UiHarness.MouseClick(rightOfBoard, new Point(Cell * 4 + 10, Cell / 2));
        Assert.Equal(DialogResult.None, rightOfBoard.DialogResult);  // и правее последней клетки
    }

    [WinFormsFact(DisplayName = "Превращение: выбор с клавиатуры")]
    public void Клавиатура()
    {
        (Keys Key, PieceType Expected)[] cases =
        {
            (Keys.Q, PieceType.Queen), (Keys.R, PieceType.Rook),
            (Keys.B, PieceType.Bishop), (Keys.N, PieceType.Knight),
            (Keys.Enter, PieceType.Queen)
        };

        foreach (var (key, expected) in cases)
        {
            using var dialog = new PromotionDialog(PieceColor.Black);
            UiHarness.KeyDown(dialog, key);
            Assert.Equal(expected, dialog.Selected);  // клавиша {key} выбирает фигуру
            Assert.Equal(DialogResult.OK, dialog.DialogResult);  // выбор принят
        }

        using var escape = new PromotionDialog(PieceColor.Black);
        UiHarness.KeyDown(escape, Keys.Escape);
        Assert.Equal(DialogResult.Cancel, escape.DialogResult);  // Esc отменяет превращение

        using var other = new PromotionDialog(PieceColor.Black);
        UiHarness.KeyDown(other, Keys.Z);
        Assert.Equal(DialogResult.None, other.DialogResult);  // посторонняя клавиша ничего не делает
    }

    [WinFormsFact(DisplayName = "Превращение: все четыре фигуры нарисованы")]
    public void Отрисовка()
    {
        using var dialog = new PromotionDialog(PieceColor.White);
        using var image = UiHarness.Render(dialog);

        for (var i = 0; i < 4; i++)
        {
            var cell = new Rectangle(i * Cell, 0, Cell, Cell);
            var background = image.GetPixel(cell.Left + 2, cell.Top + 2);
            Assert.True(UiHarness.InkFraction(image, cell, background) > 0.05,
                $"в клетке {i} нарисована фигура");
        }
    }
}
