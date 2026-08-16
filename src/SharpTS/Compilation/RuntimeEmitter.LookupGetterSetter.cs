using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>$Runtime.LookupGetterHelper(object __this, object key)</c> and
    /// <c>$Runtime.LookupSetterHelper(object __this, object key)</c> backing
    /// <c>Object.prototype.__lookupGetter__</c> / <c>__lookupSetter__</c>
    /// (ECMA-262 §B.2.2.4 / §B.2.2.5). Walks the prototype chain calling
    /// <see cref="EmittedRuntime.PDSGetPropertyDescriptor"/> at each level; returns
    /// the descriptor's [[Get]]/[[Set]] slot when an accessor descriptor is found,
    /// undefined when a data descriptor is found, undefined when the chain is
    /// exhausted.
    /// </summary>
    private void EmitLookupAccessorHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.LookupGetterHelperMethod = EmitLookupAccessorHelper(typeBuilder, runtime, isGetter: true);
        runtime.LookupSetterHelperMethod = EmitLookupAccessorHelper(typeBuilder, runtime, isGetter: false);
        runtime.DefineGetterHelperMethod = EmitDefineAccessorHelper(typeBuilder, runtime, isGetter: true);
        runtime.DefineSetterHelperMethod = EmitDefineAccessorHelper(typeBuilder, runtime, isGetter: false);
    }

    /// <summary>
    /// Emits <c>$Runtime.DefineGetterHelper / DefineSetterHelper(object __this, object key, object fn)</c>
    /// backing <c>Object.prototype.__defineGetter__/__defineSetter__</c>
    /// (ECMA-262 §B.2.2.2 / §B.2.2.3). Validates the function arg is callable,
    /// builds a configurable+enumerable accessor descriptor, and forwards to
    /// <see cref="EmittedRuntime.ObjectDefineProperty"/>.
    /// </summary>
    private MethodBuilder EmitDefineAccessorHelper(TypeBuilder typeBuilder, EmittedRuntime runtime, bool isGetter)
    {
        var name = isGetter ? "DefineGetterHelper" : "DefineSetterHelper";
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "key");
        method.DefineParameter(3, ParameterAttributes.None, "fn");

        var il = method.GetILGenerator();
        var descDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        // ECMA-262 §B.2.2.2 step 2 / §B.2.2.3 step 2: IsCallable(fn) === false → TypeError.
        // Strict callable check: $TSFunction / $BoundTSFunction / Type (constructor) /
        // MethodInfo (raw helper). Everything else (string/number/null/undefined/
        // plain object) triggers the throw.
        var isCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.MethodInfo);
        il.Emit(OpCodes.Brtrue, isCallableLabel);
        // Not callable — throw TypeError.
        il.Emit(OpCodes.Ldstr, isGetter
            ? "Object.prototype.__defineGetter__: callback must be callable"
            : "Object.prototype.__defineSetter__: callback must be callable");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(isCallableLabel);

        // desc = new Dictionary<string, object>();
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, descDictLocal);
        // desc[isGetter ? "get" : "set"] = fn;
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Ldstr, isGetter ? "get" : "set");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, setItem);
        // desc["configurable"] = true; desc["enumerable"] = true.
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        // ObjectDefineProperty(__this, key, desc); return undefined.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descDictLocal);
        il.Emit(OpCodes.Call, runtime.ObjectDefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitLookupAccessorHelper(TypeBuilder typeBuilder, EmittedRuntime runtime, bool isGetter)
    {
        var name = isGetter ? "LookupGetterHelper" : "LookupSetterHelper";
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "key");

        var il = method.GetILGenerator();
        var keyLocal = il.DeclareLocal(_types.String);
        var oLocal = il.DeclareLocal(_types.Object);
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        var returnUndefinedLabel = il.DefineLabel();
        var throwThisLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var advanceProtoLabel = il.DefineLabel();
        var hasSlotLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwThisLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwThisLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, oLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Brfalse, returnUndefinedLabel);
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnUndefinedLabel);

        // desc = PDSGetPropertyDescriptor(O, key)
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);

        var noDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Brfalse, noDescLabel);

        // PDS desc found. If isGetter: return desc.Getter ?? undefined. Else Setter.
        var slot = isGetter ? runtime.CompiledPropertyDescriptorGetter : runtime.CompiledPropertyDescriptorSetter;
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, slot.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasSlotLabel);
        // null slot — data descriptor on this level, return undefined per spec.
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasSlotLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noDescLabel);
        // No PDS descriptor. If a non-accessor data property exists on this
        // level (dict key, etc.), spec says return undefined. Otherwise walk up.
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brfalse, advanceProtoLabel);
        // Has data property at this level → return undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(advanceProtoLabel);
        // O = ObjectGetPrototypeOf(O). When the dispatch returns null (top of
        // chain), the loop-start null check returns undefined.
        il.Emit(OpCodes.Ldloc, oLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, oLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(throwThisLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            isGetter
                ? "Object.prototype.__lookupGetter__ called on null or undefined"
                : "Object.prototype.__lookupSetter__ called on null or undefined");

        il.MarkLabel(returnUndefinedLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
