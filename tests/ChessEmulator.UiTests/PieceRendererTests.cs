using ChessEmulator.Chess;
using ChessEmulator.TestKit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>
/// Отрисовка фигур. Проверка не декоративная: однажды фигуры вовсе не появлялись на доске,
/// потому что крупный символ шрифта обрезался прямоугольником вывода. Теперь фигуры
/// нарисованы собственной геометрией, и тесты следят за их видом и пропорциями.
/// </summary>
internal static class PieceRendererTests
{
    private static readonly PieceType[] Types =
    {
        PieceType.King, PieceType.Queen, PieceType.Rook,
        PieceType.Bishop, PieceType.Knight, PieceType.Pawn
    };

    private static readonly Color Background = Color.FromArgb(240, 217, 181);
    private static readonly Rectangle Cell = new(0, 0, 96, 96);

    public static void Run()
    {
        Proportions();
        Distinctness();
        Drawing();
    }

    // ------------------------------------------------------------ Пропорции

    private static void Proportions()
    {
        Test.Suite("Фигуры: пропорции", () =>
        {
            var bounds = Types.ToDictionary(t => t, t =>
            {
                using var bitmap = Draw(new Piece(PieceColor.White, t), Cell, Background);
                return UiHarness.InkBounds(bitmap, Background);
            });

            // Все фигуры стоят на одной линии — основания выровнены
            int lowest = bounds.Values.Min(b => b.Bottom);
            int highestBottom = bounds.Values.Max(b => b.Bottom);
            Test.True($"основания на одной линии (разброс {highestBottom - lowest} px)",
                highestBottom - lowest <= 2);

            // Высоты выстроены по старшинству фигур
            int Height(PieceType t) => bounds[t].Height;
            Test.True($"пешка ниже короля ({Height(PieceType.Pawn)} против {Height(PieceType.King)})",
                Height(PieceType.King) - Height(PieceType.Pawn) >= 8);
            Test.True("пешка ниже ладьи", Height(PieceType.Rook) > Height(PieceType.Pawn));
            Test.True("пешка ниже слона", Height(PieceType.Bishop) > Height(PieceType.Pawn));
            Test.True("пешка ниже коня", Height(PieceType.Knight) > Height(PieceType.Pawn));
            Test.True("ладья ниже ферзя", Height(PieceType.Queen) > Height(PieceType.Rook));
            Test.True("король — самая высокая фигура",
                Types.All(t => t == PieceType.King || Height(PieceType.King) >= Height(t)));

            // Фигура вписана в клетку и стоит по центру
            foreach (var type in Types)
            {
                var box = bounds[type];
                int center = box.Left + box.Width / 2;
                Test.True($"{type}: фигура по центру клетки (центр {center})",
                    Math.Abs(center - Cell.Width / 2) <= 4);
                Test.True($"{type}: фигура не выходит за клетку", Cell.Contains(box));
                Test.True($"{type}: фигура занимает клетку по высоте", box.Height > Cell.Height * 0.55);
                Test.True($"{type}: фигура не шире клетки", box.Width < Cell.Width);
            }

            // Симметричные фигуры и конь в профиль
            foreach (var type in new[] { PieceType.King, PieceType.Queen, PieceType.Rook, PieceType.Pawn })
            {
                double skew = Mirror(type);
                Test.True($"{type}: фигура симметрична ({skew:P1})", skew < 0.03);
            }

            double knight = Mirror(PieceType.Knight);
            Test.True($"конь нарисован в профиль ({knight:P1})", knight > 0.06);
        });
    }

    /// <summary>Несимметричность самой фигуры: тень в расчёт не идёт.</summary>
    private static double Mirror(PieceType type)
    {
        using var bitmap = Draw(new Piece(PieceColor.White, type), Cell, Background);
        return UiHarness.MirrorDifference(bitmap, Color.FromArgb(252, 252, 250));
    }

    // ------------------------------------------------------------ Различимость

    private static void Distinctness()
    {
        Test.Suite("Фигуры: различимость", () =>
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

                Test.Check($"все шесть фигур отличаются друг от друга (ближайшие — {worstPair}, {worst} px)",
                    0, tooSimilar);

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
                    Test.Check("фигуры различимы в клетке 20 px", 0, tinySimilar);
                }
                finally
                {
                    foreach (var image in tiny.Values) image.Dispose();
                }

                // Цвет фигуры виден: белая заливка светлее чёрной
                using var whiteKing = Draw(new Piece(PieceColor.White, PieceType.King), Cell, Background);
                using var blackKing = Draw(new Piece(PieceColor.Black, PieceType.King), Cell, Background);
                Test.True("белая и чёрная фигуры отличаются",
                    UiHarness.Difference(whiteKing, blackKing) > 500);
                Test.True("белая фигура светлее чёрной",
                    Brightness(whiteKing) > Brightness(blackKing) + 20);
            }
            finally
            {
                foreach (var image in images.Values) image.Dispose();
            }
        });
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

    private static void Drawing()
    {
        Test.Suite("Фигуры: отрисовка", () =>
        {
            var cell = new Rectangle(0, 0, 64, 64);

            foreach (var color in new[] { PieceColor.White, PieceColor.Black })
            {
                foreach (var type in Types)
                {
                    using var bitmap = Draw(new Piece(color, type), cell, Background);
                    double ink = UiHarness.InkFraction(bitmap, Background);
                    string name = $"{(color == PieceColor.White ? "белая" : "чёрная")} {type}";
                    Test.True($"{name}: фигура видна (закрашено {ink:P0})", ink > 0.10);
                    Test.True($"{name}: фигура не заливает всю клетку", ink < 0.85);
                }
            }

            // Фигура вписана в клетку и не выходит за её края
            using var wide = new Bitmap(128, 128);
            using (var g = Graphics.FromImage(wide))
            {
                g.Clear(Background);
                PieceRenderer.Draw(g, new Piece(PieceColor.Black, PieceType.Queen), new Rectangle(32, 32, 64, 64));
            }
            Test.Check("за пределами клетки чисто слева", 0.0,
                Math.Round(UiHarness.InkFraction(wide, new Rectangle(0, 0, 31, 128), Background), 3));
            Test.Check("за пределами клетки чисто справа", 0.0,
                Math.Round(UiHarness.InkFraction(wide, new Rectangle(97, 0, 31, 128), Background), 3));
            Test.Check("за пределами клетки чисто сверху", 0.0,
                Math.Round(UiHarness.InkFraction(wide, new Rectangle(0, 0, 128, 31), Background), 3));
            Test.True("внутри клетки фигура есть",
                UiHarness.InkFraction(wide, new Rectangle(32, 32, 64, 64), Background) > 0.10);

            // Прямоугольная клетка: фигура не растягивается
            using var stretched = Draw(new Piece(PieceColor.White, PieceType.Rook),
                new Rectangle(0, 0, 120, 60), Background);
            var box = UiHarness.InkBounds(stretched, Background);
            Test.True($"в широкой клетке фигура не растянута (ширина {box.Width}, высота {box.Height})",
                box.Width < box.Height);

            // Мелкие и крупные клетки
            using var small = Draw(new Piece(PieceColor.White, PieceType.Rook), new Rectangle(0, 0, 16, 16), Background);
            using var large = Draw(new Piece(PieceColor.White, PieceType.Rook), new Rectangle(0, 0, 160, 160), Background);
            Test.True("фигура видна в мелкой клетке", UiHarness.InkFraction(small, Background) > 0.05);
            Test.True("фигура видна в крупной клетке", UiHarness.InkFraction(large, Background) > 0.10);

            // Пустая фигура и вырожденная клетка ничего не рисуют
            using var empty = Draw(Piece.Empty, cell, Background);
            Test.Check("пустая клетка остаётся пустой", 0.0, UiHarness.InkFraction(empty, Background));
            Test.NoThrow("нулевая клетка не ломает отрисовку", () =>
            {
                using var bitmap = new Bitmap(4, 4);
                using var g = Graphics.FromImage(bitmap);
                PieceRenderer.Draw(g, new Piece(PieceColor.White, PieceType.Queen), new Rectangle(0, 0, 2, 2));
            });
            Test.NoThrow("фигуры рисуются без шрифтов системы", () =>
            {
                using var bitmap = new Bitmap(64, 64);
                using var g = Graphics.FromImage(bitmap);
                foreach (var type in Types) PieceRenderer.Draw(g, new Piece(PieceColor.Black, type), cell);
            });
        });
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
