using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>$Runtime.IsPrototypeOfHelper(object receiverProto, object target) -&gt; bool</c>:
    /// returns true iff <paramref name="receiverProto"/> appears in
    /// <paramref name="target"/>'s prototype chain (walked via <c>PDSGetPrototype</c>).
    /// Per ECMA-262 §20.1.3.4 Object.prototype.isPrototypeOf.
    /// Used to back <c>obj.isPrototypeOf(other)</c> for $TSFunction / $Object /
    /// Dictionary receivers exposed via Object.prototype's populate.
    /// </summary>
    private void EmitIsPrototypeOfHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsPrototypeOfHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        runtime.IsPrototypeOfHelperMethod = method;
        // Param 0 is "__this" so the wrapping $TSFunction routes
        // .call(other, target) through InvokeWithThis's expectsThis path —
        // which now nulls _target around the inner Invoke so direct dispatch
        // (`o.isPrototypeOf(target)`) and .call dispatch produce the same
        // semantic answer. (Pre-fix the double-prepend / target-bound shape
        // collided; see the InvokeWithThis fix in RuntimeEmitter.TSFunction.cs.)
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "target");

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // ECMA-262 §20.1.3.4 step 1: If Type(V) is not Object, return false.
        // The Type(V) check predates the ToObject(this value) step (step 2),
        // so this returns false even when `this` is undefined/null/primitive.
        // Without the order-preserving guard, ObjectGetPrototypeOf below now
        // throws TypeError on undefined V (post the gpo null-throw fix).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        // Step 2 reached (V is Object): ToObject(this) on null/undefined throws.
        // Pre-fix returned false silently, failing null-this-and-object-arg-
        // throws.js. ECMA-262 §7.1.18 mandates the TypeError.
        var receiverNullThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, receiverNullThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, receiverNullThrowLabel);
        var afterReceiverNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterReceiverNullLabel);
        il.MarkLabel(receiverNullThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(afterReceiverNullLabel);

        // Walk: current = PDSGetPrototype(target);
        // while (current != null) {
        //   if (current == receiverProto) return true;
        //   current = PDSGetPrototype(current);
        // }
        var currentLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, currentLocal);

        var loopStart = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // if (current == receiverProto) return true
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Beq, trueLabel);

        // current = PDSGetPrototype(current)
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }
}
