using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Requires and preserves an empty IL stack on fallthrough. Owns scratch locals and internal labels.
    private void EmitDefinePropertyExtensibilityValidation(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal)
    {
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
    }

    // Returns the existing/synthesized descriptor local. Owns the common synthesis exit;
    // receiver stages branch there with an empty stack. Lookup precedes classification and merge.
    private LocalBuilder EmitDefinePropertyExistingDescriptor(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal)
    {
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
        EmitDefinePropertyExistingArrayLength(
            il, runtime, propNameLocal, existingDescLocal, skipSynthExistingLabel);

        EmitDefinePropertyExistingFunctionPrototype(
            il, runtime, propNameLocal, existingDescLocal, skipSynthExistingLabel);

        EmitDefinePropertyExistingRegExpLastIndex(
            il, runtime, propNameLocal, existingDescLocal, skipSynthExistingLabel);

        var receiverIsSynthableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, receiverIsSynthableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, receiverIsSynthableLabel);
        // Compact records and other emitted ordinary-object carriers keep
        // their live own slots behind $IHasFields.  A partial descriptor such
        // as `{ writable: false }` must preserve that existing slot value just
        // as it does for Dictionary/$Object receivers.  Without this synthesis
        // the new PDS descriptor shadows the live value with undefined.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, synthArrayIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, synthListIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipSynthExistingLabel);
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
        return existingDescLocal;
    }

    // On a match writes existingDescLocal and branches to the caller-owned empty-stack exit.
    // Otherwise falls through with an empty stack; internal labels are private to this stage.
    private void EmitDefinePropertyExistingArrayLength(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder existingDescLocal,
        Label skipSynthExistingLabel)
    {
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
    }

    // Same synthesis contract: writes existingDescLocal on a match, then branches with an empty stack.
    private void EmitDefinePropertyExistingFunctionPrototype(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder existingDescLocal,
        Label skipSynthExistingLabel)
    {
        // User constructor functions also have an intrinsic descriptor that
        // is not initially stored in PDS: { writable:true, enumerable:false,
        // configurable:false }. Preserve those attributes when OrdinarySet
        // reaches DefineOwnProperty through a Proxy receiver.
        var notIntrinsicFunctionPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notIntrinsicFunctionPrototypeLabel);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notIntrinsicFunctionPrototypeLabel);
        var intrinsicFunctionPrototypeValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, intrinsicFunctionPrototypeValueLocal);
        il.Emit(OpCodes.Ldloc, intrinsicFunctionPrototypeValueLocal);
        il.Emit(OpCodes.Brfalse, notIntrinsicFunctionPrototypeLabel);
        il.Emit(OpCodes.Ldloc, intrinsicFunctionPrototypeValueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notIntrinsicFunctionPrototypeLabel);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, intrinsicFunctionPrototypeValueLocal);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Stloc, existingDescLocal);
        il.Emit(OpCodes.Br, skipSynthExistingLabel);
        il.MarkLabel(notIntrinsicFunctionPrototypeLabel);
    }

    // Same synthesis contract. Feature gating and live lastIndex lookup stay within this stage.
    private void EmitDefinePropertyExistingRegExpLastIndex(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder existingDescLocal,
        Label skipSynthExistingLabel)
    {
        // RegExp instances likewise expose intrinsic lastIndex outside PDS.
        // Its descriptor is writable but non-enumerable/non-configurable.
        if (_features.UsesRegExp)
        {
            var notIntrinsicRegExpLastIndexLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notIntrinsicRegExpLastIndexLabel);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notIntrinsicRegExpLastIndexLabel);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt,
                runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt,
                runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt,
                runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Stloc, existingDescLocal);
            il.Emit(OpCodes.Br, skipSynthExistingLabel);
            il.MarkLabel(notIntrinsicRegExpLastIndexLabel);
        }
    }

    // Returns classification locals consumed by merging after redefinition validation.
    // Requires/preserves an empty stack and rejects mixed data/accessor descriptors.
    private (LocalBuilder IsAccessor, LocalBuilder IsData) EmitDefinePropertyDescriptorClassification(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder dictLocal,
        MethodInfo dictTryGetValue)
    {
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
        return (newIsAccessorOuter, newIsDataOuter);
    }

    // Reads classification and existing fields; mutates only descriptorLocal.
    // Owns merge labels/scratch locals and preserves the empty stack.
    private void EmitDefinePropertyDescriptorMerge(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder dictLocal,
        LocalBuilder descriptorLocal,
        LocalBuilder existingDescLocal,
        MethodInfo dictTryGetValue,
        LocalBuilder newIsAccessorOuter,
        LocalBuilder newIsDataOuter)
    {
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
    }

    // Requires and preserves an empty IL stack on fallthrough. Owns scratch locals and internal labels.
    // Runs after storing attributes and before accessor storage cleanup.
    private void EmitDefinePropertyArrayIndexLengthGrowth(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal)
    {
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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, definedArrayIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipArrayIndexLengthGrowth);
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
    }

    // Returns from the emitted method after cleaning accessor backing storage.
    // Only data descriptors fall through, with an empty stack. All labels/locals are owned here.
    private void EmitDefinePropertyAccessorStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder descriptorLocal)
    {
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
        var accessorDictIndexLocal = il.DeclareLocal(_types.UInt32);
        var accessorDictKeepPlaceholderLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, accessorDictIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, accessorDictKeepPlaceholderLabel);
        // Numeric accessor keys are read through GetIndex hot paths that probe
        // dictionary storage before PDS. Remove their stale backing value.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(accessorDictKeepPlaceholderLabel);
        // Keep an undefined backing placeholder. Dictionary insertion order is
        // our ordinary-object creation-order ledger; removing accessor keys
        // loses their position when a later defineProperty converts them back
        // to data properties. GetProperty consults PDS accessors first, so the
        // placeholder is never observable as the property's value.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
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
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(accessorCleanupNotTSObject);

        if (runtime.JsonScalarRecordType is not null)
        {
            var accessorCleanupNotScalar = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CompactObjectRecordInterface);
            il.Emit(OpCodes.Brfalse, accessorCleanupNotScalar);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
            il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryStringObject, "set_Item"));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(accessorCleanupNotScalar);
        }

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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, accessorArrayIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, accessorCleanupNotArrayIndex);
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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, accessorListIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, accessorCleanupReturn);
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
    }

    // Owns data-value normalization and the shared storage-completion labels.
    // Receiver writers branch to endLabel (or skipValueSetLabel) with an empty stack;
    // this stage marks both labels and falls through empty to the caller's receiver return.
    private void EmitDefinePropertyDataStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder dictLocal,
        LocalBuilder descriptorLocal,
        MethodInfo dictTryGetValue)
    {
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

        EmitDefinePropertyDictionaryStorage(il, propNameLocal, valueToWriteLocal, wasGenericLocal, endLabel);

        EmitDefinePropertyArrayLengthStorage(
            il, runtime, propNameLocal, dictLocal, dictTryGetValue, valueToWriteLocal, wasGenericLocal, endLabel);

        EmitDefinePropertyArrayIndexStorage(
            il, runtime, propNameLocal, valueToWriteLocal, wasGenericLocal, endLabel);

        EmitDefinePropertyListIndexStorage(
            il, runtime, propNameLocal, valueToWriteLocal, wasGenericLocal, endLabel);

        EmitDefinePropertyCompactStorage(il, runtime, propNameLocal, valueToWriteLocal, wasGenericLocal, endLabel);

        EmitDefinePropertyObjectStorage(
            il, runtime, propNameLocal, valueToWriteLocal, wasGenericLocal, endLabel, skipValueSetLabel);

        il.MarkLabel(skipValueSetLabel);

        il.MarkLabel(endLabel);
    }

    // Writes dictionary storage or preserves a generic existing value, then branches to endLabel.
    // Entry, fallthrough, and the caller-owned completion label all have an empty stack.
    private void EmitDefinePropertyDictionaryStorage(
        ILGenerator il,
        LocalBuilder propNameLocal,
        LocalBuilder valueToWriteLocal,
        LocalBuilder wasGenericLocal,
        Label endLabel)
    {
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
    }

    // Applies only an explicitly supplied, already-coerced array length.
    // Branches to the caller-owned empty-stack completion on a match; otherwise falls through empty.
    private void EmitDefinePropertyArrayLengthStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder dictLocal,
        MethodInfo dictTryGetValue,
        LocalBuilder valueToWriteLocal,
        LocalBuilder wasGenericLocal,
        Label endLabel)
    {
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
    }

    // Writes an own array index without ordinary Set semantics.
    // Completion branches and fallthrough preserve the empty stack; scratch state is local.
    private void EmitDefinePropertyArrayIndexStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder valueToWriteLocal,
        LocalBuilder wasGenericLocal,
        Label endLabel)
    {
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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, arrIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notArrayIdxLabel);

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
    }

    // Writes arguments/legacy-list backing storage; owns index validation locals and labels.
    // Completion branches and fallthrough preserve the empty stack.
    private void EmitDefinePropertyListIndexStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder valueToWriteLocal,
        LocalBuilder wasGenericLocal,
        Label endLabel)
    {
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
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Ldloca, listIdxWriteLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notListIdxLabel);
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
    }

    // Materializes compact fields only when this receiver matches.
    // Completion branches and fallthrough preserve the empty stack.
    private void EmitDefinePropertyCompactStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder valueToWriteLocal,
        LocalBuilder wasGenericLocal,
        Label endLabel)
    {
        // The compact scalar carrier is an ordinary object whose canonical
        // mutable representation is its lazily materialized Fields dictionary.
        // Apply data descriptors there just as for $Object/dictionary targets.
        if (runtime.JsonScalarRecordType is not null)
        {
            var notScalarForValueLabel = il.DefineLabel();
            var scalarFieldsLocal =
                il.DeclareLocal(_types.DictionaryStringObject);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CompactObjectRecordInterface);
            il.Emit(OpCodes.Brfalse, notScalarForValueLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
            il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
            il.Emit(OpCodes.Stloc, scalarFieldsLocal);
            var scalarDoWriteLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, wasGenericLocal);
            il.Emit(OpCodes.Brfalse, scalarDoWriteLabel);
            il.Emit(OpCodes.Ldloc, scalarFieldsLocal);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brtrue, endLabel);
            il.MarkLabel(scalarDoWriteLabel);
            il.Emit(OpCodes.Ldloc, scalarFieldsLocal);
            il.Emit(OpCodes.Ldloc, propNameLocal);
            il.Emit(OpCodes.Ldloc, valueToWriteLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryStringObject, "set_Item"));
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(notScalarForValueLabel);
        }
    }

    // Final receiver writer: branches to a caller-owned completion label on every path.
    // Both destinations require an empty stack; internal locals/labels belong here.
    private void EmitDefinePropertyObjectStorage(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder propNameLocal,
        LocalBuilder valueToWriteLocal,
        LocalBuilder wasGenericLocal,
        Label endLabel,
        Label skipValueSetLabel)
    {
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
    }
}
