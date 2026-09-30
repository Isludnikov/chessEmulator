using System.Reflection;
using ChessEmulator.App;
using ChessEmulator.Chess;
using ChessEmulator.Engine;
using ChessEmulator.UI;
using Xunit;
using static ChessEmulator.UiTests.MainFormHarness;

namespace ChessEmulator.UiTests;

/// <summary>
/// Главное окно: всё, что показывает анализ, — список вариантов, советы, стрелки, строка
/// состояния и знаки разбора. Движок не запущен: его строки подаются обработчику напрямую.
/// </summary>
public class MainFormAnalysisTests
{
    private const string HintsOffText = "Подсказки движка выключены (H включает).";

    /// <summary>Номер поиска, который окно считает своим анализом.</summary>
    private const long SearchId = 7;

    /// <summary>
    /// Объявляет окну, что идёт анализ текущей позиции: без номера поиска любая строка info
    /// считается запоздавшей. Возвращает движок окна — отправителя строк.
    /// </summary>
    private static UciEngine BeginAnalysis(MainForm form)
    {
        SetField(form, "_analysisSearchId", SearchId);
        return Field<UciEngine>(form, "_engine");
    }

    private static EngineInfo Line(int multiPv, int cp, params string[] pv) => new()
    {
        SearchId = SearchId,
        MultiPv = multiPv,
        ScoreCp = cp,
        Depth = 12,
        SelDepth = 18,
        Nodes = 1_500_000,
        Nps = 2_500,
        TimeMs = 1200,
        Pv = pv
    };

    private static void Receive(MainForm form, UciEngine sender, EngineInfo info) =>
        Call(form, "OnEngineInfo", sender, info);

    private static ListView Lines(MainForm form) => Field<ListView>(form, "_linesView");

    private static string Advice(MainForm form) => Field<RichTextBox>(form, "_adviceBox").Text;

    /// <summary>Стрелки, которые окно отдало доске.</summary>
    private static List<BoardArrow> Arrows(MainForm form)
    {
        var field = typeof(BoardControl).GetField("_arrows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (List<BoardArrow>)field.GetValue(Field<BoardControl>(form, "_board"))!;
    }

    [WinFormsFact(DisplayName = "Главное окно: строка анализа попадает в список, советы и строку состояния")]
    public void СтрокаАнализа()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var engine = BeginAnalysis(form);

            Receive(form, engine, Line(1, 35, "e2e4", "e7e5", "g1f3"));

            var item = Assert.Single(Lines(form).Items.Cast<ListViewItem>());
            Assert.Equal("+0.35", item.Text);  // оценка
            Assert.Equal("12", item.SubItems[1].Text);  // глубина
            Assert.Equal("1. e4 e5 2. Nf3", item.SubItems[2].Text);  // вариант записан ходами, а не клетками

            var advice = Advice(form);
            Assert.Contains("Ход белых. Оценка: +0.35", advice);
            Assert.Contains("Лучший ход: e4.", advice);
            Assert.Contains("Главный вариант: 1. e4 e5 2. Nf3", advice);

            var status = Field<ToolStripStatusLabel>(form, "_searchStatus").Text!;
            Assert.Contains("глубина 12/18", status);
            Assert.Contains($"{1.5:0.0} млн узлов", status);
            Assert.Contains($"{2.5:0.0} тыс. узлов/с", status);
            Assert.Contains($"{1.2:0.0} с", status);

            var arrow = Assert.Single(Arrows(form));
            Assert.Equal("e2e4", new Move(arrow.From, arrow.To).ToUci());  // стрелка показывает лучший ход
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: варианты анализа идут по номерам, у главного своя стрелка")]
    public void НесколькоВариантов()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var engine = BeginAnalysis(form);

            // Движок присылает варианты вперемешку.
            Receive(form, engine, Line(2, 20, "d2d4", "d7d5"));
            Receive(form, engine, Line(1, 35, "e2e4", "e7e5"));

            var lines = Lines(form);
            string[] byNumber = ["+0.35", "+0.20"];
            Assert.Equal(byNumber, lines.Items.Cast<ListViewItem>().Select(i => i.Text));

            // Главная стрелка рисуется последней — поверх остальных — и толще их.
            var arrows = Arrows(form);
            Assert.Equal(2, arrows.Count);
            Assert.Equal("d2d4", new Move(arrows[0].From, arrows[0].To).ToUci());
            Assert.Equal("e2e4", new Move(arrows[1].From, arrows[1].To).ToUci());
            Assert.True(arrows[1].Weight > arrows[0].Weight, "стрелка лучшего хода заметнее");

            // Новая строка с тем же номером заменяет прежнюю, а не добавляется.
            Receive(form, engine, Line(2, -10, "g1f3"));
            string[] replaced = ["+0.35", "-0.10"];
            Assert.Equal(replaced, lines.Items.Cast<ListViewItem>().Select(i => i.Text));
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: чужие и запоздавшие строки анализа не показываются")]
    public void ЧужиеСтроки()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var engine = BeginAnalysis(form);
            var lines = Lines(form);

            // Строка прежнего движка, дошедшая после перезапуска.
            using (var stranger = new UciEngine()) Receive(form, stranger, Line(1, 35, "e2e4"));
            Assert.Empty(lines.Items);

            // Строка другого поиска: та же сторона на ходу, ход легален, но оценка чужая.
            var late = Line(1, 35, "e2e4");
            late.SearchId = SearchId - 1;
            Receive(form, engine, late);
            Assert.Empty(lines.Items);

            Receive(form, engine, Line(1, 35));  // без варианта показывать нечего
            Receive(form, engine, Line(1, 35, "e7e5"));  // ход не из этой позиции
            Assert.Empty(lines.Items);

            // Пока движок ищет ход соперника, его строки — не анализ.
            SetField(form, "_engineBusyWithMove", true);
            Receive(form, engine, Line(1, 35, "e2e4"));
            Assert.Empty(lines.Items);
            SetField(form, "_engineBusyWithMove", false);

            // Анализ не идёт вовсе — нулевой номер поиска своим не считается.
            SetField(form, "_analysisSearchId", 0L);
            var stray = Line(1, 35, "e2e4");
            stray.SearchId = 0;
            Receive(form, engine, stray);
            Assert.Empty(lines.Items);

            // Та же строка при идущем анализе проходит.
            BeginAnalysis(form);
            Receive(form, engine, Line(1, 35, "e2e4"));
            Assert.Single(lines.Items);
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: с выключенными подсказками анализ на экран не попадает")]
    public void ПодсказкиВыключены()
    {
        WithSettingsFile(() =>
        {
            new AppSettings { ShowEngineHints = false }.Save();

            using var form = new MainForm();
            var engine = BeginAnalysis(form);
            Receive(form, engine, Line(1, 35, "e2e4", "e7e5"));

            Assert.Empty(Lines(form).Items);  // вариантов нет
            Assert.Empty(Arrows(form));  // стрелок нет
            Assert.Equal(HintsOffText, Advice(form));  // и совет не появился
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: стрелку лучшего хода можно выключить отдельно")]
    public void СтрелкаВыключена()
    {
        WithSettingsFile(() =>
        {
            new AppSettings { ShowBestMoveArrow = false }.Save();

            using var form = new MainForm();
            var engine = BeginAnalysis(form);
            Receive(form, engine, Line(1, 35, "e2e4", "e7e5"));

            Assert.Single(Lines(form).Items);  // варианты показаны
            Assert.Empty(Arrows(form));  // а стрелок нет
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: совет называет цену сделанного хода")]
    public void ЦенаСделанногоХода()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = Play(form, "e2e4");

            // Оценки разбора партии: до хода +0.30, после него −0.40 — ход стоил 0.70.
            game.Root.EvalCp = 30;
            game.Current.EvalCp = -40;

            var engine = BeginAnalysis(form);
            Receive(form, engine, Line(1, 40, "e7e5", "g1f3"));  // +0.40 от лица чёрных, которые ходят

            var advice = Advice(form);
            Assert.Contains("Ход чёрных. Оценка: -0.40", advice);  // оценка пересчитана на белых
            Assert.Contains("Лучший ход: e5.", advice);
            Assert.Contains("Главный вариант: 1... e5 2. Nf3", advice);
            Assert.Contains("Сделанный ход e4 уступает лучшему примерно 0.70 пешки.", advice);

            // Потерю до трети пешки не называем: это шум оценки, а не ошибка.
            game.Current.EvalCp = 10;
            Receive(form, engine, Line(1, 40, "e7e5", "g1f3"));
            Assert.DoesNotContain("уступает", Advice(form));
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: двойной щелчок по строке анализа делает её первый ход")]
    public void ДвойнойЩелчокПоВарианту()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();
            var game = GameOf(form);
            var engine = BeginAnalysis(form);
            Receive(form, engine, Line(1, 35, "e2e4", "e7e5"));
            var lines = Lines(form);

            // Выбор строки хранит сам системный список, поэтому ему нужен дескриптор окна.
            // Окно при этом не показывается.
            _ = lines.Handle;

            Call(form, "OnEngineLineDoubleClick", lines, EventArgs.Empty);
            Assert.True(game.Current.IsRoot, "без выбранной строки хода нет");

            lines.Items[0].Selected = true;
            Call(form, "OnEngineLineDoubleClick", lines, EventArgs.Empty);
            Assert.Equal("e2e4", game.Current.Move.ToUci());  // сделан первый ход варианта
            Assert.Empty(lines.Items);  // позиция сменилась — прежние варианты убраны
        });
    }

    [WinFormsFact(DisplayName = "Главное окно: разбор расставляет знаки по потере оценки")]
    public void ЗнакиРазбора()
    {
        WithSettingsFile(() =>
        {
            using var form = new MainForm();

            var game = new Game();
            foreach (var san in new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6" })
                Assert.True(game.TryAddSan(san, out _), $"ход {san} находится");
            var nodes = new List<MoveNode> { game.Root };
            nodes.AddRange(game.MainLine());

            // Оценки с точки зрения белых: исходная позиция и после каждого хода.
            int[] evals = [20, 20, 120, -60, 300, 300, 290];
            for (var i = 0; i < nodes.Count; i++) nodes[i].EvalCp = evals[i];
            nodes[1].Glyph = "??";  // знак прошлого разбора должен сняться

            Call(form, "AnnotateBlunders", nodes);

            Assert.Null(nodes[1].Glyph);  // e4: оценка не изменилась
            Assert.Equal("?!", nodes[2].Glyph);  // e5: чёрные отдали пешку оценки — неточность
            Assert.Equal("?", nodes[3].Glyph);  // Nf3: белые потеряли 1.80 — ошибка
            Assert.Equal("??", nodes[4].Glyph);  // Nc6: чёрные потеряли 3.60 — зевок
            Assert.Null(nodes[5].Glyph);  // Bb5: без потерь
            Assert.Null(nodes[6].Glyph);  // a6: чёрные даже улучшили оценку

            var second = new Game();
            foreach (var san in new[] { "e4", "e5", "Nf3" })
                Assert.True(second.TryAddSan(san, out _), $"ход {san} находится");
            var line = second.MainLine();

            // Ход совпал с советом движка — не наказываем, как бы ни упала оценка.
            second.Root.EvalCp = 50;
            second.Root.BestReply = "e2e4";
            line[0].EvalCp = -400;
            // Мат считается крупнее любой оценки в пешках: чёрные своим ходом пустили мат в три хода.
            line[1].MateIn = 3;
            // У последнего хода оценки нет — его знак не трогаем.
            line[2].Glyph = "!";

            Call(form, "AnnotateBlunders", new List<MoveNode> { second.Root, line[0], line[1], line[2] });

            Assert.Null(line[0].Glyph);
            Assert.Equal("??", line[1].Glyph);
            Assert.Equal("!", line[2].Glyph);
        });
    }

    [Fact(DisplayName = "Главное окно: большие числа в строке состояния сокращаются")]
    public void СокращениеЧисел()
    {
        static string Format(long value) => (string)Call(null, "FormatNumber", value)!;

        Assert.Equal("999", Format(999));
        Assert.Equal($"{1.0:0.0} тыс.", Format(1_000));
        Assert.Equal($"{999.9:0.0} тыс.", Format(999_900));
        Assert.Equal($"{1.5:0.0} млн", Format(1_500_000));
        Assert.Equal($"{2.3:0.0} млрд", Format(2_300_000_000));
    }

    [Fact(DisplayName = "Главное окно: вариант движка переводится в запись ходов")]
    public void ВариантВЗаписьХодов()
    {
        static string San(string fen, int maxMoves, params string[] pv) =>
            (string)Call(null, "PvToSan", Position.FromFen(fen), pv, maxMoves)!;

        const string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";

        Assert.Equal("1. e4 e5 2. Nf3", San(Position.StartFen, 14, "e2e4", "e7e5", "g1f3"));
        // Вариант, начатый ходом чёрных, открывается номером с многоточием.
        Assert.Equal("1... e5 2. Nf3", San(afterE4, 14, "e7e5", "g1f3"));
        Assert.Equal("1. e4 e5", San(Position.StartFen, 2, "e2e4", "e7e5", "g1f3"));  // длина ограничена
        // Невозможный ход обрывает вариант: дальше движок говорит уже о другой позиции.
        Assert.Equal("1. e4", San(Position.StartFen, 14, "e2e4", "e2e4", "g1f3"));
        Assert.Equal(string.Empty, San(Position.StartFen, 14, "мусор"));
        Assert.Equal(string.Empty, San(Position.StartFen, 14));
    }

    [Fact(DisplayName = "Главное окно: сообщение о ходе соперника")]
    public void СообщениеОХодеСоперника()
    {
        var profile = Difficulty.All[0];
        var move = Move.FromUci("g1f3");
        string Text(OpponentChoice choice) => (string)Call(null, "OpponentMoveText", profile, choice, "Nf3")!;

        Assert.Equal($"Соперник ({profile.Title}) сыграл наугад: Nf3.",
            Text(new OpponentChoice(move, OpponentPickKind.Random, 250)));
        Assert.Equal($"Соперник ({profile.Title}): Nf3 — уступка 1.25 пешки.",
            Text(new OpponentChoice(move, OpponentPickKind.Weighted, 125)));
        Assert.Equal($"Соперник ({profile.Title}): Nf3 — уступка 0.10 пешки.",
            Text(new OpponentChoice(move, OpponentPickKind.Weighted, 10)));

        // Уступку меньше десятой пешки не называем: это шум округления, а не поддавки.
        Assert.Equal($"Соперник ({profile.Title}): Nf3.",
            Text(new OpponentChoice(move, OpponentPickKind.Weighted, 9)));
        Assert.Equal($"Соперник ({profile.Title}): Nf3.",
            Text(new OpponentChoice(move, OpponentPickKind.Best, 0)));
    }
}
