using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Arrow function collection and emission methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private readonly List<(Expr.ArrowFunction Arrow, HashSet<string> Captures)> _collectedArrows = [];
    private readonly Dictionary<Expr.ArrowFunction, Expr.ArrowFunction?> _arrowParent = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expr.ArrowFunction> _arrowsNeedingFunctionDC = new(ReferenceEqualityComparer.Instance);
    private Expr.ArrowFunction? _currentParentArrow;

    private void CollectAndDefineArrowFunctions(List<Stmt> statements)
    {
        // Walk the AST and collect all arrow functions
        foreach (var stmt in statements)
        {
            CollectArrowsFromStmt(stmt);
        }

        // Propagate function DC requirements through arrow nesting
        // If an inner arrow needs $functionDC, parent arrows also need it
        PropagateFunctionDCRequirements();

        // Define methods and display classes
        foreach (var (arrow, captures) in _collectedArrows)
        {
            // Skip async arrows - they're handled via DefineTopLevelAsyncArrows() or
            // DefineAsyncArrowStateMachines() (if inside an async function)
            if (arrow.IsAsync)
            {
                continue;
            }

            // For object methods, add __this as the first parameter
            // Rest parameters use List<object> to enable detection at invoke time
            Type[] paramTypes;
            if (arrow.HasOwnThis)
            {
                paramTypes = new Type[arrow.Parameters.Count + 1];
                paramTypes[0] = _types.Object;  // __this
                for (int i = 0; i < arrow.Parameters.Count; i++)
                    paramTypes[i + 1] = arrow.Parameters[i].IsRest ? _types.ListOfObject : _types.Object;
            }
            else
            {
                paramTypes = arrow.Parameters.Select(p => p.IsRest ? _types.ListOfObject : _types.Object).ToArray();
            }

            // Check if arrow needs function DC (for itself or to pass to inner arrows)
            bool needsFunctionDCForArrow = _arrowsNeedingFunctionDC.Contains(arrow);

            if (captures.Count == 0 && !needsFunctionDCForArrow)
            {
                // Non-capturing and doesn't need function DC: static method on $Program
                var methodBuilder = _programType.DefineMethod(
                    $"<>Arrow_{_closures.ArrowMethodCounter++}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    _types.Object,
                    paramTypes
                );

                // Define parameter names (important for InvokeWithThis to detect __this)
                if (arrow.HasOwnThis)
                {
                    methodBuilder.DefineParameter(1, ParameterAttributes.None, "__this");
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        methodBuilder.DefineParameter(i + 2, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                }
                else
                {
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                }

                _closures.ArrowMethods[arrow] = methodBuilder;
            }
            else
            {
                // Capturing: create display class
                var displayClass = _moduleBuilder.DefineType(
                    $"<>c__DisplayClass{_closures.DisplayClassCounter++}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    _types.Object
                );

                // Determine if any captured vars are top-level captured vars
                bool needsEntryPointDC = _closures.EntryPointDisplayClass != null &&
                    captures.Any(c => _closures.CapturedTopLevelVars.Contains(c));

                // Check if this arrow needs function DC (either directly or to propagate to inner arrows)
                bool needsFunctionDC = _arrowsNeedingFunctionDC.Contains(arrow);
                string? sourceFunction = needsFunctionDC && _closures.ArrowFunctionDCSource.TryGetValue(arrow, out var src) ? src : null;

                // Add fields for captured variables (except top-level and function-level captured vars)
                Dictionary<string, FieldBuilder> fieldMap = [];
                FieldBuilder? entryPointDCField = null;
                FieldBuilder? functionDCField = null;

                if (needsEntryPointDC)
                {
                    // Add field to hold reference to entry-point display class
                    entryPointDCField = displayClass.DefineField("$entryPointDC", _closures.EntryPointDisplayClass!, FieldAttributes.Public);
                }

                if (needsFunctionDC && sourceFunction != null && _closures.FunctionDisplayClasses.TryGetValue(sourceFunction, out var funcDC))
                {
                    // Add field to hold reference to function display class
                    functionDCField = displayClass.DefineField("$functionDC", funcDC, FieldAttributes.Public);
                }

                foreach (var capturedVar in captures)
                {
                    // Skip top-level captured vars - they'll be accessed through $entryPointDC
                    if (_closures.CapturedTopLevelVars.Contains(capturedVar))
                        continue;

                    // Skip function-level captured vars - they'll be accessed through $functionDC
                    if (needsFunctionDC && sourceFunction != null &&
                        _closures.FunctionDisplayClassFields.TryGetValue(sourceFunction, out var funcFields) &&
                        funcFields.ContainsKey(capturedVar))
                        continue;

                    var field = displayClass.DefineField(capturedVar, _types.Object, FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }
                _closures.DisplayClassFields[arrow] = fieldMap;

                // Track $entryPointDC field for this arrow
                if (entryPointDCField != null)
                {
                    _closures.ArrowEntryPointDCFields[arrow] = entryPointDCField;
                }

                // Track $functionDC field for this arrow
                if (functionDCField != null)
                {
                    _closures.ArrowFunctionDCFields[arrow] = functionDCField;
                }

                // Add default constructor
                var ctorBuilder = displayClass.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                );
                var ctorIL = ctorBuilder.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
                ctorIL.Emit(OpCodes.Ret);
                _closures.DisplayClassConstructors[arrow] = ctorBuilder;

                // Add Invoke method
                var invokeMethod = displayClass.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public,
                    _types.Object,
                    paramTypes
                );

                // Define parameter names (important for InvokeWithThis to detect __this)
                if (arrow.HasOwnThis)
                {
                    invokeMethod.DefineParameter(1, ParameterAttributes.None, "__this");
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        invokeMethod.DefineParameter(i + 2, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                }
                else
                {
                    for (int i = 0; i < arrow.Parameters.Count; i++)
                        invokeMethod.DefineParameter(i + 1, ParameterAttributes.None, arrow.Parameters[i].Name.Lexeme);
                }

                _closures.DisplayClasses[arrow] = displayClass;
                _closures.ArrowMethods[arrow] = invokeMethod;
            }
        }
    }

    private void CollectArrowsFromStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                CollectArrowsFromExpr(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null)
                {
                    // If initializing with a class expression, track variable name → class expr mapping
                    if (v.Initializer is Expr.ClassExpr classExpr)
                    {
                        _classExprs.VarToClassExpr[v.Name.Lexeme] = classExpr;
                    }
                    CollectArrowsFromExpr(v.Initializer);
                }
                break;
            case Stmt.Const c:
                // If initializing with a class expression, track variable name → class expr mapping
                if (c.Initializer is Expr.ClassExpr classExprConst)
                {
                    _classExprs.VarToClassExpr[c.Name.Lexeme] = classExprConst;
                }
                CollectArrowsFromExpr(c.Initializer);
                break;
            case Stmt.Function f:
                // Skip overload signatures (no body)
                if (f.Body != null)
                {
                    foreach (var s in f.Body)
                        CollectArrowsFromStmt(s);
                }
                foreach (var p in f.Parameters)
                    if (p.DefaultValue != null)
                        CollectArrowsFromExpr(p.DefaultValue);
                break;
            case Stmt.Class c:
                foreach (var method in c.Methods)
                {
                    // Skip overload signatures (no body)
                    if (method.Body != null)
                        CollectArrowsFromStmt(method);
                }
                break;
            case Stmt.If i:
                CollectArrowsFromExpr(i.Condition);
                CollectArrowsFromStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    CollectArrowsFromStmt(i.ElseBranch);
                break;
            case Stmt.While w:
                CollectArrowsFromExpr(w.Condition);
                CollectArrowsFromStmt(w.Body);
                break;
            case Stmt.For f:
                if (f.Initializer != null)
                    CollectArrowsFromStmt(f.Initializer);
                if (f.Condition != null)
                    CollectArrowsFromExpr(f.Condition);
                if (f.Increment != null)
                    CollectArrowsFromExpr(f.Increment);
                CollectArrowsFromStmt(f.Body);
                break;
            case Stmt.ForOf forOf:
                CollectArrowsFromExpr(forOf.Iterable);
                CollectArrowsFromStmt(forOf.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    CollectArrowsFromExpr(r.Value);
                break;
            case Stmt.Switch s:
                CollectArrowsFromExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    CollectArrowsFromExpr(c.Value);
                    foreach (var cs in c.Body)
                        CollectArrowsFromStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        CollectArrowsFromStmt(ds);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    CollectArrowsFromStmt(ts);
                if (t.CatchBlock != null)
                    foreach (var cs in t.CatchBlock)
                        CollectArrowsFromStmt(cs);
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        CollectArrowsFromStmt(fs);
                break;
            case Stmt.Throw th:
                CollectArrowsFromExpr(th.Value);
                break;
            case Stmt.Print p:
                CollectArrowsFromExpr(p.Expr);
                break;
        }
    }

    private void CollectArrowsFromExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af:
                var captures = _closures.Analyzer.GetCaptures(af);
                _collectedArrows.Add((af, captures));

                // Track parent arrow for function DC propagation
                _arrowParent[af] = _currentParentArrow;
                var previousParent = _currentParentArrow;
                _currentParentArrow = af;

                // Also collect arrows inside this arrow's body
                if (af.ExpressionBody != null)
                    CollectArrowsFromExpr(af.ExpressionBody);
                if (af.BlockBody != null)
                    foreach (var s in af.BlockBody)
                        CollectArrowsFromStmt(s);

                _currentParentArrow = previousParent;
                break;
            case Expr.Binary b:
                CollectArrowsFromExpr(b.Left);
                CollectArrowsFromExpr(b.Right);
                break;
            case Expr.Logical l:
                CollectArrowsFromExpr(l.Left);
                CollectArrowsFromExpr(l.Right);
                break;
            case Expr.Unary u:
                CollectArrowsFromExpr(u.Right);
                break;
            case Expr.Delete d:
                CollectArrowsFromExpr(d.Operand);
                break;
            case Expr.Grouping g:
                CollectArrowsFromExpr(g.Expression);
                break;
            case Expr.Call c:
                CollectArrowsFromExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.Get g:
                CollectArrowsFromExpr(g.Object);
                break;
            case Expr.Set s:
                CollectArrowsFromExpr(s.Object);
                CollectArrowsFromExpr(s.Value);
                break;
            case Expr.GetIndex gi:
                CollectArrowsFromExpr(gi.Object);
                CollectArrowsFromExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                CollectArrowsFromExpr(si.Object);
                CollectArrowsFromExpr(si.Index);
                CollectArrowsFromExpr(si.Value);
                break;
            case Expr.Assign a:
                CollectArrowsFromExpr(a.Value);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    CollectArrowsFromExpr(elem);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    CollectArrowsFromExpr(prop.Value);
                break;
            case Expr.Ternary t:
                CollectArrowsFromExpr(t.Condition);
                CollectArrowsFromExpr(t.ThenBranch);
                CollectArrowsFromExpr(t.ElseBranch);
                break;
            case Expr.NullishCoalescing nc:
                CollectArrowsFromExpr(nc.Left);
                CollectArrowsFromExpr(nc.Right);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    CollectArrowsFromExpr(e);
                break;
            case Expr.TaggedTemplateLiteral ttl:
                CollectArrowsFromExpr(ttl.Tag);
                foreach (var e in ttl.Expressions)
                    CollectArrowsFromExpr(e);
                break;
            case Expr.CompoundAssign ca:
                CollectArrowsFromExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                CollectArrowsFromExpr(cs.Object);
                CollectArrowsFromExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                CollectArrowsFromExpr(csi.Object);
                CollectArrowsFromExpr(csi.Index);
                CollectArrowsFromExpr(csi.Value);
                break;
            case Expr.PrefixIncrement pi:
                CollectArrowsFromExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement poi:
                CollectArrowsFromExpr(poi.Operand);
                break;
            case Expr.Await aw:
                CollectArrowsFromExpr(aw.Expression);
                break;
            case Expr.DynamicImport di:
                CollectArrowsFromExpr(di.PathExpression);
                break;
            case Expr.TypeAssertion ta:
                CollectArrowsFromExpr(ta.Expression);
                break;
            case Expr.Satisfies sat:
                CollectArrowsFromExpr(sat.Expression);
                break;
            case Expr.NonNullAssertion nna:
                CollectArrowsFromExpr(nna.Expression);
                break;
            case Expr.Spread sp:
                CollectArrowsFromExpr(sp.Expression);
                break;
            case Expr.ClassExpr ce:
                // Collect the class expression for later definition
                CollectClassExpression(ce);
                // Also collect arrows inside class expression methods
                foreach (var method in ce.Methods)
                    if (method.Body != null)
                        foreach (var s in method.Body)
                            CollectArrowsFromStmt(s);
                // Collect arrows in field initializers
                foreach (var field in ce.Fields)
                    if (field.Initializer != null)
                        CollectArrowsFromExpr(field.Initializer);
                // Collect arrows in accessor bodies
                if (ce.Accessors != null)
                    foreach (var accessor in ce.Accessors)
                        foreach (var s in accessor.Body)
                            CollectArrowsFromStmt(s);
                break;
        }
    }

    /// <summary>
    /// Collects a class expression for later type definition.
    /// </summary>
    private void CollectClassExpression(Expr.ClassExpr classExpr)
    {
        if (_classExprs.Names.ContainsKey(classExpr))
            return; // Already collected

        // Generate unique name
        string className = classExpr.Name?.Lexeme ?? $"$ClassExpr_{++_classExprs.Counter}";
        _classExprs.Names[classExpr] = className;
        _classExprs.ToDefine.Add(classExpr);
    }

    /// <summary>
    /// Propagates function display class requirements through arrow nesting.
    /// If an inner arrow needs $functionDC, its parent arrows also need it to pass it through.
    /// </summary>
    private void PropagateFunctionDCRequirements()
    {
        // First, identify arrows that directly need $functionDC
        foreach (var (arrow, captures) in _collectedArrows)
        {
            // Check if this arrow directly captures function-level variables
            foreach (var (funcName, funcDCFields) in _closures.FunctionDisplayClassFields)
            {
                if (captures.Any(c => funcDCFields.ContainsKey(c)))
                {
                    _arrowsNeedingFunctionDC.Add(arrow);
                    _closures.ArrowFunctionDCSource[arrow] = funcName;
                    break;
                }
            }
        }

        // Propagate requirements up the parent chain
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var arrow in _arrowsNeedingFunctionDC.ToList())
            {
                if (_arrowParent.TryGetValue(arrow, out var parent) && parent != null)
                {
                    if (!_arrowsNeedingFunctionDC.Contains(parent))
                    {
                        _arrowsNeedingFunctionDC.Add(parent);
                        // Inherit the source function from the child
                        if (_closures.ArrowFunctionDCSource.TryGetValue(arrow, out var source))
                        {
                            _closures.ArrowFunctionDCSource[parent] = source;
                        }
                        changed = true;
                    }
                }
            }
        }
    }

    private void EmitArrowFunctionBodies()
    {
        foreach (var (arrow, captures) in _collectedArrows)
        {
            // Skip async arrows - they're handled separately via AsyncArrowMoveNextEmitter
            // in ILCompiler.Async.cs
            if (arrow.IsAsync)
            {
                continue;
            }

            var methodBuilder = _closures.ArrowMethods[arrow];

            // Check if this arrow needs function DC (either directly or to pass to inner arrows)
            // This must match the logic in CollectAndDefineArrowFunctions
            bool needsFunctionDCForArrow = _arrowsNeedingFunctionDC.Contains(arrow);

            if (captures.Count == 0 && !needsFunctionDCForArrow)
            {
                // Non-capturing and doesn't need function DC: emit body into static method
                EmitArrowBody(arrow, methodBuilder, null);
            }
            else
            {
                // Capturing or needs function DC: emit body into display class method
                var displayClass = _closures.DisplayClasses[arrow];
                EmitArrowBody(arrow, methodBuilder, displayClass);
            }
        }
    }

    private void EmitArrowBody(Expr.ArrowFunction arrow, MethodBuilder method, TypeBuilder? displayClass)
    {
        var il = method.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            // Top-level variables for module-level access
            TopLevelStaticVars = _topLevelStaticVars,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders,
            IsStrictMode = _isStrictMode,
            // Registry services
            ClassRegistry = GetClassRegistry(),
            // Entry-point display class for accessing captured top-level variables
            EntryPointDisplayClassFields = _closures.EntryPointDisplayClassFields.Count > 0 ? _closures.EntryPointDisplayClassFields : null,
            CapturedTopLevelVars = _closures.CapturedTopLevelVars.Count > 0 ? _closures.CapturedTopLevelVars : null,
            ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null,
            EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField,
            // Function-level display class for nested arrow functions
            ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null
        };

        if (displayClass != null)
        {
            // Instance method on display class - this is arg 0
            ctx.IsInstanceMethod = true;

            // Use the pre-stored field mapping
            if (_closures.DisplayClassFields.TryGetValue(arrow, out var fieldMap))
            {
                ctx.CapturedFields = fieldMap;
            }
            else
            {
                ctx.CapturedFields = [];
            }

            // Set the $entryPointDC field if this arrow captures top-level variables
            if (_closures.ArrowEntryPointDCFields.TryGetValue(arrow, out var entryPointDCField))
            {
                ctx.CurrentArrowEntryPointDCField = entryPointDCField;
            }

            // Set the $functionDC field if this arrow captures function-level variables
            if (_closures.ArrowFunctionDCFields.TryGetValue(arrow, out var functionDCField))
            {
                ctx.CurrentArrowFunctionDCField = functionDCField;

                // Also set up the captured function locals info so LocalVariableResolver can use it
                if (_closures.ArrowFunctionDCSource.TryGetValue(arrow, out var sourceFuncName) &&
                    _closures.FunctionDisplayClassFields.TryGetValue(sourceFuncName, out var funcDCFields))
                {
                    ctx.FunctionDisplayClassFields = funcDCFields;
                    ctx.CapturedFunctionLocals = [.. funcDCFields.Keys];
                }
            }

            // For object methods, __this is the first parameter after 'this' (display class)
            // Parameters start at index 1 (display class is arg 0)
            if (arrow.HasOwnThis)
            {
                // __this is at index 1, actual parameters start at index 2
                ctx.DefineParameter("__this", 1);
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 2);
                }
            }
            else
            {
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1);
                }
            }
        }
        else
        {
            // Static method - parameters start at index 0
            if (arrow.HasOwnThis)
            {
                // __this is at index 0, actual parameters start at index 1
                ctx.DefineParameter("__this", 0);
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1);
                }
            }
            else
            {
                for (int i = 0; i < arrow.Parameters.Count; i++)
                {
                    ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i);
                }
            }
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks
        emitter.EmitDefaultParameters(arrow.Parameters, displayClass != null, arrow.HasOwnThis);

        if (arrow.ExpressionBody != null)
        {
            // Expression body: emit expression and return
            emitter.EmitExpression(arrow.ExpressionBody);
            emitter.EmitBoxIfNeeded(arrow.ExpressionBody);
            il.Emit(OpCodes.Ret);
        }
        else if (arrow.BlockBody != null)
        {
            // Block body: emit statements
            foreach (var stmt in arrow.BlockBody)
            {
                emitter.EmitStatement(stmt);
            }
            // Finalize any deferred returns from exception blocks
            if (emitter.HasDeferredReturns)
            {
                emitter.FinalizeReturns();
            }
            else
            {
                // Default return null
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }
}
