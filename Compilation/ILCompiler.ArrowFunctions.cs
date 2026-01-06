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
            var paramTypes = arrow.Parameters.Select(_ => typeof(object)).ToArray();

            if (captures.Count == 0)
            {
                // Non-capturing: static method on $Program
                var methodBuilder = _programType.DefineMethod(
                    $"<>Arrow_{_arrowMethodCounter++}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    typeof(object),
                    paramTypes
                );
                _arrowMethods[arrow] = methodBuilder;
            }
            else
            {
                // Capturing: create display class
                var displayClass = _moduleBuilder.DefineType(
                    $"<>c__DisplayClass{_displayClassCounter++}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    typeof(object)
                );

                // Add fields for captured variables
                var fieldMap = new Dictionary<string, FieldBuilder>();
                foreach (var capturedVar in captures)
                {
                    var field = displayClass.DefineField(capturedVar, typeof(object), FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }
                _displayClassFields[arrow] = fieldMap;

                // Add default constructor
                var ctorBuilder = displayClass.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                );
                var ctorIL = ctorBuilder.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
                ctorIL.Emit(OpCodes.Ret);
                _displayClassConstructors[arrow] = ctorBuilder;

                // Add Invoke method
                var invokeMethod = displayClass.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public,
                    typeof(object),
                    paramTypes
                );

                _displayClasses[arrow] = displayClass;
                _arrowMethods[arrow] = invokeMethod;
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
                    CollectArrowsFromExpr(v.Initializer);
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
            case Stmt.ForOf f:
                CollectArrowsFromExpr(f.Iterable);
                CollectArrowsFromStmt(f.Body);
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
                var captures = _closureAnalyzer.GetCaptures(af);
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
        }
    }

    private void EmitArrowFunctionBodies()
    {
        foreach (var (arrow, captures) in _collectedArrows)
        {
            var methodBuilder = _arrowMethods[arrow];

            if (captures.Count == 0)
            {
                // Non-capturing: emit body into static method
                EmitArrowBody(arrow, methodBuilder, null);
            }
            else
            {
                // Capturing: emit body into display class method
                var displayClass = _displayClasses[arrow];
                EmitArrowBody(arrow, methodBuilder, displayClass);
            }
        }
    }

    private void EmitArrowBody(Expr.ArrowFunction arrow, MethodBuilder method, TypeBuilder? displayClass)
    {
        var il = method.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null
        };

        if (displayClass != null)
        {
            // Instance method on display class - this is arg 0
            ctx.IsInstanceMethod = true;

            // Use the pre-stored field mapping
            if (_displayClassFields.TryGetValue(arrow, out var fieldMap))
            {
                ctx.CapturedFields = fieldMap;
            }
            else
            {
                ctx.CapturedFields = [];
            }

            // Parameters start at index 1
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1);
            }
        }
        else
        {
            // Static method - parameters start at index 0
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i);
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
