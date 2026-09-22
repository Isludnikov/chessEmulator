using System.Drawing.Drawing2D;

namespace ChessEmulator.UI;

/// <summary>Вертикальная шкала оценки позиции: белая часть снизу, чёрная сверху.</summary>
public sealed class EvalBar : Control
{
    private double _target = 0.5;
    private double _shown = 0.5;
    private string _text = "0.00";
    private readonly System.Windows.Forms.Timer _animation;

    public EvalBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Width = 28;
        BackColor = Color.FromArgb(32, 32, 34);

        _animation = new System.Windows.Forms.Timer { Interval = 16 };
        _animation.Tick += (_, _) =>
        {
            var delta = _target - _shown;
            if (Math.Abs(delta) < 0.002)
            {
                _shown = _target;
                _animation.Stop();
            }
            else
            {
                _shown += delta * 0.25;
            }
            Invalidate();
        };
    }

    /// <summary>Оценка в сантипешках с точки зрения белых.</summary>
    public void SetEvaluation(int? centipawns, int? mateIn)
    {
        if (mateIn.HasValue)
        {
            _target = mateIn.Value > 0 ? 1.0 : 0.0;
            // «Мат в ноль» — мат уже на доске. Знака у нуля нет, писать «#-0» незачем.
            _text = (mateIn.Value < 0 ? "#-" : "#") + Math.Abs(mateIn.Value);
        }
        else if (centipawns.HasValue)
        {
            var pawns = centipawns.Value / 100.0;
            // Сигмоида: перевес в 4 пешки ≈ 90 % шкалы.
            _target = 1.0 / (1.0 + Math.Exp(-pawns * 0.55));
            _text = Engine.EngineInfo.FormatPawns(centipawns.Value);
        }
        else
        {
            _target = 0.5;
            _text = "—";
        }

        if (!_animation.Enabled) _animation.Start();
        Invalidate();
    }

    public void Clear() => SetEvaluation(null, null);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var h = ClientSize.Height;
        var w = ClientSize.Width;
        var whiteHeight = (int)Math.Round(h * Math.Clamp(_shown, 0, 1));

        using (var black = new SolidBrush(Color.FromArgb(48, 48, 52)))
            g.FillRectangle(black, 0, 0, w, h - whiteHeight);
        using (var white = new SolidBrush(Color.FromArgb(238, 238, 238)))
            g.FillRectangle(white, 0, h - whiteHeight, w, whiteHeight);

        using (var mid = new Pen(Color.FromArgb(90, 150, 150, 160), 1))
            g.DrawLine(mid, 0, h / 2f, w, h / 2f);

        using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var whiteAhead = _shown >= 0.5;
        var textRect = whiteAhead
            ? new RectangleF(0, h - 18, w, 16)
            : new RectangleF(0, 2, w, 16);
        using var brush = new SolidBrush(whiteAhead ? Color.FromArgb(40, 40, 42) : Color.Gainsboro);
        g.DrawString(_text, font, brush, textRect, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
