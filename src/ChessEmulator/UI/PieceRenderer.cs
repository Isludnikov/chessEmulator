using System.Drawing.Drawing2D;
using ChessEmulator.Chess;

namespace ChessEmulator.UI;

/// <summary>
/// Отрисовка фигур собственной векторной графикой. Каждая фигура нарисована в квадрате
/// 100×100 и вписывается в клетку целиком, поэтому вид не зависит от шрифтов системы,
/// а высоты фигур соотносятся правильно: пешка ниже ладьи, король выше всех.
/// </summary>
public static class PieceRenderer
{
    /// <summary>Сторона квадрата, в котором заданы координаты фигур.</summary>
    private const float Design = 100f;

    /// <summary>Доля клетки, которую занимает фигура.</summary>
    private const float Fit = 0.92f;

    private sealed record PieceArt(GraphicsPath Shape, GraphicsPath? Lines, GraphicsPath? Dots) : IDisposable
    {
        public void Dispose()
        {
            Shape.Dispose();
            Lines?.Dispose();
            Dots?.Dispose();
        }
    }

    public static void Draw(Graphics g, Piece piece, Rectangle rect)
    {
        if (piece.IsEmpty || rect.Height < 4) return;

        using var art = Build(piece.Type, rect);

        bool white = piece.Color == PieceColor.White;
        var fill = white ? Color.FromArgb(252, 252, 250) : Color.FromArgb(32, 32, 34);
        var outline = white ? Color.FromArgb(28, 28, 30) : Color.FromArgb(232, 232, 232);

        float scale = Math.Min(rect.Width, rect.Height) * Fit / Design;
        using var pen = new Pen(outline, Math.Max(1f, scale * 2.4f))
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        var state = g.Save();
        g.TranslateTransform(rect.Height * 0.02f, rect.Height * 0.03f);
        using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillPath(shadow, art.Shape);
        g.Restore(state);

        using (var brush = new SolidBrush(fill)) g.FillPath(brush, art.Shape);
        g.DrawPath(pen, art.Shape);

        if (art.Lines != null) g.DrawPath(pen, art.Lines);
        if (art.Dots != null)
        {
            using var ink = new SolidBrush(outline);
            g.FillPath(ink, art.Dots);
        }
    }

    // ------------------------------------------------------------ Сборка

    private static PieceArt Build(PieceType type, Rectangle rect)
    {
        var shape = new GraphicsPath { FillMode = FillMode.Winding };
        GraphicsPath? lines = null;
        GraphicsPath? dots = null;

        switch (type)
        {
            case PieceType.Pawn: Pawn(shape); break;
            case PieceType.Rook: Rook(shape); break;
            case PieceType.Bishop: Bishop(shape, out lines); break;
            case PieceType.Knight: Knight(shape, out lines, out dots); break;
            case PieceType.Queen: Queen(shape); break;
            case PieceType.King: King(shape); break;
            default: return new PieceArt(shape, null, null);
        }

        var transform = FitTransform(rect);
        shape.Transform(transform);
        lines?.Transform(transform);
        dots?.Transform(transform);
        return new PieceArt(shape, lines, dots);
    }

    private static Matrix FitTransform(Rectangle rect)
    {
        float scale = Math.Min(rect.Width, rect.Height) * Fit / Design;
        var matrix = new Matrix();
        matrix.Translate(
            rect.X + (rect.Width - Design * scale) / 2f,
            rect.Y + (rect.Height - Design * scale) / 2f);
        matrix.Scale(scale, scale);
        return matrix;
    }

    // ------------------------------------------------------------ Фигуры

    private static void Pawn(GraphicsPath p)
    {
        // Пешка заметно ниже прочих фигур — по ней сразу видно ряд пешек на доске
        Pedestal(p, halfBase: 26, halfStep: 20);
        Body(p, bottomHalf: 18, topHalf: 9, topY: 58, c1: (17, 71), c2: (10, 64));
        Collar(p, topHalf: 15, bottomHalf: 11, top: 49, bottom: 56);
        Circle(p, 50, 35, 12);
    }

    private static void Rook(GraphicsPath p)
    {
        Pedestal(p, halfBase: 30, halfStep: 24);
        Body(p, bottomHalf: 22, topHalf: 16, topY: 46, c1: (21, 68), c2: (16, 56));
        Collar(p, topHalf: 20, bottomHalf: 16, top: 37, bottom: 44);

        // Зубцы: три выступа с двумя прорезями между ними
        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(27, 35), new PointF(27, 13), new PointF(37, 13), new PointF(37, 21),
            new PointF(45, 21), new PointF(45, 13), new PointF(55, 13), new PointF(55, 21),
            new PointF(63, 21), new PointF(63, 13), new PointF(73, 13), new PointF(73, 35)
        });
    }

    private static void Bishop(GraphicsPath p, out GraphicsPath? lines)
    {
        Pedestal(p, halfBase: 28, halfStep: 22);
        Body(p, bottomHalf: 20, topHalf: 10, topY: 50, c1: (19, 68), c2: (11, 58));
        Collar(p, topHalf: 17, bottomHalf: 12, top: 41, bottom: 48);

        // Митра: капля с остриём вверху
        p.StartFigure();
        p.AddBezier(new PointF(50, 8), new PointF(40, 19), new PointF(32, 26), new PointF(32, 32));
        p.AddBezier(new PointF(32, 32), new PointF(32, 36), new PointF(36, 39), new PointF(39, 40));
        p.AddLine(new PointF(39, 40), new PointF(61, 40));
        p.AddBezier(new PointF(61, 40), new PointF(64, 39), new PointF(68, 36), new PointF(68, 32));
        p.AddBezier(new PointF(68, 32), new PointF(68, 26), new PointF(60, 19), new PointF(50, 8));
        p.CloseFigure();

        // Прорезь на митре — главная примета слона
        lines = new GraphicsPath();
        lines.AddLine(new PointF(43, 34), new PointF(58, 19));
    }

    private static void Knight(GraphicsPath p, out GraphicsPath? lines, out GraphicsPath? dots)
    {
        Pedestal(p, halfBase: 29, halfStep: 23);

        // Голова коня в профиль: морда смотрит вправо, грива уходит назад
        p.StartFigure();
        p.AddBezier(new PointF(27, 76), new PointF(24, 58), new PointF(26, 40), new PointF(33, 28));
        p.AddBezier(new PointF(33, 28), new PointF(35, 22), new PointF(36, 16), new PointF(38, 10));
        p.AddLine(new PointF(38, 10), new PointF(44, 23));
        p.AddBezier(new PointF(44, 23), new PointF(50, 17), new PointF(58, 16), new PointF(65, 21));
        p.AddBezier(new PointF(65, 21), new PointF(73, 27), new PointF(79, 34), new PointF(81, 41));
        p.AddBezier(new PointF(81, 41), new PointF(82, 45), new PointF(78, 47), new PointF(72, 46));
        p.AddBezier(new PointF(72, 46), new PointF(64, 45), new PointF(58, 48), new PointF(56, 55));
        p.AddBezier(new PointF(56, 55), new PointF(55, 64), new PointF(62, 70), new PointF(70, 76));
        p.CloseFigure();

        // Грива тремя штрихами вдоль загривка
        lines = new GraphicsPath();
        lines.AddLine(new PointF(34, 31), new PointF(43, 36));
        lines.StartFigure();
        lines.AddLine(new PointF(30, 43), new PointF(40, 47));
        lines.StartFigure();
        lines.AddLine(new PointF(28, 55), new PointF(38, 58));

        dots = new GraphicsPath();
        dots.AddEllipse(58, 27, 5, 5);
    }

    private static void Queen(GraphicsPath p)
    {
        Pedestal(p, halfBase: 30, halfStep: 24);
        Body(p, bottomHalf: 22, topHalf: 12, topY: 48, c1: (21, 68), c2: (13, 57));
        Collar(p, topHalf: 20, bottomHalf: 14, top: 38, bottom: 46);

        // Корона из пяти зубцов
        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(30, 36), new PointF(30, 27), new PointF(34, 16), new PointF(38, 26),
            new PointF(43, 13), new PointF(47, 25), new PointF(50, 11), new PointF(53, 25),
            new PointF(57, 13), new PointF(62, 26), new PointF(66, 16), new PointF(70, 27),
            new PointF(70, 36)
        });

        Circle(p, 50, 8, 5);
    }

    private static void King(GraphicsPath p)
    {
        Pedestal(p, halfBase: 30, halfStep: 24);
        Body(p, bottomHalf: 22, topHalf: 12, topY: 48, c1: (21, 68), c2: (13, 57));
        Collar(p, topHalf: 20, bottomHalf: 14, top: 38, bottom: 46);

        // Корона
        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(31, 36), new PointF(36, 22), new PointF(64, 22), new PointF(69, 36)
        });

        // Крест
        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(46, 22), new PointF(46, 14), new PointF(39, 14), new PointF(39, 8),
            new PointF(46, 8), new PointF(46, 2), new PointF(54, 2), new PointF(54, 8),
            new PointF(61, 8), new PointF(61, 14), new PointF(54, 14), new PointF(54, 22)
        });
    }

    // ------------------------------------------------- Строительные блоки

    /// <summary>Плита и ступенька под корпусом — общее основание всех фигур.</summary>
    private static void Pedestal(GraphicsPath p, float halfBase, float halfStep)
    {
        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(50 - halfBase, 85), new PointF(50 + halfBase, 85),
            new PointF(50 + halfBase, 94), new PointF(50 - halfBase, 94)
        });

        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(50 - halfStep, 76), new PointF(50 + halfStep, 76),
            new PointF(50 + halfBase - 1, 83), new PointF(50 - halfBase + 1, 83)
        });
    }

    /// <summary>Корпус — симметричная вогнутая ножка от ступеньки вверх.</summary>
    private static void Body(GraphicsPath p, float bottomHalf, float topHalf, float topY,
        (float Half, float Y) c1, (float Half, float Y) c2)
    {
        p.StartFigure();
        p.AddBezier(
            new PointF(50 - bottomHalf, 76), new PointF(50 - c1.Half, c1.Y),
            new PointF(50 - c2.Half, c2.Y), new PointF(50 - topHalf, topY));
        p.AddLine(new PointF(50 - topHalf, topY), new PointF(50 + topHalf, topY));
        p.AddBezier(
            new PointF(50 + topHalf, topY), new PointF(50 + c2.Half, c2.Y),
            new PointF(50 + c1.Half, c1.Y), new PointF(50 + bottomHalf, 76));
        p.CloseFigure();
    }

    /// <summary>Воротник — трапеция, расширяющаяся кверху.</summary>
    private static void Collar(GraphicsPath p, float topHalf, float bottomHalf, float top, float bottom)
    {
        p.StartFigure();
        p.AddPolygon(new[]
        {
            new PointF(50 - topHalf, top), new PointF(50 + topHalf, top),
            new PointF(50 + bottomHalf, bottom), new PointF(50 - bottomHalf, bottom)
        });
    }

    private static void Circle(GraphicsPath p, float centerX, float centerY, float radius)
    {
        p.StartFigure();
        p.AddEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2);
    }
}
