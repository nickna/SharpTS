using SharpTS.Compilation.Visitors;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes async functions to identify await points and variables that must be hoisted
/// to the state machine struct. Uses the visitor pattern for AST traversal.
/// </summary>
public partial class AsyncStateAnalyzer : AstVisitorBase
{
    /// <summary>
    /// Represents a single await point in an async function.
    /// </summary>
    public record AwaitPoint(
        int StateNumber,
        Expr.Await? AwaitExpr,  // null for synthetic await points (e.g. for await…of's implicit next()/return() awaits, #631)
        HashSet<string> LiveVariables,
        int TryBlockDepth = 0,  // 0 = not in try, 1+ = nested try depth
        int? EnclosingTryId = null  // ID of the innermost try block containing this await
    );

    /// <summary>
    /// Represents a try/catch/finally block in an async function.
    /// </summary>
    public record TryBlockInfo(
        int TryId,
        Stmt.TryCatch TryStatement,
        bool HasAwaitsInTry,
        bool HasAwaitsInCatch,
        bool HasAwaitsInFinally,
        int? ParentTryId  // For nested try blocks
    );

    /// <summary>
    /// Information about an async arrow function found inside an async function.
    /// </summary>
    public record AsyncArrowInfo(
        Expr.ArrowFunction Arrow,
        HashSet<string> Captures,
        bool CapturesThis,
        int NestingLevel,  // 0 = direct child, 1 = inside another async arrow, etc.
        Expr.ArrowFunction? ParentArrow  // The parent async arrow (null if direct child of function)
    );

    /// <summary>
    /// Complete analysis results for an async function.
    /// </summary>
    public record AsyncFunctionAnalysis(
        int AwaitPointCount,
        List<AwaitPoint> AwaitPoints,
        HashSet<string> HoistedLocals,
        HashSet<string> HoistedParameters,
        bool HasTryCatch,
        bool UsesThis,
        List<AsyncArrowInfo> AsyncArrows,
        List<TryBlockInfo> TryBlocks = null!,  // Try blocks with await tracking
        // Per-binding storage names for block-scoped let/const declarations that shadow an enclosing
        // binding, keyed by declaration/reference AST node (see GeneratorBlockScopeRenamer, #766/#711).
        // Null for analyses built without the renamer (e.g. async arrows); treated as empty.
        IReadOnlyDictionary<object, string>? BlockScopeRenames = null,
        // Capturing-arrow node → (captured source name → renamed storage) (#767, async analog). Pivots
        // an inner arrow's captured field to a renamed shadow's storage. Null treated as empty.
        IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>>? BlockScopeCaptureRenames = null
    );

    // State during analysis
    private readonly List<AwaitPoint> _awaitPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterAwait = [];
    private readonly HashSet<string> _catchParameters = [];  // Catch params should not be hoisted
    private readonly List<AsyncArrowInfo> _asyncArrows = [];
    private readonly List<TryBlockInfo> _tryBlocks = [];
    private int _awaitCounter = 0;
    private bool _seenAwait = false;
    private bool _hasTryCatch = false;
    private bool _usesThis = false;
    private int _asyncArrowNestingLevel = 0;
    private Expr.ArrowFunction? _currentParentArrow = null;  // Track parent for nested arrows

    // Try block tracking
    private int _tryBlockCounter = 0;
    private int _currentTryBlockDepth = 0;
    private int? _currentTryBlockId = null;
    private readonly Stack<int> _tryBlockIdStack = new();

    // Track which region (try/catch/finally) we're currently in
    private enum TryRegion { None, Try, Catch, Finally }
    private TryRegion _currentTryRegion = TryRegion.None;
    private readonly Dictionary<int, (bool InTry, bool InCatch, bool InFinally)> _tryBlockAwaitFlags = [];

    // Reusable visitor for finding nested async arrows
    private readonly NestedAsyncArrowVisitor _nestedArrowVisitor = new();

    // Reusable visitor for analyzing arrow function captures
    private readonly CaptureAnalysisVisitor _captureVisitor = new();

    // Block-scope shadow renames for this function (#766). Maps a declaration/reference AST node to the
    // disambiguated storage name its binding uses; nodes absent from the map keep their source lexeme.
    private IReadOnlyDictionary<object, string> _renames = new Dictionary<object, string>();
    // Per-arrow capture-source pivot for renamed shadows captured by inner arrows (#767, async analog).
    private IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>> _captureRenames =
        new Dictionary<object, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Translates a declaration/reference node's source lexeme to its disambiguated storage name (#766),
    /// or returns the lexeme unchanged when the binding is not a renamed shadow.
    /// </summary>
    private string StorageName(object node, string lexeme) =>
        _renames.TryGetValue(node, out var renamed) ? renamed : lexeme;

    /// <summary>
    /// Analyzes an async function to determine await points and hoisted variables.
    /// </summary>
    public AsyncFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Disambiguate block-scoped let/const declarations that shadow an enclosing binding so the
        // hoisting decision below is made per-binding rather than per-name (#766, async analog of #711).
        // A shadow merely READ by a nested arrow is renamed and a capture-source pivot recorded (#767):
        // DefineAsyncFunction excludes such read-only-captured renamed shadows from the name-keyed
        // function display class so the arrow's read flows through the per-arrow snapshot path the pivot
        // redirects, instead of colliding with the outer same-named binding on one DC field (#837).
        var renameResult = GeneratorBlockScopeRenamer.Compute(func, arrowReadCapturesShareStorage: false);
        _renames = renameResult.Renames;
        _captureRenames = renameResult.CaptureRenames;

        // Collect parameters as variables that need hoisting
        HashSet<string> parameters = [];
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
        }

        // Analyze the function body using visitor pattern
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                Visit(stmt);
            }
        }

        // Variables that need hoisting: declared AND used after any await
        // This includes variables declared between await points that are used after a later await
        var hoistedLocals = new HashSet<string>(_declaredVariables);
        hoistedLocals.IntersectWith(_variablesUsedAfterAwait);
        hoistedLocals.ExceptWith(parameters); // Parameters are tracked separately
        hoistedLocals.ExceptWith(_catchParameters); // Catch params are scoped to catch block, not hoisted

        // Build TryBlockInfo list from collected data
        var tryBlocks = BuildTryBlockInfoList();

        return new AsyncFunctionAnalysis(
            AwaitPointCount: _awaitPoints.Count,
            AwaitPoints: [.. _awaitPoints],
            HoistedLocals: hoistedLocals,
            HoistedParameters: parameters,
            HasTryCatch: _hasTryCatch,
            UsesThis: _usesThis,
            AsyncArrows: [.. _asyncArrows],
            TryBlocks: tryBlocks,
            BlockScopeRenames: _renames,
            BlockScopeCaptureRenames: _captureRenames
        );
    }

    private List<TryBlockInfo> BuildTryBlockInfoList()
    {
        List<TryBlockInfo> result = [];
        foreach (var (tryId, flags) in _tryBlockAwaitFlags)
        {
            // Find the corresponding try statement from _tryBlocks
            var existingInfo = _tryBlocks.FirstOrDefault(t => t.TryId == tryId);
            if (existingInfo != null)
            {
                result.Add(existingInfo with
                {
                    HasAwaitsInTry = flags.InTry,
                    HasAwaitsInCatch = flags.InCatch,
                    HasAwaitsInFinally = flags.InFinally
                });
            }
        }
        // Add try blocks without awaits
        foreach (var info in _tryBlocks)
        {
            if (!result.Any(r => r.TryId == info.TryId))
            {
                result.Add(info);
            }
        }
        return result;
    }

    private void Reset()
    {
        _awaitPoints.Clear();
        _declaredVariables.Clear();
        _variablesUsedAfterAwait.Clear();
        _catchParameters.Clear();
        _asyncArrows.Clear();
        _tryBlocks.Clear();
        _awaitCounter = 0;
        _seenAwait = false;
        _hasTryCatch = false;
        _usesThis = false;
        _asyncArrowNestingLevel = 0;
        _currentParentArrow = null;
        _tryBlockCounter = 0;
        _currentTryBlockDepth = 0;
        _currentTryBlockId = null;
        _tryBlockIdStack.Clear();
        _currentTryRegion = TryRegion.None;
        _tryBlockAwaitFlags.Clear();
    }

    /// <summary>
    /// Analyzes an async arrow function to determine which variables it captures
    /// from the enclosing scope using the reusable capture analysis visitor.
    /// </summary>
    private HashSet<string> AnalyzeAsyncArrowCapturesWithVisitor(Expr.ArrowFunction af)
    {
        _captureVisitor.Reset(_declaredVariables);
        return _captureVisitor.Analyze(af);
    }

    /// <summary>
    /// Recursively analyzes an async arrow body for nested async arrows using the visitor.
    /// </summary>
    private void AnalyzeAsyncArrowBodyWithVisitor(Expr.ArrowFunction af)
    {
        // Set up callbacks for the visitor
        _nestedArrowVisitor.OnAsyncArrowFound = nestedArrow =>
        {
            // Found a nested async arrow - analyze its captures
            var captures = AnalyzeAsyncArrowCapturesWithVisitor(nestedArrow);
            var capturesThis = captures.Contains("this");
            _asyncArrows.Add(new AsyncArrowInfo(nestedArrow, captures, capturesThis, _asyncArrowNestingLevel, _currentParentArrow));

            // Recursively look for deeper nested async arrows
            var previousParent = _currentParentArrow;
            _currentParentArrow = nestedArrow;
            _asyncArrowNestingLevel++;
            AnalyzeAsyncArrowBodyWithVisitor(nestedArrow);
            _asyncArrowNestingLevel--;
            _currentParentArrow = previousParent;
        };

        _nestedArrowVisitor.OnNonAsyncArrowFound = nestedArrow =>
        {
            // Non-async nested arrow - still check for nested async arrows inside
            AnalyzeAsyncArrowBodyWithVisitor(nestedArrow);
        };

        _nestedArrowVisitor.Analyze(af);
    }
}
