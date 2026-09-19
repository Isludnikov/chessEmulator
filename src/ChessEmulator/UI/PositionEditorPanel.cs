using System.Drawing.Drawing2D;
using ChessEmulator.Chess;

namespace ChessEmulator.UI;

/// <summary>Палитра фигур для редактора: 12 фигур и ластик. Пустая фигура означает ластик.</summary>
internal sealed class PiecePalette : Control
{
    private static readonly PieceType[] Order =
    {
        PieceType.King, PieceType.Queen, PieceType.Rook,
        PieceType.Bishop, PieceType.Knight, PieceType.Pawn
    };

    private Piece _selected = new(PieceColor.White, PieceType.Pawn);
    private int _hovered = -1;

    public PiecePalette()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(32, 32, 34);
        Height = 112;
    }

    public event EventHandler? SelectionChanged;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Piece Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Columns => Order.Length + 1;   // 6 фигур + ластик в нижнем ряду

    private Rectangle CellRect(int index)
    {
        var cell = Math.Max(24, Math.Min(ClientSize.Width / Columns, ClientSize.Height / 2));
        var col = index % Columns;
        var row = index / Columns;
        return new Rectangle(col * cell, row * cell, cell, cell);
    }

    /// <summary>Индекс ячейки: 0–5 — белые, 7–12 — чёрные, 6 и 13 — ластик.</summary>
    private Piece PieceAt(int index)
    {
        var col = index % Columns;
        var row = index / Columns;
        if (col >= Order.Length) return Piece.Empty;   // ластик
        return new Piece(row == 0 ? PieceColor.White : PieceColor.Black, Order[col]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        for (var index = 0; index < Columns * 2; index++)
        {
            var rect = CellRect(index);
            if (rect.Width < 8) continue;

            var piece = PieceAt(index);
            var isEraser = piece.IsEmpty;
            if (isEraser && index != Order.Length) continue;   // ластик нужен только один

            var selected = isEraser ? _selected.IsEmpty : piece == _selected;
            var cell = Rectangle.Inflate(rect, -2, -2);

            // Фон клетки светлый, как на доске: иначе чёрные фигуры сливаются с панелью.
            using (var back = new SolidBrush(index == _hovered
                       ? Color.FromArgb(225, 200, 165)
                       : Color.FromArgb(240, 217, 181)))
            {
                g.FillRectangle(back, cell);
            }

            if (isEraser) DrawEraser(g, rect);
            else PieceRenderer.Draw(g, piece, Rectangle.Inflate(rect, -4, -4));

            if (selected)
            {
                using var frame = new Pen(Color.FromArgb(70, 130, 220), 3f);
                g.DrawRectangle(frame, cell.X + 1, cell.Y + 1, cell.Width - 3, cell.Height - 3);
            }
        }
    }

    private static void DrawEraser(Graphics g, Rectangle rect)
    {
        var inset = rect.Width / 4;
        using var pen = new Pen(Color.FromArgb(190, 60, 55), 3f);
        g.DrawLine(pen, rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
        g.DrawLine(pen, rect.Right - inset, rect.Top + inset, rect.Left + inset, rect.Bottom - inset);
    }

    /// <summary>Отрисованные ячейки: все фигуры и единственный ластик.</summary>
    private bool IsDrawn(int index) => !PieceAt(index).IsEmpty || index == Order.Length;

    private int IndexAt(Point point)
    {
        for (var index = 0; index < Columns * 2; index++)
        {
            if (IsDrawn(index) && CellRect(index).Contains(point)) return index;
        }
        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var index = IndexAt(e.Location);
        if (index >= 0) Selected = PieceAt(index);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        if (index == _hovered) return;
        _hovered = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = -1;
        Invalidate();
    }
}

public sealed class PositionAppliedEventArgs : EventArgs
{
    public PositionAppliedEventArgs(string fen) => Fen = fen;
    public string Fen { get; }
}

/// <summary>
/// Панель редактора позиции: палитра фигур и признаки позиции (очередь хода, рокировки,
/// взятие на проходе), проверка корректности и кнопки применения.
/// </summary>
public sealed class PositionEditorPanel : UserControl
{
    private readonly PiecePalette _palette = new();
    private readonly RadioButton _whiteToMove = new() { Text = "Ход белых", AutoSize = true };
    private readonly RadioButton _blackToMove = new() { Text = "Ход чёрных", AutoSize = true };
    private readonly CheckBox _whiteShort = new() { Text = "0-0 белые", AutoSize = true };
    private readonly CheckBox _whiteLong = new() { Text = "0-0-0 белые", AutoSize = true };
    private readonly CheckBox _blackShort = new() { Text = "0-0 чёрные", AutoSize = true };
    private readonly CheckBox _blackLong = new() { Text = "0-0-0 чёрные", AutoSize = true };
    private readonly ComboBox _enPassant = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly Label _status = new();
    private readonly Button _apply = new() { Text = "Применить", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Отмена", AutoSize = true };

    private bool _suppressSync;

    public PositionEditorPanel()
    {
        BackColor = Color.FromArgb(32, 32, 34);
        ForeColor = Color.Gainsboro;
        AutoScroll = true;
        Padding = new Padding(8);
        BuildLayout();
        WireEvents();
    }

    public PositionBuilder Builder { get; private set; } = new();

    /// <summary>Фигура, выбранная в палитре. Пустая — режим стирания.</summary>
    public Piece Brush => _palette.Selected;

    /// <summary>Расстановка изменилась — доску и поле FEN нужно перерисовать.</summary>
    public event EventHandler? PositionChanged;

    public event EventHandler<PositionAppliedEventArgs>? Applied;
    public event EventHandler? Cancelled;

    // ------------------------------------------------------------- Вёрстка

    private void BuildLayout()
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.FromArgb(32, 32, 34)
        };

        layout.Controls.Add(MakeLabel("Выберите фигуру и щёлкайте по доске. Правая кнопка мыши убирает фигуру."));

        _palette.Width = 330;
        _palette.Margin = new Padding(0, 4, 0, 8);
        layout.Controls.Add(_palette);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 340, Margin = new Padding(0, 0, 0, 6) };
        buttons.Controls.Add(MakeButton("Очистить доску", () => { Builder.Clear(); SyncFromBuilder(); RaiseChanged(); }));
        buttons.Controls.Add(MakeButton("Начальная позиция", () => { Builder.SetStartPosition(); SyncFromBuilder(); RaiseChanged(); }));
        buttons.Controls.Add(MakeButton("Копировать FEN", () => Clipboard.SetText(Builder.ToFen())));
        buttons.Controls.Add(MakeButton("Вставить FEN", () => SetFen(Clipboard.GetText())));
        layout.Controls.Add(buttons);

        var sideBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        _whiteToMove.ForeColor = Color.Gainsboro;
        _blackToMove.ForeColor = Color.Gainsboro;
        _whiteToMove.Checked = true;
        sideBox.Controls.Add(_whiteToMove);
        sideBox.Controls.Add(_blackToMove);
        layout.Controls.Add(sideBox);

        layout.Controls.Add(MakeLabel("Рокировки:"));
        var castling = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 340 };
        foreach (var box in new[] { _whiteShort, _whiteLong, _blackShort, _blackLong })
        {
            box.ForeColor = Color.Gainsboro;
            castling.Controls.Add(box);
        }
        layout.Controls.Add(castling);

        var epBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        epBox.Controls.Add(MakeLabel("Взятие на проходе:", 4));
        epBox.Controls.Add(_enPassant);
        layout.Controls.Add(epBox);

        _status.AutoSize = true;
        _status.MaximumSize = new Size(340, 0);
        _status.Margin = new Padding(0, 6, 0, 6);
        layout.Controls.Add(_status);

        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actions.Controls.Add(_apply);
        actions.Controls.Add(_cancel);
        layout.Controls.Add(actions);

        Controls.Add(layout);
    }

    private static Label MakeLabel(string text, int topMargin = 0) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(340, 0),
        ForeColor = Color.Gainsboro,
        Margin = new Padding(0, topMargin, 6, 0)
    };

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(52, 52, 56),
            Margin = new Padding(0, 2, 6, 2)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 76);
        button.Click += (_, _) => action();
        return button;
    }

    private void WireEvents()
    {
        _whiteToMove.CheckedChanged += (_, _) => OnFlagChanged();
        _blackToMove.CheckedChanged += (_, _) => OnFlagChanged();
        foreach (var box in new[] { _whiteShort, _whiteLong, _blackShort, _blackLong })
            box.CheckedChanged += (_, _) => OnFlagChanged();
        _enPassant.SelectedIndexChanged += (_, _) => OnFlagChanged();

        _apply.Click += (_, _) =>
        {
            Builder.Normalize();
            if (Builder.Validate().Count > 0)
            {
                SyncFromBuilder();
                return;
            }
            Applied?.Invoke(this, new PositionAppliedEventArgs(Builder.ToFen()));
        };
        _cancel.Click += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------ Поведение

    /// <summary>Начинает редактирование с указанной позиции.</summary>
    public void LoadFrom(Position position)
    {
        Builder = PositionBuilder.FromPosition(position);
        SyncFromBuilder();
    }

    /// <summary>Применяет FEN, введённый пользователем. Возвращает текст ошибки или null.</summary>
    public string? SetFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return "Пустая строка FEN.";
        try
        {
            Builder = PositionBuilder.FromFen(fen.Trim());
            SyncFromBuilder();
            RaiseChanged();
            return null;
        }
        catch (FormatException ex)
        {
            _status.ForeColor = Color.FromArgb(230, 120, 110);
            _status.Text = "Не удалось разобрать FEN: " + ex.Message;
            return ex.Message;
        }
    }

    /// <summary>Щелчок по клетке доски: ставим выбранную фигуру или стираем.</summary>
    public void HandleSquareClick(int square, MouseButtons button)
    {
        if (!Sq.IsValid(square)) return;
        Builder[square] = button == MouseButtons.Right || Brush.IsEmpty ? Piece.Empty : Brush;
        SyncFromBuilder();
        RaiseChanged();
    }

    /// <summary>Перетаскивание фигуры по доске; сброс мимо доски удаляет фигуру.</summary>
    public void HandlePieceDrag(int from, int to)
    {
        if (!Sq.IsValid(from)) return;
        if (!Sq.IsValid(to)) Builder[from] = Piece.Empty;
        else Builder.MovePiece(from, to);
        SyncFromBuilder();
        RaiseChanged();
    }

    /// <summary>Приводит позицию в порядок, проверяет её и обновляет контролы панели.</summary>
    public void SyncFromBuilder()
    {
        Builder.Normalize();

        _suppressSync = true;
        try
        {
            _whiteToMove.Checked = Builder.SideToMove == PieceColor.White;
            _blackToMove.Checked = Builder.SideToMove == PieceColor.Black;

            _whiteShort.Checked = Builder.Castling.HasFlag(CastlingRights.WhiteKing);
            _whiteLong.Checked = Builder.Castling.HasFlag(CastlingRights.WhiteQueen);
            _blackShort.Checked = Builder.Castling.HasFlag(CastlingRights.BlackKing);
            _blackLong.Checked = Builder.Castling.HasFlag(CastlingRights.BlackQueen);

            FillEnPassant();
        }
        finally
        {
            _suppressSync = false;
        }

        var errors = Builder.Validate();
        _apply.Enabled = errors.Count == 0;
        if (errors.Count == 0)
        {
            _status.ForeColor = Color.FromArgb(150, 210, 150);
            _status.Text = "Позиция корректна — можно применить.";
        }
        else
        {
            _status.ForeColor = Color.FromArgb(230, 120, 110);
            _status.Text = string.Join(Environment.NewLine, errors);
        }
    }

    private void FillEnPassant()
    {
        var available = Builder.AvailableEnPassantSquares();
        _enPassant.Items.Clear();
        _enPassant.Items.Add("нет");
        foreach (var square in available) _enPassant.Items.Add(Sq.Name(square));

        var index = Builder.EnPassant == Sq.None ? 0 : _enPassant.Items.IndexOf(Sq.Name(Builder.EnPassant));
        _enPassant.SelectedIndex = index < 0 ? 0 : index;
        _enPassant.Enabled = available.Count > 0;
    }

    private void OnFlagChanged()
    {
        if (_suppressSync) return;

        Builder.SideToMove = _blackToMove.Checked ? PieceColor.Black : PieceColor.White;

        var rights = CastlingRights.None;
        if (_whiteShort.Checked) rights |= CastlingRights.WhiteKing;
        if (_whiteLong.Checked) rights |= CastlingRights.WhiteQueen;
        if (_blackShort.Checked) rights |= CastlingRights.BlackKing;
        if (_blackLong.Checked) rights |= CastlingRights.BlackQueen;
        Builder.Castling = rights;

        var selected = _enPassant.SelectedItem as string ?? "нет";
        Builder.EnPassant = selected == "нет" ? Sq.None : Sq.Parse(selected);

        SyncFromBuilder();
        RaiseChanged();
    }

    private void RaiseChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
}
