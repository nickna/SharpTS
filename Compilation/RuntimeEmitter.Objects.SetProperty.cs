using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

// Split out of RuntimeEmitter.Objects.Properties.cs (#1141): the property/index assignment emitters (sloppy + strict).
public partial class RuntimeEmitter
{
    private void EmitSetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // SetFieldsProperty(object obj, string name, object value) -> void
        // Updates class-instance state through emitted runtime state only:
        // descriptor store checks and emitted $Object fields.
        var method = typeBuilder.DefineMethod(
            "SetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object]
        );
        runtime.SetFieldsProperty = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var trySetterLabel = il.DefineLabel();

        // Declare locals upfront
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var frozenCheckLocal = il.DeclareLocal(_types.Object);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        // If frozen, silently return (non-strict mode behavior)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, frozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, endLabel); // Frozen - silently return

        // No additional setter fallback in standalone mode.
        il.MarkLabel(trySetterLabel);

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Found a non-null _fields dictionary
        // Check if sealed: _sealedObjects.TryGetValue(obj, out _)
        var doSetFieldLabel = il.DefineLabel();
        var checkExtensibilityLabel = il.DefineLabel();
        var sealedCheckLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, sealedCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, checkExtensibilityLabel); // Not sealed, check extensibility

        // Object is sealed - check if property exists in dictionary
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, endLabel); // Property doesn't exist on sealed object, silently return
        il.Emit(OpCodes.Br, doSetFieldLabel); // Property exists, proceed to set

        // Not sealed - check extensibility via $PropertyDescriptorStore - fully standalone, no reflection
        il.MarkLabel(checkExtensibilityLabel);
        il.Emit(OpCodes.Ldarg_0); // obj
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, endLabel); // Cannot add property, silently return

        // Set the value: dict[name] = value;
        il.MarkLabel(doSetFieldLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTSObjectLabel);

        // Check $IHasFields interface (covers user-defined classes)
        var notHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);

        // Check extensibility before setting (handles sealed/non-extensible objects)
        il.Emit(OpCodes.Ldarg_0); // obj
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, endLabel); // Cannot add property, silently return

        // Call interface method: ((IHasFields)obj).SetProperty(name, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHasFieldsLabel);

        // Check $Error - handle name, message, stack properties
        var notErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, notErrorLabel);

        // Check "name"
        var notErrorNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorNameSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorNameLabel);

        // Check "message"
        var notErrorMessageLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorMessageSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorMessageLabel);

        // Check "stack"
        var notErrorStackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "stack");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorStackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorStackSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorStackLabel);

        // Check "code"
        var notErrorCodeSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorCodeSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorCodeSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorCodeSetLabel);

        // Check "syscall"
        var notErrorSyscallSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "syscall");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorSyscallSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorSyscallSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorSyscallSetLabel);

        il.MarkLabel(notErrorLabel);

        // Scoped PDS-data-store fallback for ECMA built-ins: $TSDate, $TSRegExp,
        // $TSPromise, $TSError. JS allows ad-hoc property assignment on these
        // instances (`d = new Date(); d.foo = 1; d.foo === 1`); the value lands
        // in PDS so GetFieldsProperty's PDS-data-descriptor arm reads it back.
        // Limited to these types so user-defined class instances and runtime-
        // side types (which may rely on silent-no-op semantics for unknown
        // writes — e.g., the Debug npm package) are not affected.
        // Note: $TSError already has explicit name/message/stack/code/syscall
        // handlers above and only reaches the PDS path for OTHER property
        // names (like `obj.length`, `obj[0]`).
        var pdsStoreLabel = il.DefineLabel();
        var afterPdsStoreLabel = il.DefineLabel();
        if (_features.UsesDate)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSDateType);
            il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        }
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        il.Emit(OpCodes.Br, afterPdsStoreLabel);

        il.MarkLabel(pdsStoreLabel);
        {
            var fbDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, fbDescLocal);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(afterPdsStoreLabel);

        // Fallback: Try SetMember(string, object) method for types like $HttpResponse
        // that expose property setters through their SetMember dispatch method.
        var setMemberLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "SetMember");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, setMemberLocal);

        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call SetMember(name, value): methodInfo.Invoke(obj, new object[] { name, value })
        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
    /// <summary>
    /// Emits SetFieldsPropertyStrict(object obj, string name, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen objects.
    /// </summary>
    private void EmitSetFieldsPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetFieldsPropertyStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object, _types.Boolean]
        );
        runtime.SetFieldsPropertyStrict = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var notFrozenLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var frozenCheckLocal = il.DeclareLocal(_types.Object);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, frozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Frozen - check strictMode and throw if true
        il.Emit(OpCodes.Ldarg_3); // strictMode
        var frozenSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, frozenSilentLabel);
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");
        il.MarkLabel(frozenSilentLabel);
        il.Emit(OpCodes.Ret); // Silently return in non-strict mode

        il.MarkLabel(notFrozenLabel);

        // No reflection setter fallback in standalone mode.

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);

        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Found a non-null _fields dictionary - set the value and return
        // dict[name] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSObjectLabel);

        // Check $IHasFields interface (covers user-defined classes)
        var notHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);

        // Check extensibility before setting (handles sealed/non-extensible objects)
        il.Emit(OpCodes.Ldarg_0); // obj
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, endLabel); // Cannot add property, silently return

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHasFieldsLabel);

        // Check $Error - handle name, message, stack properties
        var notErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, notErrorLabel);

        var notErrorNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorNameSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorNameLabel);

        var notErrorMessageLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorMessageSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorMessageLabel);

        var notErrorStackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "stack");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorStackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorStackSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorStackLabel);

        il.MarkLabel(notErrorLabel);

        // Built-in exotic objects (Date / RegExp / Promise / Error) accept arbitrary
        // named property assignment per ECMA-262 [[Set]], stored as PDS data
        // descriptors so GetProperty / for-in / gOPD round-trip. Mirrors the
        // non-strict SetFieldsProperty PDS-store arm — without it these writes were
        // silently dropped under "use strict", so e.g. `var d = new Date(0);
        // d.enumerable = true;` (a defineProperty attributes object — Test262
        // Object/defineProperty 15.2.3.6-3-39 et al.) lost the field. Frozen /
        // extensibility throws were already handled at the top of this method.
        var fieldsPdsStoreLabel = il.DefineLabel();
        var afterFieldsPdsStoreLabel = il.DefineLabel();
        if (_features.UsesDate)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSDateType);
            il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        }
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        il.Emit(OpCodes.Br, afterFieldsPdsStoreLabel);

        il.MarkLabel(fieldsPdsStoreLabel);
        {
            var fbDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, fbDescLocal);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(afterFieldsPdsStoreLabel);

        // Fallback: Try SetMember(string, object) method for types like $HttpResponse
        var setMemberLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "SetMember");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, setMemberLocal);

        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call SetMember(name, value): methodInfo.Invoke(obj, new object[] { name, value })
        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
    private void EmitSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1.
        var method = (MethodBuilder)runtime.SetProperty;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var tsObjectLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        EmitGlobalThisSetRedirect(il, runtime);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxySetPropertyCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), () => il.Emit(OpCodes.Ldarg_2), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // $Object (with setter support) - call obj.SetProperty(name, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // $Array — special-case `arr.length = N` (route through $Array.SetLength
        // so truncation/extension uses the sparse-aware path). Other named-
        // property writes on arrays are silently ignored; ECMA-262 §22.1.5
        // permits them but most emitters don't preserve them and the spec
        // compiler tests don't exercise that corner. Must come BEFORE the
        // Dictionary check (since $Array inherits List<object?> which is
        // neither), and BEFORE SetFieldsProperty fallthrough.
        var tsArraySetPropLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArraySetPropLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $TSFunction — JS functions are objects and support arbitrary property assignment
        // (`fn.x = 42`, `lodash.chunk = function(...){}`). Store as a data descriptor in
        // $PropertyDescriptorStore; GetFunctionMethod's fallback path reads it back. Without
        // this, the assignment would fall through to SetFieldsProperty which is a
        // class-instance path that doesn't match $TSFunction.
        var tsFunctionSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionSetLabel);

        // $CJSModule — `module.exports = X` (or any aliased write) goes through here.
        // Only emit when UsesCjsRequire is on (matching the type emission gate).
        var cjsModuleSetLabel = il.DefineLabel();
        if (_features.UsesCjsRequire)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CjsModuleType);
            il.Emit(OpCodes.Brtrue, cjsModuleSetLabel);
        }

        // $RegExp — `r.lastIndex = value` stores the raw JS value. ToLength is
        // deferred until RegExpBuiltinExec observes it.
        var tsRegExpSetLabel = il.DefineLabel();
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, tsRegExpSetLabel);
        }

        // System.Type (class reference used as value, e.g. `Scalar.PLAIN = 'x'`). JS allows
        // arbitrary static property assignment on classes; we store them in PropertyDescriptorStore.
        var typeSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeSetLabel);

        // List<object?> — raw arrays from `[...]` literal / Array.prototype.concat
        // / etc. accept arbitrary named property assignment per ECMA-262 §23.1.5
        // [[DefineOwnProperty]]. Numeric indices are handled via SetIndex; only
        // string keys land here. Store named-non-numeric writes in PDS as data
        // descriptors so GetProperty / hasOwn / gOPD round-trip. Pre-fix these
        // fell to SetFieldsProperty which silently dropped them.
        var listSetPropLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listSetPropLabel);

        // Not a dict or $Object or $TSFunction or $CJSModule or Type - try SetFieldsProperty for class instances
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // List<object?> handler: same shape as $TSArray's non-length path.
        il.MarkLabel(listSetPropLabel);
        {
            // Skip if key is "length" or a numeric index — those write paths
            // belong to SetIndex (numeric) / a dedicated length path. Silent
            // no-op for "length" matches current behavior; named numeric
            // string falls through to PDS (close enough, integer-key writes
            // through SetProperty are rare).
            var listSetIsLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, listSetIsLengthLabel);
            // Frozen guard.
            var listSetNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, listSetNotFrozenLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listSetNotFrozenLabel);
            // Accessor descriptors keep their setter semantics on List-backed
            // arrays/arguments. A present setter handles the write; getter-only
            // descriptors fall through to the non-writable no-op below.
            var listSetPdsSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, listSetPdsSetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
            var listSetNoPdsSetterLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, listSetNoPdsSetterLabel);
            EmitInvokePdsSetterWithValueAndReturn(il, runtime, listSetPdsSetterLocal);
            il.MarkLabel(listSetNoPdsSetterLabel);
            // Existing-descriptor writable=false guard.
            var listSetExistingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, listSetExistingDescLocal);
            var listSetWritableLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Brfalse, listSetWritableLabel);
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, listSetWritableLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listSetWritableLabel);
            // Install fresh data descriptor with the value.
            EmitDefineDataDescriptorFromValue(il, runtime);
            il.MarkLabel(listSetIsLengthLabel);
            il.Emit(OpCodes.Ret);
        }

        // $RegExp handler: store `r.lastIndex = value` without coercion.
        // Other property writes fall through to
        // SetFieldsProperty so user data-property assignments
        // (`Object.defineProperty(r, 'foo', {writable:true}); r.foo = ...`)
        // still hit the user-property bag.
        if (_features.UsesRegExp)
        {
            il.MarkLabel(tsRegExpSetLabel);

            // PDS-first: if user installed an accessor descriptor with a
            // setter, invoke it. If a data descriptor with writable=false
            // is present, silently swallow (non-strict; strict-mode is
            // handled by the strict variant). Mirrors the GET-side fix
            // for spec-aligned override semantics on $RegExp instances.
            var setNoPdsLabel = il.DefineLabel();
            var setPdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, setPdsDescLocal);
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Brfalse, setNoPdsLabel);

            // Accessor setter? Setter slot non-null → InvokeWithThis(rx, value).
            var setNoAccessorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            var setterValueLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, setterValueLocal);
            il.Emit(OpCodes.Ldloc, setterValueLocal);
            il.Emit(OpCodes.Brfalse, setNoAccessorLabel);
            var setterFnLocal = il.DeclareLocal(runtime.TSFunctionType);
            il.Emit(OpCodes.Ldloc, setterValueLocal);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Stloc, setterFnLocal);
            il.Emit(OpCodes.Ldloc, setterFnLocal);
            il.Emit(OpCodes.Brfalse, setNoAccessorLabel);
            il.Emit(OpCodes.Ldloc, setterFnLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(setNoAccessorLabel);

            // No setter slot — check getter. If getter present (accessor
            // descriptor), this is getter-only → silently no-op (non-strict).
            var setSilentlyIgnoreLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, setSilentlyIgnoreLabel);
            // No getter and no setter → data descriptor. Honor writable bit.
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, setSilentlyIgnoreLabel);
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(setSilentlyIgnoreLabel);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(setNoPdsLabel);

            var notLastIndexLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notLastIndexLabel);

            // ECMA-262: lastIndex is an ordinary writable data property — store
            // the assigned value as-is (ToLength is deferred to RegExpBuiltinExec).
            // Only a plain number can use the typed slot without changing an
            // observable read. Strings, booleans, undefined, and objects stay
            // boxed so `r.lastIndex` returns the assigned value until exec
            // performs ToLength and global/sticky write-back stores a number.
            // NB: JS `null` ToLengths to 0 — it must take the primitive path, not
            // the box (a boxed C# null is indistinguishable from "no box").
            var numericSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brfalse, numericSetLabel);                 // null
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.Double);
            il.Emit(OpCodes.Brtrue, numericSetLabel);
            // non-number → rx._lastIndexBoxed = value (defer ToLength/valueOf to exec)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, _tsRegExpLastIndexBoxedField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(numericSetLabel);
            // primitive: rx._lastIndex = ToLength(value); rx._lastIndexBoxed = null
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
            il.Emit(OpCodes.Ldarg_2);
            EmitToLengthBoxed(il, runtime);
            il.Emit(OpCodes.Callvirt, runtime.TSRegExpLastIndexSetter);  // also clears boxed
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notLastIndexLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
            il.Emit(OpCodes.Ret);
        }

        // System.Type handler: store as data descriptor in PropertyDescriptorStore.
        // Read path (EmitGetProperty) looks it up before falling through to .NET member
        // resolution, so writes become visible as reads.
        il.MarkLabel(typeSetLabel);
        {
            // Per ECMA-262 §17, constructor static "prototype"/"name"/"length"
            // are non-writable; non-strict writes silently no-op. Same for
            // Number constants (MAX_VALUE etc.) which are W:F,E:F,C:F.
            var typeSetSkipLabel = il.DefineLabel();
            void EmitTypeSetSkipName(string n)
            {
                var notNameLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, notNameLabel);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notNameLabel);
            }
            EmitTypeSetSkipName("prototype");
            EmitTypeSetSkipName("name");
            EmitTypeSetSkipName("length");
            // Number constants — non-writable on the Number constructor.
            var notNumberTypeForSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Double);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notNumberTypeForSetLabel);
            EmitTypeSetSkipName("MAX_VALUE");
            EmitTypeSetSkipName("MIN_VALUE");
            EmitTypeSetSkipName("NaN");
            EmitTypeSetSkipName("POSITIVE_INFINITY");
            EmitTypeSetSkipName("NEGATIVE_INFINITY");
            EmitTypeSetSkipName("MAX_SAFE_INTEGER");
            EmitTypeSetSkipName("MIN_SAFE_INTEGER");
            EmitTypeSetSkipName("EPSILON");
            il.MarkLabel(notNumberTypeForSetLabel);

            EmitDefineDataDescriptorFromValue(il, runtime);
            il.Emit(OpCodes.Ret);
        }

        // $CJSModule handler — only "exports" is writable; others are no-ops (spec behavior).
        if (_features.UsesCjsRequire)
        {
            il.MarkLabel(cjsModuleSetLabel);
            EmitCjsModuleExportsSetBranch(il, runtime);
        }

        // $Array handler — `arr.length = N` routes through SetLength. Any
        // other name falls off into the normal silent-ignore (JS permits
        // arbitrary named writes on arrays but we don't persist them).
        // ECMA-262 23.1.4.1 [[DefineOwnProperty]] for "length": if
        //   ToNumber(value) !== ToUint32(value)
        // (i.e. value is not a non-negative integer ≤ 2^32 - 1), throw
        // RangeError. Pre-fix Convert.ToInt64 rounded `1.5 → 2` silently.
        il.MarkLabel(tsArraySetPropLabel);
        {
            var notLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notLengthLabel);

            // Coerce value through ToNumber (handles strings, booleans, etc.)
            // then enforce ToUint32 round-trip. The .NET `Convert.ToInt64` path
            // truncates fractional parts; we need to flag fractional / NaN /
            // out-of-range as a RangeError instead.
            var doubleValLocal = il.DeclareLocal(_types.Double);
            var u32Local = il.DeclareLocal(_types.Int64);
            var validLengthLabel = il.DefineLabel();
            var rangeErrorLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Stloc, doubleValLocal);

            // Reject NaN / +Infinity / -Infinity via IsFinite.
            il.Emit(OpCodes.Ldloc, doubleValLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
            il.Emit(OpCodes.Brfalse, rangeErrorLabel);

            // ToUint32 reciprocity: cast to long, ensure 0..2^32-1 inclusive,
            // and that the round-trip back to double matches the original.
            il.Emit(OpCodes.Ldloc, doubleValLocal);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stloc, u32Local);
            // negative → throw
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Ldc_I8, 0L);
            il.Emit(OpCodes.Blt, rangeErrorLabel);
            // > uint.MaxValue → throw
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue);
            il.Emit(OpCodes.Bgt, rangeErrorLabel);
            // round-trip mismatch (fractional component) → throw
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ldloc, doubleValLocal);
            il.Emit(OpCodes.Bne_Un, rangeErrorLabel);
            il.Emit(OpCodes.Br, validLengthLabel);

            il.MarkLabel(rangeErrorLabel);
            GuestErrorEmitter.ThrowRangeError(il, runtime, "Invalid array length");

            il.MarkLabel(validLengthLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Callvirt, runtime.TSArraySetLength);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notLengthLabel);
            // Other named writes on arrays go to PDS as data descriptors.
            // ECMA-262 23.1.5: arrays are exotic objects but accept arbitrary
            // named property assignment via [[DefineOwnProperty]]. Test262
            // patterns like `var arr = []; arr.foo = ...; arr.foo()` rely on
            // this. GetFieldsProperty's PDS-data-descriptor arm reads it back.
            // Pre-fix unconditionally overwrote — Object.freeze(arr) didn't
            // block `arr.foo = "x"` because the existing PDS descriptor's
            // writable bit (false post-freeze via AND-mask) was ignored.
            {
                // Honor frozen state: if Object.isFrozen(arr), silently no-op
                // (non-strict). Strict callers use SetPropertyStrict which
                // can throw.
                var arrFrozenLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
                il.Emit(OpCodes.Brfalse, arrFrozenLabel);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(arrFrozenLabel);

                // A named accessor installed through defineProperty retains
                // its setter. Invoke it with the array as `this` before the
                // data-descriptor writable check.
                var arrPdsSetterLocal = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldloca, arrPdsSetterLocal);
                il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
                var arrNoPdsSetterLabel = il.DefineLabel();
                il.Emit(OpCodes.Brfalse, arrNoPdsSetterLabel);
                EmitInvokePdsSetterWithValueAndReturn(il, runtime, arrPdsSetterLocal);
                il.MarkLabel(arrNoPdsSetterLabel);

                // Honor existing-descriptor writable=false: if there's a PDS
                // data descriptor for this key with writable=false, silently
                // no-op. Accessor descriptors fall through (defining a value
                // over an accessor is handled by PDSDefineProperty).
                var arrNotWritableLabel = il.DefineLabel();
                var arrExistingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
                il.Emit(OpCodes.Stloc, arrExistingDescLocal);
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Brfalse, arrNotWritableLabel);
                // Has descriptor; check writable.
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
                il.Emit(OpCodes.Brtrue, arrNotWritableLabel);
                // Not writable — silent no-op.
                il.Emit(OpCodes.Ret);
                il.MarkLabel(arrNotWritableLabel);

                EmitDefineDataDescriptorFromValue(il, runtime);
            }
            il.Emit(OpCodes.Ret);
        }

        // $TSFunction handler: ECMA-262 §10.1.9 [[Set]] honors non-extensibility for new
        // properties. Gate via PDSCanAddProperty so `Object.preventExtensions(fn); fn.x = v`
        // silently no-ops (non-strict). Existing PDS entries still update.
        il.MarkLabel(tsFunctionSetLabel);
        {
            var tsFnDoSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
            il.Emit(OpCodes.Brtrue, tsFnDoSetLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnDoSetLabel);

            EmitDefineDataDescriptorFromValue(il, runtime);
            il.Emit(OpCodes.Ret);
        }

        // $Object handler. First check PDS for a setter accessor descriptor
        // (defineProperty-installed). If present, invoke it (passing $TSObject
        // as `this`) — TSObject.SetProperty doesn't know about PDS-stored
        // setters. Otherwise delegate to TSObject.SetProperty for the dict /
        // _getters / _setters fast path.
        il.MarkLabel(tsObjectLabel);
        {
            var tsObjPdsSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, tsObjPdsSetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
            var tsObjNoPdsSetterLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsObjNoPdsSetterLabel);
            // Invoke PDS setter.
            EmitInvokePdsSetterWithValueAndReturn(il, runtime, tsObjPdsSetterLocal);
            il.MarkLabel(tsObjNoPdsSetterLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);

        // $AbortSignal dict surface (#985): `signal.onabort = cb` on a dynamically-
        // typed (`any`) signal receiver. The typed path intercepts at compile time
        // (AbortSignalEmitter.TryEmitPropertySet); an `any` receiver lands here and
        // would otherwise store a plain "onabort" dict key that FireAbortEvent (which
        // reads the internal "_onabort" slot) never sees — so the handler never fired.
        // Signals are identified by their "_reasonSet" internal slot. Gated on
        // UsesAbortController so non-signal programs pay nothing; mirrors the GetProperty
        // signal branch (#224).
        if (_features.UsesAbortController)
        {
            var notSignalOnAbortSet = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "onabort");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notSignalOnAbortSet);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "_reasonSet");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brfalse, notSignalOnAbortSet);
            il.Emit(OpCodes.Ldarg_0);  // signal
            il.Emit(OpCodes.Ldarg_2);  // handler
            il.Emit(OpCodes.Call, runtime.AbortSignalSetOnAbort);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalOnAbortSet);
        }

        // For dictionaries, check frozen/sealed tables and silently ignore if frozen/sealed
        var sealedCheckLabel = il.DefineLabel();
        var doSetLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _).
        // Per ECMA-262, freeze only forbids writes to DATA properties — accessor
        // setters still fire because the descriptor's writable bit doesn't apply
        // to accessors. So when there's a PDS setter for this key, fall through
        // to the doSetLabel path (which invokes it). Pre-fix dropped frozen
        // accessor writes silently — broke test262 15.2.3.9-2-c-{2,3,4}.
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var frozenNotFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, frozenNotFoundLabel);
        // Frozen — only allow if there's a PDS accessor setter for this key.
        var frozenAccessorSetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, frozenAccessorSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, nullLabel); // No setter, frozen data — silent return
        il.Emit(OpCodes.Br, doSetLabel);     // Has setter — proceed (doSetLabel re-fetches via PDSTryGetSetter)
        il.MarkLabel(frozenNotFoundLabel);

        // Check if sealed and property doesn't exist
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var extensibleCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, extensibleCheckLabel); // Not sealed, check extensibility

        // Object is sealed - check if property exists (dict OR PDS). Pre-fix
        // only checked the backing dict, so an accessor-only own property
        // (defineProperty with get/set + configurable:false) was treated as
        // missing and the write silently dropped — including its setter.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, doSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brfalse, nullLabel); // Property doesn't exist, silently return
        il.Emit(OpCodes.Br, doSetLabel); // Property exists on sealed object, proceed to set

        // Check extensibility via $PropertyDescriptorStore.CanAddProperty - fully standalone, no reflection
        il.MarkLabel(extensibleCheckLabel);
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, nullLabel);  // Cannot add property, silently return

        // Actually set the property
        il.MarkLabel(doSetLabel);

        // Check for setter accessor via $PropertyDescriptorStore - fully standalone, no reflection
        var setterLocal = il.DeclareLocal(_types.Object);
        var noSetterLabel = il.DefineLabel();

        // Call PDSTryGetSetter(obj, name, out setter)
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Ldloca, setterLocal);  // out setter
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, noSetterLabel);

        // Setter was found - invoke it via InvokeMethodValue(obj, setter, [value])
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, setterLocal);

        il.MarkLabel(noSetterLabel);

        // Check if property is writable via $PropertyDescriptorStore - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        il.Emit(OpCodes.Brfalse, nullLabel);  // Not writable, silently return

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }
    /// <summary>
    /// Emits inline IL that coerces the boxed-object value at the top of the
    /// stack to int32 via the JS ToLength algorithm: null/undefined/NaN → 0,
    /// false → 0, true → 1, double via truncate (clamped to int32), int via
    /// pass-through, string via TryParse → double-path (or 0 on parse
    /// failure), other types → 0. Used by RegExpBuiltinExec after a raw
    /// <c>lastIndex</c> value has been observed.
    /// </summary>
    private void EmitToLengthBoxed(ILGenerator il, EmittedRuntime runtime)
    {
        var localVal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, localVal);

        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var intLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, intLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(doubleLabel);
        var dTmp = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dTmp);

        // NaN check: (d == d) is false iff d is NaN.
        var dNonNanLabel = il.DefineLabel();
        var dPositiveLabel = il.DefineLabel();
        var dClampLabel = il.DefineLabel();
        var dInRangeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, dNonNanLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(dNonNanLabel);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, dPositiveLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(dPositiveLabel);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Blt, dInRangeLabel);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(dInRangeLabel);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(intLabel);
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(stringLabel);
        var sTmp = il.DeclareLocal(_types.Double);
        var parseFailLabel = il.DefineLabel();
        var sPosLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.Float);
        il.Emit(OpCodes.Call, _types.GetProperty(typeof(System.Globalization.CultureInfo), "InvariantCulture").GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, sTmp);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("TryParse",
            [_types.String, typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(double).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, parseFailLabel);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, parseFailLabel);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, sPosLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(sPosLabel);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(parseFailLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(doneLabel);
    }
    /// <summary>
    /// Emits SetPropertyStrict(object obj, string name, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    private void EmitSetPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPropertyStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object, _types.Boolean]
        );
        runtime.SetPropertyStrict = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Without this redirect, a strict-mode top-level `this.foo = v`
        // (compiled `this` resolves to the globalThis sentinel) fell through
        // to SetFieldsPropertyStrict and was dropped — e.g. Test262
        // Object/create 15.2.3.5-4-177 / defineProperty 15.2.3.6-3-230 set
        // `this.value` / `this.get` and reuse `this` as a descriptor.
        EmitGlobalThisSetRedirect(il, runtime);

        // Check if $Object
        var sharpTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, sharpTSObjectLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $TSFunction — mirror the non-strict SetProperty branch (functions as objects carry
        // user-assigned properties through PDSDefineProperty).
        var tsFunctionSetStrictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionSetStrictLabel);

        // $CJSModule — mirror the non-strict branch. Gated on UsesCjsRequire.
        var cjsModuleSetStrictLabel = il.DefineLabel();
        if (_features.UsesCjsRequire)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CjsModuleType);
            il.Emit(OpCodes.Brtrue, cjsModuleSetStrictLabel);
        }

        // $Array / List<object?> — named property write on an array in strict
        // mode. The non-strict SetProperty has dedicated array branches (length
        // coercion, numeric, PDS data descriptors); without a strict equivalent
        // these writes fell to SetFieldsPropertyStrict, which silently dropped
        // them, so `arr.foo = fn; arr.foo()` saw `foo === undefined` under "use
        // strict" (Test262 Array slice/splice S15.4.4.*_A*_T*, the common
        // `arr.getClass = Object.prototype.toString` pattern). Detect arrays here
        // and route to arraySetStrictLabel, which enforces strict frozen /
        // non-writable throws then reuses the non-strict store logic.
        var arraySetStrictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, arraySetStrictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, arraySetStrictLabel);

        // Not a dict or $Object or $TSFunction or $CJSModule or array - fall back to SetFieldsPropertyStrict.
        // NOTE (#1131): unlike the non-strict SetProperty, this variant has no
        // dedicated Proxy / $RegExp / System.Type receiver branches — those
        // receivers fall through here. Preserved as-is by the strict/non-strict
        // dedup (behavior-preserving); tracked as drift in the epic notes.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetFieldsPropertyStrict);
        il.Emit(OpCodes.Ret);

        // $Array / List<object?> strict handler: ECMA-262 §10.4.2.1 / OrdinarySet
        // step 5 — a frozen array or an own non-writable data property makes the
        // assignment fail; in strict mode PutValue then throws a TypeError. We
        // honor those two cases here, then delegate the actual store to the
        // non-strict SetProperty (shared length/numeric/PDS-data-descriptor path).
        il.MarkLabel(arraySetStrictLabel);
        {
            var arrayDoStoreLabel = il.DefineLabel();

            // Frozen array → throw "Cannot assign to read only property 'name'".
            var arrayNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, arrayNotFrozenLabel);
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object '[object Array]'");
            il.MarkLabel(arrayNotFrozenLabel);

            // Own non-writable DATA descriptor → throw. Accessor descriptors
            // (setter present) and writable/absent descriptors fall through to
            // the store, where SetProperty invokes the setter or overwrites.
            var arrayDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, arrayDescLocal);
            il.Emit(OpCodes.Ldloc, arrayDescLocal);
            il.Emit(OpCodes.Brfalse, arrayDoStoreLabel); // no descriptor → store
            il.Emit(OpCodes.Ldloc, arrayDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, arrayDoStoreLabel); // accessor → delegate (invokes setter)
            il.Emit(OpCodes.Ldloc, arrayDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, arrayDoStoreLabel); // writable → store
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object '[object Array]'");

            il.MarkLabel(arrayDoStoreLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.SetProperty);
            il.Emit(OpCodes.Ret);
        }

        // $CJSModule strict handler — same as non-strict for now. Gated.
        if (_features.UsesCjsRequire)
        {
            il.MarkLabel(cjsModuleSetStrictLabel);
            EmitCjsModuleExportsSetBranch(il, runtime);
        }

        // $TSFunction handler: create data descriptor with the value, store via PDSDefineProperty
        il.MarkLabel(tsFunctionSetStrictLabel);
        EmitDefineDataDescriptorFromValue(il, runtime);
        il.Emit(OpCodes.Ret);

        // $Object - call SetPropertyStrict
        il.MarkLabel(sharpTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSetPropertyStrict);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // For dictionaries, check frozen/sealed tables
        var frozenCheckLabel = il.DefineLabel();
        var sealedCheckLabel = il.DefineLabel();
        var doSetLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, sealedCheckLabel); // Not frozen, check sealed

        // Object is frozen - check strict mode
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // Not strict, silently return

        // Strict mode and frozen - throw TypeError
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");

        // Check if sealed and property doesn't exist
        il.MarkLabel(sealedCheckLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var extensibleCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, extensibleCheckLabel); // Not sealed, check extensibility

        // Object is sealed - check if property exists
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, doSetLabel); // Property exists, can modify

        // Property doesn't exist and object is sealed - check strict mode
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // Not strict, silently return

        // Strict mode and sealed with new property - throw TypeError
        EmitThrowTypeErrorWithName(il, runtime, "Cannot add property '", "' to a sealed object");

        // Check extensibility via $PropertyDescriptorStore.CanAddProperty - fully standalone, no reflection
        il.MarkLabel(extensibleCheckLabel);
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brtrue, doSetLabel);  // Can add property, proceed to set

        // Cannot add property (non-extensible) - check strict mode
        il.Emit(OpCodes.Ldarg_3);  // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel);  // Not strict, silently return

        // Strict mode and non-extensible with new property - throw TypeError
        EmitThrowTypeErrorWithName(il, runtime, "Cannot add property '", "' to a non-extensible object");

        // Actually set the property. Mirrors the non-strict SetProperty doSet
        // arm: honor a PDS accessor setter, and an existing non-writable data
        // descriptor (or getter-only accessor). Pre-fix the strict path skipped
        // straight to dict.set_Item, so under "use strict": (1) an accessor
        // setter was bypassed and overwritten with a data value, and (2) writes
        // to a writable:false property neither threw nor were suppressed —
        // `verifyProperty`'s isWritable probe then saw the write succeed (Test262
        // Object/defineProperty 15.2.3.6-3-181 et al.).
        il.MarkLabel(doSetLabel);

        // PDS accessor setter present → invoke it (ECMA-262 OrdinarySet: a setter
        // fires regardless of strictness).
        var strictSetterLocal = il.DeclareLocal(_types.Object);
        var strictNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, strictSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, strictNoSetterLabel);
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, strictSetterLocal);
        il.MarkLabel(strictNoSetterLabel);

        // Non-writable (data writable:false, or getter-only accessor) → strict
        // throws TypeError (ECMA-262 §6.2.5.6 / PutValue), sloppy silently
        // returns. PDSIsWritable returns true when no descriptor exists.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        var strictWritableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, strictWritableLabel);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // sloppy → silent return
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");
        il.MarkLabel(strictWritableLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }
    /// <summary>
    /// Emits SetIndexStrict(object obj, object index, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen/sealed arrays.
    /// </summary>
    private void EmitSetIndexStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndexStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object, _types.Boolean]
        );
        runtime.SetIndexStrict = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var sharpTSArrayLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Symbol-keyed write (symbols work on any receiver type) — route to the
        // non-strict SetIndex, which has the symbol-key handler (registered
        // accessor setter, else symbol-dict store with extensibility checks).
        // SetIndexStrict otherwise dispatches only by RECEIVER type ($Array /
        // List / Dictionary) and would ToString a symbol key into a bogus string
        // key, dropping the write — so under "use strict" `obj[sym] = v` and
        // `obj[Symbol.iterator] = fn` were lost (Test262 Object/
        // getOwnPropertySymbols object-contains-symbol-property-without-description,
        // Array/from iter-get-iter-val-err).
        // The store itself (registered setter / symbol-dict write) is delegated
        // to the non-strict SetIndex, but its symbol handler silently no-ops on a
        // frozen/sealed/non-extensible receiver. In strict mode those must throw
        // a TypeError (ECMA-262 PutValue), so enforce that here first:
        //   frozen           → always throw (props non-writable + non-extensible);
        //   sealed/non-ext    → throw only when the symbol key is NEW (adding it);
        //   otherwise         → delegate to SetIndex (store / update / invoke setter).
        // (Test262 Object/freeze frozen-object-contains-symbol-properties-strict.)
        var notSymbolKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, notSymbolKeyLabel);

        var symThrowLabel = il.DefineLabel();
        var symRouteLabel = il.DefineLabel();
        var symStateTmp = il.DeclareLocal(_types.Object);
        var cwtTryGetValue = _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType());

        // frozen → throw.
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symStateTmp);
        il.Emit(OpCodes.Callvirt, cwtTryGetValue);
        il.Emit(OpCodes.Brtrue, symThrowLabel);

        // sealed OR non-extensible → throw only if the symbol key is not already present.
        var symSealedOrNonExtLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symStateTmp);
        il.Emit(OpCodes.Callvirt, cwtTryGetValue);
        il.Emit(OpCodes.Brtrue, symSealedOrNonExtLabel);
        il.Emit(OpCodes.Ldsfld, runtime.NonExtensibleObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symStateTmp);
        il.Emit(OpCodes.Callvirt, cwtTryGetValue);
        il.Emit(OpCodes.Brfalse, symRouteLabel); // extensible → store
        il.MarkLabel(symSealedOrNonExtLabel);
        // sealed/non-ext: present symbol → allow update (route); absent → throw.
        var symDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symDictLocal);
        il.Emit(OpCodes.Ldloc, symDictLocal);
        il.Emit(OpCodes.Brfalse, symThrowLabel); // no symbol dict → key absent → throw
        il.Emit(OpCodes.Ldloc, symDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brfalse, symThrowLabel); // key absent → throw

        il.MarkLabel(symRouteLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetIndex);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(symThrowLabel);
        EmitThrowTypeError(il, runtime, "Cannot assign to a read-only or non-extensible property");

        il.MarkLabel(notSymbolKeyLabel);

        // Check if $Array (for strict mode support)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, sharpTSArrayLabel);

        // List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $Object / $TSFunction / System.Type / other receivers: route indexed
        // writes to SetPropertyStrict(obj, ToString(index), value, strictMode),
        // mirroring how the non-strict SetIndex routes them to SetProperty. Pre-fix
        // these fell through to a silent no-op under "use strict", so `child[0] = v`
        // on a class instance / $Object dropped the element — which broke
        // Array.prototype.{reduce,every,filter,indexOf}.call(nonArrayObj, …) on
        // a `new Con()`-style receiver (Test262 Array/prototype/*/15.4.4.*-2-*).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ret);

        // $Array - call SetStrict with index and strictMode via the long API.
        // Stage E.2 M6: widened from TSArraySetStrict (int) to TSArraySetStrictLong
        // so `"use strict"; arr[2147483648] = v` doesn't truncate to int.MinValue.
        // Parallel to the M3 GetIndex/SetIndex widening.
        il.MarkLabel(sharpTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt64", _types.Object));
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetStrictLong);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check if frozen - in strict mode, throw TypeError
        var listSetLabel = il.DefineLabel();
        var listFrozenCheckLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, listFrozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var listNotFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listNotFrozenLabel);
        // Frozen - check strictMode and throw if true
        il.Emit(OpCodes.Ldarg_3); // strictMode
        var listFrozenSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listFrozenSilentLabel);
        EmitThrowTypeError(il, runtime, "Cannot assign to read only property of frozen array");
        il.MarkLabel(listFrozenSilentLabel);
        il.Emit(OpCodes.Ret); // Silently return in non-strict mode
        il.MarkLabel(listNotFrozenLabel);
        // Not frozen - set normally
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObjectNullable, "set_Item", _types.Int32, _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Dictionary string-key write: route to SetPropertyStrict(obj, ToString(idx),
        // value, strictMode) so PDS accessor setters, non-writable data descriptors,
        // and strict TypeErrors are honored identically to named-property writes.
        // Pre-fix this did a raw dict.set_Item, bypassing the writable/setter checks —
        // so under "use strict" `obj[name] = v` on a writable:false property neither
        // threw nor was suppressed, which propertyHelper.js's isWritable probe (it
        // writes via `obj[name] = …`) read back as "writable". (Test262 Object/
        // defineProperty 15.2.3.6-3-181, defineProperties 15.2.3.7-* et al.)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ret);
    }
}
