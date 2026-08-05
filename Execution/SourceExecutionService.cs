using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

/// <summary>
/// The result of interpreting or compiling and executing one TypeScript source string.
/// </summary>
/// <param name="Success">True when analysis and execution completed without an error.</param>
/// <param name="Output">Captured standard output and standard error from the program.</param>
/// <param name="Errors">Formatted analysis or runtime errors.</param>
/// <param name="ExecutionTimeMs">Wall-clock execution time, excluding analysis and compilation.</param>
/// <param name="CompileTimeMs">Compilation time for compiled execution; null for interpretation.</param>
public sealed record SourceExecutionResult(
    bool Success,
    string Output,
    string[] Errors,
    long ExecutionTimeMs,
    long? CompileTimeMs = null);

/// <summary>
/// Public embedding facade for executing a single TypeScript source string with bounded output.
/// </summary>
/// <remarks>
/// This service performs no file I/O and does not enforce a hard timeout. Untrusted-code hosts
/// should invoke it inside an isolated child process and terminate that process when their wall
/// clock or memory limit is reached.
/// </remarks>
public static class SourceExecutionService
{
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

        AppContext.SetSwitch("SharpTS.RestrictProcessControl", true);
        HttpClient.DefaultProxy = new WebProxy(proxyUri)
        {
            BypassProxyOnLocal = false
        };
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

        using var output = new CappedTextWriter(maxOutputLength);
        var errors = new List<string>();
        long executionTimeMs = 0;

        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();

            var parser = new Parser(tokens, DecoratorMode.None);
            var parseResult = parser.Parse();
            if (!parseResult.IsSuccess)
            {
                errors.AddRange(parseResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));
                if (parseResult.HitErrorLimit)
                    errors.Add("Too many errors, stopping.");
                return Result(false, output, errors, 0);
            }

            var checker = new TypeChecker();
            checker.SetDecoratorMode(DecoratorMode.None);
            var typeResult = checker.CheckWithRecovery(parseResult.Statements);
            if (!typeResult.IsSuccess)
            {
                errors.AddRange(typeResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));
                if (typeResult.HitErrorLimit)
                    errors.Add("Too many errors, stopping.");
                return Result(false, output, errors, 0);
            }

            using var interpreter = new Interpreter(output, output);
            interpreter.SetDecoratorMode(DecoratorMode.None);

            var resolver = new VariableResolver(interpreter);
            resolver.Resolve(parseResult.Statements);

            var stopwatch = Stopwatch.StartNew();
            interpreter.Interpret(parseResult.Statements, typeResult.TypeMap);
            stopwatch.Stop();
            executionTimeMs = stopwatch.ElapsedMilliseconds;

            if (interpreter.LastUncaughtError is { } uncaught)
                errors.Add(uncaught.Message);

            return Result(errors.Count == 0, output, errors, executionTimeMs);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Result(false, output, errors, executionTimeMs);
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

        using var output = new CappedTextWriter(maxOutputLength);
        var errors = new List<string>();

        try
        {
            var compileResult = CompilationService.Compile(
                source,
                new CompileOptions(DecoratorMode.None));

            if (!compileResult.Success)
            {
                errors.AddRange(compileResult.Diagnostics.Select(diagnostic => diagnostic.ToString()));
                return Result(false, output, errors, 0, compileResult.CompileTimeMs);
            }

            var runResult = CompilationService.Execute(compileResult.AssemblyBytes!, output);
            if (!runResult.Success && runResult.Error is not null)
                errors.Add(runResult.Error);

            return Result(
                runResult.Success,
                output,
                errors,
                runResult.ExecuteTimeMs,
                compileResult.CompileTimeMs);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Result(false, output, errors, 0);
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

        return JsonSerializer.Serialize(result);
    }

    private static SourceExecutionResult Result(
        bool success,
        CappedTextWriter output,
        List<string> errors,
        long executionTimeMs,
        long? compileTimeMs = null) =>
        new(success, output.GetContent(), errors.ToArray(), executionTimeMs, compileTimeMs);

    private static void ValidateMaxOutputLength(int maxOutputLength)
    {
        if (maxOutputLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputLength),
                maxOutputLength,
                "Maximum output length must be greater than zero.");
    }

    /// <summary>
    /// A synchronized writer that stops retaining guest output after the configured limit.
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
            lock (_gate)
            {
                if (_capped)
                    return;
                if (_buffer.Length >= maxLength)
                {
                    Cap();
                    return;
                }
                _buffer.Append(value);
            }
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
                if (_buffer.Length + buffer.Length > maxLength)
                {
                    Cap();
                    return;
                }
                _buffer.Append(buffer);
            }
        }

        private void Cap()
        {
            if (_capped)
                return;
            _capped = true;
            _buffer.Append(TruncationMarker);
        }
    }
}
