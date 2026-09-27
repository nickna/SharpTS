using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryEmitGenericPrivateGet(Expr.GetPrivate get)
    {
        string name = get.Name.Lexeme.TrimStart('#');
        var bridge = GetGenericPrivateFieldBridge(name);
        if (bridge is null)
            return false;
        var receiver = SpillGenericPrivateOperand(get.Object);
        var slots = EmitGenericPrivateBrandCheck(bridge, receiver, name);
        IL.Emit(OpCodes.Ldloc, slots);
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(typeof(Dictionary<string, object?>), "get_Item", typeof(string)));
        SetStackUnknown();
        return true;
    }

    protected bool TryEmitGenericPrivateSet(Expr.SetPrivate set, Func<Expr, LocalBuilder>? spill = null)
    {
        string name = set.Name.Lexeme.TrimStart('#');
        var bridge = GetGenericPrivateFieldBridge(name);
        if (bridge is null)
            return false;
        spill ??= SpillGenericPrivateOperand;
        var receiver = spill(set.Object);
        var value = spill(set.Value);
        // PutValue checks the brand after evaluating the RHS.
        var slots = EmitGenericPrivateBrandCheck(bridge, receiver, name);
        IL.Emit(OpCodes.Ldloc, slots);
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(typeof(Dictionary<string, object?>), "set_Item", typeof(string), typeof(object)));
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
        return true;
    }

    private PrivateInstanceBridge? GetGenericPrivateFieldBridge(string name)
    {
        if (Ctx.CurrentClassName is not { } current)
            return null;
        string owner = Ctx.ResolvePrivateFieldOwner(current, name);
        return Ctx.ClassRegistry?.GetPrivateFieldNames(owner)?.Contains(name) == true
            ? Ctx.ClassRegistry.GetPrivateInstanceBridge(owner) : null;
    }

    private LocalBuilder SpillGenericPrivateOperand(Expr expression)
    {
        EmitExpression(expression);
        EnsureBoxed();
        var local = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, local);
        return local;
    }

    protected bool TryEmitGenericPrivateCall(Expr.CallPrivate call, Func<Expr, LocalBuilder>? spill = null)
    {
        if (Ctx.CurrentClassName is not { } current)
            return false;
        string name = call.Name.Lexeme.TrimStart('#');
        string owner = Ctx.ResolvePrivateMethodOwner(current, name);
        var bridge = Ctx.ClassRegistry?.GetPrivateInstanceBridge(owner);
        if (bridge is null || !bridge.Methods.TryGetValue(name, out var method))
            return false;

        spill ??= SpillGenericPrivateOperand;
        var receiver = spill(call.Object);
        EmitGenericPrivateBrandCheck(bridge, receiver, name);
        List<LocalBuilder> arguments = [];
        foreach (var argument in call.Arguments)
            arguments.Add(spill(argument));
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Castclass, bridge.InterfaceType);
        foreach (var argument in arguments)
            IL.Emit(OpCodes.Ldloc, argument);
        EmitPrivateCallUndefinedPadding(call.Arguments.Count, method.GetParameters().Length);
        IL.Emit(OpCodes.Callvirt, method);
        SetStackUnknown();
        return true;
    }

    private LocalBuilder EmitGenericPrivateBrandCheck(PrivateInstanceBridge bridge, LocalBuilder receiver, string name)
    {
        var slots = IL.DeclareLocal(typeof(Dictionary<string, object?>));
        var invalid = IL.DefineLabel();
        var valid = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Isinst, bridge.InterfaceType);
        IL.Emit(OpCodes.Brfalse, invalid);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Castclass, bridge.InterfaceType);
        IL.Emit(OpCodes.Callvirt, bridge.StorageGetter);
        IL.Emit(OpCodes.Ldloc, receiver);
        IL.Emit(OpCodes.Ldloca, slots);
        var table = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, object?>>);
        IL.Emit(OpCodes.Callvirt, Types.GetMethod(table, "TryGetValue", typeof(object), typeof(Dictionary<string, object?>).MakeByRefType()));
        IL.Emit(OpCodes.Brtrue, valid);
        IL.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(IL, Ctx.Runtime!, $"Cannot access private member #{name} from an object whose class did not declare it");
        IL.MarkLabel(valid);
        return slots;
    }
}
