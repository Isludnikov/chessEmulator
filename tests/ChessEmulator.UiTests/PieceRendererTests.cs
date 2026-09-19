using ChessEmulator.Chess;
using Xunit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>
/// Отрисовка фигур. Проверка не декоративная: однажды фигуры вовсе не появлялись на доске,
/// потому что крупный символ шрифта обрезался прямоугольником вывода. Теперь фигуры
/// нарисованы собственной геометрией, и тесты следят за их видом и пропорциями.
/// </summary>
public class PieceRendererTests
{
    private static readonly PieceType[] Types =
    {
        PieceType.King, PieceType.Queen, PieceType.Rook,
        PieceType.Bishop, PieceType.Knight, PieceType.Pawn
    };

    private static readonly Color Background = Color.FromArgb(240, 217, 181);
    private static readonly Rectangle Cell = new(0, 0, 96, 96);

    // ------------------------------------------------------------ Пропорции

    [WinFormsFact(DisplayName = "Фигуры: пропорции")]
    public void Proportions()
    {
        var bounds = Types.ToDictionary(t => t, t =>
        {
            using var bitmap = Draw(new Piece(PieceColor.White, t), Cell, Background);
            return UiHarness.InkBounds(bitmap, Background);
        });

        // Все фигуры стоят на одной линии — основания выровнены
        int lowest = bounds.Values.Min(b => b.Bottom);
        int highestBottom = bounds.Values.Max(b => b.Bottom);
        Assert.True(highestBottom - lowest <= 2, $"основания на одной линии (разброс {highestBottom - lowest} px)");

        // Высоты выстроены по старшинству фигур
        int Height(PieceType t) => bounds[t].Height;
        Assert.True(Height(PieceType.King) - Height(PieceType.Pawn) >= 8, $"пешка ниже короля ({Height(PieceType.Pawn)} против {Height(PieceType.King)})");
        Assert.True(Height(PieceType.Rook) > Height(PieceType.Pawn), "пешка ниже ладьи");
        Assert.True(Height(PieceType.Bishop) > Height(PieceType.Pawn), "пешка ниже слона");
        Assert.True(Height(PieceType.Knight) > Height(PieceType.Pawn), "пешка ниже коня");
        Assert.True(Height(PieceType.Queen) > Height(PieceType.Rook), "ладья ниже ферзя");
        Assert.True(Types.All(t => t == PieceType.King || Height(PieceType.King) >= Height(t)), "король — самая высокая фигура");

        // Фигура вписана в клетку и стоит по центру
        foreach (var type in Types)
        {
            var box = bounds[type];
            int center = box.Left + box.Width / 2;
            Assert.True(Math.Abs(center - Cell.Width / 2) <= 4, $"{type}: фигура по центру клетки (центр {center})");
            Assert.True(Cell.Contains(box), $"{type}: фигура не выходит за клетку");
            Assert.True(box.Height > Cell.Height * 0.55, $"{type}: фигура занимает клетку по высоте");
            Assert.True(box.Width < Cell.Width, $"{type}: фигура не шире клетки");
        }

        // Симметричные фигуры и конь в профиль
        foreach (var type in new[] { PieceType.King, PieceType.Queen, PieceType.Rook, PieceType.Pawn })
        {
            double skew = Mirror(type);
            Assert.True(skew < 0.03, $"{type}: фигура симметрична ({skew:P1})");
        }

        double knight = Mirror(PieceType.Knight);
        Assert.True(knight > 0.06, $"конь нарисован в профиль ({knight:P1})");
    }

    /// <summary>Несимметричность самой фигуры: тень в расчёт не идёт.</summary>
    private static double Mirror(PieceType type)
    {
        using var bitmap = Draw(new Piece(PieceColor.White, type), Cell, Background);
        return UiHarness.MirrorDifference(bitmap, Color.FromArgb(252, 252, 250));
    }

    // ------------------------------------------------------------ Различимость

    [WinFormsFact(DisplayName = "Фигуры: различимость")]
    public void Distinctness()
    {
        var images = Types.ToDictionary(t => t, t => Draw(new Piece(PieceColor.White, t), Cell, Background));
        try
        {
            int tooSimilar = 0;
            int worst = int.MaxValue;
            string worstPair = "";

            for (int i = 0; i < Types.Length; i++)
            {
                for (int j = i + 1; j < Types.Length; j++)
                {
                    int diff = UiHarness.Difference(images[Types[i]], images[Types[j]]);
                    if (diff < 400) tooSimilar++;
                    if (diff < worst)
                    {
                        worst = diff;
                        worstPair = $"{Types[i]} и {Types[j]}";
                    }
                }
            }

            // все шесть фигур отличаются друг от друга (ближайшие — {worstPair}, {worst} px)
            Assert.Equal(0, tooSimilar);

            // Мелкий размер: фигуры должны различаться и на маленькой доске
            var small = new Rectangle(0, 0, 20, 20);
            var tiny = Types.ToDictionary(t => t, t => Draw(new Piece(PieceColor.White, t), small, Background));
            try
            {
                int tinySimilar = 0;
                for (int i = 0; i < Types.Length; i++)
                {
                    for (int j = i + 1; j < Types.Length; j++)
                    {
                        if (UiHarness.Difference(tiny[Types[i]], tiny[Types[j]]) < 12) tinySimilar++;
                    }
                }
                Assert.Equal(0, tinySimilar);  // фигуры различимы в клетке 20 px
            }
            finally
            {
                foreach (var image in tiny.Values) image.Dispose();
            }

            // Цвет фигуры виден: белая заливка светлее чёрной
            using var whiteKing = Draw(new Piece(PieceColor.White, PieceType.King), Cell, Background);
            using var blackKing = Draw(new Piece(PieceColor.Black, PieceType.King), Cell, Background);
            Assert.True(UiHarness.Difference(whiteKing, blackKing) > 500, "белая и чёрная фигуры отличаются");
            Assert.True(Brightness(whiteKing) > Brightness(blackKing) + 20, "белая фигура светлее чёрной");
        }
        finally
        {
            foreach (var image in images.Values) image.Dispose();
        }
    }

    /// <summary>Средняя яркость центральной части клетки — там находится корпус фигуры.</summary>
    private static double Brightness(Bitmap bitmap)
    {
        double sum = 0;
        int count = 0;
        for (int y = bitmap.Height / 3; y < bitmap.Height * 2 / 3; y++)
        {
            for (int x = bitmap.Width / 3; x < bitmap.Width * 2 / 3; x++)
            {
                sum += bitmap.GetPixel(x, y).GetBrightness() * 255;
                count++;
            }
        }
        return count == 0 ? 0 : sum / count;
    }

    // ------------------------------------------------------------ Отрисовка

    [WinFormsFact(DisplayName = "Фигуры: отрисовка")]
    public void Drawing()
    {
        var cell = new Rectangle(0, 0, 64, 64);

        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            foreach (var type in Types)
            {
                using var bitmap = Draw(new Piece(color, type), cell, Background);
                double ink = UiHarness.InkFraction(bitmap, Background);
                string name = $"{(color == PieceColor.White ? "белая" : "чёрная")} {type}";
                Assert.True(ink > 0.10, $"{name}: фигура видна (закрашено {ink:P0})");
                Assert.True(ink < 0.85, $"{name}: фигура не заливает всю клетку");
            }
        }

        // Фигура вписана в клетку и не выходит за её края
        using var wide = new Bitmap(128, 128);
        using (var g = Graphics.FromImage(wide))
        {
            g.Clear(Background);
            PieceRenderer.Draw(g, new Piece(PieceColor.Black, PieceType.Queen), new Rectangle(32, 32, 64, 64));
        }
        // за пределами клетки чисто слева
        Assert.Equal(0.0, Math.Round(UiHarness.InkFraction(wide, new Rectangle(0, 0, 31, 128), Background), 3));
        // за пределами клетки чисто справа
        Assert.Equal(0.0, Math.Round(UiHarness.InkFraction(wide, new Rectangle(97, 0, 31, 128), Background), 3));
        // за пределами клетки чисто сверху
        Assert.Equal(0.0, Math.Round(UiHarness.InkFraction(wide, new Rectangle(0, 0, 128, 31), Background), 3));
        Assert.True(UiHarness.InkFraction(wide, new Rectangle(32, 32, 64, 64), Background) > 0.10, "внутри клетки фигура есть");

        // Прямоугольная клетка: фигура не растягивается
        using var stretched = Draw(new Piece(PieceColor.White, PieceType.Rook),
            new Rectangle(0, 0, 120, 60), Background);
        var box = UiHarness.InkBounds(stretched, Background);
        Assert.True(box.Width < box.Height, $"в широкой клетке фигура не растянута (ширина {box.Width}, высота {box.Height})");

        // Мелкие и крупные клетки
        using var small = Draw(new Piece(PieceColor.White, PieceType.Rook), new Rectangle(0, 0, 16, 16), Background);
        using var large = Draw(new Piece(PieceColor.White, PieceType.Rook), new Rectangle(0, 0, 160, 160), Background);
        Assert.True(UiHarness.InkFraction(small, Background) > 0.05, "фигура видна в мелкой клетке");
        Assert.True(UiHarness.InkFraction(large, Background) > 0.10, "фигура видна в крупной клетке");

        // Пустая фигура и вырожденная клетка ничего не рисуют
        using var empty = Draw(Piece.Empty, cell, Background);
        Assert.Equal(0.0, UiHarness.InkFraction(empty, Background));  // пустая клетка остаётся пустой
        // нулевая клетка не ломает отрисовку
        Assert.Null(Record.Exception(() =>
        {
            using var bitmap = new Bitmap(4, 4);
            using var g = Graphics.FromImage(bitmap);
            PieceRenderer.Draw(g, new Piece(PieceColor.White, PieceType.Queen), new Rectangle(0, 0, 2, 2));
        }));
        // фигуры рисуются без шрифтов системы
        Assert.Null(Record.Exception(() =>
        {
            using var bitmap = new Bitmap(64, 64);
            using var g = Graphics.FromImage(bitmap);
            foreach (var type in Types) PieceRenderer.Draw(g, new Piece(PieceColor.Black, type), cell);
        }));
    }

    private static Bitmap Draw(Piece piece, Rectangle cell, Color background)
    {
        var bitmap = new Bitmap(cell.Width, cell.Height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(background);
        PieceRenderer.Draw(g, piece, cell);
        return bitmap;
    }
}
