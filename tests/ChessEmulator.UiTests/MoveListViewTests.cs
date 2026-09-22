using ChessEmulator.Chess;
using Xunit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>Запись партии: раскладка ходов, выбор хода мышью, оценки и варианты.</summary>
public class MoveListViewTests
{
    private static Game SampleGame()
    {
        var game = new Game();
        foreach (var san in new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" })
            game.TryAddSan(san, out _);
        return game;
    }

    [WinFormsFact(DisplayName = "Запись партии: раскладка")]
    public void Layout()
    {
        using var view = new MoveListView { ClientSize = new Size(320, 400) };
        var background = Color.FromArgb(32, 32, 34);

        using var blank = UiHarness.Render(view);
        Assert.True(UiHarness.InkFraction(blank, background) > 0.001, "без партии видна подсказка");
        Assert.Equal(0, view.AutoScrollMinSize.Height);  // без партии прокручивать нечего

        view.Game = SampleGame();
        using var withGame = UiHarness.Render(view);
        Assert.True(UiHarness.Difference(blank, withGame) > 500, "ходы нарисованы");
        Assert.True(UiHarness.InkFraction(withGame, background) > UiHarness.InkFraction(blank, background), "текста стало больше");
        Assert.True(view.AutoScrollMinSize.Height > 0, "список прокручивается по содержимому");

        // Комментарий и знак добавляют текст
        var annotated = SampleGame();
        annotated.MainLine()[0].Glyph = "!";
        annotated.MainLine()[1].Comment = "симметричный ответ";
        using var annotatedView = new MoveListView { ClientSize = new Size(320, 400), Game = annotated };
        using var annotatedImage = UiHarness.Render(annotatedView);
        Assert.True(UiHarness.InkFraction(annotatedImage, background) >
            UiHarness.InkFraction(withGame, background), "комментарий и знак попали в запись");

        // Оценки движка можно скрыть
        var evaluated = SampleGame();
        foreach (var node in evaluated.MainLine()) node.EvalCp = 25;
        using var withEval = new MoveListView { ClientSize = new Size(320, 400), Game = evaluated };
        using var evalImage = UiHarness.Render(withEval);
        withEval.ShowEvaluations = false;
        withEval.Reload();
        using var withoutEval = UiHarness.Render(withEval);
        Assert.True(UiHarness.Difference(evalImage, withoutEval) > 200, "оценки видны");
        Assert.True(UiHarness.InkFraction(withoutEval, background) < UiHarness.InkFraction(evalImage, background), "без оценок текста меньше");

        // Мат в оценке
        var mated = SampleGame();
        mated.MainLine()[^1].MateIn = -2;
        // оценка мата рисуется
        Assert.Null(Record.Exception(() =>
        {
            using var view2 = new MoveListView { ClientSize = new Size(320, 400), Game = mated };
            UiHarness.Render(view2).Dispose();
        }));

        // Варианты рисуются в скобках и с отступом
        var withVariation = SampleGame();
        withVariation.GoTo(withVariation.Root.MainChild!);
        withVariation.TryAddSan("c5", out _);
        withVariation.TryAddSan("Nf3", out _);
        using var variationView = new MoveListView { ClientSize = new Size(320, 400), Game = withVariation };
        using var variationImage = UiHarness.Render(variationView);
        Assert.True(variationView.AutoScrollMinSize.Height > view.AutoScrollMinSize.Height, "вариант занимает больше места");
        Assert.True(UiHarness.InkFraction(variationImage, background) > 0.01, "вариант виден");

        Assert.Null(Record.Exception(() => variationView.ScrollToCurrent()));  // прокрутка к текущему ходу не падает
        // узкая панель раскладывается
        Assert.Null(Record.Exception(() =>
        {
            using var narrow = new MoveListView { ClientSize = new Size(60, 200), Game = SampleGame() };
            UiHarness.Render(narrow).Dispose();
        }));
        // партия без ходов раскладывается
        Assert.Null(Record.Exception(() =>
        {
            using var fresh = new MoveListView { ClientSize = new Size(320, 200), Game = new Game() };
            UiHarness.Render(fresh).Dispose();
        }));
    }

    [WinFormsFact(DisplayName = "Запись партии: раскладка готова сразу после Reload")]
    public void LayoutReadyAfterReload()
    {
        // Кони ходят туда-обратно: так партию можно удлинять сколько угодно.
        var game = new Game();
        void Shuffle(int times)
        {
            for (var i = 0; i < times; i++)
                foreach (var san in new[] { "Nf3", "Nf6", "Ng1", "Ng8" })
                    Assert.True(game.TryAddSan(san, out _), $"ход {san} находится");
        }

        Shuffle(1);
        using var view = new MoveListView { ClientSize = new Size(320, 120), Game = game };
        UiHarness.Render(view).Dispose();               // первая отрисовка строит раскладку
        var before = view.AutoScrollMinSize.Height;

        Shuffle(20);
        view.Reload();
        view.ScrollToCurrent();

        // Раскладка строилась только при отрисовке, поэтому сразу после Reload список ещё
        // не знал о новых ходах — и прокрутка к последнему ходу молча не срабатывала.
        Assert.True(view.AutoScrollMinSize.Height > before,
            $"раскладка учла новые ходы, не дожидаясь отрисовки: было {before}, стало {view.AutoScrollMinSize.Height}");
    }

    [WinFormsFact(DisplayName = "Запись партии: правая кнопка не выбирает ход")]
    public void RightClickDoesNotSelect()
    {
        var game = SampleGame();
        using var view = new MoveListView { ClientSize = new Size(320, 400), Game = game };
        UiHarness.Render(view).Dispose();

        MoveNode? selected = null;
        view.NodeSelected += (_, node) => selected = node;

        // Находим точку, где точно есть ход: левым щелчком она выбирается.
        var point = new Point(30, 12);
        UiHarness.MouseDown(view, point);
        Assert.NotNull(selected);  // левая кнопка выбирает ход

        selected = null;
        UiHarness.MouseDown(view, point, MouseButtons.Right);
        Assert.Null(selected);  // правая — не выбирает
    }

    [WinFormsFact(DisplayName = "Запись партии: выбор хода мышью")]
    public void Selection()
    {
        var game = SampleGame();
        using var view = new MoveListView { ClientSize = new Size(320, 400), Game = game };
        var selected = new List<MoveNode>();
        view.NodeSelected += (_, node) => selected.Add(node);

        // Раскладка считается при отрисовке
        UiHarness.Render(view).Dispose();

        // Проходим первую строку слева направо и собираем ходы, по которым попали
        var hits = new List<string>();
        for (var x = 4; x < 300; x += 2)
        {
            selected.Clear();
            UiHarness.MouseDown(view, new Point(x, 14));
            foreach (var node in selected)
                if (hits.Count == 0 || hits[^1] != node.San) hits.Add(node.San);
        }

        Assert.True(hits.Count >= 2, "в первой строке нашлись ходы");
        Assert.Equal("e4", hits.Count > 0 ? hits[0] : "");  // первым идёт первый ход партии
        Assert.Equal("e5", hits.Count > 1 ? hits[1] : "");  // вторым — ответ чёрных

        // Щелчок по пустому месту ничего не выбирает
        selected.Clear();
        UiHarness.MouseDown(view, new Point(10, 380));
        Assert.Empty(selected);  // щелчок по пустому месту

        // Номера ходов не выбираются — только сами ходы
        Assert.True(hits.All(san => game.MainLine().Any(n => n.San == san)), "выбранные узлы — ходы основной линии");

        Assert.Null(Record.Exception(() => UiHarness.MouseMove(view, new Point(40, 14))));  // движение мыши не падает
        // щелчок без партии не падает
        Assert.Null(Record.Exception(() =>
        {
            using var emptyView = new MoveListView { ClientSize = new Size(320, 200) };
            UiHarness.Render(emptyView).Dispose();
            UiHarness.MouseDown(emptyView, new Point(20, 20));
        }));
    }
}
