using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharpTS.Test262;

/// <summary>
/// Drives a pool of persistent <c>SharpTS.Test262.Worker</c> subprocesses, each
/// fed test paths from a shared work queue and emitting JSON-line results back
/// over stdout. The parent aggregates results into a single dictionary keyed
/// by absolute path.
/// </summary>
/// <remarks>
/// <para>
/// Persistent workers (vs. per-batch kill+respawn from issue #109's earlier
/// design) keep JIT and ALC state warm across the whole regen — `dotnet`
/// process startup is paid <see cref="WorkerCount"/> times total instead of
/// once per batch. The previous batched orchestrator hit a ~1.3× speedup
/// ceiling at N=4 because per-batch subprocess spawn + JIT warmup ate most
/// of the parallel gains; persistent workers break that ceiling.
/// </para>
/// <para>
/// Crash recovery: each worker tracks the paths it has been given but hasn't
/// emitted a result for ("in-flight"). On worker death (reader sees EOF) those
/// paths are pushed back onto the shared queue so a surviving worker picks
/// them up. If the worker dies mid-test (one path in-flight), that single path
/// is bucketed as <c>RuntimeError:worker-crashed</c> instead of being requeued
/// — otherwise a pathological test could ping-pong between workers forever.
/// </para>
/// <para>
/// Idle watchdog: a parent thread monitors each worker's last-result timestamp.
/// If a worker hasn't emitted a result in <see cref="IdleResultBudget"/>, the
/// parent presumes the in-worker per-test timeout failed (e.g., pure-CPU loop
/// that never reaches a cancellation check), kills the worker, and lets the
/// remaining pool drain the queue.
/// </para>
/// </remarks>
public sealed class BatchedSubprocessRunner
{
    /// <summary>
    /// Number of worker subprocesses to run concurrently. Each worker pulls
    /// from a shared queue; with N persistent workers, total compile + JIT
    /// throughput scales close to linearly with N until disk/JIT contention
    /// flattens the curve. Capped to keep memory under control: each worker
    /// is ~100 MB resident, so N=4 ≈ 400 MB peak. Env var
    /// <c>SHARPTS_TEST262_WORKERS</c> overrides; <c>=1</c> reproduces the old
    /// single-worker semantics for bisecting flakes. The interpreted and
    /// compiled baseline facts run concurrently, so each pool defaults to one
    /// third of the logical CPUs (clamped to [1, 8]); this leaves headroom for
    /// the testhost/JIT and prevents wall-clock timeout flakes in allocation-
    /// heavy generated tests when both modes are active.
    /// </summary>
    public int WorkerCount { get; init; } = ResolveDefaultWorkerCount();

    /// <summary>
    /// Maximum gap between result lines from a single worker before the parent
    /// declares it stalled and kills it. The worker has its own per-test 5-s
    /// timeout via <c>$Runtime._cancelRequested</c>; this is the outer fallback
    /// for tests where in-worker cancellation never fires (pure-CPU loops, GC
    /// stalls, deadlocks). 5 minutes is generous: it accommodates worst-case
    /// runs of consecutive timeout-bound tests (a folder like
    /// <c>Array/from</c> averages ~4 s/test under contention, and a 60-s
    /// budget falsely killed workers there during the #103 regen).
    /// </summary>
    public TimeSpan IdleResultBudget { get; init; } = TimeSpan.FromMinutes(5);

    private static int ResolveDefaultWorkerCount()
    {
        var env = Environment.GetEnvironmentVariable("SHARPTS_TEST262_WORKERS");
        if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var n) && n > 0)
            return Math.Clamp(n, 1, 16);
        return Math.Clamp(Environment.ProcessorCount / 3, 1, 8);
    }

    public string Test262Root { get; }
    public string Mode { get; }
    public TimeSpan TestTimeout { get; }
    public string? SkipFeaturesFile { get; }
    public string WorkerExecutable { get; }

    public BatchedSubprocessRunner(
        string test262Root,
        Test262ExecutionMode mode,
        TimeSpan testTimeout,
        string? skipFeaturesFile,
        string workerExecutable)
    {
        Test262Root = test262Root;
        Mode = mode.ToString();
        TestTimeout = testTimeout;
        SkipFeaturesFile = skipFeaturesFile;
        WorkerExecutable = workerExecutable;
    }

    /// <summary>
    /// Runs every test in <paramref name="absolutePaths"/> through the worker
    /// pool. Returns a dictionary keyed by absolute path to encoded bucket.
    /// Tests for which no worker emits a result (worker died, watchdog killed,
    /// pool drained early) are bucketed as <c>RuntimeError:worker-crashed</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> RunAll(
        IReadOnlyList<string> absolutePaths,
        Action<int, int>? progress = null)
    {
        int total = absolutePaths.Count;
        if (total == 0) return new Dictionary<string, string>(StringComparer.Ordinal);

        var workQueue = new ConcurrentQueue<string>(absolutePaths);

        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var resultsLock = new object();
        int completed = 0;

        // Real results always win over pseudo-results from crash recovery.
        // A path can be speculatively marked `worker-crashed` by HandleCompletion
        // and then later actually run by another worker; without this prefer-
        // real logic we'd keep the bogus crash bucket and drop the real Pass/Fail.
        static bool IsPseudoResult(string bucket)
            => bucket.StartsWith("RuntimeError:worker-", StringComparison.Ordinal);

        void OnResult(string path, string bucket)
        {
            lock (resultsLock)
            {
                if (results.TryGetValue(path, out var existing))
                {
                    bool existingPseudo = IsPseudoResult(existing);
                    bool incomingPseudo = IsPseudoResult(bucket);
                    if (existingPseudo && !incomingPseudo)
                    {
                        results[path] = bucket; // upgrade from crash bucket to real result
                    }
                    return; // otherwise keep the first result
                }
                results[path] = bucket;
                completed++;
                progress?.Invoke(completed, total);
            }
        }

        // Run N parallel "worker slots". Each slot holds one worker subprocess;
        // when that worker hits the 1.5GB memory ceiling and exits cleanly, the
        // slot SPAWNS A REPLACEMENT and keeps draining the queue. Without this
        // respawn loop, parallelism degrades 6→5→4→…→1 over the run as workers
        // hit the ceiling (each handles ~500-1000 tests before recycling), and
        // the tail of an 11K-test regen processes at single-worker speed. With
        // respawn, all 6 slots stay busy until the shared queue is empty.
        // Each slot's task runs serially on its own thread; new workers spawn
        // inline so timing/sequencing is identical to the pre-fix single-spawn.
        int workerCount = Math.Min(WorkerCount, total);
        var slotTasks = new Task[workerCount];
        var slotDisposables = new ConcurrentBag<PersistentWorker>();
        for (int slotIndex = 0; slotIndex < workerCount; slotIndex++)
        {
            int id = slotIndex;
            slotTasks[slotIndex] = Task.Run(() =>
            {
                int spawnIndex = 0;
                while (true)
                {
                    if (!workQueue.TryPeek(out _)) return; // queue empty → slot done
                    var worker = new PersistentWorker(BuildWorkerArgs(), workQueue, OnResult, IdleResultBudget, id, spawnIndex++);
                    slotDisposables.Add(worker);
                    worker.Start();
                    worker.WaitForCompletion();
                    // Worker exited (memory ceiling, watchdog, crash, or queue
                    // drained). Loop checks queue and respawns if more work
                    // remains. The new worker reuses the same slot id; the spawn
                    // index keeps trace files unique across respawns.
                }
            });
        }
        try
        {
            Task.WaitAll(slotTasks);
        }
        finally
        {
            foreach (var w in slotDisposables) w?.Dispose();
        }

        // Recovery sweep: if any worker's HandleCompletion requeued paths during
        // its shutdown window AFTER the other workers had already exited, those
        // paths sit in the queue with no one to pick them up. Spawn a single
        // recovery worker to drain. Bounded loop: each iteration must reduce
        // queue size, otherwise we break to avoid spinning on a wedged path.
        int safetyBudget = 4;
        while (workQueue.TryPeek(out _) && safetyBudget-- > 0)
        {
            int sizeBefore = workQueue.Count;
            using var recovery = new PersistentWorker(BuildWorkerArgs(), workQueue, OnResult, IdleResultBudget, -1);
            recovery.Start();
            recovery.WaitForCompletion();
            if (workQueue.Count >= sizeBefore) break; // not making progress
        }

        // Final pass: bucket-fill anything no worker emitted (queue drained
        // because all workers crashed before reaching it, or watchdogs killed
        // the last live worker mid-stride).
        lock (resultsLock)
        {
            foreach (var path in absolutePaths)
            {
                if (!results.ContainsKey(path))
                    results[path] = "RuntimeError:worker-crashed";
            }
        }

        return results;
    }

    private string[] BuildWorkerArgs()
    {
        var args = new List<string>
        {
            WorkerExecutable,
            "--test262-root", Test262Root,
            "--mode", Mode,
            "--timeout-seconds", ((int)TestTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(SkipFeaturesFile))
        {
            args.Add("--skip-features-file");
            args.Add(SkipFeaturesFile);
        }
        return args.ToArray();
    }

    /// <summary>
    /// One persistent worker subprocess plus its writer/reader threads. Owns
    /// the lifetime of the underlying <see cref="Process"/> and tears it down
    /// on dispose. The writer pulls paths from the shared queue; the reader
    /// emits results back via the supplied callback.
    /// </summary>
    /// <summary>
    /// File-based diagnostic logger for parallel-regen flake hunting. Set
    /// <c>SHARPTS_TEST262_TRACE=1</c> to enable; otherwise the logger is a
    /// no-op. Each worker writes to its own file so threads don't interleave
    /// (stdout is captured/buffered by xUnit testhost, making it useless here).
    /// </summary>
    private sealed class WorkerTrace
    {
        private readonly StreamWriter? _writer;
        private readonly object _lock = new();
        public WorkerTrace(string? path)
        {
            if (path is null) { _writer = null; return; }
            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        }
        public void Log(string format, params object[] args)
        {
            if (_writer is null) return;
            var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {string.Format(format, args)}";
            lock (_lock) _writer.WriteLine(line);
        }
        public void Close()
        {
            if (_writer is null) return;
            lock (_lock) _writer.Dispose();
        }
    }

    private static readonly string? TraceDir = ResolveTraceDir();
    private static string? ResolveTraceDir()
    {
        var env = Environment.GetEnvironmentVariable("SHARPTS_TEST262_TRACE");
        if (string.IsNullOrEmpty(env) || env == "0") return null;
        var dir = Path.Combine(Path.GetTempPath(), $"sharpts_trace_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        Console.WriteLine($"[trace] writing to {dir}");
        return dir;
    }

    private sealed class PersistentWorker : IDisposable
    {
        private readonly Process _proc;
        private readonly ConcurrentQueue<string> _queue;
        private readonly Action<string, string> _onResult;
        private readonly TimeSpan _idleBudget;
        private readonly int _id;
        private readonly WorkerTrace _trace;
        private int _writtenCount;
        private int _readCount;

        // In-flight queue: paths sent to this worker for which we haven't yet
        // seen a result. Used to (a) detect orphans on worker death, (b) push
        // them back to the shared queue (or bucket them as crashed if a single
        // path keeps wedging successive workers).
        private readonly Queue<string> _inFlight = new();
        private readonly object _inFlightLock = new();

        // Updated by the reader on each line. Watchdog reads it. volatile so
        // the watchdog thread sees the latest value without a fence.
        private long _lastResultTicks = DateTime.UtcNow.Ticks;

        private Task? _writerTask;
        private Task? _readerTask;
        private Task? _watchdogTask;
        private readonly CancellationTokenSource _shutdownCts = new();
        private bool _killedByWatchdog;

        // Set by the reader when the worker emits its _done sentinel. Tells
        // HandleCompletion that the worker exited cleanly (drained the queue
        // and acknowledged) — anything still in inFlight is a result we
        // raced past during shutdown, NOT a crash. Without this flag the
        // pipe-buffered paths the writer pre-fed but the reader hadn't yet
        // dequeued (typical: ~50 paths) all got falsely bucketed as
        // worker-crashed during the cleanup of every clean-exiting worker.
        private bool _doneSeen;

        public PersistentWorker(string[] args, ConcurrentQueue<string> queue,
            Action<string, string> onResult, TimeSpan idleBudget, int id, int spawnIndex = 0)
        {
            _queue = queue;
            _onResult = onResult;
            _idleBudget = idleBudget;
            _id = id;
            // Trace file is unique per spawn, not per slot: the previous worker's
            // parent-side StreamWriter stays open until RunAll's finally block, so a
            // respawn reopening worker_{id}.log would hit a sharing violation.
            _trace = new WorkerTrace(TraceDir is null
                ? null
                : Path.Combine(TraceDir, $"worker_{id:D2}_{spawnIndex}.log"));

            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            // Hand the worker a per-worker trace path via env var so the
            // subprocess can also log into our trace dir.
            if (TraceDir is not null)
                psi.Environment["SHARPTS_TEST262_WORKER_TRACE"] =
                    Path.Combine(TraceDir, $"worker_{id:D2}_{spawnIndex}_subprocess.log");
            _proc = Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start Test262 worker");
            _trace.Log("spawned PID={0}", _proc.Id);
        }

        public void Start()
        {
            _writerTask = Task.Run(WriteLoop);
            _readerTask = Task.Run(ReadLoop);
            _watchdogTask = Task.Run(WatchdogLoop);
        }

        public void WaitForCompletion()
        {
            try { _writerTask?.Wait(); } catch { }
            try { _readerTask?.Wait(); } catch { }
            _shutdownCts.Cancel();
            try { _watchdogTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            try { _proc.WaitForExit(2000); } catch { }
        }

        public void Dispose()
        {
            try { _shutdownCts.Cancel(); } catch { }
            try
            {
                if (!_proc.HasExited)
                {
                    _trace.Log("DISPOSE killing live worker PID={0}", _proc.Id);
                    _proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
            _proc.Dispose();
            _shutdownCts.Dispose();
            _trace.Close();
        }

        // Writer thread: pulls paths from the shared queue and writes each to
        // the worker's stdin. Two phases:
        //
        //   1. Drain the shared queue, pushing each path into inFlight and
        //      stdin. The worker reads, runs, emits result. The reader pops
        //      from inFlight as results come in. Steady-state inFlight depth
        //      is bounded by the OS pipe buffer (~50 paths on Windows).
        //
        //   2. Once the shared queue is empty, wait for inFlight to drain
        //      AND keep checking the queue for paths that other workers
        //      requeued from crash recovery. Only close stdin when the
        //      queue is persistently empty AND inFlight is empty.
        //
        // Without phase 2, closing stdin while pipe-buffered paths were still
        // in flight would let the worker exit before the reader received their
        // results — HandleCompletion would then false-bucket those paths as
        // worker-crashed despite the worker having processed them cleanly.
        private void WriteLoop()
        {
            try
            {
                while (true)
                {
                    if (_queue.TryDequeue(out var path))
                    {
                        lock (_inFlightLock) _inFlight.Enqueue(path);
                        try
                        {
                            _proc.StandardInput.WriteLine(path);
                        }
                        catch (Exception ex)
                        {
                            // WriteLine failed (broken pipe). The path was added
                            // to inFlight but never reached the worker's stdin —
                            // pull it back out and put it on the shared queue
                            // so a surviving worker handles it.
                            lock (_inFlightLock)
                            {
                                if (_inFlight.Count > 0 && ReferenceEquals(_inFlight.Last(), path))
                                {
                                    var copy = new Queue<string>();
                                    foreach (var p in _inFlight) if (!ReferenceEquals(p, path)) copy.Enqueue(p);
                                    _inFlight.Clear();
                                    foreach (var p in copy) _inFlight.Enqueue(p);
                                    _queue.Enqueue(path);
                                    _trace.Log("WRITE-FAIL path={0} exc={1}; requeued", Path.GetFileName(path), ex.GetType().Name);
                                }
                            }
                            throw;
                        }
                        Interlocked.Increment(ref _writtenCount);
                        if (_writtenCount % 100 == 0)
                            _trace.Log("written={0} inFlight={1} queueRemaining={2}", _writtenCount, _inFlight.Count, _queue.Count);
                        continue;
                    }
                    int inFlightCount;
                    lock (_inFlightLock) inFlightCount = _inFlight.Count;
                    if (inFlightCount == 0) break;
                    Thread.Sleep(20);
                }
                _trace.Log("WRITER closing stdin (wrote {0}, inFlight={1})", _writtenCount, _inFlight.Count);
                _proc.StandardInput.Close();
            }
            catch (Exception ex)
            {
                _trace.Log("WRITER exit by exception: {0}", ex.Message);
                // Worker died (broken pipe). Reader will detect EOF and the
                // crash-handling path on completion will requeue inFlight.
            }
        }

        // Reader thread: parses JSON-line results, emits via callback, dequeues
        // in-flight. Runs until EOF (worker exited normally or crashed).
        private void ReadLoop()
        {
            try
            {
                string? line;
                while ((line = _proc.StandardOutput.ReadLine()) != null)
                {
                    Interlocked.Exchange(ref _lastResultTicks, DateTime.UtcNow.Ticks);
                    if (line.StartsWith("{\"_done\":"))
                    {
                        _doneSeen = true;
                        _trace.Log("READER saw _done (read={0}, inFlight={1})", _readCount, _inFlight.Count);
                        continue;
                    }
                    var parsed = ParseResultLine(line);
                    if (!parsed.HasValue)
                    {
                        _trace.Log("READER unparsed line: {0}", line.Length > 100 ? line[..100] + "…" : line);
                        continue;
                    }
                    var (path, bucket) = parsed.Value;
                    _onResult(path, bucket);
                    lock (_inFlightLock)
                    {
                        if (_inFlight.Count > 0)
                        {
                            var head = _inFlight.Peek();
                            if (!ReferenceEquals(head, path) && head != path)
                                _trace.Log("READER mismatch! result={0} head={1}", Path.GetFileName(path), Path.GetFileName(head));
                            _inFlight.Dequeue();
                        }
                        else
                        {
                            _trace.Log("READER inFlight empty when result {0} arrived (over-dequeue?)", Path.GetFileName(path));
                        }
                    }
                    Interlocked.Increment(ref _readCount);
                    if (_readCount % 100 == 0)
                        _trace.Log("read={0} inFlight={1}", _readCount, _inFlight.Count);
                }
                _trace.Log("READER hit EOF (read={0}, inFlight={1}, doneSeen={2})", _readCount, _inFlight.Count, _doneSeen);
            }
            catch (Exception ex)
            {
                _trace.Log("READER exit by exception: {0}", ex.Message);
            }

            // Worker has exited (or its stdout closed). Recover any unfinished
            // paths.
            HandleCompletion();
        }

        // Watchdog thread: kills the worker if no result has arrived within
        // _idleBudget. The kill triggers reader EOF, which routes through
        // HandleCompletion to recover in-flight paths.
        private void WatchdogLoop()
        {
            var token = _shutdownCts.Token;
            while (!token.IsCancellationRequested)
            {
                try { Task.Delay(1000, token).Wait(token); }
                catch { return; }
                if (_proc.HasExited) return;
                var lastTicks = Interlocked.Read(ref _lastResultTicks);
                var idle = DateTime.UtcNow.Ticks - lastTicks;
                if (idle > _idleBudget.Ticks)
                {
                    _killedByWatchdog = true;
                    _trace.Log("WATCHDOG fire (idle={0}s budget={1}s, written={2} read={3} inFlight={4})",
                        TimeSpan.FromTicks(idle).TotalSeconds, _idleBudget.TotalSeconds,
                        _writtenCount, _readCount, _inFlight.Count);
                    try { _proc.Kill(entireProcessTree: true); } catch { }
                    return;
                }
            }
        }

        // Recovery on worker exit / crash. Three cases:
        //
        // 1. Worker emitted _done before EOF (clean exit) — _doneSeen is true.
        //    Anything still in inFlight is a writer/reader race during shutdown
        //    (writer pushed the path to inFlight, but the reader hadn't yet
        //    received the corresponding result line when the worker exited).
        //    Push them back to the queue so a surviving worker reprocesses.
        //
        // 2. Worker died without emitting _done AND watchdog fired — assume
        //    the head test was the trigger and bucket it as worker-stalled
        //    so it can't ping-pong between workers forever. Requeue the rest.
        //
        // 3. Worker died without emitting _done AND watchdog didn't fire —
        //    spontaneous crash (segfault, OOM, etc). Same head-blame policy
        //    as case 2 but with the worker-crashed label.
        private void HandleCompletion()
        {
            List<string> inFlight;
            lock (_inFlightLock)
            {
                inFlight = new List<string>(_inFlight);
                _inFlight.Clear();
            }
            if (inFlight.Count == 0) return;

            if (_doneSeen)
            {
                // Clean exit — these paths weren't actually crashed; the writer
                // had pushed them into inFlight and the reader was racing to
                // catch up when the worker emitted _done and exited. Re-queue
                // for surviving workers.
                _trace.Log("HandleCompletion CLEAN-EXIT requeueing {0} unfinished paths (first 3: {1})",
                    inFlight.Count,
                    string.Join(",", inFlight.Take(3).Select(Path.GetFileName)));
                foreach (var path in inFlight) _queue.Enqueue(path);
                return;
            }

            // Pessimistic: head was the test that killed us. Requeue the rest.
            var head = inFlight[0];
            var bucket = _killedByWatchdog
                ? "RuntimeError:worker-stalled"
                : "RuntimeError:worker-crashed";
            _trace.Log("HandleCompletion CRASH (watchdog={0}) head={1} as {2}, requeue={3} (sample: {4})",
                _killedByWatchdog, Path.GetFileName(head), bucket, inFlight.Count - 1,
                inFlight.Count > 1 ? string.Join(",", inFlight.Skip(1).Take(3).Select(Path.GetFileName)) : "(none)");
            _onResult(head, bucket);
            for (int i = 1; i < inFlight.Count; i++)
                _queue.Enqueue(inFlight[i]);
        }
    }

    /// <summary>
    /// Parses one JSON-line result. Hand-rolled rather than pulling in
    /// System.Text.Json: protocol is fixed-shape, hot-path on every test,
    /// and avoiding the dep keeps the parent's link surface tiny.
    /// </summary>
    private static (string Path, string Bucket)? ParseResultLine(string line)
    {
        // Expected: {"path":"<escaped>","bucket":"<escaped>"}
        const string pathKey = "\"path\":\"";
        const string bucketKey = "\",\"bucket\":\"";
        int pi = line.IndexOf(pathKey, StringComparison.Ordinal);
        if (pi < 0) return null;
        pi += pathKey.Length;
        int bi = line.IndexOf(bucketKey, pi, StringComparison.Ordinal);
        if (bi < 0) return null;
        var path = JsonUnescape(line[pi..bi]);
        bi += bucketKey.Length;
        int end = line.LastIndexOf("\"}", StringComparison.Ordinal);
        if (end < bi) return null;
        var bucket = JsonUnescape(line[bi..end]);
        return (path, bucket);
    }

    private static string JsonUnescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            i++;
            switch (s[i])
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < s.Length)
                    {
                        var hex = s[(i + 1)..(i + 5)];
                        if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                            sb.Append((char)code);
                        i += 4;
                    }
                    break;
                default: sb.Append(s[i]); break;
            }
        }
        return sb.ToString();
    }
}
