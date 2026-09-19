using System.ComponentModel;
using System.Drawing.Drawing2D;
using ChessEmulator.Chess;

namespace ChessEmulator.UI;

/// <summary>
/// Запись партии: ходы основной линии и вариантов в виде кликабельного текста.
/// </summary>
public sealed class MoveListView : Panel
{
    private sealed class Token
    {
        public string Text = string.Empty;
        public MoveNode? Node;
        public int Depth;
        public TokenKind Kind;
        public RectangleF Bounds;
    }

    private enum TokenKind { MoveNumber, San, Bracket, Comment, Eval, Glyph }

    private readonly List<Token> _tokens = new();
    private Game? _game;
    private bool _layoutDirty = true;
    private Token? _hot;

    private readonly Font _mainFont;
    private readonly Font _numberFont;
    private readonly Font _varFont;
    private readonly Font _commentFont;

    public MoveListView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        BackColor = Color.FromArgb(32, 32, 34);
        ForeColor = Color.Gainsboro;
        Padding = new Padding(8, 6, 8, 6);

        _mainFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _numberFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
        _varFont = new Font("Segoe UI", 9f, FontStyle.Italic);
        _commentFont = new Font("Segoe UI", 9f, FontStyle.Regular);
    }

    public event EventHandler<MoveNode>? NodeSelected;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Game? Game
    {
        get => _game;
        set
        {
            _game = value;
            Reload();
        }
    }

    /// <summary>Показывать оценки движка рядом с ходами (после анализа партии).</summary>
    [DefaultValue(true)]
    public bool ShowEvaluations { get; set; } = true;

    public void Reload()
    {
        _layoutDirty = true;
        Invalidate();
    }

    public void ScrollToCurrent()
    {
        if (_game == null) return;
        var token = _tokens.FirstOrDefault(t => t.Kind == TokenKind.San && t.Node == _game.Current);
        if (token == null) return;

        var top = (int)token.Bounds.Top + Math.Abs(AutoScrollPosition.Y);
        var bottom = (int)token.Bounds.Bottom + Math.Abs(AutoScrollPosition.Y);
        var viewTop = Math.Abs(AutoScrollPosition.Y);
        var viewBottom = viewTop + ClientSize.Height;

        if (top < viewTop) AutoScrollPosition = new Point(0, Math.Max(0, top - 20));
        else if (bottom > viewBottom) AutoScrollPosition = new Point(0, Math.Max(0, bottom - ClientSize.Height + 20));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _layoutDirty = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (_layoutDirty) BuildLayout(g);

        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

        var current = _game?.Current;

        foreach (var token in _tokens)
        {
            var font = FontFor(token);
            var color = ColorFor(token);

            if (token.Kind == TokenKind.San && token.Node != null)
            {
                if (token.Node == current)
                {
                    using var brush = new SolidBrush(Color.FromArgb(70, 120, 200));
                    g.FillRectangle(brush, Rectangle.Round(Inflate(token.Bounds)));
                    color = Color.White;
                }
                else if (token == _hot)
                {
                    using var brush = new SolidBrush(Color.FromArgb(58, 58, 62));
                    g.FillRectangle(brush, Rectangle.Round(Inflate(token.Bounds)));
                }
            }

            using var textBrush = new SolidBrush(color);
            g.DrawString(token.Text, font, textBrush, token.Bounds.Location);
        }

        if (_tokens.Count == 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(120, 120, 125));
            g.DrawString("Ходов пока нет. Сделайте ход на доске или откройте PGN.",
                _commentFont, brush, new PointF(Padding.Left, Padding.Top));
        }
    }

    private static RectangleF Inflate(RectangleF r) => new(r.X - 3, r.Y - 1, r.Width + 6, r.Height + 2);

    private Font FontFor(Token token) => token.Kind switch
    {
        TokenKind.San => token.Depth == 0 ? _mainFont : _varFont,
        TokenKind.MoveNumber => token.Depth == 0 ? _numberFont : _varFont,
        TokenKind.Comment => _commentFont,
        TokenKind.Eval => _commentFont,
        TokenKind.Glyph => token.Depth == 0 ? _mainFont : _varFont,
        _ => _varFont
    };

    private Color ColorFor(Token token) => token.Kind switch
    {
        TokenKind.San => token.Depth == 0 ? Color.Gainsboro : Color.FromArgb(160, 170, 185),
        TokenKind.MoveNumber => Color.FromArgb(130, 130, 138),
        TokenKind.Comment => Color.FromArgb(140, 180, 140),
        TokenKind.Eval => Color.FromArgb(150, 150, 160),
        TokenKind.Glyph => Color.FromArgb(220, 180, 90),
        _ => Color.FromArgb(120, 120, 128)
    };

    // ------------------------------------------------------------- Раскладка

    private void BuildLayout(Graphics g)
    {
        _tokens.Clear();
        _layoutDirty = false;

        if (_game == null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }

        EmitLine(_game.Root, 0, true);

        float x = Padding.Left;
        float y = Padding.Top;
        float maxWidth = Math.Max(120, ClientSize.Width - Padding.Horizontal);
        float lineHeight = 0;
        var previousDepth = 0;

        foreach (var token in _tokens)
        {
            var font = FontFor(token);
            var size = g.MeasureString(token.Text, font, int.MaxValue, StringFormat.GenericTypographic);
            size.Width += 6;

            var startsVariation = token.Kind == TokenKind.Bracket && token.Text == "(";
            var backToMain = token.Depth == 0 && previousDepth > 0;

            if ((x + size.Width > maxWidth && x > Padding.Left) || (startsVariation && x > Padding.Left) || backToMain)
            {
                x = Padding.Left + token.Depth * 16;
                y += lineHeight > 0 ? lineHeight + 3 : 20;
                lineHeight = 0;
            }

            token.Bounds = new RectangleF(x, y, size.Width, size.Height);
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            previousDepth = token.Depth;
        }

        AutoScrollMinSize = new Size(0, (int)(y + lineHeight + Padding.Bottom + 4));
    }

    private void EmitLine(MoveNode parent, int depth, bool forceNumber)
    {
        var node = parent.MainChild;
        while (node != null)
        {
            EmitMove(node, depth, forceNumber);
            forceNumber = false;

            var owner = node.Parent!;
            if (owner.Children.Count > 1)
            {
                for (var i = 1; i < owner.Children.Count; i++)
                {
                    var variation = owner.Children[i];
                    Add("(", null, depth + 1, TokenKind.Bracket);
                    EmitMove(variation, depth + 1, true);
                    EmitLine(variation, depth + 1, false);
                    Add(")", null, depth + 1, TokenKind.Bracket);
                }
                forceNumber = true;
            }

            node = node.MainChild;
        }
    }

    private void EmitMove(MoveNode node, int depth, bool forceNumber)
    {
        if (node.IsWhiteMove) Add($"{node.MoveNumber}.", node, depth, TokenKind.MoveNumber);
        else if (forceNumber) Add($"{node.MoveNumber}...", node, depth, TokenKind.MoveNumber);

        Add(node.San, node, depth, TokenKind.San);

        if (!string.IsNullOrEmpty(node.Glyph)) Add(node.Glyph!, node, depth, TokenKind.Glyph);

        if (ShowEvaluations && (node.EvalCp.HasValue || node.MateIn.HasValue))
            Add(FormatEval(node), node, depth, TokenKind.Eval);

        if (!string.IsNullOrWhiteSpace(node.Comment))
            Add(node.Comment!.Trim(), node, depth, TokenKind.Comment);
    }

    private static string FormatEval(MoveNode node)
    {
        if (node.MateIn.HasValue) return node.MateIn.Value > 0 ? $"(#{node.MateIn})" : $"(#-{Math.Abs(node.MateIn.Value)})";
        return $"({Engine.EngineInfo.FormatPawns(node.EvalCp ?? 0)})";
    }

    private void Add(string text, MoveNode? node, int depth, TokenKind kind) =>
        _tokens.Add(new Token { Text = text, Node = node, Depth = depth, Kind = kind });

    // ------------------------------------------------------------------ Мышь

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var token = HitTest(e.Location);
        if (token == _hot) return;
        _hot = token;
        Cursor = token != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = null;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var token = HitTest(e.Location);
        if (token?.Node == null) return;
        NodeSelected?.Invoke(this, token.Node);
    }

    private Token? HitTest(Point location)
    {
        var point = new PointF(location.X - AutoScrollPosition.X, location.Y - AutoScrollPosition.Y);
        foreach (var token in _tokens)
        {
            if (token.Kind == TokenKind.San && token.Node != null && Inflate(token.Bounds).Contains(point))
                return token;
        }
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mainFont.Dispose();
            _numberFont.Dispose();
            _varFont.Dispose();
            _commentFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
