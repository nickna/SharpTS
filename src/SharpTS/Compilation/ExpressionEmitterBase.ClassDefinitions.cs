using System.Reflection.Emit;
using System.Reflection;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool IsRuntimeClassDeclarationReference(Expr expression)
    {
        if (expression is not Expr.Variable || Ctx.RuntimeClassDeclarations == null || Ctx.TypeMap == null)
            return false;
        static int DeclarationId(SharpTS.TypeSystem.TypeInfo? type) => type switch
        {
            SharpTS.TypeSystem.TypeInfo.Class cls => cls.Core.DeclarationId,
            SharpTS.TypeSystem.TypeInfo.GenericClass cls => cls.Core.DeclarationId,
            SharpTS.TypeSystem.TypeInfo.InstantiatedGeneric generic => DeclarationId(generic.GenericDefinition),
            _ => 0
        };
        int declarationId = DeclarationId(Ctx.TypeMap.Get(expression));
        return declarationId != 0 && Ctx.RuntimeClassDeclarations.Keys.Any(declaration =>
            Ctx.TypeMap.GetClassType(declaration)?.Core.DeclarationId == declarationId);
    }

    protected void EmitDynamicConstruction(Expr.New expression)
    {
        var constructor = SpillBoxed(expression.Callee);
        EmitArgsArrayWithSpread(expression.Arguments);
        var arguments = IL.DeclareLocal(Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, arguments);
        EmitCloseEvaluatedGenericConstructor(constructor, expression);
        IL.Emit(OpCodes.Ldloc, constructor);
        IL.Emit(OpCodes.Ldloc, arguments);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.DynamicConstruction.Value);
        SetStackUnknown();
    }

    // Close the evaluated Type, never the structurally checked annotation's class.
    // Run after argument evaluation, matching the existing reflection construction path.
    protected void EmitCloseEvaluatedGenericConstructor(LocalBuilder constructor, Expr.New expression)
    {
        var checkedArguments = (Ctx.TypeMap?.Get(expression) as SharpTS.TypeSystem.TypeInfo.Instance)?.ClassType
            as SharpTS.TypeSystem.TypeInfo.InstantiatedGeneric;
        Type[] arguments;
        if (checkedArguments != null)
            arguments = checkedArguments.TypeArguments.Select(argument => argument switch
            {
                SharpTS.TypeSystem.TypeInfo.TypeParameter parameter when Ctx.GenericTypeParameters.TryGetValue(parameter.Name, out var owned) => owned,
                SharpTS.TypeSystem.TypeInfo.StringLiteral => Types.String,
                SharpTS.TypeSystem.TypeInfo.NumberLiteral => Types.Double,
                SharpTS.TypeSystem.TypeInfo.BooleanLiteral => Types.Boolean,
                _ => Ctx.TypeMapper.MapTypeInfo(argument)
            }).ToArray();
        else arguments = expression.TypeArgs?.Select(ResolveTypeArg).ToArray() ?? [];

        var done = IL.DefineLabel();
        var type = IL.DeclareLocal(Types.Type);
        IL.Emit(OpCodes.Ldloc, constructor);
        IL.Emit(OpCodes.Isinst, Types.Type);
        IL.Emit(OpCodes.Stloc, type);
        IL.Emit(OpCodes.Ldloc, type);
        IL.Emit(OpCodes.Brfalse, done);
        IL.Emit(OpCodes.Ldloc, type);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.Type, "get_IsGenericTypeDefinition"));
        IL.Emit(OpCodes.Brfalse, done);
        LocalBuilder? supplied = null;
        if (arguments.Length > 0)
        {
            supplied = IL.DeclareLocal(Types.MakeArrayType(Types.Type));
            IL.Emit(OpCodes.Ldc_I4, arguments.Length);
            IL.Emit(OpCodes.Newarr, Types.Type);
            for (int i = 0; i < arguments.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldtoken, Types.IsVoid(arguments[i]) ? Types.Object : arguments[i]);
                IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            IL.Emit(OpCodes.Stloc, supplied);
        }
        var guest = IL.DefineLabel();
        var closed = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, type);
        IL.Emit(OpCodes.Ldtoken, Ctx.Runtime!.DynamicConstruction.FactoryAttributeType);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Callvirt, typeof(MemberInfo).GetMethod(nameof(MemberInfo.IsDefined), [typeof(Type), typeof(bool)])!);
        IL.Emit(OpCodes.Brtrue, guest);
        // Native CLR constructor values retain their exact explicit argument/arity contract.
        if (supplied != null)
        {
            IL.Emit(OpCodes.Ldloc, type);
            IL.Emit(OpCodes.Ldloc, supplied);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.Type, "MakeGenericType", Types.MakeArrayType(Types.Type)));
            IL.Emit(OpCodes.Stloc, type);
        }
        IL.Emit(OpCodes.Br, closed);
        IL.MarkLabel(guest);

        // Structural constructor compatibility does not identify generic parameter positions.
        // Only the same declaration can consume the annotation's supplied CLR arguments.
        var checkedDefinition = checkedArguments?.GenericDefinition
            ?? Ctx.TypeMap?.Get(ExpressionUnwrapper.Unwrap(expression.Callee));
        Type checkedOwner = checkedDefinition == null ? Types.Object : Ctx.TypeMapper.MapTypeInfoStrict(checkedDefinition);
        if (supplied != null && checkedOwner != Types.Object)
        {
            var canonical = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, type);
            IL.Emit(OpCodes.Ldtoken, checkedOwner);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Bne_Un, canonical);
            GenericTypeClosingEmitter.Emit(IL, Types, type, supplied);
            IL.Emit(OpCodes.Br, closed);
            IL.MarkLabel(canonical);
        }
        GenericTypeClosingEmitter.Emit(IL, Types, type);
        IL.MarkLabel(closed);
        IL.Emit(OpCodes.Ldloc, type);
        IL.Emit(OpCodes.Stloc, constructor);
        IL.MarkLabel(done);
    }

    protected void PublishRuntimeClassBinding(Stmt.Class declaration, string name, LocalBuilder definition)
    {
        bool shadowsOuter = Ctx.RuntimeClassLexicalShadows.Contains(declaration);
        if (!shadowsOuter && Ctx.CellBindingLocals.TryGetValue(name, out var cell))
        {
            IL.Emit(OpCodes.Ldloc, cell);
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Stfld, Types.StrongBoxOfObjectValueField);
        }
        else if (!shadowsOuter && Ctx.CapturedFunctionLocals?.Contains(name) == true
            && Ctx.FunctionDisplayClassFields?.TryGetValue(name, out var functionField) == true)
        {
            if (Ctx.FunctionDisplayClassLocal != null)
                IL.Emit(OpCodes.Ldloc, Ctx.FunctionDisplayClassLocal);
            else if (GetFunctionDCField() is { } stateField)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, stateField);
            }
            else if (Ctx.CurrentArrowFunctionDCField != null)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, Ctx.CurrentArrowFunctionDCField);
            }
            else return;
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Stfld, functionField);
        }
        else if (!shadowsOuter && Ctx.CapturedArrowLocals?.Contains(name) == true
            && Ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowField) == true
            && Ctx.ArrowScopeDisplayClassLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, Ctx.ArrowScopeDisplayClassLocal);
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Stfld, arrowField);
        }
        else if (!shadowsOuter && Ctx.IsModuleTopLevel && Ctx.CapturedTopLevelVars?.Contains(name) == true
            && Ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryField) == true)
        {
            if (Ctx.EntryPointDisplayClassLocal != null) IL.Emit(OpCodes.Ldloc, Ctx.EntryPointDisplayClassLocal);
            else if (Ctx.EntryPointDisplayClassStaticField != null) IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField);
            else return;
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Stfld, entryField);
        }

        if (Ctx.LexicalCaptureWriteBacks.TryGetValue(name, out var writeBacks))
        {
            int start = Ctx.RuntimeClassCaptureWriteBackStarts.GetValueOrDefault(declaration);
            foreach (var (displayClass, field) in writeBacks.Skip(start))
            {
                IL.Emit(OpCodes.Ldloc, displayClass);
                IL.Emit(OpCodes.Ldloc, definition);
                IL.Emit(OpCodes.Stfld, field);
            }
        }
    }

    protected bool TryEmitGuestSuperCall(Expr.Call call)
    {
        if (Ctx.CurrentClassBuilder == null || Ctx.Runtime == null) return false;
        Expr reference = UnwrapSuperCallReference(call.Callee);
        LocalBuilder receiver;
        if (reference is Expr.GetIndex { Object: Expr.Super { Method: null } } computed)
        {
            receiver = EmitComputedSuperGet(computed);
        }
        else
        {
            if (reference is not Expr.Super { Method: not null } super
                || (!Ctx.IsStaticSuperContext && super.Method.Lexeme == "constructor")) return false;

            // An unchanged, fixed-signature parent can retain its proven direct
            // dispatch. Every mutation-capable or variadic call resolves the live
            // property first, including calls from arrows and state machines.
            if (TryEmitSuperMethodCall(super.Method.Lexeme, call.Arguments))
                return true;

            receiver = SpillBoxed(new Expr.This(new Token(TokenType.THIS, "this", null, 0)));
            EmitGuestSuperValue(super.Method.Lexeme, receiver);
        }
        if (call.Optional)
        {
            var callable = _helpers.SpillStoreObject();
            var missing = IL.DefineLabel();
            var end = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, callable);
            IL.Emit(OpCodes.Brfalse, missing);
            IL.Emit(OpCodes.Ldloc, callable);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime.Sentinels.UndefinedType);
            IL.Emit(OpCodes.Brtrue, missing);
            IL.Emit(OpCodes.Ldloc, callable);
            EmitGuestMethodInvocation(receiver, call.Arguments);
            IL.Emit(OpCodes.Br, end);
            IL.MarkLabel(missing);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime.Sentinels.UndefinedInstance);
            IL.MarkLabel(end);
            SetStackUnknown();
            return true;
        }
        EmitGuestMethodInvocation(receiver, call.Arguments);
        return true;
    }

    private static Expr UnwrapSuperCallReference(Expr expression) => expression switch
    {
        Expr.Grouping grouping => UnwrapSuperCallReference(grouping.Expression),
        Expr.Instantiation instantiation => UnwrapSuperCallReference(instantiation.Expression),
        Expr.TypeAssertion assertion => UnwrapSuperCallReference(assertion.Expression),
        Expr.Satisfies satisfies => UnwrapSuperCallReference(satisfies.Expression),
        Expr.NonNullAssertion assertion => UnwrapSuperCallReference(assertion.Expression),
        _ => expression
    };

    protected bool CanEmitNominalSuperMethodCall(string superclass, string name,
        MethodBuilder method, IReadOnlyList<Expr> arguments)
    {
        if (Ctx.IsStaticSuperContext || Ctx.CurrentClassExpr != null
            || Ctx.HasGuestReceiver || Ctx.GuestThisVariableName != null
            || Ctx.RuntimeFeatures is not
                { UsesClassPrototypeMutation: false, UsesDynamicPropertyDescriptors: false, UsesObjectIntegrityMutation: false }
            || arguments.Any(argument => argument is Expr.Spread) || AnyContainsSuspension(arguments)
            || Ctx.MethodsCapturingArguments?.Contains(method) == true
            || Ctx.ClassRegistry?.HasInstancePrototypeBarrier(superclass, name, method) != false)
            return false;
        var parameters = method.GetParameters();
        // Defaults, rest markers and guest object slots require the function
        // value ABI. Exact primitive slots need no packing or omission padding.
        return arguments.Count == parameters.Length && parameters.All(parameter =>
            parameter.ParameterType == Types.Double || parameter.ParameterType == Types.Boolean
                || parameter.ParameterType == Types.String);
    }

    protected void EmitGuestSuperValue(string name, LocalBuilder receiver)
    {
        if (Ctx.CurrentClassExpr != null && TryEmitOwnedClassDefinition()) { }
        else
        {
            IL.Emit(OpCodes.Ldtoken, Ctx.CurrentClassBuilder!);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        }
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(Ctx.IsStaticSuperContext ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClassDefinitions.ReadSuper);
        SetStackUnknown();
        EmitUnchangedSuperMethodValue(name, receiver);
    }

    protected LocalBuilder EmitComputedSuperGet(Expr.GetIndex expression)
    {
        var receiver = SpillBoxed(new Expr.This(new Token(TokenType.THIS, "this", null, 0)));
        var key = SpillBoxed(expression.Index);
        IL.Emit(OpCodes.Ldloc, key);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Reflect.ToPropertyKey);
        IL.Emit(OpCodes.Stloc, key);
        // Key conversion finishes before the helper reads the live lexical base.
        if (Ctx.CurrentClassExpr != null && TryEmitOwnedClassDefinition()) { }
        else
        {
            IL.Emit(OpCodes.Ldtoken, Ctx.CurrentClassBuilder!);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        }
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Ldloc, key);
        IL.Emit(Ctx.IsStaticSuperContext ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Call, Ctx.Runtime.ClassDefinitions.ReadSuper);
        SetStackUnknown();
        if (expression.Index is Expr.Literal { Value: string name })
            EmitUnchangedSuperMethodValue(name, receiver);
        return receiver;
    }

    private void EmitUnchangedSuperMethodValue(string name, LocalBuilder receiver)
    {
        if (Ctx.IsStaticSuperContext
            || Ctx.CurrentClassBuilder is not { } owner || Ctx.ClassRegistry is not { } registry) return;
        string? superclass = Ctx.CurrentSuperclassName
            ?? registry.GetSuperclass(owner.FullName ?? Ctx.CurrentClassName!)
            ?? (Ctx.CurrentClassName is { } current ? registry.GetSuperclass(current) : null);
        if (superclass == null || Ctx.ResolveInstanceMethod(Ctx.ResolveClassName(superclass), name) is not { } parent)
            return;

        // Legacy nominal prototype methods wrap a virtual CLR MethodInfo. Keep
        // that unchanged method value nonvirtual, while live data, getters and
        // replacements retain the value returned by the prototype lookup.
        var value = _helpers.SpillStoreObject();
        var function = IL.DeclareLocal(Ctx.Runtime!.FunctionValues.Type);
        var unchanged = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime.FunctionValues.Type);
        IL.Emit(OpCodes.Stloc, function);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Brfalse, unchanged);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Callvirt, Ctx.Runtime.FunctionValues.GetMethodInfo);
        IL.Emit(OpCodes.Callvirt, Types.GetProperty(Types.MethodInfo, "Module")!.GetGetMethod()!);
        IL.Emit(OpCodes.Ldtoken, owner);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Callvirt, Types.GetProperty(Types.Type, "Module")!.GetGetMethod()!);
        IL.Emit(OpCodes.Call, Types.GetMethod(Types.Object, "Equals", Types.Object, Types.Object));
        IL.Emit(OpCodes.Brfalse, unchanged);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Callvirt, Ctx.Runtime.FunctionValues.GetMethodInfo);
        IL.Emit(OpCodes.Callvirt, Types.GetProperty(Types.MethodInfo, "MetadataToken")!.GetGetMethod()!);
        // Persisted assembly layout can remap MethodDef rows after emission.
        // Resolve both tokens at runtime rather than embedding a provisional
        // MethodBuilder token in the unchanged-value test.
        Types.EmitLoadMethodInfo(IL, parent);
        IL.Emit(OpCodes.Callvirt, Types.GetProperty(Types.MethodInfo, "MetadataToken")!.GetGetMethod()!);
        IL.Emit(OpCodes.Bne_Un, unchanged);
        var (_, bridge) = registry.GetOrCreateSuperCallBridge(owner, parent, Ctx.Runtime.FunctionAttributes);
        IL.Emit(OpCodes.Ldloc, receiver);
        Types.EmitLoadMethodInfo(IL, bridge);
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Callvirt, Ctx.Runtime.FunctionValues.LengthGetter);
        IL.Emit(OpCodes.Newobj, Ctx.Runtime.FunctionConstruction.CachedConstructor);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldloc, function);
        IL.Emit(OpCodes.Ldfld, Ctx.Runtime.FunctionValues.CapturesArgumentsField);
        IL.Emit(OpCodes.Stfld, Ctx.Runtime.FunctionValues.CapturesArgumentsField);
        IL.Emit(OpCodes.Stloc, value);
        IL.MarkLabel(unchanged);
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
    }

    protected bool TryEmitGuestThisSet(Expr.Set set)
    {
        if (set.Object is not Expr.This || (!Ctx.HasGuestReceiver && Ctx.GuestThisVariableName == null)) return false;
        var receiver = SpillBoxed(set.Object);
        var value = SpillBoxed(set.Value);
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Ldstr, set.Name.Lexeme); IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectWrite.PropertyStrict);
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
        return true;
    }

    protected bool TryEmitGuestThisCall(Expr.Call call)
    {
        if (call.Callee is not Expr.Get { Object: Expr.This, Optional: false } get || call.Optional
            || (!Ctx.HasGuestReceiver && Ctx.GuestThisVariableName == null)) return false;
        var receiver = SpillBoxed(get.Object);
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectRead.Property);
        EmitGuestMethodInvocation(receiver, call.Arguments);
        return true;
    }

    protected void EmitGuestMethodInvocation(LocalBuilder receiver, IReadOnlyList<Expr> argumentExpressions)
    {
        var callable = _helpers.SpillStoreObject();
        EmitArgsArrayWithSpread(argumentExpressions);
        var arguments = _helpers.SpillStoreObject();
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Ldloc, callable); IL.Emit(OpCodes.Ldloc, arguments);
        IL.Emit(OpCodes.Castclass, Types.ObjectArray); IL.Emit(OpCodes.Call, Ctx.Runtime!.Invocation.Method);
        SetStackUnknown();
    }

    protected bool TryEmitClassDefinitionSelf(Expr.Variable variable)
    {
        if (Ctx.ClosureAnalyzer?.TryGetClassSelfOwner(variable, out var selfOwner) == true &&
            Ctx.ClassExprBuilders?.TryGetValue(selfOwner, out var selfBuilder) == true)
        {
            // This occurrence resolves to the immutable inner binding, even if
            // a shared display class also stores an unrelated outer namesake.
            // The builder identifies a link; the value is its actual evaluation.
            bool currentOwner = ReferenceEquals(Ctx.CurrentClassExpr, selfOwner) ||
                ReferenceEquals(Ctx.CurrentClassBuilder, selfBuilder);
            if (!(currentOwner ? TryEmitOwnedClassDefinition()
                : TryEmitPrivateInOwnerDefinition(selfBuilder.FullName!))) return false;
            SetStackUnknown();
            return true;
        }
        string name = variable.Name.Lexeme;
        var expression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders?
            .FirstOrDefault(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
        if (expression?.Name?.Lexeme == name)
        {
            if (HasClassDefinitionCaptureShadow(name)) return false;
            if (!TryEmitOwnedClassDefinition()) return false;
        }
        else
        {
            // A named enclosing expression has no outer variable declaration to
            // capture. Its checked constructor binding identifies the lexical
            // self-name, whose evaluated definition travels in the owner chain.
            if (HasClassDefinitionCaptureShadow(name)
                || TryGetClassDefinitionCaptureSlot(name, out _)) return false;
            var constructor = Ctx.TypeMap?.Get(variable);
            if (constructor is not (SharpTS.TypeSystem.TypeInfo.Class or SharpTS.TypeSystem.TypeInfo.GenericClass))
                return false;
            string owner = Ctx.ResolveClassName(constructor, name);
            if (Ctx.EnclosingClassNames?.Contains(owner) != true
                || Ctx.ClassExprBuilders?.Any(entry => entry.Value.FullName == owner
                    && entry.Key.Name?.Lexeme == name) != true
                || !TryEmitPrivateInOwnerDefinition(owner)) return false;
        }
        SetStackUnknown();
        return true;
    }

    protected internal bool TryEmitOwnedClassDefinition()
    {
        if (Ctx.ClassDefinitionOwnerField is { } ownerField)
        {
            IL.Emit(OpCodes.Ldarg_0);
            if (GetThisField() is { } stateReceiver)
            {
                IL.Emit(OpCodes.Ldfld, stateReceiver);
                IL.Emit(OpCodes.Castclass, ownerField.DeclaringType!);
            }
            IL.Emit(OpCodes.Ldfld, ownerField);
        }
        else if (Ctx.ClassDefinitionVariableName is { } variable)
            EmitVariable(new Expr.Variable(new Token(TokenType.IDENTIFIER, variable, null, 0)));
        else if (Ctx.ClassDefinitionParameterIndex is { } parameter)
            IL.Emit(OpCodes.Ldarg, parameter);
        else
        {
            var expression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders?
                .FirstOrDefault(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
            if (!Ctx.IsInstanceMethod || expression == null ||
                Ctx.ClassExprDefinitionFields?.TryGetValue(expression, out var field) != true) return false;
            EmitThis();
            // Read the lexical owner's field: a derived instance can carry a
            // distinct child definition while executing this base method.
            var reference = EmitterTypeHelpers.SelfFieldReference(field!);
            if (field!.DeclaringType!.IsGenericTypeDefinition && Ctx.ClassExprFactories?.TryGetValue(expression, out var factory) == true)
                reference = EmitterTypeHelpers.ResolveField(factory.Template, field);
            IL.Emit(OpCodes.Castclass, reference.DeclaringType!);
            IL.Emit(OpCodes.Ldfld, reference);
        }
        IL.Emit(OpCodes.Castclass, Ctx.Runtime!.ClassDefinitions.Type);
        SetStackUnknown();
        return true;
    }

    protected bool TryEmitGuestThis()
    {
        if (Resolver.TryLoadThisBindingCell())
        {
            ConstructorReceiverEmitter.ReadCell(IL, Ctx);
            SetStackUnknown();
            return true;
        }
        if (Ctx.GuestThisVariableName is not { } name) return false;
        EmitVariable(new Expr.Variable(new Token(TokenType.IDENTIFIER, name, null, 0)));
        EnsureBoxed();
        return true;
    }

    protected bool TryEmitGuestThisGet(Expr.Get get)
    {
        if (get.Object is not Expr.This || get.Optional) return false;
        if (!TryEmitGuestThis())
        {
            if (!Ctx.HasGuestReceiver) return false;
            EmitThis();
            EnsureBoxed();
        }
        EmitThrowIfUndefinedReceiverOnStack(get.Name.Lexeme);
        IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ObjectRead.Property);
        SetStackUnknown();
        return true;
    }

    protected bool TryEmitClassDefinitionCapture(string name)
    {
        if (!TryGetClassDefinitionCaptureSlot(name, out var slot) || !TryEmitOwnedClassDefinition()) return false;
        IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.Captures);
        IL.Emit(OpCodes.Ldc_I4, slot);
        IL.Emit(OpCodes.Ldelem_Ref);
        IL.Emit(OpCodes.Call, Ctx.Runtime.ClassDefinitions.ReadCapture);
        Ctx.EmitLexicalTdzValueCheck(IL, name);
        SetStackUnknown();
        return true;
    }

    protected bool TryGetClassDefinitionCaptureSlot(string name, out int slot)
    {
        slot = -1;
        if (HasClassDefinitionCaptureShadow(name)) return false;
        var expression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders?
            .FirstOrDefault(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
        if (expression == null || Ctx.ClassExprCaptureSlots?.TryGetValue(expression, out var captures) != true
            || !captures!.TryGetValue(name, out slot)) return false;
        return true;
    }

    protected bool HasClassDefinitionCaptureShadow(string name)
    {
        if (Ctx.TryGetParameter(name, out _) || Ctx.Locals.GetLocal(name) != null
            || Ctx.CellBindingLocals.ContainsKey(name) || GetHoistedVariableField(name) != null
            || Ctx.CapturedFields?.ContainsKey(name) == true) return true;

        // Restoring an arrow's lexical class must not hide its live closure
        // storage. Shared captures can live behind a display-class reference
        // instead of a same-named field on the arrow itself.
        string functionName = Ctx.CurrentArrowFunctionDCFieldRenames?.GetValueOrDefault(name) ?? name;
        if (Ctx.CapturedFunctionLocals?.Contains(functionName) == true
            && Ctx.FunctionDisplayClassFields?.ContainsKey(functionName) == true
            && (Ctx.FunctionDisplayClassLocal != null || Ctx.CurrentArrowFunctionDCField != null)) return true;
        if (Ctx.CapturedArrowLocals?.Contains(name) == true
            && Ctx.ArrowScopeDisplayClassFields?.ContainsKey(name) == true
            && Ctx.ArrowScopeDisplayClassLocal != null) return true;
        if (Ctx.ParentArrowCapturedLocals?.Contains(name) == true
            && Ctx.ParentArrowScopeDisplayClassFields?.ContainsKey(name) == true
            && Ctx.CurrentArrowScopeDCField != null) return true;
        if (Ctx.ExtraArrowScopeBindings?.ContainsKey(name) == true) return true;
        return Ctx.CurrentArrowEntryPointDCField != null
            && Ctx.CapturedTopLevelVars?.Contains(name) == true
            && Ctx.EntryPointDisplayClassFields?.ContainsKey(name) == true;
    }

    protected bool TryEmitStoreClassDefinitionCapture(string name)
    {
        if (!TryGetClassDefinitionCaptureSlot(name, out var slot)) return false;
        var value = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, value);
        if (!TryEmitOwnedClassDefinition()) throw new InvalidOperationException("Capture has no class definition owner.");
        IL.Emit(OpCodes.Ldc_I4, slot);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClassDefinitions.WriteCapture);
        return true;
    }

    protected bool TryEmitClassDefinitionEnvironment(Type environment)
    {
        if (!TryEmitOwnedClassDefinition()) return false;
        IL.Emit(OpCodes.Ldtoken, environment);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClassDefinitions.FindEnvironment);
        IL.Emit(OpCodes.Castclass, environment);
        return true;
    }

    protected void EmitGuestClassDefinition(Expr.ClassExpr expression)
    {
        if (Ctx.ClassExprFactories?.TryGetValue(expression, out var factory) != true)
            throw new InvalidOperationException("Class expression factory has not been declared.");
        if (expression.SuperclassExpr is { } heritage)
        {
            EmitClassHeritageValue(heritage, expression.Name?.Lexeme);
            EnsureBoxed();
            if ((factory!.Template.BaseType == null || factory.Template.BaseType == Types.Object)
                && heritage is not Expr.ClassExpr { Methods.Count: 0, Fields.Count: 0 })
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ClassDefinitions.ValidateParent);
        }
        else IL.Emit(OpCodes.Ldnull);
        var parent = _helpers.SpillStoreObject();
        IL.Emit(OpCodes.Ldtoken, factory!.Template);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClassInitialization.RunDefinition);
        var keys = IL.DeclareLocal(Types.ObjectArray);
        EmitDefinitionKeyArray(factory.Keys);
        IL.Emit(OpCodes.Stloc, keys);
        var captures = IL.DeclareLocal(Types.ObjectArray);
        EmitDefinitionCaptureArray(expression, Ctx.ClassExprCaptureSlots!.GetValueOrDefault(expression)?.Keys.ToArray() ?? []);
        IL.Emit(OpCodes.Stloc, captures);
        IL.Emit(OpCodes.Ldtoken, factory.Template);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, factory.Method);
        IL.Emit(OpCodes.Newobj, Types.GetConstructor(typeof(Func<object[], object, object, object>), Types.Object, Types.IntPtr));
        IL.Emit(OpCodes.Ldstr, factory.Name);
        IL.Emit(OpCodes.Ldc_R8, (double)factory.Length);
        IL.Emit(OpCodes.Ldloc, parent);
        IL.Emit(OpCodes.Ldloc, keys);
        IL.Emit(OpCodes.Ldloc, captures);
        IL.Emit(OpCodes.Call, Ctx.Runtime.ClassDefinitions.Create);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Call, factory.Initializer);
        SetStackUnknown();
    }

    protected void EmitDefinitionComputedKeys(MethodBuilder method, IReadOnlyList<Expr> keys)
    {
        EmitDefinitionKeyArray(keys);
        IL.Emit(OpCodes.Call, method);
    }

    private void EmitDefinitionCaptureArray(Expr.ClassExpr expression, IReadOnlyList<string> names)
    {
        var values = new List<LocalBuilder>(names.Count);
        var enclosingExpression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders?
            .FirstOrDefault(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
        foreach (var name in names)
        {
            if (name == CompilationContext.PrivateOwnerCaptureName)
            {
                if (!TryEmitOwnedClassDefinition())
                    throw new InvalidOperationException("Nested private names require their enclosing class evaluation.");
                values.Add(_helpers.SpillStoreObject());
                continue;
            }
            // A nested class member can already own this exact lexical cell
            // through its evaluated definition. Forward that wrapper unchanged.
            if (enclosingExpression != null
                && Ctx.ClosureAnalyzer?.TryGetLoopBodyCellCapture(expression, name, out var declaration) == true
                && Ctx.ClosureAnalyzer.TryGetLoopBodyCellCapture(enclosingExpression, name, out var enclosingDeclaration)
                && ReferenceEquals(declaration, enclosingDeclaration)
                && TryGetClassDefinitionCaptureSlot(name, out var enclosingSlot) && TryEmitOwnedClassDefinition())
            {
                IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.Captures);
                IL.Emit(OpCodes.Ldc_I4, enclosingSlot);
                IL.Emit(OpCodes.Ldelem_Ref);
                values.Add(_helpers.SpillStoreObject());
                continue;
            }
            FieldInfo? field = null;
            if (LexicalEnvironmentCellAccess.TryEmitCaptureReference(IL, Ctx, expression, name))
                field = LexicalCellStorage.ForCapture(Ctx, expression, name).ValueField;
            else if (Ctx.CellBindingLocals.TryGetValue(name, out var cell))
            {
                IL.Emit(OpCodes.Ldloc, cell);
                field = Types.StrongBoxOfObjectValueField;
            }
            else if (Ctx.FunctionDisplayClassFields?.TryGetValue(name, out var functionField) == true
                && Ctx.FunctionDisplayClassLocal != null)
            {
                IL.Emit(OpCodes.Ldloc, Ctx.FunctionDisplayClassLocal);
                field = functionField;
            }
            else if (Ctx.FunctionDisplayClassFields?.TryGetValue(name, out functionField) == true
                && GetFunctionDCField() is { } stateField)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, stateField);
                field = functionField;
            }
            else if (Ctx.FunctionDisplayClassFields?.TryGetValue(name, out functionField) == true
                && Ctx.CurrentArrowFunctionDCField != null)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, Ctx.CurrentArrowFunctionDCField);
                field = functionField;
            }
            else if (Ctx.CapturedFields?.TryGetValue(name, out var capturedField) == true)
            {
                IL.Emit(OpCodes.Ldarg_0);
                field = capturedField;
            }
            else if (Ctx.EntryPointDisplayClassFields?.TryGetValue(name, out var entryField) == true
                && (Ctx.EntryPointDisplayClassLocal != null || Ctx.EntryPointDisplayClassStaticField != null))
            {
                if (Ctx.EntryPointDisplayClassLocal != null) IL.Emit(OpCodes.Ldloc, Ctx.EntryPointDisplayClassLocal);
                else IL.Emit(OpCodes.Ldsfld, Ctx.EntryPointDisplayClassStaticField!);
                field = entryField;
            }
            else if (Ctx.TopLevelStaticVars?.TryGetValue(name, out var staticField) == true)
            {
                IL.Emit(OpCodes.Ldnull);
                field = staticField;
            }
            if (field != null)
            {
                IL.Emit(OpCodes.Ldtoken, field);
                IL.Emit(OpCodes.Ldtoken, field.DeclaringType!);
                IL.Emit(OpCodes.Call, Types.GetMethod(Types.FieldInfo, "GetFieldFromHandle",
                    Types.Resolve("System.RuntimeFieldHandle"), Types.Resolve("System.RuntimeTypeHandle")));
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.ClassDefinitions.CaptureConstructor);
            }
            else if (TryGetClassDefinitionCaptureSlot(name, out var slot) && TryEmitOwnedClassDefinition())
            {
                IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.Captures);
                IL.Emit(OpCodes.Ldc_I4, slot);
                IL.Emit(OpCodes.Ldelem_Ref);
            }
            else
            {
                EmitVariable(new Expr.Variable(new Token(TokenType.IDENTIFIER, name, null, 0)));
                EnsureBoxed();
            }
            values.Add(_helpers.SpillStoreObject());
        }
        IL.Emit(OpCodes.Ldc_I4, values.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < values.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, values[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }
    }

    protected void EmitDefinitionKeyArray(IReadOnlyList<Expr> keys, bool coerceKeys = true)
    {
        var values = new List<LocalBuilder>(keys.Count);
        foreach (var key in keys)
        {
            EmitExpression(key);
            EnsureBoxed();
            if (coerceKeys)
                IL.Emit(OpCodes.Call, Ctx.Runtime!.Reflect.ToPropertyKey);
            values.Add(_helpers.SpillStoreObject());
        }
        IL.Emit(OpCodes.Ldc_I4, values.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < values.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, values[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }
    }
}
