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
        if ((Ctx.GuestThisVariableName == null && assignedValue == null) || Ctx.CurrentClassName is not { } current) return false;
        string name = token.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner);
        if (declaration?.StaticFields.ContainsKey(name) != true) return false;
        var receiver = SpillGenericPrivateOperand(receiverExpression);
        var value = assignedValue == null ? null : SpillGenericPrivateOperand(assignedValue);
        EmitOrdinaryStaticPrivateBrandCheck(owner, receiver, name);
        if (!Ctx.ClassRegistry!.TryGetCallableStaticPrivateField(owner, name, out var field))
            throw new InvalidOperationException("Static private field has no callable owner.");
        if (value == null) IL.Emit(OpCodes.Ldsfld, field!);
        else
        {
            // A successful receiver brand check does not install a later field.
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
            IL.Emit(OpCodes.Ldloc, value); IL.Emit(OpCodes.Stsfld, field); IL.Emit(OpCodes.Ldloc, value);
        }
        SetStackUnknown();
        return true;
    }

    private void EmitOrdinaryStaticPrivateBrandCheck(string owner, LocalBuilder receiver, string name)
    {
        Ctx.ClassRegistry!.TryGetClass(owner, out var builder);
        var invalid = IL.DefineLabel();
        var valid = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, receiver);
        if (builder!.IsGenericTypeDefinition)
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
        if (Ctx.CurrentClassName is not { } owner || Ctx.CurrentClassExpr is not { } expression) return false;
        string name = token.Lexeme.TrimStart('#');
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner);
        if (declaration == null) return false;
        bool isStatic = declaration.StaticFields.ContainsKey(name);
        if (!isStatic && !declaration.FieldNames.Contains(name)) return false;
        var receiver = SpillGenericPrivateOperand(receiverExpression);
        var value = assignedValue == null ? null : SpillGenericPrivateOperand(assignedValue);
        var definition = EmitDefinitionPrivateBrandCheck(receiver, name, isStatic);
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
        else if (declaration.InstanceBridge is { } bridge)
            IL.Emit(OpCodes.Ldloc, EmitGenericPrivateBrandCheck(bridge, receiver, name));
        else
        {
            var slots = IL.DeclareLocal(Types.DictionaryStringObject);
            var storage = declaration.Storage!;
            var template = Ctx.ClassExprFactories![expression].Template;
            IL.Emit(OpCodes.Ldsfld, template.IsGenericType ? EmitterTypeHelpers.ResolveField(template, storage) : storage);
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Ldloca, slots);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(storage.FieldType, "TryGetValue", Types.Object, Types.DictionaryStringObject.MakeByRefType()));
            var valid = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, valid);
            GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} before initialization");
            IL.MarkLabel(valid);
            IL.Emit(OpCodes.Ldloc, slots);
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

    protected bool TryEmitDefinitionPrivateCall(Expr.CallPrivate call)
    {
        if (Ctx.CurrentClassName is not { } current) return false;
        string name = call.Name.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateMethodOwner(current, name);
        var value = Ctx.ClassRegistry?.GetPrivateMethodValue(owner, name);
        if (value?.Definition == null) return false;
        var receiver = SpillGenericPrivateOperand(call.Object);
        EmitPrivateMethodValue(value, owner, name, receiver);
        EmitGuestMethodInvocation(receiver, call.Arguments);
        return true;
    }

    private void EmitPrivateMethodValue(PrivateMethodValue value, string owner, string name, LocalBuilder receiver)
    {
        var definition = value.Definition == null ? null : EmitDefinitionPrivateBrandCheck(receiver, name, value.Source.IsStatic);
        if (value.Source.IsStatic && definition == null)
        {
            EmitOrdinaryStaticPrivateBrandCheck(owner, receiver, name);
        }
        else if (!value.Source.IsStatic && Ctx.ClassRegistry!.GetPrivateInstanceBridge(owner) is { } bridge)
            EmitGenericPrivateBrandCheck(bridge, receiver, name);
        else if (!value.Source.IsStatic)
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

    private LocalBuilder EmitDefinitionPrivateBrandCheck(LocalBuilder receiver, string name, bool isStatic)
    {
        if (!TryEmitOwnedClassDefinition()) throw new InvalidOperationException("Private access requires its lexical class definition.");
        var definition = IL.DeclareLocal(Ctx.Runtime!.ClassDefinitions.Type);
        IL.Emit(OpCodes.Stloc, definition);
        var invalid = IL.DefineLabel();
        var valid = IL.DefineLabel();
        if (isStatic) IL.Emit(OpCodes.Ldloc, receiver);
        else
        {
            var expression = Ctx.CurrentClassExpr ?? Ctx.ClassExprBuilders!.First(entry => ReferenceEquals(entry.Value, Ctx.CurrentClassBuilder)).Key;
            var factory = Ctx.ClassExprFactories![expression];
            var field = Ctx.ClassExprDefinitionFields![expression];
            var reference = factory.Template.IsGenericType ? EmitterTypeHelpers.ResolveField(factory.Template, field) : field;
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Isinst, reference.DeclaringType!);
            IL.Emit(OpCodes.Brfalse, invalid);
            IL.Emit(OpCodes.Ldloc, receiver);
            IL.Emit(OpCodes.Castclass, reference.DeclaringType!);
            IL.Emit(OpCodes.Ldfld, reference);
        }
        IL.Emit(OpCodes.Ldloc, definition);
        IL.Emit(OpCodes.Ceq);
        IL.Emit(OpCodes.Brtrue, valid);
        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} from an object whose class did not declare it");
        IL.MarkLabel(valid);
        return definition;
    }
}
