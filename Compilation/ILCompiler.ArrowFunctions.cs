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

    private void CollectAndDefineArrowFunctions(List<Stmt> statements)
    {
        // Walk the AST and collect all arrow functions
        foreach (var stmt in statements)
        {
            CollectArrowsFromStmt(stmt);
        }

        // Define methods and display classes
        foreach (var (arrow, captures) in _collectedArrows)
        {
            // For object methods, add __this as the first parameter
            Type[] paramTypes;
            if (arrow.IsObjectMethod)
            {
                paramTypes = new Type[arrow.Parameters.Count + 1];
                paramTypes[0] = _types.Object;  // __this
                for (int i = 0; i < arrow.Parameters.Count; i++)
                    paramTypes[i + 1] = _types.Object;
            }
            else
            {
                paramTypes = arrow.Parameters.Select(_ => _types.Object).ToArray();
            }

            if (captures.Count == 0)
            {
                // Non-capturing: static method on $Program
                var methodBuilder = _programType.DefineMethod(
                    $"<>Arrow_{_closures.ArrowMethodCounter++}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    _types.Object,
                    paramTypes
                );

                // Define parameter names (important for InvokeWithThis to detect __this)
                if (arrow.IsObjectMethod)
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

                // Add fields for captured variables
                Dictionary<string, FieldBuilder> fieldMap = [];
                foreach (var capturedVar in captures)
                {
                    var field = displayClass.DefineField(capturedVar, _types.Object, FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }
                _closures.DisplayClassFields[arrow] = fieldMap;

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
                if (arrow.IsObjectMethod)
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
                // Also collect arrows inside this arrow's body
                if (af.ExpressionBody != null)
                    CollectArrowsFromExpr(af.ExpressionBody);
                if (af.BlockBody != null)
                    foreach (var s in af.BlockBody)
                        CollectArrowsFromStmt(s);
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

            if (captures.Count == 0)
            {
                // Non-capturing: emit body into static method
                EmitArrowBody(arrow, methodBuilder, null);
            }
            else
            {
                // Capturing: emit body into display class method
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
            ClassConstructors = _classes.Constructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            ClassGenericParams = _classes.GenericParams,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _classes.InstanceMethods,
            InstanceGetters = _classes.InstanceGetters,
            InstanceSetters = _classes.InstanceSetters,
            ClassSuperclass = _classes.Superclass,
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
            IsStrictMode = _isStrictMode
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

            // For object methods, __this is the first parameter after 'this' (display class)
            // Parameters start at index 1 (display class is arg 0)
            if (arrow.IsObjectMethod)
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
            if (arrow.IsObjectMethod)
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
        emitter.EmitDefaultParameters(arrow.Parameters, displayClass != null);

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
