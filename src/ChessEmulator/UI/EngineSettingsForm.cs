using ChessEmulator.App;
using ChessEmulator.Engine;

namespace ChessEmulator.UI;

/// <summary>Диалог настроек движка: путь к Stockfish и параметры поиска.</summary>
public sealed class EngineSettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly ComboBox _pathBox = new();
    private readonly NumericUpDown _threads = new();
    private readonly NumericUpDown _hash = new();
    private readonly NumericUpDown _multiPv = new();
    private readonly NumericUpDown _depthLimit = new();
    private readonly NumericUpDown _moveTime = new();
    private readonly NumericUpDown _analysisTime = new();
    private readonly CheckBox _limitStrength = new();
    private readonly NumericUpDown _elo = new();
    private readonly NumericUpDown _skill = new();
    private readonly Label _status = new();

    public EngineSettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Настройки движка";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 470);
        Font = new Font("Segoe UI", 9f);

        BuildLayout();
        LoadValues();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(14),
            AutoSize = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

        var row = 0;

        layout.Controls.Add(new Label { Text = "Путь к движку:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        _pathBox.Dock = DockStyle.Fill;
        _pathBox.DropDownStyle = ComboBoxStyle.DropDown;
        layout.Controls.Add(_pathBox, 1, row);
        var browse = new Button { Text = "Обзор…", Dock = DockStyle.Fill };
        browse.Click += OnBrowse;
        layout.Controls.Add(browse, 2, row++);

        layout.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, row);
        var detect = new Button { Text = "Найти автоматически", Dock = DockStyle.Left, AutoSize = true };
        detect.Click += (_, _) => DetectEngines(showMessage: true);
        layout.Controls.Add(detect, 1, row++);

        AddNumeric(layout, ref row, "Потоков (Threads):", _threads, 1, Math.Max(1, Environment.ProcessorCount), 1);
        AddNumeric(layout, ref row, "Хеш, МБ (Hash):", _hash, 16, 8192, 16);
        AddNumeric(layout, ref row, "Линий анализа (MultiPV):", _multiPv, 1, 10, 1);
        AddNumeric(layout, ref row, "Предел глубины (0 — нет):", _depthLimit, 0, 60, 1);
        AddNumeric(layout, ref row, "Время на ход движка, мс:", _moveTime, 50, 60000, 100);
        AddNumeric(layout, ref row, "Время на позицию при разборе, мс:", _analysisTime, 50, 10000, 100);
        AddNumeric(layout, ref row, "Уровень игры (Skill Level):", _skill, 0, 20, 1);

        _limitStrength.Text = "Ограничить силу движка рейтингом";
        _limitStrength.AutoSize = true;
        _limitStrength.CheckedChanged += (_, _) => _elo.Enabled = _limitStrength.Checked;
        layout.Controls.Add(new Label { Text = string.Empty }, 0, row);
        layout.Controls.Add(_limitStrength, 1, row++);

        AddNumeric(layout, ref row, "Рейтинг Эло (UCI_Elo):", _elo, 500, 3200, 50);

        // Подпись идёт после поля рейтинга и никогда между подписью и её полем: тесты ищут
        // NumericUpDown, следующий за подписью в порядке обхода.
        var strengthHint = new Label
        {
            Text = "Уровень игры и рейтинг действуют, когда выбрана сложность «Своя».",
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(360, 0)
        };
        layout.Controls.Add(new Label { Text = string.Empty }, 0, row);
        layout.Controls.Add(strengthHint, 1, row++);

        _status.AutoSize = true;
        _status.ForeColor = Color.DimGray;
        _status.MaximumSize = new Size(360, 0);
        layout.Controls.Add(new Label { Text = string.Empty }, 0, row);
        layout.Controls.Add(_status, 1, row++);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 46,
            Padding = new Padding(10, 8, 10, 8)
        };
        var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = 90 };
        ok.Click += (_, _) => SaveValues();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static void AddNumeric(TableLayoutPanel layout, ref int row, string label, NumericUpDown control,
        int min, int max, int step)
    {
        control.Minimum = min;
        control.Maximum = max;
        control.Increment = step;
        control.Width = 110;
        control.Anchor = AnchorStyles.Left;
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row++);
    }

    private void LoadValues()
    {
        DetectEngines(showMessage: false);
        if (!string.IsNullOrWhiteSpace(_settings.EnginePath)) _pathBox.Text = _settings.EnginePath;

        _threads.Value = Clamp(_threads, _settings.Threads);
        _hash.Value = Clamp(_hash, _settings.HashMb);
        _multiPv.Value = Clamp(_multiPv, _settings.MultiPv);
        _depthLimit.Value = Clamp(_depthLimit, _settings.AnalysisDepthLimit);
        _moveTime.Value = Clamp(_moveTime, _settings.EngineMoveTimeMs);
        _analysisTime.Value = Clamp(_analysisTime, _settings.GameAnalysisMoveTimeMs);
        _skill.Value = Clamp(_skill, _settings.SkillLevel);
        _limitStrength.Checked = _settings.LimitStrength;
        _elo.Value = Clamp(_elo, _settings.EloRating);
        _elo.Enabled = _limitStrength.Checked;
    }

    private static decimal Clamp(NumericUpDown control, int value) =>
        Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);

    private void SaveValues()
    {
        _settings.EnginePath = string.IsNullOrWhiteSpace(_pathBox.Text) ? null : _pathBox.Text.Trim();
        _settings.Threads = (int)_threads.Value;
        _settings.HashMb = (int)_hash.Value;
        _settings.MultiPv = (int)_multiPv.Value;
        _settings.AnalysisDepthLimit = (int)_depthLimit.Value;
        _settings.EngineMoveTimeMs = (int)_moveTime.Value;
        _settings.GameAnalysisMoveTimeMs = (int)_analysisTime.Value;
        _settings.SkillLevel = (int)_skill.Value;
        _settings.LimitStrength = _limitStrength.Checked;
        _settings.EloRating = (int)_elo.Value;
        _settings.Save();
    }

    private void DetectEngines(bool showMessage)
    {
        var found = EngineLocator.FindAll();
        var current = _pathBox.Text;
        _pathBox.Items.Clear();
        foreach (var path in found) _pathBox.Items.Add(path);

        if (found.Count > 0 && string.IsNullOrWhiteSpace(current)) _pathBox.Text = found[0];
        else _pathBox.Text = current;

        _status.Text = found.Count > 0
            ? $"Найдено вариантов движка: {found.Count}."
            : "Stockfish не найден. Скачайте stockfishchess.org/download и укажите путь вручную.";

        if (showMessage && found.Count == 0)
        {
            MessageBox.Show(this,
                "Stockfish не найден в стандартных папках.\n\n" +
                "Скачайте движок с stockfishchess.org/download, распакуйте и укажите путь к .exe кнопкой «Обзор…».\n" +
                "Также подойдёт папка engine рядом с программой.",
                "Движок не найден", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл движка UCI",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*"
        };
        if (!string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            try
            {
                var dir = Path.GetDirectoryName(_pathBox.Text);
                if (Directory.Exists(dir)) dialog.InitialDirectory = dir;
            }
            catch
            {
                // некорректный путь — открываем диалог по умолчанию
            }
        }

        if (dialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dialog.FileName;
    }
}
