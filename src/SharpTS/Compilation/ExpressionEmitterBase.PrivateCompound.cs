using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    private readonly record struct PrivateFieldReference(FieldInfo? StaticField, LocalBuilder? Storage, string Key);

    protected void EmitCompoundSetPrivate(Expr.CompoundSetPrivate expression)
    {
        var receiver = SpillBoxed(expression.Object);
        var reference = ResolvePrivateFieldReference(expression.Name, receiver);
        EmitPrivateFieldReferenceRead(reference);
        var current = _helpers.SpillStoreObject();
        var right = SpillBoxed(expression.Value);
        IL.Emit(OpCodes.Ldloc, current);
        IL.Emit(OpCodes.Ldloc, right);
        EmitCompoundOperation(expression.Operator.Type);
        var result = _helpers.SpillStoreObject();
        EmitPrivateFieldReferenceWrite(reference, result);
        IL.Emit(OpCodes.Ldloc, result);
        SetStackUnknown();
    }

    protected void EmitPrivateIncrement(Expr.GetPrivate field, TokenType operation, bool isPrefix)
    {
        var receiver = SpillBoxed(field.Object);
        var reference = ResolvePrivateFieldReference(field.Name, receiver);
        EmitPrivateFieldReferenceRead(reference);
        // ToNumeric may invoke guest code. Retain the original field reference
        // across conversion and return the converted old value for postfix.
        IL.Emit(OpCodes.Call, Ctx.Runtime!.NumericCoercion.ToNumeric);
        var oldValue = _helpers.SpillStoreObject();
        IL.Emit(OpCodes.Ldloc, oldValue);
        IL.Emit(operation == TokenType.PLUS_PLUS ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Call, Ctx.Runtime.Operators.UpdateNumeric);
        var newValue = _helpers.SpillStoreObject();
        EmitPrivateFieldReferenceWrite(reference, newValue);
        IL.Emit(OpCodes.Ldloc, isPrefix ? newValue : oldValue);
        SetStackUnknown();
    }

    private PrivateFieldReference ResolvePrivateFieldReference(Token fieldName, LocalBuilder receiver)
    {
        string name = fieldName.Lexeme.TrimStart('#');
        string owner = Ctx.CurrentClassName is { } current
            ? Ctx.ResolvePrivateFieldOwner(current, name)
            : throw new InvalidOperationException("Private compound assignment requires a lexical class owner.");
        var declaration = Ctx.ClassRegistry?.GetPrivateElements(owner)
            ?? throw new InvalidOperationException("Private compound assignment has no declaration metadata.");
        // The lexical declaration supplies the field's static/instance facet;
        // the original receiver is evaluated once and its actual value branded.
        bool isStatic = declaration.StaticFields.ContainsKey(name);
        if (!isStatic && !declaration.FieldNames.Contains(name))
            throw new InvalidOperationException("Private compound assignment requires a writable field.");

        var definitionExpression = Ctx.ClassExprBuilders?.FirstOrDefault(entry => entry.Value.FullName == owner).Key;
        LocalBuilder? definition = definitionExpression == null
            ? null : EmitDefinitionPrivateBrandCheck(receiver, name, isStatic, owner);
        if (isStatic && definition == null)
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
            return new(field, null, name);
        }

        string key = isStatic ? $"field:{name}" : name;
        if (isStatic)
        {
            IL.Emit(OpCodes.Ldloc, definition!);
            IL.Emit(OpCodes.Ldfld, Ctx.Runtime!.ClassDefinitions.PrivateMembers);
        }
        else
        {
            var slots = definition != null ? EmitDefinitionPrivateSlots(definition, receiver, name)
                : declaration.InstanceBridge is { } bridge
                ? EmitGenericPrivateBrandCheck(bridge, receiver, name)
                : EmitOrdinaryPrivateBrandCheck(owner, receiver, name);
            IL.Emit(OpCodes.Ldloc, slots);
        }
        // A suspended RHS must retain this exact storage object, not a plain
        // dictionary local that is reinitialized on MoveNext reentry.
        var storage = _helpers.SpillStoreObject();
        var present = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, storage);
        IL.Emit(OpCodes.Castclass, Types.DictionaryStringObject);
        IL.Emit(OpCodes.Ldstr, key);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "ContainsKey", Types.String));
        IL.Emit(OpCodes.Brtrue, present);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} before initialization");
        IL.MarkLabel(present);
        return new(null, storage, key);
    }

    private void EmitPrivateFieldReferenceRead(PrivateFieldReference reference)
    {
        if (reference.StaticField is { } field)
            IL.Emit(OpCodes.Ldsfld, field);
        else
        {
            IL.Emit(OpCodes.Ldloc, reference.Storage!);
            IL.Emit(OpCodes.Castclass, Types.DictionaryStringObject);
            IL.Emit(OpCodes.Ldstr, reference.Key);
            IL.Emit(OpCodes.Callvirt, Types.GetMethod(Types.DictionaryStringObject, "get_Item", Types.String));
        }
        SetStackUnknown();
    }

    private void EmitPrivateFieldReferenceWrite(PrivateFieldReference reference, LocalBuilder value)
    {
        if (reference.StaticField is { } field)
        {
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Stsfld, field);
        }
        else
        {
            IL.Emit(OpCodes.Ldloc, reference.Storage!);
            IL.Emit(OpCodes.Castclass, Types.DictionaryStringObject);
            IL.Emit(OpCodes.Ldstr, reference.Key);
            IL.Emit(OpCodes.Ldloc, value);
            IL.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);
        }
    }
}
