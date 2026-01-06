using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Async/await state machine compilation methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private void DefineAsyncFunction(Stmt.Function funcStmt)
    {
        // Analyze the async function for await points and hoisted variables
        var analysis = _asyncAnalyzer.Analyze(funcStmt);

        // Create state machine builder
        var smBuilder = new AsyncStateMachineBuilder(_moduleBuilder, _asyncStateMachineCounter++);
        var hasAsyncArrows = analysis.AsyncArrows.Count > 0;
        smBuilder.DefineStateMachine(funcStmt.Name.Lexeme, analysis, typeof(object), false, hasAsyncArrows);

        // Define stub method (returns Task<object>)
        var paramTypes = funcStmt.Parameters.Select(_ => typeof(object)).ToArray();
        var stubMethod = _programType.DefineMethod(
            funcStmt.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Task<object>),
            paramTypes
        );

        // Store for later body emission
        _functionBuilders[funcStmt.Name.Lexeme] = stubMethod;
        _asyncStateMachines[funcStmt.Name.Lexeme] = smBuilder;
        _asyncFunctions[funcStmt.Name.Lexeme] = funcStmt;

        // Build state machines for any async arrows found in this function
        DefineAsyncArrowStateMachines(analysis.AsyncArrows, smBuilder);
    }

    private void DefineAsyncArrowStateMachines(
        List<AsyncStateAnalyzer.AsyncArrowInfo> asyncArrows,
        AsyncStateMachineBuilder outerBuilder)
    {
        // Get all hoisted fields from the function's state machine
        var functionHoistedFields = new Dictionary<string, FieldBuilder>();
        foreach (var (name, field) in outerBuilder.HoistedParameters)
            functionHoistedFields[name] = field;
        foreach (var (name, field) in outerBuilder.HoistedLocals)
            functionHoistedFields[name] = field;

        // Sort arrows by nesting level to ensure parents are defined before children
        var sortedArrows = asyncArrows.OrderBy(a => a.NestingLevel).ToList();

        // Build a set of arrows that have nested async children
        var arrowsWithNestedChildren = new HashSet<Expr.ArrowFunction>(ReferenceEqualityComparer.Instance);
        foreach (var arrowInfo in sortedArrows)
        {
            if (arrowInfo.ParentArrow != null)
            {
                arrowsWithNestedChildren.Add(arrowInfo.ParentArrow);
            }
        }

        foreach (var arrowInfo in sortedArrows)
        {
            // Create a dedicated analyzer for this arrow's await points
            var arrowAnalysis = AnalyzeAsyncArrow(arrowInfo.Arrow);

            // Create state machine builder for the async arrow
            var arrowBuilder = new AsyncArrowStateMachineBuilder(
                _moduleBuilder,
                arrowInfo.Arrow,
                arrowInfo.Captures,
                _asyncArrowCounter++);

            // Determine the outer state machine type and hoisted fields
            Type outerStateMachineType;
            Dictionary<string, FieldBuilder> outerHoistedFields;

            // Check if this arrow has nested async children
            bool hasNestedChildren = arrowsWithNestedChildren.Contains(arrowInfo.Arrow);

            if (arrowInfo.ParentArrow == null)
            {
                // Direct child of the function - use function's state machine
                outerStateMachineType = outerBuilder.StateMachineType;
                outerHoistedFields = functionHoistedFields;
                _asyncArrowOuterBuilders[arrowInfo.Arrow] = outerBuilder;
            }
            else
            {
                // Nested arrow - use parent arrow's state machine
                if (!_asyncArrowBuilders.TryGetValue(arrowInfo.ParentArrow, out var parentBuilder))
                {
                    throw new InvalidOperationException(
                        $"Parent async arrow not found. Nesting level: {arrowInfo.NestingLevel}");
                }

                outerStateMachineType = parentBuilder.StateMachineType;

                // Get hoisted fields from parent arrow - includes its parameters, locals, and captured fields
                outerHoistedFields = [];
                foreach (var (name, field) in parentBuilder.ParameterFields)
                    outerHoistedFields[name] = field;
                foreach (var (name, field) in parentBuilder.LocalFields)
                    outerHoistedFields[name] = field;
                // Also include captured fields - they're accessible through parent's outer reference
                // These are "transitive" captures - we need to go through parent's <>__outer to access them
                HashSet<string> transitiveCaptures = [];
                foreach (var (name, field) in parentBuilder.CapturedFieldMap)
                {
                    outerHoistedFields[name] = field;
                    transitiveCaptures.Add(name);
                }
                // Also include parent's transitive captures (for deeper nesting)
                foreach (var name in parentBuilder.TransitiveCaptures)
                {
                    transitiveCaptures.Add(name);
                }

                _asyncArrowParentBuilders[arrowInfo.Arrow] = parentBuilder;

                // Pass transitive info for nested arrows
                arrowBuilder.DefineStateMachine(
                    outerStateMachineType,
                    outerHoistedFields,
                    arrowAnalysis.AwaitCount,
                    arrowInfo.Arrow.Parameters,
                    arrowAnalysis.HoistedLocals,
                    transitiveCaptures,
                    parentBuilder.OuterStateMachineField,
                    parentBuilder.OuterStateMachineType,
                    hasNestedChildren);

                // Define the stub method that will be called to invoke the async arrow
                arrowBuilder.DefineStubMethod(_programType);

                _asyncArrowBuilders[arrowInfo.Arrow] = arrowBuilder;
                continue; // Already handled the full setup
            }

            arrowBuilder.DefineStateMachine(
                outerStateMachineType,
                outerHoistedFields,
                arrowAnalysis.AwaitCount,
                arrowInfo.Arrow.Parameters,
                arrowAnalysis.HoistedLocals,
                hasNestedAsyncArrows: hasNestedChildren);

            // Define the stub method that will be called to invoke the async arrow
            arrowBuilder.DefineStubMethod(_programType);

            _asyncArrowBuilders[arrowInfo.Arrow] = arrowBuilder;
        }
    }

    /// <summary>
    /// Analyzes an async arrow function to determine its await points and hoisted variables.
    /// </summary>
    private (int AwaitCount, HashSet<string> HoistedLocals) AnalyzeAsyncArrow(Expr.ArrowFunction arrow)
    {
        var awaitCount = 0;
        var declaredVariables = new HashSet<string>();
        var variablesUsedAfterAwait = new HashSet<string>();
        var variablesDeclaredBeforeAwait = new HashSet<string>();
        var seenAwait = false;

        // Add parameters as declared variables
        foreach (var param in arrow.Parameters)
        {
            declaredVariables.Add(param.Name.Lexeme);
            variablesDeclaredBeforeAwait.Add(param.Name.Lexeme);
        }

        // Analyze expression body or block body
        if (arrow.ExpressionBody != null)
        {
            AnalyzeArrowExprForAwaits(arrow.ExpressionBody, ref awaitCount, ref seenAwait,
                declaredVariables, variablesUsedAfterAwait, variablesDeclaredBeforeAwait);
        }
        else if (arrow.BlockBody != null)
        {
            foreach (var stmt in arrow.BlockBody)
            {
                AnalyzeArrowStmtForAwaits(stmt, ref awaitCount, ref seenAwait,
                    declaredVariables, variablesUsedAfterAwait, variablesDeclaredBeforeAwait);
            }
        }

        // Variables that need hoisting: declared before await AND used after await
        var hoistedLocals = new HashSet<string>(variablesDeclaredBeforeAwait);
        hoistedLocals.IntersectWith(variablesUsedAfterAwait);

        // Remove parameters from hoisted locals (they're stored separately)
        foreach (var param in arrow.Parameters)
            hoistedLocals.Remove(param.Name.Lexeme);

        return (awaitCount, hoistedLocals);
    }

    private void AnalyzeArrowStmtForAwaits(Stmt stmt, ref int awaitCount, ref bool seenAwait,
        HashSet<string> declaredVariables, HashSet<string> usedAfterAwait, HashSet<string> declaredBeforeAwait)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                declaredVariables.Add(v.Name.Lexeme);
                if (!seenAwait)
                    declaredBeforeAwait.Add(v.Name.Lexeme);
                if (v.Initializer != null)
                    AnalyzeArrowExprForAwaits(v.Initializer, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Expression e:
                AnalyzeArrowExprForAwaits(e.Expr, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeArrowExprForAwaits(r.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.If i:
                AnalyzeArrowExprForAwaits(i.Condition, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowStmtForAwaits(i.ThenBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                if (i.ElseBranch != null)
                    AnalyzeArrowStmtForAwaits(i.ElseBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.While w:
                AnalyzeArrowExprForAwaits(w.Condition, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowStmtForAwaits(w.Body, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.ForOf f:
                declaredVariables.Add(f.Variable.Lexeme);
                if (!seenAwait)
                    declaredBeforeAwait.Add(f.Variable.Lexeme);
                AnalyzeArrowExprForAwaits(f.Iterable, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowStmtForAwaits(f.Body, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    AnalyzeArrowStmtForAwaits(s, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeArrowStmtForAwaits(s, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    AnalyzeArrowStmtForAwaits(ts, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                if (t.CatchBlock != null)
                {
                    if (t.CatchParam != null)
                    {
                        declaredVariables.Add(t.CatchParam.Lexeme);
                        if (!seenAwait)
                            declaredBeforeAwait.Add(t.CatchParam.Lexeme);
                    }
                    foreach (var cs in t.CatchBlock)
                        AnalyzeArrowStmtForAwaits(cs, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                }
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        AnalyzeArrowStmtForAwaits(fs, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Switch s:
                AnalyzeArrowExprForAwaits(s.Subject, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                foreach (var c in s.Cases)
                {
                    AnalyzeArrowExprForAwaits(c.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                    foreach (var cs in c.Body)
                        AnalyzeArrowStmtForAwaits(cs, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeArrowStmtForAwaits(ds, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Throw th:
                AnalyzeArrowExprForAwaits(th.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Stmt.Print p:
                AnalyzeArrowExprForAwaits(p.Expr, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
        }
    }

    private void AnalyzeArrowExprForAwaits(Expr expr, ref int awaitCount, ref bool seenAwait,
        HashSet<string> declaredVariables, HashSet<string> usedAfterAwait, HashSet<string> declaredBeforeAwait)
    {
        switch (expr)
        {
            case Expr.Await a:
                awaitCount++;
                seenAwait = true;
                AnalyzeArrowExprForAwaits(a.Expression, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Variable v:
                if (seenAwait && declaredVariables.Contains(v.Name.Lexeme))
                    usedAfterAwait.Add(v.Name.Lexeme);
                break;
            case Expr.Assign a:
                if (seenAwait && declaredVariables.Contains(a.Name.Lexeme))
                    usedAfterAwait.Add(a.Name.Lexeme);
                AnalyzeArrowExprForAwaits(a.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Binary b:
                AnalyzeArrowExprForAwaits(b.Left, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(b.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Logical l:
                AnalyzeArrowExprForAwaits(l.Left, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(l.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Unary u:
                AnalyzeArrowExprForAwaits(u.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Grouping g:
                AnalyzeArrowExprForAwaits(g.Expression, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Call c:
                AnalyzeArrowExprForAwaits(c.Callee, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                foreach (var arg in c.Arguments)
                    AnalyzeArrowExprForAwaits(arg, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Get g:
                AnalyzeArrowExprForAwaits(g.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Set s:
                AnalyzeArrowExprForAwaits(s.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(s.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.GetIndex gi:
                AnalyzeArrowExprForAwaits(gi.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(gi.Index, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.SetIndex si:
                AnalyzeArrowExprForAwaits(si.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(si.Index, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(si.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeArrowExprForAwaits(arg, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeArrowExprForAwaits(elem, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeArrowExprForAwaits(prop.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.Ternary t:
                AnalyzeArrowExprForAwaits(t.Condition, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(t.ThenBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(t.ElseBranch, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.NullishCoalescing nc:
                AnalyzeArrowExprForAwaits(nc.Left, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(nc.Right, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeArrowExprForAwaits(e, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.CompoundAssign ca:
                if (seenAwait && declaredVariables.Contains(ca.Name.Lexeme))
                    usedAfterAwait.Add(ca.Name.Lexeme);
                AnalyzeArrowExprForAwaits(ca.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.CompoundSet cs:
                AnalyzeArrowExprForAwaits(cs.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(cs.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.CompoundSetIndex csi:
                AnalyzeArrowExprForAwaits(csi.Object, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(csi.Index, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                AnalyzeArrowExprForAwaits(csi.Value, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.PrefixIncrement pi:
                AnalyzeArrowExprForAwaits(pi.Operand, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.PostfixIncrement poi:
                AnalyzeArrowExprForAwaits(poi.Operand, ref awaitCount, ref seenAwait, declaredVariables, usedAfterAwait, declaredBeforeAwait);
                break;
            case Expr.ArrowFunction:
                // Nested arrows don't contribute to this arrow's await analysis
                break;
        }
    }

    private void EmitAsyncStateMachineBodies()
    {
        foreach (var (funcName, smBuilder) in _asyncStateMachines)
        {
            var func = _asyncFunctions[funcName];
            var stubMethod = _functionBuilders[funcName];
            var analysis = _asyncAnalyzer.Analyze(func);

            // Emit stub method body
            EmitAsyncStubMethod(stubMethod, smBuilder, func.Parameters);

            // Create context for MoveNext emission
            var il = smBuilder.MoveNextMethod.GetILGenerator();
            var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
            {
                Runtime = _runtime,
                ClassConstructors = _classConstructors,
                ClosureAnalyzer = _closureAnalyzer,
                ArrowMethods = _arrowMethods,
                DisplayClasses = _displayClasses,
                DisplayClassFields = _displayClassFields,
                DisplayClassConstructors = _displayClassConstructors,
                StaticFields = _staticFields,
                StaticMethods = _staticMethods,
                EnumMembers = _enumMembers,
                EnumReverse = _enumReverse,
                EnumKinds = _enumKinds,
                FunctionRestParams = _functionRestParams,
                ClassGenericParams = _classGenericParams,
                FunctionGenericParams = _functionGenericParams,
                IsGenericFunction = _isGenericFunction,
                TypeMap = _typeMap,
                DeadCode = _deadCodeInfo,
                InstanceMethods = _instanceMethods,
                InstanceGetters = _instanceGetters,
                InstanceSetters = _instanceSetters,
                ClassSuperclass = _classSuperclass,
                AsyncMethods = null,
                AsyncArrowBuilders = _asyncArrowBuilders,
                AsyncArrowOuterBuilders = _asyncArrowOuterBuilders,
                AsyncArrowParentBuilders = _asyncArrowParentBuilders
            };

            // Emit MoveNext body
            var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis);
            moveNextEmitter.EmitMoveNext(func.Body, ctx, typeof(object));

            // Emit async arrow MoveNext bodies
            foreach (var arrowInfo in analysis.AsyncArrows)
            {
                if (_asyncArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
                {
                    EmitAsyncArrowMoveNext(arrowBuilder, arrowInfo.Arrow, ctx);
                }
            }

            // Finalize state machine type
            smBuilder.CreateType();
        }

        // Finalize all async arrow state machine types
        foreach (var (_, arrowBuilder) in _asyncArrowBuilders)
        {
            arrowBuilder.CreateType();
        }
    }

    private void EmitAsyncArrowMoveNext(AsyncArrowStateMachineBuilder arrowBuilder, Expr.ArrowFunction arrow, CompilationContext parentCtx)
    {
        // Create IL generator for the arrow's MoveNext
        var il = arrowBuilder.MoveNextMethod.GetILGenerator();

        // Create analysis for this arrow
        var arrowAnalysis = AnalyzeAsyncArrow(arrow);
        var analysis = new AsyncStateAnalyzer.AsyncFunctionAnalysis(
            arrowAnalysis.AwaitCount,
            [], // We'll regenerate await points during emission
            arrowAnalysis.HoistedLocals,
            new HashSet<string>(arrow.Parameters.Select(p => p.Name.Lexeme)),
            false, // HasTryCatch - will be detected during emission
            arrowBuilder.Captures.Contains("this"),
            [] // No nested async arrows handled yet
        );

        // Create a new context for arrow MoveNext emission
        var ctx = new CompilationContext(il, parentCtx.TypeMapper, parentCtx.Functions, parentCtx.Classes)
        {
            Runtime = parentCtx.Runtime,
            ClassConstructors = parentCtx.ClassConstructors,
            ClosureAnalyzer = parentCtx.ClosureAnalyzer,
            ArrowMethods = parentCtx.ArrowMethods,
            DisplayClasses = parentCtx.DisplayClasses,
            DisplayClassFields = parentCtx.DisplayClassFields,
            DisplayClassConstructors = parentCtx.DisplayClassConstructors,
            StaticFields = parentCtx.StaticFields,
            StaticMethods = parentCtx.StaticMethods,
            EnumMembers = parentCtx.EnumMembers,
            EnumReverse = parentCtx.EnumReverse,
            EnumKinds = parentCtx.EnumKinds,
            FunctionRestParams = parentCtx.FunctionRestParams,
            ClassGenericParams = parentCtx.ClassGenericParams,
            FunctionGenericParams = parentCtx.FunctionGenericParams,
            IsGenericFunction = parentCtx.IsGenericFunction,
            TypeMap = parentCtx.TypeMap,
            DeadCode = parentCtx.DeadCode,
            InstanceMethods = parentCtx.InstanceMethods,
            InstanceGetters = parentCtx.InstanceGetters,
            InstanceSetters = parentCtx.InstanceSetters,
            ClassSuperclass = parentCtx.ClassSuperclass,
            AsyncMethods = null,
            AsyncArrowBuilders = _asyncArrowBuilders,
            AsyncArrowOuterBuilders = _asyncArrowOuterBuilders,
            AsyncArrowParentBuilders = _asyncArrowParentBuilders
        };

        // Create arrow-specific emitter
        var arrowEmitter = new AsyncArrowMoveNextEmitter(arrowBuilder, analysis);

        // Get the body statements
        List<Stmt> bodyStatements;
        if (arrow.BlockBody != null)
        {
            bodyStatements = arrow.BlockBody;
        }
        else if (arrow.ExpressionBody != null)
        {
            // Create a synthetic return statement for expression body arrows
            var returnToken = new Token(TokenType.RETURN, "return", null, 0);
            bodyStatements = [new Stmt.Return(returnToken, arrow.ExpressionBody)];
        }
        else
        {
            bodyStatements = [];
        }

        arrowEmitter.EmitMoveNext(bodyStatements, ctx, typeof(object));
    }

    private void EmitAsyncStubMethod(MethodBuilder stubMethod, AsyncStateMachineBuilder smBuilder, List<Stmt.Parameter> parameters, bool isInstanceMethod = false)
    {
        var il = stubMethod.GetILGenerator();
        var smLocal = il.DeclareLocal(smBuilder.StateMachineType);

        // var sm = default(<StateMachine>);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, smBuilder.StateMachineType);

        // Copy 'this' to state machine if this is an instance method and uses 'this'
        if (isInstanceMethod && smBuilder.ThisField != null)
        {
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldarg_0);  // 'this' is arg 0 for instance methods
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        // Copy parameters to state machine fields
        // For instance methods, parameters start at arg 1 (arg 0 is 'this')
        int paramOffset = isInstanceMethod ? 1 : 0;
        for (int i = 0; i < parameters.Count; i++)
        {
            string paramName = parameters[i].Name.Lexeme;
            if (smBuilder.HoistedParameters.TryGetValue(paramName, out var field))
            {
                il.Emit(OpCodes.Ldloca, smLocal);
                il.Emit(OpCodes.Ldarg, i + paramOffset);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // sm.<>t__builder = AsyncTaskMethodBuilder<T>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Call, smBuilder.GetBuilderCreateMethod());
        il.Emit(OpCodes.Stfld, smBuilder.BuilderField);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, smBuilder.StateField);

        // If this function has async arrows, we need to box the state machine first
        // and store the boxed reference so async arrows can share the same instance
        if (smBuilder.SelfBoxedField != null)
        {
            // Box the state machine to get a heap-allocated copy
            il.Emit(OpCodes.Ldloc, smLocal);
            il.Emit(OpCodes.Box, smBuilder.StateMachineType);
            var boxedLocal = il.DeclareLocal(typeof(object));
            il.Emit(OpCodes.Stloc, boxedLocal);

            // Store the boxed reference in the state machine
            // Use Unbox to get a pointer to the boxed value, then store the reference there
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Stfld, smBuilder.SelfBoxedField);

            // Now call Start on the BOXED state machine (cast to IAsyncStateMachine)
            // builder.Start expects ref TSM, so we use Unbox to get the pointer
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderStartMethod());

            // return boxed.<>t__builder.Task
            il.Emit(OpCodes.Ldloc, boxedLocal);
            il.Emit(OpCodes.Unbox, smBuilder.StateMachineType);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderTaskGetter());
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Standard path: use stack-based state machine (runtime boxes internally)
            // sm.<>t__builder.Start(ref sm);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderStartMethod());

            // return sm.<>t__builder.Task;
            il.Emit(OpCodes.Ldloca, smLocal);
            il.Emit(OpCodes.Ldflda, smBuilder.BuilderField);
            il.Emit(OpCodes.Call, smBuilder.GetBuilderTaskGetter());
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitAsyncMethodBody(MethodBuilder methodBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        // Analyze async function to determine await points and hoisted variables
        var analysis = _asyncAnalyzer.Analyze(method);

        // Build state machine type
        var smBuilder = new AsyncStateMachineBuilder(_moduleBuilder, _asyncStateMachineCounter++);
        var hasAsyncArrows = analysis.AsyncArrows.Count > 0;
        smBuilder.DefineStateMachine(
            $"{methodBuilder.DeclaringType!.Name}_{method.Name.Lexeme}",
            analysis,
            typeof(object),
            isInstanceMethod: true,  // This is an instance method
            hasAsyncArrows: hasAsyncArrows
        );

        // Build state machines for any async arrows found in this method
        DefineAsyncArrowStateMachines(analysis.AsyncArrows, smBuilder);

        // Emit stub method body (creates state machine and starts it)
        EmitAsyncStubMethod(methodBuilder, smBuilder, method.Parameters, isInstanceMethod: true);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
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
            AsyncMethods = null,
            AsyncArrowBuilders = _asyncArrowBuilders,
            AsyncArrowOuterBuilders = _asyncArrowOuterBuilders,
            AsyncArrowParentBuilders = _asyncArrowParentBuilders
        };

        // Emit MoveNext body
        var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis);
        moveNextEmitter.EmitMoveNext(method.Body, ctx, typeof(object));

        // Emit MoveNext bodies for async arrows
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_asyncArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                var arrowAnalysis = AnalyzeAsyncArrow(arrowInfo.Arrow);
                var arrow = arrowInfo.Arrow;

                List<Stmt> bodyStatements;
                if (arrow.BlockBody != null)
                {
                    bodyStatements = arrow.BlockBody;
                }
                else if (arrow.ExpressionBody != null)
                {
                    var returnToken = new Token(TokenType.RETURN, "return", null, 0);
                    bodyStatements = [new Stmt.Return(returnToken, arrow.ExpressionBody)];
                }
                else
                {
                    bodyStatements = [];
                }

                var arrowEmitter = new AsyncArrowMoveNextEmitter(arrowBuilder,
                    new AsyncStateAnalyzer.AsyncFunctionAnalysis(
                        arrowAnalysis.AwaitCount,
                        [],  // AwaitPoints not needed for emission
                        arrowAnalysis.HoistedLocals,
                        [],  // HoistedParameters - arrow params are in ParameterFields
                        false, // HasTryCatch
                        false, // UsesThis
                        []     // AsyncArrows - handled separately via _asyncArrowBuilders
                    ));
                arrowEmitter.EmitMoveNext(bodyStatements, ctx, typeof(object));
            }
        }

        // Finalize async arrow state machine types
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_asyncArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                arrowBuilder.CreateType();
            }
        }

        // Finalize state machine type
        smBuilder.CreateType();
    }
}
