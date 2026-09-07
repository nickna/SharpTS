using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Stores the resolved TypeInfo for each expression in the AST.
/// Built by TypeChecker during static analysis, consumed by ILCompiler and Interpreter.
/// </summary>
/// <remarks>
/// Uses ReferenceEqualityComparer because C# records use structural equality by default.
/// Two Expr.Literal(42) instances would otherwise be considered equal even if they
/// appear at different locations in the AST.
/// </remarks>
public class TypeMap
{
    private readonly Dictionary<Expr, TypeInfo> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, TypeInfo.Class> _classTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeInfo.Function> _functionTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<Expr.ClassExpr, TypeInfo.Class> _classExprTypes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _undefinedReachableReturns = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _undefinedReachableNumericLocals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Parameter> _undefinedReachableNumericParams = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, TokenType> _promotableArrayLocals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> _promotableNumericMapLocals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> _promotableStringAccumulators = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, ObjectShapeInfo> _promotableObjectLocals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.Call, ObjectConsumerInfo> _promotedObjectCalls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, ClassScalarReplacementInfo> _scalarReplaceableClassLocals =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.ForOf> _stableNumericMapIterations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Stmt.ForOf, StableCustomIteratorInfo> _stableCustomIteratorLoops =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, StableCustomIteratorInfo> _stableCustomIteratorNextMethods =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Get> _stablePrimitivePromiseThenCalls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, HashSet<string>> _stableNumericCaptureFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<string>> _stableNumericFunctionCaptureFields = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> _stableCustomIteratorNumericAccumulators = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Var> _stableNumericStateMachineLocals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.Parameter> _stableNumericStateMachineParameters = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Get> _stableExactPrimitiveMethodCalls = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _stablePrimitivePromiseAllIterables = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.ArrayLiteral> _stablePrimitivePromiseAllInputInitializers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Variable> _stablePrimitivePromiseAllPushReceivers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr> _stablePrimitivePromiseAllSeedValues = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Variable> _stablePrimitivePromiseAllResultUses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Variable> _stableTypedArrayBackingReceivers = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Associates an expression with its resolved type.
    /// </summary>
    public void Set(Expr expr, TypeInfo type) => _types[expr] = type;

    /// <summary>
    /// Registers a class type by name for later lookup during compilation.
    /// </summary>
    public void SetClassType(string className, TypeInfo.Class classType) => _classTypes[className] = classType;

    /// <summary>
    /// Gets the class type by name, or null if not found.
    /// </summary>
    public TypeInfo.Class? GetClassType(string className) => _classTypes.GetValueOrDefault(className);

    /// <summary>
    /// All registered class types, keyed by (simple) class name. Used to walk the inheritance graph
    /// — e.g. to find every override of a method so the compiler can give them a hierarchy-consistent
    /// CLR signature (override-safe value-type default-parameter widening, #737).
    /// </summary>
    public IReadOnlyDictionary<string, TypeInfo.Class> ClassTypes => _classTypes;

    /// <summary>
    /// Registers a class expression type by expression reference for IL compiler lookup.
    /// </summary>
    public void SetClassExprType(Expr.ClassExpr expr, TypeInfo.Class classType) => _classExprTypes[expr] = classType;

    /// <summary>
    /// Registers a top-level function type by name.
    /// </summary>
    public void SetFunctionType(string functionName, TypeInfo.Function functionType) => _functionTypes[functionName] = functionType;

    /// <summary>
    /// Gets the function type by name, or null if not found.
    /// </summary>
    public TypeInfo.Function? GetFunctionType(string functionName) => _functionTypes.GetValueOrDefault(functionName);

    /// <summary>
    /// Marks a return value expression as one that flows into a <c>number</c>/<c>boolean</c>
    /// declared return type but whose static type (<c>any</c>/<c>unknown</c>) does not exclude
    /// the runtime <c>undefined</c> sentinel (e.g. <c>return undefined as any</c>). The IL
    /// compiler consults this to widen the otherwise-unboxed <c>double</c>/<c>bool</c> return
    /// slot back to <c>object</c> for just those functions, so a legitimate <c>undefined</c>
    /// is not silently coerced to <c>NaN</c>/<c>false</c>. Purely a compiler hint — caller-side
    /// type checking still sees the clean <c>number</c>/<c>boolean</c> return type. (#344)
    /// </summary>
    public void MarkUndefinedReachableReturn(Expr returnValue) => _undefinedReachableReturns.Add(returnValue);

    /// <summary>
    /// True if <paramref name="returnValue"/> was flagged by
    /// <see cref="MarkUndefinedReachableReturn"/>.
    /// </summary>
    public bool IsUndefinedReachableReturn(Expr returnValue) => _undefinedReachableReturns.Contains(returnValue);

    /// <summary>
    /// Flags a <c>number</c>-typed local variable declaration whose value may be the runtime
    /// <c>undefined</c> sentinel because an <c>any</c>/<c>undefined</c> value was (transitively)
    /// assigned to it (#367). Without this the IL compiler would give the local an unboxed
    /// <c>double</c> slot, coercing the sentinel to <c>NaN</c> at the store — so it must use an
    /// object slot instead. Keyed by reference, by either the declaration <see cref="Stmt"/> node
    /// or its initializer <see cref="Expr"/> (the compiler synthesizes a fresh <c>Stmt.Var</c> for
    /// <c>const</c> but reuses the original initializer expression, so both are recorded). Purely a
    /// compiler hint — caller-side type checking still sees the clean <c>number</c> type.
    /// </summary>
    public void MarkUndefinedReachableNumericLocal(object declOrInitializer) =>
        _undefinedReachableNumericLocals.Add(declOrInitializer);

    /// <summary>
    /// True if <paramref name="declOrInitializer"/> was flagged by
    /// <see cref="MarkUndefinedReachableNumericLocal"/>.
    /// </summary>
    public bool IsUndefinedReachableNumericLocal(object declOrInitializer) =>
        _undefinedReachableNumericLocals.Contains(declOrInitializer);

    /// <summary>
    /// Flags a <c>number</c>/<c>boolean</c>-typed <em>parameter</em> that an <c>any</c>/<c>undefined</c>
    /// value may have been (transitively) assigned in the body, leaving it holding the runtime
    /// <c>undefined</c> sentinel (#372 — the parameter analogue of <see cref="MarkUndefinedReachableNumericLocal"/>).
    /// A <c>: number</c> parameter compiles to an unboxed <c>double</c> arg slot (a <c>: boolean</c> to a
    /// <c>bool</c> slot) which cannot carry the sentinel — storing it coerces to <c>NaN</c>/<c>false</c>
    /// (or, for a never-initialized slot, raw garbage). The compiler's parameter resolver consults this
    /// to widen just those parameter slots back to <c>object</c>. Keyed by reference on the
    /// <see cref="Stmt.Parameter"/> node. Purely a compiler hint — caller-side checking still sees the
    /// clean <c>number</c>/<c>boolean</c> parameter type.
    /// </summary>
    public void MarkUndefinedReachableNumericParam(Stmt.Parameter param) =>
        _undefinedReachableNumericParams.Add(param);

    /// <summary>
    /// True if <paramref name="param"/> was flagged by <see cref="MarkUndefinedReachableNumericParam"/>.
    /// </summary>
    public bool IsUndefinedReachableNumericParam(Stmt.Parameter param) =>
        _undefinedReachableNumericParams.Contains(param);

    /// <summary>
    /// Flags a <c>number[]</c>/<c>boolean[]</c>-typed local <c>const</c>/<c>let</c> declaration whose
    /// initializer is an empty array literal and which is provably non-escaping (only used via
    /// index get/set, <c>.length</c>, and <c>push</c>/<c>pop</c>). The compiler promotes such a local
    /// to a concrete <c>List&lt;double&gt;</c>/<c>List&lt;bool&gt;</c> CLR slot with unboxed element access
    /// (#857/#860), instead of the default <c>object</c>/<c>$Array</c> slot. <paramref name="elementToken"/>
    /// is the element primitive token (<c>TYPE_NUMBER</c> or <c>TYPE_BOOLEAN</c>) so the compiler can pick the
    /// backing list type without re-deriving it. Keyed by reference on the declaration's <em>name token</em>
    /// (stable across both <c>Stmt.Var</c> and <c>Stmt.Const</c> — the latter is re-wrapped into a synthetic
    /// <c>Stmt.Var</c> at emit time but reuses the same name <see cref="Token"/>). Purely a compiler hint —
    /// set by the IL compiler's promotion analyzer, not by the type checker.
    /// </summary>
    public void MarkPromotableArrayLocal(Token nameToken, TokenType elementToken) =>
        _promotableArrayLocals[nameToken] = elementToken;

    /// <summary>
    /// If the declaration with name token <paramref name="nameToken"/> was flagged by
    /// <see cref="MarkPromotableArrayLocal"/>, returns true and sets <paramref name="elementToken"/> to the
    /// element primitive token; otherwise false.
    /// </summary>
    public bool IsPromotableArrayLocal(Token nameToken, out TokenType elementToken) =>
        _promotableArrayLocals.TryGetValue(nameToken, out elementToken);

    private readonly HashSet<Token> _promotableQueueLocals = new(ReferenceEqualityComparer.Instance);
    public void MarkPromotableQueueLocal(Token nameToken) => _promotableQueueLocals.Add(nameToken);
    public bool IsPromotableQueueLocal(Token nameToken) => _promotableQueueLocals.Contains(nameToken);
    private readonly HashSet<Token> _queueLocalsWithWrites = new(ReferenceEqualityComparer.Instance);
    public void MarkQueueLocalWithWrites(Token nameToken) => _queueLocalsWithWrites.Add(nameToken);
    public bool QueueLocalHasWrites(Token nameToken) => _queueLocalsWithWrites.Contains(nameToken);

    private readonly HashSet<Token> _stableNumericSliceSortReceivers =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Records the exact receiver occurrence of a discarded
    /// <c>freshNumericSlice.sort((a, b) =&gt; a - b)</c> call after whole-function
    /// analysis proves that the slice local neither escapes nor aliases.
    /// </summary>
    public void MarkStableNumericSliceSortReceiver(Token receiverToken) =>
        _stableNumericSliceSortReceivers.Add(receiverToken);

    /// <summary>True only for the receiver occurrence approved by the analyzer.</summary>
    public bool IsStableNumericSliceSortReceiver(Token receiverToken) =>
        _stableNumericSliceSortReceivers.Contains(receiverToken);

    /// <summary>
    /// Marks an indexed receiver use whose binding belongs to a fresh, exact, non-escaping numeric
    /// TypedArray local. The IL compiler may cache that receiver's backing storage around a loop.
    /// Keying the actual receiver node keeps the whole-program proof scope-correct under shadowing.
    /// </summary>
    public void MarkStableTypedArrayBackingReceiver(Expr.Variable receiver) =>
        _stableTypedArrayBackingReceivers.Add(receiver);

    public bool IsStableTypedArrayBackingReceiver(Expr.Variable receiver) =>
        _stableTypedArrayBackingReceivers.Contains(receiver);

    /// <summary>
    /// Marks a fresh, exact, non-escaping <c>Map&lt;number, number&gt;</c> function local
    /// whose complete lifetime is limited to direct numeric operations. The IL
    /// compiler may represent it as <c>Dictionary&lt;double, double&gt;</c>. Keyed by
    /// the declaration name token so a synthetic <see cref="Stmt.Var"/> emitted
    /// for <see cref="Stmt.Const"/> retains the proof.
    /// </summary>
    public void MarkPromotableNumericMapLocal(Token nameToken) =>
        _promotableNumericMapLocals.Add(nameToken);

    public bool IsPromotableNumericMapLocal(Token nameToken) =>
        _promotableNumericMapLocals.Contains(nameToken);

    /// <summary>
    /// Flags a <c>const</c>/<c>let</c> string local with a string-literal initializer that is provably
    /// non-escaping and used only via append (<c>s = s + str</c>/<c>s += str</c> in statement position),
    /// <c>s.length</c>, and <c>s.charCodeAt(i)</c>. The IL compiler promotes such a local to a concrete
    /// <c>StringBuilder</c> slot (#857), turning O(n²) repeated <c>String.Concat</c> into O(n) <c>Append</c>.
    /// Keyed by reference on the declaration's <em>name token</em> (stable across <c>Stmt.Var</c>/
    /// <c>Stmt.Const</c>). Purely a compiler hint — set by the IL compiler's promotion analyzer.
    /// </summary>
    public void MarkPromotableStringAccumulator(Token nameToken) =>
        _promotableStringAccumulators.Add(nameToken);

    /// <summary>
    /// True if the declaration with name token <paramref name="nameToken"/> was flagged by
    /// <see cref="MarkPromotableStringAccumulator"/>.
    /// </summary>
    public bool IsPromotableStringAccumulator(Token nameToken) =>
        _promotableStringAccumulators.Contains(nameToken);

    /// <summary>
    /// Flags a <c>const</c>/<c>let</c> object-literal local declaration whose literal has a fixed,
    /// statically-known primitive shape and which is provably non-escaping (only used via constant-key
    /// field read/write). The IL compiler promotes such a local to a generated value-type "shape" struct
    /// with typed fields (#862) instead of the default <c>Dictionary&lt;string, object&gt;</c>. Keyed by
    /// reference on the declaration's <em>name token</em> (stable across both <c>Stmt.Var</c> and
    /// <c>Stmt.Const</c>). Purely a compiler hint — set by the IL compiler's promotion analyzer, not by
    /// the type checker.
    /// </summary>
    public void MarkPromotableObjectLocal(Token nameToken, ObjectShapeInfo shape) =>
        _promotableObjectLocals[nameToken] = shape;

    /// <summary>
    /// If the declaration with name token <paramref name="nameToken"/> was flagged by
    /// <see cref="MarkPromotableObjectLocal"/>, returns true and sets <paramref name="shape"/> to its
    /// shape; otherwise false.
    /// </summary>
    public bool IsPromotableObjectLocal(Token nameToken, out ObjectShapeInfo shape) =>
        _promotableObjectLocals.TryGetValue(nameToken, out shape!);

    /// <summary>
    /// All distinct shapes flagged by <see cref="MarkPromotableObjectLocal"/> (one entry per marked
    /// local; the IL compiler de-duplicates by <see cref="ObjectShapeInfo.CanonicalKey"/> when defining
    /// the generated types). Empty when no object local was promoted.
    /// </summary>
    public IEnumerable<ObjectShapeInfo> PromotableObjectLocalShapes => _promotableObjectLocals.Values;

    public void MarkPromotedObjectCall(Expr.Call call, ObjectConsumerInfo summary) => _promotedObjectCalls[call] = summary;
    public bool TryGetPromotedObjectCall(Expr.Call call, out ObjectConsumerInfo summary) =>
        _promotedObjectCalls.TryGetValue(call, out summary!);

    /// <summary>
    /// Marks a fresh exact-class local whose allocation and pure constructor may be
    /// represented by the same generated typed shape used for promoted object
    /// literals. Registering the shape here also makes it available to the common
    /// shape-type definition phase.
    /// </summary>
    public void MarkScalarReplaceableClassLocal(
        Token nameToken,
        ClassScalarReplacementInfo info)
    {
        _scalarReplaceableClassLocals[nameToken] = info;
        _promotableObjectLocals[nameToken] = info.Shape;
    }

    public bool IsScalarReplaceableClassLocal(
        Token nameToken,
        out ClassScalarReplacementInfo info) =>
        _scalarReplaceableClassLocals.TryGetValue(nameToken, out info!);

    /// <summary>
    /// Marks a <c>for...of</c> over a fresh, non-escaping <c>Map&lt;number, number&gt;</c>
    /// whose entry binding is observed only through literal <c>[0]</c>/<c>[1]</c> reads.
    /// The compiler may lower this shape directly over the backing dictionary without
    /// materializing JavaScript entry arrays. This is a compiler hint only; the public
    /// TypeScript type remains the ordinary Map iterator type.
    /// </summary>
    public void MarkStableNumericMapIteration(Stmt.ForOf loop) =>
        _stableNumericMapIterations.Add(loop);

    /// <summary>
    /// True when <paramref name="loop"/> satisfies the non-escape and entry-use proof
    /// recorded by the IL compiler's stable Map iteration analyzer.
    /// </summary>
    public bool IsStableNumericMapIteration(Stmt.ForOf loop) =>
        _stableNumericMapIterations.Contains(loop);

    public void MarkStableCustomIterator(
        Stmt.ForOf loop, StableCustomIteratorInfo info)
    {
        _stableCustomIteratorLoops[loop] = info;
        _stableCustomIteratorNextMethods[info.NextMethod] = info;
    }

    public bool TryGetStableCustomIterator(
        Stmt.ForOf loop, out StableCustomIteratorInfo info) =>
        _stableCustomIteratorLoops.TryGetValue(loop, out info!);

    public bool TryGetStableCustomIteratorNext(
        Expr.ArrowFunction method, out StableCustomIteratorInfo info) =>
        _stableCustomIteratorNextMethods.TryGetValue(method, out info!);

    /// <summary>
    /// Marks a direct <c>Promise.prototype.then</c> access whose receiver is a fresh,
    /// non-escaping intrinsic Promise binding and whose inline callback results are
    /// statically primitive. The compiler may skip species, dynamic callback-shape,
    /// and thenable-adoption machinery for this exact call.
    /// </summary>
    public void MarkStablePrimitivePromiseThen(Expr.Get method) =>
        _stablePrimitivePromiseThenCalls.Add(method);

    /// <summary>
    /// True when <paramref name="method"/> satisfies the whole-program stability and
    /// primitive-result proof recorded by the Promise.then analyzer.
    /// </summary>
    public bool IsStablePrimitivePromiseThen(Expr.Get method) =>
        _stablePrimitivePromiseThenCalls.Contains(method);

    /// <summary>
    /// Marks a captured binding whose value-snapshot field may use an unboxed
    /// <c>double</c>. Keying by the exact arrow node keeps same-named bindings in
    /// unrelated scopes independent.
    /// </summary>
    public void MarkStableNumericCaptureField(Expr.ArrowFunction arrow, string name)
    {
        if (!_stableNumericCaptureFields.TryGetValue(arrow, out var names))
            _stableNumericCaptureFields[arrow] = names = [];
        names.Add(name);
    }

    public bool IsStableNumericCaptureField(Expr.ArrowFunction arrow, string name) =>
        _stableNumericCaptureFields.TryGetValue(arrow, out var names)
        && names.Contains(name);

    /// <summary>
    /// Marks a function-owned captured numeric binding whose shared display-class
    /// slot can remain an unboxed <c>double</c>. The numeric function capture proof
    /// requires initialization before closure creation, an unambiguous binding,
    /// a single capturing callable, and numeric values on every write.
    /// </summary>
    public void MarkStableNumericFunctionCaptureField(object callable, string name)
    {
        if (!_stableNumericFunctionCaptureFields.TryGetValue(callable, out var names))
            _stableNumericFunctionCaptureFields[callable] = names = [];
        names.Add(name);
    }

    public bool IsStableNumericFunctionCaptureField(object callable, string name) =>
        _stableNumericFunctionCaptureFields.TryGetValue(callable, out var names)
        && names.Contains(name);

    public void MarkStableCustomIteratorNumericAccumulator(Token name) =>
        _stableCustomIteratorNumericAccumulators.Add(name);

    public bool IsStableCustomIteratorNumericAccumulator(Token name) =>
        _stableCustomIteratorNumericAccumulators.Contains(name);

    /// <summary>
    /// Marks an explicitly numeric local whose complete state-machine lifetime has
    /// been proven to remain numeric and suspension-local. The state-machine emitter
    /// may keep the binding in an unboxed <c>double</c> local.
    /// </summary>
    public void MarkStableNumericStateMachineLocal(Stmt.Var declaration) =>
        _stableNumericStateMachineLocals.Add(declaration);

    public bool IsStableNumericStateMachineLocal(Stmt.Var declaration) =>
        _stableNumericStateMachineLocals.Contains(declaration);

    public void MarkStableNumericStateMachineParameter(Stmt.Parameter parameter) =>
        _stableNumericStateMachineParameters.Add(parameter);

    public bool IsStableNumericStateMachineParameter(Stmt.Parameter parameter) =>
        _stableNumericStateMachineParameters.Contains(parameter);

    /// <summary>
    /// Marks a class method access whose receiver is provably the exact instance
    /// created for a stable local binding (or an immediate <c>new C().m()</c>).
    /// The compiler may target a private typed method companion without changing
    /// the public virtual method ABI used by uncertain and value-backed calls.
    /// </summary>
    public void MarkStableExactPrimitiveMethodCall(Expr.Get method) =>
        _stableExactPrimitiveMethodCalls.Add(method);

    public bool IsStableExactPrimitiveMethodCall(Expr.Get method) =>
        _stableExactPrimitiveMethodCalls.Contains(method);

    /// <summary>
    /// Marks the exact iterable expression of a proven fresh, non-escaping
    /// <c>Promise&lt;number&gt;[]</c> consumed once by intrinsic <c>Promise.all</c>.
    /// The compiler may return its result in the internal unboxed numeric-list
    /// representation and omit per-element own-<c>then</c> probes.
    /// </summary>
    public void MarkStablePrimitivePromiseAllIterable(Expr iterable) =>
        _stablePrimitivePromiseAllIterables.Add(iterable);

    public bool IsStablePrimitivePromiseAllIterable(Expr iterable) =>
        _stablePrimitivePromiseAllIterables.Contains(iterable);

    /// <summary>
    /// Marks the empty literal backing the proven non-escaping Promise.all input.
    /// It may use the private typed numeric-list carrier instead of a $Array.
    /// </summary>
    public void MarkStablePrimitivePromiseAllInputInitializer(Expr.ArrayLiteral initializer) =>
        _stablePrimitivePromiseAllInputInitializers.Add(initializer);

    public bool IsStablePrimitivePromiseAllInputInitializer(Expr.ArrayLiteral initializer) =>
        _stablePrimitivePromiseAllInputInitializers.Contains(initializer);

    /// <summary>
    /// Marks an exact permitted push receiver for that private typed input.
    /// </summary>
    public void MarkStablePrimitivePromiseAllPushReceiver(Expr.Variable receiver) =>
        _stablePrimitivePromiseAllPushReceivers.Add(receiver);

    public bool IsStablePrimitivePromiseAllPushReceiver(Expr.Variable receiver) =>
        _stablePrimitivePromiseAllPushReceivers.Contains(receiver);

    /// <summary>
    /// Marks the numeric argument of an intrinsic <c>Promise.resolve</c> whose
    /// result is stored only in a proven stable primitive Promise.all input.
    /// The compiler may carry the unboxed value in that private list instead of
    /// materializing an otherwise-unobservable completed Task.
    /// </summary>
    public void MarkStablePrimitivePromiseAllSeedValue(Expr value) =>
        _stablePrimitivePromiseAllSeedValues.Add(value);

    public bool IsStablePrimitivePromiseAllSeedValue(Expr value) =>
        _stablePrimitivePromiseAllSeedValues.Contains(value);

    /// <summary>
    /// Marks a permitted length/index receiver use of the non-escaping numeric
    /// result produced by a stable primitive <c>Promise.all</c> call.
    /// </summary>
    public void MarkStablePrimitivePromiseAllResultUse(Expr.Variable variable) =>
        _stablePrimitivePromiseAllResultUses.Add(variable);

    public bool IsStablePrimitivePromiseAllResultUse(Expr.Variable variable) =>
        _stablePrimitivePromiseAllResultUses.Contains(variable);

    /// <summary>
    /// Gets the resolved type for an expression, or null if not found.
    /// </summary>
    public TypeInfo? Get(Expr expr) => _types.GetValueOrDefault(expr);

    /// <summary>
    /// Tries to get the resolved type for an expression.
    /// </summary>
    public bool TryGet(Expr expr, out TypeInfo? type) => _types.TryGetValue(expr, out type);

    /// <summary>
    /// Checks if the expression is typed as a string.
    /// </summary>
    public bool IsString(Expr expr) => Get(expr) is TypeInfo.String or TypeInfo.StringLiteral;

    /// <summary>
    /// Checks if the expression is typed as an array.
    /// </summary>
    public bool IsArray(Expr expr) => Get(expr) is TypeInfo.Array;

    /// <summary>
    /// Checks if the expression is typed as a number.
    /// </summary>
    public bool IsNumber(Expr expr) => Get(expr) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER };

    /// <summary>
    /// Checks if the expression is typed as a boolean.
    /// </summary>
    public bool IsBoolean(Expr expr) => Get(expr) is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN };

    /// <summary>
    /// Returns the number of expressions with resolved types.
    /// </summary>
    public int Count => _types.Count;
}
