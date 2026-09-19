using ChessEmulator.TestKit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>Шкала оценки: заполнение белой части и подпись.</summary>
internal static class EvalBarTests
{
    private const int Height = 400;
    private const int Width = 28;

    public static void Run()
    {
        Test.Suite("Шкала оценки", () =>
        {
            using var bar = new EvalBar { ClientSize = new Size(Width, Height) };

            Test.NoThrow("пустая шкала рисуется", () => UiHarness.Render(bar).Dispose());

            Test.Near("равная позиция — шкала пополам", 0.5, WhitePart(bar, 0, null), 0.05);
            Test.True("перевес белых поднимает шкалу", WhitePart(bar, 300, null) > 0.75);
            Test.True("перевес чёрных опускает шкалу", WhitePart(bar, -300, null) < 0.25);
            Test.True("небольшой перевес сдвигает шкалу чуть-чуть",
                Math.Abs(WhitePart(bar, 30, null) - 0.5) < 0.1);
            Test.True("мат за белых заполняет шкалу", WhitePart(bar, null, 3) > 0.97);
            Test.True("мат за чёрных опустошает шкалу", WhitePart(bar, null, -3) < 0.03);
            Test.True("огромный перевес не выходит за края", WhitePart(bar, 20000, null) <= 1.0);
            Test.Near("сброс возвращает шкалу в середину", 0.5, WhitePart(bar, null, null), 0.05);

            // Подпись меняется вместе с оценкой
            bar.SetEvaluation(0, null);
            Settle(bar);
            using var equal = UiHarness.Render(bar);
            bar.SetEvaluation(0, null);
            Settle(bar);
            using var equalAgain = UiHarness.Render(bar);
            Test.Check("одинаковая оценка рисуется одинаково", 0, UiHarness.Difference(equal, equalAgain));

            bar.SetEvaluation(37, null);
            Settle(bar);
            using var slight = UiHarness.Render(bar);
            Test.True("другая оценка рисуется иначе", UiHarness.Difference(equal, slight) > 20);

            Test.NoThrow("узкая шкала рисуется", () =>
            {
                using var narrow = new EvalBar { ClientSize = new Size(8, 60) };
                narrow.SetEvaluation(50, null);
                UiHarness.Render(narrow).Dispose();
            });
        });
    }

    /// <summary>Доля высоты шкалы, занятая белым цветом, после завершения анимации.</summary>
    private static double WhitePart(EvalBar bar, int? centipawns, int? mateIn)
    {
        bar.SetEvaluation(centipawns, mateIn);
        Settle(bar);

        using var image = UiHarness.Render(bar);
        var white = 0;
        for (var y = 0; y < image.Height; y++)
        {
            // Столбец у края: подпись рисуется по центру и не мешает
            if (image.GetPixel(1, y).R > 150) white++;
        }
        return (double)white / image.Height;
    }

    /// <summary>Прокручивает анимацию шкалы: без цикла сообщений таймер сам не тикает.</summary>
    private static void Settle(EvalBar bar)
    {
        for (var i = 0; i < 400; i++)
        {
            Application.DoEvents();
            Thread.Sleep(1);
            if (!Animating(bar)) return;
        }
    }

    private static bool Animating(EvalBar bar)
    {
        var field = typeof(EvalBar).GetField("_animation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(bar) is System.Windows.Forms.Timer { Enabled: true };
    }
}
