using System.Diagnostics;
using System.Text;

namespace ChessEmulator.Engine;

/// <summary>
/// Обёртка над шахматным движком с интерфейсом UCI (Stockfish и совместимые).
/// Все события поднимаются в потоке, который создал объект (через SynchronizationContext).
///
/// Главное правило протокола: настоящий Stockfish читает команды тем же потоком, который ждёт
/// окончания поиска. Любая команда, кроме stop, quit и ponderhit, отправленная во время поиска
/// (ucinewgame, setoption, position, go, isready), вешает движок намертво: строку он прочитает,
/// но дальше замрёт в ожидании конца поиска и уже не увидит ни stop, ни чего-либо ещё.
/// Поэтому канал команд здесь один и захватывается через <see cref="AcquireAsync"/>: перед
/// каждой операцией текущий поиск гасится и дожидается настоящего bestmove.
/// </summary>
public sealed class UciEngine : IDisposable
{
    private static readonly Encoding NoBomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private SynchronizationContext? _sync;

    /// <summary>Очередь желающих поговорить с движком: гасить чужой поиск можно только по одному.</summary>
    private readonly SemaphoreSlim _turnstile = new(1, 1);

    /// <summary>Владение каналом команд. Поиск держит его от go до bestmove.</summary>
    private readonly SemaphoreSlim _owner = new(1, 1);

    private readonly object _gate = new();

    private Process? _process;
    private TaskCompletionSource<bool>? _uciOkTcs;
    private TaskCompletionSource<bool>? _readyTcs;
    private SearchState? _search;
    private bool _disposed;

    public UciEngine() => _sync = SynchronizationContext.Current;

    public string ExecutablePath { get; private set; } = string.Empty;
    public string Name { get; private set; } = "—";
    public string Author { get; private set; } = string.Empty;
    public List<UciOption> Options { get; } = new();
    public bool IsRunning => _process is { HasExited: false };
    public bool IsSearching => _search is { Completed: false };

    /// <summary>Движок перестал отвечать; процесс снят, нужен перезапуск.</summary>
    public bool IsWedged { get; private set; }

    // --------------------------------------------------- Сторожевые таймеры
    // Вынесены в свойства: тесты уменьшают их до сотен миллисекунд.

    /// <summary>Сколько ждём bestmove после команды stop.</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Сколько движок может молчать во время поиска, прежде чем счесть его зависшим.</summary>
    public TimeSpan SilenceTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Запас сверх movetime, после которого поиск принудительно останавливается.</summary>
    public TimeSpan MoveTimeMargin { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Сколько ждём readyok (выделение большого хеша занимает время).</summary>
    public TimeSpan ReadyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Сколько ждём uciok при запуске.</summary>
    public TimeSpan UciOkTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public event EventHandler<EngineInfo>? InfoReceived;
    public event EventHandler<string>? LogReceived;
    public event EventHandler? Exited;

    /// <summary>Движок не ответил в отведённое время. Процесс уже снят — нужен перезапуск.</summary>
    public event EventHandler<string>? Unresponsive;

    private sealed class SearchState
    {
        private long _lastOutput = Environment.TickCount64;

        public TaskCompletionSource<SearchResult> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SearchResult Result { get; } = new();
        public bool Completed => Tcs.Task.IsCompleted;

        /// <summary>Команда stop уже отправлена сторожем.</summary>
        public volatile bool StopRequested;

        /// <summary>Момент (Environment.TickCount64), к которому движок обязан ответить. null — без срока.</summary>
        public long? DeadlineTicks { get; set; }

        public long LastOutputTicks => Interlocked.Read(ref _lastOutput);
        public void MarkOutput() => Interlocked.Exchange(ref _lastOutput, Environment.TickCount64);
    }

    /// <summary>Право писать в движок. Освобождается через using.</summary>
    private readonly struct Lease : IDisposable
    {
        private readonly UciEngine _engine;
        public Lease(UciEngine engine) => _engine = engine;

        public void Dispose()
        {
            // Движок могли закрыть, пока операция ждала ответа, — тогда семафора уже нет.
            try { _engine._owner.Release(); } catch (ObjectDisposedException) { }
        }
    }

    // -------------------------------------------------------------- Запуск

    public async Task StartAsync(string executablePath, CancellationToken ct = default)
    {
        Stop();

        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Файл движка не найден.", executablePath);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Без BOM: иначе первая команда уходит движку с меткой порядка байтов и он её не распознаёт.
            StandardOutputEncoding = NoBomUtf8,
            StandardInputEncoding = NoBomUtf8
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) HandleLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Post(() => LogReceived?.Invoke(this, "! " + e.Data)); };
        process.Exited += (_, _) =>
        {
            FailPendingOperations(new InvalidOperationException("Процесс движка завершился."));
            Post(() => Exited?.Invoke(this, EventArgs.Empty));
        };

        if (!process.Start()) throw new InvalidOperationException("Не удалось запустить процесс движка.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _process = process;
        ExecutablePath = executablePath;
        Name = Path.GetFileNameWithoutExtension(executablePath);
        Options.Clear();
        lock (_gate) _search = null;
        IsWedged = false;

        _uciOkTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Send("uci");
        if (!await WaitOrTimeoutAsync(_uciOkTcs.Task, UciOkTimeout, ct).ConfigureAwait(false))
            throw new TimeoutException("движок не ответил на команду uci");
        await _uciOkTcs.Task.ConfigureAwait(false);

        await IsReadyAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Ждёт readyok. Как и остальные команды, выполняется только на свободном движке.</summary>
    public async Task IsReadyAsync(CancellationToken ct = default)
    {
        ThrowIfWedged();
        if (!IsRunning) return;
        using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        await ReadyHandshakeAsync(ct).ConfigureAwait(false);
    }

    public void Stop() => Stop(graceful: true);

    /// <summary>Останавливает движок. graceful: false — сразу Kill, без ожидания (движок завис).</summary>
    public void Stop(bool graceful)
    {
        var process = _process;
        _process = null;
        if (process == null) return;

        FailPendingOperations(new OperationCanceledException("Движок остановлен."));

        try
        {
            if (!process.HasExited)
            {
                if (graceful)
                {
                    process.StandardInput.WriteLine("stop");
                    process.StandardInput.WriteLine("quit");
                    process.StandardInput.Flush();
                    if (!process.WaitForExit(1500)) process.Kill(entireProcessTree: true);
                }
                else
                {
                    // Зависший движок не читает ввод: просить его уйти бессмысленно.
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { /* уже завершён */ }
        }
        finally
        {
            process.Dispose();
        }
    }

    private void FailPendingOperations(Exception ex)
    {
        _uciOkTcs?.TrySetException(ex);
        _readyTcs?.TrySetException(ex);
        lock (_gate)
        {
            _search?.Tcs.TrySetException(ex);
            _search = null;
        }
    }

    /// <summary>
    /// Движок не отвечает. Восстановить его изнутри нельзя: единственный байт, который мог бы его
    /// расшевелить, — это stop, а сторож срабатывает как раз потому, что stop уже проигнорирован.
    /// Поэтому снимаем процесс, освобождаем всех ожидающих и просим интерфейс перезапустить движок.
    /// </summary>
    private void MarkWedged(string reason)
    {
        SearchState? state;
        lock (_gate)
        {
            if (IsWedged) return;
            IsWedged = true;
            state = _search;
            _search = null;
        }

        var error = new EngineUnresponsiveException(reason);
        state?.Tcs.TrySetException(error);
        _readyTcs?.TrySetException(error);

        Post(() => LogReceived?.Invoke(this, "! " + reason));
        Stop(graceful: false);
        Post(() => Unresponsive?.Invoke(this, reason));
    }

    private void ThrowIfWedged()
    {
        if (IsWedged) throw new EngineUnresponsiveException("движок не отвечает, требуется перезапуск");
    }

    // ------------------------------------------------------------- Команды
    // Внутренние: снаружи (из интерфейса) писать в движок можно только через операции с арендой,
    // иначе команда рискует уйти во время поиска и повесить движок.

    internal void Send(string command)
    {
        var process = _process;
        if (process == null || process.HasExited) return;
        // В зависший движок пишем только stop/quit: остальное всё равно никто не прочитает.
        if (IsWedged && command != "stop" && command != "quit") return;
        try
        {
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
            Post(() => LogReceived?.Invoke(this, "> " + command));
        }
        catch (IOException)
        {
            // Движок закрылся между проверкой и записью.
        }
    }

    internal void SetOption(string name, string value) =>
        Send(string.IsNullOrEmpty(value) ? $"setoption name {name}" : $"setoption name {name} value {value}");

    internal void NewGame() => Send("ucinewgame");

    internal void SetPosition(string fen, IEnumerable<string>? moves = null)
    {
        var sb = new StringBuilder();
        sb.Append(fen == Chess.Position.StartFen ? "position startpos" : $"position fen {fen}");
        var list = moves?.ToList();
        if (list is { Count: > 0 }) sb.Append(" moves ").Append(string.Join(' ', list));
        Send(sb.ToString());
    }

    /// <summary>Новая партия: ucinewgame с подтверждением readyok — только на свободном движке.</summary>
    public async Task NewGameAsync(CancellationToken ct = default)
    {
        ThrowIfWedged();
        if (!IsRunning) return;
        using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        Send("ucinewgame");
        await ReadyHandshakeAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Выставляет параметры движка (неподдерживаемые пропускаются) — только на свободном движке.</summary>
    public async Task ApplyOptionsAsync(IEnumerable<KeyValuePair<string, string>> options,
        CancellationToken ct = default)
    {
        ThrowIfWedged();
        if (!IsRunning) return;
        var list = options.ToList();
        if (list.Count == 0) return;

        using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        foreach (var option in list)
        {
            if (SupportsOption(option.Key)) SetOption(option.Key, option.Value);
        }
        await ReadyHandshakeAsync(ct).ConfigureAwait(false);
    }

    public bool SupportsOption(string name) =>
        Options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    public UciOption? FindOption(string name) =>
        Options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    // --------------------------------------------------------------- Поиск

    /// <summary>Запускает поиск и ждёт bestmove. Одновременно выполняется только один поиск.</summary>
    public async Task<SearchResult> GoAsync(string fen, IEnumerable<string>? moves, SearchLimits limits,
        CancellationToken ct = default)
    {
        ThrowIfWedged();
        if (!IsRunning) throw new InvalidOperationException("Движок не запущен.");

        using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var state = new SearchState();
        lock (_gate) _search = state;

        using var registration = ct.Register(() =>
        {
            state.StopRequested = true;
            Send("stop");
        });

        SetPosition(fen, moves);
        Send(limits.ToGoCommand());
        if (limits.MoveTimeMs is { } movetime)
            state.DeadlineTicks = Environment.TickCount64 + movetime + (long)MoveTimeMargin.TotalMilliseconds;

        return await WatchSearchAsync(state).ConfigureAwait(false);
    }

    /// <summary>
    /// Ждёт bestmove под присмотром двух сторожей: движок не должен молчать дольше
    /// <see cref="SilenceTimeout"/>, а поиск с movetime — выходить за отведённое время.
    /// Бесконечный анализ и поиск по глубине ограничены только молчанием: они вправе идти долго.
    /// </summary>
    private async Task<SearchResult> WatchSearchAsync(SearchState state)
    {
        while (true)
        {
            var silenceAt = state.LastOutputTicks + (long)SilenceTimeout.TotalMilliseconds;
            var next = state.DeadlineTicks is { } deadline && deadline < silenceAt ? deadline : silenceAt;
            var wait = Math.Max(0, next - Environment.TickCount64);

            if (await WaitOrTimeoutAsync(state.Tcs.Task, TimeSpan.FromMilliseconds(wait)).ConfigureAwait(false))
                return await state.Tcs.Task.ConfigureAwait(false);

            var now = Environment.TickCount64;
            if (now - state.LastOutputTicks >= (long)SilenceTimeout.TotalMilliseconds)
            {
                MarkWedged($"движок молчит {SilenceTimeout.TotalSeconds:0} с во время поиска");
                return await state.Tcs.Task.ConfigureAwait(false);  // бросит EngineUnresponsiveException
            }

            if (state.DeadlineTicks is { } hard && now >= hard)
            {
                if (state.StopRequested)
                {
                    MarkWedged("движок не остановился после истечения времени на ход");
                    return await state.Tcs.Task.ConfigureAwait(false);
                }

                // Движок жив (строки идут), но просрочил movetime — просим остановиться.
                state.StopRequested = true;
                Send("stop");
                state.DeadlineTicks = now + (long)StopTimeout.TotalMilliseconds;
            }
        }
    }

    /// <summary>Останавливает текущий поиск и ждёт, пока движок пришлёт bestmove.</summary>
    public async Task StopSearchAsync()
    {
        SearchState? state;
        lock (_gate) state = _search;
        if (state == null) return;
        if (state.Completed)
        {
            lock (_gate) { if (ReferenceEquals(_search, state)) _search = null; }
            return;
        }

        state.StopRequested = true;
        Send("stop");
        if (await WaitOrTimeoutAsync(state.Tcs.Task, StopTimeout).ConfigureAwait(false))
        {
            try { await state.Tcs.Task.ConfigureAwait(false); } catch { /* поиск прерван */ }
            return;
        }

        // Движок не ответил на stop — дальше с ним разговаривать нельзя.
        MarkWedged($"движок не ответил на stop за {StopTimeout.TotalSeconds:0} с");
    }

    // -------------------------------------------------------- Аренда канала

    /// <summary>
    /// Захватывает канал команд: сначала гасит текущий поиск и дожидается bestmove, и только
    /// потом берёт владение. Турникет гарантирует, что между нашим stop и захватом канала никто
    /// не успеет запустить новый поиск.
    /// </summary>
    private async Task<Lease> AcquireAsync(CancellationToken ct)
    {
        ThrowIfWedged();
        if (!IsRunning) throw new InvalidOperationException("Движок не запущен.");

        await _turnstile.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Бесконечный анализ держит канал, пока не придёт bestmove, поэтому сначала stop.
            await StopSearchAsync().ConfigureAwait(false);
            await _owner.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _turnstile.Release();
        }

        if (IsWedged || !IsRunning)
        {
            _owner.Release();
            ThrowIfWedged();
            throw new InvalidOperationException("Движок не запущен.");
        }

        return new Lease(this);
    }

    private async Task ReadyHandshakeAsync(CancellationToken ct)
    {
        if (!IsRunning) return;
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Send("isready");
        if (!await WaitOrTimeoutAsync(_readyTcs.Task, ReadyTimeout, ct).ConfigureAwait(false))
        {
            MarkWedged($"движок не ответил на isready за {ReadyTimeout.TotalSeconds:0} с");
            ThrowIfWedged();
        }
        await _readyTcs.Task.ConfigureAwait(false);
    }

    /// <summary>true — задача успела, false — истёк срок. Отмена по токену бросает исключение.</summary>
    private static async Task<bool> WaitOrTimeoutAsync(Task task, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var finished = await Task.WhenAny(task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        return finished == task;
    }

    // ------------------------------------------------------- Разбор вывода

    private void HandleLine(string line)
    {
        Post(() => LogReceived?.Invoke(this, "< " + line));

        SearchState? current;
        lock (_gate) current = _search;
        current?.MarkOutput();

        if (line.StartsWith("info ", StringComparison.Ordinal))
        {
            if (line.StartsWith("info string", StringComparison.Ordinal)) return;
            var info = ParseInfo(line);
            if (info == null) return;

            if (current != null && !current.Completed && info.Pv.Length > 0) current.Result.Lines[info.MultiPv] = info;

            Post(() => InfoReceived?.Invoke(this, info));
            return;
        }

        if (line.StartsWith("bestmove", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            SearchState? state;
            lock (_gate)
            {
                state = _search;
                _search = null;
            }
            // Запоздавший bestmove от уже забытого поиска просто игнорируем.
            if (state == null) return;
            state.Result.BestMove = parts.Length > 1 ? parts[1] : string.Empty;
            var ponderIdx = Array.IndexOf(parts, "ponder");
            if (ponderIdx > 0 && ponderIdx + 1 < parts.Length) state.Result.Ponder = parts[ponderIdx + 1];
            state.Tcs.TrySetResult(state.Result);
            return;
        }

        if (line.StartsWith("id name ", StringComparison.Ordinal))
        {
            Name = line[8..].Trim();
            return;
        }

        if (line.StartsWith("id author ", StringComparison.Ordinal))
        {
            Author = line[10..].Trim();
            return;
        }

        if (line.StartsWith("option name ", StringComparison.Ordinal))
        {
            var option = ParseOption(line);
            if (option != null) Options.Add(option);
            return;
        }

        if (line.StartsWith("uciok", StringComparison.Ordinal)) _uciOkTcs?.TrySetResult(true);
        else if (line.StartsWith("readyok", StringComparison.Ordinal)) _readyTcs?.TrySetResult(true);
    }

    private static EngineInfo? ParseInfo(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var info = new EngineInfo();
        var any = false;

        for (var i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "depth" when i + 1 < tokens.Length:
                    info.Depth = ParseInt(tokens[++i]);
                    any = true;
                    break;
                case "seldepth" when i + 1 < tokens.Length:
                    info.SelDepth = ParseInt(tokens[++i]);
                    break;
                case "multipv" when i + 1 < tokens.Length:
                    info.MultiPv = Math.Max(1, ParseInt(tokens[++i]));
                    break;
                case "nodes" when i + 1 < tokens.Length:
                    info.Nodes = ParseLong(tokens[++i]);
                    break;
                case "nps" when i + 1 < tokens.Length:
                    info.Nps = ParseLong(tokens[++i]);
                    break;
                case "time" when i + 1 < tokens.Length:
                    info.TimeMs = ParseInt(tokens[++i]);
                    break;
                case "hashfull" when i + 1 < tokens.Length:
                    info.HashFull = ParseInt(tokens[++i]);
                    break;
                case "tbhits" when i + 1 < tokens.Length:
                    info.TbHits = ParseInt(tokens[++i]);
                    break;
                case "score":
                    while (i + 1 < tokens.Length)
                    {
                        var kind = tokens[i + 1];
                        if (kind == "cp" && i + 2 < tokens.Length)
                        {
                            info.ScoreCp = ParseInt(tokens[i + 2]);
                            i += 2;
                            any = true;
                        }
                        else if (kind == "mate" && i + 2 < tokens.Length)
                        {
                            info.ScoreMate = ParseInt(tokens[i + 2]);
                            i += 2;
                            any = true;
                        }
                        else if (kind == "lowerbound")
                        {
                            info.LowerBound = true;
                            i += 1;
                        }
                        else if (kind == "upperbound")
                        {
                            info.UpperBound = true;
                            i += 1;
                        }
                        else
                        {
                            break;
                        }
                    }
                    break;
                case "pv":
                    info.Pv = tokens.Skip(i + 1).ToArray();
                    i = tokens.Length;
                    any = true;
                    break;
            }
        }

        return any ? info : null;
    }

    private static UciOption? ParseOption(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var option = new UciOption();
        var name = new List<string>();
        var defaultValue = new List<string>();
        var section = string.Empty;

        for (var i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i];
            switch (token)
            {
                case "name" or "type" or "default" or "min" or "max" or "var":
                    section = token;
                    if (token == "var") option.Vars.Add(string.Empty);
                    continue;
            }

            switch (section)
            {
                case "name": name.Add(token); break;
                case "type": option.Type = token; break;
                case "default": defaultValue.Add(token); break;
                case "min": option.Min = token; break;
                case "max": option.Max = token; break;
                case "var":
                    var last = option.Vars.Count - 1;
                    option.Vars[last] = option.Vars[last].Length == 0 ? token : option.Vars[last] + " " + token;
                    break;
            }
        }

        option.Name = string.Join(' ', name);
        option.Default = string.Join(' ', defaultValue);
        return option.Name.Length > 0 ? option : null;
    }

    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;
    private static long ParseLong(string s) => long.TryParse(s, out var v) ? v : 0;

    private void Post(Action action)
    {
        if (_disposed) return;
        if (_sync != null) _sync.Post(_ => action(), null);
        else action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop(graceful: !IsWedged);
        _turnstile.Dispose();
        _owner.Dispose();
    }
}
