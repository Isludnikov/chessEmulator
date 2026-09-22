using System.Text;

namespace ChessEmulator.Chess;

/// <summary>
/// Позиция на доске: расстановка фигур, очередь хода, рокировки, взятие на проходе, счётчики.
/// Ход не меняет объект: <see cref="MakeMove"/> возвращает новую позицию.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly byte[] _squares;

    public PieceColor SideToMove { get; private set; }
    public CastlingRights Castling { get; private set; }
    public int EnPassant { get; private set; } = Sq.None;
    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; } = 1;

    public Position() => _squares = new byte[64];

    private Position(byte[] squares) => _squares = squares;

    /// <summary>
    /// Собирает позицию из готового массива клеток. Используется <see cref="PositionBuilder"/>:
    /// снаружи сборки позиция остаётся неизменяемой.
    /// </summary>
    internal static Position Build(byte[] squares, PieceColor sideToMove, CastlingRights castling,
        int enPassant, int halfmoveClock, int fullmoveNumber)
    {
        if (squares.Length != 64) throw new ArgumentException("Нужен массив из 64 клеток.", nameof(squares));

        return new Position((byte[])squares.Clone())
        {
            SideToMove = sideToMove,
            Castling = castling,
            EnPassant = enPassant,
            HalfmoveClock = Math.Clamp(halfmoveClock, 0, 100),
            FullmoveNumber = Math.Max(1, fullmoveNumber)
        };
    }

    public Piece this[int square] => new(_squares[square]);

    public Piece At(int file, int rank) => new(_squares[Sq.Of(file, rank)]);

    public PieceColor Opponent => Other(SideToMove);

    public static PieceColor Other(PieceColor c) => c == PieceColor.White ? PieceColor.Black : PieceColor.White;

    public Position Clone() => new((byte[])_squares.Clone())
    {
        SideToMove = SideToMove,
        Castling = Castling,
        EnPassant = EnPassant,
        HalfmoveClock = HalfmoveClock,
        FullmoveNumber = FullmoveNumber
    };

    // ------------------------------------------------------------------ FEN

    public static Position FromFen(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FormatException("Некорректная FEN-строка.");

        var pos = new Position();
        int rank = 7, file = 0;
        foreach (var c in parts[0])
        {
            if (c == '/')
            {
                rank--;
                file = 0;
            }
            else if (char.IsDigit(c))
            {
                file += c - '0';
            }
            else
            {
                if (!Sq.IsValid(file, rank)) throw new FormatException("Некорректная FEN-строка.");
                var piece = Piece.FromFenChar(c);
                // Неизвестная буква — опечатка в чужом файле. Молча превратить её в пустую
                // клетку значит показать пользователю не ту позицию, которую он вставил.
                if (piece.IsEmpty) throw new FormatException($"Неизвестная фигура в FEN: '{c}'.");
                pos._squares[Sq.Of(file, rank)] = piece.Value;
                file++;
            }
        }

        pos.SideToMove = parts[1].StartsWith("b", StringComparison.OrdinalIgnoreCase)
            ? PieceColor.Black
            : PieceColor.White;

        var rights = CastlingRights.None;
        if (parts.Length > 2 && parts[2] != "-")
        {
            foreach (var c in parts[2])
            {
                rights |= c switch
                {
                    'K' => CastlingRights.WhiteKing,
                    'Q' => CastlingRights.WhiteQueen,
                    'k' => CastlingRights.BlackKing,
                    'q' => CastlingRights.BlackQueen,
                    _ => CastlingRights.None
                };
            }
        }
        pos.Castling = rights;

        pos.EnPassant = parts.Length > 3 && parts[3] != "-" ? Sq.Parse(parts[3]) : Sq.None;
        pos.HalfmoveClock = parts.Length > 4 && int.TryParse(parts[4], out var hm) ? hm : 0;
        pos.FullmoveNumber = parts.Length > 5 && int.TryParse(parts[5], out var fm) ? Math.Max(1, fm) : 1;

        // FEN приходит из буфера обмена и из чужих PGN, поэтому может противоречить доске:
        // права на рокировку без короля или ладьи на исходном поле, поле взятия на проходе
        // без пешки, которая туда шагнула. Генератор ходов таким расхождениям не рад — он
        // выдаёт ходы несуществующими фигурами, — так что приводим позицию в порядок теми же
        // правилами, что и редактор позиции.
        var builder = PositionBuilder.FromPosition(pos);
        builder.Normalize();
        return builder.ToPosition();
    }

    public string ToFen()
    {
        var sb = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var piece = At(file, rank);
                if (piece.IsEmpty)
                {
                    empty++;
                }
                else
                {
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    sb.Append(piece.ToFenChar());
                }
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }

        sb.Append(SideToMove == PieceColor.White ? " w " : " b ");

        if (Castling == CastlingRights.None)
        {
            sb.Append('-');
        }
        else
        {
            if (Castling.HasFlag(CastlingRights.WhiteKing)) sb.Append('K');
            if (Castling.HasFlag(CastlingRights.WhiteQueen)) sb.Append('Q');
            if (Castling.HasFlag(CastlingRights.BlackKing)) sb.Append('k');
            if (Castling.HasFlag(CastlingRights.BlackQueen)) sb.Append('q');
        }

        sb.Append(' ').Append(EnPassant == Sq.None ? "-" : Sq.Name(EnPassant));
        sb.Append(' ').Append(HalfmoveClock);
        sb.Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    private string? _repetitionKey;

    /// <summary>
    /// Ключ для поиска троекратного повторения: FEN без счётчиков ходов. Поле взятия
    /// на проходе попадает в ключ, только когда взятие действительно возможно, — так
    /// требует ФИДЕ 9.2.2, иначе ход пешкой через клетку навсегда «расщепляет» позицию.
    /// </summary>
    public string RepetitionKey() => _repetitionKey ??= BuildRepetitionKey();

    private string BuildRepetitionKey()
    {
        var fen = ToFen();
        var lastSpace = fen.LastIndexOf(' ');
        var prevSpace = lastSpace > 0 ? fen.LastIndexOf(' ', lastSpace - 1) : -1;
        var key = prevSpace > 0 ? fen[..prevSpace] : fen;

        if (EnPassant == Sq.None || HasEnPassantCapture()) return key;

        var epSpace = key.LastIndexOf(' ');
        return epSpace > 0 ? key[..epSpace] + " -" : key;
    }

    /// <summary>Может ли сторона на ходу взять на проходе прямо сейчас.</summary>
    private bool HasEnPassantCapture()
    {
        if (EnPassant == Sq.None) return false;

        // Сначала дешёвая проверка соседства: без пешки рядом считать ходы незачем.
        var fromRank = Sq.Rank(EnPassant) + (SideToMove == PieceColor.White ? -1 : 1);
        var pawn = new Piece(SideToMove, PieceType.Pawn).Value;
        var file = Sq.File(EnPassant);
        var adjacent = false;
        for (var df = -1; df <= 1; df += 2)
        {
            var nf = file + df;
            if (Sq.IsValid(nf, fromRank) && _squares[Sq.Of(nf, fromRank)] == pawn) adjacent = true;
        }
        if (!adjacent) return false;

        // Пешка рядом есть, но она может быть связана — тогда взятия всё равно нет.
        foreach (var move in LegalMoves)
        {
            if (IsEnPassantMove(move)) return true;
        }
        return false;
    }

    // -------------------------------------------------------- Атаки и шахи

    private static readonly int[][] KnightDeltas =
    {
        new[] { 1, 2 }, new[] { 2, 1 }, new[] { 2, -1 }, new[] { 1, -2 },
        new[] { -1, -2 }, new[] { -2, -1 }, new[] { -2, 1 }, new[] { -1, 2 }
    };

    private static readonly int[][] KingDeltas =
    {
        new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 }, new[] { -1, 1 },
        new[] { -1, 0 }, new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 }
    };

    private static readonly int[][] BishopDeltas =
    {
        new[] { 1, 1 }, new[] { -1, 1 }, new[] { -1, -1 }, new[] { 1, -1 }
    };

    private static readonly int[][] RookDeltas =
    {
        new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { 0, -1 }
    };

    public int FindKing(PieceColor color)
    {
        var target = new Piece(color, PieceType.King).Value;
        for (var i = 0; i < 64; i++)
        {
            if (_squares[i] == target) return i;
        }
        return Sq.None;
    }

    /// <summary>Атакована ли клетка фигурами цвета <paramref name="by"/>.</summary>
    public bool IsAttacked(int square, PieceColor by)
    {
        int f = Sq.File(square), r = Sq.Rank(square);

        var pawnRank = by == PieceColor.White ? r - 1 : r + 1;
        if (Sq.IsValid(f - 1, pawnRank) && At(f - 1, pawnRank).Is(by, PieceType.Pawn)) return true;
        if (Sq.IsValid(f + 1, pawnRank) && At(f + 1, pawnRank).Is(by, PieceType.Pawn)) return true;

        foreach (var d in KnightDeltas)
        {
            if (Sq.IsValid(f + d[0], r + d[1]) && At(f + d[0], r + d[1]).Is(by, PieceType.Knight)) return true;
        }

        foreach (var d in KingDeltas)
        {
            if (Sq.IsValid(f + d[0], r + d[1]) && At(f + d[0], r + d[1]).Is(by, PieceType.King)) return true;
        }

        return SlidingAttack(f, r, BishopDeltas, by, PieceType.Bishop)
            || SlidingAttack(f, r, RookDeltas, by, PieceType.Rook);
    }

    private bool SlidingAttack(int f, int r, int[][] deltas, PieceColor by, PieceType slider)
    {
        foreach (var d in deltas)
        {
            int nf = f + d[0], nr = r + d[1];
            while (Sq.IsValid(nf, nr))
            {
                var piece = At(nf, nr);
                if (!piece.IsEmpty)
                {
                    if (piece.Color == by && (piece.Type == slider || piece.Type == PieceType.Queen)) return true;
                    break;
                }
                nf += d[0];
                nr += d[1];
            }
        }
        return false;
    }

    public bool IsInCheck(PieceColor color)
    {
        var king = FindKing(color);
        return king != Sq.None && IsAttacked(king, Other(color));
    }

    public bool IsInCheck() => IsInCheck(SideToMove);

    // --------------------------------------------------------- Генерация ходов

    private List<Move>? _legalCache;

    public IReadOnlyList<Move> LegalMoves => _legalCache ??= GenerateLegalMoves();

    public bool IsLegal(Move move)
    {
        foreach (var m in LegalMoves)
        {
            if (m == move) return true;
        }
        return false;
    }

    /// <summary>Ищет легальный ход по клеткам начала и конца.</summary>
    public bool TryFindMove(int from, int to, PieceType promotion, out Move move)
    {
        foreach (var m in LegalMoves)
        {
            if (m.From != from || m.To != to) continue;
            if (promotion != PieceType.None && m.Promotion != promotion) continue;
            move = m;
            return true;
        }
        move = Move.None;
        return false;
    }

    public bool HasMoveFrom(int square)
    {
        foreach (var m in LegalMoves)
        {
            if (m.From == square) return true;
        }
        return false;
    }

    public bool IsPromotionMove(int from, int to)
    {
        var piece = this[from];
        if (piece.IsEmpty || piece.Type != PieceType.Pawn) return false;
        var targetRank = Sq.Rank(to);
        return piece.Color == PieceColor.White ? targetRank == 7 : targetRank == 0;
    }

    private List<Move> GenerateLegalMoves()
    {
        var pseudo = GeneratePseudoLegalMoves();
        var result = new List<Move>(pseudo.Count);
        foreach (var move in pseudo)
        {
            var next = ApplyMoveRaw(move);
            if (!next.IsInCheck(SideToMove)) result.Add(move);
        }
        return result;
    }

    private List<Move> GeneratePseudoLegalMoves()
    {
        var moves = new List<Move>(48);
        var me = SideToMove;

        for (var sq = 0; sq < 64; sq++)
        {
            var piece = new Piece(_squares[sq]);
            if (piece.IsEmpty || piece.Color != me) continue;

            int f = Sq.File(sq), r = Sq.Rank(sq);
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    GeneratePawnMoves(moves, sq, f, r, me);
                    break;
                case PieceType.Knight:
                    GenerateStepMoves(moves, sq, f, r, me, KnightDeltas);
                    break;
                case PieceType.Bishop:
                    GenerateSlidingMoves(moves, sq, f, r, me, BishopDeltas);
                    break;
                case PieceType.Rook:
                    GenerateSlidingMoves(moves, sq, f, r, me, RookDeltas);
                    break;
                case PieceType.Queen:
                    GenerateSlidingMoves(moves, sq, f, r, me, BishopDeltas);
                    GenerateSlidingMoves(moves, sq, f, r, me, RookDeltas);
                    break;
                case PieceType.King:
                    GenerateStepMoves(moves, sq, f, r, me, KingDeltas);
                    GenerateCastlingMoves(moves, me);
                    break;
            }
        }

        return moves;
    }

    private void GeneratePawnMoves(List<Move> moves, int sq, int f, int r, PieceColor me)
    {
        var dir = me == PieceColor.White ? 1 : -1;
        var startRank = me == PieceColor.White ? 1 : 6;
        var promoRank = me == PieceColor.White ? 7 : 0;
        var oneR = r + dir;

        if (Sq.IsValid(f, oneR) && At(f, oneR).IsEmpty)
        {
            AddPawnMove(moves, sq, Sq.Of(f, oneR), oneR == promoRank);
            var twoR = r + 2 * dir;
            if (r == startRank && Sq.IsValid(f, twoR) && At(f, twoR).IsEmpty)
                moves.Add(new Move(sq, Sq.Of(f, twoR)));
        }

        for (var df = -1; df <= 1; df += 2)
        {
            var nf = f + df;
            if (!Sq.IsValid(nf, oneR)) continue;
            var target = Sq.Of(nf, oneR);
            var occupant = new Piece(_squares[target]);
            if (!occupant.IsEmpty && occupant.Color != me)
                AddPawnMove(moves, sq, target, oneR == promoRank);
            else if (occupant.IsEmpty && target == EnPassant)
                moves.Add(new Move(sq, target));
        }
    }

    private static void AddPawnMove(List<Move> moves, int from, int to, bool promotion)
    {
        if (promotion)
        {
            moves.Add(new Move(from, to, PieceType.Queen));
            moves.Add(new Move(from, to, PieceType.Rook));
            moves.Add(new Move(from, to, PieceType.Bishop));
            moves.Add(new Move(from, to, PieceType.Knight));
        }
        else
        {
            moves.Add(new Move(from, to));
        }
    }

    private void GenerateStepMoves(List<Move> moves, int sq, int f, int r, PieceColor me, int[][] deltas)
    {
        foreach (var d in deltas)
        {
            int nf = f + d[0], nr = r + d[1];
            if (!Sq.IsValid(nf, nr)) continue;
            var occupant = At(nf, nr);
            if (occupant.IsEmpty || occupant.Color != me)
                moves.Add(new Move(sq, Sq.Of(nf, nr)));
        }
    }

    private void GenerateSlidingMoves(List<Move> moves, int sq, int f, int r, PieceColor me, int[][] deltas)
    {
        foreach (var d in deltas)
        {
            int nf = f + d[0], nr = r + d[1];
            while (Sq.IsValid(nf, nr))
            {
                var occupant = At(nf, nr);
                if (occupant.IsEmpty)
                {
                    moves.Add(new Move(sq, Sq.Of(nf, nr)));
                }
                else
                {
                    if (occupant.Color != me) moves.Add(new Move(sq, Sq.Of(nf, nr)));
                    break;
                }
                nf += d[0];
                nr += d[1];
            }
        }
    }

    private void GenerateCastlingMoves(List<Move> moves, PieceColor me)
    {
        var enemy = Other(me);
        if (IsInCheck(me)) return;

        // Права на рокировку могут пережить короля на исходном поле, если позицию собрали
        // через Build в обход FromFen. Без этой проверки рокировка «ходит» пустой клеткой.
        var homeSquare = me == PieceColor.White ? 4 : 60;
        if (!new Piece(_squares[homeSquare]).Is(me, PieceType.King)) return;

        if (me == PieceColor.White)
        {
            if (Castling.HasFlag(CastlingRights.WhiteKing) &&
                _squares[5] == 0 && _squares[6] == 0 &&
                new Piece(_squares[7]).Is(PieceColor.White, PieceType.Rook) &&
                !IsAttacked(5, enemy) && !IsAttacked(6, enemy))
                moves.Add(new Move(4, 6));

            if (Castling.HasFlag(CastlingRights.WhiteQueen) &&
                _squares[1] == 0 && _squares[2] == 0 && _squares[3] == 0 &&
                new Piece(_squares[0]).Is(PieceColor.White, PieceType.Rook) &&
                !IsAttacked(3, enemy) && !IsAttacked(2, enemy))
                moves.Add(new Move(4, 2));
        }
        else
        {
            if (Castling.HasFlag(CastlingRights.BlackKing) &&
                _squares[61] == 0 && _squares[62] == 0 &&
                new Piece(_squares[63]).Is(PieceColor.Black, PieceType.Rook) &&
                !IsAttacked(61, enemy) && !IsAttacked(62, enemy))
                moves.Add(new Move(60, 62));

            if (Castling.HasFlag(CastlingRights.BlackQueen) &&
                _squares[57] == 0 && _squares[58] == 0 && _squares[59] == 0 &&
                new Piece(_squares[56]).Is(PieceColor.Black, PieceType.Rook) &&
                !IsAttacked(59, enemy) && !IsAttacked(58, enemy))
                moves.Add(new Move(60, 58));
        }
    }

    // ------------------------------------------------------- Выполнение хода

    public bool IsCastlingMove(Move move) =>
        this[move.From].Type == PieceType.King && Math.Abs(Sq.File(move.To) - Sq.File(move.From)) == 2;

    public bool IsEnPassantMove(Move move) =>
        this[move.From].Type == PieceType.Pawn && move.To == EnPassant && _squares[move.To] == 0;

    public bool IsCapture(Move move) => _squares[move.To] != 0 || IsEnPassantMove(move);

    /// <summary>Применяет ход и возвращает новую позицию (без проверки легальности).</summary>
    public Position MakeMove(Move move) => ApplyMoveRaw(move);

    private Position ApplyMoveRaw(Move move)
    {
        var next = new Position((byte[])_squares.Clone())
        {
            SideToMove = Other(SideToMove),
            Castling = Castling,
            EnPassant = Sq.None,
            HalfmoveClock = HalfmoveClock + 1,
            FullmoveNumber = FullmoveNumber + (SideToMove == PieceColor.Black ? 1 : 0)
        };

        var moving = new Piece(_squares[move.From]);
        var isCapture = _squares[move.To] != 0;

        if (moving.Type == PieceType.Pawn && move.To == EnPassant && _squares[move.To] == 0)
        {
            var capturedSquare = SideToMove == PieceColor.White ? move.To - 8 : move.To + 8;
            next._squares[capturedSquare] = 0;
            isCapture = true;
        }

        next._squares[move.To] = moving.Value;
        next._squares[move.From] = 0;

        if (moving.Type == PieceType.Pawn && move.Promotion != PieceType.None)
            next._squares[move.To] = new Piece(moving.Color, move.Promotion).Value;

        if (moving.Type == PieceType.King && Math.Abs(Sq.File(move.To) - Sq.File(move.From)) == 2)
        {
            switch (move.To)
            {
                case 6: next._squares[5] = next._squares[7]; next._squares[7] = 0; break;
                case 2: next._squares[3] = next._squares[0]; next._squares[0] = 0; break;
                case 62: next._squares[61] = next._squares[63]; next._squares[63] = 0; break;
                case 58: next._squares[59] = next._squares[56]; next._squares[56] = 0; break;
            }
        }

        if (moving.Type == PieceType.Pawn && Math.Abs(Sq.Rank(move.To) - Sq.Rank(move.From)) == 2)
            next.EnPassant = (move.From + move.To) / 2;

        var rights = next.Castling;
        if (moving.Type == PieceType.King)
        {
            rights &= moving.Color == PieceColor.White
                ? ~(CastlingRights.WhiteKing | CastlingRights.WhiteQueen)
                : ~(CastlingRights.BlackKing | CastlingRights.BlackQueen);
        }
        rights = RemoveRightsForSquare(rights, move.From);
        rights = RemoveRightsForSquare(rights, move.To);
        next.Castling = rights;

        if (moving.Type == PieceType.Pawn || isCapture) next.HalfmoveClock = 0;

        return next;
    }

    private static CastlingRights RemoveRightsForSquare(CastlingRights rights, int square) => square switch
    {
        0 => rights & ~CastlingRights.WhiteQueen,
        7 => rights & ~CastlingRights.WhiteKing,
        56 => rights & ~CastlingRights.BlackQueen,
        63 => rights & ~CastlingRights.BlackKing,
        _ => rights
    };

    // ------------------------------------------------------ Состояние партии

    public bool IsCheckmate => LegalMoves.Count == 0 && IsInCheck();
    public bool IsStalemate => LegalMoves.Count == 0 && !IsInCheck();

    public bool HasInsufficientMaterial()
    {
        int knights = 0, bishops = 0;
        var bishopSquareColors = new HashSet<int>();

        for (var i = 0; i < 64; i++)
        {
            var p = new Piece(_squares[i]);
            if (p.IsEmpty) continue;
            switch (p.Type)
            {
                case PieceType.King:
                    break;
                case PieceType.Knight:
                    knights++;
                    break;
                case PieceType.Bishop:
                    bishops++;
                    bishopSquareColors.Add((Sq.File(i) + Sq.Rank(i)) & 1);
                    break;
                default:
                    return false;
            }
        }

        if (knights == 0 && bishops == 0) return true;
        if (knights + bishops == 1) return true;
        if (knights == 0 && bishopSquareColors.Count == 1) return true;
        return false;
    }

    /// <summary>Приблизительный материальный баланс в пешках (плюс — перевес белых).</summary>
    public int MaterialBalance()
    {
        var score = 0;
        for (var i = 0; i < 64; i++)
        {
            var p = new Piece(_squares[i]);
            if (p.IsEmpty) continue;
            var value = p.Type switch
            {
                PieceType.Pawn => 1,
                PieceType.Knight => 3,
                PieceType.Bishop => 3,
                PieceType.Rook => 5,
                PieceType.Queen => 9,
                _ => 0
            };
            score += p.Color == PieceColor.White ? value : -value;
        }
        return score;
    }

    // ------------------------------------------------------------------ SAN

    /// <summary>Запись хода в шахматной нотации (SAN): Nf3, exd5, O-O, e8=Q#.</summary>
    public string ToSan(Move move)
    {
        var moving = this[move.From];
        if (moving.IsEmpty) return move.ToUci();

        var sb = new StringBuilder();

        if (moving.Type == PieceType.King && Math.Abs(Sq.File(move.To) - Sq.File(move.From)) == 2)
        {
            sb.Append(Sq.File(move.To) == 6 ? "O-O" : "O-O-O");
        }
        else if (moving.Type == PieceType.Pawn)
        {
            if (IsCapture(move)) sb.Append((char)('a' + Sq.File(move.From))).Append('x');
            sb.Append(Sq.Name(move.To));
            if (move.Promotion != PieceType.None) sb.Append('=').Append(PieceLetter(move.Promotion));
        }
        else
        {
            sb.Append(PieceLetter(moving.Type));
            sb.Append(Disambiguation(move, moving));
            if (IsCapture(move)) sb.Append('x');
            sb.Append(Sq.Name(move.To));
        }

        var after = MakeMove(move);
        if (after.IsInCheck()) sb.Append(after.LegalMoves.Count == 0 ? '#' : '+');

        return sb.ToString();
    }

    private string Disambiguation(Move move, Piece moving)
    {
        var rivals = new List<int>();
        foreach (var m in LegalMoves)
        {
            if (m.To != move.To || m.From == move.From) continue;
            var other = this[m.From];
            if (other.Type == moving.Type && other.Color == moving.Color) rivals.Add(m.From);
        }

        if (rivals.Count == 0) return string.Empty;

        var sameFile = rivals.Any(s => Sq.File(s) == Sq.File(move.From));
        var sameRank = rivals.Any(s => Sq.Rank(s) == Sq.Rank(move.From));

        if (!sameFile) return ((char)('a' + Sq.File(move.From))).ToString();
        if (!sameRank) return ((char)('1' + Sq.Rank(move.From))).ToString();
        return Sq.Name(move.From);
    }

    public static char PieceLetter(PieceType type) => type switch
    {
        PieceType.Knight => 'N',
        PieceType.Bishop => 'B',
        PieceType.Rook => 'R',
        PieceType.Queen => 'Q',
        PieceType.King => 'K',
        _ => 'P'
    };

    public static PieceType LetterToPiece(char c) => char.ToUpperInvariant(c) switch
    {
        'N' => PieceType.Knight,
        'B' => PieceType.Bishop,
        'R' => PieceType.Rook,
        'Q' => PieceType.Queen,
        'K' => PieceType.King,
        'P' => PieceType.Pawn,
        _ => PieceType.None
    };

    /// <summary>Разбирает ход в нотации SAN относительно текущей позиции.</summary>
    public bool TryParseSan(string san, out Move move)
    {
        move = Move.None;
        if (string.IsNullOrWhiteSpace(san)) return false;

        var s = san.Trim().Replace("e.p.", string.Empty).Trim().TrimEnd('!', '?', '+', '#');
        if (s.Length == 0) return false;

        var normalized = s.Replace('0', 'O');
        if (normalized == "O-O" || normalized == "O-O-O")
        {
            var kingFrom = SideToMove == PieceColor.White ? 4 : 60;
            var kingTo = normalized == "O-O" ? kingFrom + 2 : kingFrom - 2;
            return TryFindMove(kingFrom, kingTo, PieceType.None, out move);
        }

        var promotion = PieceType.None;
        var eq = s.IndexOf('=');
        if (eq >= 0 && eq + 1 < s.Length)
        {
            promotion = LetterToPiece(s[eq + 1]);
            s = s[..eq];
        }
        else if (s.Length >= 3 && "NBRQ".IndexOf(s[^1]) >= 0 && char.IsDigit(s[^2]))
        {
            promotion = LetterToPiece(s[^1]);
            s = s[..^1];
        }

        var pieceType = PieceType.Pawn;
        var idx = 0;
        if ("NBRQK".IndexOf(s[0]) >= 0)
        {
            pieceType = LetterToPiece(s[0]);
            idx = 1;
        }

        var body = s[idx..].Replace("x", string.Empty).Replace("-", string.Empty);
        if (body.Length < 2) return false;

        var to = Sq.Parse(body[^2..]);
        if (to == Sq.None) return false;

        var hint = body[..^2];
        int hintFile = -1, hintRank = -1;
        foreach (var c in hint)
        {
            if (c >= 'a' && c <= 'h') hintFile = c - 'a';
            else if (c >= '1' && c <= '8') hintRank = c - '1';
        }

        foreach (var m in LegalMoves)
        {
            if (m.To != to || m.Promotion != promotion) continue;
            var piece = this[m.From];
            if (piece.Type != pieceType) continue;
            if (hintFile >= 0 && Sq.File(m.From) != hintFile) continue;
            if (hintRank >= 0 && Sq.Rank(m.From) != hintRank) continue;
            move = m;
            return true;
        }

        return false;
    }

    public override string ToString() => ToFen();
}
