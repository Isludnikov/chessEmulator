using Xunit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>Шкала оценки: заполнение белой части и подпись.</summary>
public class EvalBarTests
{
    private const int Height = 400;
    private const int Width = 28;

    [WinFormsFact(DisplayName = "Шкала оценки")]
    public void Заполнение_и_подпись()
    {
        using var bar = new EvalBar { ClientSize = new Size(Width, Height) };

        Assert.Null(Record.Exception(() => UiHarness.Render(bar).Dispose()));  // пустая шкала рисуется

        Assert.Equal(0.5, WhitePart(bar, 0, null), 0.05);  // равная позиция — шкала пополам
        Assert.True(WhitePart(bar, 300, null) > 0.75, "перевес белых поднимает шкалу");
        Assert.True(WhitePart(bar, -300, null) < 0.25, "перевес чёрных опускает шкалу");
        Assert.True(Math.Abs(WhitePart(bar, 30, null) - 0.5) < 0.1, "небольшой перевес сдвигает шкалу чуть-чуть");
        Assert.True(WhitePart(bar, null, 3) > 0.97, "мат за белых заполняет шкалу");
        Assert.True(WhitePart(bar, null, -3) < 0.03, "мат за чёрных опустошает шкалу");
        Assert.True(WhitePart(bar, 20000, null) <= 1.0, "огромный перевес не выходит за края");
        Assert.Equal(0.5, WhitePart(bar, null, null), 0.05);  // сброс возвращает шкалу в середину

        // Подпись меняется вместе с оценкой
        bar.SetEvaluation(0, null);
        Settle(bar);
        using var equal = UiHarness.Render(bar);
        bar.SetEvaluation(0, null);
        Settle(bar);
        using var equalAgain = UiHarness.Render(bar);
        Assert.Equal(0, UiHarness.Difference(equal, equalAgain));  // одинаковая оценка рисуется одинаково

        bar.SetEvaluation(37, null);
        Settle(bar);
        using var slight = UiHarness.Render(bar);
        Assert.True(UiHarness.Difference(equal, slight) > 20, "другая оценка рисуется иначе");

        // узкая шкала рисуется
        Assert.Null(Record.Exception(() =>
        {
            using var narrow = new EvalBar { ClientSize = new Size(8, 60) };
            narrow.SetEvaluation(50, null);
            UiHarness.Render(narrow).Dispose();
        }));
    }

    [WinFormsFact(DisplayName = "Шкала оценки: подпись мата")]
    public void ПодписьМата()
    {
        using var bar = new EvalBar { ClientSize = new Size(Width, Height) };

        bar.SetEvaluation(null, 3);
        Assert.Equal("#3", Caption(bar));  // мат за белых

        bar.SetEvaluation(null, -3);
        Assert.Equal("#-3", Caption(bar));  // мат за чёрных

        // «Мат в ноль» приходит от движка в уже заматованной позиции: знака у нуля нет.
        bar.SetEvaluation(null, 0);
        Assert.Equal("#0", Caption(bar));  // не «#-0»

        bar.Clear();
        Assert.Equal("—", Caption(bar));  // без оценки подписи нет
    }

    /// <summary>Подпись шкалы: текст рисуется в картинку, поэтому читаем его из поля.</summary>
    private static string Caption(EvalBar bar)
    {
        var field = typeof(EvalBar).GetField("_text",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (string)field!.GetValue(bar)!;
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
