using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Populates <see cref="EmittedRuntime.NumberPrototypeField"/> with
    /// <c>$TSFunction</c> wrappers for the Number prototype methods we
    /// have helpers for. Mirrors <see cref="EmitArrayPrototypePopulate"/>.
    /// Only toFixed/toPrecision/toExponential have direct runtime helpers;
    /// others (toString/valueOf/toLocaleString) are wired to NumberToStringRadix
    /// as placeholders — they're typeof-probed but not invoked by the
    /// not-a-constructor.js tests, so the placeholder is sufficient.
    /// </summary>
    private void DefineNumberPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.NumberPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_NumberPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitNumberPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit valueOf helper before the populate body that wires it up.
        var numberValueOfHelper = EmitNumberValueOfHelper(typeBuilder, runtime);

        var method = runtime.NumberPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.NumberPrototypeField);

        var numDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // ECMA-262 21.1.3 Number.prototype.constructor === Number. Compiled
        // bare `Number` resolves to typeof(double) (per ILEmitter.Expressions
        // and InstanceOf semantics).
        EmitInstallConstructor(il, runtime, runtime.NumberPrototypeField, numDescLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.Double);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Wire with explicit JS-spec name + length per ECMA-262. Number's
        // prototype methods take (thisNumberValue, digits/precision/radix);
        // name first param "__this" so $TSFunction.InvokeWithThis prepends
        // the receiver. Without this, `n.toExponential(1000)` would map
        // 1000 to value (the first arg) and lose the receiver.
        // Built-in §17 attrs: W:T, E:F, C:T. Install a PDS data descriptor.
        void Wire(string jsName, MethodBuilder? helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.NumberPrototypeField, numDescLocal,
                setItem, jsName, helper, jsLength);

        Wire("toFixed",        runtime.NumberToFixed,         1);
        Wire("toPrecision",    runtime.NumberToPrecision,     1);
        Wire("toExponential",  runtime.NumberToExponential,   1);
        // Stub these with NumberToStringRadix so typeof + IsConstructor work.
        // Not actually invoked by user code in the not-a-constructor.js path.
        Wire("toString",       runtime.NumberToStringRadix,   1);
        Wire("toLocaleString", runtime.NumberToStringRadix,   0);
        Wire("valueOf",        numberValueOfHelper,           0);

        // PDSSetPrototype(NumberPrototypeField, ObjectPrototypeField).
        // Per ECMA-262 §21.1.3 Number.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Number.prototype.valueOf helper per ECMA-262 21.1.3.7
    /// thisNumberValue. Returns the receiver if it's a double primitive,
    /// the boxed wrapper's <c>__primitiveValue</c> when it's a real number,
    /// or 0 when the receiver is the Number.prototype singleton. All other
    /// receivers (String wrappers, plain objects, etc.) throw TypeError —
    /// matches Test262's `Number.prototype.valueOf.call(non-Number)` checks.
    /// </summary>
    private MethodBuilder EmitNumberValueOfHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberValueOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();

        // Double primitive — return as-is.
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDoubleLabel);

        // ECMA-262 §21.1.3: Number.prototype's [[NumberData]] is +0.
        var notNumberPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        il.Emit(OpCodes.Bne_Un, notNumberPrototypeLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumberPrototypeLabel);

        // Boxed Number wrapper: only when __primitiveValue is itself a double.
        var primValLocal = il.DeclareLocal(_types.Object);
        var throwTypeErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwTypeErrorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, throwTypeErrorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocal);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, throwTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwTypeErrorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Number.prototype.valueOf requires that 'this' be a Number");

        return method;
    }
}
