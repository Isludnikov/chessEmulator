using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessEmulator.Chess;

namespace ChessEmulator.UI;

public sealed class MoveMadeEventArgs : EventArgs
{
    public MoveMadeEventArgs(Move move) => Move = move;
    public Move Move { get; }
}

public sealed class PromotionEventArgs : EventArgs
{
    public PromotionEventArgs(PieceColor color) => Color = color;
    public PieceColor Color { get; }
    public PieceType Selected { get; set; } = PieceType.Queen;
    public bool Cancelled { get; set; }
}

/// <summary>Стрелка на доске (подсказка движка).</summary>
public readonly record struct BoardArrow(int From, int To, Color Color, float Weight = 1f);

/// <summary>Режим доски: игра по правилам или свободная расстановка фигур.</summary>
public enum BoardMode
{
    Play,
    Edit
}

/// <summary>Щелчок по клетке в режиме редактирования.</summary>
public sealed class EditSquareEventArgs : EventArgs
{
    public EditSquareEventArgs(int square, MouseButtons button)
    {
        Square = square;
        Button = button;
    }

    public int Square { get; }
    public MouseButtons Button { get; }
}

/// <summary>Перетаскивание фигуры в режиме редактирования. <see cref="To"/> = Sq.None — фигуру сбросили мимо доски.</summary>
public sealed class EditDragEventArgs : EventArgs
{
    public EditDragEventArgs(int from, int to)
    {
        From = from;
        To = to;
    }

    public int From { get; }
    public int To { get; }
}

/// <summary>Интерактивная шахматная доска: отрисовка позиции, ходы мышью, подсветки и стрелки.</summary>
public sealed class BoardControl : Control
{
    private Position _position = Position.FromFen(Position.StartFen);
    private bool _flipped;
    private int _selected = Sq.None;
    private int _hover = Sq.None;
    private int _dragFrom = Sq.None;
    private Point _dragPoint;
    private bool _dragging;
    private readonly List<int> _targets = new();
    private readonly List<BoardArrow> _arrows = new();
    private Chess.Move _lastMove = Chess.Move.None;
    private BoardMode _mode = BoardMode.Play;
    private Point _dragOrigin;

    private int _squareSize = 64;
    private Rectangle _boardRect;

    private static readonly Color LightSquare = Color.FromArgb(240, 217, 181);
    private static readonly Color DarkSquare = Color.FromArgb(181, 136, 99);
    private static readonly Color LastMoveTint = Color.FromArgb(120, 255, 214, 92);
    private static readonly Color SelectedTint = Color.FromArgb(140, 106, 190, 255);
    private static readonly Color CheckTint = Color.FromArgb(150, 220, 70, 60);
    private static readonly Color HoverTint = Color.FromArgb(60, 255, 255, 255);

    public BoardControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(40, 40, 42);
        TabStop = true;
        MinimumSize = new Size(240, 240);
    }

    public event EventHandler<MoveMadeEventArgs>? MoveMade;
    public event EventHandler<PromotionEventArgs>? PromotionNeeded;
    public event EventHandler<EditSquareEventArgs>? EditSquareClicked;
    public event EventHandler<EditDragEventArgs>? EditPieceDragged;

    /// <summary>
    /// В режиме <see cref="BoardMode.Edit"/> доска не знает правил: она лишь сообщает о щелчках
    /// и перетаскиваниях, а расстановку меняет владелец. <see cref="InteractionEnabled"/> при этом не действует.
    /// </summary>
    [DefaultValue(BoardMode.Play)]
    public BoardMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            _selected = Sq.None;
            _targets.Clear();
            _dragging = false;
            _dragFrom = Sq.None;
            Invalidate();
        }
    }

    /// <summary>Фигура, которую ставит щелчок в режиме редактирования. Пустая фигура — ластик.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Piece EditBrush { get; set; } = Piece.Empty;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Position Position
    {
        get => _position;
        set
        {
            _position = value;
            _selected = Sq.None;
            _targets.Clear();
            _dragging = false;
            _dragFrom = Sq.None;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Flipped
    {
        get => _flipped;
        set { _flipped = value; Invalidate(); }
    }

    [DefaultValue(true)] public bool ShowCoordinates { get; set; } = true;
    [DefaultValue(true)] public bool ShowLegalMoveHints { get; set; } = true;
    [DefaultValue(true)] public bool InteractionEnabled { get; set; } = true;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Chess.Move LastMove
    {
        get => _lastMove;
        set { _lastMove = value; Invalidate(); }
    }

    public void SetArrows(IEnumerable<BoardArrow> arrows)
    {
        _arrows.Clear();
        _arrows.AddRange(arrows);
        Invalidate();
    }

    public void ClearArrows()
    {
        if (_arrows.Count == 0) return;
        _arrows.Clear();
        Invalidate();
    }

    public void ClearSelection()
    {
        _selected = Sq.None;
        _targets.Clear();
        Invalidate();
    }

    // ----------------------------------------------------------- Геометрия

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
    }

    private void RecalculateLayout()
    {
        var side = Math.Max(64, Math.Min(ClientSize.Width, ClientSize.Height));
        _squareSize = Math.Max(8, side / 8);
        var boardSide = _squareSize * 8;
        _boardRect = new Rectangle(
            (ClientSize.Width - boardSide) / 2,
            (ClientSize.Height - boardSide) / 2,
            boardSide, boardSide);
    }

    private Rectangle SquareRect(int square)
    {
        int file = Sq.File(square), rank = Sq.Rank(square);
        var col = _flipped ? 7 - file : file;
        var row = _flipped ? rank : 7 - rank;
        return new Rectangle(
            _boardRect.X + col * _squareSize,
            _boardRect.Y + row * _squareSize,
            _squareSize, _squareSize);
    }

    private PointF SquareCenter(int square)
    {
        var r = SquareRect(square);
        return new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
    }

    private int SquareAt(Point point)
    {
        if (!_boardRect.Contains(point)) return Sq.None;
        var col = (point.X - _boardRect.X) / _squareSize;
        var row = (point.Y - _boardRect.Y) / _squareSize;
        if (col is < 0 or > 7 || row is < 0 or > 7) return Sq.None;
        var file = _flipped ? 7 - col : col;
        var rank = _flipped ? row : 7 - row;
        return Sq.Of(file, rank);
    }

    // ---------------------------------------------------------- Отрисовка

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_boardRect.Width == 0) RecalculateLayout();

        g.Clear(BackColor);

        var borderColor = Mode == BoardMode.Edit ? Color.FromArgb(230, 160, 70) : Color.FromArgb(25, 25, 27);
        using (var border = new Pen(borderColor, Mode == BoardMode.Edit ? 3 : 2))
        {
            g.DrawRectangle(border, _boardRect.X - 2, _boardRect.Y - 2, _boardRect.Width + 3, _boardRect.Height + 3);
        }

        var checkedKing = _position.IsInCheck() ? _position.FindKing(_position.SideToMove) : Sq.None;

        for (var square = 0; square < 64; square++)
        {
            var rect = SquareRect(square);
            var isLight = (Sq.File(square) + Sq.Rank(square)) % 2 == 1;
            using (var brush = new SolidBrush(isLight ? LightSquare : DarkSquare)) g.FillRectangle(brush, rect);

            if (!_lastMove.IsNone && (square == _lastMove.From || square == _lastMove.To))
                using (var brush = new SolidBrush(LastMoveTint)) g.FillRectangle(brush, rect);

            if (square == checkedKing)
                using (var brush = new SolidBrush(CheckTint)) g.FillRectangle(brush, rect);

            if (square == _selected)
                using (var brush = new SolidBrush(SelectedTint)) g.FillRectangle(brush, rect);

            if (square == _hover && InteractionEnabled && _hover != _selected)
                using (var brush = new SolidBrush(HoverTint)) g.FillRectangle(brush, rect);
        }

        if (ShowCoordinates) DrawCoordinates(g);
        if (ShowLegalMoveHints && Mode == BoardMode.Play) DrawTargets(g);

        for (var square = 0; square < 64; square++)
        {
            if (_dragging && square == _dragFrom) continue;
            var piece = _position[square];
            if (!piece.IsEmpty) DrawPiece(g, piece, SquareRect(square));
        }

        foreach (var arrow in _arrows) DrawArrow(g, arrow);

        if (_dragging && _dragFrom != Sq.None)
        {
            var piece = _position[_dragFrom];
            if (!piece.IsEmpty)
            {
                var rect = new Rectangle(
                    _dragPoint.X - _squareSize / 2,
                    _dragPoint.Y - _squareSize / 2,
                    _squareSize, _squareSize);
                DrawPiece(g, piece, rect);
            }
        }
    }

    private void DrawCoordinates(Graphics g)
    {
        var fontSize = Math.Max(7f, _squareSize * 0.16f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);

        for (var i = 0; i < 8; i++)
        {
            var file = _flipped ? 7 - i : i;
            var rank = _flipped ? i : 7 - i;

            // Буквы на нижней горизонтали
            var bottomSquare = Sq.Of(file, _flipped ? 7 : 0);
            var bottomRect = SquareRect(bottomSquare);
            var bottomLight = (Sq.File(bottomSquare) + Sq.Rank(bottomSquare)) % 2 == 1;
            using (var brush = new SolidBrush(bottomLight ? DarkSquare : LightSquare))
            {
                g.DrawString(((char)('a' + file)).ToString(), font, brush,
                    bottomRect.Right - fontSize * 1.4f, bottomRect.Bottom - fontSize * 1.6f);
            }

            // Цифры на левой вертикали
            var leftSquare = Sq.Of(_flipped ? 7 : 0, rank);
            var leftRect = SquareRect(leftSquare);
            var leftLight = (Sq.File(leftSquare) + Sq.Rank(leftSquare)) % 2 == 1;
            using (var brush = new SolidBrush(leftLight ? DarkSquare : LightSquare))
            {
                g.DrawString((rank + 1).ToString(), font, brush,
                    leftRect.X + fontSize * 0.2f, leftRect.Y + fontSize * 0.2f);
            }
        }
    }

    private void DrawTargets(Graphics g)
    {
        if (_targets.Count == 0) return;
        using var dot = new SolidBrush(Color.FromArgb(70, 20, 20, 20));
        using var ring = new Pen(Color.FromArgb(90, 20, 20, 20), Math.Max(3f, _squareSize * 0.08f));

        foreach (var target in _targets)
        {
            var rect = SquareRect(target);
            if (_position[target].IsEmpty && !_position.IsEnPassantMove(new Chess.Move(_selected, target)))
            {
                var d = (int)(_squareSize * 0.28f);
                g.FillEllipse(dot, rect.X + (rect.Width - d) / 2, rect.Y + (rect.Height - d) / 2, d, d);
            }
            else
            {
                var inset = (int)(_squareSize * 0.06f);
                g.DrawEllipse(ring, rect.X + inset, rect.Y + inset,
                    rect.Width - 2 * inset, rect.Height - 2 * inset);
            }
        }
    }

    private static void DrawPiece(Graphics g, Piece piece, Rectangle rect) =>
        PieceRenderer.Draw(g, piece, rect);

    private void DrawArrow(Graphics g, BoardArrow arrow)
    {
        var from = SquareCenter(arrow.From);
        var to = SquareCenter(arrow.To);

        var width = Math.Max(4f, _squareSize * 0.16f * arrow.Weight);
        var head = Math.Max(10f, _squareSize * 0.34f * arrow.Weight);

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;

        float ux = dx / len, uy = dy / len;
        var start = new PointF(from.X + ux * _squareSize * 0.28f, from.Y + uy * _squareSize * 0.28f);
        var tip = new PointF(to.X - ux * _squareSize * 0.08f, to.Y - uy * _squareSize * 0.08f);
        var baseCenter = new PointF(tip.X - ux * head, tip.Y - uy * head);

        using var brush = new SolidBrush(arrow.Color);
        using var pen = new Pen(arrow.Color, width) { StartCap = LineCap.Round, EndCap = LineCap.Flat };
        g.DrawLine(pen, start, baseCenter);

        var hw = head * 0.55f;
        var left = new PointF(baseCenter.X - uy * hw, baseCenter.Y + ux * hw);
        var right = new PointF(baseCenter.X + uy * hw, baseCenter.Y - ux * hw);
        g.FillPolygon(brush, new[] { tip, left, right });
    }

    // --------------------------------------------------------------- Мышь

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var square = SquareAt(e.Location);
        if (square == Sq.None) return;

        if (Mode == BoardMode.Edit)
        {
            HandleEditMouseDown(square, e);
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            ClearSelection();
            return;
        }

        if (e.Button != MouseButtons.Left || !InteractionEnabled) return;

        if (_selected != Sq.None && _targets.Contains(square))
        {
            TryMove(_selected, square);
            return;
        }

        var piece = _position[square];
        if (!piece.IsEmpty && piece.Color == _position.SideToMove && _position.HasMoveFrom(square))
        {
            _selected = square;
            _dragFrom = square;
            _dragPoint = e.Location;
            _dragging = false;
            UpdateTargets(square);
        }
        else
        {
            ClearSelection();
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var square = SquareAt(e.Location);
        if (square != _hover)
        {
            _hover = square;
            Invalidate();
        }

        if (Mode == BoardMode.Edit)
        {
            HandleEditMouseMove(e);
            return;
        }

        if (e.Button == MouseButtons.Left && _dragFrom != Sq.None && InteractionEnabled)
        {
            _dragPoint = e.Location;
            _dragging = true;
            Cursor = Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Cursor = Cursors.Default;

        if (Mode == BoardMode.Edit)
        {
            HandleEditMouseUp(e);
            return;
        }

        if (e.Button != MouseButtons.Left || _dragFrom == Sq.None) return;

        if (_dragging)
        {
            var target = SquareAt(e.Location);
            _dragging = false;
            if (target != Sq.None && target != _dragFrom && _targets.Contains(target))
            {
                TryMove(_dragFrom, target);
                return;
            }
            Invalidate();
        }

        _dragFrom = Sq.None;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = Sq.None;
        Invalidate();
    }

    // ------------------------------------------------- Мышь в режиме редактора

    private void HandleEditMouseDown(int square, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            EditSquareClicked?.Invoke(this, new EditSquareEventArgs(square, MouseButtons.Right));
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        _dragFrom = square;
        _dragOrigin = e.Location;
        _dragPoint = e.Location;
        _dragging = false;
    }

    private void HandleEditMouseMove(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragFrom == Sq.None) return;
        if (_position[_dragFrom].IsEmpty) return;

        // Порог, чтобы дрожание мыши не превращало щелчок в перетаскивание.
        var drag = SystemInformation.DragSize;
        if (!_dragging &&
            Math.Abs(e.X - _dragOrigin.X) < drag.Width &&
            Math.Abs(e.Y - _dragOrigin.Y) < drag.Height)
            return;

        _dragPoint = e.Location;
        _dragging = true;
        Cursor = Cursors.Hand;
        Invalidate();
    }

    private void HandleEditMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _dragFrom == Sq.None) return;

        var from = _dragFrom;
        var dragged = _dragging;
        _dragFrom = Sq.None;
        _dragging = false;

        if (dragged)
        {
            var target = SquareAt(e.Location);
            Invalidate();
            if (target != from) EditPieceDragged?.Invoke(this, new EditDragEventArgs(from, target));
            return;
        }

        EditSquareClicked?.Invoke(this, new EditSquareEventArgs(from, MouseButtons.Left));
    }

    private void UpdateTargets(int from)
    {
        _targets.Clear();
        foreach (var move in _position.LegalMoves)
        {
            if (move.From == from && !_targets.Contains(move.To)) _targets.Add(move.To);
        }
    }

    private void TryMove(int from, int to)
    {
        var promotion = PieceType.None;
        if (_position.IsPromotionMove(from, to))
        {
            var args = new PromotionEventArgs(_position.SideToMove);
            PromotionNeeded?.Invoke(this, args);
            if (args.Cancelled)
            {
                ClearSelection();
                _dragFrom = Sq.None;
                return;
            }
            promotion = args.Selected;
        }

        if (_position.TryFindMove(from, to, promotion, out var move))
        {
            ClearSelection();
            _dragFrom = Sq.None;
            MoveMade?.Invoke(this, new MoveMadeEventArgs(move));
        }
        else
        {
            ClearSelection();
            _dragFrom = Sq.None;
        }
    }
}
