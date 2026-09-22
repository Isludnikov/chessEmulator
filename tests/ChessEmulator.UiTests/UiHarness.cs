using System.Reflection;

namespace ChessEmulator.UiTests;

/// <summary>
/// Помощники для проверки контролов без окна: синтетическая мышь, отрисовка в картинку
/// и подсчёт «чернил» — закрашенных пикселей.
/// </summary>
internal static class UiHarness
{
    private const BindingFlags Protected = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Raise(Control control, string method, EventArgs args)
    {
        var info = control.GetType().GetMethod(method, Protected, null, new[] { args.GetType() }, null)
                   ?? throw new MissingMethodException(control.GetType().Name, method);
        info.Invoke(control, new object[] { args });
    }

    // -------------------------------------------------------------- Мышь

    public static void MouseDown(Control control, Point at, MouseButtons button = MouseButtons.Left) =>
        Raise(control, "OnMouseDown", new MouseEventArgs(button, 1, at.X, at.Y, 0));

    public static void MouseMove(Control control, Point at, MouseButtons button = MouseButtons.Left) =>
        Raise(control, "OnMouseMove", new MouseEventArgs(button, 0, at.X, at.Y, 0));

    public static void MouseUp(Control control, Point at, MouseButtons button = MouseButtons.Left) =>
        Raise(control, "OnMouseUp", new MouseEventArgs(button, 1, at.X, at.Y, 0));

    /// <summary>Событие OnMouseClick — его слушают контролы, которым не нужен весь цикл нажатия.</summary>
    public static void MouseClick(Control control, Point at, MouseButtons button = MouseButtons.Left) =>
        Raise(control, "OnMouseClick", new MouseEventArgs(button, 1, at.X, at.Y, 0));

    /// <summary>Нажатие клавиши.</summary>
    public static void KeyDown(Control control, Keys key) =>
        Raise(control, "OnKeyDown", new KeyEventArgs(key));

    /// <summary>
    /// Нажатие кнопки внутри формы. PerformClick здесь не годится: он молча ничего не делает,
    /// пока форма не показана, — а показывать окна тесты не должны.
    /// </summary>
    public static void Press(Control control) => Raise(control, "OnClick", EventArgs.Empty);

    /// <summary>Щелчок без перетаскивания: нажали и отпустили в одной точке.</summary>
    public static void Click(Control control, Point at, MouseButtons button = MouseButtons.Left)
    {
        MouseDown(control, at, button);
        MouseUp(control, at, button);
    }

    /// <summary>Перетаскивание с промежуточным движением — иначе контрол считает это щелчком.</summary>
    public static void Drag(Control control, Point from, Point to)
    {
        MouseDown(control, from);
        var drag = SystemInformation.DragSize;
        MouseMove(control, new Point(from.X + drag.Width + 4, from.Y + drag.Height + 4));
        MouseMove(control, to);
        MouseUp(control, to);
    }

    // --------------------------------------------------------- Отрисовка

    /// <summary>Рисует контрол в картинку, минуя создание окна.</summary>
    public static Bitmap Render(Control control)
    {
        var size = control.ClientSize;
        var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        using var g = Graphics.FromImage(bitmap);
        g.Clear(control.BackColor);
        Raise(control, "OnPaint", new PaintEventArgs(g, new Rectangle(Point.Empty, size)));
        return bitmap;
    }

    /// <summary>Доля пикселей, отличающихся от фона — так видно, что нарисовано хоть что-то.</summary>
    public static double InkFraction(Bitmap bitmap, Color background)
    {
        int ink = 0, total = bitmap.Width * bitmap.Height;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!Similar(bitmap.GetPixel(x, y), background)) ink++;
            }
        }
        return total == 0 ? 0 : (double)ink / total;
    }

    /// <summary>Доля закрашенных пикселей внутри прямоугольника.</summary>
    public static double InkFraction(Bitmap bitmap, Rectangle area, Color background)
    {
        int ink = 0, total = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) continue;
                total++;
                if (!Similar(bitmap.GetPixel(x, y), background)) ink++;
            }
        }
        return total == 0 ? 0 : (double)ink / total;
    }

    /// <summary>Сколько пикселей различаются на двух картинках одного размера.</summary>
    public static int Difference(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return int.MaxValue;
        var diff = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) diff++;
            }
        }
        return diff;
    }

    /// <summary>Сколько пикселей различаются внутри прямоугольника.</summary>
    public static int Difference(Bitmap a, Bitmap b, Rectangle area)
    {
        var diff = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (x < 0 || y < 0 || x >= a.Width || y >= a.Height) continue;
                if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) diff++;
            }
        }
        return diff;
    }

    private static bool Similar(Color a, Color b, int tolerance = 12) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    /// <summary>Ищет кнопку с указанной надписью в дереве контролов.</summary>
    public static Button FindButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && button.Text == text) return button;
            var found = TryFindButton(child, text);
            if (found != null) return found;
        }
        throw new InvalidOperationException($"Кнопка «{text}» не найдена.");
    }

    private static Button? TryFindButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && button.Text == text) return button;
            var found = TryFindButton(child, text);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Прямоугольник, в который укладывается всё нарисованное поверх фона.</summary>
    public static Rectangle InkBounds(Bitmap bitmap, Color background)
    {
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (Similar(bitmap.GetPixel(x, y), background)) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    /// <summary>
    /// Насколько пятно цвета <paramref name="match"/> отличается от своего зеркального отражения,
    /// в долях площади. Тень фигуры сдвинута вбок и в этот цвет не попадает, поэтому
    /// проверяется именно симметрия самой фигуры.
    /// </summary>
    public static double MirrorDifference(Bitmap bitmap, Color match)
    {
        int diff = 0, total = bitmap.Width * bitmap.Height;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                bool here = Similar(bitmap.GetPixel(x, y), match, 30);
                bool mirrored = Similar(bitmap.GetPixel(bitmap.Width - 1 - x, y), match, 30);
                if (here != mirrored) diff++;
            }
        }
        return total == 0 ? 0 : (double)diff / total;
    }

    /// <summary>Все контролы дерева, включая вложенные.</summary>
    public static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var inner in All(child)) yield return inner;
        }
    }

    /// <summary>Ищет контрол нужного типа с указанной надписью.</summary>
    public static T ByText<T>(Control root, string text) where T : Control =>
        All(root).OfType<T>().FirstOrDefault(c => c.Text == text)
        ?? throw new InvalidOperationException($"{typeof(T).Name} «{text}» не найден.");

    /// <summary>Ищет контрол нужного типа в дереве.</summary>
    public static T Find<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) return match;
            try
            {
                return Find<T>(child);
            }
            catch (InvalidOperationException)
            {
                // продолжаем обход
            }
        }
        throw new InvalidOperationException($"Контрол {typeof(T).Name} не найден.");
    }
}
