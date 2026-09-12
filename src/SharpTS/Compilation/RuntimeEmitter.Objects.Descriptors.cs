using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Marks the shared descriptor-found label with an empty stack, then branches to the
    // shared exit with one result object. Reads the stored descriptor and receiver (arg 0);
    // internal labels and scratch locals belong to this stage.
    private void EmitStoredPropertyDescriptorResult(
        ILGenerator il, EmittedRuntime runtime,
        LocalBuilder descriptorLocal, LocalBuilder resultDictLocal,
        Label hasDescriptorLabel, Label endLabel)
    {
        // hasDescriptorLabel: Convert $CompiledPropertyDescriptor to JS object
        il.MarkLabel(hasDescriptorLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // Check if it's an accessor property (has getter or setter)
        var isAccessorLabel = il.DefineLabel();
        var isDataLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, isAccessorLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, isAccessorLabel);
        il.Emit(OpCodes.Br, isDataLabel);

        // Accessor property - set get and set. ECMA-262 §6.2.5.4
        // FromPropertyDescriptor: an accessor descriptor result always has
        // "get" and "set" keys even when one slot is empty (the missing slot
        // serializes as JS undefined). Pre-fix the missing key wasn't present
        // at all, causing `"set" in desc` to be false for getter-only
        // accessors. Stash $Undefined.Instance when the slot is null.
        il.MarkLabel(isAccessorLabel);

        // Set get property
        var noGetLabel = il.DefineLabel();
        var afterGetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, noGetLabel);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, afterGetLabel);
        il.MarkLabel(noGetLabel);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.MarkLabel(afterGetLabel);

        // Set set property
        var noSetLabel = il.DefineLabel();
        var afterSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, noSetLabel);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, afterSetLabel);
        il.MarkLabel(noSetLabel);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.MarkLabel(afterSetLabel);

        var afterAccessorLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterAccessorLabel);

        // Data property - set value and writable. Frozen/sealed override the
        // descriptor's stored writable/configurable: spec says Object.freeze
        // mutates each descriptor, but we don't mutate storage — reflect at
        // read time to keep the storage stable across {freeze, defrost} cycles.
        il.MarkLabel(isDataLabel);
        var pdsIsFrozenLocal = il.DeclareLocal(_types.Boolean);
        var pdsIsSealedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Stloc, pdsIsFrozenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Stloc, pdsIsSealedLocal);

        // Set value
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Set writable: stored value AND !frozen.
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, pdsIsFrozenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !frozen
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.MarkLabel(afterAccessorLabel);

        // Set enumerable (freeze/seal preserve enumerability).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // Set configurable: stored value AND !(frozen OR sealed).
        // For accessor (data path not entered), the pdsIs* locals are still
        // computed in the data branch — when we reach here via accessor,
        // they're default-zero (Boolean) which means the override AND yields
        // the stored value. Compute them here for accessor independence.
        var pdsCfgIsFrozenLocal = il.DeclareLocal(_types.Boolean);
        var pdsCfgIsSealedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Stloc, pdsCfgIsFrozenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Stloc, pdsCfgIsSealedLocal);

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, pdsCfgIsFrozenLocal);
        il.Emit(OpCodes.Ldloc, pdsCfgIsSealedLocal);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !(frozen || sealed)
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
    }

    /// <summary>
    /// Helper: emits IL to set a boolean descriptor field
    /// (writable/enumerable/configurable) on the result dict to a constant
    /// value. Reduces 6 lines of boilerplate to one call at each site.
    /// </summary>
    private void EmitDescriptorBoolField(ILGenerator il, LocalBuilder resultDictLocal, string fieldName, bool value)
    {
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, fieldName);
        il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
    }

    // These stages require an empty evaluation stack and leave it empty on fallthrough.
    // Receiver branches own their labels and return from the emitted method when handled;
    // the caller owns shared locals and preserves dispatch/coercion order.
    private void EmitDefinePropertyReceiverValidation(ILGenerator il, EmittedRuntime runtime)
    {
        // ECMA-262 §20.1.2.4 step 1: If Type(O) is not Object, throw TypeError.
        // Covers null/undefined/primitives. test262 15.2.3.6-{1-*}.js verify.
        var primitiveThrowLabel = il.DefineLabel();
        var skipTypeThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, primitiveThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, primitiveThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, primitiveThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, primitiveThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, primitiveThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, primitiveThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, primitiveThrowLabel);
        il.Emit(OpCodes.Br, skipTypeThrowLabel);

        il.MarkLabel(primitiveThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.defineProperty called on non-object");
        il.MarkLabel(skipTypeThrowLabel);
    }

    private void EmitDefinePropertySymbolReceiver(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, LocalBuilder descriptorLocal)
    {
        // Symbol-keyed properties live in the object's symbol dictionary. Normalize
        // the supplied descriptor through this same method using an ephemeral
        // string-keyed holder, then store the resulting compiled descriptor. This
        // keeps the symbol path aligned with the ordinary ToPropertyDescriptor
        // validation/defaulting rules without maintaining a second parser.
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);

        // A symbol property uses a separate key dictionary, but it is still an
        // ordinary own property for [[Extensible]]. Existing symbol keys may be
        // redefined; creating a new one on a non-extensible target must throw.
        var symbolCanDefineLabel = il.DefineLabel();
        var symbolDefineDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDefineDictLocal);
        il.Emit(OpCodes.Ldloc, symbolDefineDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brtrue, symbolCanDefineLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsExtensible);
        il.Emit(OpCodes.Brtrue, symbolCanDefineLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "Cannot define property on a non-extensible object");
        il.MarkLabel(symbolCanDefineLabel);

        var symbolDescriptorHolderLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, symbolDescriptorHolderLocal);
        il.Emit(OpCodes.Ldloc, symbolDescriptorHolderLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, symbolDescriptorHolderLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);

        // GetSymbolDict(obj)[symbol] = normalizedDescriptor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
        // Return the target object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSymbolLabel);
    }

    private void EmitDefinePropertyProxyReceiver(ILGenerator il, EmittedRuntime runtime, MethodBuilder method, LocalBuilder propNameLocal)
    {
        // Proxy [[DefineOwnProperty]] dispatch. The callback lets the runtime
        // proxy forward a missing trap to the emitted target representation.
        var notProxyForDefineLabel = il.DefineLabel();
        var proxyForDefineLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il,
            () => il.Emit(OpCodes.Ldarg_0),
            proxyForDefineLabel,
            notProxyForDefineLabel);
        il.MarkLabel(proxyForDefineLabel);
        EmitProxyMethodCallUnwrapped(
            il, runtime, () => il.Emit(OpCodes.Ldarg_0),
            "TrapDefinePropertyCompiled", () =>
        {
            il.Emit(OpCodes.Ldc_I4_7);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, method);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(
                typeof(Func<object, object, object, object?>),
                _types.Object, _types.IntPtr)!);
            il.Emit(OpCodes.Stelem_Ref);
            EmitDelegateArgument(3, runtime.ObjectGetOwnPropertyDescriptor,
                typeof(Func<object, object, object?>));
            EmitDelegateArgument(4, runtime.ObjectIsExtensible,
                typeof(Func<object, bool>));
            EmitDelegateArgument(5, runtime.GetProperty,
                typeof(Func<object, string, object?>));
            EmitDelegateArgument(6, runtime.HasOwnPropertyHelperMethod,
                typeof(Func<object, string, bool>));

            void EmitDelegateArgument(int slot, MethodInfo target, Type delegateType)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, target);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    delegateType, _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
            }
        });
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var proxyDefineSucceededLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, proxyDefineSucceededLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Proxy defineProperty trap returned false");
        il.MarkLabel(proxyDefineSucceededLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyForDefineLabel);
    }

    /// <summary>
    /// Emits Object.defineProperty(obj, prop, descriptor) - defines or modifies a property.
    /// Signature: object ObjectDefineProperty(object obj, object prop, object descriptor)
    /// Creates a $CompiledPropertyDescriptor and registers it in the emitted $PropertyDescriptorStore.
    /// </summary>
    private void EmitObjectDefineProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectDefineProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.ObjectDefineProperty = method;

        var il = method.GetILGenerator();

        // Emit standalone property descriptor creation and registration
        // This avoids any runtime dependency on SharpTS.dll

        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var propNameLocal = il.DeclareLocal(_types.String);
        var valueLocal = il.DeclareLocal(_types.Object);
        var notDictLabel = il.DefineLabel();
        var setDescriptorDoneLabel = il.DefineLabel();

        EmitDefinePropertyReceiverValidation(il, runtime);

        EmitDefinePropertySymbolReceiver(il, runtime, method, descriptorLocal);

        // propName = $Runtime.ToJsString(prop) — ECMA-262 §7.1.19 ToPropertyKey
        // string path via the spec-shaped ToString. Avoids the prop.ToString()
        // Callvirt-on-null NRE for `Object.defineProperty(obj, null, ...)`,
        // and unlike runtime.Stringify (which produces debug "[1, 2]" form for
        // arrays) honors `Array.prototype.toString` join semantics so
        // `defineProperty(obj, [1], ...)` lands at key "1" (matches V8/SM).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, propNameLocal);

        EmitDefinePropertyProxyReceiver(il, runtime, method, propNameLocal);

        var (lenWasCoercedLocal, lenValLocal, coercedLenLocal) =
            EmitDefinePropertyArrayLengthCoercion(il, runtime, propNameLocal);

        EmitDefinePropertyExtensibilityValidation(il, runtime, propNameLocal);

        // Create new $CompiledPropertyDescriptor
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descriptorLocal);

        // ECMA-262 6.2.5.1 CompletePropertyDescriptor: when Object.defineProperty receives
        // a partial descriptor, unspecified writable/enumerable/configurable default to FALSE.
        // The ctor sets them to true (used by CreateDataProperty for `obj.foo = X`);
        // we reset them here to match the spec for the defineProperty path.
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);

        EmitDefinePropertyDescriptorTypeValidation(il, runtime);

        EmitDefinePropertyDescriptorNormalization(
            il, runtime, dictLocal, lenWasCoercedLocal, lenValLocal, coercedLenLocal);

        // Extract properties from descriptor dictionary
        var dictTryGetValue = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType());

        EmitDefinePropertyDescriptorFields(il, runtime, dictLocal, descriptorLocal, valueLocal, dictTryGetValue);

        il.MarkLabel(setDescriptorDoneLabel);

        var existingDescLocal = EmitDefinePropertyExistingDescriptor(il, runtime, propNameLocal);

        var (newIsAccessorOuter, newIsDataOuter) =
            EmitDefinePropertyDescriptorClassification(il, runtime, dictLocal, dictTryGetValue);

        EmitDefinePropertyRedefinitionValidation(il, runtime, propNameLocal, dictLocal, existingDescLocal, dictTryGetValue);

        EmitDefinePropertyDescriptorMerge(
            il, runtime, dictLocal, descriptorLocal, existingDescLocal, dictTryGetValue, newIsAccessorOuter, newIsDataOuter);

        // Call $PropertyDescriptorStore.DefineProperty(obj, propName, descriptor)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        var descriptorStoredLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, descriptorStoredLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "Cannot define property on a non-extensible object");
        il.MarkLabel(descriptorStoredLabel);

        EmitDefinePropertyArrayIndexLengthGrowth(il, runtime, propNameLocal);

        EmitDefinePropertyAccessorStorage(il, runtime, propNameLocal, descriptorLocal);

        EmitDefinePropertyDataStorage(il, runtime, propNameLocal, dictLocal, descriptorLocal, dictTryGetValue);
        // Return the object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // Reads receiver/descriptor arguments (0/2), with an empty stack on entry and fallthrough.
    // The returned locals are the only state shared with descriptor normalization: when
    // WasCoerced is true, OriginalValue caches the single getter read and CoercedValue
    // holds the validated length. All labels and remaining locals belong to this stage.
    private (LocalBuilder WasCoerced, LocalBuilder OriginalValue, LocalBuilder CoercedValue)
        EmitDefinePropertyArrayLengthCoercion(ILGenerator il, EmittedRuntime runtime, LocalBuilder propNameLocal)
    {
        // ECMA-262 10.4.2.4 ArraySetLength steps 3-4: newLen =
        // ToUint32(Desc.[[Value]]), numberLen = ToNumber(Desc.[[Value]]) —
        // exactly two coercions, in that order (test262 define-own-prop-
        // length-coercion-order.js counts the valueOf calls). If
        // SameValueZero(newLen, numberLen) is false → RangeError, which
        // rejects NaN, ±Infinity, negatives, non-integers, and >= 2^32.
        // The coerced newLen then REPLACES the descriptor's value (stashed
        // into the synth dict after the overlay pass below) so the raw
        // object never reaches the PDS — re-coercing a stored object value
        // on a later redefine is what produced the unbounded
        // ObjectDefineProperty ⇄ ToNumber recursion of issue #180.
        // Only fires for compiled Array receivers with
        // propName == "length" and a value-typed descriptor.
        var skipArrayLenCheck = il.DefineLabel();
        var lenWasCoercedLocal = il.DeclareLocal(_types.Boolean);
        var coercedLenLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
        var lenValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, lenValLocal);
        // First coercion (ToUint32's inner ToNumber) — valueOf call #1.
        var lenNumLocal = il.DeclareLocal(_types.Double);
        var newLenLocal = il.DeclareLocal(_types.Double);
        var numberLenLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, lenNumLocal);
        // newLen = ToUint32(lenNum): NaN/±Inf → 0; else truncate, fmod 2^32,
        // normalize into [0, 2^32).
        var uintZeroLabel = il.DefineLabel();
        var uintDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenNumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, uintZeroLabel);
        il.Emit(OpCodes.Ldloc, lenNumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, uintZeroLabel);
        il.Emit(OpCodes.Ldloc, lenNumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", _types.Double));
        il.Emit(OpCodes.Ldc_R8, 4294967296.0);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, newLenLocal);
        il.Emit(OpCodes.Ldloc, newLenLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bge, uintDoneLabel);
        il.Emit(OpCodes.Ldloc, newLenLocal);
        il.Emit(OpCodes.Ldc_R8, 4294967296.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, newLenLocal);
        il.Emit(OpCodes.Br, uintDoneLabel);
        il.MarkLabel(uintZeroLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, newLenLocal);
        il.MarkLabel(uintDoneLabel);
        // Normalize -0 → +0 (x + 0.0 is identity for everything else).
        il.Emit(OpCodes.Ldloc, newLenLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, newLenLocal);
        // Second coercion — valueOf call #2.
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, numberLenLocal);
        // SameValueZero(newLen, numberLen) — Bne_Un branches on unordered,
        // so a NaN numberLen lands at rangeErr; ±0 compare equal.
        var rangeErrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newLenLocal);
        il.Emit(OpCodes.Ldloc, numberLenLocal);
        il.Emit(OpCodes.Bne_Un, rangeErrLabel);
        // Stash box(newLen) for the synth-dict override below.
        il.Emit(OpCodes.Ldloc, newLenLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, coercedLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, lenWasCoercedLocal);
        il.Emit(OpCodes.Br, skipArrayLenCheck);
        il.MarkLabel(rangeErrLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "Invalid array length");
        il.MarkLabel(skipArrayLenCheck);

        return (lenWasCoercedLocal, lenValLocal, coercedLenLocal);
    }

    // Reads descriptor argument 2; throws or falls through with an empty stack.
    private void EmitDefinePropertyDescriptorTypeValidation(ILGenerator il, EmittedRuntime runtime)
    {
        // ECMA-262 §6.2.5.5 ToPropertyDescriptor step 1: If Type(Obj) is not
        // Object, throw TypeError. Covers null/undefined/primitives in the
        // descriptor slot. Tests 15.2.3.6-3-{15,16,17,...} verify each.
        var descTypeOkLabel = il.DefineLabel();
        var descThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, descThrowLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        // BigInt and Symbol are also primitives per ECMA-262 — reject them too.
        // BigInt: System.Numerics.BigInteger (boxed). Symbol: $TSSymbol.
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, descThrowLabel);
        il.Emit(OpCodes.Br, descTypeOkLabel);

        il.MarkLabel(descThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Property description must be an object");

        il.MarkLabel(descTypeOkLabel);
    }

    // Reads descriptor argument 2 and the array-length coercion locals; writes dictLocal.
    // Entry and fallthrough stacks are empty. Getter reads, own-field overlays, and the
    // coerced-length override must remain in this order. Scratch locals/labels stay here.
    private void EmitDefinePropertyDescriptorNormalization(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder dictLocal,
        LocalBuilder lenWasCoercedLocal, LocalBuilder lenValLocal, LocalBuilder coercedLenLocal)
    {
        // ECMA-262 §6.2.5.5 ToPropertyDescriptor reads each known descriptor
        // field via [[Get]], which walks the prototype chain AND invokes
        // accessors. We always normalize the descriptor into a fresh Dict via
        // runtime.GetProperty (which checks PDS accessors + walks proto chain
        // for $Object / $IHasFields), then if the descriptor is itself a Dict,
        // overlay explicit own keys on top — so `{value: undefined}` correctly
        // sets value to JS undefined rather than being treated as absent.
        var origDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, origDictLocal);

        // synthDict = new Dictionary<string, object?>();
        var synthDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, synthDictLocal);

        var synthDictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        var synthDictTryGetValue = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType());

        // For each well-known descriptor field, GetProperty(descriptor, field).
        // GetProperty walks the prototype chain and invokes getters. A defined
        // result is stashed directly. When Get yields undefined we additionally
        // probe for an INHERITED accessor (getter OR setter) via the prototype-
        // walking __lookupGetter__/__lookupSetter__ helpers: a setter-only
        // inherited `value` (or any field) IS specified per §6.2.5.5 HasProperty
        // even though Get reads undefined (#801), so we stash $Undefined for it.
        // Treats "undefined with no accessor" as "field absent" UNLESS the field
        // is an explicit own key on the input Dict (the overlay pass handles that).
        //
        // Branches to `target` when `local` holds a defined value (non-null and
        // not $Undefined); otherwise falls through.
        void EmitBranchIfDefined(LocalBuilder local, Label target)
        {
            var notDefined = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Brfalse, notDefined);
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, notDefined);
            il.Emit(OpCodes.Br, target);
            il.MarkLabel(notDefined);
        }

        var getterGet = runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!;
        var setterGet = runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!;

        void EmitGetAndStash(string field)
        {
            var stashLabel = il.DefineLabel();
            var skipLabel = il.DefineLabel();
            var fieldValLocal = il.DeclareLocal(_types.Object);

            // fieldVal = GetProperty(descriptor, field)
            // ArraySetLength already performed the single observable [[Get]]
            // of Desc.[[Value]] before its two numeric coercions. Reuse that
            // cached result during ToPropertyDescriptor normalization so an
            // accessor-backed descriptor is not invoked twice.
            if (field == "value")
            {
                var loadValueNormallyLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, lenWasCoercedLocal);
                il.Emit(OpCodes.Brfalse, loadValueNormallyLabel);
                il.Emit(OpCodes.Ldloc, lenValLocal);
                il.Emit(OpCodes.Stloc, fieldValLocal);
                il.Emit(OpCodes.Br, stashLabel);
                il.MarkLabel(loadValueNormallyLabel);
            }
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fieldValLocal);
            // Defined value → stash it.
            EmitBranchIfDefined(fieldValLocal, stashLabel);
            // A null/undefined Get result is still specified when HasProperty
            // succeeds.  Use the shared existence-only walk so ordinary
            // $IHasFields slots (including compact-record `{ get: null }`),
            // inherited data properties, and accessor descriptors are all
            // distinguished from an absent field without firing a getter.
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
            il.Emit(OpCodes.Brtrue, stashLabel);
            il.Emit(OpCodes.Br, skipLabel);

            // stash: synthDict[field] = fieldVal.  CLR null is JavaScript null
            // and must remain distinct from the emitted $Undefined singleton;
            // setter-only accessors already return that singleton from
            // GetProperty.  Rewriting a proven-present null here incorrectly
            // accepted descriptors such as `{ get: null }`.
            il.MarkLabel(stashLabel);
            il.Emit(OpCodes.Ldloc, synthDictLocal);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Ldloc, fieldValLocal);
            il.Emit(OpCodes.Callvirt, synthDictSetItem);
            il.MarkLabel(skipLabel);
        }
        EmitGetAndStash("value");
        EmitGetAndStash("writable");
        EmitGetAndStash("get");
        EmitGetAndStash("set");
        EmitGetAndStash("enumerable");
        EmitGetAndStash("configurable");

        // Overlay: if descriptor is a Dict, copy each well-known field that
        // is present as an OWN key in the input Dict over the synth — this
        // preserves `{value: undefined}` (explicit own key with undefined
        // value) while still picking up PDS accessors via the GetProperty
        // pass above.
        var skipOverlayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, origDictLocal);
        il.Emit(OpCodes.Brfalse, skipOverlayLabel);

        void EmitOverlay(string field)
        {
            var skipLabel = il.DefineLabel();
            var fieldValLocal = il.DeclareLocal(_types.Object);

            // Accessor properties on dictionary-backed objects also have a raw
            // placeholder entry in the dictionary. The GetProperty pass above
            // has already invoked the accessor and stashed its result; copying
            // the placeholder here would replace that result with undefined.
            // Only overlay ordinary own data entries (the path needed to
            // distinguish an explicit `{ value: undefined }` from absence).
            var ownDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, ownDescriptorLocal);
            var noOwnDescriptorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, ownDescriptorLocal);
            il.Emit(OpCodes.Brfalse, noOwnDescriptorLabel);
            il.Emit(OpCodes.Ldloc, ownDescriptorLocal);
            il.Emit(OpCodes.Callvirt, getterGet);
            il.Emit(OpCodes.Brtrue, skipLabel);
            il.Emit(OpCodes.Ldloc, ownDescriptorLocal);
            il.Emit(OpCodes.Callvirt, setterGet);
            il.Emit(OpCodes.Brtrue, skipLabel);
            il.MarkLabel(noOwnDescriptorLabel);

            il.Emit(OpCodes.Ldloc, origDictLocal);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Ldloca, fieldValLocal);
            il.Emit(OpCodes.Callvirt, synthDictTryGetValue);
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Ldloc, synthDictLocal);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Ldloc, fieldValLocal);
            il.Emit(OpCodes.Callvirt, synthDictSetItem);
            il.MarkLabel(skipLabel);
        }
        EmitOverlay("value");
        EmitOverlay("writable");
        EmitOverlay("get");
        EmitOverlay("set");
        EmitOverlay("enumerable");
        EmitOverlay("configurable");

        il.MarkLabel(skipOverlayLabel);

        // ECMA-262 10.4.2.4 ArraySetLength step 5: newLenDesc.[[Value]] =
        // newLen. When the array-length coercion above ran, override the
        // synth dict's "value" with the coerced uint32 so the descriptor
        // (and the PDS entry it becomes) holds a plain number — never the
        // raw object whose valueOf re-fires on later redefines (issue #180).
        var skipLenValueOverride = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenWasCoercedLocal);
        il.Emit(OpCodes.Brfalse, skipLenValueOverride);
        il.Emit(OpCodes.Ldloc, synthDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, coercedLenLocal);
        il.Emit(OpCodes.Callvirt, synthDictSetItem);
        il.MarkLabel(skipLenValueOverride);

        il.Emit(OpCodes.Ldloc, synthDictLocal);
        il.Emit(OpCodes.Stloc, dictLocal);
    }

    // Reads the normalized dictionary and updates the caller-owned descriptor. valueLocal
    // is caller-owned scratch; its final value is not an output of this stage. Callable
    // validation may throw. Entry/fallthrough stacks are empty and all labels are local.
    private void EmitDefinePropertyDescriptorFields(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder dictLocal,
        LocalBuilder descriptorLocal, LocalBuilder valueLocal, MethodInfo dictTryGetValue)
    {
        // Try to get "value" property
        var noValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, noValueLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.MarkLabel(noValueLabel);

        // Try to get "writable" property
        var noWritableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, noWritableLabel);
        // Convert to bool and set
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);  // Convert to bool
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.MarkLabel(noWritableLabel);

        // Try to get "get" property (getter). ECMA-262 §6.2.5.5 step 7:
        // if "get" is present and not callable and not undefined → throw TypeError.
        // For undefined, we store $Undefined.Instance in the slot so the
        // descriptor classifier (slot non-null = accessor) still treats this
        // as an accessor descriptor (verifyProperty expects `desc.get === undefined`).
        var noGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, noGetterLabel);
        var getterStoreLabel = il.DefineLabel();
        var getterIsUndefLabel = il.DefineLabel();
        // Only JS-undefined (Isinst UndefinedType) is the accepted non-callable
        // value per ECMA-262 §6.2.5.5 step 7. JS-null falls through to the
        // callable-instance check (which it fails) and throws.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, getterIsUndefLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, getterStoreLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brtrue, getterStoreLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Property descriptor 'get' is not callable");
        il.MarkLabel(getterIsUndefLabel);
        // Store $Undefined.Instance so the descriptor remains classified as
        // accessor (slot non-null).
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetSetMethod()!);
        il.Emit(OpCodes.Br, noGetterLabel);
        il.MarkLabel(getterStoreLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetSetMethod()!);
        il.MarkLabel(noGetterLabel);

        // Try to get "set" property (setter). Same callable check as "get".
        var noSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, noSetterLabel);
        var setterStoreLabel = il.DefineLabel();
        var setterIsUndefLabel = il.DefineLabel();
        // Only JS-undefined accepted as non-callable per §6.2.5.5 step 8.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, setterIsUndefLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, setterStoreLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brtrue, setterStoreLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Property descriptor 'set' is not callable");
        il.MarkLabel(setterIsUndefLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetSetMethod()!);
        il.Emit(OpCodes.Br, noSetterLabel);
        il.MarkLabel(setterStoreLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetSetMethod()!);
        il.MarkLabel(noSetterLabel);

        // Try to get "enumerable" property
        var noEnumerableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, noEnumerableLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.MarkLabel(noEnumerableLabel);

        // Try to get "configurable" property
        var noConfigurableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, noConfigurableLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.MarkLabel(noConfigurableLabel);
    }

    // Reads the normalized fields and existing descriptor without mutating either.
    // Receiver argument 0 supplies the live array length for SameValue checks.
    // Every branch/throw belongs to this stage; entry and fallthrough stacks are empty.
    private void EmitDefinePropertyRedefinitionValidation(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder propNameLocal,
        LocalBuilder dictLocal, LocalBuilder existingDescLocal, MethodInfo dictTryGetValue)
    {
        var validationEndLabel = il.DefineLabel();
        // No existing descriptor → skip validation (new property add).
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Brfalse, validationEndLabel);
        // Existing is configurable → all changes allowed.
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, validationEndLabel);

        // Existing is non-configurable. Examine new descriptor for forbidden
        // changes. Re-consult the input dict for "was field X specified"
        // (the parsed descriptor already has all fields normalized).
        var throwRedefineLabel = il.DefineLabel();

        // We only run this block when the input was a dict (dictLocal non-null).
        // For non-dict descriptor sources we fall through to the apply step.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, validationEndLabel);

        // Rule (a): if new specifies configurable=true → throw.
        var configKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloca, configKeyLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        var checkEnumerableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, checkEnumerableLabel);
        il.Emit(OpCodes.Ldloc, configKeyLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brtrue, throwRedefineLabel);
        il.MarkLabel(checkEnumerableLabel);

        // Rule (b): if new specifies enumerable AND it differs from existing → throw.
        var enumKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldloca, enumKeyLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        var checkTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, checkTypeLabel);
        il.Emit(OpCodes.Ldloc, enumKeyLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, throwRedefineLabel);
        il.MarkLabel(checkTypeLabel);

        // Rule (c): accessor↔data type swap. Existing is accessor if Getter
        // OR Setter is non-null. New is accessor if it specifies "get" or "set".
        var existingIsAccessor = il.DeclareLocal(_types.Boolean);
        var notExistingAccessor = il.DefineLabel();
        var setExistingAccessor = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, setExistingAccessor);
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notExistingAccessor);
        il.MarkLabel(setExistingAccessor);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, existingIsAccessor);
        var afterExistingAccessor = il.DefineLabel();
        il.Emit(OpCodes.Br, afterExistingAccessor);
        il.MarkLabel(notExistingAccessor);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, existingIsAccessor);
        il.MarkLabel(afterExistingAccessor);

        var newIsAccessor = il.DeclareLocal(_types.Boolean);
        var newIsData = il.DeclareLocal(_types.Boolean);
        var setNewAccessor = il.DefineLabel();
        var afterNewAccessor = il.DefineLabel();
        var tmpVal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Ldloca, tmpVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brtrue, setNewAccessor);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Ldloca, tmpVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, afterNewAccessor);
        il.MarkLabel(setNewAccessor);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, newIsAccessor);
        il.MarkLabel(afterNewAccessor);

        var setNewData = il.DefineLabel();
        var afterNewData = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, tmpVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brtrue, setNewData);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloca, tmpVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, afterNewData);
        il.MarkLabel(setNewData);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, newIsData);
        il.MarkLabel(afterNewData);

        // Type swap: existing accessor + new data → throw. Existing data + new
        // accessor → throw. (Same descriptor type required when configurable=false.)
        var typeSwapDoneLabel = il.DefineLabel();
        var existingIsDataLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingIsAccessor);
        il.Emit(OpCodes.Brfalse, existingIsDataLabel);
        // existing accessor: new data forbids it.
        il.Emit(OpCodes.Ldloc, newIsData);
        il.Emit(OpCodes.Brtrue, throwRedefineLabel);
        il.Emit(OpCodes.Br, typeSwapDoneLabel);
        il.MarkLabel(existingIsDataLabel);
        // existing data: new accessor forbids it.
        il.Emit(OpCodes.Ldloc, newIsAccessor);
        il.Emit(OpCodes.Brtrue, throwRedefineLabel);
        il.MarkLabel(typeSwapDoneLabel);

        // Accessor-redefine validation: when existing is accessor + new is
        // accessor + existing.configurable=false, ECMA-262 §10.1.6.3
        // ValidateAndApplyPropertyDescriptor step 7.b/7.c require:
        //   - if Desc has [[Get]] and !SameValue(Desc.[[Get]], current.[[Get]]) → throw
        //   - if Desc has [[Set]] and !SameValue(Desc.[[Set]], current.[[Set]]) → throw
        // Test262 15.2.3.6-4-{97,99,etc.} cover this. Without this check,
        // accessor descriptors silently accept incompatible redefines.
        var skipAccessorCheck = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingIsAccessor);
        il.Emit(OpCodes.Brfalse, skipAccessorCheck);
        // existing is accessor. Check new "get" / "set" if present.
        var accessorGetKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Ldloca, accessorGetKeyLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        var skipGetCheck = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipGetCheck);
        // SameValue(new.get, existing.get); throw if false.
        var existingGetterForCompare = il.DeclareLocal(_types.Object);
        var haveExistingGetter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, existingGetterForCompare);
        il.Emit(OpCodes.Ldloc, existingGetterForCompare);
        il.Emit(OpCodes.Brtrue, haveExistingGetter);
        // An omitted getter in a completed accessor descriptor has the
        // ECMAScript value undefined. Normalize the CLR null representation
        // before SameValue so `{ get: undefined }` is an allowed no-op.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, existingGetterForCompare);
        il.MarkLabel(haveExistingGetter);
        il.Emit(OpCodes.Ldloc, accessorGetKeyLocal);
        il.Emit(OpCodes.Ldloc, existingGetterForCompare);
        il.Emit(OpCodes.Call, runtime.ObjectIs);
        il.Emit(OpCodes.Brfalse, throwRedefineLabel);
        il.MarkLabel(skipGetCheck);
        var accessorSetKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Ldloca, accessorSetKeyLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        var skipSetCheck = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipSetCheck);
        var existingSetterForCompare = il.DeclareLocal(_types.Object);
        var haveExistingSetter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, existingSetterForCompare);
        il.Emit(OpCodes.Ldloc, existingSetterForCompare);
        il.Emit(OpCodes.Brtrue, haveExistingSetter);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, existingSetterForCompare);
        il.MarkLabel(haveExistingSetter);
        il.Emit(OpCodes.Ldloc, accessorSetKeyLocal);
        il.Emit(OpCodes.Ldloc, existingSetterForCompare);
        il.Emit(OpCodes.Call, runtime.ObjectIs);
        il.Emit(OpCodes.Brfalse, throwRedefineLabel);
        il.MarkLabel(skipSetCheck);
        il.MarkLabel(skipAccessorCheck);

        // Rule (d): data with existing.writable=false: cannot set writable=true.
        // (writable: false → true is forbidden when configurable=false.)
        // Existing is data when existingIsAccessor=false.
        var skipWritableCheck = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingIsAccessor);
        il.Emit(OpCodes.Brtrue, skipWritableCheck);
        // existing data. Check writable.
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipWritableCheck); // existing.writable=true → all OK
        // existing.writable=false. New specifies writable=true → throw.
        var writableKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloca, writableKeyLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        var checkValueChange = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, checkValueChange);
        il.Emit(OpCodes.Ldloc, writableKeyLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brtrue, throwRedefineLabel);
        il.MarkLabel(checkValueChange);
        // New specifies value != existing.value → throw (data with writable=false).
        // Skip the equality check when existing.value is null: the prior PDS
        // descriptor was installed without an explicit value (\`defineProperty\`
        // with {writable:false} alone, before any value was captured).
        // For arrays, \`length\` is special — its value lives on the List<object?>
        // itself, not the PDS slot. ECMA-262 §10.4.2.4 ArraySetLength compares
        // newLen to oldLen (the current list length), so override
        // existingValueForCompare with list.Count when target is List + "length".
        // Without this override, the back-filled \$Undefined would either
        // (a) skip the check (was previous fix — regressed 4-162 etc.) or
        // (b) compare against undefined and falsely throw on same-length redefine.
        var valueKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, valueKeyLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, skipWritableCheck);
        var existingValueForCompare = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, existingValueForCompare);

        // Array \`length\` special case: read list.Count for compare.
        var afterArrayLenOverride = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArrayStorage.Type);
        var arrayLenLocal = il.DeclareLocal(runtime.ArrayStorage.Type);
        il.Emit(OpCodes.Stloc, arrayLenLocal);
        il.Emit(OpCodes.Ldloc, arrayLenLocal);
        il.Emit(OpCodes.Brfalse, afterArrayLenOverride);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, afterArrayLenOverride);
        // existingValueForCompare = (double)array.[[ArrayLength]]
        il.Emit(OpCodes.Ldloc, arrayLenLocal);
        il.Emit(OpCodes.Callvirt, runtime.ArrayStorage.LongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, existingValueForCompare);
        il.MarkLabel(afterArrayLenOverride);

        il.Emit(OpCodes.Ldloc, existingValueForCompare);
        il.Emit(OpCodes.Brfalse, skipWritableCheck);  // null existing → skip
        // ECMA-262 SameValue (Object.is) not Object.Equals: distinguishes
        // +0 vs -0 (returns false) and equates NaN with itself (returns true).
        // Test262 15.2.3.6-4-87 asserts redefining {value:+0,writable:false}
        // with {value:-0} throws TypeError. runtime.ObjectIs implements proper
        // SameValue per §7.2.10.
        il.Emit(OpCodes.Ldloc, valueKeyLocal);
        il.Emit(OpCodes.Ldloc, existingValueForCompare);
        il.Emit(OpCodes.Call, runtime.ObjectIs);
        il.Emit(OpCodes.Brfalse, throwRedefineLabel);
        il.MarkLabel(skipWritableCheck);

        // Validation passed.
        il.Emit(OpCodes.Br, validationEndLabel);

        il.MarkLabel(throwRedefineLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot redefine property");

        il.MarkLabel(validationEndLabel);
    }

    /// <summary>
    /// Emits Object.getOwnPropertyDescriptor(obj, prop) - gets a property descriptor.
    /// Signature: object ObjectGetOwnPropertyDescriptor(object obj, object prop)
    /// Returns a JavaScript object with descriptor properties.
    /// </summary>
    private void DeclareObjectGetOwnPropertyDescriptor(
        TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.ObjectGetOwnPropertyDescriptor = typeBuilder.DefineMethod(
            "ObjectGetOwnPropertyDescriptor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
    }

    private void EmitObjectGetOwnPropertyDescriptor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.ObjectGetOwnPropertyDescriptor;

        var il = method.GetILGenerator();

        // NOTE: Spec ToObject step throws on null/undefined; we deliberately
        // skip that guard because too many test262 tests indirectly call this
        // function on `desc.get` where desc is undefined (e.g., when probing
        // built-ins we haven't installed descriptors for). Fail→RuntimeError
        // cascade was net -114 in a regen attempt; revert until built-in
        // descriptors are complete.

        var propNameLocal = il.DeclareLocal(_types.String);
        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var resultDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);
        var returnNullLabel = il.DefineLabel();
        var checkObjPropertyLabel = il.DefineLabel();
        var hasDescriptorLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // The orchestration owns shared result locals and exits. Receiver stages enter
        // empty, then either fall through empty, branch to hasDescriptorLabel/returnNullLabel
        // empty, or branch to endLabel with one result object. No exception region crosses
        // a stage boundary. Dispatch order determines descriptor precedence.
        EmitGetOwnDescriptorProxyReceiver(il, runtime);

        // ECMA-262 §7.3.5 + §19.1.2.4: when the property key is a Symbol,
        // look it up in the per-object symbol dict (same one that handles
        // `obj[Symbol.x]` index access). Required for prop-desc.js tests
        // that probe Symbol.match/matchAll/replace/search/split on
        // RegExp.prototype. Without this the ToJsString below throws
        // TypeError on every Symbol-keyed gOPD call.
        var notSymbolKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolKeyLabel);
        EmitSymbolKeyDescriptorLookup(il, runtime, descriptorLocal, hasDescriptorLabel);
        il.MarkLabel(notSymbolKeyLabel);

        // propName = $Runtime.ToJsString(prop) — spec ECMA-262 ToString. Honors
        // Array.prototype.toString (so `gOPD(obj, [1])` looks up "1", not "[1]"),
        // and avoids the prop.ToString() NRE for null.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, propNameLocal);

        EmitGetOwnDescriptorGlobalReceiver(
            il, runtime, propNameLocal, descriptorLocal, resultDictLocal, hasDescriptorLabel, returnNullLabel, endLabel);

        EmitGetOwnDescriptorArrayLength(il, runtime, propNameLocal, resultDictLocal, endLabel);

        // Try to get descriptor from $PropertyDescriptorStore
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);

        // If descriptor is not null, convert it to a JS object
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brtrue, hasDescriptorLabel);

        EmitGetOwnDescriptorRegExpReceiver(il, runtime, propNameLocal, resultDictLocal, endLabel);

        EmitGetOwnDescriptorFunctionReceiver(
            il, runtime, propNameLocal, resultDictLocal, returnNullLabel, endLabel);

        EmitGetOwnDescriptorConstructorReceiver(
            il, runtime, propNameLocal, resultDictLocal, returnNullLabel, endLabel);

        EmitGetOwnDescriptorIndexedReceiver(il, runtime, propNameLocal, resultDictLocal, returnNullLabel, endLabel);

        EmitGetOwnDescriptorMathReceiver(il, runtime, propNameLocal, resultDictLocal, returnNullLabel, endLabel);

        EmitGetOwnDescriptorJsonReceiver(il, runtime, propNameLocal, resultDictLocal, returnNullLabel, endLabel);

        EmitGetOwnDescriptorDictionaryReceiver(
            il, runtime, propNameLocal, resultDictLocal, valueLocal, returnNullLabel, endLabel);

        EmitGetOwnDescriptorLiteralAccessor(il, runtime, propNameLocal, resultDictLocal, valueLocal, endLabel);

        EmitGetOwnDescriptorFieldsReceiver(
            il, runtime, propNameLocal, resultDictLocal, valueLocal, returnNullLabel, endLabel);

        EmitStoredPropertyDescriptorResult(il, runtime, descriptorLocal, resultDictLocal, hasDescriptorLabel, endLabel);

        // returnNullLabel: return undefined
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Stack-effect: pops 0, returns from the enclosing method when the
    /// arg1 prop key is a Symbol. Reads <c>GetSymbolDict(arg0)</c> and, if
    /// the symbol resolves, builds a JS descriptor dict with
    /// <c>{value, writable:true, enumerable:false, configurable:true}</c>
    /// (the ECMA-262 §17 default for built-in data slots — matches
    /// RegExp.prototype's well-known-symbol-keyed methods). Returns
    /// undefined if the symbol isn't present in the dict — same semantics
    /// as the string-keyed PDS miss path below the call site.
    /// </summary>
    private void EmitSymbolKeyDescriptorLookup(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder descriptorLocal,
        Label hasDescriptorLabel)
    {
        var symDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var valueLocal = il.DeclareLocal(_types.Object);
        var resultDictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symDictLocal);

        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, foundLabel);

        // Not in user symbol-dict — return undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(foundLabel);
        // User-defined symbol properties may carry a full descriptor. Reuse the
        // ordinary descriptor-to-object builder in the enclosing method.
        var rawSymbolValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brfalse, rawSymbolValueLabel);
        il.Emit(OpCodes.Br, hasDescriptorLabel);
        il.MarkLabel(rawSymbolValueLabel);

        // Build descriptor dict — attributes for symbol-keyed entries match
        // the spec-standard built-in default {writable:true,
        // enumerable:false, configurable:true} (ECMA-262 §17). Exception:
        // @@toStringTag entries are {writable:false, enumerable:false,
        // configurable:true} per ECMA-262 §25.5.4 / §27.2.5.5 / §22.2.6.13.
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        // writable: false when key is Symbol.toStringTag, else true.
        var notToStringTagLabel = il.DefineLabel();
        var writableDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToStringTag);
        il.Emit(OpCodes.Bne_Un, notToStringTagLabel);
        // matches Symbol.toStringTag → writable:false
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        il.Emit(OpCodes.Br, writableDoneLabel);
        il.MarkLabel(notToStringTagLabel);
        EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
        il.MarkLabel(writableDoneLabel);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.defineProperties(obj, props) - defines multiple properties.
    /// Signature: object ObjectDefineProperties(object obj, object props)
    /// Iterates over keys of props dictionary and calls ObjectDefineProperty for each.
    /// </summary>
    private void EmitObjectDefineProperties(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectDefineProperties",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ObjectDefineProperties = method;

        var il = method.GetILGenerator();

        // ECMA-262 §20.1.2.3 step 1: If Type(O) is not Object, throw TypeError.
        // Covers null/undefined/primitives. 15.2.3.7-1-*.js verify.
        var dpsThrowLabel = il.DefineLabel();
        var dpsOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, dpsThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, dpsThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, dpsThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, dpsThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, dpsThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dpsThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, dpsThrowLabel);
        il.Emit(OpCodes.Br, dpsOkLabel);

        il.MarkLabel(dpsThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.defineProperties called on non-object");
        il.MarkLabel(dpsOkLabel);

        // ECMA-262 §20.1.2.3 step 2: Let props be ? ToObject(Properties).
        // ToObject throws TypeError for null/undefined. Tests 15.2.3.7-2-{1,2}
        // verify each.
        var dpsPropsOkLabel = il.DefineLabel();
        var dpsPropsThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, dpsPropsThrowLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, dpsPropsThrowLabel);
        il.Emit(OpCodes.Br, dpsPropsOkLabel);

        il.MarkLabel(dpsPropsThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(dpsPropsOkLabel);

        // Use the same generic enumerable-own-key abstraction as Object.keys.
        // The previous implementation unwrapped only $Object and otherwise
        // assumed Dictionary<string, object?>, which made boxed strings,
        // functions, Errors, Dates, RegExps, and singleton objects either no-op
        // or enter an invalid PDS-extra path. GetKeys already unifies backing
        // dictionaries, $IHasFields, indexed carriers, and descriptor-only own
        // properties while filtering non-enumerable keys.
        var keysLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.Object);
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetKeys);
        il.Emit(OpCodes.Stloc, keysLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, keyLocal);

        // DefinePropertyOrThrow(target, key, ToPropertyDescriptor(Get(props,key))).
        // GetProperty is essential here: accessor-valued entries must invoke
        // their getter with the original Properties object as receiver.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ObjectDefineProperty);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);
        il.MarkLabel(loopEndLabel);

        // Return obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.getOwnPropertyDescriptors(obj) - gets all own property descriptors.
    /// Signature: object ObjectGetOwnPropertyDescriptors(object obj)
    /// Iterates over keys and calls ObjectGetOwnPropertyDescriptor for each, collecting into a new dict.
    /// </summary>
    private void EmitObjectGetOwnPropertyDescriptors(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectGetOwnPropertyDescriptors",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ObjectGetOwnPropertyDescriptors = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var keysLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.Object);
        var descLocal = il.DeclareLocal(_types.Object);

        // ECMA-262 §20.1.2.3 step 1: ToObject throws for null/undefined.
        // Do this before allocating/enumerating so the guest observes a
        // TypeError rather than a host NullReferenceException wrapped as Error.
        var descriptorsReceiverThrowLabel = il.DefineLabel();
        var descriptorsReceiverContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, descriptorsReceiverThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, descriptorsReceiverContinueLabel);
        il.MarkLabel(descriptorsReceiverThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(descriptorsReceiverContinueLabel);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Obtain the complete [[OwnPropertyKeys]] list exactly once. The Proxy
        // branch preserves trap order (including interleaved string/Symbol
        // results); ordinary objects use the shared string-then-Symbol helper.
        var proxyKeysLabel = il.DefineLabel();
        var ordinaryKeysLabel = il.DefineLabel();
        var keysReadyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0),
            proxyKeysLabel, ordinaryKeysLabel);
        il.MarkLabel(proxyKeysLabel);
        EmitProxyOwnKeysCompiledCall(
            il, runtime, () => il.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Stloc, keysLocal);
        il.Emit(OpCodes.Br, keysReadyLabel);
        il.MarkLabel(ordinaryKeysLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetOrdinaryOwnPropertyKeys);
        il.Emit(OpCodes.Stloc, keysLocal);
        il.MarkLabel(keysReadyLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var skipNullLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var storedLabel = il.DefineLabel();
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Brfalse, skipNullLabel);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, skipNullLabel);

        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, storedLabel);

        il.MarkLabel(symbolKeyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
        il.MarkLabel(storedLabel);

        il.MarkLabel(skipNullLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
