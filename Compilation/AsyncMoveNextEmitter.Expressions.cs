using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase
    // EmitDefaultForType is defined in AsyncMoveNextEmitter.ArrowFunctions.cs

    protected override void EmitAwait(Expr.Await a)
    {
        int stateNumber = _currentAwaitState++;
        var resumeLabel = _stateLabels[stateNumber];
        var continueLabel = _il.DefineLabel();
        var awaiterField = _builder.AwaiterFields[stateNumber];

        // 1. Emit the awaited expression (should produce Task<object>)
        EmitExpression(a.Expression);
        EnsureBoxed();

        // 2. Convert to Task<object> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));

        // 3. Get awaiter: task.GetAwaiter()
        _il.Emit(OpCodes.Call, _builder.GetTaskGetAwaiterMethod());

        // 4. Store awaiter to field
        var awaiterLocal = _il.DeclareLocal(_builder.AwaiterType);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, awaiterField);

        // 5. Check IsCompleted
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Call, _builder.GetAwaiterIsCompletedGetter());
        _il.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend
        // this.<>1__state = stateNumber
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNumber);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, _builder.GetBuilderAwaitUnsafeOnCompletedMethod());

        // return (exit MoveNext)
        _il.Emit(OpCodes.Leave, _endLabel);

        // 7. Resume point (jumped to from state switch)
        _il.MarkLabel(resumeLabel);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult()
        // If we're inside a try block with awaits, wrap GetResult in try/catch
        if (_currentTryCatchExceptionLocal != null)
        {
            var getResultDoneLabel = _il.DefineLabel();

            _il.BeginExceptionBlock();

            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, awaiterField);
            _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());

            // Store result temporarily
            var resultTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.Emit(OpCodes.Leave, getResultDoneLabel);

            _il.BeginCatchBlock(typeof(Exception));
            // Wrap and store exception
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
            _il.Emit(OpCodes.Stloc, _currentTryCatchExceptionLocal);
            // Push null as result
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stloc, resultTemp);
            _il.Emit(OpCodes.Leave, getResultDoneLabel);

            _il.EndExceptionBlock();

            _il.MarkLabel(getResultDoneLabel);
            _il.Emit(OpCodes.Ldloc, resultTemp);
        }
        else
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldflda, awaiterField);
            _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());
        }

        // Result is now on stack
        SetStackUnknown();
    }

    protected override void EmitLiteral(Expr.Literal l)
    {
        switch (l.Value)
        {
            case null:
                EmitNullConstant();
                break;
            case double d:
                EmitDoubleConstant(d);
                break;
            case bool b:
                EmitBoolConstant(b);
                break;
            case string s:
                EmitStringConstant(s);
                break;
            default:
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    protected override void EmitVariable(Expr.Variable v)
    {
        string name = v.Name.Lexeme;

        // Try resolver first (hoisted fields and non-hoisted locals)
        var stackType = _resolver!.TryLoadVariable(name);
        if (stackType != null)
        {
            SetStackType(stackType.Value);
            return;
        }

        // Fallback: Check if it's a function
        if (_ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod))
        {
            // Create TSFunction wrapper
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a namespace - load the static field
        if (_ctx.NamespaceFields?.TryGetValue(name, out var nsField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a top-level variable captured from entry point
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, topLevelField);
            SetStackUnknown();
            return;
        }

        // Not found - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitAssign(Expr.Assign a)
    {
        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();

        // Duplicate for return value
        _il.Emit(OpCodes.Dup);

        // Check if it's a top-level variable captured from entry point
        if (_ctx!.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            _il.Emit(OpCodes.Stsfld, topLevelField);
            SetStackUnknown();
            return;
        }

        // Use resolver to store (consumes one copy, leaves one on stack as return value)
        _resolver!.TryStoreVariable(name);

        SetStackUnknown();
    }

    protected override void EmitSuper(Expr.Super s)
    {
        // Load hoisted 'this' from state machine field
        // In async methods, 'this' is hoisted to the state machine struct
        if (_builder.ThisField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);  // State machine ref
            _il.Emit(OpCodes.Ldfld, _builder.ThisField);
        }
        else
        {
            // Fallback - shouldn't happen if 'this' hoisting is working properly
            _il.Emit(OpCodes.Ldnull);
        }

        // Load the method name
        _il.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");

        // Call GetSuperMethod(instance, methodName) to get a callable wrapper (TSFunction)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetSuperMethod);
        SetStackUnknown();
    }

    protected override void EmitBinary(Expr.Binary b)
    {
        // Emit left and right operands
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        // Use runtime Add method for + since it handles string concatenation
        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _helpers.EmitCallUnknown(_ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                _helpers.EmitArithmeticBinary(OpCodes.Sub);
                break;
            case TokenType.STAR:
                _helpers.EmitArithmeticBinary(OpCodes.Mul);
                break;
            case TokenType.SLASH:
                _helpers.EmitArithmeticBinary(OpCodes.Div);
                break;
            case TokenType.PERCENT:
                _helpers.EmitArithmeticBinary(OpCodes.Rem);
                break;
            case TokenType.LESS:
                _helpers.EmitNumericComparison(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                _helpers.EmitNumericComparisonLe();
                break;
            case TokenType.GREATER:
                _helpers.EmitNumericComparison(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                _helpers.EmitNumericComparisonGe();
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                _helpers.EmitRuntimeEquals(_ctx!.Runtime!.Equals);
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _helpers.EmitRuntimeNotEquals(_ctx!.Runtime!.Equals);
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    protected override void EmitCall(Expr.Call c)
    {
        // Handle console.log specially (handles both Variable and Get patterns)
        if (_helpers.TryEmitConsoleLog(c,
            arg => { EmitExpression(arg); EnsureBoxed(); },
            _ctx!.Runtime!.ConsoleLog))
        {
            return;
        }

        // Static type dispatch via registry (Math, JSON, Object, Array, Number, Promise, Symbol)
        if (c.Callee is Expr.Get staticGet &&
            staticGet.Object is Expr.Variable staticVar &&
            _ctx?.TypeEmitterRegistry != null)
        {
            var staticStrategy = _ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
            if (staticStrategy != null && staticStrategy.TryEmitStaticCall(this, staticGet.Name.Lexeme, c.Arguments))
            {
                SetStackUnknown();
                return;
            }
        }

        // Handle Class.staticMethod() calls
        if (c.Callee is Expr.Get classStaticGet &&
            classStaticGet.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.StaticMethods != null &&
                _ctx.StaticMethods.TryGetValue(resolvedClassName, out var classMethods) &&
                classMethods.TryGetValue(classStaticGet.Name.Lexeme, out var staticMethod))
            {
                var staticMethodParams = staticMethod.GetParameters();
                var paramCount = staticMethodParams.Length;

                // Emit all arguments and save to temps (await may occur in arguments)
                List<LocalBuilder> staticArgTemps = [];
                for (int i = 0; i < c.Arguments.Count; i++)
                {
                    EmitExpression(c.Arguments[i]);
                    EnsureBoxed();
                    var temp = _il.DeclareLocal(typeof(object));
                    _il.Emit(OpCodes.Stloc, temp);
                    staticArgTemps.Add(temp);
                }

                // Load args from temps with proper type conversions
                for (int i = 0; i < staticArgTemps.Count; i++)
                {
                    _il.Emit(OpCodes.Ldloc, staticArgTemps[i]);
                    if (i < staticMethodParams.Length)
                    {
                        var targetType = staticMethodParams[i].ParameterType;
                        if (targetType.IsValueType && targetType != typeof(object))
                        {
                            _il.Emit(OpCodes.Unbox_Any, targetType);
                        }
                    }
                }

                // Pad missing optional arguments with appropriate default values
                for (int i = c.Arguments.Count; i < paramCount; i++)
                {
                    EmitDefaultForType(staticMethodParams[i].ParameterType);
                }

                _il.Emit(OpCodes.Call, staticMethod);
                SetStackUnknown();
                return;
            }
        }

        // Handle Promise instance methods: promise.then(onFulfilled?, onRejected?)
        // promise.catch(onRejected), promise.finally(onFinally)
        if (c.Callee is Expr.Get methodGet)
        {
            string methodName = methodGet.Name.Lexeme;
            if (methodName is "then" or "catch" or "finally")
            {
                EmitPromiseInstanceMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }

            // Try direct dispatch for known class instance methods
            if (TryEmitDirectMethodCall(methodGet.Object, methodName, c.Arguments))
                return;

            // Type-first dispatch: Use TypeEmitterRegistry if we have type information
            var objType = _ctx?.TypeMap?.Get(methodGet.Object);
            if (objType != null && _ctx?.TypeEmitterRegistry != null)
            {
                var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
                if (strategy != null && strategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                    return;

                // Handle union types - try emitters for member types
                if (objType is TypeSystem.TypeInfo.Union union)
                {
                    // Try string emitter if union contains string
                    bool hasStringMember = union.Types.Any(t => t is TypeSystem.TypeInfo.String or TypeSystem.TypeInfo.StringLiteral);
                    if (hasStringMember)
                    {
                        var stringStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                        if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                            return;
                    }

                    // Try array emitter if union contains array
                    bool hasArrayMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Array);
                    if (hasArrayMember)
                    {
                        var arrayStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                        if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                            return;
                    }
                }
            }

            // Fallback: Method name-based dispatch for known built-in methods when type info is unavailable
            // This handles cases where type inference is lost (e.g., loop variables from for-await)
            if (_ctx?.TypeEmitterRegistry != null)
            {
                // String-only methods
                if (methodName is "charAt" or "substring" or "toUpperCase" or "toLowerCase"
                    or "trim" or "replace" or "split" or "startsWith" or "endsWith"
                    or "repeat" or "padStart" or "padEnd" or "charCodeAt" or "lastIndexOf"
                    or "trimStart" or "trimEnd" or "replaceAll" or "at" or "match" or "search")
                {
                    var stringStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                    if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                        return;
                }

                // Array-only methods
                if (methodName is "pop" or "shift" or "unshift" or "map" or "filter" or "forEach"
                    or "push" or "find" or "findIndex" or "some" or "every" or "reduce" or "join"
                    or "reverse")
                {
                    var arrayStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                    if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                        return;
                }
            }

            // Handle ambiguous methods (slice, concat, includes, indexOf) that exist on both string and array
            // Use runtime dispatch when type is unknown
            if (methodName is "slice" or "concat" or "includes" or "indexOf")
            {
                EmitAmbiguousMethodCall(methodGet.Object, methodName, c.Arguments);
                return;
            }
        }

        // Check if it's a direct function call
        if (c.Callee is Expr.Variable funcVar && _ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(funcVar.Name.Lexeme), out var funcMethod))
        {
            // Direct call to known function
            // IMPORTANT: In async context, await can happen in arguments
            // Emit all arguments first and store to temps
            var parameters = funcMethod.GetParameters();
            List<LocalBuilder> directArgTemps = [];

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < c.Arguments.Count)
                {
                    EmitExpression(c.Arguments[i]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                directArgTemps.Add(temp);
            }

            // Now load all args from temps and call
            foreach (var temp in directArgTemps)
            {
                _il.Emit(OpCodes.Ldloc, temp);
            }
            _il.Emit(OpCodes.Call, funcMethod);
            SetStackUnknown();
            return;
        }

        // Generic call through TSFunction
        // IMPORTANT: In async context, await can happen in callee or arguments
        // Emit all parts that may contain await first and store to temps

        // Emit callee first and save to temp
        EmitExpression(c.Callee);
        EnsureBoxed();
        var calleeTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, calleeTemp);

        // Emit all arguments and save to temps
        List<LocalBuilder> argTemps = [];
        foreach (var arg in c.Arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Now build the call with saved values (no awaits can happen here)
        _il.Emit(OpCodes.Ldloc, calleeTemp);

        // Build arguments array from temps
        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < argTemps.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, argTemps[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        SetStackUnknown();
    }

    protected override void EmitGet(Expr.Get g)
    {
        // Special case: Symbol well-known symbols
        if (g.Object is Expr.Variable symV && symV.Name.Lexeme == "Symbol")
        {
            switch (g.Name.Lexeme)
            {
                case "iterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
                    SetStackUnknown();
                    return;
                case "asyncIterator":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolAsyncIterator);
                    SetStackUnknown();
                    return;
                case "toStringTag":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToStringTag);
                    SetStackUnknown();
                    return;
                case "hasInstance":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolHasInstance);
                    SetStackUnknown();
                    return;
                case "isConcatSpreadable":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIsConcatSpreadable);
                    SetStackUnknown();
                    return;
                case "toPrimitive":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolToPrimitive);
                    SetStackUnknown();
                    return;
                case "species":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolSpecies);
                    SetStackUnknown();
                    return;
                case "unscopables":
                    _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolUnscopables);
                    SetStackUnknown();
                    return;
            }
        }

        // Handle static field access: Class.field
        if (g.Object is Expr.Variable classVar &&
            _ctx!.Classes.TryGetValue(_ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            // Try to find static field using stored FieldBuilders
            if (_ctx.StaticFields != null &&
                _ctx.StaticFields.TryGetValue(resolvedClassName, out var classFields) &&
                classFields.TryGetValue(g.Name.Lexeme, out var staticField))
            {
                _il.Emit(OpCodes.Ldsfld, staticField);
                SetStackUnknown();
                return;
            }
        }

        // Default: dynamic property access
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        string name = ca.Name.Lexeme;

        // IMPORTANT: Emit the new value first (may contain await which clears stack)
        EmitExpression(ca.Value);
        EnsureBoxed();
        var valueTemp = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, valueTemp);

        // Now load current value (after await if any)
        EmitVariable(new Expr.Variable(ca.Name));
        EnsureBoxed();

        // Load the value back
        _il.Emit(OpCodes.Ldloc, valueTemp);

        // Apply operation
        var op = ca.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            default:
                // For other compound ops, convert to double
                var rightLocal = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Ldloc, rightLocal);
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

                switch (op)
                {
                    case TokenType.MINUS_EQUAL:
                        _il.Emit(OpCodes.Sub);
                        break;
                    case TokenType.STAR_EQUAL:
                        _il.Emit(OpCodes.Mul);
                        break;
                    case TokenType.SLASH_EQUAL:
                        _il.Emit(OpCodes.Div);
                        break;
                    default:
                        _il.Emit(OpCodes.Add);
                        break;
                }
                _il.Emit(OpCodes.Box, typeof(double));
                break;
        }

        // Store result
        _il.Emit(OpCodes.Dup);
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

        SetStackUnknown();
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = pi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load, increment, store, return new value
            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            _il.Emit(OpCodes.Dup);
            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }
        }
        SetStackUnknown();
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v)
        {
            string name = v.Name.Lexeme;
            double delta = poi.Operator.Type == TokenType.PLUS_PLUS ? 1.0 : -1.0;

            // Load original value
            EmitVariable(v);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);

            // Increment and store
            _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _il.Emit(OpCodes.Ldc_R8, delta);
            _il.Emit(OpCodes.Add);
            _il.Emit(OpCodes.Box, typeof(double));

            var field = _builder.GetVariableField(name);
            if (field != null)
            {
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else if (_ctx!.Locals.TryGetLocal(name, out var local))
            {
                _il.Emit(OpCodes.Stloc, local);
            }

            // Original value is on stack
        }
        SetStackUnknown();
    }

    protected override void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Emit the path expression
        EmitExpression(di.PathExpression);
        EnsureBoxed();

        // Convert to string
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", [typeof(object)])!);

        // Push current module path (or empty string if not in module context)
        _il.Emit(OpCodes.Ldstr, _ctx?.CurrentModulePath ?? "");

        // Call DynamicImportModule(path, currentModulePath) -> Task<object?>
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.DynamicImportModule);

        // Wrap Task<object?> in SharpTSPromise
        _il.Emit(OpCodes.Call, _ctx.Runtime.WrapTaskAsPromise);

        SetStackUnknown();
    }

    protected override void EmitImportMeta(Expr.ImportMeta im)
    {
        // Get current module path and convert to file:// URL
        string url = _ctx?.CurrentModulePath ?? "";
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }

        // Create Dictionary<string, object> and add "url" property
        _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "url");
        _il.Emit(OpCodes.Ldstr, url);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Wrap in SharpTSObject
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        SetStackUnknown();
    }

    protected override void EmitUnknownExpression(Expr expr)
    {
        // Unsupported expression - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    #region Ambiguous Method Runtime Dispatch

    /// <summary>
    /// Handles methods that exist on both strings and arrays at runtime.
    /// Used as fallback when type information is not available.
    /// </summary>
    private void EmitAmbiguousMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the object
        EmitExpression(obj);
        EnsureBoxed();

        var objLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, objLocal);

        // Check if it's a string
        var isStringLabel = _il.DefineLabel();
        var isListLabel = _il.DefineLabel();
        var doneLabel = _il.DefineLabel();

        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Isinst, typeof(string));
        _il.Emit(OpCodes.Brtrue, isStringLabel);

        // Assume it's a list if not a string
        _il.Emit(OpCodes.Br, isListLabel);

        // String path
        _il.MarkLabel(isStringLabel);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Castclass, typeof(string));

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Castclass, typeof(string));
                }
                else
                {
                    _il.Emit(OpCodes.Ldstr, "");
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringSlice);
                break;

            case "concat":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringConcat);
                break;
        }

        _il.Emit(OpCodes.Br, doneLabel);

        // List path
        _il.MarkLabel(isListLabel);
        _il.Emit(OpCodes.Ldloc, objLocal);
        _il.Emit(OpCodes.Castclass, typeof(List<object>));

        switch (methodName)
        {
            case "includes":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayIncludes);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;

            case "indexOf":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayIndexOf);
                _il.Emit(OpCodes.Box, typeof(double));
                break;

            case "slice":
                _il.Emit(OpCodes.Ldc_I4, arguments.Count);
                _il.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArraySlice);
                break;

            case "concat":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.ArrayConcat);
                break;
        }

        _il.MarkLabel(doneLabel);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a Promise instance method call (.then, .catch, .finally).
    /// These methods take callbacks and return a new Promise (Task).
    /// </summary>
    private void EmitPromiseInstanceMethodCall(Expr promise, string methodName, List<Expr> arguments)
    {
        // Emit the promise (should be Task<object?>)
        EmitExpression(promise);
        EnsureBoxed();

        // Cast to Task<object?> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object?>));

        switch (methodName)
        {
            case "then":
                // promise.then(onFulfilled?, onRejected?)
                // PromiseThen(Task<object?> promise, object? onFulfilled, object? onRejected)

                // onFulfilled callback (optional)
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                // onRejected callback (optional)
                if (arguments.Count > 1)
                {
                    EmitExpression(arguments[1]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseThen);
                break;

            case "catch":
                // promise.catch(onRejected)
                // PromiseCatch(Task<object?> promise, object? onRejected)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseCatch);
                break;

            case "finally":
                // promise.finally(onFinally)
                // PromiseFinally(Task<object?> promise, object? onFinally)

                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }

                _il.Emit(OpCodes.Call, _ctx!.Runtime!.PromiseFinally);
                break;

            default:
                // Unknown method - just return the promise unchanged
                break;
        }

        SetStackUnknown();
    }

    #endregion
}
