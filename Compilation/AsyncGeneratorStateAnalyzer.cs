using SharpTS.Compilation.Visitors;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes async generator functions to identify both yield and await points,
/// as well as variables that must be hoisted to the state machine struct.
/// Combines the analysis patterns from GeneratorStateAnalyzer and AsyncStateAnalyzer.
/// </summary>
public partial class AsyncGeneratorStateAnalyzer : AstVisitorBase
{
    /// <summary>
    /// The type of suspension point in an async generator.
    /// </summary>
    public enum SuspensionType { Yield, Await }

    /// <summary>
    /// Represents a single suspension point (yield or await) in an async generator function.
    /// </summary>
    public record SuspensionPoint(
        int StateNumber,
        SuspensionType Type,
        Expr? Expression,             // Yield.Value or Await expression
        HashSet<string> LiveVariables,
        bool IsDelegatingYield = false,  // For yield* expressions
        int TryBlockDepth = 0,           // For await in try blocks
        int? EnclosingTryId = null       // For await in try blocks
    );

    /// <summary>
    /// Represents a try/catch/finally block in an async generator function.
    /// </summary>
    public record TryBlockInfo(
        int TryId,
        Stmt.TryCatch TryStatement,
        bool HasSuspensionsInTry,
        bool HasSuspensionsInCatch,
        bool HasSuspensionsInFinally,
        int? ParentTryId
    );

    /// <summary>
    /// Complete analysis results for an async generator function.
    /// </summary>
    public record AsyncGeneratorFunctionAnalysis(
        int SuspensionPointCount,
        List<SuspensionPoint> SuspensionPoints,
        HashSet<string> HoistedLocals,
        HashSet<string> HoistedParameters,
        bool UsesThis,
        bool HasYieldStar,
        bool HasTryCatch,
        List<TryBlockInfo> TryBlocks
    )
    {
        /// <summary>
        /// Gets only the yield suspension points.
        /// </summary>
        public IEnumerable<SuspensionPoint> YieldPoints =>
            SuspensionPoints.Where(p => p.Type == SuspensionType.Yield);

        /// <summary>
        /// Gets only the await suspension points.
        /// </summary>
        public IEnumerable<SuspensionPoint> AwaitPoints =>
            SuspensionPoints.Where(p => p.Type == SuspensionType.Await);

        /// <summary>
        /// Whether any try block contains suspension points (requires special handling).
        /// </summary>
        public bool HasSuspensionsInTryBlocks =>
            TryBlocks?.Any(t => t.HasSuspensionsInTry || t.HasSuspensionsInCatch || t.HasSuspensionsInFinally) ?? false;
    }

    // State during analysis
    private readonly List<SuspensionPoint> _suspensionPoints = [];
    private readonly HashSet<string> _declaredVariables = [];
    private readonly HashSet<string> _variablesUsedAfterSuspension = [];
    private readonly HashSet<string> _variablesDeclaredBeforeSuspension = [];
    private readonly List<TryBlockInfo> _tryBlocks = [];
    private int _stateCounter = 0;
    private bool _seenSuspension = false;
    private bool _usesThis = false;
    private bool _hasYieldStar = false;
    private bool _hasTryCatch = false;

    // Try block tracking
    private int _tryBlockCounter = 0;
    private int _currentTryBlockDepth = 0;
    private int? _currentTryBlockId = null;
    private readonly Stack<int> _tryBlockIdStack = new();

    // Track which region (try/catch/finally) we're currently in
    private enum TryRegion { None, Try, Catch, Finally }
    private TryRegion _currentTryRegion = TryRegion.None;
    private readonly Dictionary<int, (bool InTry, bool InCatch, bool InFinally)> _tryBlockSuspensionFlags = [];

    // Reusable visitor for analyzing captures
    private readonly CaptureAnalysisVisitor _captureVisitor = new();

    /// <summary>
    /// Analyzes an async generator function to determine suspension points and hoisted variables.
    /// </summary>
    public AsyncGeneratorFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Collect parameters as variables that need hoisting
        HashSet<string> parameters = [];
        foreach (var param in func.Parameters)
        {
            parameters.Add(param.Name.Lexeme);
            _declaredVariables.Add(param.Name.Lexeme);
            _variablesDeclaredBeforeSuspension.Add(param.Name.Lexeme);
        }

        // Analyze the function body using visitor pattern
        if (func.Body != null)
        {
            foreach (var stmt in func.Body)
            {
                Visit(stmt);
            }
        }

        // Variables that need hoisting: any local variable used after a suspension point
        // (because the value must persist across the suspension)
        var hoistedLocals = new HashSet<string>(_declaredVariables);
        hoistedLocals.IntersectWith(_variablesUsedAfterSuspension);
        hoistedLocals.ExceptWith(parameters); // Parameters are tracked separately

        // Build TryBlockInfo list from collected data
        var tryBlocks = BuildTryBlockInfoList();

        return new AsyncGeneratorFunctionAnalysis(
            SuspensionPointCount: _suspensionPoints.Count,
            SuspensionPoints: [.. _suspensionPoints],
            HoistedLocals: hoistedLocals,
            HoistedParameters: parameters,
            UsesThis: _usesThis,
            HasYieldStar: _hasYieldStar,
            HasTryCatch: _hasTryCatch,
            TryBlocks: tryBlocks
        );
    }

    private List<TryBlockInfo> BuildTryBlockInfoList()
    {
        List<TryBlockInfo> result = [];
        foreach (var (tryId, flags) in _tryBlockSuspensionFlags)
        {
            var existingInfo = _tryBlocks.FirstOrDefault(t => t.TryId == tryId);
            if (existingInfo != null)
            {
                result.Add(existingInfo with
                {
                    HasSuspensionsInTry = flags.InTry,
                    HasSuspensionsInCatch = flags.InCatch,
                    HasSuspensionsInFinally = flags.InFinally
                });
            }
        }
        // Add try blocks without suspensions
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
        _suspensionPoints.Clear();
        _declaredVariables.Clear();
        _variablesUsedAfterSuspension.Clear();
        _variablesDeclaredBeforeSuspension.Clear();
        _tryBlocks.Clear();
        _stateCounter = 0;
        _seenSuspension = false;
        _usesThis = false;
        _hasYieldStar = false;
        _hasTryCatch = false;
        _tryBlockCounter = 0;
        _currentTryBlockDepth = 0;
        _currentTryBlockId = null;
        _tryBlockIdStack.Clear();
        _currentTryRegion = TryRegion.None;
        _tryBlockSuspensionFlags.Clear();
    }

    private void RecordSuspensionInTryBlock()
    {
        if (_currentTryBlockId.HasValue && _currentTryRegion != TryRegion.None)
        {
            var tryId = _currentTryBlockId.Value;
            if (!_tryBlockSuspensionFlags.TryGetValue(tryId, out var flags))
            {
                flags = (false, false, false);
            }
            flags = _currentTryRegion switch
            {
                TryRegion.Try => (true, flags.InCatch, flags.InFinally),
                TryRegion.Catch => (flags.InTry, true, flags.InFinally),
                TryRegion.Finally => (flags.InTry, flags.InCatch, true),
                _ => flags
            };
            _tryBlockSuspensionFlags[tryId] = flags;
        }
    }
}
