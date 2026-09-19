using System.Text;
using ChessEmulator.Engine;

namespace ChessEmulator.UI;

/// <summary>
/// Окно «Журнал UCI…»: живой протокол общения с движком. Команды и ответы идут вперемешку
/// в том же порядке, в каком они были, — по нему сразу видно, если команда ушла во время поиска.
/// </summary>
public sealed class UciLogForm : Form
{
    private readonly UciLog _log;
    private readonly TextBox _text = new();
    private readonly CheckBox _showInfo = new();
    private readonly CheckBox _autoScroll = new();
    private readonly System.Windows.Forms.Timer _flushTimer = new();
    private readonly StringBuilder _pending = new();

    public UciLogForm(UciLog log)
    {
        _log = log;

        Text = "Журнал UCI";
        ClientSize = new Size(900, 520);
        MinimumSize = new Size(480, 240);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(32, 32, 34);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;

        _text.Name = "uciLogText";
        _text.Dock = DockStyle.Fill;
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.WordWrap = false;
        _text.BorderStyle = BorderStyle.None;
        _text.BackColor = Color.FromArgb(24, 24, 26);
        _text.ForeColor = Color.Gainsboro;
        _text.Font = new Font("Consolas", 9f);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            WrapContents = false,
            Padding = new Padding(6, 6, 6, 6),
            BackColor = Color.FromArgb(32, 32, 34)
        };

        // Строк анализа сотни в секунду — по умолчанию прячем, иначе журнал нечитаем.
        _showInfo.Name = "uciLogShowInfo";
        _showInfo.Text = "Показывать info";
        _showInfo.AutoSize = true;
        _showInfo.ForeColor = Color.Gainsboro;
        _showInfo.Margin = new Padding(0, 4, 12, 0);
        _showInfo.CheckedChanged += (_, _) => Reload();

        _autoScroll.Text = "Прокручивать";
        _autoScroll.AutoSize = true;
        _autoScroll.Checked = true;
        _autoScroll.ForeColor = Color.Gainsboro;
        _autoScroll.Margin = new Padding(0, 4, 12, 0);

        bottom.Controls.Add(_showInfo);
        bottom.Controls.Add(_autoScroll);
        bottom.Controls.Add(MakeButton("Очистить", () => { _log.Clear(); Reload(); }));
        bottom.Controls.Add(MakeButton("Копировать всё", CopyAll));
        bottom.Controls.Add(MakeButton("Сохранить в файл…", SaveToFile));

        Controls.Add(_text);
        Controls.Add(bottom);

        Reload();

        // Пишем пачками: построчное обновление TextBox при бесконечном анализе съедает поток UI.
        _flushTimer.Interval = 200;
        _flushTimer.Tick += (_, _) => Flush();
        _flushTimer.Start();

        _log.EntryAdded += OnEntryAdded;
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 26,
            Margin = new Padding(0, 1, 6, 1),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(52, 52, 56)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 74);
        button.Click += (_, _) => action();
        return button;
    }

    private void OnEntryAdded(object? sender, UciLogEntry entry)
    {
        if (!_showInfo.Checked && entry.IsInfo) return;
        lock (_pending) _pending.AppendLine(entry.ToString());
    }

    private void Flush()
    {
        // Тик таймера мог быть поставлен в очередь перед закрытием окна.
        if (IsDisposed || Disposing) return;

        string text;
        lock (_pending)
        {
            if (_pending.Length == 0) return;
            text = _pending.ToString();
            _pending.Clear();
        }

        _text.AppendText(text);
        if (!_autoScroll.Checked) return;
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }

    private void Reload()
    {
        lock (_pending) _pending.Clear();

        var sb = new StringBuilder();
        foreach (var entry in _log.Snapshot())
        {
            if (!_showInfo.Checked && entry.IsInfo) continue;
            sb.AppendLine(entry.ToString());
        }

        _text.Text = sb.ToString();
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }

    private void CopyAll()
    {
        if (_text.TextLength == 0) return;
        Clipboard.SetText(_text.Text);
    }

    private void SaveToFile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить журнал UCI",
            Filter = "Текстовый файл (*.log)|*.log|Все файлы (*.*)|*.*",
            FileName = $"uci-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, _text.Text, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось сохранить файл: " + ex.Message, "Журнал UCI",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Именно здесь, а не в OnFormClosed: окно могут освободить и не показав.
        if (disposing)
        {
            _log.EntryAdded -= OnEntryAdded;
            _flushTimer.Stop();
            _flushTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
