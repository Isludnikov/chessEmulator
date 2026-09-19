using System.Globalization;

namespace ChessEmulator.Engine;

/// <summary>Одна строка анализа (info depth … pv …) от UCI-движка.</summary>
public sealed class EngineInfo
{
    public int Depth { get; set; }
    public int SelDepth { get; set; }
    public int MultiPv { get; set; } = 1;
    public int? ScoreCp { get; set; }
    public int? ScoreMate { get; set; }
    public bool LowerBound { get; set; }
    public bool UpperBound { get; set; }
    public long Nodes { get; set; }
    public long Nps { get; set; }
    public int TimeMs { get; set; }
    public int HashFull { get; set; }
    public int TbHits { get; set; }
    public string[] Pv { get; set; } = Array.Empty<string>();

    /// <summary>Оценка с точки зрения белых (движок отдаёт её от лица стороны, которая ходит).</summary>
    public int? WhiteCp(bool whiteToMove) => ScoreCp is null ? null : whiteToMove ? ScoreCp : -ScoreCp;

    public int? WhiteMate(bool whiteToMove) => ScoreMate is null ? null : whiteToMove ? ScoreMate : -ScoreMate;

    public string ScoreText(bool whiteToMove)
    {
        var mate = WhiteMate(whiteToMove);
        if (mate.HasValue) return (mate.Value > 0 ? "#" : "#-") + Math.Abs(mate.Value);
        var cp = WhiteCp(whiteToMove);
        return cp.HasValue ? FormatPawns(cp.Value) : "—";
    }

    /// <summary>Оценка в пешках. Точка как разделитель — так принято в шахматной нотации.</summary>
    public static string FormatPawns(int centipawns)
    {
        var pawns = centipawns / 100.0;
        return (pawns > 0 ? "+" : string.Empty) + pawns.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

/// <summary>Параметр движка, объявленный командой "option name …".</summary>
public sealed class UciOption
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Default { get; set; } = string.Empty;
    public string? Min { get; set; }
    public string? Max { get; set; }
    public List<string> Vars { get; } = new();
}

/// <summary>Ограничения поиска для команды "go".</summary>
public sealed class SearchLimits
{
    public bool Infinite { get; set; }
    public int? Depth { get; set; }
    public int? MoveTimeMs { get; set; }
    public long? Nodes { get; set; }

    public static SearchLimits AsInfinite() => new() { Infinite = true };
    public static SearchLimits ByDepth(int depth) => new() { Depth = depth };
    public static SearchLimits ByTime(int ms) => new() { MoveTimeMs = ms };

    public string ToGoCommand()
    {
        if (Infinite) return "go infinite";
        var parts = new List<string> { "go" };
        if (Depth.HasValue) parts.Add($"depth {Depth.Value}");
        if (MoveTimeMs.HasValue) parts.Add($"movetime {MoveTimeMs.Value}");
        if (Nodes.HasValue) parts.Add($"nodes {Nodes.Value}");
        if (parts.Count == 1) parts.Add("depth 18");
        return string.Join(' ', parts);
    }
}

/// <summary>Результат завершённого поиска.</summary>
public sealed class SearchResult
{
    public string BestMove { get; set; } = string.Empty;
    public string? Ponder { get; set; }

    /// <summary>Последние строки анализа по номеру MultiPV.</summary>
    public Dictionary<int, EngineInfo> Lines { get; } = new();

    public EngineInfo? Best => Lines.TryGetValue(1, out var info) ? info : Lines.Values.FirstOrDefault();
}

/// <summary>
/// Движок перестал читать команды или отвечать: процесс снят, требуется перезапуск.
/// Наследуется от InvalidOperationException, чтобы уже существующие обработчики
/// «движок недоступен» не пропускали этот случай.
/// </summary>
public sealed class EngineUnresponsiveException : InvalidOperationException
{
    public EngineUnresponsiveException(string message) : base(message) { }
}
