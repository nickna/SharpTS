using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Statement type checking - CheckStmt and the main dispatch switch.
/// </summary>
/// <remarks>
/// Contains the main statement dispatch (CheckStmt) and inline handling for simple
/// statements. Complex statement handlers are split into separate partial files:
/// <list type="bullet">
///   <item><description><c>TypeChecker.Statements.Classes.cs</c> - Class declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Interfaces.cs</c> - Interface declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Functions.cs</c> - Function declaration and overload handling</description></item>
///   <item><description><c>TypeChecker.Statements.Enums.cs</c> - Enum declaration with const enum support</description></item>
///   <item><description><c>TypeChecker.Statements.ControlFlow.cs</c> - Block, switch, try/catch checking</description></item>
///   <item><description><c>TypeChecker.Statements.Modules.cs</c> - Export statement checking</description></item>
/// </list>
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Type-checks a statement. Dispatches to the appropriate Visit* method via the registry.
    /// </summary>
    /// <param name="stmt">The statement AST node to type-check.</param>
    private void CheckStmt(Stmt stmt)
    {
        _registry.DispatchStmt(stmt, this);
    }

    // Statement handlers - called by the registry

    internal VoidResult VisitBlock(Stmt.Block stmt)
    {
        CheckBlock(stmt.Statements, new TypeEnvironment(_environment));
        return VoidResult.Instance;
    }

    internal VoidResult VisitSequence(Stmt.Sequence stmt)
    {
        foreach (var s in stmt.Statements)
            CheckStmt(s);
        return VoidResult.Instance;
    }

    internal VoidResult VisitLabeledStatement(Stmt.LabeledStatement stmt)
    {
        string labelName = stmt.Label.Lexeme;

        // Check for label shadowing
        if (_activeLabels.ContainsKey(labelName))
        {
            throw new TypeCheckException($"Label '{labelName}' already declared in this scope", tsCode: "TS2300");
        }

        // Determine if this label is on a loop (for continue validation)
        bool isOnLoop = stmt.Statement is Stmt.While
                     or Stmt.For
                     or Stmt.DoWhile
                     or Stmt.ForOf
                     or Stmt.ForIn
                     or Stmt.LabeledStatement; // Allow chained labels

        // If chained label, inherit loop status from inner
        if (stmt.Statement is Stmt.LabeledStatement)
        {
            isOnLoop = true;
        }

        _activeLabels[labelName] = isOnLoop;
        try
        {
            CheckStmt(stmt.Statement);
        }
        finally
        {
            _activeLabels.Remove(labelName);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitInterface(Stmt.Interface stmt)
    {
        CheckInterfaceDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitTypeAlias(Stmt.TypeAlias stmt)
    {
        if (stmt.TypeParameters != null && stmt.TypeParameters.Count > 0)
        {
            var typeParamNames = stmt.TypeParameters.Select(tp => tp.Name.Lexeme).ToList();
            ValidateTypeAliasSpreadConstraints(stmt);
            _environment.DefineGenericTypeAlias(stmt.Name.Lexeme, stmt.TypeDefinition, typeParamNames, stmt.TypeDefinitionNode);
        }
        else
        {
            _environment.DefineTypeAlias(stmt.Name.Lexeme, stmt.TypeDefinition);
        }
        // After defining (so the alias stays usable even when a clause is malformed): validate
        // the infer declarations of every conditional type in the alias body.
        var outerTypeParams = stmt.TypeParameters?.Select(tp => tp.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
        ValidateInferDeclarations(stmt.TypeDefinitionNode, outerTypeParams);
        RecordAliasParamConstraints(stmt);
        ValidateAliasBody(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitEnum(Stmt.Enum stmt)
    {
        CheckEnumDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitNamespace(Stmt.Namespace stmt)
    {
        CheckNamespace(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitImportAlias(Stmt.ImportAlias stmt)
    {
        CheckImportAlias(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitClass(Stmt.Class stmt)
    {
        CheckClassDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitVar(Stmt.Var stmt)
    {
        // Captured before any provisional Define overwrites it — used for the TS2403
        // redeclaration check once this declaration's type settles. Locally only: a same-named
        // var in an OUTER scope is shadowing, not redeclaration.
        TypeInfo? preExistingType = stmt.IsVar && _environment.IsDefinedLocally(stmt.Name.Lexeme)
            ? _environment.Get(stmt.Name.Lexeme) : null;

        // Node-first annotation resolution (type-AST migration), string fallback.
        TypeInfo? declaredType = ResolveAnnotation(stmt.TypeAnnotation, stmt.TypeAnnotationNode);

        // TS2304: a bare annotation name that resolves to nothing (e.g. `var a: A;` where the
        // only `A` lives in a nested module, invisible here). See ReportUnknownTypeName.
        if (declaredType is TypeInfo.Any)
            ReportUnknownTypeName(stmt.TypeAnnotation, stmt.TypeAnnotationNode, stmt.Name.Line);

        // VarHoister carries the first nested declaration's initializer onto the synthetic hoisted
        // `var` (which itself has no Initializer) when that declaration had no annotation. Infer the
        // binding's declared type from it so a later `var z: number;` / `var z = 5;` reports TS2403
        // instead of being silenced by an `any` placeholder. (See Stmt.Var.HoistTypeInferenceInitializer.)
        if (declaredType is null && stmt.Initializer is null && stmt.HoistTypeInferenceInitializer is { } inferenceSource)
        {
            declaredType = InferHoistedVarType(inferenceSource);
        }

        if (stmt.HasDefiniteAssignmentAssertion)
        {
            _environment.Define(stmt.Name.Lexeme, declaredType!);
            CheckVarRedeclaration(stmt, preExistingType, declaredType!);
            // Record the declared type for assignment checking
            RecordDeclaredType(stmt.Name.Lexeme, declaredType!);
            // Register as local variable for escape analysis
            _escapeAnalyzer.DefineVariable(stmt.Name.Lexeme);
            return VoidResult.Instance;
        }

        if (stmt.Initializer != null)
        {
            var provisionalType = declaredType ?? new TypeInfo.Any();
            _environment.Define(stmt.Name.Lexeme, provisionalType);
            // Register as local variable for escape analysis
            _escapeAnalyzer.DefineVariable(stmt.Name.Lexeme);

            // Track variable-to-variable aliases for narrowing invalidation
            // e.g., "const alias = obj" tracks that "alias" is an alias for "obj"
            if (stmt.Initializer is Expr.Variable initVar)
            {
                var initType = _environment.Get(initVar.Name.Lexeme);
                if (initType != null && IsObjectType(initType))
                {
                    _variableAliases[stmt.Name.Lexeme] = initVar.Name.Lexeme;
                }
            }

            if (declaredType is TypeInfo.Tuple tupleType && stmt.Initializer is Expr.ArrayLiteral arrayLit)
            {
                CheckArrayLiteralAgainstTuple(arrayLit, tupleType, stmt.Name.Lexeme);
            }
            else
            {
                Expr initializer = stmt.Initializer;
                bool checkExcessProps = false;

                if (declaredType != null && initializer is Expr.ObjectLiteral objLit)
                {
                    initializer = objLit with { IsFresh = true };
                    checkExcessProps = true;
                }

                TypeInfo initializerType;
                if (declaredType != null && initializer is Expr.ArrowFunction arrowFn)
                {
                    initializerType = CheckArrowFunction(arrowFn, declaredType);
                }
                else
                {
                    // An un-annotated synthetic temp may carry a contextual shape (the destructuring
                    // binding pattern) used ONLY to guide inference — never as the binding's type and with
                    // no compatibility check; the inferred result below is still what's kept (#783).
                    TypeInfo? context = declaredType ?? ResolveAnnotation(null, stmt.InitializerContext);
                    initializerType = CheckExprWithContext(initializer, context);
                }

                if (declaredType != null)
                {
                    if (checkExcessProps && initializerType is TypeInfo.Record actualRecord)
                    {
                        CheckExcessProperties(actualRecord, declaredType, stmt.Initializer);
                    }

                    if (!IsCompatible(declaredType, initializerType))
                    {
                        throw new TypeMismatchException(declaredType, initializerType, stmt.Name.Line, tsCode: AssignmentDiagnosticCode(declaredType, initializerType));
                    }
                }
                else
                {
                    initializerType = WidenLiteralType(initializerType);
                    // JS: `let x = null` / `let x = undefined` leave x freely reassignable.
                    // Widen top-level null/undefined to any for untyped let/var.
                    if (initializerType is TypeInfo.Null or TypeInfo.Undefined)
                    {
                        initializerType = new TypeInfo.Any();
                    }
                    declaredType = initializerType;
                    _environment.Define(stmt.Name.Lexeme, declaredType);
                }

                declaredType ??= initializerType;
            }
            CheckVarRedeclaration(stmt, preExistingType, declaredType!);
            // Record the declared type for assignment checking
            RecordDeclaredType(stmt.Name.Lexeme, declaredType!);
            return VoidResult.Instance;
        }

        declaredType ??= new TypeInfo.Any();
        _environment.Define(stmt.Name.Lexeme, declaredType);
        CheckVarRedeclaration(stmt, preExistingType, declaredType);
        // Record the declared type for assignment checking
        RecordDeclaredType(stmt.Name.Lexeme, declaredType);
        return VoidResult.Instance;
    }

    /// <summary>
    /// TS2304 ("Cannot find name 'X'"): a variable/const annotated with a bare type name that
    /// resolves to nothing — not a primitive/keyword, built-in lib type, in-scope type parameter,
    /// type alias, or declared class/interface/enum. A class hoisted for a forward reference is
    /// registered as an <c>any</c> placeholder, so it still counts as declared and is exempt.
    ///
    /// Reported only at the declaration check site, which runs after PreRegisterTypeDeclarations +
    /// class/var hoisting for the enclosing top-level / module / namespace scope — so an unresolved
    /// name there is genuinely undeclared (e.g. a class nested in an inner module, invisible to the
    /// outer scope: <c>var a: A; module M { class A {} }</c> ⇒ TS2304). Gated to non-function-body
    /// scopes: function and plain-block bodies do NOT pre-register their nested type/class
    /// declarations, so a forward reference inside one resolves to <c>any</c> and must stay lenient
    /// (tsc accepts it via hoisting). Deliberately narrower than a throw at the general ToTypeInfo
    /// <c>any</c> fallback, whose leniency is load-bearing during hoisting and infer resolution.
    /// </summary>
    private void ReportUnknownTypeName(string? annotation, TypeNode? annotationNode, int line)
    {
        // Function/method bodies (and the blocks within them) don't pre-register nested types, so a
        // forward reference there would falsely look undeclared. Only fire where hoisting is complete.
        if (_currentFunctionReturnType is not null) return;

        if (BareTypeReferenceName(annotation, annotationNode) is not { } name) return;

        // `any` is the keyword, not an unknown name. Everything below is already known to resolve to
        // `any` (the caller checked), so a name bound anywhere type-visible is a forward/known type:
        // a class/interface/enum/value binding or hoisted placeholder (Get), a type parameter, a type
        // alias, or a mapped-type parameter currently being parsed. None of those are "cannot find".
        if (name == "any") return;
        if (_environment.Get(name) is not null) return;
        if (_environment.GetTypeParameter(name) is not null) return;
        if (_environment.GetTypeAlias(name) is not null) return;
        if (_environment.GetGenericTypeAlias(name) is not null) return;
        if (_openTypeVariablesInScope?.Contains(name) == true) return;

        throw new TypeCheckException($" Cannot find name '{name}'.", line, tsCode: "TS2304");
    }

    /// <summary>
    /// The referenced type name when <paramref name="annotationNode"/> / <paramref name="annotation"/>
    /// is a single bare type reference with no type arguments — a plain identifier such as <c>A</c>,
    /// NOT <c>A&lt;T&gt;</c>, <c>A[]</c>, <c>A | B</c>, <c>{ … }</c>, or a qualified <c>N.Id</c>;
    /// otherwise null. A qualified name is excluded deliberately: it resolves through the namespace
    /// path (permissively to <c>any</c>), so it isn't a "cannot find name" candidate here. Prefers the
    /// structured node and only falls back to the string when no node is present.
    /// </summary>
    private static string? BareTypeReferenceName(string? annotation, TypeNode? annotationNode)
    {
        string? candidate = annotationNode is not null
            ? (annotationNode is NamedTypeNode { TypeArguments: null } named ? named.Name : null)
            : annotation?.Trim();
        return candidate is not null && IsSimpleIdentifier(candidate) ? candidate : null;
    }

    /// <summary>A non-empty string of identifier characters with no dots, brackets, or operators.</summary>
    private static bool IsSimpleIdentifier(string s)
    {
        if (s.Length == 0) return false;
        if (!(char.IsLetter(s[0]) || s[0] is '_' or '$')) return false;
        for (int i = 1; i < s.Length; i++)
            if (!(char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$')) return false;
        return true;
    }

    /// <summary>
    /// TS2403: subsequent `var` declarations of the same name in the same scope must keep the
    /// same type (`var r4 = fooA(...)` then `var r4 = fooB(...)` with a different result type).
    /// Identical re-declarations are legal JS/TS and pass silently.
    /// </summary>
    private void CheckVarRedeclaration(Stmt.Var stmt, TypeInfo? previous, TypeInfo newType)
    {
        // `Any` covers both the var-hoisting placeholder (first declaration) and explicit
        // any-typed vars — neither participates in the redeclaration check.
        if (!stmt.IsVar || previous is null or TypeInfo.Any) return;
        if (!TypeInfoEqualityComparer.Instance.Equals(previous, newType))
        {
            throw new TypeCheckException(
                $" Subsequent variable declarations must have the same type. Variable '{stmt.Name.Lexeme}' must be of type '{previous}', but here has type '{newType}'.",
                line: stmt.Name.Line, tsCode: "TS2403");
        }
    }

    /// <summary>
    /// Infers the declared type of a hoisted <c>var</c> from the first nested declaration's
    /// initializer (carried on <see cref="Stmt.Var.HoistTypeInferenceInitializer"/>). The literal
    /// type is widened to match how <c>var x = expr;</c> is normally typed, and top-level
    /// null/undefined widen to <c>any</c>. Errors are suppressed and degrade to <c>any</c>: the
    /// initializer is also checked at its original (rewritten) position, where any real diagnostic
    /// is reported with the correct location. The speculative check runs in its own narrowing scope
    /// so it leaves no narrowings behind.
    /// </summary>
    private TypeInfo? InferHoistedVarType(Expr initializer)
    {
        PushEmptyNarrowingScope();
        try
        {
            TypeInfo inferred = WidenLiteralType(CheckExpr(initializer));
            if (inferred is TypeInfo.Null or TypeInfo.Undefined)
                return new TypeInfo.Any();
            return inferred;
        }
        catch (TypeMismatchException) { return null; }
        catch (TypeCheckException) { return null; }
        finally { PopNarrowingScope(); }
    }

    internal VoidResult VisitConst(Stmt.Const stmt)
    {
        TypeInfo constDeclaredType;

        // Track variable-to-variable aliases for narrowing invalidation
        // e.g., "const alias = obj" tracks that "alias" is an alias for "obj"
        if (stmt.Initializer is Expr.Variable initVar)
        {
            var initType = _environment.Get(initVar.Name.Lexeme);
            if (initType != null && IsObjectType(initType))
            {
                _variableAliases[stmt.Name.Lexeme] = initVar.Name.Lexeme;
            }
        }

        if (stmt.TypeAnnotation == "unique symbol")
        {
            if (stmt.Initializer is not Expr.Call call ||
                call.Callee is not Expr.Variable v ||
                v.Name.Lexeme != "Symbol")
            {
                throw new TypeCheckException(
                    $"'unique symbol' must be initialized with Symbol() at line {stmt.Name.Line}.", tsCode: "TS1331");
            }
            if (call.Arguments.Count > 0)
            {
                var argType = CheckExpr(call.Arguments[0]);
                if (argType is not TypeInfo.String && argType is not TypeInfo.StringLiteral && argType is not TypeInfo.Any)
                    throw new TypeCheckException($"Symbol() description must be a string.", tsCode: "TS2345");
            }
            constDeclaredType = new TypeInfo.UniqueSymbol(
                stmt.Name.Lexeme,
                $"typeof {stmt.Name.Lexeme}");
        }
        else if (stmt.TypeAnnotation != null)
        {
            constDeclaredType = ResolveAnnotation(stmt.TypeAnnotation, stmt.TypeAnnotationNode)!;
            if (constDeclaredType is TypeInfo.Any)
                ReportUnknownTypeName(stmt.TypeAnnotation, stmt.TypeAnnotationNode, stmt.Name.Line);
            _environment.Define(stmt.Name.Lexeme, constDeclaredType);
            var initType = CheckExprWithContext(stmt.Initializer, constDeclaredType);
            if (!IsCompatible(constDeclaredType, initType))
            {
                throw new TypeMismatchException(constDeclaredType, initType, stmt.Name.Line, tsCode: AssignmentDiagnosticCode(constDeclaredType, initType));
            }
        }
        else
        {
            _environment.Define(stmt.Name.Lexeme, new TypeInfo.Any());
            constDeclaredType = WidenConstInitializerType(stmt.Initializer, CheckExpr(stmt.Initializer));
            _environment.Define(stmt.Name.Lexeme, constDeclaredType);
        }

        _environment.Define(stmt.Name.Lexeme, constDeclaredType);
        // Register as local variable for escape analysis
        _escapeAnalyzer.DefineVariable(stmt.Name.Lexeme);
        return VoidResult.Instance;
    }

    internal VoidResult VisitFunction(Stmt.Function stmt)
    {
        CheckFunctionDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitReturn(Stmt.Return stmt)
    {
        if (_inStaticBlock)
        {
            throw new TypeCheckException("Return statements are not allowed in static blocks.", tsCode: "TS1108");
        }

        // When inferring return type, collect the type instead of validating
        if (_inferredReturnTypes != null && _currentFunctionReturnType is TypeInfo.Inferred)
        {
            if (stmt.Value != null)
            {
                TypeInfo actualType = CheckExpr(stmt.Value);
                _inferredReturnTypes.Add(actualType);
            }
            else
            {
                _inferredReturnTypes.Add(new TypeInfo.Undefined());
            }
            return VoidResult.Instance;
        }

        if (_currentFunctionReturnType != null)
        {
            if (_currentFunctionReturnType is TypeInfo.Tuple tupleRetType &&
                stmt.Value is Expr.ArrayLiteral arrayLitRet)
            {
                CheckArrayLiteralAgainstTuple(arrayLitRet, tupleRetType, "return value");
            }
            else
            {
                TypeInfo expectedReturnType = _currentFunctionReturnType;
                if (_inAsyncFunction && expectedReturnType is TypeInfo.Promise promiseType)
                {
                    expectedReturnType = promiseType.ValueType;
                }

                TypeInfo actualReturnType = stmt.Value != null
                    ? CheckExprWithContext(stmt.Value, expectedReturnType)
                    : new TypeInfo.Void();

                MarkIfUndefinedReachableNumericReturn(stmt.Value, actualReturnType);

                if (_inGeneratorFunction && expectedReturnType is TypeInfo.Void)
                {
                    // Allow any return value in generators with no explicit return type
                }
                else if (!IsCompatible(expectedReturnType, actualReturnType))
                {
                    throw new TypeMismatchException(_currentFunctionReturnType, actualReturnType, stmt.Keyword.Line, tsCode: "TS2322");
                }
            }
        }
        else if (stmt.Value != null)
        {
            CheckExpr(stmt.Value);
        }
        return VoidResult.Instance;
    }

    /// <summary>
    /// When a return value whose runtime value can be the <c>undefined</c> sentinel (i.e. a
    /// statically <c>any</c>/<c>unknown</c> value, e.g. <c>return undefined as any</c>) flows into
    /// a declared <c>number</c>/<c>boolean</c> return type, the IL compiler would give the function
    /// an unboxed <c>double</c>/<c>bool</c> return slot. That slot cannot carry <c>undefined</c>, so
    /// it is silently coerced to <c>NaN</c>/<c>false</c> (#344). Record the value expression so the
    /// compiler widens that one function's slot back to <c>object</c>. <c>any</c>/<c>unknown</c> are
    /// the only statically-checked types whose runtime value can be <c>undefined</c> while still
    /// satisfying a <c>number</c>/<c>boolean</c> annotation (an exact annotation otherwise rejects
    /// <c>undefined</c>; an inferred return that admits it widens to a union → object slot).
    /// Compiler hint only — caller-side checking still sees the clean <c>number</c>/<c>boolean</c>.
    /// </summary>
    private void MarkIfUndefinedReachableNumericReturn(Expr? value, TypeInfo actualType)
    {
        if (value == null) return;

        TypeInfo declared = _currentFunctionReturnType ?? actualType;
        if (_inAsyncFunction && declared is TypeInfo.Promise promised) declared = promised.ValueType;
        if (declared is not TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN })
            return;

        if (ReturnValueMayBeUndefinedSentinel(value))
            _typeMap.MarkUndefinedReachableReturn(value);
    }

    private bool ReturnValueMayBeUndefinedSentinel(Expr value) =>
        ValueMayBeUndefinedSentinel(value, taintedLocals: null);

    /// <summary>
    /// True if <paramref name="value"/> can evaluate to a runtime value of static type
    /// <c>any</c>/<c>unknown</c> (which therefore can be the <c>undefined</c> sentinel). Recurses
    /// through "pass-through" forms whose result is one of their operands — groupings, ternaries,
    /// and <c>||</c>/<c>&amp;&amp;</c>/<c>??</c> — because those collapse a mixed branch type (e.g.
    /// <c>42 | any</c> → <c>42</c>) and hide the <c>any</c> at the top level. Leaf types come from
    /// the <see cref="TypeMap"/>, which has every sub-expression recorded by the time a return is
    /// checked. Over-approximates (both ternary branches), which is sound: at worst a function that
    /// could never produce <c>undefined</c> keeps an object slot.
    /// <para>
    /// When <paramref name="taintedLocals"/> is supplied (#367), a leaf reference to one of those
    /// locals also counts: a <c>number</c>/<c>boolean</c>-typed local statically reports its narrow
    /// type at a <c>return</c>, hiding that an earlier <c>any</c>/<c>undefined</c> assignment left it
    /// holding the sentinel. The same predicate grows the taint set (transitive assignments) and
    /// flags the returns, so both stay in lock-step.
    /// </para>
    /// </summary>
    private bool ValueMayBeUndefinedSentinel(Expr value, HashSet<string>? taintedLocals) => value switch
    {
        Expr.Grouping g => ValueMayBeUndefinedSentinel(g.Expression, taintedLocals),
        Expr.Ternary t => ValueMayBeUndefinedSentinel(t.ThenBranch, taintedLocals)
                       || ValueMayBeUndefinedSentinel(t.ElseBranch, taintedLocals),
        Expr.Logical l => ValueMayBeUndefinedSentinel(l.Left, taintedLocals)
                       || ValueMayBeUndefinedSentinel(l.Right, taintedLocals),
        Expr.NullishCoalescing n => ValueMayBeUndefinedSentinel(n.Left, taintedLocals)
                                 || ValueMayBeUndefinedSentinel(n.Right, taintedLocals),
        Expr.Assign a => ValueMayBeUndefinedSentinel(a.Value, taintedLocals),
        Expr.Variable v when taintedLocals != null && taintedLocals.Contains(v.Name.Lexeme) => true,
        _ => TypeAdmitsUndefinedSentinel(_typeMap.Get(value))
    };

    private static bool TypeAdmitsUndefinedSentinel(TypeInfo? type) => type switch
    {
        TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Undefined => true,
        TypeInfo.Union u => u.Types.Any(TypeAdmitsUndefinedSentinel),
        _ => false
    };

    /// <summary>
    /// #367/#372 residual of #344: a <c>number</c>/<c>boolean</c>-typed <em>local or parameter</em>
    /// can be unsoundly assigned an <c>any</c>/<c>undefined</c> value (e.g.
    /// <c>let x: number = undefined as any</c> or <c>x = undefined as any</c> on a <c>x: number</c>
    /// parameter) and so hold the runtime <c>undefined</c> sentinel. A <c>number</c>/<c>boolean</c>
    /// slot is unboxed (<c>double</c>/<c>bool</c>), which cannot carry the sentinel — storing it
    /// coerces to <c>NaN</c>/<c>false</c> (a never-initialized double arg slot yields raw garbage).
    /// The narrow static type at every later use (a <c>return x</c>, a <c>console.log(x)</c>, an
    /// arithmetic op) reports <c>number</c>/<c>boolean</c>, hiding the taint from the per-expression
    /// #344 detection.
    ///
    /// <para>
    /// Run a whole-body taint pass <em>after</em> the body is type-checked (every sub-expression's
    /// type is then in the <see cref="TypeMap"/>): compute the set of names that may hold the
    /// sentinel to a fixpoint — seeded by direct <c>any</c>/<c>undefined</c> assignments and grown
    /// transitively through other tainted names — then flag, so the compiler widens each affected
    /// slot back to <c>object</c>:
    /// <list type="bullet">
    ///   <item>tainted local <em>declarations</em> (their unboxed double slot would corrupt at the store);</item>
    ///   <item>tainted <em>parameters</em> (their unboxed double arg slot likewise corrupts on reassignment);</item>
    ///   <item><em>returns</em> of a tainted name (the unboxed return slot would corrupt at the return).</item>
    /// </list>
    /// </para>
    ///
    /// Order-independent, so a taint reaching an earlier use via a loop back-edge is still caught.
    /// Over-approximates (no narrowing): at worst a needless object slot, never a wrong value. Runs
    /// regardless of the declared return type — unlike #367 it is not gated on a <c>number</c>/<c>boolean</c>
    /// return, because the corruption happens at the local/param store, independent of how (or
    /// whether) the value is returned. Each flag is a no-op unless the slot it names would otherwise
    /// be unboxed, so widening returns/locals/params that are not numeric costs nothing.
    /// </summary>
    private void MarkUndefinedReachableNumericSlots(
        IReadOnlyList<Stmt> body,
        IReadOnlyList<Stmt.Parameter>? parameters = null)
    {
        var collected = ReturnLocalTaintCollector.Collect(body);
        if (collected.Assignments.Count == 0) return;

        var tainted = new HashSet<string>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (name, assignedValue) in collected.Assignments)
            {
                if (tainted.Contains(name)) continue;
                if (ValueMayBeUndefinedSentinel(assignedValue, tainted))
                {
                    tainted.Add(name);
                    changed = true;
                }
            }
        }
        if (tainted.Count == 0) return;

        // Local slot: object-slot every tainted local so its declaration does not get an unboxed
        // double slot that would coerce the sentinel to NaN at the store (e.g. the intermediate `z`
        // in `let y = undefined as any; let z = y; return z`, or simply a local that is only logged).
        // Both the declaration node and its initializer are flagged — `const` is recompiled through a
        // fresh Stmt.Var that reuses the original initializer expression.
        foreach (var (name, node, initializer) in collected.Declarations)
        {
            if (!tainted.Contains(name)) continue;
            _typeMap.MarkUndefinedReachableNumericLocal(node);
            if (initializer != null) _typeMap.MarkUndefinedReachableNumericLocal(initializer);
        }

        // Parameter slot: object-slot every tainted parameter (e.g. `function p(x: number) { x =
        // undefined as any; ... }`) so the compiler does not give it an unboxed double/bool arg slot.
        // A parameter has no declaration node in `collected`; it is matched by name.
        if (parameters != null)
            foreach (var param in parameters)
                if (tainted.Contains(param.Name.Lexeme))
                    _typeMap.MarkUndefinedReachableNumericParam(param);

        // Return slot: flag returns of a tainted name so the compiler widens the otherwise-unboxed
        // double/bool return slot back to object.
        foreach (var ret in collected.Returns)
            if (ValueMayBeUndefinedSentinel(ret, tainted))
                _typeMap.MarkUndefinedReachableReturn(ret);
    }

    internal VoidResult VisitExpression(Stmt.Expression stmt)
    {
        CheckExpr(stmt.Expr);
        if (stmt.Expr is Expr.Call assertCall)
        {
            ApplyAssertionNarrowing(assertCall);
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitIf(Stmt.If stmt)
    {
        CheckExpr(stmt.Condition);

        // Try compound type guard analysis first (handles && conditions like x !== null && y !== null)
        var compoundGuards = AnalyzeCompoundTypeGuards(stmt.Condition);

        if (compoundGuards.Count > 0)
        {
            // Apply all narrowings for compound conditions
            ApplyCompoundNarrowings(stmt, compoundGuards);
        }
        else
        {
            // Fall back to legacy type guard analysis for other patterns (typeof, instanceof, etc.)
            var guard = AnalyzeTypeGuard(stmt.Condition);

            if (guard.VarName != null)
            {
                var thenEnv = new TypeEnvironment(_environment);
                thenEnv.Define(guard.VarName, guard.NarrowedType!);
                using (new EnvironmentScope(this, thenEnv))
                {
                    CheckStmt(stmt.ThenBranch);
                }

                if (stmt.ElseBranch != null && guard.ExcludedType != null)
                {
                    var elseEnv = new TypeEnvironment(_environment);
                    elseEnv.Define(guard.VarName, guard.ExcludedType);
                    using (new EnvironmentScope(this, elseEnv))
                    {
                        CheckStmt(stmt.ElseBranch);
                    }
                }
                else if (stmt.ElseBranch != null)
                {
                    CheckStmt(stmt.ElseBranch);
                }

                if (stmt.ElseBranch == null && guard.ExcludedType != null && AlwaysTerminates(stmt.ThenBranch))
                {
                    _environment.Define(guard.VarName, guard.ExcludedType);
                }
                else if (stmt.ElseBranch != null && guard.NarrowedType != null &&
                         AlwaysTerminates(stmt.ElseBranch) && !AlwaysTerminates(stmt.ThenBranch))
                {
                    _environment.Define(guard.VarName, guard.NarrowedType);
                }
            }
            else
            {
                CheckStmt(stmt.ThenBranch);
                if (stmt.ElseBranch != null) CheckStmt(stmt.ElseBranch);

                // `if (A || B) return;` — when the then-branch always
                // terminates and there is no else, the code after the if sees
                // the negation of EVERY disjunct (De Morgan): apply each
                // disjunct's excluded type. Handles the early-return guard
                // idiom `if (x == null || x.length === 0) return;` (#216) —
                // compound analysis only decomposes &&, and the legacy guard
                // can't see through ||.
                if (stmt.ElseBranch == null
                    && AlwaysTerminates(stmt.ThenBranch)
                    && stmt.Condition is Expr.Logical { } topOr
                    && topOr.Operator.Type == TokenType.OR_OR)
                {
                    ApplyDisjunctExclusions(topOr);
                }
            }
        }
        return VoidResult.Instance;
    }

    /// <summary>
    /// Applies each disjunct's excluded type to the current environment.
    /// Used after a terminating then-branch with no else, where the negation
    /// of every disjunct is known to hold. Variable paths only — property
    /// path exclusions would need a narrowing-context push scoped to the
    /// rest of the enclosing block, which the context stack doesn't model.
    /// </summary>
    private void ApplyDisjunctExclusions(Expr.Logical orExpr)
    {
        foreach (var (path, _, excludedType) in CollectDisjunctGuards(orExpr))
        {
            if (path is Narrowing.NarrowingPath.Variable varPath)
            {
                _environment.Define(varPath.Name, excludedType);
            }
        }
    }

    /// <summary>
    /// Applies compound narrowings from multiple type guards (e.g., x !== null && y !== null).
    /// </summary>
    private void ApplyCompoundNarrowings(
        Stmt.If stmt,
        List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> narrowings)
    {
        // Separate variable and property path narrowings
        var varNarrowings = narrowings.Where(n => n.Path is Narrowing.NarrowingPath.Variable).ToList();
        var propNarrowings = narrowings.Where(n => n.Path is not Narrowing.NarrowingPath.Variable).ToList();

        // Build then branch environment with all variable narrowings
        var thenEnv = new TypeEnvironment(_environment);
        foreach (var (path, narrowedType, _) in varNarrowings)
        {
            if (path is Narrowing.NarrowingPath.Variable varPath)
            {
                thenEnv.Define(varPath.Name, narrowedType);
            }
        }

        // Build then branch context with all property narrowings
        var thenContext = Narrowing.NarrowingContext.Empty;
        foreach (var (path, narrowedType, _) in propNarrowings)
        {
            thenContext = thenContext.WithNarrowing(path, narrowedType);
        }

        // Check then branch with all narrowings applied
        using (new EnvironmentScope(this, thenEnv))
        {
            if (!thenContext.IsEmpty)
            {
                PushNarrowingContext(thenContext);
            }
            try
            {
                CheckStmt(stmt.ThenBranch);
            }
            finally
            {
                if (!thenContext.IsEmpty)
                {
                    PopNarrowingContext();
                }
            }
        }

        // For else branch, apply excluded types
        if (stmt.ElseBranch != null)
        {
            // For a single condition, we can apply the excluded type in the else branch
            // For compound conditions (&&), the else branch means at least one condition is false,
            // so we can't safely narrow all variables to their excluded types
            if (narrowings.Count == 1)
            {
                var (path, _, excludedType) = narrowings[0];

                // Build else branch environment with excluded type for variables
                var elseEnv = new TypeEnvironment(_environment);
                var elseContext = Narrowing.NarrowingContext.Empty;

                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    elseEnv.Define(varPath.Name, excludedType);
                }
                else
                {
                    elseContext = elseContext.WithNarrowing(path, excludedType);
                }

                using (new EnvironmentScope(this, elseEnv))
                {
                    if (!elseContext.IsEmpty)
                    {
                        PushNarrowingContext(elseContext);
                    }
                    try
                    {
                        CheckStmt(stmt.ElseBranch);
                    }
                    finally
                    {
                        if (!elseContext.IsEmpty)
                        {
                            PopNarrowingContext();
                        }
                    }
                }
            }
            else
            {
                // For compound conditions, just check without narrowing
                CheckStmt(stmt.ElseBranch);
            }
        }

        // Handle early termination: if then branch terminates, apply excluded types after
        if (stmt.ElseBranch == null && AlwaysTerminates(stmt.ThenBranch))
        {
            foreach (var (path, _, excludedType) in narrowings)
            {
                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    _environment.Define(varPath.Name, excludedType);
                }
                else
                {
                    AddNarrowing(path, excludedType);
                }
            }
        }
    }

    internal VoidResult VisitWhile(Stmt.While stmt)
    {
        CheckExpr(stmt.Condition);
        var conditionNarrowings = AnalyzeLoopConditionNarrowings(stmt.Condition);
        CheckLoopBody(stmt.Body, conditionNarrowings);
        ApplyLoopExitNarrowings(conditionNarrowings);
        return VoidResult.Instance;
    }

    internal VoidResult VisitDoWhile(Stmt.DoWhile stmt)
    {
        // Do-while runs body first, so no condition narrowings in body
        CheckLoopBody(stmt.Body, conditionNarrowings: null);
        CheckExpr(stmt.Condition);
        return VoidResult.Instance;
    }

    internal VoidResult VisitFor(Stmt.For stmt)
    {
        if (stmt.Initializer != null)
            CheckStmt(stmt.Initializer);

        List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> conditionNarrowings = [];
        if (stmt.Condition != null)
        {
            CheckExpr(stmt.Condition);
            conditionNarrowings = AnalyzeLoopConditionNarrowings(stmt.Condition);
        }

        // Include increment expression in assigned paths analysis
        HashSet<Narrowing.NarrowingPath>? incrementPaths = null;
        if (stmt.Increment != null)
        {
            incrementPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(
                new Stmt.Expression(stmt.Increment));
        }

        CheckLoopBody(stmt.Body, conditionNarrowings, incrementPaths);

        if (stmt.Increment != null)
            CheckExpr(stmt.Increment);

        ApplyLoopExitNarrowings(conditionNarrowings);
        return VoidResult.Instance;
    }

    internal VoidResult VisitForOf(Stmt.ForOf stmt)
    {
        // `for await...of` drives the async-iterator protocol, so — like an `await` expression or
        // `await using` — it is only valid inside an async function. SharpTS does not support
        // top-level await, so a top-level (or otherwise non-async-context) `for await` is rejected
        // here rather than silently degrading to a synchronous `for...of`, which would then fail at
        // runtime with a misleading 'not iterable' error (#672). Mirrors CheckAwait / CheckUsingDeclaration.
        if (stmt.IsAsync && !_inAsyncFunction)
        {
            throw new TypeCheckException(
                "'await' is only valid inside an async function.",
                stmt.Variable.Line, tsCode: "TS1308");
        }

        TypeInfo iterableType = CheckExpr(stmt.Iterable);

        // Now that generator yield-type inference draws from the `yield` / `yield*` operands (#548), the
        // Generator/AsyncGenerator yield type is real (a delegating-only generator infers its delegate's
        // element type, not `void`), so `for...of` can bind it directly instead of falling to `any`. A sync
        // generator is also a valid `for await...of` source (each yield is awaited), so its arm is unguarded;
        // the async-only records bind only under `for await`.
        TypeInfo elementType = iterableType switch
        {
            TypeInfo.Array arr => arr.ElementType,
            TypeInfo.Map mapType => TypeInfo.Tuple.FromTypes([mapType.KeyType, mapType.ValueType], 2),
            TypeInfo.Set setType => setType.ElementType,
            TypeInfo.Iterator iterType => iterType.ElementType,
            TypeInfo.Iterable iterableElem => iterableElem.ElementType,
            TypeInfo.Generator genType => genType.YieldType,
            TypeInfo.AsyncGenerator asyncGenType when stmt.IsAsync => asyncGenType.YieldType,
            TypeInfo.AsyncIterator asyncItType when stmt.IsAsync => asyncItType.ElementType,
            TypeInfo.AsyncIterable asyncIter when stmt.IsAsync => asyncIter.ElementType,
            // A hand-written object exposing [Symbol.asyncIterator] is async-iterable structurally (#662).
            _ when stmt.IsAsync && TryGetStructuralAsyncIterableElement(iterableType, out var asyncStructuralElem) => asyncStructuralElem,
            // A hand-written object exposing [Symbol.iterator] is iterable structurally (#485).
            _ when !stmt.IsAsync && TryGetStructuralIterableElement(iterableType, out var structuralElem) => structuralElem,
            // A structural object with no [Symbol.iterator] is an iterator-only or plain object, not an
            // Iterable — tsc rejects the loop with TS2488 rather than binding `any` (#550). Gated to types
            // SharpTS can prove non-iterable so it never rejects code tsc accepts (see the helper). Async
            // sources remain lenient (no TS2488 analog for async — async non-iterability is rarer and not
            // yet diagnosed).
            _ when !stmt.IsAsync && IsProvablyNonIterableStructuralObject(iterableType) =>
                throw new TypeCheckException(
                    $" Type '{iterableType}' must have a '[Symbol.iterator]()' method that returns an iterator.",
                    tsCode: "TS2488"),
            _ => new TypeInfo.Any()
        };

        TypeEnvironment forOfEnv = new(_environment);
        forOfEnv.Define(stmt.Variable.Lexeme, elementType);

        CheckLoopBody(stmt.Body, conditionNarrowings: null, loopEnvironment: forOfEnv);
        return VoidResult.Instance;
    }

    internal VoidResult VisitForIn(Stmt.ForIn stmt)
    {
        TypeInfo objType = CheckExpr(stmt.Object);

        if (objType is not (TypeInfo.Record or TypeInfo.Instance or TypeInfo.Array or TypeInfo.Any or TypeInfo.Class))
        {
            throw new TypeCheckException($"'for...in' requires an object, got {objType}", tsCode: "TS2549");
        }

        TypeEnvironment forInEnv = new(_environment);
        forInEnv.Define(stmt.Variable.Lexeme, new TypeInfo.String());

        CheckLoopBody(stmt.Body, conditionNarrowings: null, loopEnvironment: forInEnv);
        return VoidResult.Instance;
    }

    #region Loop Helper Methods

    /// <summary>
    /// Analyzes a loop condition for type narrowings (handles both compound && conditions and simple guards).
    /// </summary>
    private List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)>
        AnalyzeLoopConditionNarrowings(Expr condition)
    {
        var narrowings = AnalyzeCompoundTypeGuards(condition);
        if (narrowings.Count == 0)
        {
            var (path, narrowedType, excludedType) = AnalyzePathTypeGuard(condition);
            if (path != null && narrowedType != null && excludedType != null)
            {
                narrowings.Add((path, narrowedType, excludedType));
            }
        }
        return narrowings;
    }

    /// <summary>
    /// Checks a loop body with proper narrowing scope management and assignment invalidation.
    /// </summary>
    /// <param name="body">The loop body statement</param>
    /// <param name="conditionNarrowings">Narrowings from condition to apply in body (null for do-while, for-of, for-in)</param>
    /// <param name="additionalAssignedPaths">Additional paths to consider as assigned (e.g., from increment)</param>
    /// <param name="loopEnvironment">Custom environment for the loop (e.g., for-of/for-in iterator variable)</param>
    private void CheckLoopBody(
        Stmt body,
        List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)>? conditionNarrowings,
        HashSet<Narrowing.NarrowingPath>? additionalAssignedPaths = null,
        TypeEnvironment? loopEnvironment = null)
    {
        // Get assigned paths from body
        var assignedPaths = ControlFlow.LoopAssignmentAnalyzer.GetAssignedPaths(body);
        if (additionalAssignedPaths != null)
        {
            foreach (var p in additionalAssignedPaths)
                assignedPaths.Add(p);
        }

        // Set up loop environment if provided
        var prevEnv = _environment;
        if (loopEnvironment != null)
        {
            _environment = loopEnvironment;
        }

        _loopDepth++;
        try
        {
            PushNarrowingScope();
            try
            {
                // Invalidate narrowings for any paths assigned within the loop
                foreach (var assignedPath in assignedPaths)
                {
                    InvalidateNarrowingsFor(assignedPath);
                }

                // Apply the loop-condition narrowings. The condition is re-evaluated at the top of
                // every iteration, so its narrowing holds at the top of the body even when the body
                // reassigns the guarded variable later: the reassignment is on a downstream flow edge
                // and is handled by the normal in-body flow analysis (CheckAssign invalidates the
                // narrowing at the assignment point, so uses *after* it widen back). Suppressing the
                // narrowing whenever the variable is assigned *anywhere* in the body over-invalidated
                // uses that precede the reassignment, diverging from tsc (#556).
                if (conditionNarrowings != null)
                {
                    foreach (var (condPath, narrowedType, _) in conditionNarrowings)
                    {
                        AddNarrowing(condPath, narrowedType);
                    }
                }

                CheckStmt(body);
            }
            finally
            {
                PopNarrowingScope();
            }
        }
        finally
        {
            _loopDepth--;
            if (loopEnvironment != null)
            {
                _environment = prevEnv;
            }
        }
    }

    /// <summary>
    /// Applies exit narrowings after a loop completes (the excluded types from the condition).
    /// </summary>
    private void ApplyLoopExitNarrowings(
        List<(Narrowing.NarrowingPath Path, TypeInfo NarrowedType, TypeInfo ExcludedType)> narrowings)
    {
        foreach (var (condPath, _, excludedType) in narrowings)
        {
            AddNarrowing(condPath, excludedType);
        }
    }

    #endregion

    internal VoidResult VisitBreak(Stmt.Break stmt)
    {
        if (stmt.Label != null)
        {
            string labelName = stmt.Label.Lexeme;
            if (!_activeLabels.ContainsKey(labelName))
            {
                throw new TypeCheckException($"Label '{labelName}' not found", tsCode: "TS1116");
            }
        }
        else
        {
            if (_loopDepth == 0 && _switchDepth == 0)
            {
                throw new TypeOperationException("'break' can only be used inside a loop or switch", tsCode: "TS1105");
            }
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitSwitch(Stmt.Switch stmt)
    {
        CheckSwitch(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitTryCatch(Stmt.TryCatch stmt)
    {
        CheckTryCatch(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitThrow(Stmt.Throw stmt)
    {
        CheckExpr(stmt.Value);
        return VoidResult.Instance;
    }

    internal VoidResult VisitContinue(Stmt.Continue stmt)
    {
        if (stmt.Label != null)
        {
            string labelName = stmt.Label.Lexeme;
            if (!_activeLabels.TryGetValue(labelName, out bool isOnLoop))
            {
                throw new TypeCheckException($"Label '{labelName}' not found", tsCode: "TS1116");
            }
            if (!isOnLoop)
            {
                throw new TypeOperationException($"Cannot continue to non-loop label '{labelName}'", tsCode: "TS1116");
            }
        }
        else
        {
            if (_loopDepth == 0)
            {
                throw new TypeOperationException("'continue' can only be used inside a loop", tsCode: "TS1104");
            }
        }
        return VoidResult.Instance;
    }

    internal VoidResult VisitPrint(Stmt.Print stmt)
    {
        CheckExpr(stmt.Expr);
        return VoidResult.Instance;
    }

    internal VoidResult VisitImport(Stmt.Import stmt)
    {
        if (_currentModule == null)
        {
            // dotnet: imports need no module graph — they resolve via reflection, so
            // single-file checking (and the language server, which checks buffers in
            // isolation) can bind them here. Module mode binds them in BindModuleImports
            // from the module's pre-resolved ExportedTypes instead.
            if (Modules.DotNetImports.IsDotNetSpecifier(stmt.ModulePath))
            {
                BindStandaloneDotNetImport(stmt);
                return VoidResult.Instance;
            }

            // SharpTS-only: module-mode requirement (continued message)
            throw new TypeCheckException("Import statements require module mode. " +
                               "Use 'dotnet run -- --compile' with multi-file support", stmt.Keyword.Line);
        }
        return VoidResult.Instance;
    }

    /// <summary>
    /// Binds a <c>dotnet:</c> import outside module mode by resolving each named import via
    /// reflection and synthesizing its static type — the same resolution and synthesis the
    /// module path uses (<c>DotNetImports.EnsureExport</c>), minus the module bookkeeping.
    /// </summary>
    private void BindStandaloneDotNetImport(Stmt.Import import)
    {
        if (import.DefaultImport != null || import.NamespaceImport != null)
        {
            throw new TypeCheckException(
                $"'{import.ModulePath}' supports named imports only, e.g. import {{ TypeName }} from \"{import.ModulePath}\".",
                import.Keyword.Line);
        }

        if (import.NamedImports == null) return;

        string specifier = import.ModulePath[Modules.DotNetImports.Prefix.Length..];
        foreach (var spec in import.NamedImports)
        {
            Type clrType;
            try
            {
                clrType = Modules.DotNetImports.ResolveExportType(specifier, spec.Imported.Lexeme);
            }
            catch (Exception ex)
            {
                throw new TypeCheckException(ex.Message, spec.Imported.Line);
            }
            _environment.Define(spec.LocalName?.Lexeme ?? spec.Imported.Lexeme, DotNetTypeSynthesizer.Synthesize(clrType));
        }
    }

    internal VoidResult VisitImportRequire(Stmt.ImportRequire stmt)
    {
        CheckImportRequire(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitExport(Stmt.Export stmt)
    {
        CheckExportStatement(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitFileDirective(Stmt.FileDirective stmt)
    {
        ValidateFileDirective(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitDirective(Stmt.Directive stmt) => VoidResult.Instance;
    internal VoidResult VisitStaticBlock(Stmt.StaticBlock stmt) => VoidResult.Instance;

    internal VoidResult VisitDeclareModule(Stmt.DeclareModule stmt)
    {
        CheckDeclareModuleStatement(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitDeclareGlobal(Stmt.DeclareGlobal stmt)
    {
        CheckDeclareGlobalStatement(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitUsing(Stmt.Using stmt)
    {
        CheckUsingDeclaration(stmt);
        return VoidResult.Instance;
    }

    internal VoidResult VisitField(Stmt.Field stmt) => VoidResult.Instance;
    internal VoidResult VisitAccessor(Stmt.Accessor stmt) => VoidResult.Instance;
    internal VoidResult VisitAutoAccessor(Stmt.AutoAccessor stmt) => VoidResult.Instance;

    /// <summary>
    /// Type checks a 'using' or 'await using' declaration.
    /// Validates the basic structure and defines variables in scope.
    /// Actual dispose method validation is done at runtime for flexibility.
    /// </summary>
    private void CheckUsingDeclaration(Stmt.Using usingStmt)
    {
        // 'await using' is only valid inside async functions
        if (usingStmt.IsAsync && !_inAsyncFunction)
        {
            throw new TypeCheckException(
                "'await using' is only allowed inside an async function.",
                usingStmt.Keyword.Line, tsCode: "TS1308");
        }

        foreach (var binding in usingStmt.Bindings)
        {
            TypeInfo initType = CheckExpr(binding.Initializer);

            // Check for declared type annotation
            TypeInfo? declaredType = null;
            if (binding.TypeAnnotation != null)
            {
                declaredType = ResolveAnnotation(binding.TypeAnnotation, binding.TypeAnnotationNode);
                if (!IsCompatible(declaredType, initType))
                {
                    throw new TypeMismatchException(declaredType, initType, binding.Name!.Line, tsCode: "TS2322");
                }
            }

            TypeInfo resourceType = declaredType ?? initType;

            // Validate that the type is object-like (can have Symbol.dispose method)
            // Primitive types cannot have dispose methods
            if (resourceType is TypeInfo.Primitive prim && !IsNullablePrimitive(prim))
            {
                throw new TypeCheckException(
                    $"Type '{resourceType}' cannot be used with 'using' - it cannot have a disposal method.",
                    usingStmt.Keyword.Line, tsCode: "TS2851");
            }

            // Define variable (const-like - cannot reassign)
            if (binding.Name != null)
            {
                _environment.Define(binding.Name.Lexeme, resourceType);
            }
        }
    }

    /// <summary>
    /// Checks if a primitive type is nullable (null or undefined).
    /// </summary>
    private static bool IsNullablePrimitive(TypeInfo.Primitive prim)
    {
        return prim.Type == TokenType.NULL || prim.Type == TokenType.UNDEFINED;
    }

    /// <summary>
    /// Validates file-level directives like @Namespace.
    /// </summary>
    private void ValidateFileDirective(Stmt.FileDirective directive)
    {
        foreach (var decorator in directive.Decorators)
        {
            if (decorator.Expression is Expr.Call call &&
                call.Callee is Expr.Variable v &&
                v.Name.Lexeme == "Namespace")
            {
                if (call.Arguments.Count != 1)
                {
                    // SharpTS-only: @Namespace decorator validation
                    throw new TypeCheckException("@Namespace requires exactly one string argument", decorator.AtToken.Line);
                }
                if (call.Arguments[0] is not Expr.Literal { Value: string })
                {
                    // SharpTS-only: @Namespace decorator validation
                    throw new TypeCheckException("@Namespace argument must be a string literal", decorator.AtToken.Line);
                }
            }
            else
            {
                // SharpTS-only: file-level directive validation
                throw new TypeCheckException("Unknown file-level directive. Only @Namespace is supported", decorator.AtToken.Line);
            }
        }
    }

    /// <summary>
    /// Determines if a statement always terminates (returns, throws, etc.).
    /// Used for control flow analysis to determine if narrowed types persist.
    /// </summary>
    private static bool AlwaysTerminates(Stmt stmt) => stmt switch
    {
        Stmt.Return => true,
        Stmt.Throw => true,
        Stmt.Block block => block.Statements.Count > 0 && AlwaysTerminates(block.Statements[^1]),
        Stmt.Sequence seq => seq.Statements.Count > 0 && AlwaysTerminates(seq.Statements[^1]),
        Stmt.If ifStmt => AlwaysTerminates(ifStmt.ThenBranch) &&
                         ifStmt.ElseBranch != null && AlwaysTerminates(ifStmt.ElseBranch),
        _ => false
    };

    /// <summary>
    /// Validates the <c>infer</c> declarations of every conditional type in a type node tree:
    /// all declarations of one infer name within an extends clause must have identical constraints
    /// (TS2838; declarations without a constraint are exempt), and an infer constraint cannot
    /// reference other infer parameters declared in the same extends clause (TS2304 — they are not
    /// in scope there). No-op when the annotation has no node (string fallback).
    /// </summary>
    private void ValidateInferDeclarations(TypeNode? node, HashSet<string>? outerTypeParams)
    {
        if (node is null) return;
        if (node is ConditionalTypeNode conditional)
            ValidateInferClauseOf(conditional, outerTypeParams);
        foreach (var child in TypeNodeChildren(node))
            ValidateInferDeclarations(child, outerTypeParams);
    }

    private void ValidateInferClauseOf(ConditionalTypeNode conditional, HashSet<string>? outerTypeParams)
    {
        List<InferTypeNode>? infers = null;
        CollectClauseInfers(conditional.ExtendsType, ref infers);
        if (infers is null) return;

        // TS2838: among same-named declarations, every *constrained* one must agree.
        foreach (var group in infers.GroupBy(i => i.Name))
        {
            TypeInfo? first = null;
            foreach (var decl in group)
            {
                if (decl.Constraint is null) continue;
                var constraint = TryToTypeInfo(decl.Constraint);
                if (constraint is null) continue; // unresolvable constraint — don't guess
                if (first is null)
                {
                    first = constraint;
                }
                else if (!TypeInfoEqualityComparer.Instance.Equals(first, constraint))
                {
                    throw new TypeCheckException(
                        $"All declarations of '{group.Key}' must have identical constraints.",
                        decl.Line, tsCode: "TS2838");
                }
            }
        }

        // TS2304: sibling infer parameters are not in scope inside a constraint. A name that is
        // also an outer type parameter resolves to the outer declaration instead — no error.
        var clauseNames = infers.Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        if (outerTypeParams is not null)
            clauseNames.ExceptWith(outerTypeParams);
        foreach (var decl in infers)
        {
            if (decl.Constraint is { } constraintNode &&
                FindReferenceTo(constraintNode, clauseNames) is { } reference)
            {
                throw new TypeCheckException(
                    $"Cannot find name '{reference.Name}'.", reference.Line, tsCode: "TS2304");
            }
        }
    }

    /// <summary>
    /// Collects the infer declarations belonging to one extends clause. Nested conditional types
    /// are their own inference scope, so descent stops at them.
    /// </summary>
    private static void CollectClauseInfers(TypeNode node, ref List<InferTypeNode>? result)
    {
        if (node is ConditionalTypeNode) return;
        if (node is InferTypeNode infer)
            (result ??= []).Add(infer);
        foreach (var child in TypeNodeChildren(node))
            CollectClauseInfers(child, ref result);
    }

    /// <summary>
    /// Finds the first bare type reference to one of <paramref name="names"/> inside a constraint
    /// (stopping at nested conditional scopes), or null.
    /// </summary>
    private static NamedTypeNode? FindReferenceTo(TypeNode node, HashSet<string> names)
    {
        if (node is ConditionalTypeNode) return null;
        if (node is NamedTypeNode named && named.TypeArguments is null && names.Contains(named.Name))
            return named;
        foreach (var child in TypeNodeChildren(node))
        {
            if (FindReferenceTo(child, names) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Enumerates the direct child type nodes of a node, across every node shape.
    /// </summary>
    private static IEnumerable<TypeNode> TypeNodeChildren(TypeNode node)
    {
        switch (node)
        {
            case NamedTypeNode { TypeArguments: { } args }:
                foreach (var a in args) yield return a;
                break;
            case ReadonlyTypeNode r:
                yield return r.Inner;
                break;
            case TypePredicateNode p:
                yield return p.PredicateType;
                break;
            case ArrayTypeNode a:
                yield return a.ElementType;
                break;
            case UnionTypeNode u:
                foreach (var m in u.Members) yield return m;
                break;
            case IntersectionTypeNode i:
                foreach (var m in i.Members) yield return m;
                break;
            case KeyofTypeNode k:
                yield return k.Operand;
                break;
            case IndexedAccessTypeNode ia:
                yield return ia.ObjectType;
                yield return ia.IndexType;
                break;
            case ConditionalTypeNode c:
                yield return c.CheckType;
                yield return c.ExtendsType;
                yield return c.TrueType;
                yield return c.FalseType;
                break;
            case InferTypeNode { Constraint: { } constraint }:
                yield return constraint;
                break;
            case FunctionTypeNode f:
                if (f.ThisType is { } thisType) yield return thisType;
                foreach (var param in f.Parameters) yield return param.Type;
                yield return f.ReturnType;
                break;
            case ConstructorTypeNode ct:
                foreach (var param in ct.Parameters) yield return param.Type;
                yield return ct.ReturnType;
                break;
            case GenericConstructorTypeNode gc:
                yield return gc.Body;
                break;
            case GenericFunctionTypeNode gf:
                yield return gf.Body;
                break;
            case TemplateLiteralTypeNode tl:
                foreach (var t in tl.InterpolatedTypes) yield return t;
                break;
            case ObjectTypeNode o:
                foreach (var member in o.Members)
                {
                    switch (member)
                    {
                        case PropertyMemberNode prop: yield return prop.Type; break;
                        case IndexSignatureNode idx: yield return idx.ValueType; break;
                        case CallSignatureMemberNode call: yield return call.Signature; break;
                        case ConstructSignatureMemberNode ctor: yield return ctor.Signature; break;
                    }
                }
                break;
            case MappedTypeNode m:
                yield return m.Constraint;
                yield return m.ValueType;
                if (m.AsClause is { } asClause) yield return asClause;
                break;
            case TupleTypeNode tu:
                foreach (var elem in tu.Elements) yield return elem.Type;
                break;
        }
    }

    /// <summary>
    /// Validates that any spread elements in a type alias definition reference type parameters
    /// that are constrained to array-like types (e.g., T extends unknown[]).
    /// </summary>
    private void ValidateTypeAliasSpreadConstraints(Stmt.TypeAlias typeAlias)
    {
        if (typeAlias.TypeParameters == null || typeAlias.TypeParameters.Count == 0)
            return;

        // Build a dictionary of type parameter constraints
        var constraints = new Dictionary<string, string?>();
        foreach (var tp in typeAlias.TypeParameters)
        {
            constraints[tp.Name.Lexeme] = tp.Constraint;
        }

        // Find spread patterns like ...T in the definition
        string definition = typeAlias.TypeDefinition;
        int idx = 0;
        while ((idx = definition.IndexOf("...", idx, StringComparison.Ordinal)) >= 0)
        {
            idx += 3; // Skip past "..."
            if (idx >= definition.Length)
                break;

            // Skip whitespace
            while (idx < definition.Length && char.IsWhiteSpace(definition[idx]))
                idx++;

            if (idx >= definition.Length)
                break;

            // Extract the type name after ...
            int start = idx;
            while (idx < definition.Length && (char.IsLetterOrDigit(definition[idx]) || definition[idx] == '_'))
                idx++;

            if (start == idx)
                continue;

            string typeName = definition[start..idx];

            // Check if this is a type parameter with an array-like constraint
            if (constraints.TryGetValue(typeName, out var constraint))
            {
                // Type parameter found - check constraint
                if (string.IsNullOrEmpty(constraint) || !IsArrayLikeConstraint(constraint))
                {
                    throw new TypeCheckException(
                        $" A rest element type must be an array type. " +
                        $"Type parameter '{typeName}' is not constrained to an array type.", tsCode: "TS2574");
                }
            }
            // If not a type parameter (e.g., a concrete type like ...number[]), that's fine
        }
    }

    /// <summary>
    /// Checks if a constraint string represents an array-like type.
    /// </summary>
    private static bool IsArrayLikeConstraint(string constraint)
    {
        string trimmed = constraint.Trim();
        // Check for common array-like constraints
        return trimmed.EndsWith("[]", StringComparison.Ordinal) ||  // T extends number[], string[], unknown[], etc.
               trimmed == "unknown[]" ||
               trimmed.StartsWith("[", StringComparison.Ordinal) || // T extends [string, number], etc. (tuples)
               trimmed == "readonly unknown[]" ||
               trimmed.StartsWith("readonly ", StringComparison.Ordinal) && trimmed.EndsWith("[]", StringComparison.Ordinal) ||
               trimmed.StartsWith("Array<", StringComparison.Ordinal); // T extends Array<unknown>
    }

    /// <summary>
    /// Applies type narrowing from assertion function calls.
    /// When a function with "asserts x is T" or "asserts x" return type is called,
    /// the variable x is narrowed in all subsequent code.
    /// </summary>
    private void ApplyAssertionNarrowing(Expr.Call call)
    {
        // Get the callee's type
        TypeInfo? calleeType = null;

        if (call.Callee is Expr.Variable funcVar)
        {
            calleeType = _environment.Get(funcVar.Name.Lexeme);
        }
        else if (call.Callee is Expr.Get getExpr)
        {
            var objType = CheckExpr(getExpr.Object);
            calleeType = GetMemberType(objType, getExpr.Name.Lexeme);
        }

        if (calleeType == null) return;

        // Get the return type
        TypeInfo? returnType = calleeType switch
        {
            TypeInfo.Function func => func.ReturnType,
            TypeInfo.GenericFunction gf => gf.ReturnType,
            _ => null
        };

        // Handle "asserts x is T" - narrow to the predicate type
        if (returnType is TypeInfo.TypePredicate pred && pred.IsAssertion)
        {
            // Look up the parameter index by name from the function type
            int paramIndex = FindParameterIndex(calleeType, pred.ParameterName);
            if (paramIndex >= 0 && paramIndex < call.Arguments.Count &&
                call.Arguments[paramIndex] is Expr.Variable argVar)
            {
                // Narrow the type in the current environment
                _environment.Define(argVar.Name.Lexeme, pred.PredicateType);
            }
        }
        // Handle "asserts x" - narrow to exclude null/undefined
        else if (returnType is TypeInfo.AssertsNonNull assertsNonNull)
        {
            // Look up the parameter index by name from the function type
            int paramIndex = FindParameterIndex(calleeType, assertsNonNull.ParameterName);
            if (paramIndex >= 0 && paramIndex < call.Arguments.Count &&
                call.Arguments[paramIndex] is Expr.Variable argVar)
            {
                var currentType = _environment.Get(argVar.Name.Lexeme);
                if (currentType != null)
                {
                    // Remove null and undefined from the type
                    TypeInfo narrowedType = ExcludeNullUndefined(currentType);
                    _environment.Define(argVar.Name.Lexeme, narrowedType);
                }
            }
        }
    }

    /// <summary>
    /// Removes null and undefined from a type.
    /// </summary>
    private static TypeInfo ExcludeNullUndefined(TypeInfo type)
    {
        if (type is TypeInfo.Union union)
        {
            var remaining = union.FlattenedTypes
                .Where(t => t is not TypeInfo.Null and not TypeInfo.Undefined)
                .ToList();

            if (remaining.Count == 0) return new TypeInfo.Never();
            if (remaining.Count == 1) return remaining[0];
            return new TypeInfo.Union(remaining);
        }

        // If the type itself is null or undefined, return never
        if (type is TypeInfo.Null or TypeInfo.Undefined)
            return new TypeInfo.Never();

        return type;
    }
}
