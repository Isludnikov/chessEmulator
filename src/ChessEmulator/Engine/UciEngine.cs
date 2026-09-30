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

    /// <summary>
    /// Запись в stdin и снятие процесса. StreamWriter не потокобезопасен, а пишут в него сразу
    /// несколько потоков: сторож, отмена по токену (поток того, кто отменил), владелец аренды.
    /// Без блокировки stop мог бы вклиниться посреди «position … moves» — движок этого не переживёт.
    /// </summary>
    private readonly object _writeLock = new();

    private Process? _process;
    private TaskCompletionSource<bool>? _uciOkTcs;
    private TaskCompletionSource<bool>? _readyTcs;
    private SearchState? _search;
    private bool _disposed;

    // Счётчики рукопожатий isready/readyok (под _gate), обнуляются при запуске процесса.
    private long _readySent;
    private long _readyAnswered;
    private long _readyAwaited;

    public UciEngine() => _sync = SynchronizationContext.Current;

    public string ExecutablePath { get; private set; } = string.Empty;
    public string Name { get; private set; } = "—";
    public string Author { get; private set; } = string.Empty;
    public List<UciOption> Options { get; } = new();
    public bool IsRunning
    {
        get
        {
            var process = _process;
            if (process == null) return false;
            // Процесс могли снять и освободить из другого потока между чтением поля и вопросом.
            try { return !process.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
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

    private long _searchCounter;

    private sealed class SearchState
    {
        private long _lastOutput = Environment.TickCount64;

        public SearchState(long id) => Id = id;

        /// <summary>Номер поиска: им помечаются строки info (<see cref="EngineInfo.SearchId"/>).</summary>
        public long Id { get; }

        public TaskCompletionSource<SearchResult> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SearchResult Result { get; } = new();
        public bool Completed => Tcs.Task.IsCompleted;

        private long _stopSentAt;

        /// <summary>Момент первой отправки stop (Environment.TickCount64), 0 — stop ещё не посылали.</summary>
        public long StopSentTicks => Interlocked.Read(ref _stopSentAt);

        /// <summary>Отмечает отправку stop. Считается первая: от неё и отсчитывается срок ответа.</summary>
        public void MarkStopSent() => Interlocked.CompareExchange(ref _stopSentAt, Environment.TickCount64, 0);

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

        // Семафоры никогда не освобождаются (см. UciEngine.Dispose), поэтому Release всегда безопасен.
        public void Dispose() => _engine._owner.Release();
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
        // Каждый обработчик сверяет процесс: запоздавшие строки и Exited уже снятого процесса
        // иначе уронили бы ожидания нового (uciok, isready, поиск).
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && ReferenceEquals(_process, process)) HandleLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && ReferenceEquals(_process, process))
                Post(() => LogReceived?.Invoke(this, "! " + e.Data));
        };
        process.Exited += (_, _) =>
        {
            // Снятый нами процесс ожидания уже уронил в Stop; тут остаётся только внезапная смерть.
            if (!ReferenceEquals(_process, process)) return;
            FailPendingOperations(new InvalidOperationException("Процесс движка завершился."));
            Post(() => Exited?.Invoke(this, EventArgs.Empty));
        };

        if (!process.Start()) throw new InvalidOperationException("Не удалось запустить процесс движка.");

        // До начала чтения: иначе первые строки движка пришли бы, пока поле ещё пустое.
        _process = process;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        ExecutablePath = executablePath;
        Name = Path.GetFileNameWithoutExtension(executablePath);
        Options.Clear();
        lock (_gate)
        {
            _search = null;
            _readySent = _readyAnswered = _readyAwaited = 0;
        }
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
        // Останавливать могут одновременно интерфейс и сторож: процесс достаётся ровно одному.
        var process = Interlocked.Exchange(ref _process, null);
        if (process == null) return;

        FailPendingOperations(new OperationCanceledException("Движок остановлен."));

        // Под той же блокировкой, что и Send: иначе чужая запись попала бы в уже освобождённый поток.
        lock (_writeLock)
        {
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
        // В зависший движок пишем только stop/quit: остальное всё равно никто не прочитает.
        if (IsWedged && command != "stop" && command != "quit") return;

        lock (_writeLock)
        {
            // Читаем под блокировкой: Stop снимает и освобождает процесс под ней же.
            var process = _process;
            if (process == null) return;
            try
            {
                if (process.HasExited) return;
                process.StandardInput.WriteLine(command);
                process.StandardInput.Flush();
                if (command == "isready") lock (_gate) _readySent++;
                Post(() => LogReceived?.Invoke(this, "> " + command));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Движок закрылся между проверкой и записью. Бросать нельзя: Send зовётся и из
                // обработчика отмены токена, и исключение ушло бы в CancellationTokenSource.Cancel.
            }
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

    /// <summary>
    /// Запускает поиск и ждёт bestmove. Одновременно выполняется только один поиск.
    /// <paramref name="onStarted"/> получает номер поиска (как и события, через SynchronizationContext)
    /// раньше любой его строки info — по нему интерфейс отличает свои строки от запоздавших чужих.
    /// </summary>
    public async Task<SearchResult> GoAsync(string fen, IEnumerable<string>? moves, SearchLimits limits,
        CancellationToken ct = default, Action<long>? onStarted = null)
    {
        ThrowIfWedged();
        if (!IsRunning) throw new InvalidOperationException("Движок не запущен.");

        using var lease = await AcquireAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var state = new SearchState(Interlocked.Increment(ref _searchCounter));
        lock (_gate) _search = state;
        // До go: строки info этого поиска встанут в очередь уже после уведомления.
        if (onStarted != null) Post(() => onStarted(state.Id));

        SetPosition(fen, moves);
        Send(limits.ToGoCommand());
        if (limits.MoveTimeMs is { } movetime)
            state.DeadlineTicks = Environment.TickCount64 + movetime + (long)MoveTimeMargin.TotalMilliseconds;

        // Только после go: отмена, пришедшая раньше, отправила бы stop в пустоту, и поиск
        // шёл бы дальше как ни в чём не бывало. Уже отменённый токен сработает прямо здесь.
        using var registration = ct.Register(() =>
        {
            state.MarkStopSent();
            Send("stop");
        });

        return await WatchSearchAsync(state).ConfigureAwait(false);
    }

    /// <summary>
    /// Ждёт bestmove под присмотром двух сторожей: движок не должен молчать дольше
    /// <see cref="SilenceTimeout"/>, а поиск с movetime — выходить за отведённое время.
    /// Бесконечный анализ и поиск по глубине ограничены только молчанием: они вправе идти долго.
    /// </summary>
    private async Task<SearchResult> WatchSearchAsync(SearchState state)
    {
        var stopTimeoutMs = (long)StopTimeout.TotalMilliseconds;
        while (true)
        {
            var silenceAt = state.LastOutputTicks + (long)SilenceTimeout.TotalMilliseconds;
            var next = state.DeadlineTicks is { } deadline && deadline < silenceAt ? deadline : silenceAt;
            var wait = Math.Max(0, next - Environment.TickCount64);

            if (await WaitOrTimeoutAsync(state.Tcs.Task, TimeSpan.FromMilliseconds(wait)).ConfigureAwait(false))
                return await state.Tcs.Task.ConfigureAwait(false);

            var now = Environment.TickCount64;
            if (now - state.LastOutputTicks >= (long)SilenceTimeout.TotalMilliseconds)
                return await FailSearchAsync(state, $"движок молчит {SilenceTimeout.TotalSeconds:0} с во время поиска")
                    .ConfigureAwait(false);

            if (state.DeadlineTicks is { } hard && now >= hard)
            {
                // stop мог послать и кто-то другой (отмена, StopSearchAsync) — тогда срок ответа
                // считаем от его отправки, а не объявляем движок зависшим сразу.
                var stopSent = state.StopSentTicks;
                if (stopSent != 0)
                {
                    if (now - stopSent >= stopTimeoutMs)
                        return await FailSearchAsync(state, "движок не остановился после истечения времени на ход")
                            .ConfigureAwait(false);
                    state.DeadlineTicks = stopSent + stopTimeoutMs;
                    continue;
                }

                // Движок жив (строки идут), но просрочил movetime — просим остановиться.
                state.MarkStopSent();
                Send("stop");
                state.DeadlineTicks = now + stopTimeoutMs;
            }
        }
    }

    /// <summary>
    /// Объявляет движок зависшим и роняет ожидание поиска. Обычно ожидание роняет сам
    /// MarkWedged, но если зависание объявили раньше нас, ронять уже нечего — тогда
    /// бросаем сами, иначе поиск остался бы ждать bestmove, которого не будет.
    /// </summary>
    private async Task<SearchResult> FailSearchAsync(SearchState state, string reason)
    {
        MarkWedged(reason);
        if (!state.Tcs.Task.IsCompleted) throw new EngineUnresponsiveException(reason);
        return await state.Tcs.Task.ConfigureAwait(false);
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

        state.MarkStopSent();
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
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            // Ждём ответ именно на этот isready: readyok от прошлого рукопожатия, брошенного
            // по отмене, иначе закрыл бы наше раньше времени.
            // Send считает каждый ушедший isready, так что наш будет следующим по номеру.
            _readyTcs = tcs;
            _readyAwaited = _readySent + 1;
        }
        Send("isready");
        if (!await WaitOrTimeoutAsync(tcs.Task, ReadyTimeout, ct).ConfigureAwait(false))
        {
            var reason = $"движок не ответил на isready за {ReadyTimeout.TotalSeconds:0} с";
            MarkWedged(reason);
            // Обычно MarkWedged роняет наше ожидание с этой же причиной. Но если зависание
            // объявили раньше нас, ронять уже нечего — тогда называем причину сами, иначе
            // остались бы ждать ответа, которого не будет.
            if (!tcs.Task.IsCompleted) throw new EngineUnresponsiveException(reason);
        }
        await tcs.Task.ConfigureAwait(false);
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
            info.SearchId = current?.Id ?? 0;

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
        else if (line.StartsWith("readyok", StringComparison.Ordinal))
        {
            TaskCompletionSource<bool>? ready = null;
            lock (_gate)
            {
                // readyok приходят строго по порядку isready: считаем их и закрываем ожидание,
                // только когда пришёл ответ на последний отправленный.
                if (++_readyAnswered >= _readyAwaited) ready = _readyTcs;
            }
            ready?.TrySetResult(true);
        }
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
                        // Нечисловую оценку пропускаем: превратить её в ноль значит показать
                        // на шкале уверенное равенство там, где движок ничего не сказал.
                        if (kind == "cp" && i + 2 < tokens.Length)
                        {
                            if (int.TryParse(tokens[i + 2], out var cp))
                            {
                                info.ScoreCp = cp;
                                any = true;
                            }
                            i += 2;
                        }
                        else if (kind == "mate" && i + 2 < tokens.Length)
                        {
                            if (int.TryParse(tokens[i + 2], out var mate))
                            {
                                info.ScoreMate = mate;
                                any = true;
                            }
                            i += 2;
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
        // Семафоры намеренно не освобождаем: Dispose не будит тех, кто уже ждёт в WaitAsync,
        // и они повисли бы навсегда. Неуправляемых ресурсов у них нет (AvailableWaitHandle не
        // используется), а проснувшийся ожидающий увидит остановленный движок и бросит исключение.
    }
}
