using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Populates <see cref="EmittedRuntime.BooleanPrototypeField"/> with
    /// <c>$TSFunction</c> wrappers for toString/valueOf. No dedicated
    /// $Runtime helpers exist for these (compiled mode handles
    /// <c>Boolean.toString()</c> via the inline String-conversion path),
    /// so the wrappers point at <see cref="EmittedRuntime.StringPrototypeGenericStub"/>
    /// — sufficient for typeof + IsConstructor probes from
    /// Test262 not-a-constructor.js tests.
    /// </summary>
    private void DefineBooleanPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.BooleanPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_BooleanPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitBooleanPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit toString / valueOf helpers before the populate body that wires
        // them up (Stage Path-A: spec-correct thisBooleanValue extraction).
        var booleanToStringHelper = EmitBooleanToStringHelper(typeBuilder, runtime);
        var booleanValueOfHelper = EmitBooleanValueOfHelper(typeBuilder, runtime);

        var method = runtime.BooleanPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.BooleanPrototypeField);

        var boolDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // ECMA-262 20.3.3 Boolean.prototype.constructor === Boolean. Compiled
        // bare `Boolean` resolves to typeof(bool).
        EmitInstallConstructor(il, runtime, runtime.BooleanPrototypeField, boolDescLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.Boolean);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Wire with explicit JS-spec name + length per ECMA-262.
        // Boolean.prototype.{toString,valueOf} take (thisBooleanValue) — name
        // first param "__this" so $TSFunction.InvokeWithThis prepends the receiver.
        // Built-in §17 attrs: W:T, E:F, C:T. Install a PDS data descriptor.
        void Wire(string jsName, MethodBuilder? helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.BooleanPrototypeField, boolDescLocal,
                setItem, jsName, helper, jsLength);

        Wire("toString", booleanToStringHelper, 0);
        Wire("valueOf",  booleanValueOfHelper,  0);

        // Per ECMA-262 §20.3.3 Boolean.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Boolean.prototype.toString helper (ECMA-262 20.3.3.2). Returns
    /// "true" / "false" based on thisBooleanValue extraction:
    ///   - bool primitive → format directly
    ///   - $Object with __primitiveValue: bool → format the unwrapped value
    ///   - Boolean.prototype itself → "false" (its [[BooleanData]] is +false)
    ///   - else → "false" (lenient: spec throws TypeError, but compiled mode's
    ///     looser behavior matches what the not-a-constructor probes need).
    /// Avoids $Runtime.GetProperty for the prototype-singleton case to dodge
    /// the prototype-chain recursion that walks back into BooleanPrototype.
    /// </summary>
    private MethodBuilder EmitBooleanToStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BooleanToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        var il = method.GetILGenerator();

        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var notBoolLabel = il.DefineLabel();
        var notBoxedLabel = il.DefineLabel();

        // bool primitive
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notBoolLabel);

        // $TSObject → check __primitiveValue. Use TryGetValue on the field
        // dict directly to avoid recursing through GetProperty's prototype-
        // chain walk (which would loop back to BooleanPrototype).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        var primValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Ldloca, primValLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);
        il.MarkLabel(notBoxedLabel);

        // Boolean.prototype itself: [[BooleanData]] is +false → "false".
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        il.Emit(OpCodes.Beq, falseLabel);

        // Other receivers (e.g. new String() with __primitiveType="String", or a
        // plain Object): per ECMA-262 §20.3.3.2 throw TypeError. The borrowed-
        // method tests `s1.toString = Boolean.prototype.toString; s1.toString()`
        // rely on this throw.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Boolean.prototype.toString requires a Boolean this value");

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits Boolean.prototype.valueOf helper (ECMA-262 20.3.3.3). Returns the
    /// boolean primitive via thisBooleanValue extraction, with the same
    /// receiver shape recognition as <see cref="EmitBooleanToStringHelper"/>.
    /// Default for unrecognized receivers: false (matches Boolean.prototype's
    /// own [[BooleanData]]).
    /// </summary>
    private MethodBuilder EmitBooleanValueOfHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BooleanValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();

        var notBoolLabel = il.DefineLabel();
        var notBoxedLabel = il.DefineLabel();

        // bool primitive → return as-is
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoolLabel);

        // $TSObject → unwrap __primitiveValue if it's a bool
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        var primValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Ldloca, primValLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoxedLabel);

        // Boolean.prototype itself: [[BooleanData]] is +false.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        var notBoolPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notBoolPrototypeLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoolPrototypeLabel);

        // Other receivers: throw TypeError per ECMA-262 §20.3.3.3.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Boolean.prototype.valueOf requires a Boolean this value");

        // Unreachable but balances stack:
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
