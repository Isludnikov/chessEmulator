using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessEmulator.Chess;

namespace ChessEmulator.UI;

/// <summary>Выбор фигуры при превращении пешки.</summary>
public sealed class PromotionDialog : Form
{
    private static readonly PieceType[] Choices =
    {
        PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight
    };

    private readonly PieceColor _color;
    private int _hovered = -1;
    private const int Cell = 68;

    public PieceType Selected { get; private set; } = PieceType.Queen;

    public PromotionDialog(PieceColor color)
    {
        _color = color;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Text = "Превращение пешки";
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(Cell * Choices.Length, Cell);
        BackColor = Color.FromArgb(45, 45, 48);
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        for (var i = 0; i < Choices.Length; i++)
        {
            var rect = new Rectangle(i * Cell, 0, Cell, Cell);
            using (var brush = new SolidBrush(i == _hovered
                       ? Color.FromArgb(70, 120, 200)
                       : Color.FromArgb(i % 2 == 0 ? 240 : 205, i % 2 == 0 ? 217 : 175, i % 2 == 0 ? 181 : 130)))
            {
                g.FillRectangle(brush, rect);
            }
            DrawPiece(g, new Piece(_color, Choices[i]), rect);
        }
    }

    private static void DrawPiece(Graphics g, Piece piece, Rectangle rect) =>
        PieceRenderer.Draw(g, piece, rect);

    /// <summary>
    /// Номер фигуры под курсором или -1. Обычное деление здесь не годится: оно округляет
    /// к нулю, и точка левее доски попала бы в первую клетку.
    /// </summary>
    private int IndexAt(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.Y >= Cell) return -1;
        var index = point.X / Cell;
        return index < Choices.Length ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        if (index == _hovered) return;
        _hovered = index;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        // Превращение — выбор без возврата, поэтому случайная правая кнопка его не делает.
        if (e.Button != MouseButtons.Left) return;
        var index = IndexAt(e.Location);
        if (index < 0) return;
        Selected = Choices[index];
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        PieceType? choice = e.KeyCode switch
        {
            Keys.Q => PieceType.Queen,
            Keys.R => PieceType.Rook,
            Keys.B => PieceType.Bishop,
            Keys.N => PieceType.Knight,
            Keys.Enter => PieceType.Queen,
            _ => null
        };

        if (choice.HasValue)
        {
            Selected = choice.Value;
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
