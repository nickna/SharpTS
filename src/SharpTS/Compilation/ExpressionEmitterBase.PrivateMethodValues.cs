using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryEmitGuestInstancePrivateField(Expr receiverExpression, Token token, Expr? assignedValue = null)
    {
        if ((!Ctx.HasGuestReceiver && Ctx.GuestThisVariableName == null) || Ctx.CurrentClassName is not { } current) return false;
        string name = token.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner);
        if (declaration?.FieldNames.Contains(name) != true) return false;
        var receiver = SpillGenericPrivateOperand(receiverExpression);
        var value = assignedValue == null ? null : SpillGenericPrivateOperand(assignedValue);
        var slots = declaration.InstanceBridge is { } bridge ? EmitGenericPrivateBrandCheck(bridge, receiver, name)
            : EmitOrdinaryPrivateBrandCheck(owner, receiver, name);
        IL.Emit(OpCodes.Ldloc, slots); IL.Emit(OpCodes.Ldstr, name);
        if (value == null) IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "get_Item", Types.String));
        else
        {
            IL.Emit(OpCodes.Ldloc, value); IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem); IL.Emit(OpCodes.Ldloc, value);
        }
        SetStackUnknown();
        return true;
    }

    private LocalBuilder EmitOrdinaryPrivateBrandCheck(string owner, LocalBuilder receiver, string name)
    {
        var table = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>>);
        var slots = IL.DeclareLocal(Types.DictionaryStringObject);
        var invalid = IL.DefineLabel();
        var valid = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Brfalse, invalid);
        IL.Emit(OpCodes.Ldsfld, Ctx.ClassRegistry!.GetPrivateFieldStorage(owner)!);
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Ldloca, slots);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(table, "TryGetValue", Types.Object, Types.DictionaryStringObject.MakeByRefType()));
        IL.Emit(OpCodes.Brtrue, valid);
        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} from an object whose class did not declare it");
        IL.MarkLabel(valid);
        return slots;
    }

    protected bool TryEmitGuestStaticPrivateField(Expr receiverExpression, Token token, Expr? assignedValue = null)
    {
        // Dynamic reads must evaluate and brand the receiver before loading the lexical field.
        bool dynamicRead = Ctx.TypeMap?.Get(receiverExpression) is SharpTS.TypeSystem.TypeInfo.Any;
        // An arrow's CLR Invoke can be an instance method while this retains its lexical static home.
        bool lexicalStaticRead = receiverExpression is Expr.This && Ctx.IsStaticSuperContext;
        bool newlyAdmittedStaticRead = lexicalStaticRead && Ctx.GuestThisVariableName == null
            && assignedValue == null && !dynamicRead;
        if ((Ctx.GuestThisVariableName == null && assignedValue == null && !dynamicRead && !lexicalStaticRead)
            || Ctx.CurrentClassName is not { } current) return false;
        string name = token.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner);
        if (declaration?.StaticFields.ContainsKey(name) != true) return false;
        var receiver = SpillGenericPrivateOperand(receiverExpression);
        var value = assignedValue == null ? null : SpillGenericPrivateOperand(assignedValue);
        EmitOrdinaryStaticPrivateBrandCheck(owner, receiver, name);
        if (!Ctx.ClassRegistry!.TryGetCallableStaticPrivateField(owner, name, out var field))
            throw new InvalidOperationException("Static private field has no callable owner.");
        if (value != null || dynamicRead || newlyAdmittedStaticRead)
        {
            // A successful receiver brand check does not install a later field.
            // Dynamic and newly admitted static-this reads must reject uninitialized fields.
            var presence = field!.DeclaringType!.IsConstructedGenericType
                ? EmitterTypeHelpers.ResolveField(field.DeclaringType, declaration.StaticPresence!)
                : declaration.StaticPresence!;
            var installed = IL.DefineLabel();
            IL.Emit(OpCodes.Ldsfld, presence);
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(typeof(HashSet<string>), "Contains", Types.String));
            IL.Emit(OpCodes.Brtrue, installed);
            GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} before initialization");
            IL.MarkLabel(installed);
        }
        if (value == null) IL.Emit(OpCodes.Ldsfld, field!);
        else
        {
            IL.Emit(OpCodes.Ldloc, value); IL.Emit(OpCodes.Stsfld, field!); IL.Emit(OpCodes.Ldloc, value);
        }
        SetStackUnknown();
        return true;
    }

    private void EmitOrdinaryStaticPrivateBrandCheck(string owner, LocalBuilder receiver, string name)
    {
        var declaration = Ctx.ClassRegistry!.GetPrivateElements(owner)
            ?? throw new InvalidOperationException("Static private member has no declaration owner.");
        var builder = declaration.StaticMethods.TryGetValue(name, out var method)
            ? method.DeclaringType!
            : declaration.StaticFields.TryGetValue(name, out var field)
                ? field.DeclaringType!
                : throw new InvalidOperationException("Static private member has no emitted owner.");
        var invalid = IL.DefineLabel();
        var valid = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, receiver);
        if (builder.IsGenericTypeDefinition)
        {
            var constructor = IL.DeclareLocal(Types.Type);
            IL.Emit(OpCodes.Isinst, Types.Type);
            IL.Emit(OpCodes.Stloc, constructor);
            IL.Emit(OpCodes.Ldloc, constructor); IL.Emit(OpCodes.Brfalse, invalid);
            IL.Emit(OpCodes.Ldloc, constructor);
            IL.Emit(OpCodes.Callvirt, Types.GetProperty(Types.Type, "IsGenericType").GetGetMethod()!);
            IL.Emit(OpCodes.Brfalse, invalid);
            IL.Emit(OpCodes.Ldloc, constructor);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.Type, "GetGenericTypeDefinition"));
        }
        IL.Emit(OpCodes.Ldtoken, builder);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Ceq); IL.Emit(OpCodes.Brtrue, valid);
        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} from an object whose class did not declare it");
        IL.MarkLabel(valid);
    }

    protected bool TryEmitDefinitionPrivateField(Expr receiverExpression, Token token, Expr? assignedValue = null)
    {
        if (Ctx.CurrentClassName is not { } current || Ctx.CurrentClassExpr == null) return false;
        string name = token.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        var expression = Ctx.ClassExprBuilders?.FirstOrDefault(entry => entry.Value.FullName == owner).Key;
        if (expression == null) return false;
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner);
        if (declaration == null) return false;
        bool isStatic = declaration.StaticFields.ContainsKey(name);
        if (!isStatic && !declaration.FieldNames.Contains(name)) return false;
        var receiver = SpillGenericPrivateOperand(receiverExpression);
        var value = assignedValue == null ? null : SpillGenericPrivateOperand(assignedValue);
        var definition = EmitDefinitionPrivateBrandCheck(receiver, name, isStatic, owner);
        if (isStatic)
        {
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.PrivateMembers);
            if (value != null)
            {
                var slots = IL.DeclareLocal(Types.DictionaryStringObject);
                var installed = IL.DefineLabel();
                IL.Emit(OpCodes.Stloc, slots);
                IL.Emit(OpCodes.Ldloc, slots);
                IL.Emit(OpCodes.Ldstr, $"field:{name}");
                IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "ContainsKey", Types.String));
                IL.Emit(OpCodes.Brtrue, installed);
                GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} before initialization");
                IL.MarkLabel(installed);
                IL.Emit(OpCodes.Ldloc, slots);
            }
        }
        else
        {
            IL.Emit(OpCodes.Ldloc, EmitDefinitionPrivateSlots(definition, receiver, name));
        }
        IL.Emit(OpCodes.Ldstr, isStatic ? $"field:{name}" : name);
        if (value == null) IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "get_Item", Types.String));
        else
        {
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);
            IL.Emit(OpCodes.Ldloc, value);
        }
        SetStackUnknown();
        return true;
    }

    protected bool TryEmitPrivateMethodValue(Expr.GetPrivate get)
    {
        if (Ctx.CurrentClassName is not { } current) return false;
        string name = get.Name.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateMethodOwner(current, name);
        var value = Ctx.ClassRegistry?.GetPrivateMethodValue(owner, name);
        if (value is null) return false;
        var receiver = SpillGenericPrivateOperand(get.Object);
        EmitPrivateMethodValue(value, owner, name, receiver);
        return true;
    }

    protected bool TryEmitBrandedPrivateCall(Expr.CallPrivate call)
    {
        if (Ctx.CurrentClassName is not { } current) return false;
        string name = call.Name.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateMethodOwner(current, name);
        var value = Ctx.ClassRegistry?.GetPrivateMethodValue(owner, name);
        if (value is null) return false;
        bool requiresArgumentPacking = call.Arguments.Any(argument => argument is Expr.Spread)
            || value.Source.Parameters.Any(parameter => parameter.IsRest);
        if (!requiresArgumentPacking && !Ctx.HasGuestReceiver
                && !Ctx.TypeMapper.IsDynamicClassInstance(Ctx.TypeMap?.GetClassType(owner))
                && value.Definition == null && !value.Source.IsStatic
                && !Parsing.Visitors.ArgumentsRefScanner.Contains(value.Source.Parameters, value.Source.Body,
                    treatEvalReferenceAsUse: true)) return false;
        var receiver = SpillGenericPrivateOperand(call.Object);
        EmitPrivateMethodValue(value, owner, name, receiver);
        EmitGuestMethodInvocation(receiver, call.Arguments);
        return true;
    }

    protected bool TryEmitPrivateFieldCall(Expr.CallPrivate call)
    {
        if (Ctx.CurrentClassName is not { } current) return false;
        string name = call.Name.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner);
        bool isStatic = declaration?.StaticFields.ContainsKey(name) == true;
        if (declaration is null || (!isStatic && !declaration.FieldNames.Contains(name))) return false;

        var receiver = SpillGenericPrivateOperand(call.Object);
        var expression = Ctx.ClassExprBuilders?.FirstOrDefault(entry => entry.Value.FullName == owner).Key;
        var definition = expression == null ? null : EmitDefinitionPrivateBrandCheck(receiver, name, isStatic, owner);
        LocalBuilder slots;
        string key = name;
        if (isStatic && definition != null)
        {
            slots = IL.DeclareLocal(Types.DictionaryStringObject);
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.PrivateMembers);
            IL.Emit(OpCodes.Stloc, slots);
            key = $"field:{name}";
        }
        else if (isStatic)
        {
            EmitOrdinaryStaticPrivateBrandCheck(owner, receiver, name);
            if (!Ctx.ClassRegistry!.TryGetCallableStaticPrivateField(owner, name, out var field))
                throw new InvalidOperationException("Static private field has no callable owner.");
            var presence = field!.DeclaringType!.IsConstructedGenericType
                ? EmitterTypeHelpers.ResolveField(field.DeclaringType, declaration.StaticPresence!)
                : declaration.StaticPresence!;
            var installed = IL.DefineLabel();
            IL.Emit(OpCodes.Ldsfld, presence);
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(typeof(HashSet<string>), "Contains", Types.String));
            IL.Emit(OpCodes.Brtrue, installed);
            GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} before initialization");
            IL.MarkLabel(installed);
            IL.Emit(OpCodes.Ldsfld, field);
            SetStackUnknown();
            EmitGuestMethodInvocation(receiver, call.Arguments);
            return true;
        }
        else if (definition != null)
            slots = EmitDefinitionPrivateSlots(definition, receiver, name);
        else if (declaration.InstanceBridge is { } bridge)
            slots = EmitGenericPrivateBrandCheck(bridge, receiver, name);
        else
            slots = EmitOrdinaryPrivateBrandCheck(owner, receiver, name);

        var present = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, slots);
        IL.Emit(OpCodes.Ldstr, key);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "ContainsKey", Types.String));
        IL.Emit(OpCodes.Brtrue, present);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} before initialization");
        IL.MarkLabel(present);
        IL.Emit(OpCodes.Ldloc, slots);
        IL.Emit(OpCodes.Ldstr, key);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "get_Item", Types.String));
        SetStackUnknown();
        EmitGuestMethodInvocation(receiver, call.Arguments);
        return true;
    }

    private void EmitPrivateMethodValue(PrivateMethodValue value, string owner, string name, LocalBuilder receiver)
    {
        var definition = value.Definition == null ? null : EmitDefinitionPrivateBrandCheck(receiver, name, value.Source.IsStatic, owner);
        if (value.Source.IsStatic && definition == null)
        {
            EmitOrdinaryStaticPrivateBrandCheck(owner, receiver, name);
        }
        else if (definition == null && !value.Source.IsStatic && Ctx.ClassRegistry!.GetPrivateInstanceBridge(owner) is { } bridge)
            EmitGenericPrivateBrandCheck(bridge, receiver, name);
        else if (definition == null && !value.Source.IsStatic)
            EmitOrdinaryPrivateBrandCheck(owner, receiver, name);
        if (definition == null) IL.Emit(OpCodes.Ldsfld, value.Cache!);
        else
        {
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.PrivateMembers);
            IL.Emit(OpCodes.Ldstr, $"method:{name}");
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "get_Item", Types.String));
        }
        SetStackUnknown();
    }

    private LocalBuilder EmitDefinitionPrivateBrandCheck(LocalBuilder receiver, string name, bool isStatic, string? owner = null)
    {
        owner ??= Ctx.CurrentClassName!;
        if (!TryEmitPrivateInOwnerDefinition(owner)) throw new InvalidOperationException("Private access requires its lexical class definition.");
        var definition = IL.DeclareLocal(Ctx.Runtime!.ClassDefinitions.Type);
        IL.Emit(OpCodes.Stloc, definition);
        var invalid = IL.DefineLabel();
        var valid = IL.DefineLabel();
        if (isStatic)
        {
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Ldloc, definition);
            IL.Emit(OpCodes.Ceq);
            IL.Emit(OpCodes.Brtrue, valid);
        }
        else
        {
            EmitDefinitionPrivateSlots(definition, receiver, name);
            IL.Emit(OpCodes.Br, valid);
        }
        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} from an object whose class did not declare it");
        IL.MarkLabel(valid);
        return definition;
    }

    private LocalBuilder EmitDefinitionPrivateSlots(LocalBuilder definition, LocalBuilder receiver, string name)
    {
        var table = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>>);
        var slots = IL.DeclareLocal(Types.DictionaryStringObject);
        var valid = IL.DefineLabel();
        var invalid = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Brfalse, invalid);
        EmitDefinitionPrivateTable(definition);
        IL.Emit(OpCodes.Ldloc, receiver); IL.Emit(OpCodes.Ldloca, slots);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(table, "TryGetValue", Types.Object, Types.DictionaryStringObject.MakeByRefType()));
        IL.Emit(OpCodes.Brtrue, valid);
        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} from an object whose class did not declare it");
        IL.MarkLabel(valid);
        return slots;
    }

    private void EmitDefinitionPrivateTable(LocalBuilder definition)
    {
        IL.Emit(OpCodes.Ldloc, definition);
        IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.PrivateMembers);
        IL.Emit(OpCodes.Ldstr, "'instanceStorage");
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "get_Item", Types.String));
        IL.Emit(OpCodes.Castclass, typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>>));
    }
}
