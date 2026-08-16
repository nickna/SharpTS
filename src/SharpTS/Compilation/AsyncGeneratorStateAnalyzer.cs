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
        List<TryBlockInfo> TryBlocks,
        List<Stmt.ForOf> ForOfLoopsWithSuspension,  // for...of loops containing yields/awaits that need enumerator hoisting
        // Per-binding storage names for block-scoped let/const declarations that shadow an enclosing
        // binding, keyed by declaration/reference AST node (see GeneratorBlockScopeRenamer, #766/#711).
        IReadOnlyDictionary<object, string>? BlockScopeRenames = null,
        // Capturing-arrow node → (captured source name → renamed storage) (#767, async-gen analog).
        // Pivots an inner arrow's captured field to a renamed shadow's storage. Null treated as empty.
        IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>>? BlockScopeCaptureRenames = null
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
    private readonly List<Stmt.ForOf> _forOfLoopsWithSuspension = [];  // for...of loops containing yields/awaits
    private readonly Stack<Stmt.ForOf> _forOfStack = new();  // Track nested for...of loops
    private readonly Dictionary<Stmt.ForOf, HashSet<string>> _variablesUsedInLoopBody = new();  // Variables used in each for...of body
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

    // Block-scope shadow renames for this function (#766). Maps a declaration/reference AST node to the
    // disambiguated storage name its binding uses; nodes absent from the map keep their source lexeme.
    private IReadOnlyDictionary<object, string> _renames = new Dictionary<object, string>();
    // Per-arrow capture-source pivot for renamed shadows captured by inner arrows (#767, async-gen analog).
    private IReadOnlyDictionary<object, IReadOnlyDictionary<string, string>> _captureRenames =
        new Dictionary<object, IReadOnlyDictionary<string, string>>();

    /// <summary>
    /// Translates a declaration/reference node's source lexeme to its disambiguated storage name (#766),
    /// or returns the lexeme unchanged when the binding is not a renamed shadow.
    /// </summary>
    private string StorageName(object node, string lexeme) =>
        _renames.TryGetValue(node, out var renamed) ? renamed : lexeme;

    /// <summary>
    /// Analyzes an async generator function to determine suspension points and hoisted variables.
    /// </summary>
    public AsyncGeneratorFunctionAnalysis Analyze(Stmt.Function func)
    {
        Reset();

        // Disambiguate block-scoped let/const declarations that shadow an enclosing binding so the
        // hoisting decision below is made per-binding rather than per-name (#766, async analog of #711).
        var renameResult = GeneratorBlockScopeRenamer.Compute(func);
        _renames = renameResult.Renames;
        _captureRenames = renameResult.CaptureRenames;

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
            TryBlocks: tryBlocks,
            ForOfLoopsWithSuspension: [.. _forOfLoopsWithSuspension],
            BlockScopeRenames: _renames,
            BlockScopeCaptureRenames: _captureRenames
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
        _forOfLoopsWithSuspension.Clear();
        _forOfStack.Clear();
        _variablesUsedInLoopBody.Clear();
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

    /// <summary>
    /// Reserves a synthetic await suspension point — one that has no <see cref="Expr.Await"/> node in the
    /// source. These back the implicit awaits of <c>for await…of</c> (the iterator's next()/return(), #697)
    /// and of <c>yield*</c> delegation to a genuinely-async iterator (the delegated iterator's next(), #688).
    /// The emitter consumes them, in the same traversal order, via <c>EmitAwaitFromValueOnStack</c>; this
    /// mirrors <c>AsyncStateAnalyzer.VisitForOf</c>'s <c>RecordAwaitPoint(null)</c> for async functions (#631).
    /// <see cref="SuspensionPoint.Expression"/> is left null and is never dereferenced — only
    /// <see cref="SuspensionPoint.StateNumber"/> is read (to define the resume label in MoveNextAsync).
    /// </summary>
    private void RecordSyntheticAwaitPoint()
    {
        var liveVars = new HashSet<string>(_declaredVariables);
        _suspensionPoints.Add(new SuspensionPoint(
            _stateCounter++,
            SuspensionType.Await,
            Expression: null,
            liveVars,
            IsDelegatingYield: false,
            TryBlockDepth: _currentTryBlockDepth,
            EnclosingTryId: _currentTryBlockId
        ));
        _seenSuspension = true;

        // Track suspension in try block (a for-await/delegating-yield inside a try makes that try suspend).
        RecordSuspensionInTryBlock();

        // Any for...of loop currently on the stack now contains a suspension and needs enumerator/variable
        // hoisting (its body re-executes across the suspension).
        foreach (var forOf in _forOfStack)
        {
            if (!_forOfLoopsWithSuspension.Contains(forOf))
                _forOfLoopsWithSuspension.Add(forOf);
        }
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
