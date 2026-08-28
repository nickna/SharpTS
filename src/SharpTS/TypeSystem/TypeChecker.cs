using System.Collections.Frozen;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Static type analyzer that validates the AST before execution.
/// </summary>
/// <remarks>
/// Third stage of the compiler pipeline. Traverses the AST from <see cref="Parser"/> and
/// validates type compatibility, function signatures, class inheritance, and interface
/// implementations. Uses <see cref="TypeEnvironment"/> for scope tracking and <see cref="TypeInfo"/>
/// records for type representations. Supports both structural typing (interfaces) and nominal
/// typing (classes). Type checking runs at compile-time, completely separate from runtime
/// execution. Errors throw exceptions with "Type Error:" prefix.
///
/// Dispatches to all AST node types through the <see cref="NodeRegistry{TContext, TExprResult, TStmtResult}"/>,
/// which ensures compile-time safety when new expression or statement types are added.
///
/// This class is split across partial files:
/// <list type="bullet">
///   <item><description><c>TypeChecker.cs</c> - Core infrastructure, fields, entry points, module helpers</description></item>
///   <item><description><c>TypeChecker.Statements.cs</c> - Main CheckStmt dispatch and simple statement handlers</description></item>
///   <item><description><c>TypeChecker.Statements.Classes.cs</c> - Class declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Interfaces.cs</c> - Interface declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Functions.cs</c> - Function declaration and overload handling</description></item>
///   <item><description><c>TypeChecker.Statements.Enums.cs</c> - Enum declaration with const enum support</description></item>
///   <item><description><c>TypeChecker.Statements.ControlFlow.cs</c> - Block, switch, try/catch checking</description></item>
///   <item><description><c>TypeChecker.Statements.Modules.cs</c> - Export statement checking</description></item>
///   <item><description><c>TypeChecker.Expressions.cs</c> - Expression checking (CheckExpr, literals, arrays, objects, arrow functions)</description></item>
///   <item><description><c>TypeChecker.Properties.cs</c> - Property access (CheckGet, CheckSet, CheckNew, CheckThis, CheckSuper, indexing)</description></item>
///   <item><description><c>TypeChecker.Calls.cs</c> - Function calls (CheckCall, overload resolution)</description></item>
///   <item><description><c>TypeChecker.Operators.cs</c> - Operators (binary, unary, logical, compound assignment)</description></item>
///   <item><description><c>TypeChecker.Compatibility.cs</c> - Type compatibility core (IsCompatible, IsCompatibleCore, caching)</description></item>
///   <item><description><c>TypeChecker.Compatibility.Helpers.cs</c> - Type predicates and class accessors</description></item>
///   <item><description><c>TypeChecker.Compatibility.TypeGuards.cs</c> - Control-flow type narrowing</description></item>
///   <item><description><c>TypeChecker.Compatibility.Structural.cs</c> - Duck typing and member access</description></item>
///   <item><description><c>TypeChecker.Compatibility.Tuples.cs</c> - Tuple and array compatibility</description></item>
///   <item><description><c>TypeChecker.Compatibility.Callable.cs</c> - Callable/constructable interface matching</description></item>
///   <item><description><c>TypeChecker.Compatibility.TemplateLiterals.cs</c> - Template literal pattern matching</description></item>
///   <item><description><c>TypeChecker.Generics.cs</c> - Generic types (instantiation, substitution, type inference)</description></item>
///   <item><description><c>TypeChecker.TypeParsing.cs</c> - Type string parsing (ToTypeInfo, union/intersection/tuple/function parsing)</description></item>
///   <item><description><c>TypeChecker.Validation.cs</c> - Validation (interface implementation, abstract members, override checking)</description></item>
/// </list>
/// </remarks>
/// <seealso cref="TypeEnvironment"/>
/// <seealso cref="TypeInfo"/>
public partial class TypeChecker
{
    private TypeEnvironment _environment = new();
    private TypeMap _typeMap = new();
    private SourceDocument? _standaloneSourceDocument;

    /// <summary>
    /// Semantic declaration identities and use-to-declaration bindings produced by the most recent
    /// check.
    /// </summary>
    public BindingIndex Bindings { get; } = new();

    private SourceDocument? CurrentSourceDocument =>
        _currentModule?.Document ?? _standaloneSourceDocument;

    private void DeclareValue(
        TypeEnvironment environment,
        Token declaration,
        TypeInfo type,
        bool mergeWithLocal = false)
    {
        BindingSymbol symbol = RegisterValueDeclaration(
            environment,
            declaration,
            mergeWithLocal);
        environment.Define(declaration.Lexeme, type);
        environment.DefineValueBinding(declaration.Lexeme, symbol);
    }

    private BindingSymbol RegisterValueDeclaration(
        TypeEnvironment environment,
        Token declaration,
        bool mergeWithLocal = false)
    {
        BindingSymbol? existing = mergeWithLocal
            ? environment.GetLocalValueBinding(declaration.Lexeme)
            : null;
        BindingSymbol symbol = Bindings.Declare(
            declaration,
            CurrentSourceDocument,
            BindingNamespace.Value,
            existing);
        environment.DefineValueBinding(declaration.Lexeme, symbol);
        return symbol;
    }

    private void DeclareValue(Token declaration, TypeInfo type, bool mergeWithLocal = false) =>
        DeclareValue(_environment, declaration, type, mergeWithLocal);

    private BindingSymbol RegisterValueDeclaration(
        Token declaration,
        bool mergeWithLocal = false) =>
        RegisterValueDeclaration(_environment, declaration, mergeWithLocal);

    private void BindValueUse(Token use)
    {
        if (_environment.GetValueBinding(use.Lexeme) is { } symbol)
            Bindings.Bind(use, CurrentSourceDocument, symbol);
    }

    private BindingSymbol RegisterTypeDeclaration(
        TypeEnvironment environment,
        Token declaration,
        bool mergeWithLocal = false)
    {
        BindingSymbol? existing = mergeWithLocal
            ? environment.GetLocalTypeSymbol(declaration.Lexeme)
            : null;
        BindingSymbol symbol = Bindings.Declare(
            declaration,
            CurrentSourceDocument,
            BindingNamespace.Type,
            existing);
        environment.DefineTypeBinding(declaration.Lexeme, symbol);
        return symbol;
    }

    private BindingSymbol RegisterTypeDeclaration(
        Token declaration,
        bool mergeWithLocal = false) =>
        RegisterTypeDeclaration(_environment, declaration, mergeWithLocal);

    private void DefineSourceTypeParameter(
        TypeEnvironment environment,
        TypeParam declaration,
        TypeInfo typeParameter)
    {
        BindingSymbol symbol = Bindings.Declare(
            declaration.Name,
            CurrentSourceDocument,
            BindingNamespace.Type,
            environment.GetLocalTypeSymbol(declaration.Name.Lexeme));
        environment.DefineTypeBinding(declaration.Name.Lexeme, symbol);
        environment.DefineTypeParameter(declaration.Name.Lexeme, typeParameter);
    }

    private void DefineSourceTypeParameter(
        TypeEnvironment environment,
        Token declaration,
        TypeInfo typeParameter)
    {
        BindingSymbol symbol = Bindings.Declare(
            declaration,
            CurrentSourceDocument,
            BindingNamespace.Type,
            environment.GetLocalTypeSymbol(declaration.Lexeme));
        environment.DefineTypeBinding(declaration.Lexeme, symbol);
        environment.DefineTypeParameter(declaration.Lexeme, typeParameter);
    }

    private void BindTypeUse(NamedTypeNode named)
    {
        if (named.NameToken is null)
            return;

        string rootName = named.Name.Split('.', 2)[0];
        if (_environment.GetTypeSymbol(rootName) is { } symbol)
            Bindings.Bind(named.NameToken, CurrentSourceDocument, symbol);

        if (named.NameTokens is not { Count: > 1 } tokens ||
            _environment.GetNamespace(rootName) is not { } currentNamespace)
        {
            return;
        }

        for (int i = 1; i < tokens.Count; i++)
        {
            Token member = tokens[i];
            BindingSymbol? memberSymbol =
                currentNamespace.GetTypeBinding(member.Lexeme)
                ?? currentNamespace.GetValueBinding(member.Lexeme);
            if (memberSymbol is not null)
                Bindings.Bind(member, CurrentSourceDocument, memberSymbol);

            if (currentNamespace.Types.GetValueOrDefault(member.Lexeme)
                is TypeInfo.Namespace nested)
            {
                currentNamespace = nested;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// When false (TypeScript's <c>strictNullChecks: off</c>), <c>null</c> and <c>undefined</c>
    /// are assignable to every type (except <c>never</c>). Defaults to true to preserve SharpTS's
    /// strict behavior; the TS conformance runner sets it from each test's <c>@strict</c> /
    /// <c>@strictNullChecks</c> directive (which default off for the legacy corpus).
    /// </summary>
    private readonly bool _strictNullChecks;

    /// <summary>
    /// When true (TypeScript's <c>strictFunctionTypes</c>), function-type parameters compare
    /// contravariantly; members declared with METHOD syntax keep the legacy bivariant comparison
    /// (tsc's exemption). Defaults to false — the product keeps bivariant relating; the TS
    /// conformance runner sets it from each test's <c>@strict</c> directive.
    /// </summary>
    private readonly bool _strictFunctionTypes;

    /// <summary>
    /// When true (TypeScript's <c>noImplicitAny</c>), unannotated parameters of DECLARED
    /// functions, methods and constructors report TS7006 (TS7019 for rest parameters).
    /// Arrow/function-expression parameters are deliberately exempt — see
    /// <c>ReportImplicitAnyParameters</c>. Defaults to false. Never participates in
    /// assignability, so unlike <see cref="_strictFunctionTypes"/> it needs no
    /// compatibility-cache guard.
    /// </summary>
    private readonly bool _noImplicitAny;
    private readonly bool _noImplicitThis;
    private readonly bool _strictPropertyInitialization;
    private readonly bool _checkVariableUseBeforeAssignment;
    private readonly bool _exactOptionalPropertyTypes;
    private readonly bool _noUncheckedIndexedAccess;
    private readonly bool _noUnusedLocals;
    private readonly HashSet<(SourceDocument Document, string Name)> _synthesizedJsxUses = [];

    /// <summary>
    /// Non-zero while comparing members declared with method syntax — within such a comparison,
    /// function parameters relate bivariantly even under <see cref="_strictFunctionTypes"/>.
    /// </summary>
    private int _methodBivarianceDepth;

    /// <summary>
    /// Non-zero while measuring a generic interface's type-parameter variances. Within
    /// measurement, the callback comparison rule is enabled (both params pure function types →
    /// relate swapped with strict parameters), so a parameter used only in callback-parameter
    /// positions measures covariant — tsc's Promise rule.
    /// </summary>
    private int _varianceMeasurementDepth;

    /// <summary>
    /// True for the immediate signature relation spawned by the callback rule: its parameters
    /// relate contravariantly only (no bivariant leg, no callback re-fire). Captured-and-cleared
    /// at RelateFunctionShapes entry so it never leaks into nested comparisons.
    /// </summary>
    private bool _inCallbackComparison;

    /// <summary>
    /// True when checking a worker_threads worker script — enables the worker-scoped
    /// globals in <see cref="LookupVariable"/>. Set via <see cref="AsWorkerContext"/>.
    /// </summary>
    private bool _isWorkerContext;

    /// <summary>
    /// Parameters already reported by <see cref="ReportImplicitAnyParameters"/>, by reference
    /// identity. Needed because a function's signature is visited more than once:
    /// HoistFunctionDeclarations re-runs for every enclosing function body, and CheckModules can
    /// re-enter a module after dynamic-import discovery.
    /// </summary>
    private HashSet<Stmt.Parameter>? _implicitAnyReported;

    /// <summary>
    /// Reports TS7006 (TS7019 for rest parameters) for unannotated parameters of a DECLARED
    /// function, method or constructor, when <c>noImplicitAny</c> is on.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately NOT called for arrow or function-expression parameters. SharpTS
    /// contextually types an arrow argument only when the callee is a plain
    /// <see cref="TypeInfo.Function"/>/<see cref="TypeInfo.GenericFunction"/> (see
    /// <c>TypeChecker.Calls.cs</c>) — not for overloaded or callable-interface callees, not for
    /// parameters typed as a union containing a function (so <c>p.then(v =&gt; …)</c> misses), and
    /// not for <c>any</c>-typed callees, which covers <c>console</c>, <c>Math</c>, <c>JSON</c>,
    /// <c>Promise</c> and <c>fetch</c>. Reporting there would fire on idiomatic code.</para>
    /// <para>A declared function's parameters are never contextually typed in tsc either, so on
    /// this population SharpTS and tsc agree exactly: no false positives by construction. The
    /// cost is under-reporting on arrows, which is the safe direction.</para>
    /// <para>Must NOT be called from <c>BuildFunctionSignature</c>: that runs during hoisting as
    /// well as checking, which would double-report.</para>
    /// </remarks>
    private void ReportImplicitAnyParameters(List<Stmt.Parameter> parameters, bool isAmbient)
    {
        if (!_noImplicitAny || isAmbient) return;
        // Speculative return-type inference re-checks bodies; RecordTypeError also guards this,
        // but returning early keeps the dedupe set from being poisoned by a suppressed pass.
        if (_suppressDiagnostics > 0) return;
        // A CommonJS dependency's missing annotations are not actionable by the user, and the
        // generic path would turn each into a warning — hundreds of them on --strict.
        if (IsLenientModule()) return;

        _implicitAnyReported ??= new HashSet<Stmt.Parameter>(ReferenceEqualityComparer.Instance);

        foreach (var param in parameters)
        {
            if (param.Type is not null || param.TypeAnnotationNode is not null) continue;
            // tsc infers the type from the initializer, so `function f(x = 0)` is not implicit any.
            if (param.DefaultValue is not null) continue;
            // Destructured parameters collapse to a synthetic `_paramN` binding at parse time, so
            // the per-element names tsc reports TS7031 on do not exist here.
            if (param.Name.Lexeme.StartsWith("_param", StringComparison.Ordinal)) continue;
            if (!_implicitAnyReported.Add(param)) continue;

            RecordTypeError(new TypeCheckException(
                param.IsRest
                    ? $" Rest parameter '{param.Name.Lexeme}' implicitly has an 'any[]' type."
                    : $" Parameter '{param.Name.Lexeme}' implicitly has an 'any' type.",
                line: param.Name.Line,
                tsCode: param.IsRest ? "TS7019" : "TS7006"));
        }
    }

    /// <summary>The resolved strictness configuration this checker was constructed with.</summary>
    public TypeCheckerOptions Options { get; }

    /// <summary>
    /// Creates a type checker from a resolved options object. This is the constructor the
    /// CLI/tsconfig layer uses; the flags are mirrored into fields so the assignability read
    /// sites stay untouched.
    /// </summary>
    public TypeChecker(TypeCheckerOptions options)
    {
        Options = options ?? TypeCheckerOptions.Default;
        _strictNullChecks = Options.StrictNullChecks;
        _strictFunctionTypes = Options.StrictFunctionTypes;
        _noImplicitAny = Options.NoImplicitAny;
        _noImplicitThis = Options.NoImplicitThis;
        _strictPropertyInitialization = Options.StrictPropertyInitialization;
        _checkVariableUseBeforeAssignment = Options.CheckVariableUseBeforeAssignment;
        _exactOptionalPropertyTypes = Options.ExactOptionalPropertyTypes;
        _noUncheckedIndexedAccess = Options.NoUncheckedIndexedAccess;
        _noUnusedLocals = Options.NoUnusedLocals;
        _diagnostics.MaxErrors = Options.MaxErrors;
    }

    /// <summary>
    /// Creates a type checker. <paramref name="strictNullChecks"/> defaults to true.
    /// </summary>
    /// <remarks>
    /// Kept permanently: <c>new TypeChecker()</c> is the idiomatic "product defaults" spelling
    /// at ~58 sites, and the TS conformance runner drives this overload by name. It forwards to
    /// the options constructor and leaves <see cref="TypeCheckerOptions.NoImplicitAny"/> off, so
    /// existing callers keep exactly their current semantics.
    /// <para><b>Do not change these defaults.</b> The Test262 and TS-conformance runners build
    /// their checkers directly rather than through the CLI, so the committed baselines are pinned
    /// to precisely these values.</para>
    /// </remarks>
    public TypeChecker(bool strictNullChecks = true, int maxErrors = 10, bool strictFunctionTypes = false)
        : this(new TypeCheckerOptions
        {
            StrictNullChecks = strictNullChecks,
            MaxErrors = maxErrors,
            StrictFunctionTypes = strictFunctionTypes,
        })
    { }

    // We need to track the current function's expected return type to validate 'return' statements
    private TypeInfo? _currentFunctionReturnType = null;
    // Phase 1A enables this only for hosted program preparation. Keeping the
    // default false preserves the ordinary CLI/interpreter/compiler contract.
    private bool _allowHostedTopLevelAwait;
    // When non-null, VisitReturn collects return expression types here instead of validating (for inference)
    private List<TypeInfo>? _inferredReturnTypes = null;
    // When non-null (set only while inferring a generator's return type), CheckYield collects the operand
    // types of `yield` / `yield*` here. A generator's inferred type argument is the union of these YIELD
    // types, NOT the function's `return` value — `return` is the (discarded) TReturn (#548). Distinct from
    // _inferredReturnTypes so a generator with `return x` still type-checks x without polluting the yield type.
    private List<TypeInfo>? _inferredYieldTypes = null;
    private TypeInfo.Class? _currentClass = null;
    private bool _inStaticMethod = false;
    private bool _inStaticBlock = false;
    // Track the declared 'this' type for explicit this parameter (e.g., function f(this: MyType) {})
    private TypeInfo? _currentFunctionThisType = null;
    // Contextual 'this' type for object literal accessor bodies (set during CheckObject two-pass)
    private TypeInfo? _pendingObjectThisType = null;

    // Memoization cache for IsCompatible checks - cleared per Check() call
    private Dictionary<(TypeInfo Expected, TypeInfo Actual), bool>? _compatibilityCache;

    // Path-based narrowing context stack for control flow type narrowing
    // Each scope level can have its own narrowings (for if/else branches, etc.)
    private readonly Stack<Narrowing.NarrowingContext> _narrowingContextStack = new();

    // Escape analysis for inter-procedural aliasing
    // Tracks when objects escape to outer scopes and might be aliased by global variables
    private readonly Narrowing.EscapeAnalyzer _escapeAnalyzer = new();

    // Alias tracking for narrowing invalidation
    // Maps a variable name to the variable it was assigned from (for simple variable-to-variable aliases)
    // e.g., "const alias = obj" would add entry: "alias" -> "obj"
    private readonly Dictionary<string, string> _variableAliases = new();

    // Track declared variable types separately from potentially narrowed types
    // This allows assignments to check against the original declared type, not the narrowed type
    // Stack of dictionaries to handle function scope boundaries
    private readonly Stack<Dictionary<string, TypeInfo>> _declaredVariableTypesStack = new();

    // Flow state for TS2454 ("variable is used before being assigned"). Each callable/module
    // frame owns its own binding-identity map so a deferred closure read of an outer variable is
    // not confused with an immediate read in the variable's declaring flow. Blocks deliberately
    // share their callable frame; assignments in them participate in the surrounding flow.
    private readonly Stack<Dictionary<BindingSymbol, bool>> _definiteAssignmentStack = new();
    private int _namespaceDepth;

    /// <summary>
    /// Gets the narrowed type for a path, if one exists in the current scope.
    /// Also checks for explicit invalidations that block lookup in parent scopes.
    /// </summary>
    private TypeInfo? GetNarrowing(Narrowing.NarrowingPath path)
    {
        foreach (var context in _narrowingContextStack)
        {
            // Check if this path has been explicitly invalidated in this scope
            // If so, stop the upward search - the narrowing is no longer valid
            if (context.IsInvalidated(path))
                return null;

            var narrowed = context.GetNarrowing(path);
            if (narrowed != null) return narrowed;
        }
        return null;
    }

    /// <summary>
    /// Enters a new narrowing scope with the given context.
    /// </summary>
    private void PushNarrowingContext(Narrowing.NarrowingContext context) => _narrowingContextStack.Push(context);

    /// <summary>
    /// Enters a new empty narrowing scope.
    /// </summary>
    private void PushEmptyNarrowingScope() => _narrowingContextStack.Push(Narrowing.NarrowingContext.Empty);

    /// <summary>
    /// Enters a new narrowing scope that inherits from the current scope.
    /// Used for control flow branches where narrowings should be isolated.
    /// </summary>
    private void PushNarrowingScope()
    {
        var current = _narrowingContextStack.Count > 0
            ? _narrowingContextStack.Peek()
            : Narrowing.NarrowingContext.Empty;
        _narrowingContextStack.Push(current);
    }

    /// <summary>
    /// Exits the current narrowing scope, discarding its narrowings.
    /// </summary>
    private void PopNarrowingScope()
    {
        if (_narrowingContextStack.Count > 0)
            _narrowingContextStack.Pop();
    }

    /// <summary>
    /// Exits the current narrowing scope.
    /// </summary>
    private void PopNarrowingContext()
    {
        if (_narrowingContextStack.Count > 0)
            _narrowingContextStack.Pop();
    }

    /// <summary>
    /// Adds a narrowing to the current scope.
    /// </summary>
    private void AddNarrowing(Narrowing.NarrowingPath path, TypeInfo narrowedType)
    {
        if (_narrowingContextStack.Count > 0)
        {
            var current = _narrowingContextStack.Pop();
            _narrowingContextStack.Push(current.WithNarrowing(path, narrowedType));
        }
        else
        {
            // If no context exists, create one with this narrowing
            _narrowingContextStack.Push(Narrowing.NarrowingContext.Empty.WithNarrowing(path, narrowedType));
        }
    }

    /// <summary>
    /// Invalidates narrowings affected by an assignment to the given path.
    /// </summary>
    private void InvalidateNarrowingsFor(Narrowing.NarrowingPath assignedPath)
    {
        if (_narrowingContextStack.Count > 0)
        {
            var current = _narrowingContextStack.Pop();
            _narrowingContextStack.Push(current.Invalidate(assignedPath));
        }
    }

    /// <summary>
    /// Invalidates only the property/element narrowings rooted at <paramref name="basePath"/> (paths
    /// strictly deeper than it), leaving the base path's OWN narrowing intact. Used when a variable is
    /// reassigned to a value that stays within its narrowing: the variable itself is still narrowed,
    /// but its old property narrowings describe a now-replaced value, so they are dropped (#570).
    /// </summary>
    private void InvalidatePropertyNarrowingsFor(Narrowing.NarrowingPath basePath)
    {
        if (_narrowingContextStack.Count > 0)
        {
            var current = _narrowingContextStack.Pop();
            _narrowingContextStack.Push(current.InvalidatePropertiesOf(basePath));
        }
    }

    /// <summary>
    /// Resets active lexical (<see cref="TypeEnvironment"/>) narrowings of <paramref name="name"/> in
    /// the ENCLOSING scopes that hold them back to <paramref name="declaredType"/>. <c>if</c>-guard
    /// variable narrowing is applied by redefining the variable in the guard's child environment; when
    /// a reassignment that escapes the narrowing happens inside a further-nested block, that block's
    /// environment is discarded at the join, so the outer guard narrowing would otherwise survive into
    /// later statements (the #570 soundness gap). Widening the guards' environments closes that gap.
    /// The reassignment's own (current) scope is widened separately by the caller's
    /// <c>_environment.Define</c>; this walks strictly OUTWARD.
    /// <para>
    /// It widens EVERY enclosing scope that holds a narrowing of the variable, not just the nearest:
    /// when two (or more) guards narrow the SAME variable and the escaping reassignment sits under the
    /// inner one, widening only the inner guard left the outer guard's narrowing stale, so a read at
    /// the outer level after the inner block still saw it (#654). The walk stops at the variable's
    /// declaration — whose binding is the full declared type, since guards only ever install proper
    /// subtypes — so it never crosses into an OUTER, same-named shadowing variable whose own narrowing
    /// is still valid.
    /// </para>
    /// </summary>
    private void WidenEnclosingNarrowing(string name, TypeInfo declaredType)
    {
        for (TypeEnvironment? env = _environment.Enclosing; env != null; env = env.Enclosing)
        {
            if (!env.IsDefinedLocally(name))
                continue;

            var local = env.Get(name);

            // A binding that isn't assignable to the declared type belongs to a different
            // (shadowing) variable of the same name — stop before touching it.
            if (local != null && !IsCompatible(declaredType, local))
                return;

            env.Define(name, declaredType);

            // Reached the declaration (its binding is the full declared type): nothing further
            // out narrows THIS variable, so stop rather than widen an outer shadowing variable.
            if (local == null || TypeInfoEqualityComparer.Instance.Equals(local, declaredType))
                return;
        }
    }

    /// <summary>
    /// Pushes a new scope for declared variable types (called when entering a function).
    /// </summary>
    private void PushDeclaredVariableScope()
    {
        _declaredVariableTypesStack.Push(new Dictionary<string, TypeInfo>());
        PushDefiniteAssignmentScope();
    }

    /// <summary>
    /// Pops the current scope for declared variable types (called when exiting a function).
    /// </summary>
    private void PopDeclaredVariableScope()
    {
        if (_declaredVariableTypesStack.Count > 0)
            _declaredVariableTypesStack.Pop();
        PopDefiniteAssignmentScope();
    }

    private void PushDefiniteAssignmentScope() =>
        _definiteAssignmentStack.Push(new Dictionary<BindingSymbol, bool>(ReferenceEqualityComparer.Instance));

    private void PopDefiniteAssignmentScope()
    {
        if (_definiteAssignmentStack.Count > 0)
            _definiteAssignmentStack.Pop();
    }

    private void RecordDefiniteAssignmentDeclaration(
        BindingSymbol symbol,
        bool isAssigned,
        TypeInfo declaredType)
    {
        if (!_checkVariableUseBeforeAssignment || !_strictNullChecks || CanBeUndefined(declaredType) ||
            !_definiteAssignmentStack.TryPeek(out var state))
        {
            return;
        }

        // A declaration without an initializer does not undo an assignment made by an earlier
        // merged `var` declaration. Conversely, an initialized redeclaration makes the binding
        // definitely assigned from that point onward.
        if (state.TryGetValue(symbol, out bool alreadyAssigned))
            state[symbol] = alreadyAssigned || isAssigned;
        else
            state[symbol] = isAssigned;
    }

    private void MarkDefinitelyAssigned(Token name)
    {
        if (!_definiteAssignmentStack.TryPeek(out var state) ||
            _environment.GetValueBinding(name.Lexeme) is not { } symbol ||
            !state.ContainsKey(symbol))
        {
            return;
        }

        state[symbol] = true;
    }

    private void ThrowIfUsedBeforeAssigned(Token name, BindingSymbol symbol)
    {
        if (_definiteAssignmentStack.TryPeek(out var state) &&
            state.TryGetValue(symbol, out bool assigned) && !assigned)
        {
            var error = new TypeCheckException(
                $" Variable '{name.Lexeme}' is used before being assigned.",
                line: name.Line,
                tsCode: "TS2454");
            // A use-before-assignment diagnostic does not make the expression untypable. In
            // recovery mode keep checking with the variable's declared type so an enclosing
            // assignment can also report its own incompatibility on the same line, as tsc does.
            if (_recoveryMode)
                RecordTypeError(error);
            else
                throw error;
        }
    }

    private Dictionary<BindingSymbol, bool>? SnapshotDefiniteAssignmentState() =>
        _definiteAssignmentStack.TryPeek(out var state)
            ? new Dictionary<BindingSymbol, bool>(state, ReferenceEqualityComparer.Instance)
            : null;

    private void RestoreDefiniteAssignmentState(
        IReadOnlyDictionary<BindingSymbol, bool>? snapshot)
    {
        if (snapshot is null || !_definiteAssignmentStack.TryPeek(out var state))
            return;

        state.Clear();
        foreach (var (symbol, assigned) in snapshot)
            state[symbol] = assigned;
    }

    private void MergeDefiniteAssignmentBranches(
        IReadOnlyDictionary<BindingSymbol, bool>? incoming,
        IReadOnlyDictionary<BindingSymbol, bool>? thenExit,
        IReadOnlyDictionary<BindingSymbol, bool>? elseExit,
        bool thenTerminates,
        bool elseTerminates)
    {
        if (incoming is null || !_definiteAssignmentStack.TryPeek(out var state))
            return;

        IReadOnlyDictionary<BindingSymbol, bool> first;
        IReadOnlyDictionary<BindingSymbol, bool> second;
        if (thenTerminates && !elseTerminates)
        {
            first = second = elseExit ?? incoming;
        }
        else if (elseTerminates && !thenTerminates)
        {
            first = second = thenExit ?? incoming;
        }
        else if (thenTerminates && elseTerminates)
        {
            first = second = incoming;
        }
        else
        {
            first = thenExit ?? incoming;
            second = elseExit ?? incoming;
        }

        state.Clear();
        foreach (var (symbol, wasAssigned) in incoming)
        {
            bool assigned = wasAssigned ||
                (first.GetValueOrDefault(symbol) && second.GetValueOrDefault(symbol));
            state[symbol] = assigned;
        }
    }

    /// <summary>
    /// Records the declared type of a variable for assignment checking.
    /// </summary>
    private void RecordDeclaredType(string name, TypeInfo type)
    {
        if (_declaredVariableTypesStack.Count > 0)
        {
            _declaredVariableTypesStack.Peek()[name] = type;
        }
    }

    /// <summary>
    /// Gets the declared type of a variable (ignoring any narrowings).
    /// Falls back to the current environment type if no declared type was recorded.
    /// </summary>
    private TypeInfo? GetDeclaredType(string name)
    {
        // Search through scopes from innermost to outermost
        foreach (var scope in _declaredVariableTypesStack)
        {
            if (scope.TryGetValue(name, out var type))
                return type;
        }
        // Fall back to environment (this handles globals and cases where we didn't track)
        return _environment.Get(name);
    }

    /// <summary>
    /// Whether <paramref name="name"/> has a recorded declared type in the current function's
    /// declared-type stack (rather than only living in the environment). Function locals and
    /// parameters are tracked; module/top-level variables are NOT, so <see cref="GetDeclaredType"/>
    /// falls back to the environment binding for them. Post-write variable narrowing (#653) replaces
    /// the environment binding with the narrowed type, so it must only run for tracked variables —
    /// otherwise a later assignment's <see cref="GetDeclaredType"/> would read the narrowed type as
    /// the "declared" type and wrongly reject a valid reassignment to another union member.
    /// </summary>
    private bool IsDeclaredTypeTracked(string name)
    {
        foreach (var scope in _declaredVariableTypesStack)
            if (scope.ContainsKey(name)) return true;
        return false;
    }

    /// <summary>
    /// Invalidates property narrowings for an object when it's passed to a function
    /// that might mutate it. Only affects mutable property narrowings, not the object itself.
    /// Readonly properties are preserved since they can't be mutated.
    /// </summary>
    private void InvalidatePropertiesForFunctionArg(Narrowing.NarrowingPath basePath)
    {
        if (_narrowingContextStack.Count > 0)
        {
            var current = _narrowingContextStack.Pop();
            _narrowingContextStack.Push(current.InvalidatePropertiesOf(basePath, IsReadonlyProperty));
        }
    }

    /// <summary>
    /// Checks if a property at the given path is readonly (safe to keep narrowed across function calls).
    /// </summary>
    /// <param name="basePath">The path to the object containing the property.</param>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>True if the property is readonly and should not be invalidated.</returns>
    private bool IsReadonlyProperty(Narrowing.NarrowingPath basePath, string propertyName)
    {
        // Get the type for the base path
        var baseType = GetTypeForNarrowingPath(basePath);
        if (baseType == null) return false;

        // Check if the property is readonly in the type
        return IsPropertyReadonly(baseType, propertyName);
    }

    /// <summary>
    /// Gets the type for a narrowing path by looking up the variable/property chain.
    /// </summary>
    private TypeInfo? GetTypeForNarrowingPath(Narrowing.NarrowingPath path)
    {
        return path switch
        {
            Narrowing.NarrowingPath.Variable v => _environment.Get(v.Name),
            Narrowing.NarrowingPath.PropertyAccess pa =>
                GetPropertyTypeForNarrowing(GetTypeForNarrowingPath(pa.Base), pa.Property),
            Narrowing.NarrowingPath.ElementAccess ea =>
                GetElementTypeForNarrowing(GetTypeForNarrowingPath(ea.Base), ea.Index),
            _ => null
        };
    }

    /// <summary>
    /// Gets the type of a property from a parent type (for narrowing path resolution).
    /// </summary>
    private TypeInfo? GetPropertyTypeForNarrowing(TypeInfo? parentType, string propertyName)
    {
        if (parentType == null) return null;

        return parentType switch
        {
            TypeInfo.Interface iface =>
                iface.Members.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.GenericInterface gi =>
                gi.Members.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Record rec =>
                rec.Fields.TryGetValue(propertyName, out var t) ? t : null,
            TypeInfo.Instance inst => GetPropertyTypeForNarrowing(inst.ResolvedClassType, propertyName),
            TypeInfo.Class cls =>
                cls.FieldTypes.TryGetValue(propertyName, out var t) ? t :
                cls.Getters.TryGetValue(propertyName, out var g) ? g : null,
            TypeInfo.InstantiatedGeneric ig => GetPropertyTypeForNarrowing(ig.GenericDefinition, propertyName),
            _ => null
        };
    }

    /// <summary>
    /// Gets the element type from a tuple or array type (for narrowing path resolution).
    /// </summary>
    private static TypeInfo? GetElementTypeForNarrowing(TypeInfo? parentType, int index)
    {
        return parentType switch
        {
            TypeInfo.Tuple tuple when index < tuple.Elements.Count => tuple.Elements[index].Type,
            TypeInfo.Array arr => arr.ElementType,
            _ => null
        };
    }

    /// <summary>
    /// Checks if a property is readonly in the given type.
    /// </summary>
    private static bool IsPropertyReadonly(TypeInfo type, string propertyName)
    {
        return type switch
        {
            TypeInfo.Interface iface => iface.IsMemberReadonly(propertyName),
            TypeInfo.GenericInterface gi => gi.IsMemberReadonly(propertyName),
            TypeInfo.Class cls => cls.ReadonlyFields.Contains(propertyName),
            TypeInfo.Record rec => rec.IsReadonly,  // Readonly record makes all fields readonly
            TypeInfo.Instance inst => IsPropertyReadonly(inst.ResolvedClassType, propertyName),
            TypeInfo.InstantiatedGeneric ig => IsPropertyReadonly(ig.GenericDefinition, propertyName),
            _ => false
        };
    }

    /// <summary>
    /// Extracts a NarrowingPath from an expression if it represents a narrowable location.
    /// Returns null if the expression is not narrowable (e.g., a literal or complex expression).
    /// </summary>
    private static Narrowing.NarrowingPath? GetNarrowingPath(Parsing.Expr expr)
    {
        return expr switch
        {
            Parsing.Expr.Variable v => new Narrowing.NarrowingPath.Variable(v.Name.Lexeme),
            Parsing.Expr.Get get when GetNarrowingPath(get.Object) is { } basePath =>
                new Narrowing.NarrowingPath.PropertyAccess(basePath, get.Name.Lexeme),
            Parsing.Expr.GetIndex idx when GetNarrowingPath(idx.Object) is { } basePath &&
                                           idx.Index is Parsing.Expr.Literal { Value: double d } &&
                                           d == Math.Floor(d) =>
                new Narrowing.NarrowingPath.ElementAccess(basePath, (int)d),
            _ => null
        };
    }

    // Error recovery support
    private readonly DiagnosticCollector _diagnostics = new();
    private CancellationToken _cancellationToken;

    /// <summary>
    /// When &gt; 0, <see cref="RecordTypeError(TypeCheckException)"/> and its overload are no-ops.
    /// Set during speculative hoist-time return-type inference (#383), which checks a function body
    /// purely to learn its inferred return type and must not surface (duplicate) diagnostics — the
    /// real declaration pass reports them at their proper locations.
    /// </summary>
    private int _suppressDiagnostics = 0;

    /// <summary>
    /// Memoizes the hoist-time speculatively-inferred return type of an un-annotated, forward-
    /// referenced local <c>function</c> (#383), keyed by declaration identity (reference equality).
    /// A null value marks an in-progress or failed inference: don't re-attempt, leave the hoisted
    /// <c>any</c> placeholder in place. Non-null values are re-registered into each fresh function
    /// scope on subsequent hoisting passes without re-checking the body.
    /// </summary>
    private readonly Dictionary<Stmt.Function, TypeInfo?> _hoistInferredReturnTypes =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets any diagnostics collected during module type checking.
    /// </summary>
    public IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics.Diagnostics;
    private string? _filePath = null;

    /// <summary>
    /// Best-guess line of the statement currently being checked. Used as a fallback
    /// when a thrown TypeCheckException doesn't carry its own line info, so that
    /// `// @ts-ignore` and `// @ts-expect-error` directives can match diagnostics
    /// to the right line.
    /// </summary>
    private int? _currentStatementLine = null;

    /// <summary>
    /// When non-null, a generic type-argument constraint violation (TS2344) encountered while
    /// resolving an <c>extends</c>/superclass clause is RECORDED at this line and instantiation
    /// proceeds with the given (constraint-violating) arguments — mirroring tsc, which reports
    /// TS2344 and keeps checking the rest of the declaration and its siblings. Null (the default)
    /// preserves the legacy throw, which other call sites (<c>new A&lt;Bad&gt;()</c>,
    /// <c>let x: Box&lt;Bad&gt;</c>, generic calls) and their tests rely on. Save/restore around
    /// each set to survive nesting. See <see cref="InstantiateGenericClass"/> (#895).
    /// </summary>
    private int? _extendsClauseConstraintLine = null;

    /// <summary>
    /// True while checking via <see cref="CheckWithRecovery"/>. In this mode, a type error in one
    /// statement is recorded and checking continues with the next statement (so all errors surface,
    /// each at its own line) rather than the first error aborting the enclosing scope.
    /// </summary>
    private bool _recoveryMode = false;

    /// <summary>
    /// Checks a list of statements. In recovery mode each statement is checked independently — a
    /// <see cref="TypeCheckException"/> is recorded against that statement's line and the remaining
    /// statements are still checked. In strict mode the first error propagates as before.
    /// </summary>
    private void CheckStmtList(List<Stmt> statements)
    {
        if (!_recoveryMode)
        {
            foreach (Stmt statement in statements)
            {
                ThrowIfCancellationRequested();
                CheckStmt(statement);
            }
            return;
        }

        foreach (Stmt statement in statements)
        {
            ThrowIfCancellationRequested();
            if (_diagnostics.HitErrorLimit) return;
            int? saved = _currentStatementLine;
            _currentStatementLine = TryGetStmtLine(statement) ?? saved;
            try
            {
                CheckStmt(statement);
            }
            catch (TypeMismatchException ex) { RecordTypeError(ex); }
            catch (TypeCheckException ex) { RecordTypeError(ex); }
            finally { _currentStatementLine = saved; }
        }
    }

    /// <summary>
    /// Sets the file path for source location reporting.
    /// </summary>
    public TypeChecker WithFilePath(string? filePath)
    {
        _filePath = filePath;
        return this;
    }

    /// <summary>
    /// Supplies cooperative cancellation for editor checks. The checker observes it between
    /// declarations, statements, and module passes without changing compiler callers.
    /// </summary>
    public TypeChecker WithCancellation(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        return this;
    }

    private void ThrowIfCancellationRequested() =>
        _cancellationToken.ThrowIfCancellationRequested();

    /// <summary>
    /// Marks this checker as running a worker_threads worker script, so the
    /// worker-scoped globals (<c>parentPort</c>, <c>postMessage</c>, <c>workerData</c>,
    /// <c>threadId</c>, <c>isMainThread</c>) resolve as <c>any</c> instead of TS2304.
    /// These are bound at runtime by <see cref="SharpTS.Runtime.Types.SharpTSWorker"/>'s
    /// SetupWorkerGlobals and are not visible on the main thread, mirroring Node.
    /// </summary>
    public TypeChecker AsWorkerContext()
    {
        _isWorkerContext = true;
        return this;
    }

    /// <summary>
    /// Maximum depth for recursive type alias expansion.
    /// </summary>
    // Type-node expansion carries substantially larger CLR frames than the
    // old string parser. Twenty-four nested instantiations are ample for real
    // declarations and leave enough stack to report TS2589 recoverably.
    private const int MaxTypeAliasExpansionDepth = 24;

    /// <summary>
    /// Tracks type aliases currently being expanded to detect circular references.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _typeAliasExpansionStack;

    /// <summary>
    /// Current recursion depth during type alias expansion.
    /// </summary>
    [ThreadStatic]
    private static int _typeAliasExpansionDepth;

    /// <summary>
    /// Names of type variables that are bound but not yet substitutable — mapped-type
    /// parameters whose owning body is currently being parsed (e.g. P while parsing the
    /// value type of <c>{ [P in K]: DeepReadonly&lt;T[P]&gt; }</c>). Identifiers matching these
    /// parse to <see cref="TypeInfo.TypeParameter"/>, and generic alias references whose
    /// arguments mention them are deferred instead of instantiated (#185).
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _openTypeVariablesInScope;

    /// <summary>
    /// Maximum recursion depth for string-based type resolution (ToTypeInfo and the
    /// generic/mapped/indexed-access helpers that funnel back through it). A backstop
    /// against pathological self-referential type strings: it converts what would be an
    /// uncatchable StackOverflowException (which crashes the whole process) into a normal
    /// catchable type error. Set well above any legitimate nesting depth.
    /// </summary>
    private const int MaxTypeResolutionDepth = 400;

    /// <summary>
    /// Current recursion depth through <c>ToTypeInfo</c>.
    /// </summary>
    [ThreadStatic]
    private static int _typeResolutionDepth;

    /// <summary>
    /// Cache for expanded type aliases to ensure the same TypeInfo object is reused.
    /// This enables identity-based caching in IsCompatible to work correctly with recursive types.
    /// Key: alias name (or "name&lt;arg1,arg2&gt;" for generic), Value: expanded TypeInfo
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, TypeInfo>? _expandedTypeAliasCache;

    /// <summary>
    /// RAII-style helper for safely managing TypeEnvironment scope changes.
    /// Automatically restores the previous environment on disposal, even if an exception is thrown.
    /// </summary>
    /// <remarks>
    /// Usage: using var _ = new EnvironmentScope(this, newEnvironment);
    /// This ensures _environment is always restored when the scope exits, preventing corruption
    /// if type checking throws an exception during the scope's lifetime.
    /// </remarks>
    private readonly struct EnvironmentScope : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly TypeEnvironment _previous;

        public EnvironmentScope(TypeChecker checker, TypeEnvironment newEnv)
        {
            _checker = checker;
            _previous = checker._environment;
            checker._environment = newEnv;
        }

        public void Dispose() => _checker._environment = _previous;
    }

    /// <summary>
    /// Builds a function signature by parsing parameters and validating optional/required ordering.
    /// Returns the DECLARED annotation types (`paramTypes`) — used for the function's own callable
    /// signature (`f?: T`, not `f: T | undefined`; matters for arity/display). Body-scope binding
    /// uses a WIDENED type instead — see <see cref="WidenOptionalParamsForBody"/> — since a caller
    /// may genuinely omit a `?`-optional parameter with no default.
    /// </summary>
    /// <param name="parameters">Function/method parameters to parse</param>
    /// <param name="validateDefaults">Whether to type-check default parameter values</param>
    /// <param name="contextName">Context name for error messages (e.g., "method 'foo'" or "function 'bar'")</param>
    /// <returns>Tuple of (parameter types, required parameter count, has rest parameter, parameter names)</returns>
    private (List<TypeInfo> paramTypes, int requiredParams, bool hasRest, List<string> paramNames) BuildFunctionSignature(
        List<Stmt.Parameter> parameters,
        bool validateDefaults,
        string contextName)
    {
        List<TypeInfo> paramTypes = [];
        List<string> paramNames = [];
        int requiredParams = 0;
        bool seenDefault = false;

        // A default value may reference any PRECEDING parameter (`(x: T, y: U = x)`), so defaults
        // are checked in a scope where the earlier parameters are progressively defined. Each
        // parameter is defined AFTER its own default is checked, so self-reference still resolves
        // to an outer binding or errors. A preceding bare-optional parameter is visible here with
        // its WIDENED (possibly-undefined) type, matching what the function body itself would see.
        var paramScope = new TypeEnvironment(_environment);

        foreach (var param in parameters)
        {
            TypeInfo? resolvedParameter = ResolveAnnotation(param.Type, param.TypeAnnotationNode);
            if (resolvedParameter is null && param.DestructuredProperties is { Count: > 0 } properties)
            {
                var fields = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
                var optional = new HashSet<string>(StringComparer.Ordinal);
                foreach (Stmt.DestructuredParameterProperty property in properties)
                {
                    fields[property.Key.Lexeme] = property.DefaultValue is { } defaultExpression
                        ? WidenLiteralType(CheckExpr(defaultExpression))
                        : TypeInfo.Any.Shared;
                    if (property.DefaultValue is not null) optional.Add(property.Key.Lexeme);

                    if (property.IsRenamed && property.Binding.Lexeme is
                        "string" or "number" or "boolean" or "object" or "symbol" or "bigint")
                    {
                        RecordTypeError(new TypeCheckException(
                            $"'{property.Binding.Lexeme}' is an unused renaming of '{property.Key.Lexeme}'. Did you intend to use it as a type annotation?",
                            property.Binding.Line, tsCode: "TS2842"));
                        if (_noImplicitAny)
                            RecordTypeError(new TypeCheckException(
                                $"Binding element '{property.Binding.Lexeme}' implicitly has an 'any' type.",
                                property.Binding.Line, tsCode: "TS7031"));
                    }
                }
                resolvedParameter = new TypeInfo.Record(
                    fields.ToFrozenDictionary(StringComparer.Ordinal),
                    OptionalFields: optional.Count > 0
                        ? optional.ToFrozenSet(StringComparer.Ordinal)
                        : null);
            }
            TypeInfo paramType = resolvedParameter ?? TypeInfo.Any.Shared;
            paramTypes.Add(paramType);
            paramNames.Add(param.Name.Lexeme);

            if (param.IsRest)
            {
                DeclareValue(paramScope, param.Name, paramType);
                continue;
            }

            bool isOptional = param.DefaultValue != null || param.IsOptional;

            if (param.DefaultValue != null)
            {
                seenDefault = true;
                if (validateDefaults)
                {
                    TypeInfo defaultType;
                    using (new EnvironmentScope(this, paramScope))
                    {
                        defaultType = CheckExpr(param.DefaultValue);
                    }
                    if (!IsCompatible(paramType, defaultType))
                    {
                        throw new TypeMismatchException($"Default value type is not assignable to parameter type in {contextName}", paramType, defaultType, tsCode: "TS2322");
                    }
                }
            }
            else if (param.IsOptional)
            {
                seenDefault = true;
            }
            else
            {
                if (seenDefault)
                {
                    throw new TypeCheckException($"Required parameter cannot follow optional parameter in {contextName}", tsCode: "TS1016");
                }
                requiredParams++;
            }

            DeclareValue(
                paramScope,
                param.Name,
                param.IsOptional && param.DefaultValue == null
                    ? CreateUnion(paramType, TypeInfo.Undefined.Shared)
                    : paramType);
        }

        bool hasRest = parameters.Any(p => p.IsRest);
        return (paramTypes, requiredParams, hasRest, paramNames);
    }

    /// <summary>
    /// Widens a function/method/arrow's declared parameter types for BODY-SCOPE binding: a bare
    /// `?`-optional parameter with no default may genuinely be omitted by the caller, so the body
    /// sees `T | undefined` even though the callable signature itself (<c>paramTypes</c> as returned
    /// by <see cref="BuildFunctionSignature"/>) keeps exactly `T`. A defaulted parameter is NOT
    /// widened: the default substitutes a same-typed value before the body ever observes it, so
    /// it's never actually undefined there. Rest parameters and required parameters pass through.
    /// </summary>
    private List<TypeInfo> WidenOptionalParamsForBody(List<TypeInfo> paramTypes, List<Stmt.Parameter> parameters)
    {
        var result = new List<TypeInfo>(paramTypes.Count);
        for (int i = 0; i < paramTypes.Count; i++)
        {
            var param = i < parameters.Count ? parameters[i] : null;
            result.Add(param is { IsOptional: true, DefaultValue: null }
                ? CreateUnion(paramTypes[i], TypeInfo.Undefined.Shared)
                : paramTypes[i]);
        }
        return result;
    }

    /// <summary>
    /// Collapses a list of types into a single type or union.
    /// If the list has only one element, returns that element directly.
    /// Otherwise, creates a Union type.
    /// </summary>
    private static TypeInfo CollapseOrCreateUnion(List<TypeInfo> types)
    {
        return types.Count == 1 ? types[0] : new TypeInfo.Union(types);
    }

    private int _loopDepth = 0;
    private int _switchDepth = 0;
    // Track if we're inside an async function (for validating 'await' usage)
    private bool _inAsyncFunction = false;
    // Track if we're inside a generator function (for validating 'yield' usage)
    private bool _inGeneratorFunction = false;

    // Track active labels for labeled statements (label name -> isOnLoop)
    private sealed record ActiveLabel(bool IsOnLoop, BindingSymbol Symbol);

    private readonly Dictionary<string, ActiveLabel> _activeLabels = [];

    // Track pending overload signatures per lexical environment. Function names can repeat
    // independently in the global scope, modules, and nested scopes.
    private readonly Dictionary<(TypeEnvironment Environment, string Name), List<TypeInfo.Function>> _pendingOverloadSignatures = [];

    // Track type parameters for generic overloaded functions
    private readonly Dictionary<(TypeEnvironment Environment, string Name), List<TypeInfo.TypeParameter>> _pendingOverloadTypeParams = [];

    // Decorator mode configuration
    private DecoratorMode _decoratorMode = DecoratorMode.None;

    /// <summary>
    /// Sets the decorator mode for type checking decorators.
    /// </summary>
    public void SetDecoratorMode(DecoratorMode mode) => _decoratorMode = mode;

    internal void EnableHostedTopLevelAwait() => _allowHostedTopLevelAwait = true;

    // Module support - track the current module being type-checked
    private ParsedModule? _currentModule = null;
    private ModuleResolver? _moduleResolver = null;

    // Track dynamic import paths discovered during type checking
    // Used for module discovery - ensures dynamically imported modules are compiled
    private readonly HashSet<string> _dynamicImportPaths = [];
    private readonly HashSet<(string Specifier, string ImportingModulePath)>
        _dynamicImportReferences = [];

    /// <summary>
    /// Gets the set of module paths discovered in dynamic import expressions with string literal paths.
    /// These paths are relative to the importing module and should be resolved before use.
    /// </summary>
    public IReadOnlySet<string> DynamicImportPaths => _dynamicImportPaths;

    /// <summary>
    /// Literal dynamic imports paired with the source module that owns the
    /// specifier. Compilers use this to resolve nested relative imports correctly.
    /// </summary>
    public IReadOnlySet<(string Specifier, string ImportingModulePath)>
        DynamicImportReferences => _dynamicImportReferences;

    /// <summary>
    /// Type-checks the given statements and returns a TypeMap with resolved types for all expressions.
    /// </summary>
    /// <param name="statements">The AST statements to check.</param>
    /// <returns>A TypeMap containing the resolved type for each expression.</returns>
    public TypeMap Check(List<Stmt> statements) => Check(statements, sourceDocument: null);

    /// <summary>
    /// Type-checks statements while retaining their source document in the semantic binding index.
    /// </summary>
    public TypeMap Check(List<Stmt> statements, SourceDocument? sourceDocument)
    {
        Bindings.Clear();
        _standaloneSourceDocument = sourceDocument;

        // Clear caches for fresh check
        _compatibilityCache = null;
        _expandedTypeAliasCache = null;
        _compatibilityInProgress = null;
        _ts2741Reported = null;
        _implicitAnyReported = null;
        _compatibilityCheckDepth = 0;
        _narrowingContextStack.Clear();
        ThrowIfCancellationRequested();

        // Module/top-level declarations live in their own declared-type frame so that
        // GetDeclaredType / IsDeclaredTypeTracked treat them like function locals (#743).
        // Clear first: the checker is reused across REPL lines and may retain frames from a
        // prior check (function frames are normally popped, but be defensive on early-exit).
        _declaredVariableTypesStack.Clear();
        _definiteAssignmentStack.Clear();
        PushDeclaredVariableScope();

        // Pre-define built-ins
        _environment.Define("console", TypeInfo.Any.Shared);
        _environment.Define("Reflect", TypeInfo.Any.Shared);
        _environment.Define("process", TypeInfo.Any.Shared);

        // Pre-register type declarations (interfaces, classes, enums, type aliases)
        // This ensures types are available when parsing function signatures during hoisting
        PreRegisterTypeDeclarations(statements);

        // Hoist class declarations (as Any for forward references in function bodies)
        HoistClassDeclarations(statements);

        // Hoist function declarations (now type references will resolve correctly)
        HoistFunctionDeclarations(statements);

        // Hoist var declarations (pre-define as any for forward reference support)
        HoistVarDeclarations(statements);

        // Hoist let/const declarations (pre-define as any so an earlier function body can
        // forward-reference a later block-scoped binding — #533)
        HoistLexicalDeclarations(statements);

        foreach (Stmt statement in statements)
        {
            CheckStmt(statement);
        }

        return _typeMap;
    }

    /// <summary>
    /// Type-checks the given statements with error recovery, collecting multiple errors.
    /// </summary>
    /// <param name="statements">The AST statements to check.</param>
    /// <returns>A TypeCheckDiagnosticResult containing the type map and any errors encountered.</returns>
    public TypeCheckDiagnosticResult CheckWithRecovery(List<Stmt> statements) =>
        CheckWithRecovery(statements, sourceDocument: null);

    /// <summary>
    /// Type-checks statements with recovery while retaining their source document in the semantic
    /// binding index.
    /// </summary>
    public TypeCheckDiagnosticResult CheckWithRecovery(
        List<Stmt> statements,
        SourceDocument? sourceDocument)
    {
        Bindings.Clear();
        _standaloneSourceDocument = sourceDocument;

        _diagnostics.Clear();
        _recoveryMode = true;
        // Clear caches for fresh check
        _compatibilityCache = null;
        _expandedTypeAliasCache = null;
        _compatibilityInProgress = null;
        _ts2741Reported = null;
        _implicitAnyReported = null;
        _compatibilityCheckDepth = 0;
        _narrowingContextStack.Clear();

        // Module/top-level declarations live in their own declared-type frame so that
        // GetDeclaredType / IsDeclaredTypeTracked treat them like function locals (#743).
        // Clear first: the checker is reused across REPL lines and may retain frames from a
        // prior check (function frames are normally popped, but be defensive on early-exit).
        _declaredVariableTypesStack.Clear();
        _definiteAssignmentStack.Clear();
        PushDeclaredVariableScope();

        // Pre-define built-ins
        _environment.Define("console", TypeInfo.Any.Shared);
        _environment.Define("Reflect", TypeInfo.Any.Shared);
        _environment.Define("process", TypeInfo.Any.Shared);

        // Pre-register type declarations
        PreRegisterTypeDeclarations(statements);
        ThrowIfCancellationRequested();

        // Hoist class declarations (as Any for forward references in function bodies)
        HoistClassDeclarations(statements);
        ThrowIfCancellationRequested();

        // Hoist function declarations
        HoistFunctionDeclarations(statements);
        ThrowIfCancellationRequested();

        // Hoist var declarations (pre-define as any for forward reference support)
        HoistVarDeclarations(statements);
        ThrowIfCancellationRequested();

        // Hoist let/const declarations (pre-define as any so an earlier function body can
        // forward-reference a later block-scoped binding — #533)
        HoistLexicalDeclarations(statements);

        foreach (Stmt statement in statements)
        {
            ThrowIfCancellationRequested();
            if (_diagnostics.HitErrorLimit)
            {
                _recoveryMode = false;
                return new TypeCheckDiagnosticResult(_typeMap, _diagnostics.Diagnostics, HitErrorLimit: true);
            }

            _currentStatementLine = TryGetStmtLine(statement);
            try
            {
                CheckStmt(statement);
            }
            catch (TypeMismatchException ex)
            {
                RecordTypeError(ex);
            }
            catch (TypeCheckException ex)
            {
                RecordTypeError(ex);
            }
            catch (Exception ex)
            {
                RecordTypeError(ex.Message, _currentStatementLine);
            }
        }
        _currentStatementLine = null;
        _recoveryMode = false;

        return new TypeCheckDiagnosticResult(_typeMap, _diagnostics.Diagnostics);
    }

    /// <summary>
    /// Records a type checking error from a TypeCheckException.
    /// </summary>
    private void RecordTypeError(TypeCheckException ex)
    {
        if (_suppressDiagnostics > 0) return;

        // Extract the core message by removing the "Type Error: " or "Type Error at line X: " prefix
        string message = ex.Message;
        if (message.StartsWith("Type Error at line"))
        {
            var colonIndex = message.IndexOf(": ", 15); // Skip past "Type Error at line X"
            if (colonIndex > 0)
                message = message[(colonIndex + 2)..];
        }
        else if (message.StartsWith("Type Error: "))
        {
            message = message["Type Error: ".Length..];
        }

        // Map exception type to diagnostic code
        DiagnosticCode code = ex switch
        {
            TypeMismatchException => DiagnosticCode.TypeMismatch,
            TypeOperationException => DiagnosticCode.TypeOperation,
            _ => DiagnosticCode.TypeError
        };

        // Fall back to the current statement's line when the exception didn't carry one.
        // Lets `// @ts-ignore` / `@ts-expect-error` line directives target diagnostics
        // whose throw-sites don't (yet) plumb token-level line info.
        int? line = ex.Line ?? _currentStatementLine;
        SourceLocation? location = line.HasValue
            ? new SourceLocation(_filePath, line.Value, ex.Column ?? 1)
            : null;

        // Preserve the canonical TSnnnn code from the throw site so the TS conformance
        // runner (which diffs on (line, tsCode)) can match against *.errors.txt baselines.
        string? tsCode = ex.Diagnostic.TsCode;

        if (IsLenientModule())
            _diagnostics.AddWarning(code, message, location, tsCode);
        else
            _diagnostics.AddError(code, message, location, tsCode);
    }

    /// <summary>
    /// Records a type checking error from a raw message.
    /// </summary>
    private void RecordTypeError(string message, int? line = null)
    {
        if (_suppressDiagnostics > 0) return;

        SourceLocation? location = line.HasValue
            ? new SourceLocation(_filePath, line.Value)
            : null;
        if (IsLenientModule())
            _diagnostics.AddWarning(DiagnosticCode.TypeError, message, location);
        else
            _diagnostics.AddError(DiagnosticCode.TypeError, message, location);
    }

    /// <summary>
    /// Determines if the current module should use lenient type checking.
    /// CJS dependency modules are always lenient — type errors become warnings
    /// that don't block execution, matching TypeScript's default behavior for
    /// JavaScript files (checkJs: false).
    /// </summary>
    private bool IsLenientModule()
    {
        if (_currentModule == null) return false;
        return _currentModule.IsCommonJs;
    }

    /// <summary>
    /// Pre-registers type declarations (interfaces, enums, type aliases) before function hoisting.
    /// This ensures type names are available when parsing function signatures.
    /// Full validation happens later during CheckStmt.
    /// Note: Classes are NOT pre-registered to avoid breaking inheritance checking with MutableClass.
    /// </summary>
    private void PreRegisterTypeDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case Stmt.Interface itf:
                    RegisterTypeDeclaration(itf.Name, mergeWithLocal: true);
                    PreRegisterInterface(itf);
                    break;
                case Stmt.TypeAlias alias:
                    RegisterTypeDeclaration(alias.Name);
                    PreRegisterTypeAlias(alias);
                    break;
                case Stmt.Enum enumStmt:
                    RegisterTypeDeclaration(enumStmt.Name, mergeWithLocal: true);
                    PreRegisterEnum(enumStmt);
                    break;
                case Stmt.Class cls:
                    // Register only the source identity here. The semantic class type is still
                    // created by CheckClassDeclaration, preserving the existing inheritance order.
                    RegisterTypeDeclaration(cls.Name);
                    break;
                case Stmt.Namespace ns:
                    RegisterTypeDeclaration(ns.Name, mergeWithLocal: true);
                    break;
                // Note: Classes are not pre-registered here because doing so creates MutableClass
                // objects that break inheritance checking. Classes are properly registered during
                // CheckClassDeclaration which handles inheritance correctly.
                case Stmt.Export export when export.Declaration != null:
                    // Handle exported type declarations
                    PreRegisterTypeDeclarations([export.Declaration]);
                    break;
                // Namespace members are NOT pre-registered here: that would leak them into the
                // enclosing scope, where a same-named declaration in a later namespace would be
                // skipped as "already defined" and its references silently bind to the first
                // declaration (assignmentCompatWithObjectMembers' module-scoped redeclarations).
                // CheckNamespace pre-registers its members inside the namespace scope instead.
            }
        }
    }

    /// <summary>
    /// Pre-registers a type alias before function hoisting.
    /// Type aliases are just stored as string definitions, so pre-registration is the same as full registration.
    /// </summary>
    private void PreRegisterTypeAlias(Stmt.TypeAlias typeAlias)
    {
        // Skip if already registered
        if (_environment.GetTypeAlias(typeAlias.Name.Lexeme) != null)
            return;

        if (typeAlias.TypeParameters != null && typeAlias.TypeParameters.Count > 0)
        {
            var typeParamNames = typeAlias.TypeParameters.Select(tp => tp.Name.Lexeme).ToList();
            _environment.DefineGenericTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition, typeParamNames, typeAlias.TypeDefinitionNode);
        }
        else
        {
            _environment.DefineTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition, typeAlias.TypeDefinitionNode);
        }
        RecordAliasParamConstraints(typeAlias);
    }

    /// <summary>
    /// Pre-registers an enum before function hoisting.
    /// Creates a basic enum type with placeholder values. Full validation happens in CheckEnumDeclaration.
    /// </summary>
    private void PreRegisterEnum(Stmt.Enum enumStmt)
    {
        // Skip if already registered
        if (_environment.IsDefinedLocally(enumStmt.Name.Lexeme))
            return;

        // Create a basic enum with member names (values will be computed during full check)
        Dictionary<string, object> members = [];
        double value = 0;

        foreach (var member in enumStmt.Members)
        {
            // During pre-registration, just assign sequential numeric values as placeholders
            // The full check will compute actual values
            members[member.Name.Lexeme] = value++;
        }

        var enumType = new TypeInfo.Enum(
            enumStmt.Name.Lexeme,
            members.ToFrozenDictionary(),
            EnumKind.Numeric,
            enumStmt.IsConst
        );
        DeclareValue(enumStmt.Name, enumType);
        _environment.DefineType(enumStmt.Name.Lexeme, enumType);
    }

    /// <summary>
    /// Type-checks multiple modules in dependency order.
    /// </summary>
    /// <param name="modules">Modules in dependency order (dependencies first)</param>
    /// <param name="resolver">Module resolver for path resolution</param>
    /// <returns>A TypeMap containing resolved types for all expressions across all modules</returns>
    public TypeMap CheckModules(List<ParsedModule> modules, ModuleResolver resolver)
    {
        Bindings.Clear();
        _synthesizedJsxUses.Clear();
        _standaloneSourceDocument = null;

        // Clear compatibility cache for fresh check
        _compatibilityCache = null;

        _moduleResolver = resolver;

        // Base declared-type frame for the whole run, mirroring Check()/CheckWithRecovery (#743) —
        // shared by all script files, which share global scope. With no frame on the stack,
        // RecordDeclaredType silently drops every binding and GetDeclaredType falls back to the
        // environment binding — which control-flow narrowing mutates — so an assignment inside a
        // narrowed branch is checked against the narrowed literal type instead of the declared one
        // (#1218: `let found = false; if (!found) { found = true; }` rejected in module mode).
        // Clear first: the checker may retain frames from a prior check's early exit.
        _declaredVariableTypesStack.Clear();
        _definiteAssignmentStack.Clear();
        PushDeclaredVariableScope();

        // Pre-define built-ins in the global environment
        _environment.Define("console", TypeInfo.Any.Shared);
        _environment.Define("Reflect", TypeInfo.Any.Shared);
        _environment.Define("process", TypeInfo.Any.Shared);

        // Create a shared script environment for script files (they share global scope)
        var scriptEnv = new TypeEnvironment(_environment);

        // First pass: collect all exports from each module. Declaration files are trusted
        // compiler inputs: this pass consumes every surface SharpTS understands, then marks
        // them complete so checker-parity gaps inside third-party @types packages are not
        // reported as errors in the user's program. For source modules this preparatory pass
        // type-checks bodies too (suppressed), so it runs in its own declared-type frame: its
        // records are discarded rather than leaking into the base frame the authoritative
        // second pass — which re-checks and re-records everything — reads through (#1218).
        PushDeclaredVariableScope();
        try
        {
            foreach (var module in modules)
            {
                ThrowIfCancellationRequested();
                _currentModule = module;
                // Attribute diagnostics to the module being checked — without
                // this, errors raised inside module sources (including built-in
                // module declarations like events.ts) render as bare
                // "at line N" with no file context (#216).
                _filePath = module.Path;
                if (module.IsDefaultLibrary)
                {
                    CollectDefaultLibraryDeclarations(module, scriptEnv);
                    module.IsTypeChecked = true;
                }
                else if (module.IsScript)
                {
                    // Scripts use shared environment and don't export
                    CollectScriptDeclarations(module, scriptEnv);
                    if (module.IsDeclarationFile)
                        module.IsTypeChecked = true;
                }
                else
                {
                    CollectModuleExports(module, scriptEnv);
                    if (module.IsDeclarationFile)
                        module.IsTypeChecked = true;
                }
            }
        }
        finally
        {
            PopDeclaredVariableScope();
        }

        // External declaration modules (notably @types packages) commonly use
        // `declare global { ... }`. Their bodies are collected during the export
        // pass, then merged before any user module is checked.
        // Stdlib shims merge first: namespace merging is last-wins per key, so ordering
        // stdlib: modules ahead of everything else lets user/@types declarations (e.g. a
        // real @types/react JSX namespace) override the embedded react shim's. Stable
        // order is preserved within each group.
        var augmentationOrder = modules
            .OrderBy(m => m.Path.StartsWith("stdlib:", StringComparison.Ordinal) ? 0 : 1)
            .ToList();
        using (new EnvironmentScope(this, scriptEnv))
        {
            foreach (var module in augmentationOrder)
            {
                ThrowIfCancellationRequested();
                foreach (var augmentation in module.GlobalAugmentations)
                {
                    try { CheckAndMergeGlobalMember(augmentation); }
                    catch (TypeCheckException ex)
                    {
                        if (!module.IsDeclarationFile)
                            RecordTypeError(ex);
                    }
                }
            }
        }

        // Global augmentation checking can relate partially-built ambient types. Those verdicts
        // are preparatory and must not answer compatibility queries in the authoritative source
        // pass (recursive class shapes are particularly sensitive to the incomplete identities).
        _compatibilityCache = null;
        _identityCompatibilityCache = null;
        _compatibilityInProgress = null;
        _compatibilityCheckDepth = 0;

        // Second pass: type-check each module with imports resolved.
        foreach (var module in modules)
        {
            ThrowIfCancellationRequested();
            if (module.IsTypeChecked)
            {
                continue;
            }

            _currentModule = module;
            _filePath = module.Path; // diagnostic attribution — see first pass (#216)

            if (module.IsScript)
            {
                // Script files share the global script environment
                // Type-check in the shared script environment
                using (new EnvironmentScope(this, scriptEnv))
                {
                    // Pre-register type declarations (may have been done in first pass, but safe to repeat)
                    PreRegisterTypeDeclarations(module.Statements);

                    // Hoist class declarations (as Any for forward references)
                    HoistClassDeclarations(module.Statements);

                    // Hoist function declarations
                    HoistFunctionDeclarations(module.Statements);

                    // Hoist var declarations (pre-define as any for forward reference support)
                    HoistVarDeclarations(module.Statements);

                    // Hoist let/const declarations (pre-define as any for forward reference support — #533)
                    HoistLexicalDeclarations(module.Statements);

                    // Check all statements with error recovery
                    foreach (var stmt in module.Statements)
                    {
                        ThrowIfCancellationRequested();
                        // Fallback line for diagnostics whose throw-site doesn't carry one, mirroring
                        // the script path (CheckWithRecovery). Without it, module-mode errors render
                        // with no location (#468).
                        _currentStatementLine = TryGetStmtLine(stmt);
                        bool previousRecoveryMode = _recoveryMode;
                        // Namespace checking has its own two-pass/member loops. Let those nested
                        // loops recover independently; otherwise the first bad namespace member (or
                        // function statement inside it) aborts the rest of the namespace. Ordinary
                        // top-level functions remain statement-atomic so an error cannot leave
                        // partial flow narrowing behind and create cascade diagnostics.
                        _recoveryMode = previousRecoveryMode ||
                            stmt is Stmt.Namespace or Stmt.Export { Declaration: Stmt.Namespace };
                        try
                        {
                            CheckStmt(stmt);
                        }
                        catch (TypeCheckException ex)
                        {
                            RecordTypeError(ex);
                        }
                        finally
                        {
                            _recoveryMode = previousRecoveryMode;
                        }
                    }
                }
            }
            else
            {
                // Module files get isolated scope
                // Global script declarations (including lib.*.d.ts and global
                // augmentations) are visible inside external modules too.
                var moduleEnv = new TypeEnvironment(scriptEnv);

                // CJS modules have module, exports, and global in scope
                if (module.IsCommonJs)
                {
                    moduleEnv.Define("exports", TypeInfo.Any.Shared);
                    moduleEnv.Define("module", TypeInfo.Any.Shared);
                    moduleEnv.Define("global", TypeInfo.Any.Shared);
                }

                // Bind imports from dependencies
                BindModuleImports(module, moduleEnv, reportErrors: true);

                // Per-module declared-type frame: the module's top-level bindings (and class-method
                // locals, which record into the innermost frame) are tracked like function locals
                // and don't leak into sibling modules (#1218).
                PushDeclaredVariableScope();
                try
                {
                    // Type-check module body with error recovery
                    using (new EnvironmentScope(this, moduleEnv))
                    {
                        // First pass: pre-register type declarations
                        PreRegisterTypeDeclarations(module.Statements);

                        // Hoist class declarations (as Any for forward references)
                        HoistClassDeclarations(module.Statements);

                        // Second pass: hoist function declarations (now types are available)
                        HoistFunctionDeclarations(module.Statements);

                        // Hoist var declarations (pre-define as any for forward reference support)
                        HoistVarDeclarations(module.Statements);

                        // Hoist let/const declarations (pre-define as any for forward reference support — #533)
                        HoistLexicalDeclarations(module.Statements);

                        // Third pass: check all statements with error recovery
                        foreach (var stmt in module.Statements)
                        {
                            ThrowIfCancellationRequested();
                            // Fallback line for diagnostics whose throw-site doesn't carry one, mirroring
                            // the script path (CheckWithRecovery). Without it, module-mode errors render
                            // with no location (#468).
                            _currentStatementLine = TryGetStmtLine(stmt);
                            bool previousRecoveryMode = _recoveryMode;
                            _recoveryMode = previousRecoveryMode ||
                                stmt is Stmt.Namespace or Stmt.Export { Declaration: Stmt.Namespace };
                            try
                            {
                                CheckStmt(stmt);
                            }
                            catch (TypeCheckException ex)
                            {
                                RecordTypeError(ex);
                            }
                            finally
                            {
                                _recoveryMode = previousRecoveryMode;
                            }
                        }

                        ReportUnusedImports(module);
                    }
                }
                finally
                {
                    PopDeclaredVariableScope();
                }
            }

            module.IsTypeChecked = true;
        }

        _currentModule = null;
        _filePath = null;
        _currentStatementLine = null;
        return _typeMap;
    }

    private void ReportUnusedImports(ParsedModule module)
    {
        if (!_noUnusedLocals || module.Document is null)
            return;

        foreach (Stmt.Import import in module.Statements.OfType<Stmt.Import>())
        {
            if (import.IsSynthesizedJsxRuntime)
                continue;

            if (import.DefaultImport is { } defaultImport)
                ReportUnusedImportBinding(module.Document, defaultImport, [defaultImport]);
            if (import.NamespaceImport is { } namespaceImport)
                ReportUnusedImportBinding(module.Document, namespaceImport, [namespaceImport]);
            foreach (Stmt.ImportSpecifier specifier in import.NamedImports ?? [])
            {
                Token local = specifier.LocalName ?? specifier.Imported;
                Token[] declarationTokens = specifier.LocalName is null
                    ? [specifier.Imported]
                    : [specifier.Imported, specifier.LocalName];
                ReportUnusedImportBinding(module.Document, local, declarationTokens);
            }
        }
    }

    private void ReportUnusedImportBinding(
        SourceDocument document,
        Token local,
        IReadOnlyCollection<Token> declarationTokens)
    {
        IReadOnlyList<BindingSymbol> symbols = Bindings.FindSymbols(document, local.Start);
        if (symbols.Count == 0)
            return;

        bool used = Bindings.FindReferences(symbols, includeDeclarations: true).Any(occurrence =>
            ReferenceEquals(occurrence.Document, document) &&
            !declarationTokens.Contains(occurrence.Name, ReferenceEqualityComparer.Instance));
        used |= _synthesizedJsxUses.Contains((document, local.Lexeme));
        if (!used)
        {
            RecordTypeError(new TypeCheckException(
                $"'{local.Lexeme}' is declared but its value is never read.",
                local.Line,
                tsCode: "TS6133"));
        }
    }

    /// <summary>
    /// Binds a compiler-supplied lib.*.d.ts into the shared global environment.
    /// The pinned files are trusted inputs: unsupported declaration constructs
    /// are skipped locally rather than surfaced as errors in every user program.
    /// Successfully understood declarations still retain their full types.
    /// </summary>
    private void CollectDefaultLibraryDeclarations(ParsedModule library, TypeEnvironment scriptEnv)
    {
        using (new EnvironmentScope(this, scriptEnv))
        {
            try { PreRegisterTypeDeclarations(library.Statements); }
            catch { /* keep binding the remaining declarations below */ }
            try { HoistClassDeclarations(library.Statements); }
            catch { }
            try { HoistFunctionDeclarations(library.Statements); }
            catch { }
            try { HoistVarDeclarations(library.Statements); }
            catch { }
            try { HoistLexicalDeclarations(library.Statements); }
            catch { }

            _suppressDiagnostics++;
            bool previousRecoveryMode = _recoveryMode;
            _recoveryMode = true;
            try
            {
                foreach (var statement in library.Statements)
                {
                    try
                    {
                        CheckStmt(statement);
                    }
                    catch (Exception)
                    {
                        // A single declaration must not prevent later globals in
                        // the same standard library from being available.
                    }
                }
            }
            finally
            {
                _recoveryMode = previousRecoveryMode;
                _suppressDiagnostics--;
            }
        }
    }

    /// <summary>
    /// Collects declarations from a script file into the shared script environment.
    /// Scripts share global scope, so all declarations are visible to other scripts.
    /// </summary>
    private void CollectScriptDeclarations(ParsedModule script, TypeEnvironment scriptEnv)
    {
        using (new EnvironmentScope(this, scriptEnv))
        {
            // Pre-register type declarations (interfaces, enums, type aliases)
            PreRegisterTypeDeclarations(script.Statements);

            // Hoist function declarations
            HoistFunctionDeclarations(script.Statements);

            // Hoist var declarations (pre-define as any for forward reference support)
            HoistVarDeclarations(script.Statements);

            // Hoist let/const declarations (pre-define as any for forward reference support — #533)
            HoistLexicalDeclarations(script.Statements);

            // Process declarations to populate the shared environment. This is
            // only a preparatory pass; diagnostics and bodies are handled by the
            // authoritative recovery-enabled second pass below.
            _suppressDiagnostics++;
            bool previousRecoveryMode = _recoveryMode;
            if (script.IsDeclarationFile)
                _recoveryMode = true;
            try
            {
                foreach (var stmt in script.Statements)
                {
                    try
                    {
                        switch (stmt)
                        {
                            case Stmt.Function:
                            case Stmt.Class:
                            case Stmt.Interface:
                            case Stmt.TypeAlias:
                            case Stmt.Enum:
                            case Stmt.Namespace:
                            case Stmt.Var:
                            case Stmt.Const:
                                CheckStmt(stmt);
                                break;
                        }
                    }
                    catch (TypeCheckException)
                    {
                        // A bad declaration must not prevent later globals from
                        // being collected; it is reported in the second pass.
                    }
                }
            }
            finally
            {
                _recoveryMode = previousRecoveryMode;
                _suppressDiagnostics--;
            }
        }
    }

    /// <summary>
    /// Collects exports from a module (first pass - just register export types).
    /// </summary>
    private void CollectModuleExports(ParsedModule module, TypeEnvironment scriptEnv)
    {
        module.ExportedValueBindings.Clear();
        module.ExportedTypeBindings.Clear();
        module.DefaultValueBinding = null;
        module.DefaultTypeBinding = null;

        // External modules can refer to declarations contributed by referenced scripts.
        // This is especially common in UMD declaration files, where an ambient module
        // exports a global namespace (for example `export = __React`). Use the same
        // shared script environment as the authoritative module pass so the preparatory
        // export surface does not collapse to an empty namespace.
        var moduleEnv = new TypeEnvironment(scriptEnv);

        // CJS modules have module, exports, and global in scope
        if (module.IsCommonJs)
        {
            moduleEnv.Define("exports", TypeInfo.Any.Shared);
            moduleEnv.Define("module", TypeInfo.Any.Shared);
            moduleEnv.Define("global", TypeInfo.Any.Shared);
        }

        using (new EnvironmentScope(this, moduleEnv))
        {
            // First, bind imports so we can reference imported types in our declarations
            BindModuleImports(module, moduleEnv);

            // Pre-register type declarations first
            PreRegisterTypeDeclarations(module.Statements);

            // Hoist class declarations (as Any for forward references)
            HoistClassDeclarations(module.Statements);

            // Hoist function declarations (now types are available)
            HoistFunctionDeclarations(module.Statements);

            // Hoist var declarations (pre-define as any for forward reference support)
            HoistVarDeclarations(module.Statements);

            // Hoist let/const declarations (pre-define as any for forward reference support — #533)
            HoistLexicalDeclarations(module.Statements);

            // Then, process all declarations to populate the environment. This is a PREPARATORY
            // pass: it registers declaration/export types so forward references and exports resolve.
            // The authoritative body type-check (with source-location attribution) runs in
            // CheckModules' second pass, which re-checks every statement — so suppress diagnostics
            // here to avoid reporting each error twice, once in this pass and once in the second
            // (#468). The per-statement catch still lets collection continue past a faulty statement.
            _suppressDiagnostics++;
            try
            {
                foreach (var stmt in module.Statements)
                {
                    try
                    {
                        // Skip imports - already bound above
                        if (stmt is Stmt.Import)
                        {
                            continue;
                        }

                        // For exports, process the underlying declaration
                        if (stmt is Stmt.Export export)
                        {
                            if (export.GlobalNamespaceName != null)
                            {
                                // Applied after all exports have been collected.
                            }
                            else if (export.ExportAssignment != null)
                            {
                                // Evaluate export assignments after declarations. Ambient UMD
                                // packages commonly write `export = React` before the namespace
                                // declaration which supplies that symbol.
                            }
                            else if (export.Declaration != null)
                            {
                                CheckStmt(export.Declaration);
                            }
                            else if (export.DefaultExpr != null)
                            {
                                var type = CheckExpr(export.DefaultExpr);
                                module.DefaultExportType = type;
                                if (export.DefaultExpr is Expr.Variable variable)
                                {
                                    module.DefaultValueBinding =
                                        _environment.GetValueBinding(variable.Name.Lexeme);
                                }
                            }
                            else if (export.NamedExports != null && export.FromModulePath == null)
                            {
                                // Named exports like `export { x, y }` need the declarations to be processed first
                                // They'll be resolved in the second pass
                            }
                        }
                        else
                        {
                            // Regular declarations
                            CheckStmt(stmt);
                        }
                    }
                    catch (TypeCheckException ex)
                    {
                        RecordTypeError(ex);
                    }
                }
            }
            finally
            {
                _suppressDiagnostics--;
            }

            _suppressDiagnostics++;
            try
            {
                foreach (Stmt.Export assignment in module.Statements
                             .OfType<Stmt.Export>()
                             .Where(export => export.ExportAssignment != null))
                {
                    try
                    {
                        module.HasExportAssignment = true;
                        module.ExportAssignmentType = assignment.ExportAssignment is Expr.Variable variable &&
                                                      _environment.GetNamespace(variable.Name.Lexeme) is { } exportNamespace
                            ? exportNamespace
                            : CheckExpr(assignment.ExportAssignment!);
                    }
                    catch (TypeCheckException ex)
                    {
                        RecordTypeError(ex);
                    }
                }
            }
            finally
            {
                _suppressDiagnostics--;
            }

        // Now collect exports
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Export export)
            {
                if (export.IsDefaultExport)
                {
                    if (export.Declaration != null)
                    {
                        module.DefaultExportType = GetDeclaredType(export.Declaration);
                        RecordExportedDeclarationBindings(
                            module,
                            export.Declaration,
                            exportedName: "default",
                            isDefault: true);
                    }
                    // DefaultExpr already handled above
                }
                else if (export.Declaration != null)
                {
                    string name = GetDeclarationName(export.Declaration);
                    var type = GetDeclaredType(export.Declaration);
                    // Function/namespace declaration merging carries the namespace as the type
                    // facet and the function through its value binding. Retain the namespace so
                    // factory-local qualified types such as `predom.JSX.Element` survive import.
                    if (!module.ExportedTypes.TryGetValue(name, out TypeInfo? prior) ||
                        prior is not TypeInfo.Namespace || type is TypeInfo.Namespace)
                        module.ExportedTypes[name] = type;
                    RecordExportedDeclarationBindings(
                        module,
                        export.Declaration,
                        exportedName: name,
                        isDefault: false);
                }
                else if (export.NamedExports != null && export.FromModulePath == null)
                {
                    foreach (var spec in export.NamedExports)
                    {
                        bool isTypeOnly = export.IsTypeOnly || spec.IsTypeOnly;
                        var type = _environment.Get(spec.LocalName.Lexeme)
                            ?? _environment.GetTypeBinding(spec.LocalName.Lexeme);
                        string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                        if (type != null)
                        {
                            if (exportedName == "default")
                                module.DefaultExportType = type;
                            else
                                module.ExportedTypes[exportedName] = type;
                        }

                        string bindingName = spec.LocalName.Lexeme;
                        if (!isTypeOnly &&
                            _environment.GetValueBinding(bindingName) is { } valueBinding)
                        {
                            if (exportedName == "default")
                                module.DefaultValueBinding = valueBinding;
                            else
                                module.ExportedValueBindings[exportedName] = valueBinding;
                            BindExportSpecifier(spec, valueBinding);
                        }
                        if (_environment.GetTypeSymbol(bindingName) is { } typeBinding)
                        {
                            if (exportedName == "default")
                                module.DefaultTypeBinding = typeBinding;
                            else
                                module.ExportedTypeBindings[exportedName] = typeBinding;
                            BindExportSpecifier(spec, typeBinding);
                        }
                    }
                }
                else if (export.FromModulePath != null)
                {
                    // Re-export - resolve from the source module
                    string sourcePath;
                    try
                    {
                        sourcePath = _moduleResolver!.ResolveModulePath(
                            export.FromModulePath, module.Path);
                    }
                    catch
                    {
                        // The authoritative statement pass reports TS2307.
                        continue;
                    }
                    var sourceModule = _moduleResolver.GetCachedModule(sourcePath);

                    if (sourceModule != null)
                    {
                        if (export.NamespaceExportName != null)
                        {
                            var namespaceType = CreateModuleNamespaceType(
                                export.NamespaceExportName.Lexeme, sourceModule);
                            var namespaceValueBinding = Bindings.Declare(
                                export.NamespaceExportName,
                                CurrentSourceDocument,
                                BindingNamespace.Value);
                            var namespaceTypeBinding = Bindings.Declare(
                                export.NamespaceExportName,
                                CurrentSourceDocument,
                                BindingNamespace.Type);
                            if (export.NamespaceExportName.Type == TokenType.DEFAULT
                                || export.NamespaceExportName.Lexeme == "default")
                            {
                                module.DefaultExportType = namespaceType;
                                module.DefaultValueBinding = namespaceValueBinding;
                                module.DefaultTypeBinding = namespaceTypeBinding;
                            }
                            else
                            {
                                module.ExportedTypes[export.NamespaceExportName.Lexeme] = namespaceType;
                                module.ExportedValueBindings[export.NamespaceExportName.Lexeme] =
                                    namespaceValueBinding;
                                module.ExportedTypeBindings[export.NamespaceExportName.Lexeme] =
                                    namespaceTypeBinding;
                            }
                        }
                        else if (export.NamedExports != null)
                        {
                            // Re-export specific names. For CJS sources we don't know at type-check
                            // time which names the module's runtime body will define on `exports`
                            // (e.g. Babel's `Object.defineProperty(exports, "x", { get })` pattern),
                            // so trust the re-exporter and type each name as Any — mirrors the
                            // import-from-CJS handling in BindModuleImports.
                            foreach (var spec in export.NamedExports)
                            {
                                string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                                bool isTypeOnly = export.IsTypeOnly || spec.IsTypeOnly;
                                if (sourceModule.ExportedTypes.TryGetValue(spec.LocalName.Lexeme, out var type))
                                {
                                    module.ExportedTypes[exportedName] = type;
                                }
                                else if (sourceModule.IsCommonJs)
                                {
                                    module.ExportedTypes[exportedName] = TypeInfo.Any.Shared;
                                }

                                if (!isTypeOnly &&
                                    sourceModule.ExportedValueBindings.TryGetValue(
                                        spec.LocalName.Lexeme,
                                        out var valueBinding))
                                {
                                    module.ExportedValueBindings[exportedName] = valueBinding;
                                    BindExportSpecifier(spec, valueBinding);
                                }
                                if (sourceModule.ExportedTypeBindings.TryGetValue(
                                        spec.LocalName.Lexeme,
                                        out var typeBinding))
                                {
                                    module.ExportedTypeBindings[exportedName] = typeBinding;
                                    BindExportSpecifier(spec, typeBinding);
                                }
                            }
                        }
                        else
                        {
                            // Re-export all: export * from './module'
                            foreach (var (name, type) in sourceModule.ExportedTypes)
                            {
                                module.ExportedTypes[name] = type;
                            }
                            foreach (var (name, binding) in sourceModule.ExportedValueBindings)
                            {
                                module.ExportedValueBindings[name] = binding;
                            }
                            foreach (var (name, binding) in sourceModule.ExportedTypeBindings)
                            {
                                module.ExportedTypeBindings[name] = binding;
                            }
                        }
                    }
                }
            }
        }

        // Every declaration in an ambient external module is exported even
        // without an `export` modifier. The resolver represents
        // `declare module "elements" { class MyElement {} }` as a synthetic
        // module, so publish those declarations here before import-equals
        // consumers build their namespace view.
        if (module.IsAmbientModule)
        {
            foreach (Stmt declaration in module.Statements)
            {
                if (declaration is not (Stmt.Class or Stmt.Interface or Stmt.Function or
                    Stmt.Var or Stmt.Const or Stmt.TypeAlias or Stmt.Enum or Stmt.Namespace))
                {
                    continue;
                }

                string exportedName = GetDeclarationName(declaration);
                module.ExportedTypes[exportedName] = GetDeclaredType(declaration);
                RecordExportedDeclarationBindings(
                    module, declaration, exportedName, isDefault: false);
            }
        }

        // UMD declarations commonly pair `export = value` with
        // `export as namespace GlobalName`. Prefer the assignment type; for an
        // ESM-shaped declaration expose the complete module namespace.
        foreach (var globalExport in module.Statements
                     .OfType<Stmt.Export>()
                     .Where(export => export.GlobalNamespaceName != null))
        {
            string globalName = globalExport.GlobalNamespaceName!.Lexeme;
            TypeInfo globalType = module.ExportAssignmentType
                ?? CreateModuleNamespaceType(globalName, module);
            if (globalType is TypeInfo.Namespace globalNamespace)
                moduleEnv.Enclosing?.DefineNamespace(globalName, globalNamespace with { Name = globalName });
            else
                moduleEnv.Enclosing?.DefineImportAlias(globalName, globalType, isValue: true);
        }
        }
    }

    private void RecordExportedDeclarationBindings(
        ParsedModule module,
        Stmt declaration,
        string exportedName,
        bool isDefault)
    {
        string localName = GetDeclarationName(declaration);
        if (_environment.GetValueBinding(localName) is { } valueBinding)
        {
            if (isDefault)
                module.DefaultValueBinding = valueBinding;
            else
                module.ExportedValueBindings[exportedName] = valueBinding;
        }
        if (_environment.GetTypeSymbol(localName) is { } typeBinding)
        {
            if (isDefault)
                module.DefaultTypeBinding = typeBinding;
            else
                module.ExportedTypeBindings[exportedName] = typeBinding;
        }
    }

    private void BindExportSpecifier(Stmt.ExportSpecifier specifier, BindingSymbol binding)
    {
        Bindings.Bind(specifier.LocalName, CurrentSourceDocument, binding);
        if (specifier.ExportedName is { } exportedName)
            Bindings.Bind(exportedName, CurrentSourceDocument, binding);
    }

    private static TypeInfo.Namespace CreateModuleNamespaceType(
        string name, ParsedModule module)
    {
        var members = module.ExportedTypes.ToFrozenDictionary();
        return new TypeInfo.Namespace(
            name,
            members,
            members,
            module.ExportedTypeBindings.Count == 0
                ? null
                : module.ExportedTypeBindings.ToFrozenDictionary(),
            module.ExportedValueBindings.Count == 0
                ? null
                : module.ExportedValueBindings.ToFrozenDictionary());
    }

    private static TypeInfo? ResolveDeferredModuleExportAssignment(
        ParsedModule module,
        TypeEnvironment environment)
    {
        if (module.ExportAssignmentType is not null)
            return module.ExportAssignmentType;

        if (!module.HasExportAssignment || module.Statements
                .OfType<Stmt.Export>()
                .FirstOrDefault(export => export.ExportAssignment is Expr.Variable)
            is not { ExportAssignment: Expr.Variable assignment })
            return null;

        TypeInfo? resolved = environment.GetNamespace(assignment.Name.Lexeme)
            ?? module.ExportedTypes.GetValueOrDefault(assignment.Name.Lexeme);
        module.ExportAssignmentType = resolved;
        return resolved;
    }

    /// <summary>
    /// Binds imported symbols from dependencies into the module's environment.
    /// </summary>
    private void BindModuleImports(
        ParsedModule module, TypeEnvironment env, bool reportErrors = false)
    {
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Import import)
            {
                try
                {
                    if (import.IsSynthesizedJsxRuntime)
                    {
                        // Resolution already loaded a real runtime when one exists. For the
                        // checker these names are emitter plumbing: TypeScript does not expose
                        // missing-package/member diagnostics for a synthesized JSX import.
                        foreach (var spec in import.NamedImports ?? [])
                        {
                            Token local = spec.LocalName ?? spec.Imported;
                            env.Define(local.Lexeme, TypeInfo.Any.Shared);
                            BindImportedDeclaration(
                                env,
                                local,
                                BindingNamespace.Value,
                                importedToken: spec.Imported);
                        }
                        continue;
                    }

                    string importedPath = _moduleResolver!.ResolveModulePath(import.ModulePath, module.Path);
                    var importedModule = _moduleResolver.GetCachedModule(importedPath);

                    if (importedModule == null)
                    {
                        throw new TypeCheckException($"Cannot find module '{import.ModulePath}'", import.Keyword.Line, tsCode: "TS2307");
                    }

                    // CommonJS modules carry no static type info — treat all imports as `any`.
                    // The actual values come from `module.exports` at runtime via the CJS interop path.
                    bool isCjsImport = importedModule.IsCommonJs;

                    // Default import
                    if (import.DefaultImport != null)
                    {
                        if (isCjsImport)
                        {
                            env.Define(import.DefaultImport.Lexeme, TypeInfo.Any.Shared);
                            BindImportedDeclaration(
                                env,
                                import.DefaultImport,
                                BindingNamespace.Value);
                        }
                        else
                        {
                            TypeInfo? defaultImportType = importedModule.DefaultExportType;
                            if (defaultImportType is null && Options.AllowSyntheticDefaultImports &&
                                importedModule.HasExportAssignment)
                                defaultImportType = ResolveDeferredModuleExportAssignment(importedModule, env);
                            if (defaultImportType == null)
                            {
                                throw new TypeCheckException($"Module '{import.ModulePath}' has no default export", import.Keyword.Line, tsCode: "TS1192");
                            }
                            if (defaultImportType is TypeInfo.Namespace defaultNamespace)
                            {
                                env.DefineNamespace(
                                    import.DefaultImport.Lexeme,
                                    defaultNamespace with { Name = import.DefaultImport.Lexeme });
                            }
                            else
                            {
                                env.DefineImportAlias(
                                    import.DefaultImport.Lexeme,
                                    defaultImportType,
                                    isValue: !import.IsTypeOnly);
                            }

                            if (!import.IsTypeOnly)
                            {
                                BindImportedDeclaration(
                                    env,
                                    import.DefaultImport,
                                    BindingNamespace.Value,
                                    importedModule.DefaultValueBinding);
                            }
                            BindImportedDeclaration(
                                env,
                                import.DefaultImport,
                                BindingNamespace.Type,
                                importedModule.DefaultTypeBinding);
                        }
                    }

                    // Namespace import: import * as Module from './file'
                    if (import.NamespaceImport != null)
                    {
                        if (isCjsImport)
                        {
                            env.Define(import.NamespaceImport.Lexeme, TypeInfo.Any.Shared);
                            BindImportedDeclaration(
                                env,
                                import.NamespaceImport,
                                BindingNamespace.Value);
                        }
                        else
                        {
                            TypeInfo.Namespace namespaceType =
                                importedModule.HasExportAssignment &&
                                importedModule.ExportAssignmentType is TypeInfo.Namespace exportNamespace
                                    ? exportNamespace with { Name = import.NamespaceImport.Lexeme }
                                    : CreateModuleNamespaceType(import.NamespaceImport.Lexeme, importedModule);
                            env.DefineNamespace(import.NamespaceImport.Lexeme, namespaceType);
                            if (!import.IsTypeOnly)
                            {
                                BindImportedDeclaration(
                                    env,
                                    import.NamespaceImport,
                                    BindingNamespace.Value);
                            }
                            BindImportedDeclaration(
                                env,
                                import.NamespaceImport,
                                BindingNamespace.Type);
                        }
                    }

                    // Named imports: import { x, y as z } from './file'
                    if (import.NamedImports != null)
                    {
                        foreach (var spec in import.NamedImports)
                        {
                            string importedName = spec.Imported.Lexeme;
                            string localName = spec.LocalName?.Lexeme ?? importedName;

                            if (isCjsImport)
                            {
                                env.Define(localName, TypeInfo.Any.Shared);
                                BindImportedDeclaration(
                                    env,
                                    spec.LocalName ?? spec.Imported,
                                    BindingNamespace.Value,
                                    importedToken: spec.Imported);
                                continue;
                            }

                            if (!importedModule.ExportedTypes.TryGetValue(importedName, out var type))
                            {
                                throw new TypeCheckException($"Module '{import.ModulePath}' has no export named '{importedName}'", import.Keyword.Line, tsCode: "TS2305");
                            }

                            bool hasValueExport = importedModule.ExportedValueBindings.ContainsKey(importedName);
                            bool isTypeOnly = import.IsTypeOnly || spec.IsTypeOnly;
                            if (reportErrors &&
                                module.Path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) &&
                                !isTypeOnly &&
                                !hasValueExport)
                            {
                                RecordTypeError(new TypeCheckException(
                                    $"'{importedName}' is a type and cannot be imported in JavaScript files.",
                                    spec.Imported.Line,
                                    tsCode: "TS18042"));
                            }

                            if (type is TypeInfo.Namespace importedNamespace)
                            {
                                env.DefineNamespace(
                                    localName,
                                    importedNamespace with { Name = localName });
                                // A namespace declaration has a runtime value facet even when an
                                // ambient/export collection pass did not retain value-binding
                                // provenance for it. Named namespace imports are therefore valid
                                // JSX factory values unless explicitly type-only.
                                if (!isTypeOnly)
                                    env.DefineImportAlias(localName, importedNamespace, isValue: true);
                            }
                            else
                                env.DefineImportAlias(
                                    localName, type,
                                    // Some executable/embedded modules expose their value
                                    // surface without source binding provenance. Keep the
                                    // historical value alias for ordinary imports; the
                                    // binding map is still authoritative for the JSX-file
                                    // TS18042 diagnostic above.
                                    isValue: !isTypeOnly);

                            Token localToken = spec.LocalName ?? spec.Imported;
                            if (!isTypeOnly &&
                                importedModule.ExportedValueBindings.TryGetValue(
                                    importedName,
                                    out var valueBinding))
                            {
                                BindImportedDeclaration(
                                    env,
                                    localToken,
                                    BindingNamespace.Value,
                                    valueBinding,
                                    spec.Imported);
                            }
                            if (importedModule.ExportedTypeBindings.TryGetValue(
                                    importedName,
                                    out var typeBinding))
                            {
                                BindImportedDeclaration(
                                    env,
                                    localToken,
                                    BindingNamespace.Type,
                                    typeBinding,
                                    spec.Imported);
                            }
                        }
                    }
                }
                catch (TypeCheckException ex)
                {
                    if (reportErrors) RecordTypeError(ex);
                }
                catch (Exception)
                {
                    if (reportErrors)
                    {
                        RecordTypeError(new TypeCheckException(
                            $"Cannot find module '{import.ModulePath}'",
                            import.Keyword.Line, tsCode: "TS2307"));
                    }
                }
            }
        }
    }

    private BindingSymbol BindImportedDeclaration(
        TypeEnvironment environment,
        Token localName,
        BindingNamespace bindingNamespace,
        BindingSymbol? sourceBinding = null,
        Token? importedToken = null)
    {
        BindingSymbol binding = sourceBinding ?? Bindings.Declare(
            localName,
            CurrentSourceDocument,
            bindingNamespace);

        if (bindingNamespace == BindingNamespace.Value)
            environment.DefineValueBinding(localName.Lexeme, binding);
        else if (bindingNamespace == BindingNamespace.Type)
            environment.DefineTypeBinding(localName.Lexeme, binding);

        Bindings.Bind(localName, CurrentSourceDocument, binding);
        if (importedToken is not null && !ReferenceEquals(importedToken, localName))
            Bindings.Bind(importedToken, CurrentSourceDocument, binding);
        return binding;
    }

    /// <summary>
    /// Gets the name of a declaration (function, class, variable, etc.)
    /// </summary>
    private string GetDeclarationName(Stmt decl)
    {
        return decl switch
        {
            Stmt.Function f => f.Name.Lexeme,
            Stmt.Class c => c.Name.Lexeme,
            Stmt.Var v => v.Name.Lexeme,
            // `export const x = …` now parses as Stmt.Const (was Stmt.Var before #428).
            Stmt.Const c => c.Name.Lexeme,
            Stmt.Interface i => i.Name.Lexeme,
            Stmt.TypeAlias t => t.Name.Lexeme,
            Stmt.Enum e => e.Name.Lexeme,
            Stmt.Namespace n => n.Name.Lexeme,
            // SharpTS-only: internal invariant
            _ => throw new TypeCheckException($" Cannot get name of declaration type {decl.GetType().Name}")
        };
    }

    /// <summary>
    /// Gets the type of a declaration.
    /// </summary>
    private TypeInfo GetDeclaredType(Stmt decl)
    {
        return decl switch
        {
            Stmt.Function f => _environment.Get(f.Name.Lexeme) ?? TypeInfo.Any.Shared,
            Stmt.Class c => _environment.GetTypeBinding(c.Name.Lexeme) ?? TypeInfo.Any.Shared,
            Stmt.Var v => _environment.Get(v.Name.Lexeme) ?? TypeInfo.Any.Shared,
            // `export const x = …` now parses as Stmt.Const (was Stmt.Var before #428). The
            // environment holds the narrowed literal type recorded by VisitConst.
            Stmt.Const c => _environment.Get(c.Name.Lexeme) ?? TypeInfo.Any.Shared,
            Stmt.Interface i => _environment.GetTypeBinding(i.Name.Lexeme) ?? TypeInfo.Any.Shared,
            Stmt.TypeAlias t => ResolveAnnotation(t.TypeDefinition, t.TypeDefinitionNode)!,
            Stmt.Enum e => _environment.GetTypeBinding(e.Name.Lexeme) ?? TypeInfo.Any.Shared,
            Stmt.Namespace n => _environment.GetNamespace(n.Name.Lexeme) is { } ns
                ? ns
                : TypeInfo.Any.Shared,
            _ => TypeInfo.Any.Shared
        };
    }

    /// <summary>
    /// Best-effort extraction of a 1-based line number from a statement, by digging into
    /// the first identifier/operator token. Returns null when the statement has no token
    /// directly addressable. Used to attribute diagnostics to a line for pragma matching
    /// when the throw site didn't include line info itself.
    /// </summary>
    private static int? TryGetStmtLine(Stmt stmt) => stmt switch
    {
        Stmt.Expression e => TryGetExprLine(e.Expr),
        Stmt.Var v => v.Name.Line,
        Stmt.Const c => c.Name.Line,
        Stmt.Function f => f.Name.Line,
        Stmt.Class c => c.Name.Line,
        Stmt.Interface i => i.Name.Line,
        Stmt.Return r => r.Value is not null ? TryGetExprLine(r.Value) : null,
        Stmt.If i => TryGetExprLine(i.Condition),
        Stmt.While w => TryGetExprLine(w.Condition),
        _ => null,
    };

    private static int? TryGetExprLine(Expr expr) => expr switch
    {
        Expr.Variable v => v.Name.Line,
        Expr.Set s => s.Name.Line,
        Expr.Get g => g.Name.Line,
        Expr.Assign a => a.Name.Line,
        Expr.Binary b => b.Operator.Line,
        Expr.Logical l => l.Operator.Line,
        Expr.Unary u => u.Operator.Line,
        Expr.CompoundAssign ca => ca.Name.Line,
        Expr.CompoundSet cs => cs.Name.Line,
        Expr.Call c => TryGetExprLine(c.Callee),
        Expr.New n => TryGetExprLine(n.Callee),
        // Index forms carry no token of their own — attribute to their object's line so a write
        // through a bracket (`part.subparts[0] = …` → TS2542) lands on its own statement rather
        // than falling back to the enclosing declaration (#365).
        Expr.SetIndex si => TryGetExprLine(si.Object),
        Expr.GetIndex gi => TryGetExprLine(gi.Object),
        Expr.CompoundSetIndex csi => TryGetExprLine(csi.Object),
        _ => null,
    };
}
