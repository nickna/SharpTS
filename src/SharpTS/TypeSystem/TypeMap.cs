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
    private readonly HashSet<Token> _promotableStringAccumulators = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, ObjectShapeInfo> _promotableObjectLocals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Stmt.ForOf> _stableNumericMapIterations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.Get> _stablePrimitivePromiseThenCalls = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>
    /// Marks a direct <c>Promise.prototype.then</c> access whose receiver is a fresh,
    /// non-escaping intrinsic Promise binding and whose sole inline fulfillment
    /// callback has a statically primitive result. The compiler may skip species,
    /// dynamic callback-shape, and thenable-adoption machinery for this exact call.
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
