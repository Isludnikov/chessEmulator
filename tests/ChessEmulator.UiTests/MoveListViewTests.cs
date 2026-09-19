using ChessEmulator.Chess;
using ChessEmulator.TestKit;
using ChessEmulator.UI;

namespace ChessEmulator.UiTests;

/// <summary>Запись партии: раскладка ходов, выбор хода мышью, оценки и варианты.</summary>
internal static class MoveListViewTests
{
    public static void Run()
    {
        Layout();
        Selection();
    }

    private static Game SampleGame()
    {
        var game = new Game();
        foreach (var san in new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" })
            game.TryAddSan(san, out _);
        return game;
    }

    private static void Layout()
    {
        Test.Suite("Запись партии: раскладка", () =>
        {
            using var view = new MoveListView { ClientSize = new Size(320, 400) };
            var background = Color.FromArgb(32, 32, 34);

            using var blank = UiHarness.Render(view);
            Test.True("без партии видна подсказка", UiHarness.InkFraction(blank, background) > 0.001);
            Test.Check("без партии прокручивать нечего", 0, view.AutoScrollMinSize.Height);

            view.Game = SampleGame();
            using var withGame = UiHarness.Render(view);
            Test.True("ходы нарисованы", UiHarness.Difference(blank, withGame) > 500);
            Test.True("текста стало больше",
                UiHarness.InkFraction(withGame, background) > UiHarness.InkFraction(blank, background));
            Test.True("список прокручивается по содержимому", view.AutoScrollMinSize.Height > 0);

            // Комментарий и знак добавляют текст
            var annotated = SampleGame();
            annotated.MainLine()[0].Glyph = "!";
            annotated.MainLine()[1].Comment = "симметричный ответ";
            using var annotatedView = new MoveListView { ClientSize = new Size(320, 400), Game = annotated };
            using var annotatedImage = UiHarness.Render(annotatedView);
            Test.True("комментарий и знак попали в запись",
                UiHarness.InkFraction(annotatedImage, background) >
                UiHarness.InkFraction(withGame, background));

            // Оценки движка можно скрыть
            var evaluated = SampleGame();
            foreach (var node in evaluated.MainLine()) node.EvalCp = 25;
            using var withEval = new MoveListView { ClientSize = new Size(320, 400), Game = evaluated };
            using var evalImage = UiHarness.Render(withEval);
            withEval.ShowEvaluations = false;
            withEval.Reload();
            using var withoutEval = UiHarness.Render(withEval);
            Test.True("оценки видны", UiHarness.Difference(evalImage, withoutEval) > 200);
            Test.True("без оценок текста меньше",
                UiHarness.InkFraction(withoutEval, background) < UiHarness.InkFraction(evalImage, background));

            // Мат в оценке
            var mated = SampleGame();
            mated.MainLine()[^1].MateIn = -2;
            Test.NoThrow("оценка мата рисуется", () =>
            {
                using var view2 = new MoveListView { ClientSize = new Size(320, 400), Game = mated };
                UiHarness.Render(view2).Dispose();
            });

            // Варианты рисуются в скобках и с отступом
            var withVariation = SampleGame();
            withVariation.GoTo(withVariation.Root.MainChild!);
            withVariation.TryAddSan("c5", out _);
            withVariation.TryAddSan("Nf3", out _);
            using var variationView = new MoveListView { ClientSize = new Size(320, 400), Game = withVariation };
            using var variationImage = UiHarness.Render(variationView);
            Test.True("вариант занимает больше места",
                variationView.AutoScrollMinSize.Height > view.AutoScrollMinSize.Height);
            Test.True("вариант виден", UiHarness.InkFraction(variationImage, background) > 0.01);

            Test.NoThrow("прокрутка к текущему ходу не падает", () => variationView.ScrollToCurrent());
            Test.NoThrow("узкая панель раскладывается", () =>
            {
                using var narrow = new MoveListView { ClientSize = new Size(60, 200), Game = SampleGame() };
                UiHarness.Render(narrow).Dispose();
            });
            Test.NoThrow("партия без ходов раскладывается", () =>
            {
                using var fresh = new MoveListView { ClientSize = new Size(320, 200), Game = new Game() };
                UiHarness.Render(fresh).Dispose();
            });
        });
    }

    private static void Selection()
    {
        Test.Suite("Запись партии: выбор хода мышью", () =>
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

            Test.True("в первой строке нашлись ходы", hits.Count >= 2);
            Test.Check("первым идёт первый ход партии", "e4", hits.Count > 0 ? hits[0] : "");
            Test.Check("вторым — ответ чёрных", "e5", hits.Count > 1 ? hits[1] : "");

            // Щелчок по пустому месту ничего не выбирает
            selected.Clear();
            UiHarness.MouseDown(view, new Point(10, 380));
            Test.Check("щелчок по пустому месту", 0, selected.Count);

            // Номера ходов не выбираются — только сами ходы
            Test.True("выбранные узлы — ходы основной линии",
                hits.All(san => game.MainLine().Any(n => n.San == san)));

            Test.NoThrow("движение мыши не падает", () => UiHarness.MouseMove(view, new Point(40, 14)));
            Test.NoThrow("щелчок без партии не падает", () =>
            {
                using var emptyView = new MoveListView { ClientSize = new Size(320, 200) };
                UiHarness.Render(emptyView).Dispose();
                UiHarness.MouseDown(emptyView, new Point(20, 20));
            });
        });
    }
}
