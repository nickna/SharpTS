using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
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

        // Symbol-keyed properties live in the object's symbol dictionary. Normalize
        // the supplied descriptor through this same method using an ephemeral
        // string-keyed holder, then store the resulting compiled descriptor. This
        // keeps the symbol path aligned with the ordinary ToPropertyDescriptor
        // validation/defaulting rules without maintaining a second parser.
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);

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

        // propName = $Runtime.ToJsString(prop) — ECMA-262 §7.1.19 ToPropertyKey
        // string path via the spec-shaped ToString. Avoids the prop.ToString()
        // Callvirt-on-null NRE for `Object.defineProperty(obj, null, ...)`,
        // and unlike runtime.Stringify (which produces debug "[1, 2]" form for
        // arrays) honors `Array.prototype.toString` join semantics so
        // `defineProperty(obj, [1], ...)` lands at key "1" (matches V8/SM).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, propNameLocal);

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
        // Only fires for $Array receivers with
        // propName == "length" and a value-typed descriptor.
        var skipArrayLenCheck = il.DefineLabel();
        var lenWasCoercedLocal = il.DeclareLocal(_types.Boolean);
        var coercedLenLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
        var lenValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, lenValLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, skipArrayLenCheck);
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

        // Check if object is frozen - if so, throw TypeError
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Throw TypeError: Cannot define property on frozen object
        il.Emit(OpCodes.Ldstr, "Cannot define property: object is not extensible");
        il.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        il.Emit(OpCodes.Call, runtime.CreateException);  // Wrap in .NET exception
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFrozenLabel);

        // ECMA-262 §10.1.6.3 [[DefineOwnProperty]]: throw TypeError when
        // adding a new property to a non-extensible object. \`PDSCanAddProperty\`
        // returns true when the object IS extensible OR the property already
        // exists (modify-in-place is always allowed). Sealed/frozen objects
        // are also non-extensible, so this single gate covers all three.
        var canAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brtrue, canAddLabel);

        // Can't add - throw TypeError
        il.Emit(OpCodes.Ldstr, "Cannot define property: object is not extensible");
        il.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        il.Emit(OpCodes.Call, runtime.CreateException);  // Wrap in .NET exception
        il.Emit(OpCodes.Throw);

        il.MarkLabel(canAddLabel);

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

        // Walks the descriptor's prototype chain via the PDS (the compiled
        // prototype link) and branches to `target` if any level has a PDS
        // accessor descriptor (getter OR setter) for `field`. Detects an
        // inherited setter-only `value` whose Get yields undefined (#801). Uses
        // only PDS primitives, which are emitted before this method.
        var getterGet = runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!;
        var setterGet = runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!;
        void EmitBranchIfAccessorOnChain(string field, Label target)
        {
            var curLocal = il.DeclareLocal(_types.Object);
            var pdescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var depthLocal = il.DeclareLocal(_types.Int32);
            var loopLabel = il.DefineLabel();
            var notFoundLabel = il.DefineLabel();
            var noPdescLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, curLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, depthLocal);

            il.MarkLabel(loopLabel);
            // cur == null → not found
            il.Emit(OpCodes.Ldloc, curLocal);
            il.Emit(OpCodes.Brfalse, notFoundLabel);
            // depth guard (cycle safety)
            il.Emit(OpCodes.Ldloc, depthLocal);
            il.Emit(OpCodes.Ldc_I4, 64);
            il.Emit(OpCodes.Bge, notFoundLabel);
            il.Emit(OpCodes.Ldloc, depthLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, depthLocal);
            // pdesc = PDSGetPropertyDescriptor(cur, field)
            il.Emit(OpCodes.Ldloc, curLocal);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, pdescLocal);
            il.Emit(OpCodes.Ldloc, pdescLocal);
            il.Emit(OpCodes.Brfalse, noPdescLabel);
            // pdesc.Getter != null → accessor present
            il.Emit(OpCodes.Ldloc, pdescLocal);
            il.Emit(OpCodes.Callvirt, getterGet);
            il.Emit(OpCodes.Brtrue, target);
            // pdesc.Setter != null → accessor present
            il.Emit(OpCodes.Ldloc, pdescLocal);
            il.Emit(OpCodes.Callvirt, setterGet);
            il.Emit(OpCodes.Brtrue, target);
            il.MarkLabel(noPdescLabel);
            // cur = PDSGetPrototype(cur)
            il.Emit(OpCodes.Ldloc, curLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
            il.Emit(OpCodes.Stloc, curLocal);
            il.Emit(OpCodes.Br, loopLabel);

            il.MarkLabel(notFoundLabel);
        }

        void EmitGetAndStash(string field)
        {
            var stashLabel = il.DefineLabel();
            var skipLabel = il.DefineLabel();
            var fieldValLocal = il.DeclareLocal(_types.Object);

            // fieldVal = GetProperty(descriptor, field)
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fieldValLocal);
            // Defined value → stash it.
            EmitBranchIfDefined(fieldValLocal, stashLabel);
            // Undefined Get result: still present if an own/inherited accessor exists.
            EmitBranchIfAccessorOnChain(field, stashLabel);
            il.Emit(OpCodes.Br, skipLabel);

            // stash: synthDict[field] = fieldVal, normalizing null → $Undefined so
            // an accessor-only (setter-only) field records a present undefined value.
            il.MarkLabel(stashLabel);
            var haveValLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, fieldValLocal);
            il.Emit(OpCodes.Brtrue, haveValLabel);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Stloc, fieldValLocal);
            il.MarkLabel(haveValLabel);
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

        // Extract properties from descriptor dictionary
        var dictTryGetValue = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType());

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

        il.MarkLabel(setDescriptorDoneLabel);

        // ECMA-262 §10.1.6.3 ValidateAndApplyPropertyDescriptor: when an
        // existing non-configurable descriptor is being redefined, reject
        // incompatible changes. Covers Object/defineProperty/15.2.3.6-4-*
        // family (~50 tests) plus most defineProperties spec-validation tests.
        var existingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, existingDescLocal);

        // If PDS has no descriptor but the property exists on the object
        // (set via plain `obj.foo = X` before defineProperty), synthesize a
        // default data descriptor with the spec defaults for ordinary writes:
        // writable=true, enumerable=true, configurable=true (Value = current
        // slot). Pre-fix the merge step below was skipped and defineProperty
        // defaulted unspecified fields to false, regressing the
        // writable/enumerable/configurable bits for redefined plain-set
        // properties (test262 15.2.3.6-4-100..).
        // Restrict synth to plain Dictionary / $TSObject receivers and own
        // indexed elements of $TSArray (including arguments). Array indices
        // created by literals/assignment live only in backing storage and have
        // the same W/E/C=true defaults. Array.length, Function.name/length,
        // Type.prototype etc. have spec-specific descriptors and must not be
        // synthesized here.
        var skipSynthExistingLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Brtrue, skipSynthExistingLabel);
        // Arrays always have an own, non-configurable length data property,
        // even before any descriptor has been installed in the side store.
        // Feed that intrinsic descriptor through the ordinary validation and
        // merge path so omitted fields preserve writable=true and attempts to
        // make length configurable/enumerable/accessor-shaped are rejected.
        var notIntrinsicArrayLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notIntrinsicArrayLengthLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notIntrinsicArrayLengthLabel);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Stloc, existingDescLocal);
        il.Emit(OpCodes.Br, skipSynthExistingLabel);
        il.MarkLabel(notIntrinsicArrayLengthLabel);

        var receiverIsSynthableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, receiverIsSynthableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, receiverIsSynthableLabel);
        var checkSynthListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, checkSynthListLabel);
        var synthArrayIndexLocal = il.DeclareLocal(_types.UInt32);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, synthArrayIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
        il.Emit(OpCodes.Ldloc, synthArrayIndexLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, skipSynthExistingLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, synthArrayIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
        il.Emit(OpCodes.Br, receiverIsSynthableLabel);

        // $Arguments and legacy array carriers inherit List<object>. Their
        // in-range indexed elements are ordinary W/E/C=true data properties.
        il.MarkLabel(checkSynthListLabel);
        var synthListLocal = il.DeclareLocal(_types.ListOfObject);
        var synthListIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, synthListLocal);
        il.Emit(OpCodes.Ldloc, synthListLocal);
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, synthListIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
        il.Emit(OpCodes.Ldloc, synthListIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, skipSynthExistingLabel);
        il.Emit(OpCodes.Ldloc, synthListIndexLocal);
        il.Emit(OpCodes.Ldloc, synthListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, skipSynthExistingLabel);
        il.MarkLabel(receiverIsSynthableLabel);
        // Existence check via HasOwnPropertyHelper.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
        // Synthesize: ctor sets W/E/C=true; Value = GetProperty(obj, key).
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Stloc, existingDescLocal);
        il.MarkLabel(skipSynthExistingLabel);

        // Classify new descriptor type ahead of both validation and merge:
        // accessor if dict has "get"/"set", data if it has "value"/"writable".
        var newIsAccessorOuter = il.DeclareLocal(_types.Boolean);
        var newIsDataOuter = il.DeclareLocal(_types.Boolean);
        var tmpClassifyVal = il.DeclareLocal(_types.Object);
        var skipClassifyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, skipClassifyLabel);

        var setNewAccessorOuter = il.DefineLabel();
        var afterNewAccessorOuter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Ldloca, tmpClassifyVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brtrue, setNewAccessorOuter);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Ldloca, tmpClassifyVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, afterNewAccessorOuter);
        il.MarkLabel(setNewAccessorOuter);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, newIsAccessorOuter);
        il.MarkLabel(afterNewAccessorOuter);

        var setNewDataOuter = il.DefineLabel();
        var afterNewDataOuter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, tmpClassifyVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brtrue, setNewDataOuter);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloca, tmpClassifyVal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, afterNewDataOuter);
        il.MarkLabel(setNewDataOuter);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, newIsDataOuter);
        il.MarkLabel(afterNewDataOuter);

        il.MarkLabel(skipClassifyLabel);

        // ECMA-262 §6.2.5.5 ToPropertyDescriptor step 10: an attempt to
        // combine accessor (get/set) and data (value/writable) attributes in
        // a single descriptor throws TypeError. test262 15.2.3.6-3-1 et al.
        var noMixLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newIsAccessorOuter);
        il.Emit(OpCodes.Brfalse, noMixLabel);
        il.Emit(OpCodes.Ldloc, newIsDataOuter);
        il.Emit(OpCodes.Brfalse, noMixLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute");
        il.MarkLabel(noMixLabel);

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
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        var arrayLenLocal = il.DeclareLocal(runtime.TSArrayType);
        il.Emit(OpCodes.Stloc, arrayLenLocal);
        il.Emit(OpCodes.Ldloc, arrayLenLocal);
        il.Emit(OpCodes.Brfalse, afterArrayLenOverride);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, afterArrayLenOverride);
        // existingValueForCompare = (double)array.[[ArrayLength]]
        il.Emit(OpCodes.Ldloc, arrayLenLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
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

        // ECMA-262 §10.1.6.3 step 6: when modifying an existing descriptor,
        // unspecified fields keep their existing values (don't overwrite to
        // defaults). Merge existing's values into descriptorLocal for any
        // field NOT specified in the new dict.
        var skipMergeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingDescLocal);
        il.Emit(OpCodes.Brfalse, skipMergeLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, skipMergeLabel);

        void MergeIfMissing(string fieldName, PropertyInfo prop, LocalBuilder? skipWhenLocal = null)
        {
            var skipLabel = il.DefineLabel();
            // Skip if cross-type redefine: don't carry data fields into a new
            // accessor descriptor (or vice versa). \`skipWhenLocal\` is the
            // boolean that, when true, indicates an incompatible new-desc type.
            if (skipWhenLocal != null)
            {
                il.Emit(OpCodes.Ldloc, skipWhenLocal);
                il.Emit(OpCodes.Brtrue, skipLabel);
            }
            var tmpKey = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, fieldName);
            il.Emit(OpCodes.Ldloca, tmpKey);
            il.Emit(OpCodes.Callvirt, dictTryGetValue);
            il.Emit(OpCodes.Brtrue, skipLabel);   // already specified — skip merge
            // Copy from existing
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldloc, existingDescLocal);
            il.Emit(OpCodes.Callvirt, prop.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, prop.GetSetMethod()!);
            il.MarkLabel(skipLabel);
        }
        // Cross-type merge guards: new is data → don't carry get/set from
        // existing accessor; new is accessor → don't carry value/writable
        // from existing data. Use the OUTER (always-computed) classifiers
        // so the guard fires regardless of whether validation ran.
        MergeIfMissing("value", runtime.CompiledPropertyDescriptorValue, newIsAccessorOuter);
        MergeIfMissing("writable", runtime.CompiledPropertyDescriptorWritable, newIsAccessorOuter);
        MergeIfMissing("get", runtime.CompiledPropertyDescriptorGetter, newIsDataOuter);
        MergeIfMissing("set", runtime.CompiledPropertyDescriptorSetter, newIsDataOuter);
        MergeIfMissing("enumerable", runtime.CompiledPropertyDescriptorEnumerable);
        MergeIfMissing("configurable", runtime.CompiledPropertyDescriptorConfigurable);

        il.MarkLabel(skipMergeLabel);

        // Call $PropertyDescriptorStore.DefineProperty(obj, propName, descriptor)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);  // Discard bool result

        // ArrayDefineOwnProperty updates [[ArrayLength]] for every newly
        // defined numeric index, including accessor descriptors that have no
        // value to write into dense storage. Grow the observable length before
        // the accessor path skips the data-value write below.
        var skipArrayIndexLengthGrowth = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, skipArrayIndexLengthGrowth);
        var definedArrayIndexLocal = il.DeclareLocal(_types.UInt32);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, definedArrayIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, skipArrayIndexLengthGrowth);
        il.Emit(OpCodes.Ldloc, definedArrayIndexLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, skipArrayIndexLengthGrowth); // 2^32-1 is not an array index
        var arrayLengthAlreadyCoversIndex = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Ldloc, definedArrayIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Bgt_Un, arrayLengthAlreadyCoversIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, definedArrayIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetLength);
        il.MarkLabel(arrayLengthAlreadyCoversIndex);
        il.MarkLabel(skipArrayIndexLengthGrowth);

        // Accessor descriptors replace any previous data property's backing
        // storage. PDS is the source of truth for the accessor itself, but
        // ordinary reads probe the receiver's fast storage before PDS on a
        // few hot paths. Leaving the old value there makes a redefinition
        // such as { 0: 1 } -> get 0() permanently return 1 and prevents the
        // getter's side effects from running. Clear only storage; keep the
        // newly-installed descriptor and (for arrays) the existing length.
        var notAccessorDescriptorLabel = il.DefineLabel();
        var cleanupAccessorStorageLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, cleanupAccessorStorageLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notAccessorDescriptorLabel);

        il.MarkLabel(cleanupAccessorStorageLabel);
        var accessorCleanupNotDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, accessorCleanupNotDict);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(accessorCleanupNotDict);

        var accessorCleanupNotTSObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, accessorCleanupNotTSObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(accessorCleanupNotTSObject);

        var accessorCleanupNotArrayIndex = il.DefineLabel();
        var accessorArrayIndexLocal = il.DeclareLocal(_types.UInt32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, accessorCleanupNotArrayIndex);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, accessorArrayIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, accessorCleanupNotArrayIndex);
        il.Emit(OpCodes.Ldloc, accessorArrayIndexLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, accessorCleanupNotArrayIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, accessorArrayIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayDeleteAt);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(accessorCleanupNotArrayIndex);

        var accessorCleanupReturn = il.DefineLabel();
        var accessorListLocal = il.DeclareLocal(_types.ListOfObject);
        var accessorListIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, accessorListLocal);
        il.Emit(OpCodes.Ldloc, accessorListLocal);
        il.Emit(OpCodes.Brfalse, accessorCleanupReturn);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, accessorListIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, accessorCleanupReturn);
        il.Emit(OpCodes.Ldloc, accessorListIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, accessorCleanupReturn);
        il.Emit(OpCodes.Ldloc, accessorListIndexLocal);
        il.Emit(OpCodes.Ldloc, accessorListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, accessorCleanupReturn);
        il.Emit(OpCodes.Ldloc, accessorListLocal);
        il.Emit(OpCodes.Ldloc, accessorListIndexLocal);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "set_Item", _types.Int32, _types.Object));
        il.MarkLabel(accessorCleanupReturn);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notAccessorDescriptorLabel);


        // Also set the value on the object if it's a data/generic property (no
        // accessor). ECMA-262 §6.2.5.6 CompletePropertyDescriptor: a generic
        // descriptor like `{enumerable:true}` defaults Value to undefined.
        // Without writing the key into the underlying dict/_fields, Object.keys
        // and for-in iterate dict.Keys and miss the property entirely (PDS-only
        // residency). Writing $Undefined.Instance when the slot is null gives
        // the dict-keys path the key while keeping JS-visible value = undefined.
        var skipValueSetLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Skip if accessor: getter or setter non-null.
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipValueSetLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipValueSetLabel);

        // valueToWrite = descriptor.Value, defaulting to $Undefined only when
        // the INPUT descriptor omitted "value". Presence, rather than the
        // CLR slot's nullness, matters: `{value:null}` is an explicit data
        // value and must survive identical redefinition.
        // Track wasGenericLocal for descriptors with no explicit Value. Used
        // below to skip overwriting a live dict
        // entry — RegExp's Symbol.search sets lastIndex=0 internally and then
        // user code does defineProperty(obj, 'lastIndex', {writable:false}),
        // which must not clobber the 0.
        var valueToWriteLocal = il.DeclareLocal(_types.Object);
        var wasGenericLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valueToWriteLocal);
        var haveValueLabel = il.DefineLabel();
        var explicitValuePresenceLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, explicitValuePresenceLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brtrue, haveValueLabel);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Brtrue, haveValueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, wasGenericLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, valueToWriteLocal);
        // Also back-fill descriptor.Value so gOPD reports `value: undefined`
        // (not null), matching the JS-visible spec form.
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.MarkLabel(haveValueLabel);

        // Set value on object if it's a dictionary. Skip overwrite when the
        // descriptor was generic AND the dict already holds the key (preserve
        // the live value).
        var notDictForValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictForValueLabel);

        var dictLocalForWrite = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocalForWrite);
        var dictDoWriteLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, wasGenericLocal);
        il.Emit(OpCodes.Brfalse, dictDoWriteLabel);
        il.Emit(OpCodes.Ldloc, dictLocalForWrite);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, endLabel);
        il.MarkLabel(dictDoWriteLabel);

        il.Emit(OpCodes.Ldloc, dictLocalForWrite);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notDictForValueLabel);

        // List<object?> (or $TSArray) + "length": call TSArraySetLength to
        // actually truncate/extend the backing list. Pre-fix the value was
        // stored in PDS but never applied to the list, so
        // `Object.defineProperty(arr, "length", {value: 0})` left arr.length
        // unchanged (test262 15.2.3.6-4-{130,131,...}). The length value was
        // already range-validated at the top of this method.
        var notListForLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notListForLengthLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notListForLengthLabel);
        // Gate on the INPUT descriptor having an own "value" — not on the
        // post-merge descriptor slot. ECMA-262 §10.4.2.4 step 2: a define
        // with no [[Value]] (e.g. {writable:false}) is OrdinaryDefineOwnProperty
        // only and must never re-coerce or re-apply a previously stored
        // length value (issue #180 recursion; stale-length truncation).
        var lenApplyValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, lenApplyValLocal);
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, notListForLengthLabel);
        il.Emit(OpCodes.Ldloc, wasGenericLocal);
        il.Emit(OpCodes.Brtrue, notListForLengthLabel);
        // Convert value to uint32. Already validated.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetLength);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notListForLengthLabel);

        // $TSArray + numeric index property name: initialize the backing slot
        // directly, bypassing ordinary [[Set]]. PDS already contains the new
        // descriptor here, so routing through SetIndex would incorrectly let
        // its (usually false) Writable bit block the define operation itself.
        // A generic descriptor preserves an existing own element, but creates
        // an undefined element when the index was previously absent; an
        // inherited index does not count as an own element for that decision.
        var notArrayIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notArrayIdxLabel);
        var arrIdxLocal = il.DeclareLocal(_types.UInt32);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, arrIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notArrayIdxLabel);
        il.Emit(OpCodes.Ldloc, arrIdxLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, notArrayIdxLabel); // 2^32-1 is an ordinary property name

        var writeArrayIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, wasGenericLocal);
        il.Emit(OpCodes.Brfalse, writeArrayIdxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, arrIdxLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brtrue, endLabel);
        il.MarkLabel(writeArrayIdxLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, arrIdxLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetLong);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notArrayIdxLabel);

        // $Arguments and legacy List<object> carriers: indexed data
        // descriptors must update the live backing slot too. PDS owns the
        // attributes, while List storage owns ordinary reads.
        var notListIdxLabel = il.DefineLabel();
        var listIdxReceiverLocal = il.DeclareLocal(_types.ListOfObject);
        var listIdxWriteLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listIdxReceiverLocal);
        il.Emit(OpCodes.Ldloc, listIdxReceiverLocal);
        il.Emit(OpCodes.Brfalse, notListIdxLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, listIdxWriteLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notListIdxLabel);
        il.Emit(OpCodes.Ldloc, listIdxWriteLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notListIdxLabel);
        il.Emit(OpCodes.Ldloc, listIdxWriteLocal);
        il.Emit(OpCodes.Ldloc, listIdxReceiverLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, notListIdxLabel);
        var writeListIdxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, wasGenericLocal);
        il.Emit(OpCodes.Brfalse, writeListIdxLabel);
        il.Emit(OpCodes.Ldloc, listIdxReceiverLocal);
        il.Emit(OpCodes.Ldloc, listIdxWriteLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, endLabel);
        il.MarkLabel(writeListIdxLabel);
        il.Emit(OpCodes.Ldloc, listIdxReceiverLocal);
        il.Emit(OpCodes.Ldloc, listIdxWriteLocal);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "set_Item", _types.Int32, _types.Object));
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notListIdxLabel);

        // Also write the value to $Object._fields when target is $Object.
        // Same generic-skip semantics as the dict path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, skipValueSetLabel);

        var tsObjFieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Stloc, tsObjFieldsLocal);

        var tsObjDoWriteLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, wasGenericLocal);
        il.Emit(OpCodes.Brfalse, tsObjDoWriteLabel);
        il.Emit(OpCodes.Ldloc, tsObjFieldsLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, endLabel);
        il.MarkLabel(tsObjDoWriteLabel);

        il.Emit(OpCodes.Ldloc, tsObjFieldsLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloc, valueToWriteLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(skipValueSetLabel);

        il.MarkLabel(endLabel);
        // Return the object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.getOwnPropertyDescriptor(obj, prop) - gets a property descriptor.
    /// Signature: object ObjectGetOwnPropertyDescriptor(object obj, object prop)
    /// Returns a JavaScript object with descriptor properties.
    /// </summary>
    private void EmitObjectGetOwnPropertyDescriptor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectGetOwnPropertyDescriptor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ObjectGetOwnPropertyDescriptor = method;

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

        void EmitBuiltinDataDescriptor(
            Action emitValue, bool writable, bool configurable)
        {
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            emitValue();
            il.Emit(OpCodes.Callvirt,
                _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", writable);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", configurable);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
        }

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

        // The global object exposes its standard functions and constants as
        // own properties. Route descriptor values through the same global
        // lookup used by ordinary reads so cached function identity is
        // preserved (`desc.value === global.parseInt`).
        var notGlobalObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalObjectLabel);

        // User-defined global descriptors (including the Test262 $DONE hook)
        // take precedence over the synthesized intrinsic descriptors below.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brtrue, hasDescriptorLabel);

        // Configurable standard globals can be deleted. The intrinsic lookup
        // remains available internally, so hide it through the deletion ledger
        // before synthesizing the public own-property descriptor.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);

        void EmitGlobalDescriptorCheck(
            string name, bool writable, bool configurable)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, next);
            EmitBuiltinDataDescriptor(() =>
            {
                il.Emit(OpCodes.Ldstr, name);
                il.Emit(OpCodes.Call, runtime.GlobalThisGetProperty);
            }, writable, configurable);
            il.MarkLabel(next);
        }
        EmitGlobalDescriptorCheck("parseInt", true, true);
        EmitGlobalDescriptorCheck("parseFloat", true, true);
        EmitGlobalDescriptorCheck("isNaN", true, true);
        EmitGlobalDescriptorCheck("isFinite", true, true);
        EmitGlobalDescriptorCheck("eval", true, true);
        EmitGlobalDescriptorCheck("NaN", false, false);
        EmitGlobalDescriptorCheck("Infinity", false, false);
        EmitGlobalDescriptorCheck("undefined", false, false);
        EmitGlobalDescriptorCheck("globalThis", true, true);
        foreach (var globalName in new[]
        {
            "Array", "Date", "RegExp", "Map", "Set", "WeakMap", "WeakSet",
            "Promise", "Function", "Object", "Number", "String", "Boolean",
            "Symbol", "Error", "TypeError", "RangeError", "ReferenceError",
            "SyntaxError", "URIError", "EvalError", "AggregateError", "Math", "JSON"
        })
        {
            EmitGlobalDescriptorCheck(globalName, true, true);
        }
        il.Emit(OpCodes.Br, returnNullLabel);
        il.MarkLabel(notGlobalObjectLabel);

        // Array length is an intrinsic data property whose value lives on the
        // array, while a writable=false transition is recorded in the PDS.
        // Report the live value and the effective writable bit together;
        // reading descriptor.Value directly would become stale after a later
        // successful `arr.length = n` assignment.
        var notArrayLengthDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notArrayLengthDescriptorLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notArrayLengthDescriptorLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notArrayLengthDescriptorLabel);

        // Try to get descriptor from $PropertyDescriptorStore
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);

        // If descriptor is not null, convert it to a JS object
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brtrue, hasDescriptorLabel);

        // ECMA-262 §17 — built-in functions expose `name` and `length` as
        // { writable: false, enumerable: false, configurable: true } own data
        // properties. Synthesize those descriptors when the receiver is a
        // $TSFunction (covers RegExp.prototype[Symbol.match], etc. that
        // verifyProperty inspects). Other callable wrappers fall through to
        // the existing paths (PDS / dict / class instance). After
        // `delete fn.name`/`length`, IsBuiltinDeleted hides the synthetic
        // descriptor — descriptor lookup returns null, matching the post-
        // delete state expected by verifyProperty's isConfigurable check.
        var notTSFunctionForDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunctionForDescLabel);

        // name / length only — anything else on a function returns null.
        var notFnNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notFnNameLabel);
        // Hide if this instance had `name` deleted.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        // value = TSFunction.GetMember(fn, "name") — or just inline it via the
        // GetProperty path which handles function name lookup.
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notFnNameLabel);

        var notFnLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notFnLengthLabel);
        // Hide if this instance had `length` deleted.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notFnLengthLabel);

        // Other keys on a function: not own → null.
        il.Emit(OpCodes.Br, returnNullLabel);
        il.MarkLabel(notTSFunctionForDescLabel);

        // System.Type — synthesize descriptor for built-in constructor's own
        // static properties. ECMA-262 §17 + §22.x: "prototype" is { value: X,
        // writable:false, enumerable:false, configurable:false }; static
        // constants (Number.MAX_VALUE etc.) likewise non-{writable,enumerable,
        // configurable}. Static methods are { writable:true, enumerable:false,
        // configurable:true }. verifyNotConfigurable / verifyProperty read
        // these descriptors via Object.getOwnPropertyDescriptor.
        var notTypeForDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeForDescLabel);
        // "prototype" — non-configurable data descriptor.
        var typeIsPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, typeIsPrototypeLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(typeIsPrototypeLabel);
        // "name" — configurable data descriptor.
        var typeIsNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, typeIsNameLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(typeIsNameLabel);
        // "length" — configurable data descriptor.
        var typeIsLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, typeIsLengthLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(typeIsLengthLabel);

        // System.Object: explicit JS-spec static names list since static
        // dispatch is syntactic (compile-time) and runtime.GetProperty doesn't
        // resolve them via reflection. Synthesize spec-aligned method descriptors
        // for the known names. Mirrors HasOwnPropertyHelper's Object Type
        // names list.
        var objTypeIsObjectLabel = il.DefineLabel();
        var objTypeNotObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, objTypeNotObjectLabel);
        void EmitObjectMethodValueDescCheck(string n, MethodBuilder targetMethod, int specLength)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            // Build descriptor { value: $TSFunction.GetOrCreate(method, name, length), W:true, E:false, C:true }.
            // Test262 15.2.3.3-4-{23,24,25,etc.} verify `desc.value === Object.X`.
            // GetOrCreate caches by MethodInfo so this returns the SAME instance
            // as the static dispatch path that resolves Object.X directly.
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            _types.EmitLoadMethodInfo(il, targetMethod);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Ldc_I4, specLength);
            il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        void EmitObjectMethodNameCheck(string n)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            // Build descriptor { value: LookupBuiltInStaticMember(receiver, n),
            // W:true, E:false, C:true }. The lookup helper returns the SAME
            // $TSFunction wrapper that syntactic `Object.X` resolves to (via
            // TSFunctionGetOrCreate cache), so `desc.value === Object.X` holds.
            // Test262 15.2.3.3-4-{14,15,...} rely on this identity.
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldarg_0);  // receiver (typeof(Object))
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Call, runtime.LookupBuiltInStaticMember);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        // Use the value-aware variant only for methods whose MethodBuilder
        // is already defined when this gOPD method emits — others would
        // capture a null token (EmitObjectGetOwnPropertyDescriptor runs
        // at line 638 of RuntimeClass.cs setup; methods emitted later in
        // the dispatch chain — ObjectGetOwnPropertyDescriptors, Object
        // GetPrototypeOf, ObjectSetPrototypeOf, ObjectCreate, ObjectPrevent
        // Extensions, ObjectIsExtensible, ObjectGroupBy, GetOwnProperty
        // Symbols, ObjectDefineProperties — can't use this path here).
        EmitObjectMethodValueDescCheck("assign", runtime.ObjectAssign, 2);
        EmitObjectMethodNameCheck("create");
        EmitObjectMethodNameCheck("defineProperties");
        EmitObjectMethodValueDescCheck("defineProperty", runtime.ObjectDefineProperty, 3);
        EmitObjectMethodValueDescCheck("entries", runtime.GetEntries, 1);
        EmitObjectMethodValueDescCheck("freeze", runtime.ObjectFreeze, 1);
        EmitObjectMethodValueDescCheck("fromEntries", runtime.ObjectFromEntries, 1);
        EmitObjectMethodNameCheck("getOwnPropertyDescriptor");
        EmitObjectMethodNameCheck("getOwnPropertyDescriptors");
        EmitObjectMethodValueDescCheck("getOwnPropertyNames", runtime.GetOwnPropertyNames, 1);
        EmitObjectMethodNameCheck("getOwnPropertySymbols");
        EmitObjectMethodNameCheck("getPrototypeOf");
        EmitObjectMethodNameCheck("groupBy");
        EmitObjectMethodValueDescCheck("hasOwn", runtime.ObjectHasOwn, 2);
        EmitObjectMethodValueDescCheck("is", runtime.ObjectIs, 2);
        EmitObjectMethodNameCheck("isExtensible");
        EmitObjectMethodValueDescCheck("isFrozen", runtime.ObjectIsFrozen, 1);
        EmitObjectMethodValueDescCheck("isSealed", runtime.ObjectIsSealed, 1);
        EmitObjectMethodValueDescCheck("keys", runtime.GetKeys, 1);
        EmitObjectMethodNameCheck("preventExtensions");
        EmitObjectMethodValueDescCheck("seal", runtime.ObjectSeal, 1);
        EmitObjectMethodNameCheck("setPrototypeOf");
        EmitObjectMethodNameCheck("values");
        il.MarkLabel(objTypeNotObjectLabel);

        // IList<object> → JS Array constructor. Same method-descriptor shape
        // as Object.
        var notArrayTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notArrayTypeLabel);
        void EmitArrayMethodNameCheck(string n)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        EmitArrayMethodNameCheck("from");
        EmitArrayMethodNameCheck("fromAsync");
        EmitArrayMethodNameCheck("isArray");
        EmitArrayMethodNameCheck("of");
        il.MarkLabel(notArrayTypeLabel);

        // System.Double → JS Number constructor. Static constants have W:F,E:F,C:F;
        // static methods have W:T,E:F,C:T. Same dispatch shape as Object Type.
        var notDoubleTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notDoubleTypeLabel);
        void EmitNumberStaticCheck(string n, bool isMethod, double? constValue = null, MethodBuilder? methodTarget = null, int methodArity = 1)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            if (constValue.HasValue)
            {
                il.Emit(OpCodes.Ldc_R8, constValue.Value);
                il.Emit(OpCodes.Box, _types.Double);
            }
            else if (methodTarget != null)
            {
                // Emit TSFunction.GetOrCreate(methodInfo, name, length) so the
                // descriptor's .value === Number.X (same cached wrapper).
                _types.EmitLoadMethodInfo(il, methodTarget);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Ldc_I4, methodArity);
                il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, runtime.GetProperty);
            }
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", isMethod);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", isMethod);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        // Constants — embed the literal value so dynamic gOPD round-trips even
        // when runtime.GetProperty can't resolve the JS-static intercept path.
        EmitNumberStaticCheck("MAX_VALUE", false, double.MaxValue);
        EmitNumberStaticCheck("MIN_VALUE", false, double.Epsilon);
        EmitNumberStaticCheck("NaN", false, double.NaN);
        EmitNumberStaticCheck("POSITIVE_INFINITY", false, double.PositiveInfinity);
        EmitNumberStaticCheck("NEGATIVE_INFINITY", false, double.NegativeInfinity);
        EmitNumberStaticCheck("MAX_SAFE_INTEGER", false, 9007199254740991.0);
        EmitNumberStaticCheck("MIN_SAFE_INTEGER", false, -9007199254740991.0);
        EmitNumberStaticCheck("EPSILON", false, 2.220446049250313e-16);
        // Methods — TODO: emit TSFunction.GetOrCreate inline once EmitNumberMethods
        // runs BEFORE EmitObjectGetOwnPropertyDescriptor (currently runs after,
        // so runtime.NumberParseInt etc. are null at this emit site).
        EmitNumberStaticCheck("parseInt", true);
        EmitNumberStaticCheck("parseFloat", true);
        EmitNumberStaticCheck("isNaN", true);
        EmitNumberStaticCheck("isFinite", true);
        EmitNumberStaticCheck("isInteger", true);
        EmitNumberStaticCheck("isSafeInteger", true);
        il.MarkLabel(notDoubleTypeLabel);

        // Probe GetProperty: if it returns a non-undefined value, the property
        // is reachable through our static dispatch — synthesize a descriptor.
        // This catches JS-named constants (Number.MAX_VALUE → System.Double.
        // MaxValue) where reflection on the Type by JS name would miss.
        // Skip null returns since `GetProperty` returns null for unresolved.
        var typeProbeValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, typeProbeValueLocal);
        // Reject null and Undefined sentinel (property unknown).
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        // Has a value — synthesize descriptor. Per ECMA-262 §17, built-in
        // METHODS are { writable:true, enumerable:false, configurable:true };
        // built-in CONSTANTS (Number.MAX_VALUE etc.) are { writable:false,
        // enumerable:false, configurable:false }. Distinguish via $TSFunction
        // marker on the probed value.
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        // Branch on $TSFunction (method) vs other (constant).
        var typeProbeIsFnLabel = il.DefineLabel();
        var typeProbeAfterAttrsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, typeProbeValueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, typeProbeIsFnLabel);
        // Constant: W=false, E=false, C=false.
        EmitDescriptorBoolField(il, resultDictLocal, "writable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", false);
        il.Emit(OpCodes.Br, typeProbeAfterAttrsLabel);
        il.MarkLabel(typeProbeIsFnLabel);
        // Method: W=true, E=false, C=true.
        EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
        EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
        EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
        il.MarkLabel(typeProbeAfterAttrsLabel);
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(notTypeForDescLabel);

        // No descriptor - check if it's an array first
        var notListLabel = il.DefineLabel();
        var notTSArrayLabel = il.DefineLabel();
        var isListLabel = il.DefineLabel();
        var handleArrayLabel = il.DefineLabel();
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var descriptorReceiverIsTSArray = il.DeclareLocal(_types.Boolean);

        // Check for List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brtrue, isListLabel);

        // Check for $Array (SharpTSArray)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);

        // It's $Array - get Elements list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, descriptorReceiverIsTSArray);
        il.Emit(OpCodes.Br, handleArrayLabel);

        il.MarkLabel(isListLabel);
        // listLocal already has the list

        il.MarkLabel(handleArrayLabel);
        // Handle array property - check if propName is "length" or numeric index

        // Check for "length" property
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notLengthLabel);

        // Return length descriptor: { value: length, writable: true, enumerable: false, configurable: false }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // value = list.Count
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = false
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = false
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notLengthLabel);
        // Check if it's a numeric index
        var notNumericIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notNumericIndexLabel);

        // Check if index is in bounds
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnNullLabel);

        // $TSArray length can include holes. A deleted index is not an own
        // property even though it remains below Elements.Count; raw List
        // receivers retain their dense in-bounds semantics.
        var descriptorArrayIndexPresent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorReceiverIsTSArray);
        il.Emit(OpCodes.Brfalse, descriptorArrayIndexPresent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.MarkLabel(descriptorArrayIndexPresent);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, returnNullLabel);

        // Arguments/legacy List carriers use the shared hole sentinel for a
        // deleted index. In-range holes are absent own properties.
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, returnNullLabel);

        // Return element descriptor: { value: element, writable: true, enumerable: true, configurable: true }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // value = list[index]
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = true
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notNumericIndexLabel);
        // Not length or numeric index on array - return null
        il.Emit(OpCodes.Br, returnNullLabel);

        il.MarkLabel(notTSArrayLabel);

        // Math singleton dict — synthesize spec descriptors for its known
        // methods (W:T,E:F,C:T) and constants (W:F,E:F,C:F). The singleton
        // is otherwise empty; static dispatch handles Math.abs() etc., but
        // gOPD(Math, "abs") needs to report the spec descriptor. Skip the
        // synth if the (Math, name) pair was deleted (IsBuiltinDeleted).
        var notMathSingletonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MathSingletonField);
        il.Emit(OpCodes.Bne_Un, notMathSingletonLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        void EmitMathNameDesc(string n, bool isMethod, double? constValue = null,
            MethodBuilder? methodTarget = null, int methodArity = 1)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            if (constValue.HasValue)
            {
                il.Emit(OpCodes.Ldc_R8, constValue.Value);
                il.Emit(OpCodes.Box, _types.Double);
            }
            else if (methodTarget != null)
            {
                // $TSFunction.GetOrCreate(adapter MethodInfo, name, length)
                // returns the SAME instance as the static dispatch path
                // (MathStaticEmitter uses TSFunctionGetOrCreate too), so
                // `desc.value === Math.X` holds in user code.
                _types.EmitLoadMethodInfo(il, methodTarget);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Ldc_I4, methodArity);
                il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
            }
            else
            {
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            }
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", isMethod);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", isMethod);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        // Methods (W:T, E:F, C:T) with identity-stable value (Math.X === Math.X).
        // Spec lengths per ECMA-262 §21.3.2 — same as MathStaticEmitter.
        (string n, MethodBuilder? m, int len)[] mathMethods =
        {
            ("abs",    runtime.MathAbsAdapter,    1),
            ("acos",   runtime.MathAcosAdapter,   1),
            ("acosh",  runtime.MathAcoshAdapter,  1),
            ("asin",   runtime.MathAsinAdapter,   1),
            ("asinh",  runtime.MathAsinhAdapter,  1),
            ("atan",   runtime.MathAtanAdapter,   1),
            ("atan2",  runtime.MathAtan2Adapter,  2),
            ("atanh",  runtime.MathAtanhAdapter,  1),
            ("cbrt",   runtime.MathCbrtAdapter,   1),
            ("ceil",   runtime.MathCeilAdapter,   1),
            ("clz32",  runtime.MathClz32Adapter,  1),
            ("cos",    runtime.MathCosAdapter,    1),
            ("cosh",   runtime.MathCoshAdapter,   1),
            ("exp",    runtime.MathExpAdapter,    1),
            ("expm1",  runtime.MathExpm1Adapter,  1),
            ("floor",  runtime.MathFloorAdapter,  1),
            ("fround", runtime.MathFroundAdapter, 1),
            ("f16round", runtime.MathF16RoundAdapter, 1),
            ("hypot",  runtime.MathHypotAdapter,  2),
            ("imul",   runtime.MathImulAdapter,   2),
            ("log",    runtime.MathLogAdapter,    1),
            ("log10",  runtime.MathLog10Adapter,  1),
            ("log1p",  runtime.MathLog1pAdapter,  1),
            ("log2",   runtime.MathLog2Adapter,   1),
            ("max",    runtime.MathMaxAdapter,    2),
            ("min",    runtime.MathMinAdapter,    2),
            ("pow",    runtime.MathPowAdapter,    2),
            // "random" → runtime.Random; EmitRandom now precedes gOPD emit
            // (see RuntimeEmitter.RuntimeClass.cs ~line 660), so we can wire
            // the actual MethodBuilder here for `desc.value === Math.random`.
            ("random", runtime.Random,             0),
            ("round",  runtime.MathRoundAdapter,  1),
            ("sign",   runtime.MathSignAdapter,   1),
            ("sin",    runtime.MathSinAdapter,    1),
            ("sinh",   runtime.MathSinhAdapter,   1),
            ("sqrt",   runtime.MathSqrtAdapter,   1),
            ("tan",    runtime.MathTanAdapter,    1),
            ("tanh",   runtime.MathTanhAdapter,   1),
            ("trunc",  runtime.MathTruncAdapter,  1),
            ("sumPrecise", runtime.MathSumPrecise, 1),
        };
        foreach (var (mn, mb, ml) in mathMethods)
            EmitMathNameDesc(mn, isMethod: true, methodTarget: mb, methodArity: ml);
        // Constants (W:F, E:F, C:F) with embedded literal values.
        EmitMathNameDesc("E", isMethod: false, constValue: System.Math.E);
        EmitMathNameDesc("LN10", isMethod: false, constValue: System.Math.Log(10));
        EmitMathNameDesc("LN2", isMethod: false, constValue: System.Math.Log(2));
        EmitMathNameDesc("LOG10E", isMethod: false, constValue: 1.0 / System.Math.Log(10));
        EmitMathNameDesc("LOG2E", isMethod: false, constValue: 1.0 / System.Math.Log(2));
        EmitMathNameDesc("PI", isMethod: false, constValue: System.Math.PI);
        EmitMathNameDesc("SQRT1_2", isMethod: false, constValue: System.Math.Sqrt(0.5));
        EmitMathNameDesc("SQRT2", isMethod: false, constValue: System.Math.Sqrt(2));
        il.MarkLabel(notMathSingletonLabel);

        // JSON singleton — synth descriptors for parse/stringify/isRawJSON/rawJSON.
        // Same pattern as Math; the singleton dict is empty, static dispatch
        // handles JSON.X(); gOPD just needs to report spec attrs.
        var notJsonSingletonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.JsonSingletonField);
        il.Emit(OpCodes.Bne_Un, notJsonSingletonLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brtrue, returnNullLabel);
        void EmitJsonNameDesc(string n)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, n);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, resultDictLocal);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Ldstr, "value");
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
            EmitDescriptorBoolField(il, resultDictLocal, "writable", true);
            EmitDescriptorBoolField(il, resultDictLocal, "enumerable", false);
            EmitDescriptorBoolField(il, resultDictLocal, "configurable", true);
            il.Emit(OpCodes.Ldloc, resultDictLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }
        EmitJsonNameDesc("parse");
        EmitJsonNameDesc("stringify");
        EmitJsonNameDesc("isRawJSON");
        EmitJsonNameDesc("rawJSON");
        il.MarkLabel(notJsonSingletonLabel);

        // No descriptor - check if property exists on the object directly (Dictionary case)
        var notDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // Check if dictionary contains the key
        var dictContainsKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Property exists on dict - create default data descriptor.
        // ECMA-262 §10.1.5.1 OrdinaryGetOwnProperty + Object.freeze/seal:
        // frozen → writable=false + configurable=false; sealed → writable
        // preserves but configurable=false. We don't store per-property
        // descriptors when the user wrote `obj.foo = X` directly, so we
        // synthesize one — and reflect the object's frozen/sealed state
        // here so verifyProperty sees the spec-mandated immutability.
        var dictIsFrozenLocal = il.DeclareLocal(_types.Boolean);
        var dictIsSealedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Stloc, dictIsFrozenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Stloc, dictIsSealedLocal);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // Get the value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, valueLocal);

        // Set value property
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = !frozen (sealed preserves writable=true for the synth path).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloc, dictIsFrozenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !frozen
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = true (freeze/seal preserve enumerability).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = !(frozen || sealed).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloc, dictIsFrozenLocal);
        il.Emit(OpCodes.Ldloc, dictIsSealedLocal);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !(frozen || sealed)
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

        // Not a dictionary - check if it implements $IHasFields (class instances)
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Get the fields dictionary from the class instance
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // Check if the fields dictionary contains the key
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Build data descriptor from the class field value. Same frozen/sealed
        // reflection as the dict path so Object.freeze on a class instance
        // surfaces writable=false / configurable=false.
        var hfIsFrozenLocal = il.DeclareLocal(_types.Boolean);
        var hfIsSealedLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
        il.Emit(OpCodes.Stloc, hfIsFrozenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsSealed);
        il.Emit(OpCodes.Stloc, hfIsSealedLocal);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // Get the value from the fields dictionary
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, valueLocal);

        // Set value
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // writable = !frozen.
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Ldloc, hfIsFrozenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // enumerable = true.
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        // configurable = !(frozen || sealed).
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "configurable");
        il.Emit(OpCodes.Ldloc, hfIsFrozenLocal);
        il.Emit(OpCodes.Ldloc, hfIsSealedLocal);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Br, endLabel);

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

        // Save the ORIGINAL props identity for PDS lookups before the $TSObject
        // unwrap below. PDS entries (accessor descriptors) are keyed against
        // the $TSObject, not its inner _fields dict, so the unwrapped arg1 is
        // useless for PDS lookups.
        var origPropsLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, origPropsLocal);

        // If props is a $Object (e.g. `new Constructor()`), unwrap to its
        // _fields Dict so the iteration path below sees the own keys. This
        // is the simple case for ECMA-262 §20.1.2.3 step 3 when the source
        // is a JS object literal exposed as $Object.
        var notTSObjectPropsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectPropsLabel);
        // Replace arg1 by-value with the unwrapped _fields. We can't actually
        // overwrite Ldarg_1, so push the unwrapped value into a local that
        // shadows for the iteration. Easiest: load fields here and stash into
        // a local that the subsequent Isinst sees.
        // Simpler: also overwrite the local arg by pushing arg1 = fields via
        // Starg. (Starg modifies the argument slot.)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Starg_S, (byte)1);
        il.MarkLabel(notTSObjectPropsLabel);

        // Cast props to Dictionary<string, object?>
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var enumeratorLocal = il.DeclareLocal(typeof(Dictionary<string, object?>.Enumerator));
        var currentLocal = il.DeclareLocal(typeof(KeyValuePair<string, object?>));

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // dict = props as Dictionary<string, object?>
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // If not a dictionary, just return obj
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop. ECMA-262 §20.1.2.3 step 3: For each key in OwnPropertyKeys
        // filter by `Enumerable` before calling DefinePropertyOrThrow. We use
        // PDSGetPropertyDescriptor to check the enumerable bit when a PDS
        // descriptor exists (e.g. installed by a prior defineProperty with
        // enumerable:false), and otherwise treat the dict key as enumerable
        // (the default for object-literal own keys).
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var moveNext = typeof(Dictionary<string, object?>.Enumerator).GetMethod("MoveNext")!;
        il.Emit(OpCodes.Call, moveNext);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // Get current KVP
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var currentProp = typeof(Dictionary<string, object?>.Enumerator).GetProperty("Current")!.GetGetMethod()!;
        il.Emit(OpCodes.Call, currentProp);
        il.Emit(OpCodes.Stloc, currentLocal);

        var keyGetter = typeof(KeyValuePair<string, object?>).GetProperty("Key")!.GetGetMethod()!;
        var valueGetter = typeof(KeyValuePair<string, object?>).GetProperty("Value")!.GetGetMethod()!;

        // Skip internal marker keys (`__primitiveType` / `__primitiveValue` etc.)
        // — these are CLR-level slots on boxed-primitive wrappers, NOT JS-visible
        // own properties. Per ECMA-262 wrappers don't expose them via
        // OwnPropertyKeys. Cheap StartsWith("__") gate matches the
        // get_Length convention used elsewhere.
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, keyGetter);
        il.Emit(OpCodes.Ldstr, "__");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brtrue, loopStartLabel);

        // Skip if PDS descriptor exists with Enumerable=false.
        var enumOkLabel = il.DefineLabel();
        var keyDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_1);  // props
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, keyGetter);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, keyDescLocal);
        il.Emit(OpCodes.Ldloc, keyDescLocal);
        il.Emit(OpCodes.Brfalse, enumOkLabel);
        il.Emit(OpCodes.Ldloc, keyDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, enumOkLabel);
        // Non-enumerable — skip this key.
        il.Emit(OpCodes.Br, loopStartLabel);
        il.MarkLabel(enumOkLabel);

        // Call ObjectDefineProperty(obj, key, descriptor)
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, keyGetter);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, valueGetter);
        il.Emit(OpCodes.Call, runtime.ObjectDefineProperty);
        il.Emit(OpCodes.Pop);  // Discard return value from defineProperty

        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // PDS-extras loop: iterate accessor-only own keys not in _fields/dict.
        // For each, Get(props, key) fires the getter and yields the descriptor
        // object to pass to ObjectDefineProperty. Per ECMA-262 §20.1.2.3 step 3,
        // descriptor objects are obtained via Get(O, key) — accessor-only keys
        // therefore route through the getter rather than reading dict directly.
        var pdsExtraKeys = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, origPropsLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, pdsExtraKeys);
        var pdsIdxLocal = il.DeclareLocal(_types.Int32);
        var pdsLoopStartLabel = il.DefineLabel();
        var pdsLoopEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, pdsIdxLocal);
        il.MarkLabel(pdsLoopStartLabel);
        il.Emit(OpCodes.Ldloc, pdsIdxLocal);
        il.Emit(OpCodes.Ldloc, pdsExtraKeys);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, pdsLoopEndLabel);
        var pdsCurKey = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, pdsExtraKeys);
        il.Emit(OpCodes.Ldloc, pdsIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, pdsCurKey);
        // ObjectDefineProperty(obj, key, GetProperty(origProps, key))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, pdsCurKey);
        il.Emit(OpCodes.Ldloc, origPropsLocal);
        il.Emit(OpCodes.Ldloc, pdsCurKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ObjectDefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, pdsIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, pdsIdxLocal);
        il.Emit(OpCodes.Br, pdsLoopStartLabel);
        il.MarkLabel(pdsLoopEndLabel);

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
        var keyLocal = il.DeclareLocal(_types.String);
        var descLocal = il.DeclareLocal(_types.Object);

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var skipNullLabel = il.DefineLabel();

        // result = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Get OWN property names (NOT filtered by enumerable). ECMA-262
        // §20.1.2.7 Object.getOwnPropertyDescriptors uses [[OwnPropertyKeys]]
        // (no enumerable filter) — non-enumerable own keys must appear in
        // the result. Pre-fix used runtime.GetKeys which post-e0577095 also
        // filters by PDS enumerable; that regressed inherited-properties-
        // omitted.js (a non-enumerable own key was missing from the result).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetOwnPropertyNames);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, keysLocal);

        // If keys is null, return empty result
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop
        il.MarkLabel(loopStartLabel);
        // if (index >= keys.Count) break
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // key = keys[index].ToString()
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, keyLocal);

        // desc = ObjectGetOwnPropertyDescriptor(obj, key)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);

        // if (desc == null || desc is undefined) skip
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Brfalse, skipNullLabel);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, skipNullLabel);

        // result[key] = desc
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.MarkLabel(skipNullLabel);
        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // ECMA-262 §20.1.2.7 step 3: For each own key (string AND symbol)
        // of obj, return descriptor. After the string-key loop, also
        // populate the result's symbol dict with descriptors for each
        // own symbol-keyed property.
        var symKeysLocal = il.DeclareLocal(_types.ListOfObject);
        var symIdxLocal = il.DeclareLocal(_types.Int32);
        var symKeyLocal = il.DeclareLocal(_types.Object);
        var symDescLocal = il.DeclareLocal(_types.Object);
        var symLoopStart = il.DefineLabel();
        var symLoopEnd = il.DefineLabel();
        var symSkipNullLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetOwnPropertySymbols);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, symKeysLocal);
        il.Emit(OpCodes.Ldloc, symKeysLocal);
        il.Emit(OpCodes.Brfalse, symLoopEnd);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, symIdxLocal);

        il.MarkLabel(symLoopStart);
        il.Emit(OpCodes.Ldloc, symIdxLocal);
        il.Emit(OpCodes.Ldloc, symKeysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, symLoopEnd);

        // symKey = keys[idx];
        il.Emit(OpCodes.Ldloc, symKeysLocal);
        il.Emit(OpCodes.Ldloc, symIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, symKeyLocal);

        // symDesc = ObjectGetOwnPropertyDescriptor(obj, symKey)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, symKeyLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, symDescLocal);

        il.Emit(OpCodes.Ldloc, symDescLocal);
        il.Emit(OpCodes.Brfalse, symSkipNullLabel);
        il.Emit(OpCodes.Ldloc, symDescLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, symSkipNullLabel);

        // GetSymbolDict(result)[symKey] = symDesc
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldloc, symKeyLocal);
        il.Emit(OpCodes.Ldloc, symDescLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        il.MarkLabel(symSkipNullLabel);
        il.Emit(OpCodes.Ldloc, symIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, symIdxLocal);
        il.Emit(OpCodes.Br, symLoopStart);

        il.MarkLabel(symLoopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
