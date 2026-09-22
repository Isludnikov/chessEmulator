using System.Globalization;
using System.Text;
using ChessEmulator.App;
using ChessEmulator.Chess;
using ChessEmulator.Engine;

namespace ChessEmulator.UI;

public sealed class MainForm : Form
{
    private enum PlayMode
    {
        Analysis,
        EngineBlack,
        EngineWhite
    }

    private readonly AppSettings _settings;
    private Game _game;
    private UciEngine _engine = new();

    private readonly BoardControl _board = new();
    private readonly EvalBar _evalBar = new();
    private readonly MoveListView _moveList = new();
    private readonly ListView _linesView = new();
    private readonly RichTextBox _adviceBox = new();
    private readonly TextBox _fenBox = new();
    private readonly TextBox _commentBox = new();
    private readonly ComboBox _playModeBox = new();
    private readonly ComboBox _difficultyBox = new();
    private readonly Label _difficultyHint = new();
    private readonly Button _hintButton = new();
    private readonly Button _playBestButton = new();

    /// <summary>
    /// Разброс ходов соперника на слабых уровнях. Поле, а не Random.Shared: всё крутится
    /// в потоке интерфейса, зато при нужде датчик можно посеять.
    /// </summary>
    private readonly Random _random = new();

    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _engineStatus = new();
    private readonly ToolStripStatusLabel _searchStatus = new();
    private readonly ToolStripStatusLabel _opponentStatus = new();
    private readonly ToolStripStatusLabel _resultStatus = new();
    private readonly ToolStripProgressBar _progress = new();

    private ToolStripMenuItem _analysisMenuItem = null!;
    private ToolStripButton _analysisButton = null!;
    private ToolStripMenuItem _engineHintsMenuItem = null!;
    private ToolStripMenuItem _arrowMenuItem = null!;

    /// <summary>Чем объясняем пустую панель советов, когда подсказки выключены.</summary>
    private const string HintsOffText = "Подсказки движка выключены (H включает).";

    // Редактор позиции
    private readonly PositionEditorPanel _editorPanel = new();
    private SplitContainer _rightSplit = null!;
    private Control _enginePanel = null!;
    private FlowLayoutPanel _navPanel = null!;
    private ToolStrip _toolStrip = null!;
    private ToolStripButton _editButton = null!;
    private ToolStripMenuItem _fileMenu = null!;
    private ToolStripMenuItem _engineMenu = null!;
    private bool _editing;
    private int _savedSplitterDistance;

    /// <summary>Узел, на котором плашку с результатом закрыли вручную: туда её не возвращаем.</summary>
    private MoveNode? _bannerDismissedFor;

    private readonly Dictionary<int, EngineInfo> _lines = new();
    private int _analysisGeneration;
    private bool _engineBusyWithMove;
    private CancellationTokenSource? _gameAnalysisCts;
    private bool _suppressCommentEvents;
    private readonly UciLog _uciLog = new();
    private UciLogForm? _uciLogForm;
    private bool _restartingEngine;
    private DateTime _lastEngineRestartUtc = DateTime.MinValue;

    public MainForm(string? pgnPath = null)
    {
        _settings = AppSettings.Load();
        _game = new Game();

        Text = "Chess Emulator — анализ партий со Stockfish";
        MinimumSize = new Size(1100, 720);
        ClientSize = new Size(1500, 950);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 30, 32);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        BuildUi();
        WireEvents();
        ApplyEngineHints();

        if (!string.IsNullOrEmpty(pgnPath) && ReadPgnFile(pgnPath) is { } startupGame)
        {
            _game = startupGame;
            _moveList.Game = _game;
        }
        RefreshAll();

        Shown += async (_, _) => await AutoStartEngineAsync();
    }

    // ------------------------------------------------------------ Интерфейс

    private void BuildUi()
    {
        var menu = BuildMenu();
        var toolStrip = BuildToolStrip();
        BuildStatusStrip();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 760,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(24, 24, 26)
        };

        // ---- левая часть: шкала оценки + доска
        var boardHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(30, 30, 32),
            Padding = new Padding(10)
        };
        boardHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        boardHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _evalBar.Dock = DockStyle.Fill;
        _evalBar.Margin = new Padding(0, 0, 8, 0);
        _board.Dock = DockStyle.Fill;
        _board.Flipped = _settings.BoardFlipped;
        _board.ShowCoordinates = _settings.ShowCoordinates;
        _board.ShowLegalMoveHints = _settings.ShowLegalMoveHints;

        boardHost.Controls.Add(_evalBar, 0, 0);
        boardHost.Controls.Add(_board, 1, 0);
        split.Panel1.Controls.Add(boardHost);

        // ---- правая часть: движок сверху, запись партии снизу
        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(24, 24, 26)
        };
        _rightSplit = rightSplit;

        // Панель редактора и панель движка занимают одно место и переключаются видимостью:
        // так не теряется состояние списка вариантов и не дёргается раскладка.
        _enginePanel = BuildEnginePanel();
        _enginePanel.Dock = DockStyle.Fill;
        _editorPanel.Dock = DockStyle.Fill;
        _editorPanel.Visible = false;

        rightSplit.Panel1.Controls.Add(_editorPanel);
        rightSplit.Panel1.Controls.Add(_enginePanel);
        rightSplit.Panel2.Controls.Add(BuildGamePanel());
        split.Panel2.Controls.Add(rightSplit);

        Controls.Add(split);
        Controls.Add(toolStrip);
        Controls.Add(menu);
        Controls.Add(_statusStrip);
        MainMenuStrip = menu;

        // Доска квадратная, поэтому левой панели хватает ширины по её высоте —
        // остальное место отдаём анализу и записи партии.
        void FitBoardPanel()
        {
            var available = split.ClientSize.Width - split.SplitterWidth;
            if (available <= 0) return;
            var desired = split.Panel1.ClientSize.Height + boardHost.Padding.Horizontal + 34;
            split.SplitterDistance = Math.Clamp(desired, 420, Math.Max(420, available - 420));
        }

        Load += (_, _) =>
        {
            FitBoardPanel();
            rightSplit.SplitterDistance = Math.Max(240, (int)(rightSplit.ClientSize.Height * 0.52));
        };
        split.SizeChanged += (_, _) => FitBoardPanel();
    }

    private Control BuildEnginePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.FromArgb(32, 32, 34),
            Padding = new Padding(8)
        };
        // Шапка в две строки: режим с кнопками и сложность. В одну строку они не влезают —
        // при минимальной ширине окна правой панели достаётся всего 420 точек.
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        var modeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };

        var difficultyRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };

        var modeLabel = new Label
        {
            Text = "Режим:",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Margin = new Padding(0, 6, 4, 0)
        };
        _playModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _playModeBox.Width = 190;
        _playModeBox.Items.AddRange(new object[]
        {
            "Анализ (ходят оба)",
            "Играю белыми — движок чёрными",
            "Играю чёрными — движок белыми"
        });
        _playModeBox.SelectedIndex = 0;

        StyleEngineButton(_hintButton, "Подсказка", "Показать лучший ход, не делая его", new Padding(10, 2, 0, 0));
        _hintButton.Click += async (_, _) => await PlayEngineMoveAsync(applyToBoard: false);

        StyleEngineButton(_playBestButton, "Сыграть лучший", "Сделать ход, который советует движок",
            new Padding(6, 2, 0, 0));
        _playBestButton.Click += async (_, _) => await PlayEngineMoveAsync(applyToBoard: true);

        var difficultyLabel = new Label
        {
            Text = "Сложность:",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Margin = new Padding(0, 6, 4, 0)
        };

        _difficultyBox.Name = "difficultyBox";
        _difficultyBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _difficultyBox.Width = 150;
        _difficultyBox.Items.AddRange(Difficulty.All.Select(p => (object)p.Title).ToArray());
        _difficultyBox.SelectedIndex = Difficulty.IndexOf(_settings.Difficulty);

        _difficultyHint.AutoSize = true;
        _difficultyHint.ForeColor = Color.DimGray;
        _difficultyHint.Margin = new Padding(8, 6, 0, 0);
        _difficultyHint.Text = CurrentDifficulty().Hint;

        modeRow.Controls.Add(modeLabel);
        modeRow.Controls.Add(_playModeBox);
        modeRow.Controls.Add(_hintButton);
        modeRow.Controls.Add(_playBestButton);

        difficultyRow.Controls.Add(difficultyLabel);
        difficultyRow.Controls.Add(_difficultyBox);
        difficultyRow.Controls.Add(_difficultyHint);

        header.Controls.Add(modeRow);
        header.Controls.Add(difficultyRow);

        _linesView.Dock = DockStyle.Fill;
        _linesView.View = View.Details;
        _linesView.FullRowSelect = true;
        _linesView.GridLines = false;
        _linesView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _linesView.BackColor = Color.FromArgb(38, 38, 41);
        _linesView.ForeColor = Color.Gainsboro;
        _linesView.BorderStyle = BorderStyle.None;
        _linesView.Columns.Add("Оценка", 80);
        _linesView.Columns.Add("Глубина", 70);
        _linesView.Columns.Add("Вариант", 520);
        _linesView.DoubleClick += OnEngineLineDoubleClick;

        _adviceBox.Dock = DockStyle.Fill;
        _adviceBox.ReadOnly = true;
        _adviceBox.BorderStyle = BorderStyle.None;
        _adviceBox.BackColor = Color.FromArgb(38, 38, 41);
        _adviceBox.ForeColor = Color.Gainsboro;
        _adviceBox.Font = new Font("Segoe UI", 9.5f);
        _adviceBox.Text = "Движок не запущен.";

        panel.Controls.Add(header, 0, 0);
        panel.Controls.Add(_linesView, 0, 1);
        panel.Controls.Add(_adviceBox, 0, 2);
        return panel;
    }

    private static void StyleEngineButton(Button button, string text, string tooltip, Padding margin)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 26;
        button.Margin = margin;
        button.FlatStyle = FlatStyle.Flat;
        button.ForeColor = Color.Gainsboro;
        button.BackColor = Color.FromArgb(52, 52, 56);
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 76);
        var tip = new ToolTip();
        tip.SetToolTip(button, tooltip);
    }

    private Control BuildGamePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.FromArgb(32, 32, 34),
            Padding = new Padding(8)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        _moveList.Dock = DockStyle.Fill;
        _moveList.Game = _game;

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _navPanel = nav;
        nav.Controls.Add(MakeNavButton("|<", "В начало (Home)", () => { _game.GoToStart(); RefreshAll(); }));
        nav.Controls.Add(MakeNavButton("<", "Назад (←)", () => { _game.GoBack(); RefreshAll(); }));
        nav.Controls.Add(MakeNavButton(">", "Вперёд (→)", () => { _game.GoForward(); RefreshAll(); }));
        nav.Controls.Add(MakeNavButton(">|", "В конец (End)", () => { _game.GoToEnd(); RefreshAll(); }));
        nav.Controls.Add(MakeNavButton("Флип", "Перевернуть доску (F)", ToggleFlip));
        nav.Controls.Add(MakeNavButton("Удалить", "Удалить ход и продолжение (Del)", DeleteCurrentMove));
        nav.Controls.Add(MakeNavButton("Вариант→", "Сделать вариант основной линией", () =>
        {
            _game.PromoteToMainLine();
            RefreshAll();
        }));

        _commentBox.Dock = DockStyle.Fill;
        _commentBox.Multiline = true;
        _commentBox.ScrollBars = ScrollBars.Vertical;
        _commentBox.BackColor = Color.FromArgb(38, 38, 41);
        _commentBox.ForeColor = Color.Gainsboro;
        _commentBox.BorderStyle = BorderStyle.FixedSingle;
        _commentBox.TextChanged += OnCommentChanged;

        _fenBox.Dock = DockStyle.Fill;
        _fenBox.ReadOnly = false;
        _fenBox.BackColor = Color.FromArgb(38, 38, 41);
        _fenBox.ForeColor = Color.Gainsboro;
        _fenBox.BorderStyle = BorderStyle.FixedSingle;
        _fenBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            // В режиме редактора FEN правит расстановку, а не начинает новую партию.
            if (_editing) _editorPanel.SetFen(_fenBox.Text);
            else _ = LoadFenAsync(_fenBox.Text);
        };

        panel.Controls.Add(_moveList, 0, 0);
        panel.Controls.Add(nav, 0, 1);
        panel.Controls.Add(_commentBox, 0, 2);
        panel.Controls.Add(_fenBox, 0, 3);
        return panel;
    }

    private static Button MakeNavButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(40, 26),
            Height = 26,
            Margin = new Padding(0, 2, 4, 2),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(52, 52, 56)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 76);
        button.Click += (_, _) => action();
        var tip = new ToolTip();
        tip.SetToolTip(button, tooltip);
        return button;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.Gainsboro,
            Renderer = new ToolStripProfessionalRenderer()
        };

        var file = new ToolStripMenuItem("Партия");
        file.DropDownItems.Add(new ToolStripMenuItem("Новая партия", null, async (_, _) => await NewGameAsync()) { ShortcutKeys = Keys.Control | Keys.N });
        file.DropDownItems.Add(new ToolStripMenuItem("Новая партия из позиции (FEN)…", null, async (_, _) => await NewGameFromFenAsync()));
        file.DropDownItems.Add(new ToolStripMenuItem("Редактор позиции…", null, async (_, _) => await BeginPositionEditAsync())
        {
            ShortcutKeys = Keys.Control | Keys.E
        });
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Открыть PGN…", null, async (_, _) => await OpenPgnAsync()) { ShortcutKeys = Keys.Control | Keys.O });
        file.DropDownItems.Add(new ToolStripMenuItem("Сохранить PGN…", null, (_, _) => SavePgn()) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Обрезать партию после текущего хода", null, (_, _) =>
        {
            if (_game.TruncateAfterCurrent()) RefreshAll();
        }));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Вставить PGN из буфера", null, async (_, _) => await PastePgnAsync()));
        file.DropDownItems.Add(new ToolStripMenuItem("Копировать PGN в буфер", null, (_, _) => CopyPgn()));
        file.DropDownItems.Add(new ToolStripMenuItem("Копировать FEN", null, (_, _) => CopyFen()) { ShortcutKeys = Keys.Control | Keys.C });
        file.DropDownItems.Add(new ToolStripMenuItem("Вставить FEN из буфера", null, async (_, _) => await LoadFenAsync(Clipboard.GetText())) { ShortcutKeys = Keys.Control | Keys.V });
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("Выход", null, (_, _) => Close()));

        var view = new ToolStripMenuItem("Вид");
        var flipItem = new ToolStripMenuItem("Перевернуть доску", null, (_, _) => ToggleFlip())
        {
            ShortcutKeyDisplayString = "F"
        };
        var coordsItem = new ToolStripMenuItem("Показывать координаты", null, (_, _) => { }) { CheckOnClick = true, Checked = _settings.ShowCoordinates };
        coordsItem.Click += (_, _) =>
        {
            _settings.ShowCoordinates = coordsItem.Checked;
            _board.ShowCoordinates = coordsItem.Checked;
            _board.Invalidate();
            _settings.Save();
        };
        var hintsItem = new ToolStripMenuItem("Подсвечивать возможные ходы", null, (_, _) => { }) { CheckOnClick = true, Checked = _settings.ShowLegalMoveHints };
        hintsItem.Click += (_, _) =>
        {
            _settings.ShowLegalMoveHints = hintsItem.Checked;
            _board.ShowLegalMoveHints = hintsItem.Checked;
            _board.Invalidate();
            _settings.Save();
        };
        _arrowMenuItem = new ToolStripMenuItem("Показывать стрелки лучших ходов", null, (_, _) => { }) { CheckOnClick = true, Checked = _settings.ShowBestMoveArrow };
        _arrowMenuItem.Click += (_, _) =>
        {
            _settings.ShowBestMoveArrow = _arrowMenuItem.Checked;
            if (!_arrowMenuItem.Checked) _board.ClearArrows();
            else UpdateArrows();
            _settings.Save();
        };

        // Этот пункт стоит отдельно от прочих настроек вида: он гасит не только показ, но и сам
        // анализ, поэтому ведёт себя как «Анализ включён» — галочку выставляет ApplyEngineHints,
        // иначе горячая клавиша и меню разошлись бы в показаниях.
        _engineHintsMenuItem = new ToolStripMenuItem("Подсказки движка", null, (_, _) => ToggleEngineHints())
        {
            CheckOnClick = false,
            Checked = _settings.ShowEngineHints,
            ShortcutKeyDisplayString = "H"
        };

        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            flipItem, coordsItem, hintsItem, _arrowMenuItem, new ToolStripSeparator(), _engineHintsMenuItem
        });

        var engineMenu = new ToolStripMenuItem("Движок");
        _analysisMenuItem = new ToolStripMenuItem("Анализ включён", null, (_, _) => ToggleAnalysis())
        {
            CheckOnClick = false,
            Checked = _settings.AutoAnalyze,
            ShortcutKeyDisplayString = "Пробел"
        };
        engineMenu.DropDownItems.Add(_analysisMenuItem);
        engineMenu.DropDownItems.Add(new ToolStripMenuItem("Разобрать всю партию…", null, async (_, _) => await AnalyzeWholeGameAsync()));
        engineMenu.DropDownItems.Add(new ToolStripSeparator());
        engineMenu.DropDownItems.Add(new ToolStripMenuItem("Настройки движка…", null, async (_, _) => await ShowEngineSettingsAsync()));
        engineMenu.DropDownItems.Add(new ToolStripMenuItem("Перезапустить движок", null, async (_, _) => await RestartEngineAsync()));
        engineMenu.DropDownItems.Add(new ToolStripSeparator());
        engineMenu.DropDownItems.Add(new ToolStripMenuItem("Журнал UCI…", null, (_, _) => ShowUciLog()));

        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add(new ToolStripMenuItem("Горячие клавиши и возможности", null, (_, _) => ShowHelp()));
        help.DropDownItems.Add(new ToolStripMenuItem("О программе", null, (_, _) => MessageBox.Show(this,
            "Chess Emulator\nИнтерактивная доска для разбора партий с движком Stockfish.\n\n" +
            "Windows Forms, .NET 10.", "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        _fileMenu = file;
        _engineMenu = engineMenu;

        menu.Items.AddRange(new ToolStripItem[] { file, view, engineMenu, help });
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip
        {
            BackColor = Color.FromArgb(45, 45, 48),
            ForeColor = Color.Gainsboro,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new ToolStripProfessionalRenderer(),
            ImageScalingSize = new Size(16, 16)
        };

        _analysisButton = new ToolStripButton("Анализ")
        {
            CheckOnClick = false,
            Checked = _settings.AutoAnalyze,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Включить/выключить постоянный анализ (пробел)"
        };
        _analysisButton.Click += (_, _) => ToggleAnalysis();

        var newGame = new ToolStripButton("Новая") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        newGame.Click += async (_, _) => await NewGameAsync();

        var open = new ToolStripButton("Открыть PGN") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        open.Click += async (_, _) => await OpenPgnAsync();

        var save = new ToolStripButton("Сохранить PGN") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        save.Click += (_, _) => SavePgn();

        var analyzeGame = new ToolStripButton("Разобрать партию") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        analyzeGame.Click += async (_, _) => await AnalyzeWholeGameAsync();

        var settings = new ToolStripButton("Движок…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        settings.Click += async (_, _) => await ShowEngineSettingsAsync();

        _editButton = new ToolStripButton("Редактор позиции")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "Расставить фигуры мышью (Ctrl+E)"
        };
        _editButton.Click += async (_, _) =>
        {
            if (_editing) EndPositionEdit(apply: false, fen: null);
            else await BeginPositionEditAsync();
        };

        strip.Items.AddRange(new ToolStripItem[]
        {
            newGame, open, save, _editButton, new ToolStripSeparator(),
            _analysisButton, analyzeGame, new ToolStripSeparator(), settings
        });
        _toolStrip = strip;
        return strip;
    }

    private void BuildStatusStrip()
    {
        _statusStrip.BackColor = Color.FromArgb(45, 45, 48);
        _statusStrip.ForeColor = Color.Gainsboro;
        _engineStatus.Text = "Движок: не запущен";
        _engineStatus.Spring = false;
        _searchStatus.Text = string.Empty;
        _searchStatus.Spring = true;
        _searchStatus.TextAlign = ContentAlignment.MiddleLeft;
        // Отдельная ячейка: строку поиска анализ переписывает на каждой строчке info,
        // и сообщение о ходе соперника исчезало бы через долю секунды после появления.
        _opponentStatus.Text = string.Empty;
        _opponentStatus.ForeColor = Color.FromArgb(220, 200, 140);
        _resultStatus.Text = "Партия идёт";
        _progress.Visible = false;
        _progress.Width = 160;
        _statusStrip.Items.AddRange(new ToolStripItem[]
        {
            _engineStatus, _searchStatus, _opponentStatus, _progress, _resultStatus
        });
    }

    private void WireEvents()
    {
        _board.MoveMade += (_, e) => ApplyUserMove(e.Move);
        _board.PromotionNeeded += (_, e) =>
        {
            using var dialog = new PromotionDialog(e.Color);
            e.Cancelled = dialog.ShowDialog(this) != DialogResult.OK;
            e.Selected = dialog.Selected;
        };

        _board.ResultBannerDismissed += (_, _) => _bannerDismissedFor = _game.Current;

        _board.EditSquareClicked += (_, e) => _editorPanel.HandleSquareClick(e.Square, e.Button);
        _board.EditPieceDragged += (_, e) => _editorPanel.HandlePieceDrag(e.From, e.To);

        _editorPanel.PositionChanged += (_, _) => SyncEditorToBoard();
        _editorPanel.Applied += (_, e) => EndPositionEdit(apply: true, fen: e.Fen);
        _editorPanel.Cancelled += (_, _) => EndPositionEdit(apply: false, fen: null);

        _moveList.NodeSelected += (_, node) =>
        {
            _game.GoTo(node);
            RefreshAll();
        };

        _playModeBox.SelectedIndexChanged += async (_, _) =>
        {
            await MaybeLetEngineMoveAsync();
        };

        // Движку тут не уходит ничего: постоянные параметры всегда полной силы, ослабление
        // собирается заново перед каждым ходом соперника. Поэтому смена уровня не способна
        // ни прервать анализ, ни сломать ход, который сейчас считается.
        _difficultyBox.SelectedIndexChanged += (_, _) =>
        {
            var index = Math.Clamp(_difficultyBox.SelectedIndex, 0, Difficulty.All.Count - 1);
            var level = Difficulty.All[index].Level;
            if (level == _settings.Difficulty) return;

            _settings.Difficulty = level;
            _settings.Save();

            var profile = CurrentDifficulty();
            _difficultyHint.Text = profile.Hint;
            _searchStatus.Text = $"Сложность соперника: {profile.Title} — {profile.Hint}.";
        };

        FormClosing += (_, _) =>
        {
            _gameAnalysisCts?.Cancel();
            _settings.BoardFlipped = _board.Flipped;
            _settings.Save();
            _engine.Dispose();
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_editing)
        {
            // В редакторе работают только выход по Esc и переворот доски.
            if (keyData == Keys.Escape)
            {
                EndPositionEdit(apply: false, fen: null);
                return true;
            }
            if (keyData == Keys.F && !_fenBox.Focused)
            {
                ToggleFlip();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        switch (keyData)
        {
            case Keys.Left:
                _game.GoBack();
                RefreshAll();
                return true;
            case Keys.Right:
                _game.GoForward();
                RefreshAll();
                return true;
            case Keys.Home:
                _game.GoToStart();
                RefreshAll();
                return true;
            case Keys.End:
                _game.GoToEnd();
                RefreshAll();
                return true;
            case Keys.Delete:
                if (!_commentBox.Focused && !_fenBox.Focused)
                {
                    DeleteCurrentMove();
                    return true;
                }
                break;
            case Keys.F:
                if (!_commentBox.Focused && !_fenBox.Focused)
                {
                    ToggleFlip();
                    return true;
                }
                break;
            case Keys.Space:
                if (!_commentBox.Focused && !_fenBox.Focused)
                {
                    ToggleAnalysis();
                    return true;
                }
                break;
            case Keys.H:
                if (!_commentBox.Focused && !_fenBox.Focused)
                {
                    ToggleEngineHints();
                    return true;
                }
                break;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------- Работа с партией

    private void ApplyUserMove(Chess.Move move)
    {
        if (_editing) return;
        _game.AddMove(move);

        // Если движок сейчас ответит, бесконечный анализ запускать незачем: его пришлось бы
        // тут же гасить ради поиска хода — лишний круг stop/go на каждом ходу.
        var engineAnswers = EngineShouldMove();
        RefreshAll(startAnalysis: !engineAnswers);
        if (engineAnswers) _ = MaybeLetEngineMoveAsync();
    }

    /// <summary>
    /// Единственный путь смены партии. Движок переводится в новую партию командой ucinewgame,
    /// а её нельзя посылать во время поиска: настоящий Stockfish на этом зависает навсегда.
    /// Поэтому идём через NewGameAsync, который сначала гасит анализ и дожидается bestmove.
    /// </summary>
    private async Task StartNewGameAsync(Game game)
    {
        _game = game;
        _moveList.Game = _game;
        _bannerDismissedFor = null;
        RefreshAll(startAnalysis: false);
        await ResetEngineForNewGameAsync();
        if (!EngineShouldMove()) await RefreshAnalysisAsync();
        await MaybeLetEngineMoveAsync();
    }

    private async Task ResetEngineForNewGameAsync()
    {
        if (!_engine.IsRunning) return;
        _analysisGeneration++;
        try
        {
            await _engine.NewGameAsync();
        }
        catch (EngineUnresponsiveException ex)
        {
            await HandleEngineWedgedAsync(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            _searchStatus.Text = "Движок недоступен: " + ex.Message;
        }
    }

    private Task NewGameAsync() => StartNewGameAsync(new Game());

    private async Task NewGameFromFenAsync()
    {
        var fen = Prompt("Введите позицию в формате FEN:", "Новая партия из позиции", Position.StartFen);
        if (string.IsNullOrWhiteSpace(fen)) return;
        await LoadFenAsync(fen);
    }

    private async Task LoadFenAsync(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return;
        Game game;
        try
        {
            var position = Position.FromFen(fen.Trim());
            game = new Game(position.ToFen());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось разобрать FEN: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await StartNewGameAsync(game);
    }

    private void DeleteCurrentMove()
    {
        if (_game.Current.IsRoot) return;
        _game.DeleteCurrent();
        RefreshAll();
    }

    private void ToggleFlip()
    {
        _board.Flipped = !_board.Flipped;
        _settings.BoardFlipped = _board.Flipped;
        _settings.Save();
    }

    private async Task OpenPgnAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Открыть партию",
            Filter = "Партии PGN (*.pgn)|*.pgn|Все файлы (*.*)|*.*"
        };
        if (Directory.Exists(_settings.LastPgnDirectory)) dialog.InitialDirectory = _settings.LastPgnDirectory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (ReadPgnFile(dialog.FileName) is { } game) await StartNewGameAsync(game);
    }

    /// <summary>Читает партию из файла. Возвращает null, если пользователь отказался или файл не подошёл.</summary>
    private Game? ReadPgnFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var games = Pgn.ReadAll(text, 200);
            if (games.Count == 0)
            {
                MessageBox.Show(this, "В файле не найдено партий.", "Открытие PGN",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            var game = games.Count == 1 ? games[0] : ChooseGame(games);
            if (game == null) return null;

            _settings.LastPgnDirectory = Path.GetDirectoryName(path);
            _settings.Save();
            return game;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось прочитать файл: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private Game? ChooseGame(List<Game> games)
    {
        using var dialog = new Form
        {
            Text = "Выберите партию",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(640, 420),
            FormBorderStyle = FormBorderStyle.SizableToolWindow
        };

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        foreach (var g in games)
        {
            list.Items.Add($"{Header(g, "White")} — {Header(g, "Black")}  ({Header(g, "Result")}, {Header(g, "Date")}, {Header(g, "Event")})");
        }
        list.SelectedIndex = 0;
        list.DoubleClick += (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); };

        var ok = new Button { Text = "Открыть", Dock = DockStyle.Bottom, Height = 34, DialogResult = DialogResult.OK };
        dialog.Controls.Add(list);
        dialog.Controls.Add(ok);
        dialog.AcceptButton = ok;

        return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0
            ? games[list.SelectedIndex]
            : null;

        static string Header(Game g, string key) => g.Headers.TryGetValue(key, out var v) ? v : "?";
    }

    private void SavePgn()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить партию",
            Filter = "Партии PGN (*.pgn)|*.pgn",
            FileName = BuildFileName()
        };
        if (Directory.Exists(_settings.LastPgnDirectory)) dialog.InitialDirectory = _settings.LastPgnDirectory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, Pgn.Write(_game), new UTF8Encoding(false));
            _settings.LastPgnDirectory = Path.GetDirectoryName(dialog.FileName);
            _settings.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить файл: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string BuildFileName()
    {
        var white = _game.Headers.TryGetValue("White", out var w) ? w : "White";
        var black = _game.Headers.TryGetValue("Black", out var b) ? b : "Black";
        var name = $"{white}-{black}-{DateTime.Now:yyyyMMdd}.pgn";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private void CopyPgn()
    {
        Clipboard.SetText(Pgn.Write(_game));
        _searchStatus.Text = "PGN скопирован в буфер обмена.";
    }

    private async Task PastePgnAsync()
    {
        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) return;
        Game game;
        try
        {
            game = Pgn.Read(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось разобрать PGN: " + ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await StartNewGameAsync(game);
    }

    private void CopyFen()
    {
        Clipboard.SetText(_game.CurrentPosition.ToFen());
        _searchStatus.Text = "FEN скопирован в буфер обмена.";
    }

    private void OnCommentChanged(object? sender, EventArgs e)
    {
        if (_suppressCommentEvents || _editing || _game.Current.IsRoot) return;
        _game.Current.Comment = string.IsNullOrWhiteSpace(_commentBox.Text) ? null : _commentBox.Text.Trim();
        _moveList.Reload();
    }

    // ----------------------------------------------------- Редактор позиции

    /// <summary>Включает свободную расстановку фигур на главной доске.</summary>
    private async Task BeginPositionEditAsync()
    {
        if (_editing) return;

        if (_gameAnalysisCts != null)
        {
            MessageBox.Show(this, "Идёт разбор партии. Дождитесь его окончания или прервите разбор.",
                "Редактор позиции", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_engineBusyWithMove)
        {
            MessageBox.Show(this, "Движок сейчас обдумывает ход.",
                "Редактор позиции", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Флаг ставим до ожидания: запоздавшие ответы движка не должны трогать доску.
        _editing = true;
        _analysisGeneration++;
        if (_engine.IsRunning) await _engine.StopSearchAsync();

        _editorPanel.LoadFrom(_game.CurrentPosition);

        _board.Mode = BoardMode.Edit;
        _board.LastMove = Chess.Move.None;
        _lines.Clear();
        _linesView.Items.Clear();
        _board.ClearArrows();
        _opponentStatus.Text = string.Empty;
        _adviceBox.Text = "Редактор позиции: анализ приостановлен.";
        _evalBar.Clear();
        UpdateEngineControlsEnabled();

        _savedSplitterDistance = _rightSplit.SplitterDistance;
        _rightSplit.SplitterDistance = Math.Min(
            Math.Max(_rightSplit.SplitterDistance, 430),
            Math.Max(_rightSplit.Panel1MinSize, _rightSplit.Height - _rightSplit.Panel2MinSize - _rightSplit.SplitterWidth));

        _enginePanel.Visible = false;
        _editorPanel.Visible = true;
        _editButton.Text = "Выйти из редактора";
        SetPlayControlsEnabled(false);

        _resultStatus.Text = "Редактор позиции";
        _searchStatus.Text = "Выберите фигуру в палитре и щёлкайте по доске. Правая кнопка мыши убирает фигуру.";
        SyncEditorToBoard();
    }

    /// <summary>Выходит из редактора: либо начинает партию с расставленной позиции, либо отменяет правки.</summary>
    private void EndPositionEdit(bool apply, string? fen)
    {
        if (!_editing) return;

        if (apply && fen != null && _game.MainLine().Count > 0)
        {
            var answer = MessageBox.Show(this,
                "Будет создана новая партия с этой позиции, а ходы текущей партии потеряются.\r\n\r\nПродолжить?",
                "Редактор позиции", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        _editing = false;
        _board.Mode = BoardMode.Play;
        _editorPanel.Visible = false;
        _enginePanel.Visible = true;
        _editButton.Text = "Редактор позиции";
        SetPlayControlsEnabled(true);

        // SetPlayControlsEnabled включает панель инструментов целиком, в том числе кнопку
        // «Анализ», — возвращаем ей состояние, которое диктуют подсказки.
        ApplyEngineHints();

        if (_savedSplitterDistance > 0) _rightSplit.SplitterDistance = _savedSplitterDistance;

        if (apply && fen != null) _ = LoadFenAsync(fen);
        else RefreshAll();
    }

    /// <summary>Показывает редактируемую расстановку на доске и в поле FEN.</summary>
    private void SyncEditorToBoard()
    {
        if (!_editing) return;
        _board.EditBrush = _editorPanel.Brush;
        _board.Position = _editorPanel.Builder.ToPosition();
        _fenBox.Text = _editorPanel.Builder.ToFen();
    }

    /// <summary>Отключает всё, что относится к игре и движку, пока идёт расстановка.</summary>
    private void SetPlayControlsEnabled(bool enabled)
    {
        _moveList.Enabled = enabled;
        _navPanel.Enabled = enabled;
        _commentBox.Enabled = enabled && !_game.Current.IsRoot;
        _playModeBox.Enabled = enabled;
        _difficultyBox.Enabled = enabled;

        // Пункты меню отключаем целиком: их горячие клавиши (Ctrl+N, Ctrl+V и другие)
        // иначе сработают прямо из-под редактора и подменят партию.
        _fileMenu.Enabled = enabled;
        _engineMenu.Enabled = enabled;

        foreach (ToolStripItem item in _toolStrip.Items)
        {
            if (!ReferenceEquals(item, _editButton)) item.Enabled = enabled;
        }
    }

    // -------------------------------------------------------- Обновление UI

    private void RefreshAll(bool startAnalysis = true)
    {
        // В режиме редактора доской управляет панель расстановки, а не текущая партия.
        if (_editing) return;

        var node = _game.Current;
        var position = node.Position;

        _board.Position = position;
        _board.LastMove = node.IsRoot ? Chess.Move.None : node.Move;
        _board.InteractionEnabled = !_engineBusyWithMove && position.LegalMoves.Count > 0;

        _fenBox.Text = position.ToFen();

        _suppressCommentEvents = true;
        _commentBox.Text = node.Comment ?? string.Empty;
        _commentBox.Enabled = !node.IsRoot;
        _suppressCommentEvents = false;

        _moveList.Reload();
        _moveList.ScrollToCurrent();

        UpdateResultStatus();
        UpdateStaticEvaluation();

        ResetEngineOutput();
        UpdateEngineControlsEnabled();

        if (startAnalysis) _ = RefreshAnalysisAsync();
    }

    /// <summary>
    /// Сбрасывает всё, что показывает движок. Без этого на экране оставался текст разбора
    /// предыдущей позиции — он выглядит как ответ на текущую и сбивает с толку.
    /// </summary>
    private void ResetEngineOutput()
    {
        _lines.Clear();
        _linesView.Items.Clear();
        _board.ClearArrows();

        // Сообщение о ходе соперника относится к предыдущей позиции: при переходе на другой
        // ход оно перестаёт быть правдой. Ход соперника выставит его заново уже после RefreshAll.
        _opponentStatus.Text = string.Empty;

        // Проверяем раньше запущенности движка: панель пуста по воле пользователя, а состояние
        // движка и так видно в строке внизу.
        if (!_settings.ShowEngineHints)
        {
            _adviceBox.Text = HintsOffText;
            return;
        }

        if (!_engine.IsRunning)
        {
            _adviceBox.Text = "Движок не запущен.";
            return;
        }

        _adviceBox.Text = _settings.AutoAnalyze
            ? "Анализ…"
            : "Анализ выключен (пробел включает).";
    }

    /// <summary>Кнопки движка доступны, только когда он свободен.</summary>
    private void UpdateEngineControlsEnabled()
    {
        var free = _engine.IsRunning && !_engineBusyWithMove && _gameAnalysisCts == null && !_editing;
        _hintButton.Enabled = free;
        _playBestButton.Enabled = free;
    }

    private void UpdateResultStatus()
    {
        var node = _game.Current;
        var (state, reason) = _game.EvaluateState(node);

        _resultStatus.Text = GameResultText.StatusLine(state, reason, node.Position);
        _resultStatus.ForeColor = GameResultText.StatusColor(state);

        UpdateResultBanner(node, state, reason);
    }

    /// <summary>
    /// Показывает или снимает плашку с результатом. Закрытую вручную плашку не возвращаем,
    /// пока не уйдём с этого хода.
    /// </summary>
    private void UpdateResultBanner(MoveNode node, GameResultState state, GameEndReason reason)
    {
        // Уходя с хода, забываем о закрытии: вернулись в конец партии — плашка снова на месте.
        if (!ReferenceEquals(node, _bannerDismissedFor)) _bannerDismissedFor = null;

        if (!GameResultText.ShouldShowBanner(state, node) || _bannerDismissedFor != null)
        {
            _board.ClearResultBanner();
            return;
        }

        _board.ShowResultBanner(
            GameResultText.Headline(state, reason),
            GameResultText.Score(state),
            GameResultText.BannerStyle(state));
    }

    /// <summary>Пока движок не дал оценку, показываем материальный баланс.</summary>
    private void UpdateStaticEvaluation()
    {
        var node = _game.Current;
        if (node.EvalCp.HasValue || node.MateIn.HasValue)
        {
            _evalBar.SetEvaluation(node.EvalCp, node.MateIn);
            return;
        }
        // Пока движок не оценил новую позицию, показываем материал: иначе на шкале осталась бы
        // оценка предыдущей позиции.
        _evalBar.SetEvaluation(_game.CurrentPosition.MaterialBalance() * 100, null);
    }

    // ------------------------------------------------------------- Движок

    private async Task AutoStartEngineAsync()
    {
        var path = _settings.EnginePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) path = EngineLocator.FindFirst();

        if (string.IsNullOrWhiteSpace(path))
        {
            _engineStatus.Text = "Движок: не найден";
            _adviceBox.Text =
                "Stockfish не найден.\r\n\r\n" +
                "Скачайте движок с stockfishchess.org/download, распакуйте и укажите путь: меню «Движок» → «Настройки движка…».\r\n" +
                "Можно также положить stockfish.exe в папку engine рядом с программой — он подхватится сам.";
            return;
        }

        await StartEngineAsync(path);
    }

    private async Task StartEngineAsync(string path)
    {
        try
        {
            _engineStatus.Text = "Движок: запуск…";
            _uciLog.Add("* запуск движка: " + path);
            _engine.Dispose();
            _engine = new UciEngine();
            _engine.LogReceived += _uciLog.Append;
            _engine.InfoReceived += OnEngineInfo;
            _engine.Exited += (_, _) =>
            {
                _engineStatus.Text = "Движок: завершился";
                UpdateEngineControlsEnabled();
            };
            _engine.Unresponsive += (_, reason) => _ = HandleEngineWedgedAsync(reason);

            await _engine.StartAsync(path);
            await ApplyEngineOptionsAsync();
            await _engine.NewGameAsync();

            _settings.EnginePath = path;
            _settings.Save();

            _engineStatus.Text = $"Движок: {_engine.Name}";
            // Имя движка уже видно в строке состояния, поэтому при выключенных подсказках
            // панель объясняет, почему она пуста, а не повторяет его.
            _adviceBox.Text = _settings.ShowEngineHints ? $"{_engine.Name} готов к работе." : HintsOffText;
            UpdateEngineControlsEnabled();
            await RefreshAnalysisAsync();
        }
        catch (Exception ex)
        {
            _engineStatus.Text = "Движок: ошибка запуска";
            _adviceBox.Text = "Не удалось запустить движок: " + ex.Message;
            UpdateEngineControlsEnabled();
        }
    }

    /// <summary>
    /// Передаёт движку постоянные настройки. Неподдерживаемые параметры обёртка пропускает сама.
    /// Сила тут всегда полная: ослабление живёт ровно один ход соперника, чтобы шкала оценки,
    /// стрелки, подсказки и разбор партии не врали вслед за выбранным уровнем.
    /// </summary>
    private async Task ApplyEngineOptionsAsync()
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("Threads", _settings.Threads.ToString()),
            new("Hash", _settings.HashMb.ToString())
        };
        options.AddRange(Difficulty.FullStrengthOptions(_settings.MultiPv));

        await _engine.ApplyOptionsAsync(options);
    }

    /// <summary>Сложность соперника с подставленными ручными настройками диалога.</summary>
    private DifficultyProfile CurrentDifficulty() => Difficulty.Resolve(
        _settings.Difficulty,
        new DifficultyDefaults(_settings.SkillLevel, _settings.LimitStrength,
            _settings.EloRating, _settings.EngineMoveTimeMs, _settings.MultiPv));

    /// <summary>
    /// Возврат к полной силе после хода соперника. Threads и Hash сюда не входят: у обоих
    /// в Stockfish есть обработчик изменения — хеш пересоздаёт и чистит таблицу перестановок,
    /// потоки пересоздают пул. Дважды на каждый ход это заметная просадка.
    /// </summary>
    private async Task RestoreFullStrengthAsync()
    {
        try
        {
            await _engine.ApplyOptionsAsync(Difficulty.FullStrengthOptions(_settings.MultiPv));
        }
        catch (Exception ex) when (ex is EngineUnresponsiveException or InvalidOperationException
                                       or TimeoutException or OperationCanceledException)
        {
            // Движок уже снят или закрыт: полную силу вернёт перезапуск, он выставляет
            // параметры заново. Бросать отсюда нельзя — мы внутри finally и затёрли бы
            // настоящую причину сбоя.
        }
    }

    /// <summary>
    /// Ход соперника ищется ослабленным движком, но ослабление живёт ровно один поиск:
    /// полная сила возвращается до того, как ход попадёт на доску и запустится анализ.
    /// </summary>
    private async Task<SearchResult> SearchOpponentMoveAsync(DifficultyProfile profile)
    {
        var weakened = false;
        try
        {
            if (!profile.IsFullStrength)
            {
                // ApplyOptionsAsync сам гасит текущий поиск и дожидается bestmove:
                // setoption во время поиска настоящий Stockfish не переживает.
                await _engine.ApplyOptionsAsync(
                    Difficulty.OpponentOptions(profile, _engine.FindOption("UCI_Elo")));
                weakened = true;
            }

            var limits = new SearchLimits
            {
                // Время ставим всегда, даже когда ограничение задаёт глубина: без него
                // у поиска не будет жёсткого срока и его сторожит только молчание движка.
                MoveTimeMs = profile.MoveTimeMs,
                Depth = profile.DepthLimit > 0 ? profile.DepthLimit : null
            };
            return await _engine.GoAsync(_game.StartFen, _game.UciMovesToCurrent(), limits);
        }
        finally
        {
            if (weakened) await RestoreFullStrengthAsync();
        }
    }

    /// <summary>
    /// Движок перестал отвечать: процесс уже снят обёрткой, поднимаем его заново.
    /// Повторный отказ в течение полуминуты не перезапускаем — иначе получится карусель.
    /// </summary>
    private async Task HandleEngineWedgedAsync(string reason)
    {
        if (_restartingEngine) return;

        _uciLog.Add("* движок не отвечает: " + reason);
        SaveHangReport(reason);

        _engineStatus.Text = "Движок: не отвечает";
        _engineBusyWithMove = false;
        UpdateEngineControlsEnabled();
        _board.InteractionEnabled = !_editing && _game.CurrentPosition.LegalMoves.Count > 0;

        var path = _settings.EnginePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _searchStatus.Text = "Движок не отвечает: " + reason;
            return;
        }

        if (DateTime.UtcNow - _lastEngineRestartUtc < TimeSpan.FromSeconds(30))
        {
            _searchStatus.Text = "Движок не отвечает: " + reason;
            _adviceBox.Text =
                "Движок повторно перестал отвечать (" + reason + ").\r\n\r\n" +
                "Проверьте путь и настройки: меню «Движок» → «Настройки движка…», " +
                "затем «Перезапустить движок».";
            return;
        }

        _restartingEngine = true;
        _lastEngineRestartUtc = DateTime.UtcNow;
        _searchStatus.Text = "Движок не отвечает — перезапуск…";
        try
        {
            await StartEngineAsync(path!);
        }
        catch (Exception ex)
        {
            _searchStatus.Text = "Не удалось перезапустить движок: " + ex.Message;
        }
        finally
        {
            _restartingEngine = false;
        }
    }

    private void ShowUciLog()
    {
        if (_uciLogForm is { IsDisposed: false })
        {
            _uciLogForm.Activate();
            return;
        }

        _uciLogForm = new UciLogForm(_uciLog);
        _uciLogForm.FormClosed += (_, _) => _uciLogForm = null;
        _uciLogForm.Show(this);
    }

    /// <summary>Сохраняет хвост журнала рядом с настройками — чтобы сбой можно было разобрать потом.</summary>
    private void SaveHangReport(string reason)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChessEmulator", "uci-hang.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var report = new StringBuilder();
            report.AppendLine($"{DateTime.Now:u}  движок не отвечает: {reason}");
            foreach (var entry in _uciLog.Tail(200)) report.AppendLine("    " + entry);
            report.AppendLine();
            File.AppendAllText(path, report.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Журнал не критичен: не смогли записать — работаем дальше.
        }
    }

    private async Task RestartEngineAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.EnginePath))
        {
            await ShowEngineSettingsAsync();
            return;
        }
        await StartEngineAsync(_settings.EnginePath!);
    }

    private async Task ShowEngineSettingsAsync()
    {
        var previousPath = _settings.EnginePath;
        using var dialog = new EngineSettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (!string.Equals(previousPath, _settings.EnginePath, StringComparison.OrdinalIgnoreCase) ||
            !_engine.IsRunning)
        {
            if (!string.IsNullOrWhiteSpace(_settings.EnginePath)) await StartEngineAsync(_settings.EnginePath!);
        }
        else
        {
            // ApplyOptionsAsync сам дождётся окончания поиска: setoption во время поиска
            // настоящий Stockfish не переживает.
            try
            {
                await ApplyEngineOptionsAsync();
            }
            catch (EngineUnresponsiveException ex)
            {
                await HandleEngineWedgedAsync(ex.Message);
                return;
            }
            await RefreshAnalysisAsync();
        }
    }

    /// <summary>
    /// Главный выключатель подсказок: убирает всё, что называет лучший ход, и заодно
    /// останавливает постоянный анализ — прятать результат, продолжая его считать, значит
    /// греть процессор впустую.
    /// </summary>
    private void ToggleEngineHints()
    {
        _settings.ShowEngineHints = !_settings.ShowEngineHints;
        _settings.Save();
        ApplyEngineHints();
        _ = RefreshAnalysisAsync();
    }

    /// <summary>Приводит интерфейс в соответствие с настройкой подсказок.</summary>
    private void ApplyEngineHints()
    {
        var on = _settings.ShowEngineHints;
        _engineHintsMenuItem.Checked = on;

        // Подчинённые переключатели гасим, а не переписываем: их галочки показывают запомненное
        // состояние, к которому вернёмся, когда подсказки включат обратно.
        _arrowMenuItem.Enabled = on;
        _analysisMenuItem.Enabled = on;
        _analysisButton.Enabled = on;

        // Пустая таблица с шапкой «Оценка · Глубина · Вариант» выглядит как поломка, поэтому
        // без подсказок убираем и шапку. Строку разметки не сворачиваем: высоты у панели
        // фиксированные, и вместо списка осталась бы дыра.
        _linesView.HeaderStyle = on ? ColumnHeaderStyle.Nonclickable : ColumnHeaderStyle.None;

        if (_editing) return;

        // Анализ встал — шкала возвращается к материалу или к сохранённой оценке разбора.
        if (!on) UpdateStaticEvaluation();
        ResetEngineOutput();
    }

    private void ToggleAnalysis()
    {
        // Пункт меню и кнопка сейчас серые, так что сюда попадают только пробелом. Промолчать
        // нельзя: клавиша выглядела бы сломанной.
        if (!_settings.ShowEngineHints)
        {
            _searchStatus.Text = HintsOffText;
            return;
        }

        _settings.AutoAnalyze = !_settings.AutoAnalyze;
        _settings.Save();
        _analysisMenuItem.Checked = _settings.AutoAnalyze;
        _analysisButton.Checked = _settings.AutoAnalyze;
        _ = RefreshAnalysisAsync();
    }

    private async Task RefreshAnalysisAsync()
    {
        var generation = ++_analysisGeneration;

        if (!_engine.IsRunning) return;
        await _engine.StopSearchAsync();
        if (generation != _analysisGeneration) return;

        if (_editing)
        {
            _searchStatus.Text = "Редактор позиции — анализ приостановлен.";
            return;
        }

        if (!_settings.AnalysisRuns || _engineBusyWithMove || _gameAnalysisCts != null)
        {
            _searchStatus.Text = string.Empty;
            return;
        }

        var position = _game.CurrentPosition;
        if (position.LegalMoves.Count == 0)
        {
            _searchStatus.Text = "Позиция окончена — анализ не требуется.";
            return;
        }

        var limits = _settings.AnalysisDepthLimit > 0
            ? SearchLimits.ByDepth(_settings.AnalysisDepthLimit)
            : SearchLimits.AsInfinite();

        if (generation != _analysisGeneration) return;

        try
        {
            await _engine.GoAsync(_game.StartFen, _game.UciMovesToCurrent(), limits);
        }
        catch (EngineUnresponsiveException)
        {
            // Движок снят обёрткой; перезапуском займётся обработчик Unresponsive.
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or TimeoutException)
        {
            // Поиск прерван сменой позиции или остановкой движка.
        }
    }

    private void OnEngineInfo(object? sender, EngineInfo info)
    {
        if (!_settings.ShowEngineHints || _gameAnalysisCts != null || _engineBusyWithMove || _editing) return;
        if (info.Pv.Length == 0) return;

        var position = _game.CurrentPosition;
        var firstMove = Chess.Move.FromUci(info.Pv[0]);
        if (!position.TryFindMove(firstMove.From, firstMove.To, firstMove.Promotion, out _)) return;

        _lines[info.MultiPv] = info;
        UpdateLinesView();
        UpdateArrows();

        var whiteToMove = position.SideToMove == PieceColor.White;
        if (_lines.TryGetValue(1, out var best))
        {
            _evalBar.SetEvaluation(best.WhiteCp(whiteToMove), best.WhiteMate(whiteToMove));
            _searchStatus.Text =
                $"глубина {best.Depth}/{best.SelDepth}  •  {FormatNumber(best.Nodes)} узлов  •  " +
                $"{FormatNumber(best.Nps)} узлов/с  •  {best.TimeMs / 1000.0:0.0} с";
            UpdateAdvice(best);
        }
    }

    private static string FormatNumber(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000.0:0.0} млрд",
        >= 1_000_000 => $"{value / 1_000_000.0:0.0} млн",
        >= 1_000 => $"{value / 1_000.0:0.0} тыс.",
        _ => value.ToString()
    };

    private void UpdateLinesView()
    {
        var position = _game.CurrentPosition;
        var whiteToMove = position.SideToMove == PieceColor.White;

        _linesView.BeginUpdate();
        _linesView.Items.Clear();
        foreach (var pair in _lines.OrderBy(p => p.Key))
        {
            var info = pair.Value;
            var item = new ListViewItem(info.ScoreText(whiteToMove));
            item.SubItems.Add(info.Depth.ToString());
            item.SubItems.Add(PvToSan(position, info.Pv, 14));
            item.Tag = info;
            if (pair.Key == 1) item.ForeColor = Color.FromArgb(150, 220, 150);
            _linesView.Items.Add(item);
        }
        _linesView.EndUpdate();
    }

    private void UpdateArrows()
    {
        if (!_settings.ShowBestMoveArrow || _lines.Count == 0)
        {
            _board.ClearArrows();
            return;
        }

        var arrows = new List<BoardArrow>();
        foreach (var pair in _lines.OrderByDescending(p => p.Key))
        {
            if (pair.Value.Pv.Length == 0) continue;
            var move = Chess.Move.FromUci(pair.Value.Pv[0]);
            if (move.IsNone) continue;

            var color = pair.Key == 1
                ? Color.FromArgb(200, 90, 180, 90)
                : Color.FromArgb(110, 90, 140, 200);
            arrows.Add(new BoardArrow(move.From, move.To, color, pair.Key == 1 ? 1f : 0.7f));
        }
        _board.SetArrows(arrows);
    }

    private void UpdateAdvice(EngineInfo best)
    {
        var position = _game.CurrentPosition;
        var whiteToMove = position.SideToMove == PieceColor.White;
        var sb = new StringBuilder();

        var side = whiteToMove ? "белых" : "чёрных";
        sb.AppendLine($"Ход {side}. Оценка: {best.ScoreText(whiteToMove)} (плюс — перевес белых).");

        if (best.Pv.Length > 0)
        {
            var move = Chess.Move.FromUci(best.Pv[0]);
            if (position.TryFindMove(move.From, move.To, move.Promotion, out var legal))
                sb.AppendLine($"Лучший ход: {position.ToSan(legal)}.");
            sb.AppendLine($"Главный вариант: {PvToSan(position, best.Pv, 10)}");
        }

        var node = _game.Current;
        if (!node.IsRoot && node.Parent is { } parent && parent.EvalCp.HasValue && node.EvalCp.HasValue)
        {
            var loss = node.IsWhiteMove
                ? parent.EvalCp.Value - node.EvalCp.Value
                : node.EvalCp.Value - parent.EvalCp.Value;
            if (loss > 30)
                sb.AppendLine($"Сделанный ход {node.San} уступает лучшему примерно " +
                              $"{(loss / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} пешки.");
        }

        _adviceBox.Text = sb.ToString();
    }

    private static string PvToSan(Position start, IReadOnlyList<string> pv, int maxMoves)
    {
        var sb = new StringBuilder();
        var position = start;
        var count = 0;

        foreach (var uci in pv)
        {
            if (count >= maxMoves) break;
            var raw = Chess.Move.FromUci(uci);
            if (!position.TryFindMove(raw.From, raw.To, raw.Promotion, out var move)) break;

            if (position.SideToMove == PieceColor.White) sb.Append(position.FullmoveNumber).Append(". ");
            else if (count == 0) sb.Append(position.FullmoveNumber).Append("... ");

            sb.Append(position.ToSan(move)).Append(' ');
            position = position.MakeMove(move);
            count++;
        }

        return sb.ToString().Trim();
    }

    private void OnEngineLineDoubleClick(object? sender, EventArgs e)
    {
        if (_linesView.SelectedItems.Count == 0) return;
        if (_linesView.SelectedItems[0].Tag is not EngineInfo info || info.Pv.Length == 0) return;

        var raw = Chess.Move.FromUci(info.Pv[0]);
        if (_game.CurrentPosition.TryFindMove(raw.From, raw.To, raw.Promotion, out var move)) ApplyUserMove(move);
    }

    // ---------------------------------------------------- Ходы движка и разбор

    private PlayMode CurrentMode => (PlayMode)Math.Max(0, _playModeBox.SelectedIndex);

    private bool IsEngineTurn()
    {
        var side = _game.CurrentPosition.SideToMove;
        return CurrentMode switch
        {
            PlayMode.EngineBlack => side == PieceColor.Black,
            PlayMode.EngineWhite => side == PieceColor.White,
            _ => false
        };
    }

    /// <summary>Сейчас очередь движка ходить, и он может это сделать.</summary>
    private bool EngineShouldMove() =>
        !_editing && IsEngineTurn() && _engine.IsRunning && _gameAnalysisCts == null
        && _game.CurrentPosition.LegalMoves.Count > 0;

    private async Task MaybeLetEngineMoveAsync()
    {
        if (!EngineShouldMove()) return;
        await PlayEngineMoveAsync(applyToBoard: true, asOpponent: true);
    }

    /// <summary>
    /// Ищет ход движком. Сложность применяется только когда движок играет за соперника:
    /// «Подсказка» и «Сыграть лучший» отвечают на вопрос «а как надо?» и обязаны советовать
    /// сильнейший ход, иначе человек сходил бы по совету и получил разнос от шкалы оценки.
    /// </summary>
    private async Task PlayEngineMoveAsync(bool applyToBoard, bool asOpponent = false)
    {
        if (_editing) return;
        if (!_engine.IsRunning)
        {
            MessageBox.Show(this, "Движок не запущен. Укажите путь к Stockfish в настройках движка.",
                "Движок", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_engineBusyWithMove || _gameAnalysisCts != null) return;
        if (_game.CurrentPosition.LegalMoves.Count == 0) return;

        _engineBusyWithMove = true;
        UpdateEngineControlsEnabled();
        _board.InteractionEnabled = false;
        _searchStatus.Text = "Движок думает…";

        try
        {
            _analysisGeneration++;
            await _engine.StopSearchAsync();

            var profile = asOpponent ? CurrentDifficulty() : Difficulty.For(DifficultyLevel.Maximum);
            var started = Environment.TickCount64;

            var result = asOpponent
                ? await SearchOpponentMoveAsync(profile)
                : await _engine.GoAsync(_game.StartFen, _game.UciMovesToCurrent(),
                    SearchLimits.ByTime(_settings.EngineMoveTimeMs));
            // Дальше движок снова на полной силе: возврат сделан внутри SearchOpponentMoveAsync,
            // до того как ход попадёт на доску и RefreshAll запустит бесконечный анализ.

            var choice = OpponentMovePicker.Pick(result, _game.CurrentPosition, profile, _random);
            if (!choice.Found)
            {
                _searchStatus.Text = "Движок не предложил ход.";
                return;
            }

            var move = choice.Move;

            // На слабом уровне ход находится за единицы миллисекунд, а мгновенный ответ
            // выглядит поломкой программы, а не игрой соперника.
            if (asOpponent && profile.MinThinkMs > 0)
            {
                var left = profile.MinThinkMs - (int)(Environment.TickCount64 - started);
                if (left > 0) await Task.Delay(left);
            }

            if (applyToBoard)
            {
                var san = _game.CurrentPosition.ToSan(move);
                _engineBusyWithMove = false;
                _game.AddMove(move);
                RefreshAll();
                // После RefreshAll: он сбрасывает ячейку вместе с остальным выводом движка.
                if (asOpponent) _opponentStatus.Text = OpponentMoveText(profile, choice, san);
                await MaybeLetEngineMoveAsync();
            }
            else
            {
                var color = Color.FromArgb(210, 230, 160, 70);
                _board.SetArrows(new[] { new BoardArrow(move.From, move.To, color) });
                _adviceBox.Text = $"Подсказка: {_game.CurrentPosition.ToSan(move)}" +
                                  (result.Best != null
                                      ? $" (оценка {result.Best.ScoreText(_game.CurrentPosition.SideToMove == PieceColor.White)})"
                                      : string.Empty);
            }
        }
        catch (EngineUnresponsiveException ex)
        {
            _searchStatus.Text = "Движок не отвечает — перезапуск…";
            _engineBusyWithMove = false;
            UpdateEngineControlsEnabled();
            await HandleEngineWedgedAsync(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _searchStatus.Text = "Ошибка движка: " + ex.Message;
        }
        finally
        {
            _engineBusyWithMove = false;
            UpdateEngineControlsEnabled();
            _board.InteractionEnabled = !_editing && _game.CurrentPosition.LegalMoves.Count > 0;
            if (!applyToBoard) await RefreshAnalysisAsync();
        }
    }

    /// <summary>
    /// Что показать в строке состояния после хода соперника. Уступку меньше десятой пешки
    /// не называем: это шум округления, а не поддавки.
    /// </summary>
    private const int OpponentLossNoticeCp = 10;

    private static string OpponentMoveText(DifficultyProfile profile, OpponentChoice choice, string san) =>
        choice.Kind switch
        {
            OpponentPickKind.Random => $"Соперник ({profile.Title}) сыграл наугад: {san}.",
            _ when choice.LossCp >= OpponentLossNoticeCp =>
                $"Соперник ({profile.Title}): {san} — уступка " +
                $"{(choice.LossCp / 100.0).ToString("0.00", CultureInfo.InvariantCulture)} пешки.",
            _ => $"Соперник ({profile.Title}): {san}."
        };

    private async Task AnalyzeWholeGameAsync()
    {
        if (_editing) return;

        // Разбор вклинился бы между ослаблением и возвратом силы — и вся партия была бы
        // разобрана движком соперника, а не полным.
        if (_engineBusyWithMove)
        {
            MessageBox.Show(this, "Движок сейчас обдумывает ход.", "Разбор партии",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_engine.IsRunning)
        {
            MessageBox.Show(this, "Сначала укажите путь к Stockfish в настройках движка.",
                "Движок не запущен", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_gameAnalysisCts != null)
        {
            _gameAnalysisCts.Cancel();
            return;
        }

        var line = _game.MainLine();
        if (line.Count == 0)
        {
            MessageBox.Show(this, "В партии нет ходов.", "Разбор партии",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _analysisGeneration++;
        await _engine.StopSearchAsync();

        _gameAnalysisCts = new CancellationTokenSource();
        var token = _gameAnalysisCts.Token;

        var nodes = new List<MoveNode> { _game.Root };
        nodes.AddRange(line);

        _progress.Visible = true;
        _progress.Minimum = 0;
        _progress.Maximum = nodes.Count;
        _progress.Value = 0;
        _board.InteractionEnabled = false;
        UpdateEngineControlsEnabled();

        try
        {
            var movesSoFar = new List<string>();
            for (var i = 0; i < nodes.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var node = nodes[i];
                if (i > 0) movesSoFar.Add(node.Move.ToUci());

                if (node.Position.LegalMoves.Count == 0)
                {
                    var mate = node.Position.IsInCheck();
                    node.MateIn = mate
                        ? node.Position.SideToMove == PieceColor.White ? -1 : 1
                        : null;
                    if (!mate) node.EvalCp = 0;
                }
                else
                {
                    var result = await _engine.GoAsync(_game.StartFen, movesSoFar.ToList(),
                        SearchLimits.ByTime(_settings.GameAnalysisMoveTimeMs), token);

                    var info = result.Best;
                    if (info != null)
                    {
                        var whiteToMove = node.Position.SideToMove == PieceColor.White;
                        node.EvalCp = info.WhiteCp(whiteToMove);
                        node.MateIn = info.WhiteMate(whiteToMove);
                    }
                    node.BestReply = result.BestMove;
                }

                _progress.Value = Math.Min(_progress.Maximum, i + 1);
                _searchStatus.Text = $"Разбор партии: позиция {i + 1} из {nodes.Count}";
                _moveList.Reload();
            }

            AnnotateBlunders(nodes);
            _searchStatus.Text = "Разбор партии завершён.";
        }
        catch (OperationCanceledException)
        {
            _searchStatus.Text = "Разбор партии прерван.";
        }
        catch (Exception ex)
        {
            _searchStatus.Text = "Ошибка разбора: " + ex.Message;
        }
        finally
        {
            _gameAnalysisCts?.Dispose();
            _gameAnalysisCts = null;
            _progress.Visible = false;
            UpdateEngineControlsEnabled();
            _board.InteractionEnabled = !_editing && _game.CurrentPosition.LegalMoves.Count > 0;
            _moveList.Reload();
            await RefreshAnalysisAsync();
        }
    }

    /// <summary>Расставляет знаки ?!, ? и ?? по потере оценки относительно предыдущей позиции.</summary>
    private void AnnotateBlunders(List<MoveNode> nodes)
    {
        const int mateScore = 10000;

        for (var i = 1; i < nodes.Count; i++)
        {
            var previous = nodes[i - 1];
            var node = nodes[i];

            var before = ToScore(previous, mateScore);
            var after = ToScore(node, mateScore);
            if (before is null || after is null) continue;

            var loss = node.IsWhiteMove ? before.Value - after.Value : after.Value - before.Value;

            // Ход совпал с рекомендацией движка — не наказываем за округление.
            if (!string.IsNullOrEmpty(previous.BestReply) && previous.BestReply == node.Move.ToUci())
            {
                node.Glyph = null;
                continue;
            }

            node.Glyph = loss switch
            {
                >= 300 => "??",
                >= 150 => "?",
                >= 80 => "?!",
                _ => null
            };
        }

        static int? ToScore(MoveNode node, int mateScore)
        {
            if (node.MateIn.HasValue)
                return node.MateIn.Value > 0 ? mateScore - node.MateIn.Value : -mateScore - node.MateIn.Value;
            return node.EvalCp;
        }
    }

    // ------------------------------------------------------------ Мелочи

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "Ходы: перетащите фигуру мышью или щёлкните по ней и по целевой клетке.\r\n" +
            "Стрелки ← → — назад/вперёд по партии, Home/End — в начало/конец, Del — удалить ход с продолжением.\r\n" +
            "F — перевернуть доску, Пробел — включить/выключить анализ, " +
            "H — показать/скрыть подсказки движка.\r\n" +
            "Без подсказок движок молчит: нет ни советов, ни стрелок, ни списка вариантов, " +
            "и постоянный анализ не идёт. Кнопки «Подсказка», «Сыграть лучший» и " +
            "«Разобрать партию» по-прежнему работают по запросу.\r\n\r\n" +
            "Ctrl+E — редактор позиции: выберите фигуру в палитре и щёлкайте по доске, правая кнопка убирает фигуру, " +
            "фигуры можно перетаскивать. Esc — выйти без изменений.\r\n\r\n" +
            "Ход, сделанный не в конце партии, создаёт вариант — кнопка ↑ делает его основной линией.\r\n" +
            "Двойной щелчок по строке анализа делает первый ход этого варианта на доске.\r\n" +
            "«Разобрать партию» прогоняет движок по всем позициям и расставляет знаки ?!, ? и ??.\r\n\r\n" +
            "«Сложность» задаёт силу соперника и действует только на его ход: подсказки, шкала " +
            "оценки, стрелки и разбор партии всегда считаются движком на полной силе.\r\n" +
            "Поля «Уровень игры» и «Рейтинг Эло» в настройках движка работают при сложности «Своя».",
            "Возможности программы", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string? Prompt(string message, string title, string initial)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(560, 130),
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new Label { Text = message, Dock = DockStyle.Top, Height = 26, Padding = new Padding(10, 6, 0, 0) };
        var input = new TextBox { Text = initial, Dock = DockStyle.Top, Margin = new Padding(10) , Width = 520 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44 };
        var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        host.Controls.Add(input);

        dialog.Controls.Add(host);
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(label);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }
}
