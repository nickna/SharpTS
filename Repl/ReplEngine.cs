using PrettyPrompt;
using PrettyPrompt.Highlighting;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Repl;

/// <summary>
/// Enhanced REPL engine using PrettyPrompt for multi-line editing,
/// syntax highlighting, persistent history, and auto-display of results.
/// </summary>
public sealed class ReplEngine : IDisposable
{
    private Interpreter _interpreter;
    private VariableResolver _resolver;
    private TypeChecker _typeChecker;
    private readonly DecoratorMode _decoratorMode;
    private readonly TypeCheckerOptions _typeOptions;
    private readonly List<string> _sessionHistory = [];
    private readonly List<Stmt> _accumulatedStatements = [];
    private readonly ReplCompletionSession _completionSession;

    /// <param name="typeOptions">
    /// Resolved strictness for the session. Null keeps product defaults. The checker instance is
    /// long-lived across REPL lines, and its assignability caches are keyed by type pair only —
    /// so a future `.strict on` command must build a NEW checker via <see cref="CreateTypeChecker"/>
    /// and replay the session, never mutate the live one.
    /// </param>
    public ReplEngine(DecoratorMode decoratorMode, TypeCheckerOptions? typeOptions = null)
    {
        _decoratorMode = decoratorMode;
        _typeOptions = typeOptions ?? TypeCheckerOptions.Default;
        _interpreter = new Interpreter();
        _interpreter.SetDecoratorMode(decoratorMode);
        _resolver = new VariableResolver(_interpreter);
        _typeChecker = CreateTypeChecker();

        // Holds the current interpreter/checker by reference so the prompt callbacks, which are
        // built once before the read loop, keep working after `.reset` replaces them.
        _completionSession = new ReplCompletionSession(
            _interpreter, _typeChecker, decoratorMode, _accumulatedStatements);
        Completions = new ReplCompletionProvider(_completionSession);
    }

    /// <summary>
    /// Autocomplete over the current session state. Exposed for tests, which cannot drive
    /// <see cref="RunAsync"/> because it blocks on a real console prompt.
    /// </summary>
    internal ReplCompletionProvider Completions { get; }

    private TypeChecker CreateTypeChecker()
    {
        var checker = new TypeChecker(_typeOptions);
        checker.SetDecoratorMode(_decoratorMode);
        return checker;
    }

    public async Task RunAsync()
    {
        // Persistent history file at ~/.sharpts/repl_history
        var historyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sharpts");
        Directory.CreateDirectory(historyDir);
        var historyPath = Path.Combine(historyDir, "repl_history");

        var callbacks = new ReplCallbacks(Completions);
        var configuration = new PromptConfiguration(
            prompt: new FormattedString("> ", new FormatSpan(0, 2, AnsiColor.Cyan)));

        await using var prompt = new Prompt(
            persistentHistoryFilepath: historyPath,
            callbacks: callbacks,
            configuration: configuration);

        // Tracks a Ctrl+C that discarded the previous line, so a second one exits.
        var cancelledPreviousLine = false;

        while (true)
        {
            // Tick the event loop between inputs so timers/microtasks fire
            _interpreter.TickEventLoop();

            var response = await prompt.ReadLineAsync();

            if (!response.IsSuccess)
            {
                // Ctrl+C. PrettyPrompt reports it as an unsuccessful read whose text is always
                // empty — so there is no way to tell a cancelled line from a cancelled empty
                // prompt — and it suppresses process termination, which means this loop is the
                // only thing that can end the session. A second consecutive press exits, matching
                // the Node REPL. (Ctrl+D cannot be used for this: PrettyPrompt binds it to
                // forward-delete, so it never reaches here.)
                if (cancelledPreviousLine)
                    break;

                cancelledPreviousLine = true;
                Console.WriteLine("(To exit, press Ctrl+C again or type .exit)");
                continue;
            }

            cancelledPreviousLine = false;
            var input = response.Text;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            _sessionHistory.Add(input);

            // Handle dot-commands
            if (DotCommands.IsDotCommand(input))
            {
                var commands = new DotCommands(_interpreter, _resolver, _typeChecker,
                    _decoratorMode, _sessionHistory, _accumulatedStatements);
                commands.Execute(input);

                // `.load` appends to the accumulated statements, so any dot-command may have
                // changed what completion should offer.
                _completionSession.OnStatementsAppended();

                if (commands.ExitRequested)
                    break;

                if (commands.ResetRequested)
                {
                    _interpreter.Dispose();
                    _interpreter = new Interpreter();
                    _interpreter.SetDecoratorMode(_decoratorMode);
                    _resolver = new VariableResolver(_interpreter);
                    _typeChecker = CreateTypeChecker();
                    _accumulatedStatements.Clear();
                    // Point completion at the new state and drop its cached member lists.
                    _completionSession.Replace(_interpreter, _typeChecker);
                    Console.WriteLine("REPL state has been reset.");
                }

                continue;
            }

            // Execute TypeScript input with Ctrl+C interruption support
            ExecuteWithInterrupt(input);
        }
    }

    private void ExecuteWithInterrupt(string source)
    {
        var executionThread = Thread.CurrentThread;
        var cancelled = false;

        void handler(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // Prevent process exit
            cancelled = true;
            executionThread.Interrupt();
        }

        Console.CancelKeyPress += handler;
        try
        {
            ExecuteInput(source);
        }
        catch (ThreadInterruptedException)
        {
            Console.WriteLine();
            Console.WriteLine("Execution interrupted.");
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            if (cancelled)
            {
                // Clear any residual interrupt flag. If the interrupt was already
                // consumed by the catch above, Sleep(0) returns immediately.
                // If a second interrupt arrived between catch and here, Sleep(0)
                // throws — we catch and discard it so the REPL loop survives.
                try { Thread.Sleep(0); }
                catch (ThreadInterruptedException) { /* consumed */ }
            }
        }
    }

    /// <summary>Disposes the interpreter that owns the session's runtime state.</summary>
    public void Dispose() => _interpreter.Dispose();

    private static ParseDiagnosticResult TryParse(string source, DecoratorMode decoratorMode)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, decoratorMode);
        return parser.Parse();
    }

    /// <summary>
    /// Runs one line of REPL input: parse, resolve, interpret, accumulate.
    /// Internal so tests can build up real session state without driving the console prompt.
    /// </summary>
    internal void ExecuteInput(string source)
    {
        try
        {
            // Parse — if it fails, retry with an appended semicolon (REPL convenience:
            // the parser requires semicolons but REPL users often omit them)
            var parseResult = TryParse(source, _decoratorMode);

            if (!parseResult.IsSuccess)
            {
                var retryResult = TryParse(source + ";", _decoratorMode);
                if (retryResult.IsSuccess)
                {
                    parseResult = retryResult;
                }
                else
                {
                    // Show the original errors (not the retry errors)
                    foreach (var diagnostic in parseResult.Diagnostics)
                        Console.WriteLine($"Error: {diagnostic}");
                    if (parseResult.HitErrorLimit)
                        Console.WriteLine("Too many errors, stopping.");
                    return;
                }
            }

            // Variable resolution — reuse the persistent resolver.
            // If resolution throws (e.g. malformed closure leaves scope stack dirty),
            // replace with a fresh resolver. The important accumulated state lives in
            // the interpreter's _locals dictionary, not in the resolver itself.
            try
            {
                _resolver.Resolve(parseResult.Statements);
            }
            catch
            {
                _resolver = new VariableResolver(_interpreter);
            }

            // Execute and capture result
            var result = _interpreter.InterpretRepl(parseResult.Statements);

            // Accumulate statements so .type and autocomplete can resolve types from previous
            // inputs. Type checking is deferred — it only runs when one of them is invoked.
            _accumulatedStatements.AddRange(parseResult.Statements);
            _completionSession.OnStatementsAppended();

            // Tick event loop after execution to process async work
            _interpreter.TickEventLoop();

            // Auto-display expression results (skip undefined, like Node.js)
            if (result is not null and not SharpTSUndefined)
            {
                Console.WriteLine(ValueFormatter.Format(result));
            }
        }
        catch (SharpTSException ex)
        {
            Console.WriteLine($"Error: {ex.Diagnostic}");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Runtime Error:"))
        {
            Console.WriteLine(ex.Message);
        }
        catch (ThreadInterruptedException)
        {
            throw; // Re-throw for the outer handler
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
