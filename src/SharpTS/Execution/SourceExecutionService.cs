using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Parsing;

namespace SharpTS.Execution;

/// <summary>
/// The result of interpreting or compiling and executing one TypeScript source string.
/// </summary>
/// <param name="Success">True when analysis and execution completed without an error.</param>
/// <param name="Output">Captured standard output and standard error from the program.</param>
/// <param name="Errors">Formatted analysis or runtime errors.</param>
/// <param name="ExecutionTimeMs">Wall-clock execution time, excluding analysis and compilation.</param>
/// <param name="CompileTimeMs">
/// Complete compilation wall time for compiled execution; null for interpretation.
/// </param>
public sealed record SourceExecutionResult(
    bool Success,
    string Output,
    string[] Errors,
    long ExecutionTimeMs,
    long? CompileTimeMs = null)
{
    /// <summary>Ordered, precise pipeline phase timings.</summary>
    public IReadOnlyList<ExecutionPhaseTiming> Timings { get; init; } = [];
}

/// <summary>
/// Public embedding facade for executing a single TypeScript source string with bounded output.
/// </summary>
/// <remarks>
/// This service performs no file I/O and does not enforce a hard timeout. Untrusted-code hosts
/// should invoke it inside an isolated child process and terminate that process when their wall
/// clock or memory limit is reached. Calls through this facade are serialized because compiled
/// execution temporarily redirects process-wide console writers. Static imports, dynamic imports,
/// re-exports, and CommonJS <c>require()</c> are rejected explicitly.
/// </remarks>
public static class SourceExecutionService
{
    private static readonly object ExecutionGate = new();

    /// <summary>The default maximum number of captured characters.</summary>
    public const int DefaultMaxOutputLength = 100 * 1024;

    /// <summary>
    /// Applies the process-wide controls expected by a child process that executes
    /// untrusted single-source programs.
    /// </summary>
    public static void ConfigureUntrustedProcess(object? blockedProxyUri)
    {
        if (blockedProxyUri is not string proxyUri || string.IsNullOrWhiteSpace(proxyUri))
            throw new ArgumentException("A blocked proxy URI is required.", nameof(blockedProxyUri));

        lock (ExecutionGate)
        {
            AppContext.SetSwitch("SharpTS.RestrictProcessControl", true);
            HttpClient.DefaultProxy = new WebProxy(proxyUri)
            {
                BypassProxyOnLocal = false
            };
        }
    }

    /// <summary>
    /// Lexes, parses, type-checks, resolves, and interprets a source string with decorators disabled.
    /// </summary>
    public static SourceExecutionResult Interpret(
        string source,
        int maxOutputLength = DefaultMaxOutputLength)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateMaxOutputLength(maxOutputLength);

        lock (ExecutionGate)
            return InterpretCore(source, maxOutputLength);
    }

    private static SourceExecutionResult InterpretCore(string source, int maxOutputLength)
    {
        using var output = new CappedTextWriter(maxOutputLength);
        var errors = new List<string>();
        var timings = new List<ExecutionPhaseTiming>();

        try
        {
            var analysis = SingleSourceAnalyzer.Analyze(
                source,
                new SingleSourceAnalysisOptions(
                    DecoratorMode.None,
                    CompilationService.DefaultSourceFileName));
            timings.AddRange(analysis.Timings);
            if (!analysis.Success)
            {
                errors.AddRange(analysis.Diagnostics
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.ToString()));
                if (analysis.HitErrorLimit)
                    errors.Add("Too many errors, stopping.");
                return Result(false, output, errors, 0, timings: timings);
            }

            Interpreter? interpreter = null;
            var prepareStartedAt = Stopwatch.GetTimestamp();
            try
            {
                interpreter = new Interpreter(output, output);
                interpreter.SetDecoratorMode(DecoratorMode.None);

                var resolver = new VariableResolver(interpreter);
                resolver.Resolve(analysis.Statements);
                timings.Add(ExecutionPhaseTiming.Completed(
                    ExecutionPhaseTiming.PrepareInterpreter, ElapsedMilliseconds(prepareStartedAt)));
            }
            catch (Exception ex)
            {
                timings.Add(ExecutionPhaseTiming.Failed(
                    ExecutionPhaseTiming.PrepareInterpreter, ElapsedMilliseconds(prepareStartedAt)));
                errors.Add(ex.Message);
                interpreter?.Dispose();
                return Result(false, output, errors, 0, timings: timings);
            }

            using (interpreter)
            {
                var executeStartedAt = Stopwatch.GetTimestamp();
                try
                {
                    interpreter.Interpret(analysis.Statements, analysis.TypeMap!);
                    var executionDurationMs = ElapsedMilliseconds(executeStartedAt);

                    if (interpreter.LastUncaughtError is { } uncaught)
                    {
                        errors.Add(uncaught.Message);
                        timings.Add(ExecutionPhaseTiming.Failed(
                            ExecutionPhaseTiming.Execute, executionDurationMs));
                    }
                    else
                    {
                        timings.Add(ExecutionPhaseTiming.Completed(
                            ExecutionPhaseTiming.Execute, executionDurationMs));
                    }

                    return Result(
                        errors.Count == 0,
                        output,
                        errors,
                        (long)executionDurationMs,
                        timings: timings);
                }
                catch (Exception ex)
                {
                    var executionDurationMs = ElapsedMilliseconds(executeStartedAt);
                    timings.Add(ExecutionPhaseTiming.Failed(
                        ExecutionPhaseTiming.Execute, executionDurationMs));
                    errors.Add(ex.Message);
                    return Result(
                        false,
                        output,
                        errors,
                        (long)executionDurationMs,
                        timings: timings);
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Result(false, output, errors, 0, timings: timings);
        }
    }

    /// <summary>
    /// Compiles a source string to an in-memory assembly and executes it with decorators disabled.
    /// </summary>
    /// <remarks>
    /// Dynamic assembly execution requires a managed runtime. It is unavailable in Native AOT.
    /// </remarks>
    public static SourceExecutionResult CompileAndExecute(
        string source,
        int maxOutputLength = DefaultMaxOutputLength)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateMaxOutputLength(maxOutputLength);

        lock (ExecutionGate)
            return CompileAndExecuteCore(source, maxOutputLength);
    }

    private static SourceExecutionResult CompileAndExecuteCore(string source, int maxOutputLength)
    {
        using var output = new CappedTextWriter(maxOutputLength);
        var errors = new List<string>();
        var timings = new List<ExecutionPhaseTiming>();

        try
        {
            var compileResult = CompilationService.Compile(
                source,
                new CompileOptions(DecoratorMode.None));
            timings.AddRange(compileResult.Timings);

            if (!compileResult.Success)
            {
                errors.AddRange(compileResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));
                return Result(
                    false,
                    output,
                    errors,
                    0,
                    compileResult.CompileTimeMs,
                    timings);
            }

            var runResult = CompilationService.Execute(compileResult.AssemblyBytes!, output);
            timings.AddRange(runResult.Timings);
            if (!runResult.Success && runResult.Error is not null)
                errors.Add(runResult.Error);

            return Result(
                runResult.Success,
                output,
                errors,
                runResult.ExecuteTimeMs,
                compileResult.CompileTimeMs,
                timings);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Result(false, output, errors, 0, timings: timings);
        }
    }

    /// <summary>
    /// JSON bridge used by the <c>sharpts:execution</c> module across the standalone
    /// compiled-runtime boundary.
    /// </summary>
    public static string RunJson(object? source, object? mode, object? maxOutputLength)
    {
        if (source is not string sourceText || string.IsNullOrWhiteSpace(sourceText))
            throw new ArgumentException("Source code cannot be empty.", nameof(source));

        var requestedMode = mode?.ToString() ?? "interpret";
        var limit = maxOutputLength switch
        {
            double number when double.IsFinite(number) && number == Math.Truncate(number)
                && number >= 1 && number <= int.MaxValue => (int)number,
            int number when number > 0 => number,
            _ => throw new ArgumentOutOfRangeException(
                nameof(maxOutputLength), maxOutputLength, "Maximum output length must be a positive integer.")
        };

        var result = requestedMode.Equals("compile", StringComparison.OrdinalIgnoreCase)
            ? CompileAndExecute(sourceText, limit)
            : requestedMode.Equals("interpret", StringComparison.OrdinalIgnoreCase)
                ? Interpret(sourceText, limit)
                : throw new ArgumentException("Mode must be 'interpret' or 'compile'.", nameof(mode));

        return JsonSerializer.Serialize(
            result,
            SourceExecutionJsonContext.Default.SourceExecutionResult);
    }

    private static SourceExecutionResult Result(
        bool success,
        CappedTextWriter output,
        List<string> errors,
        long executionTimeMs,
        long? compileTimeMs = null,
        IReadOnlyList<ExecutionPhaseTiming>? timings = null) =>
        new(
            success,
            output.GetContent(),
            errors.ToArray(),
            executionTimeMs,
            compileTimeMs)
        {
            Timings = (timings ?? []).ToArray()
        };

    private static double ElapsedMilliseconds(long startedAt) =>
        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    private static void ValidateMaxOutputLength(int maxOutputLength)
    {
        if (maxOutputLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputLength),
                maxOutputLength,
                "Maximum output length must be greater than zero.");
    }

    /// <summary>
    /// A synchronized writer that never retains more than the configured number of characters.
    /// Deriving directly from TextWriter ensures every WriteLine overload flows through the cap.
    /// </summary>
    private sealed class CappedTextWriter(int maxLength) : TextWriter
    {
        private const string TruncationMarker = "\n[Output truncated]\n";
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();
        private bool _capped;

        public override Encoding Encoding => Encoding.UTF8;

        public string GetContent()
        {
            lock (_gate)
                return _buffer.ToString();
        }

        public override void Write(char value)
        {
            Span<char> buffer = stackalloc char[1];
            buffer[0] = value;
            Write(buffer);
        }

        public override void Write(string? value)
        {
            if (value is null)
                return;
            Write(value.AsSpan());
        }

        public override void Write(char[]? buffer, int index, int count)
        {
            if (buffer is null)
                return;
            Write(buffer.AsSpan(index, count));
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            lock (_gate)
            {
                if (_capped)
                    return;
                if (buffer.Length > maxLength - _buffer.Length)
                {
                    Cap(buffer);
                    return;
                }
                _buffer.Append(buffer);
            }
        }

        private void Cap(ReadOnlySpan<char> overflow)
        {
            if (_capped)
                return;

            _capped = true;

            // Keep the result at or below maxLength. When the limit can hold the marker,
            // preserve the earliest output prefix and replace its tail with the marker. For
            // very small limits, retaining guest output is more useful than a partial marker.
            bool includeMarker = maxLength >= TruncationMarker.Length;
            int contentLimit = includeMarker
                ? maxLength - TruncationMarker.Length
                : maxLength;

            if (_buffer.Length > contentLimit)
                _buffer.Length = contentLimit;

            int remaining = contentLimit - _buffer.Length;
            if (remaining > 0)
                _buffer.Append(overflow[..Math.Min(remaining, overflow.Length)]);

            if (includeMarker)
                _buffer.Append(TruncationMarker);
        }
    }
}
