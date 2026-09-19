using System.Text;

namespace ChessEmulator.Chess;

/// <summary>Чтение и запись партий в формате PGN (с вариантами, комментариями и NAG).</summary>
public static class Pgn
{
    private static readonly string[] HeaderOrder =
    {
        "Event", "Site", "Date", "Round", "White", "Black", "Result",
        "WhiteElo", "BlackElo", "ECO", "Opening", "TimeControl", "Termination", "SetUp", "FEN", "Annotator"
    };

    // --------------------------------------------------------------- Экспорт

    public static string Write(Game game)
    {
        var sb = new StringBuilder();

        foreach (var key in HeaderOrder)
        {
            if (game.Headers.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                sb.AppendLine($"[{key} \"{Escape(value)}\"]");
        }
        foreach (var kv in game.Headers)
        {
            if (!HeaderOrder.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kv.Value))
                sb.AppendLine($"[{kv.Key} \"{Escape(kv.Value)}\"]");
        }
        sb.AppendLine();

        var body = new StringBuilder();
        WriteNodeChildren(body, game.Root, forceNumber: true);
        body.Append(game.Headers.TryGetValue("Result", out var result) ? result : "*");

        sb.AppendLine(WrapLines(body.ToString(), 80));
        return sb.ToString();
    }

    private static void WriteNodeChildren(StringBuilder sb, MoveNode parent, bool forceNumber)
    {
        var node = parent.MainChild;
        if (node == null) return;

        AppendMove(sb, node, forceNumber);

        // Варианты к основному ходу
        for (var i = 1; i < parent.Children.Count; i++)
        {
            sb.Append("( ");
            AppendMove(sb, parent.Children[i], true);
            WriteNodeChildren(sb, parent.Children[i], false);
            sb.Append(") ");
        }

        WriteNodeChildren(sb, node, parent.Children.Count > 1);
    }

    private static void AppendMove(StringBuilder sb, MoveNode node, bool forceNumber)
    {
        if (node.IsWhiteMove) sb.Append(node.MoveNumber).Append(". ");
        else if (forceNumber) sb.Append(node.MoveNumber).Append("... ");

        sb.Append(node.San);
        if (!string.IsNullOrEmpty(node.Glyph)) sb.Append(node.Glyph);
        sb.Append(' ');
        if (!string.IsNullOrWhiteSpace(node.Comment)) sb.Append('{').Append(node.Comment).Append("} ");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string WrapLines(string text, int width)
    {
        var sb = new StringBuilder();
        var lineLength = 0;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (lineLength + token.Length + 1 > width)
            {
                sb.AppendLine();
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                sb.Append(' ');
                lineLength++;
            }
            sb.Append(token);
            lineLength += token.Length;
        }
        return sb.ToString();
    }

    // ----------------------------------------------------------------- Импорт

    /// <summary>Разбирает первую партию из текста PGN.</summary>
    public static Game Read(string pgnText)
    {
        var games = ReadAll(pgnText, 1);
        if (games.Count == 0) throw new FormatException("В файле не найдено ни одной партии.");
        return games[0];
    }

    /// <summary>Разбирает все партии из текста PGN (не более <paramref name="limit"/>).</summary>
    public static List<Game> ReadAll(string pgnText, int limit = int.MaxValue)
    {
        var result = new List<Game>();
        foreach (var chunk in SplitGames(pgnText))
        {
            if (result.Count >= limit) break;
            var game = ParseSingle(chunk);
            if (game != null) result.Add(game);
        }
        return result;
    }

    private static IEnumerable<string> SplitGames(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var seenMoves = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var isHeader = line.StartsWith("[") && line.EndsWith("]");

            if (isHeader && seenMoves && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
                seenMoves = false;
            }

            if (!isHeader && line.Length > 0) seenMoves = true;
            current.AppendLine(raw);
        }

        if (current.ToString().Trim().Length > 0) yield return current.ToString();
    }

    private static Game? ParseSingle(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var moveText = new StringBuilder();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var firstQuote = line.IndexOf('"');
                var lastQuote = line.LastIndexOf('"');
                if (firstQuote > 0 && lastQuote > firstQuote)
                {
                    var key = line[1..firstQuote].Trim();
                    var value = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1)
                        .Replace("\\\"", "\"").Replace("\\\\", "\\");
                    if (key.Length > 0) headers[key] = value;
                }
            }
            else if (!line.StartsWith("%"))
            {
                moveText.Append(line).Append(' ');
            }
        }

        var fen = headers.TryGetValue("FEN", out var f) && !string.IsNullOrWhiteSpace(f) ? f : Position.StartFen;

        Game game;
        try
        {
            game = new Game(fen);
        }
        catch (FormatException)
        {
            game = new Game();
        }

        foreach (var kv in headers) game.Headers[kv.Key] = kv.Value;

        ParseMoveText(game, moveText.ToString());
        game.GoToStart();
        return game;
    }

    private static void ParseMoveText(Game game, string moveText)
    {
        var stack = new Stack<MoveNode>();
        var node = game.Root;
        MoveNode? lastMoveNode = null;
        var i = 0;

        while (i < moveText.Length)
        {
            var c = moveText[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '{')
            {
                var end = moveText.IndexOf('}', i);
                if (end < 0) end = moveText.Length - 1;
                var comment = moveText.Substring(i + 1, end - i - 1).Trim();
                lastMoveNode?.Comment = Append(lastMoveNode.Comment, comment);
                i = end + 1;
                continue;
            }

            if (c == ';')
            {
                i = moveText.Length;
                continue;
            }

            if (c == '(')
            {
                // Вариант начинается от позиции ПЕРЕД последним ходом
                stack.Push(node);
                if (node.Parent != null) node = node.Parent;
                lastMoveNode = null;
                i++;
                continue;
            }

            if (c == ')')
            {
                if (stack.Count > 0) node = stack.Pop();
                lastMoveNode = node.IsRoot ? null : node;
                i++;
                continue;
            }

            if (c == '$')
            {
                var j = i + 1;
                while (j < moveText.Length && char.IsDigit(moveText[j])) j++;
                lastMoveNode?.Glyph = NagToSymbol(moveText.Substring(i + 1, j - i - 1));
                i = j;
                continue;
            }

            // Токен: номер хода, ход или результат
            var start = i;
            while (i < moveText.Length && !char.IsWhiteSpace(moveText[i]) &&
                   moveText[i] != '{' && moveText[i] != '(' && moveText[i] != ')' && moveText[i] != ';')
                i++;

            var token = moveText.Substring(start, i - start).Trim();
            if (token.Length == 0) continue;
            if (token is "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                // Результат из текста партии берём, только если заголовка [Result] не было.
                if (token != "*" && game.Headers.TryGetValue("Result", out var known) && known == "*")
                    game.Headers["Result"] = token;
                continue;
            }

            token = token.TrimStart('.');
            // Номер хода вида "12." или "12..."
            var dot = token.IndexOf('.');
            if (dot >= 0)
            {
                var numeric = token[..dot];
                if (numeric.Length > 0 && numeric.All(char.IsDigit))
                {
                    token = token[dot..].TrimStart('.');
                    if (token.Length == 0) continue;
                }
            }

            if (token.All(char.IsDigit)) continue;

            // Знаки оценки хода пишутся прямо после него: Nf3!?, Qh5??
            var suffix = string.Empty;
            while (token.Length > 0 && (token[^1] == '!' || token[^1] == '?'))
            {
                suffix = token[^1] + suffix;
                token = token[..^1];
            }

            var appended = TryAppend(node, token);
            if (appended != null)
            {
                if (suffix.Length > 0) appended.Glyph = suffix;
                node = appended;
                lastMoveNode = appended;
            }
        }
    }

    private static MoveNode? TryAppend(MoveNode parent, string san)
    {
        if (!parent.Position.TryParseSan(san, out var move)) return null;

        foreach (var child in parent.Children)
        {
            if (child.Move == move) return child;
        }

        var node = MoveNode.Create(parent, move);
        parent.Children.Add(node);
        return node;
    }

    /// <summary>Переводит числовой код NAG в привычный знак ( $2 → ? ).</summary>
    private static string? NagToSymbol(string digits) => digits switch
    {
        "1" => "!",
        "2" => "?",
        "3" => "!!",
        "4" => "??",
        "5" => "!?",
        "6" => "?!",
        _ => null
    };

    private static string Append(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : existing + " " + addition;
}
