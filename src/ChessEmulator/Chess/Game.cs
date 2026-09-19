namespace ChessEmulator.Chess;

/// <summary>Узел дерева ходов. Первый потомок — основная линия, остальные — варианты.</summary>
public sealed class MoveNode
{
    public MoveNode? Parent { get; internal set; }
    public List<MoveNode> Children { get; } = new();

    public Move Move { get; init; }
    public string San { get; init; } = string.Empty;
    public required Position Position { get; init; }

    /// <summary>Полуход от начала партии (0 — стартовая позиция).</summary>
    public int Ply { get; init; }

    public string? Comment { get; set; }
    public string? Glyph { get; set; }

    /// <summary>Оценка движка в сантипешках с точки зрения белых (после анализа партии).</summary>
    public int? EvalCp { get; set; }

    public int? MateIn { get; set; }

    /// <summary>Лучший ход по мнению движка в этой позиции (UCI).</summary>
    public string? BestReply { get; set; }

    /// <summary>Номер хода в записи партии (1, 2, 3…).</summary>
    public int MoveNumber { get; init; } = 1;

    /// <summary>Цвет, сделавший этот ход.</summary>
    public PieceColor SideMoved { get; init; }

    public bool IsWhiteMove => SideMoved == PieceColor.White;

    public bool IsRoot => Parent == null;

    public MoveNode? MainChild => Children.Count > 0 ? Children[0] : null;

    /// <summary>Создаёт узел-потомок для хода из позиции родителя.</summary>
    public static MoveNode Create(MoveNode parent, Move move)
    {
        var pos = parent.Position;
        return new MoveNode
        {
            Parent = parent,
            Move = move,
            San = pos.ToSan(move),
            Position = pos.MakeMove(move),
            Ply = parent.Ply + 1,
            MoveNumber = pos.FullmoveNumber,
            SideMoved = pos.SideToMove
        };
    }

    public IEnumerable<MoveNode> PathFromRoot()
    {
        var stack = new Stack<MoveNode>();
        for (var n = this; n != null && !n.IsRoot; n = n.Parent) stack.Push(n);
        return stack;
    }
}

/// <summary>Партия: дерево ходов, заголовки PGN и текущая позиция просмотра.</summary>
public sealed class Game
{
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Event"] = "?",
        ["Site"] = "?",
        ["Date"] = DateTime.Now.ToString("yyyy.MM.dd"),
        ["Round"] = "?",
        ["White"] = "?",
        ["Black"] = "?",
        ["Result"] = "*"
    };

    public string StartFen { get; private set; }
    public MoveNode Root { get; private set; }
    public MoveNode Current { get; private set; }

    public event EventHandler? Changed;

    public Game(string? fen = null)
    {
        StartFen = string.IsNullOrWhiteSpace(fen) ? Position.StartFen : fen.Trim();
        var start = Position.FromFen(StartFen);
        Root = new MoveNode { Position = start, Ply = 0 };
        Current = Root;
        if (StartFen != Position.StartFen)
        {
            Headers["FEN"] = StartFen;
            Headers["SetUp"] = "1";
        }
    }

    public Position CurrentPosition => Current.Position;

    public void Reset(string? fen = null)
    {
        StartFen = string.IsNullOrWhiteSpace(fen) ? Position.StartFen : fen!.Trim();
        var start = Position.FromFen(StartFen);
        Root = new MoveNode { Position = start, Ply = 0 };
        Current = Root;
        Headers["Result"] = "*";
        Headers["Date"] = DateTime.Now.ToString("yyyy.MM.dd");
        if (StartFen != Position.StartFen)
        {
            Headers["FEN"] = StartFen;
            Headers["SetUp"] = "1";
        }
        else
        {
            Headers.Remove("FEN");
            Headers.Remove("SetUp");
        }
        OnChanged();
    }

    /// <summary>Добавляет ход из текущей позиции; если такой ход уже есть — просто переходит к нему.</summary>
    public MoveNode AddMove(Move move)
    {
        foreach (var child in Current.Children)
        {
            if (child.Move == move)
            {
                Current = child;
                OnChanged();
                return child;
            }
        }

        var node = MoveNode.Create(Current, move);
        Current.Children.Add(node);
        Current = node;
        UpdateResultHeader();
        OnChanged();
        return node;
    }

    public bool TryAddSan(string san, out MoveNode? node)
    {
        node = null;
        if (!Current.Position.TryParseSan(san, out var move)) return false;
        node = AddMove(move);
        return true;
    }

    public void GoTo(MoveNode node)
    {
        Current = node;
        OnChanged();
    }

    public bool GoForward()
    {
        var next = Current.MainChild;
        if (next == null) return false;
        Current = next;
        OnChanged();
        return true;
    }

    public bool GoBack()
    {
        if (Current.Parent == null) return false;
        Current = Current.Parent;
        OnChanged();
        return true;
    }

    public void GoToStart()
    {
        Current = Root;
        OnChanged();
    }

    public void GoToEnd()
    {
        var n = Current;
        while (n.MainChild != null) n = n.MainChild;
        Current = n;
        OnChanged();
    }

    /// <summary>Удаляет текущий ход вместе со всем продолжением.</summary>
    public bool DeleteCurrent()
    {
        if (Current.IsRoot || Current.Parent == null) return false;
        var parent = Current.Parent;
        parent.Children.Remove(Current);
        Current = parent;
        UpdateResultHeader();
        OnChanged();
        return true;
    }

    /// <summary>Удаляет все ходы после текущего.</summary>
    public bool TruncateAfterCurrent()
    {
        if (Current.Children.Count == 0) return false;
        Current.Children.Clear();
        UpdateResultHeader();
        OnChanged();
        return true;
    }

    /// <summary>Делает вариант, содержащий текущий ход, основной линией.</summary>
    public bool PromoteToMainLine()
    {
        var node = Current;
        var changed = false;
        while (node.Parent != null)
        {
            var parent = node.Parent;
            var idx = parent.Children.IndexOf(node);
            if (idx > 0)
            {
                parent.Children.RemoveAt(idx);
                parent.Children.Insert(0, node);
                changed = true;
            }
            node = parent;
        }
        if (changed) OnChanged();
        return changed;
    }

    public IReadOnlyList<MoveNode> MainLine()
    {
        var list = new List<MoveNode>();
        for (var n = Root.MainChild; n != null; n = n.MainChild) list.Add(n);
        return list;
    }

    /// <summary>Ходы от корня до текущего узла в формате UCI — для команды position.</summary>
    public List<string> UciMovesToCurrent()
    {
        var moves = new List<string>();
        foreach (var node in Current.PathFromRoot()) moves.Add(node.Move.ToUci());
        return moves;
    }

    // -------------------------------------------------- Определение результата

    public bool IsThreefoldRepetition(MoveNode node)
    {
        var key = node.Position.RepetitionKey();
        var count = 0;
        for (var n = node; n != null; n = n.Parent)
        {
            if (n.Position.RepetitionKey() == key) count++;
            if (count >= 3) return true;
        }
        return false;
    }

    public (GameResultState State, GameEndReason Reason) EvaluateState(MoveNode node)
    {
        var pos = node.Position;
        if (pos.LegalMoves.Count == 0)
        {
            if (pos.IsInCheck())
            {
                return pos.SideToMove == PieceColor.White
                    ? (GameResultState.BlackWins, GameEndReason.Checkmate)
                    : (GameResultState.WhiteWins, GameEndReason.Checkmate);
            }
            return (GameResultState.Draw, GameEndReason.Stalemate);
        }

        if (pos.HasInsufficientMaterial()) return (GameResultState.Draw, GameEndReason.InsufficientMaterial);
        if (pos.HalfmoveClock >= 100) return (GameResultState.Draw, GameEndReason.FiftyMoveRule);
        if (IsThreefoldRepetition(node)) return (GameResultState.Draw, GameEndReason.ThreefoldRepetition);
        return (GameResultState.InProgress, GameEndReason.None);
    }

    private void UpdateResultHeader()
    {
        var line = MainLine();
        var last = line.Count > 0 ? line[^1] : Root;
        var (state, _) = EvaluateState(last);
        Headers["Result"] = state switch
        {
            GameResultState.WhiteWins => "1-0",
            GameResultState.BlackWins => "0-1",
            GameResultState.Draw => "1/2-1/2",
            _ => Headers.TryGetValue("Result", out var r) && r != "*" && line.Count == 0 ? r : "*"
        };
    }

    public void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    internal void SetRoot(MoveNode root, string startFen)
    {
        Root = root;
        StartFen = startFen;
        Current = root;
    }
}
