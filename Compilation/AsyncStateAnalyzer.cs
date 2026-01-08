using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes async functions to identify await points and variables that must be hoisted
/// to the state machine struct.
/// </summary>
public partial class AsyncStateAnalyzer
{
    /// <summary>
    /// Represents a single await point in an async function.
    /// </summary>
    public record AwaitPoint(
        int StateNumber,
        Expr.Await AwaitExpr,
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
        List<TryBlockInfo> TryBlocks = null!  // Try blocks with await tracking
    )
    {
        /// <summary>
        /// Whether any try block contains await points (requires special handling).
        /// </summary>
        public bool HasAwaitsInTryBlocks =>
            TryBlocks?.Any(t => t.HasAwaitsInTry || t.HasAwaitsInCatch || t.HasAwaitsInFinally) ?? false;
    }

    // State during analysis
    private readonly List<AwaitPoint> _awaitPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterAwait = [];
    private readonly HashSet<string> _variablesDeclaredBeforeAwait = [];
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

    /// <summary>
    /// Analyzes an async function to determine await points and hoisted variables.
    /// </summary>
    public AsyncFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Collect parameters as variables that need hoisting
        var parameters = new HashSet<string>();
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
            _variablesDeclaredBeforeAwait.Add(param.Name.Lexeme);
        }

        // Analyze the function body
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                AnalyzeStmt(stmt);
            }
        }

        // Variables that need hoisting: declared before await AND used after await
        var hoistedLocals = new HashSet<string>(_variablesDeclaredBeforeAwait);
        hoistedLocals.IntersectWith(_variablesUsedAfterAwait);
        hoistedLocals.ExceptWith(parameters); // Parameters are tracked separately

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
            TryBlocks: tryBlocks
        );
    }

    private List<TryBlockInfo> BuildTryBlockInfoList()
    {
        var result = new List<TryBlockInfo>();
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
        _variablesDeclaredBeforeAwait.Clear();
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
}
