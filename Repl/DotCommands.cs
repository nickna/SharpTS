using SharpTS.Diagnostics.Exceptions;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Repl;

/// <summary>
/// Handles dot-commands in the REPL (e.g., .help, .clear, .exit).
/// </summary>
internal sealed class DotCommands
{
    private readonly Interpreter _interpreter;
    private readonly VariableResolver _resolver;
    private readonly TypeChecker _typeChecker;
    private readonly DecoratorMode _decoratorMode;
    private readonly List<string> _sessionHistory;
    private readonly List<Stmt> _accumulatedStatements;

    /// <summary>
    /// Set to true when .exit is invoked to signal the REPL loop to terminate.
    /// </summary>
    public bool ExitRequested { get; private set; }

    /// <summary>
    /// Set to true when .reset is invoked to signal the REPL to recreate the interpreter.
    /// </summary>
    public bool ResetRequested { get; private set; }

    public DotCommands(Interpreter interpreter, VariableResolver resolver,
        TypeChecker typeChecker, DecoratorMode decoratorMode,
        List<string> sessionHistory, List<Stmt> accumulatedStatements)
    {
        _interpreter = interpreter;
        _resolver = resolver;
        _typeChecker = typeChecker;
        _decoratorMode = decoratorMode;
        _sessionHistory = sessionHistory;
        _accumulatedStatements = accumulatedStatements;
    }

    /// <summary>
    /// Every dot-command with its one-line help text. Single source of truth: <see cref="Execute"/>,
    /// <see cref="PrintHelp"/>, and REPL autocomplete all read this, so a new command shows up in
    /// all three without further edits.
    /// </summary>
    public static readonly (string Name, string Args, string Help)[] Commands =
    [
        (".help", "", "Show this help message"),
        (".clear", "", "Clear the console screen"),
        (".exit", "", "Exit the REPL (also: Ctrl+C twice)"),
        (".reset", "", "Reset interpreter state (fresh environment)"),
        (".type", " <expr>", "Show the TypeScript type of an expression"),
        (".save", " <file>", "Save session history to a TypeScript file"),
        (".load", " <file>", "Load and execute a TypeScript file"),
    ];

    /// <summary>
    /// Returns true if the input is a dot-command.
    /// </summary>
    public static bool IsDotCommand(string input)
    {
        return input.TrimStart().StartsWith('.');
    }

    /// <summary>
    /// Executes a dot-command. Returns true if the command was recognized.
    /// </summary>
    public bool Execute(string input)
    {
        var trimmed = input.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : null;

        switch (command)
        {
            case ".help":
                PrintHelp();
                return true;

            case ".clear":
                Console.Clear();
                return true;

            case ".exit":
                ExitRequested = true;
                return true;

            case ".reset":
                ResetRequested = true;
                return true;

            case ".type":
                ShowType(arg);
                return true;

            case ".save":
                SaveSession(arg);
                return true;

            case ".load":
                LoadFile(arg);
                return true;

            default:
                Console.WriteLine($"Unknown command: {command}. Type .help for available commands.");
                return true;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("REPL Commands:");
        foreach (var (name, args, help) in Commands)
            Console.WriteLine($"  {name + args,-18} {help}");

        Console.WriteLine("""

            Keyboard Shortcuts:
              Enter              Submit input (or continue on incomplete input)
              Shift+Enter        Force new line
              Up/Down            Navigate completions when open, else command history
              Ctrl+C             Discard current input / interrupt execution; twice to exit
              Ctrl+L             Clear screen

            Completion:
              (as you type)      Suggestions appear for names and after '.'
              Tab                Accept the highlighted suggestion
              Ctrl+Space         Show suggestions
              Escape             Dismiss suggestions
            """);
    }

    private void ShowType(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            Console.WriteLine("Usage: .type <expression>");
            return;
        }

        try
        {
            // Parse the expression, with semicolon retry for bare expressions
            var lexer = new Lexer(expression);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, _decoratorMode);
            var parseResult = parser.Parse();

            if (!parseResult.IsSuccess)
            {
                // Retry with semicolon
                lexer = new Lexer(expression + ";");
                tokens = lexer.ScanTokens();
                parser = new Parser(tokens, _decoratorMode);
                var retryResult = parser.Parse();
                if (retryResult.IsSuccess)
                    parseResult = retryResult;
                else
                {
                    Console.WriteLine("Parse error in expression.");
                    return;
                }
            }

            // Use the persistent TypeChecker that has accumulated all previous declarations.
            // Build a combined statement list: all previous + the new expression.
            var combinedStatements = new List<Stmt>(_accumulatedStatements);
            combinedStatements.AddRange(parseResult.Statements);

            var typeResult = _typeChecker.CheckWithRecovery(combinedStatements);

            // Find the type of the last expression statement (the one we just added)
            var lastExpr = parseResult.Statements
                .OfType<Stmt.Expression>()
                .LastOrDefault();

            if (lastExpr != null && typeResult.TypeMap.TryGet(lastExpr.Expr, out var typeInfo) && typeInfo != null)
            {
                Console.WriteLine(typeInfo.ToString());
            }
            else
            {
                Console.WriteLine("Could not determine type.");
            }
        }
        catch (SharpTSException ex)
        {
            Console.WriteLine($"Error: {ex.Diagnostic}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private void SaveSession(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("Usage: .save <filename>");
            return;
        }

        try
        {
            File.WriteAllLines(filePath, _sessionHistory);
            Console.WriteLine($"Session saved to {filePath} ({_sessionHistory.Count} lines)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving session: {ex.Message}");
        }
    }

    private void LoadFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Console.WriteLine("Usage: .load <filename>");
            return;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return;
        }

        try
        {
            var source = File.ReadAllText(filePath);
            Console.WriteLine($"Loading {filePath}...");

            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, _decoratorMode);
            var parseResult = parser.Parse();

            if (!parseResult.IsSuccess)
            {
                foreach (var diag in parseResult.Diagnostics)
                    Console.WriteLine($"Error: {diag}");
                return;
            }

            // Use the persistent resolver (not a throwaway) so loaded declarations
            // are visible in subsequent REPL inputs.
            _resolver.Resolve(parseResult.Statements);

            // Use InterpretRepl + TickEventLoop instead of Interpret, which calls
            // RunEventLoop → CompleteAdding() and poisons the callback queue.
            _interpreter.InterpretRepl(parseResult.Statements);
            _interpreter.TickEventLoop();

            // Accumulate loaded statements for .type resolution
            _accumulatedStatements.AddRange(parseResult.Statements);

            Console.WriteLine($"Loaded {filePath}");
        }
        catch (SharpTSException ex)
        {
            Console.WriteLine($"Error: {ex.Diagnostic}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
