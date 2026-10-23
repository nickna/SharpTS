using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    /// <summary>
    /// Finalize return handling for methods that had returns inside exception blocks.
    /// Must be called after emitting the method body but before the final Ret.
    /// </summary>
    public void FinalizeReturns()
    {
        if (_ctx.ReturnValueLocal != null || _ctx.HasDeferredVoidReturn)
        {
            // Mark the return label and emit the actual return
            // Use builder's MarkLabel since ReturnLabel was defined with builder
            _ctx.ILBuilder.MarkLabel(_ctx.ReturnLabel);
            if (_ctx.ReturnValueLocal != null)
                IL.Emit(OpCodes.Ldloc, _ctx.ReturnValueLocal);
            if (_ctx.CapturesClassConstructorReturn)
                PublishClassConstructorReturnCompletion();
            IL.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Check if the method had returns inside exception blocks that need finalization.
    /// </summary>
    public bool HasDeferredReturns => _ctx.ReturnValueLocal != null || _ctx.HasDeferredVoidReturn;

    /// <summary>
    /// Emit default parameter value checks at function entry.
    /// For each parameter with a default value, checks if arg is the JavaScript
    /// <c>undefined</c> sentinel and assigns the default.
    /// </summary>
    /// <param name="parameters">The parameter list</param>
    /// <param name="isInstanceMethod">True if this is an instance method (has implicit this at arg 0)</param>
    /// <param name="hasOwnThis">True if function has __this as first explicit parameter</param>
    /// <param name="paramTypes">Optional resolved parameter types. When provided, value-type
    /// parameters are skipped — the null-check pattern (<c>ldarg; brtrue</c>) does not apply
    /// to value types since they cannot be null. Value-type defaults are still handled by
    /// <see cref="OverloadGenerator"/> for direct calls; the $TSFunction.Invoke path does
    /// not support user-specified value-type defaults.</param>
    public void EmitDefaultParameters(
        List<Stmt.Parameter> parameters,
        bool isInstanceMethod,
        bool hasOwnThis = false,
        Type[]? paramTypes = null)
        => EmitDefaultParameters(
            parameters,
            argumentOffset: (isInstanceMethod ? 1 : 0) + (hasOwnThis ? 1 : 0),
            paramTypes: paramTypes);

    /// <summary>
    /// Offset-based parameter-default lowering used by the shared function
    /// environment prologue. Unlike the compatibility overload, this directly
    /// describes the emitted signature and therefore also covers display-class
    /// and explicit-<c>this</c> prefixes without reconstructing their shape.
    /// The synchronous environment prologue opts into incremental capture
    /// publication; direct iterator callers retain their own transfer policy.
    /// </summary>
    internal void EmitDefaultParameters(
        List<Stmt.Parameter> parameters,
        int argumentOffset,
        Type[]? paramTypes = null,
        List<Stmt>? body = null,
        bool publishCapturedParameters = false)
    {
        var builder = _ctx.ILBuilder;
        var initializers = PrepareParameterBindingInitializers(body);

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue == null || paramTypes != null && paramTypes[i].IsValueType)
            {
                EmitParameterBindingInitializer(param, initializers, ParameterTdzNames(parameters.Skip(i + 1), initializers));
                if (publishCapturedParameters) PublishCapturedParameter(param);
                continue;
            }

            // Skip value-type parameters when we know the resolved types: `ldarg; brtrue`
            // is meaningless on a double/bool, which can't hold the `$Undefined` sentinel.
            // A *defaulted* param is widened to an `object` slot upstream (ParameterTypeResolver
            // WidenDefaultedParamsToObject / WidenValueTypeDefaultedMethodParams), so it does NOT
            // hit this guard — its default fires for direct AND $TSFunction.Invoke callers (#925).
            // This only defends a genuinely value-typed slot that reached here without that widening.
            int argIndex = i + argumentOffset;

            // JS spec: defaults fire only when the argument is `undefined` — missing
            // arguments are padded with the $Undefined singleton by call emitters.
            // CLR null represents the JavaScript null value and must be preserved.
            var fireDefault = builder.DefineLabel($"fire_default_{i}");
            var skipDefault = builder.DefineLabel($"skip_default_{i}");

            if (_ctx.Runtime?.Sentinels.UndefinedInstance != null)
            {
                IL.Emit(OpCodes.Ldarg, argIndex);
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.Sentinels.UndefinedInstance);
                IL.Emit(OpCodes.Beq, fireDefault);
            }

            IL.Emit(OpCodes.Br, skipDefault);

            builder.MarkLabel(fireDefault);
            EmitDefaultParameterExpression(param.DefaultValue, ParameterTdzNames(parameters.Skip(i), initializers));
            EmitBoxIfNeeded(param.DefaultValue);
            IL.Emit(OpCodes.Starg, argIndex);

            builder.MarkLabel(skipDefault);
            EmitParameterBindingInitializer(param, initializers, ParameterTdzNames(parameters.Skip(i + 1), initializers));
            if (publishCapturedParameters) PublishCapturedParameter(param);
        }
    }

    private void PublishCapturedParameter(Stmt.Parameter parameter)
    {
        string name = parameter.Name.Lexeme;
        LocalBuilder? displayClass = null;
        FieldBuilder? field = null;
        if (_ctx.FunctionDisplayClassLocal is { } functionDisplayClass
            && _ctx.CapturedFunctionLocals?.Contains(name) == true
            && _ctx.FunctionDisplayClassFields?.TryGetValue(name, out var functionField) == true)
        {
            displayClass = functionDisplayClass;
            field = functionField;
        }
        else if (_ctx.ArrowScopeDisplayClassLocal is { } scopeDisplayClass
            && _ctx.CapturedArrowLocals?.Contains(name) == true
            && _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var scopeField) == true)
        {
            displayClass = scopeDisplayClass;
            field = scopeField;
        }
        if (displayClass is null || field is null || !_ctx.TryGetParameter(name, out int argument)) return;

        // A later default can invoke or mutate this binding through a closure.
        // Publish once at initialization, rather than reseeding from a stale arg
        // after all defaults have finished.
        Type parameterType = _ctx.TryGetParameterType(name, out var type) ? type ?? _ctx.Types.Object : _ctx.Types.Object;
        IL.Emit(OpCodes.Ldloc, displayClass);
        IL.Emit(OpCodes.Ldarg, argument);
        if (field.FieldType == _ctx.Types.Double && parameterType != _ctx.Types.Double)
        {
            if (parameterType.IsValueType) IL.Emit(OpCodes.Box, parameterType);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.NumericCoercion.ConvertToNumber);
        }
        else if (field.FieldType == _ctx.Types.Object && parameterType.IsValueType)
            IL.Emit(OpCodes.Box, parameterType);
        IL.Emit(OpCodes.Stfld, field);
        (_ctx.PublishedCapturedParameterNames ??= new(StringComparer.Ordinal)).Add(name);
        _ctx.UseOwnedCapturedParameterStorage = true;
    }

    internal void EmitDefaultParameterExpression(Expr expression, IEnumerable<string> tdzNames)
        => EmitInParameterEnvironment(tdzNames, () => EmitExpression(expression));

    internal Dictionary<Token, Stmt.Sequence>? PrepareParameterBindingInitializers(List<Stmt>? body)
    {
        if (body is null) return null;
        Dictionary<Token, Stmt.Sequence>? initializers = null;
        foreach (var statement in body)
        {
            if (statement is not Stmt.Sequence { ParameterName: { } parameter } initializer) continue;
            (initializers ??= new(ReferenceEqualityComparer.Instance)).Add(parameter, initializer);
            // Captured parameter bindings own their cells before any default can create a closure.
            EnterLoopBodyCellScope(initializer.Statements, []);
        }
        return initializers;
    }

    internal static IEnumerable<string> ParameterTdzNames(IEnumerable<Stmt.Parameter> parameters,
        IReadOnlyDictionary<Token, Stmt.Sequence>? initializers)
    {
        foreach (var parameter in parameters)
        {
            yield return parameter.Name.Lexeme;
            if (initializers?.TryGetValue(parameter.Name, out var initializer) == true)
                foreach (var name in LexicalDeclarationBindings.Collect(initializer.Statements))
                    yield return name.Lexeme;
        }
    }

    internal void EmitParameterBindingInitializer(Stmt.Parameter parameter,
        IReadOnlyDictionary<Token, Stmt.Sequence>? initializers, IEnumerable<string> laterTdzNames)
    {
        if (initializers is null || !initializers.TryGetValue(parameter.Name, out var initializer)) return;
        var tdzNames = laterTdzNames.Concat(LexicalDeclarationBindings.Collect(initializer.Statements)
            .Select(name => name.Lexeme)).ToHashSet(StringComparer.Ordinal);
        var capturedFunctionLocals = _ctx.CapturedFunctionLocals;
        var capturedArrowLocals = _ctx.CapturedArrowLocals;
        EmitInParameterEnvironment(tdzNames, () => EmitBindingStatements(initializer.Statements));
        (_ctx.InitializedParameterInitializers ??= new(ReferenceEqualityComparer.Instance)).Add(initializer);

        void EmitBindingStatements(IEnumerable<Stmt> statements)
        {
            foreach (var statement in statements)
            {
                if (statement is Stmt.Sequence sequence) EmitBindingStatements(sequence.Statements);
                else
                {
                    string? name = statement switch
                    {
                        Stmt.Var variable => variable.Name.Lexeme,
                        Stmt.Const constant => constant.Name.Lexeme,
                        _ => null,
                    };
                    if (name is not null)
                    {
                        (_ctx.InitializedParameterBindingNames ??= new(StringComparer.Ordinal)).Add(name);
                        if (capturedFunctionLocals?.Contains(name) == true) _ctx.CapturedFunctionLocals?.Add(name);
                        if (capturedArrowLocals?.Contains(name) == true) _ctx.CapturedArrowLocals?.Add(name);
                    }
                    EmitStatement(statement);
                    if (name is not null) tdzNames.Remove(name);
                }
            }
        }
    }

    private void EmitInParameterEnvironment(IEnumerable<string> tdzNames, Action emit)
    {
        var capturedFunctionLocals = _ctx.CapturedFunctionLocals;
        var capturedArrowLocals = _ctx.CapturedArrowLocals;
        var parameterTdzNames = _ctx.DefaultParameterTdzNames;
        _ctx.DefaultParameterTdzNames = tdzNames as IReadOnlySet<string>
            ?? tdzNames.ToHashSet(StringComparer.Ordinal);
        try
        {
            // Defaults run outside this callable's body environment. Its
            // capture storage is allocated already so closures can retain
            // the shared instance, but body bindings must not hide outer
            // bindings while evaluating a default. Keep inherited capture
            // paths and the incoming parameter slots available.
            if (_ctx.FunctionDisplayClassLocal != null)
                _ctx.CapturedFunctionLocals = capturedFunctionLocals is null ? null
                    : capturedFunctionLocals.Where(name => _ctx.InitializedParameterBindingNames?.Contains(name) == true
                        || _ctx.PublishedCapturedParameterNames?.Contains(name) == true)
                        .ToHashSet(StringComparer.Ordinal);
            if (_ctx.ArrowScopeDisplayClassLocal != null)
                _ctx.CapturedArrowLocals = capturedArrowLocals is null ? null
                    : capturedArrowLocals.Where(name => _ctx.InitializedParameterBindingNames?.Contains(name) == true
                        || _ctx.PublishedCapturedParameterNames?.Contains(name) == true)
                        .ToHashSet(StringComparer.Ordinal);
            emit();
        }
        finally
        {
            _ctx.CapturedFunctionLocals = capturedFunctionLocals;
            _ctx.CapturedArrowLocals = capturedArrowLocals;
            _ctx.DefaultParameterTdzNames = parameterTdzNames;
        }
    }

    /// <summary>
    /// Instantiates selected function-body var bindings before body evaluation.
    /// Parameter copies retain their final value; forward vars begin undefined.
    /// Default-created closures retain the parameter environment's existing slots.
    /// </summary>
    internal void InitializeFunctionBodyVarBindings(List<Stmt>? body)
    {
        if (body is null) return;
        var analyzer = _ctx.ClosureAnalyzer;
        foreach (var (declaration, parameter) in analyzer?.GetParameterBodyCopies(body) ?? [])
        {
            CreateCell(declaration, () =>
            {
                var parameterType = _resolver.TryLoadVariable(parameter.Lexeme)
                    ?? throw new InvalidOperationException($"Parameter binding '{parameter.Lexeme}' is unavailable.");
                SetStackType(parameterType);
                EnsureBoxed();
            });
        }
        foreach (var declaration in analyzer?.GetUndefinedBodyVars(body) ?? [])
            CreateCell(declaration, () => IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance));
        InitializeVarLoopHeadBindings(body);

        void CreateCell(Token declaration, Action emitValue)
        {
            string name = declaration.Lexeme;
            if (_ctx.LoopBodyCellDeclarations.TryGetValue(name, out var existing)
                && ReferenceEquals(existing, declaration)) return;
            emitValue();
            IL.Emit(OpCodes.Newobj, _ctx.Types.StrongBoxOfObjectCtor);
            var cell = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
            IL.Emit(OpCodes.Stloc, cell);
            _ctx.CellBindingLocals[name] = cell;
            _ctx.LoopBodyCellDeclarations[name] = declaration;
            _ctx.LexicalCellStorageNames[declaration] = name;
        }
    }

    private HashSet<string>? _ownedVarLoopHeadNames;

    /// <summary>Creates containing-callable homes for var loop heads before body evaluation.</summary>
    private void InitializeVarLoopHeadBindings(List<Stmt> body)
    {
        var heads = SharpTS.Execution.InterpreterVarDeclarations.CollectLoopHeads(body);
        if (heads.Count == 0) return;
        _ownedVarLoopHeadNames = heads.Select(head => head.Lexeme).ToHashSet(StringComparer.Ordinal);
        var functionNames = SharpTS.Runtime.ParameterBinder.BodyVarDeclarations(body)
            .Where(declaration => declaration.IsFunction)
            .Select(declaration => declaration.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
        foreach (var head in heads)
        {
            string name = head.Lexeme;
            if (_ctx.HasLoopBodyBindingAccess(name) || _ctx.TryGetParameter(name, out _)
                || _ctx.InitializedParameterNames?.Contains(name) == true || functionNames.Contains(name))
                continue;
            if (_ctx.CapturedFunctionLocals?.Contains(name) == true
                && _ctx.FunctionDisplayClassFields?.TryGetValue(name, out var functionField) == true
                && _ctx.FunctionDisplayClassLocal != null)
            {
                // A proven numeric capture cannot be observed before its initializer.
                if (functionField.FieldType == _ctx.Types.Object)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                    IL.Emit(OpCodes.Stfld, functionField);
                }
                continue;
            }
            if (_ctx.CapturedArrowLocals?.Contains(name) == true
                && _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowField) == true
                && _ctx.ArrowScopeDisplayClassLocal != null)
            {
                IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
                IL.Emit(OpCodes.Stfld, arrowField);
                continue;
            }
            if (_ctx.Locals.HasLocal(name)) continue;
            var local = _ctx.Locals.DeclareLocal(name, _ctx.Types.Object, head);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>Enters one evaluated class-expression static block activation.</summary>
    internal void EmitClassExpressionStaticBlock(Stmt.StaticBlock block)
    {
        _ctx.Locals.EnterScope();
        List<LoopBodyCellScopeEntry> vars = [];
        try
        {
            // Every owned var masks outer storage; only selected captures share a box.
            foreach (var declaration in _ctx.ClosureAnalyzer?.GetClassStaticBlockVars(block.Body) ?? [])
                EnterLexicalCellBinding(declaration, declaration, vars, classStaticBlockVar: true);
            EmitStatement(new Stmt.Block(block.Body));
        }
        finally
        {
            ExitLoopBodyCellScope(vars);
            _ctx.Locals.ExitScope();
        }
    }

    /// <summary>
    /// Routes the shared static-dispatch helpers' extra-arg boxing through
    /// EmitBoxIfNeeded so ILEmitter keeps its TypeMap/literal-aware boxing
    /// (the base seam defaults to stack-state-only EnsureBoxed).
    /// </summary>
    protected override void EnsureBoxedArg(Expr arg) => EmitBoxIfNeeded(arg);

    public void EmitBoxIfNeeded(Expr expr)
    {
        // First, check if we already have an unboxed value type on the stack
        // This handles typed locals and other cases where _stackType is known
        if (_stackType == StackType.Double)
        {
            EmitBoxDouble();
            return;
        }
        if (_stackType == StackType.Boolean)
        {
            EmitBoxBool();
            return;
        }

        // Optimization: Use TypeMap to skip boxing check for known reference types
        // This avoids the pattern match overhead for expressions that definitely don't need boxing
        TypeInfo? type = _ctx.TypeMap?.Get(expr);
        if (type != null)
        {
            // Reference types never need boxing - skip the literal check entirely
            if (type is TypeInfo.String
                or TypeInfo.Array
                or TypeInfo.Instance
                or TypeInfo.Record
                or TypeInfo.Class
                or TypeInfo.Interface
                or TypeInfo.Function
                or TypeInfo.Void
                or TypeInfo.Null)
            {
                return;
            }
            // For primitives (number/boolean) and other types (Any, Union, etc.),
            // fall through to the literal check - only literals produce unboxed values
        }

        // Expressions may produce unboxed value types: literals, arithmetic (StackType.Double),
        // comparisons (StackType.Boolean), and typed locals. The _stackType checks above
        // handle these cases. The literal check below is a fallback for when _stackType
        // tracking doesn't catch a literal (e.g., in complex expression trees).
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double)
            {
                EmitBoxDouble();
            }
            else if (lit.Value is bool)
            {
                EmitBoxBool();
            }
        }
    }

    /// <summary>
    /// Emits conversion from the current stack value to the target parameter type.
    /// Handles boxing for object, unboxing for value types, and pass-through for matching types.
    /// </summary>
    public new void EmitConversionForParameter(Expr expr, Type targetType)
    {
        // If target is object, box value types
        if (targetType == typeof(object))
        {
            EmitBoxIfNeeded(expr);
            return;
        }

        // Check if the expression produces a matching type
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double && targetType == typeof(double))
            {
                // Already emitted as double, no conversion needed
                return;
            }
            if (lit.Value is bool && targetType == typeof(bool))
            {
                // Already emitted as bool, no conversion needed
                return;
            }
            if (lit.Value is string && targetType == typeof(string))
            {
                // Already emitted as string, no conversion needed
                return;
            }
        }

        // If stack has a known type matching target, no conversion needed
        if (_stackType == StackType.Double && targetType == typeof(double))
            return;
        if (_stackType == StackType.Boolean && targetType == typeof(bool))
            return;

        // Check if target is a union type using marker interface
        if (UnionTypeHelper.IsUnionType(targetType))
        {
            // Determine source type from stack or expression
            Type? sourceType = _stackType switch
            {
                StackType.Double => typeof(double),
                StackType.Boolean => typeof(bool),
                StackType.String => typeof(string),
                _ => null
            };

            // If stack type is unknown, try to determine from expression
            if (sourceType == null && expr is Expr.Literal exprLit)
            {
                sourceType = exprLit.Value switch
                {
                    double => typeof(double),
                    string => typeof(string),
                    bool => typeof(bool),
                    _ => null
                };
            }

            // Try to find an implicit conversion operator
            if (sourceType != null && _ctx.UnionGenerator != null)
            {
                var implicitOp = _ctx.UnionGenerator.GetImplicitConversion(targetType, sourceType);

                if (implicitOp != null)
                {
                    IL.Emit(OpCodes.Call, implicitOp);
                    return;
                }
            }

            // Fallback: box the value and create a default union
            // This won't work correctly but prevents crashes
            EmitBoxIfNeeded(expr);
            var valueLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, valueLocal);
            var unionLocal = IL.DeclareLocal(targetType);
            IL.Emit(OpCodes.Ldloca, unionLocal);
            IL.Emit(OpCodes.Initobj, targetType);
            IL.Emit(OpCodes.Ldloc, unionLocal);
            return;
        }

        // If target is a value type and we have an object, unbox
        if (targetType.IsValueType)
        {
            if (_stackType != StackType.Double && _stackType != StackType.Boolean)
                IL.Emit(OpCodes.Unbox_Any, targetType);
            return;
        }

        // Target is a non-object reference type. EmitExpression can leave a broader `object` on the
        // stack — an `any`-typed value, or a GetIndex/GetProperty result — that the callee's typed
        // string slot would reject at IL verification even though the JIT tolerates it. Emit the
        // downcast the verifier needs (#1246). Ordinary user-class instance slots also need it
        // after constructor return selection leaves an object on the stack. Replacement-capable
        // instances use object slots and exit above. Delegate/List/Task runtime representations
        // remain outside this conversion.
        if ((_ctx.Types.IsString(targetType) && _stackType != StackType.String)
            || _ctx.TypeMapper.IsUserClassReferenceType(targetType))
        {
            EmitBoxIfNeeded(expr);
            IL.Emit(OpCodes.Castclass, targetType);
        }
    }

    /// <summary>
    /// Emits a default value for the given type.
    /// Used when padding missing arguments (fallback when no overload matches).
    /// </summary>
    public new void EmitDefaultForType(Type type) => base.EmitDefaultForType(type);

    public override void EmitExpressionAsDouble(Expr expr)
    {
        if (expr is Expr.Call call && TryEmitTypedNumericIndirectCall(call))
            return;

        if (expr is Expr.Call
            {
                Optional: false, Arguments.Count: 0,
                Callee: Expr.Get { Optional: false, Name.Lexeme: "shift", Object: Expr.Variable receiver }
            }
            && _ctx.TryGetPromotedQueueLocal(receiver.Name.Lexeme) is { Queue.ShiftNumber: { } shift } queue)
        {
            IL.Emit(OpCodes.Ldloc, queue.Local);
            IL.Emit(OpCodes.Call, shift);
            SetStackType(StackType.Double);
            return;
        }
        if (expr is Expr.GetIndex { Optional: false, Object: Expr.Variable indexed } index
            && _ctx.TryGetPromotedQueueLocal(indexed.Name.Lexeme) is { Queue.GetNumber: not null } indexedQueue)
        {
            EmitQueueGet(indexedQueue.Local, indexedQueue.Queue, index.Index, numeric: true);
            return;
        }
        if (expr is Expr.Get stableRecordGet
            && TryEmitStableRecordDestructureGetAsDouble(stableRecordGet))
            return;

        if (expr is Expr.GetIndex stableArrayGet
            && TryEmitStableArrayDestructureGetAsDouble(stableArrayGet))
            return;

        if (expr is Expr.GetIndex flattenedRestIndex
            && TryEmitFlattenedNumericRestIndex(flattenedRestIndex))
            return;

        // A number[] read consumed as a number can keep the numeric-mode $Array
        // result native while its guarded cold arm performs the same ordinary
        // property lookup + ToNumber coercion this method used previously.
        if (expr is Expr.Get { Optional: false, Name.Lexeme: "length" } arrayLength &&
            _ctx.TypeMap?.Get(arrayLength.Object) is TypeInfo.Array)
        {
            EmitGet(arrayLength);
            EnsureDouble();
            return;
        }

        if (expr is Expr.Get interfaceGet && TryEmitInterfaceRecordGet(interfaceGet, numericConsumer: true))
            return;

        if (expr is Expr.GetIndex getIndex && TryEmitGuardedArrayIndex(getIndex))
            return;

        // Emit expression and ensure result is a double on the stack
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            // Literal double - push directly
            EmitDoubleConstant(d);
        }
        else if (expr is Expr.Literal intLit && intLit.Value is int i)
        {
            EmitDoubleConstant((double)i);
        }
        else if (expr is Expr.Unary unary
                 && ConstantFolder.TryFoldUnary(unary, out object? folded)
                 && folded is double number)
        {
            // Unary numeric literals are otherwise emitted through the boxed
            // expression path and immediately converted back to double.
            EmitDoubleConstant(number);
        }
        else
        {
            // Other expressions - emit and convert if needed
            EmitExpression(expr);
            EnsureDouble();
        }
    }

    private void EmitUnboxToDouble()
    {
        // Convert object to double using Convert.ToDouble
        EmitConvertToDouble();
    }

    private bool IsStringExpression(Expr expr)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value is string,
            Expr.TemplateLiteral => true,
            Expr.Binary bin when bin.Operator.Type == TokenType.PLUS =>
                IsStringExpression(bin.Left) || IsStringExpression(bin.Right),
            _ => false
        };
    }

    protected override void EmitTruthyCheck()
    {
        if (_ctx.Runtime?.Booleans.IsTruthy != null)
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime.Booleans.IsTruthy);
            return;
        }

        // Truthy check for boxed value:
        // - null => false
        // - boxed false => false
        // - everything else => true
        var builder = _ctx.ILBuilder;
        var checkBoolLabel = builder.DefineLabel("truthy_checkbool");
        var falseLabel = builder.DefineLabel("truthy_false");
        var endLabel = builder.DefineLabel("truthy_end");

        // Check for null
        IL.Emit(OpCodes.Dup);
        builder.Emit_Brfalse(falseLabel);

        // Check if it's a boolean
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, _ctx.Types.Boolean);
        builder.Emit_Brfalse(checkBoolLabel);

        // It's a boxed bool - unbox and use the value
        IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(checkBoolLabel);
        // Not null and not bool - always truthy
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(falseLabel);
        // Null - false
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_0);

        builder.MarkLabel(endLabel);
    }

    private static bool IsComparisonOp(TokenType op) =>
        op is TokenType.LESS or TokenType.GREATER or TokenType.LESS_EQUAL or TokenType.GREATER_EQUAL
            or TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL
            or TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL;
}
