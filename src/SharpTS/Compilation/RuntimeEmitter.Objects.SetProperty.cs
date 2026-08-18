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

        // Honor any own descriptor before receiver-specific storage. This is
        // the common OrdinarySet path for CLR-backed JS objects (Date, Error,
        // RegExp, Promise, and other host carriers): accessors invoke their
        // setter, non-writable/getter-only properties reject the write, and a
        // writable data property updates only [[Value]] while preserving its
        // attributes.
        var existingPdsDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var existingPdsSetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, existingPdsSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        var noExistingPdsSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noExistingPdsSetterLabel);
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, existingPdsSetterLocal);
        il.MarkLabel(noExistingPdsSetterLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, existingPdsDescriptorLocal);
        var noExistingPdsDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, existingPdsDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noExistingPdsDescriptorLabel);
        il.Emit(OpCodes.Ldloc, existingPdsDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, endLabel);
        il.Emit(OpCodes.Ldloc, existingPdsDescriptorLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noExistingPdsDescriptorLabel);

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
        // Error properties are ordinary JS data properties after
        // construction and accept values of any ECMAScript type. Keep the
        // string-typed CLR slots as constructor/runtime internals; user writes
        // live in PDS so object-valued name/message assignments round-trip.
        EmitDefineDataDescriptorFromValue(il, runtime);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorNameLabel);

        // Check "message"
        var notErrorMessageLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorMessageLabel);
        // Constructor-created message is a PDS data property. Keep its stored
        // value synchronized with the CLR backing slot so bracket assignment
        // observes ordinary writable-data-property semantics.
        var errorMessageDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, errorMessageDescriptorLocal);
        var noErrorMessageDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, errorMessageDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noErrorMessageDescriptorLabel);
        il.Emit(OpCodes.Ldloc, errorMessageDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, endLabel);
        il.Emit(OpCodes.Ldloc, errorMessageDescriptorLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        // The ECMAScript message slot accepts any value after construction;
        // do not flow through the string-typed CLR compatibility property.
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noErrorMessageDescriptorLabel);
        EmitDefineDataDescriptorFromValue(il, runtime);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorMessageLabel);

        // Check "stack"
        var notErrorStackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "stack");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorStackLabel);
        EmitDefineDataDescriptorFromValue(il, runtime);
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
        // The intrinsic Promise representation is Task<object?>. It is still
        // an ordinary extensible ECMAScript object and must retain expando
        // writes such as `promise.then = customThen`.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        il.Emit(OpCodes.Br, afterPdsStoreLabel);

        il.MarkLabel(pdsStoreLabel);
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
            il.Emit(OpCodes.Brfalse, endLabel);
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
        var strictErrorMessageDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, strictErrorMessageDescriptorLocal);
        var noStrictErrorMessageDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, strictErrorMessageDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noStrictErrorMessageDescriptorLabel);
        il.Emit(OpCodes.Ldloc, strictErrorMessageDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        var strictErrorMessageWritableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, strictErrorMessageWritableLabel);
        il.Emit(OpCodes.Ldarg_3);
        var strictErrorMessageSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictErrorMessageSilentLabel);
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");
        il.MarkLabel(strictErrorMessageSilentLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(strictErrorMessageWritableLabel);
        il.Emit(OpCodes.Ldloc, strictErrorMessageDescriptorLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noStrictErrorMessageDescriptorLabel);
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
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
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

        // $Arguments has a JS-visible length slot independent from its List
        // backing store. Writes must update that live slot so an already-
        // created ArrayIterator observes truncation on its next() call.
        var notArgumentsLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brfalse, notArgumentsLengthLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notArgumentsLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.ArgumentsType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, runtime.ArgumentsLengthField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArgumentsLengthLabel);

        // Proxy dispatch is omitted entirely from assemblies that do not use
        // Proxy. Its receiver-aware ordinary-set helper is feature-gated too.
        if (_features.UsesProxy)
        {
            var notProxyLabel = il.DefineLabel();
            EmitProxySetPropertyCheck(
                il, runtime,
                () => il.Emit(OpCodes.Ldarg_0),
                () => il.Emit(OpCodes.Ldarg_1),
                () => il.Emit(OpCodes.Ldarg_2),
                notProxyLabel);
            il.MarkLabel(notProxyLabel);
        }

        // OrdinarySetWithOwnDescriptor consults an inherited descriptor before
        // creating a new own property. This shared check covers intrinsic CLR
        // carriers (boxed primitives, Date, and bound functions) whose own
        // storage branches otherwise created a shadow even for a getter-only
        // inherited accessor.
        var inheritedSetContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brtrue, inheritedSetContinueLabel);
        var inheritedSetPrototypeLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, inheritedSetPrototypeLocal);
        il.Emit(OpCodes.Ldloc, inheritedSetPrototypeLocal);
        il.Emit(OpCodes.Brfalse, inheritedSetContinueLabel);
        // An inherited Proxy supplies [[Set]] itself; dispatch before probing
        // the emitted descriptor store so its trap observes the original
        // receiver (the object on which assignment began).
        if (_features.UsesProxy)
        {
            var inheritedProxyLabel = il.DefineLabel();
            var inheritedNotProxyLabel = il.DefineLabel();
            EmitProxyTypeCheck(
                il, () => il.Emit(OpCodes.Ldloc, inheritedSetPrototypeLocal),
                inheritedProxyLabel, inheritedNotProxyLabel);
            il.MarkLabel(inheritedProxyLabel);
            EmitProxySetCompiledCall(
                il, runtime,
                () => il.Emit(OpCodes.Ldloc, inheritedSetPrototypeLocal),
                () => il.Emit(OpCodes.Ldarg_1),
                () => il.Emit(OpCodes.Ldarg_2),
                () => il.Emit(OpCodes.Ldarg_0));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(inheritedNotProxyLabel);
        }
        var inheritedSetDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldloc, inheritedSetPrototypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, inheritedSetDescriptorLocal);
        il.Emit(OpCodes.Ldloc, inheritedSetDescriptorLocal);
        il.Emit(OpCodes.Brfalse, inheritedSetContinueLabel);
        var inheritedSetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, inheritedSetDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, inheritedSetterLocal);
        var inheritedSetNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, inheritedSetterLocal);
        il.Emit(OpCodes.Brfalse, inheritedSetNoSetterLabel);
        il.Emit(OpCodes.Ldloc, inheritedSetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nullLabel);
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, inheritedSetterLocal);
        il.MarkLabel(inheritedSetNoSetterLabel);
        // Getter-only accessors reject assignment; writable inherited data
        // properties allow creation of a new own property.
        il.Emit(OpCodes.Ldloc, inheritedSetDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, nullLabel);
        il.Emit(OpCodes.Ldloc, inheritedSetDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.MarkLabel(inheritedSetContinueLabel);

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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
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
            var listSetDefineNewLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Brfalse, listSetDefineNewLabel);
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            var listSetUpdateExistingLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, listSetUpdateExistingLabel);
            il.Emit(OpCodes.Ret);
            // Ordinary Set updates only [[Value]] on an existing writable data
            // descriptor; enumerable/configurable/writable remain unchanged.
            il.MarkLabel(listSetUpdateExistingLabel);
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listSetDefineNewLabel);
            // A new named property gets the ordinary assignment defaults.
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

            // Updating a previously defined static property preserves its
            // attributes. A missing property created by assignment gets the
            // ordinary assignment defaults; a synthesized built-in static is
            // likewise writable/configurable and non-enumerable.
            var existingTypeDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, existingTypeDescriptorLocal);
            var newTypeDescriptorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, existingTypeDescriptorLocal);
            il.Emit(OpCodes.Brfalse, newTypeDescriptorLabel);
            il.Emit(OpCodes.Ldloc, existingTypeDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, typeSetSkipLabel);
            il.Emit(OpCodes.Ldloc, existingTypeDescriptorLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(newTypeDescriptorLabel);
            var newTypeDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var newTypeEnumerableLocal = il.DeclareLocal(_types.Boolean);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, newTypeEnumerableLocal);
            var ordinaryTypeAssignmentLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.LookupBuiltInStaticMember);
            il.Emit(OpCodes.Brfalse, ordinaryTypeAssignmentLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, newTypeEnumerableLocal);
            il.MarkLabel(ordinaryTypeAssignmentLabel);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, newTypeDescriptorLocal);
            il.Emit(OpCodes.Ldloc, newTypeDescriptorLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, newTypeDescriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, newTypeDescriptorLocal);
            il.Emit(OpCodes.Ldloc, newTypeEnumerableLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, newTypeDescriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, newTypeDescriptorLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(typeSetSkipLabel);
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

            // A writable:false array-length descriptor blocks assignment.
            // In sloppy code the failed Set is silent; SetPropertyStrict has
            // already performed the same check and throws before delegating.
            var lengthWritableLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, runtime.PDSIsWritable);
            il.Emit(OpCodes.Brtrue, lengthWritableLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(lengthWritableLabel);

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
                var arrDefineNewLabel = il.DefineLabel();
                var arrExistingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
                il.Emit(OpCodes.Stloc, arrExistingDescLocal);
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Brfalse, arrDefineNewLabel);
                // Has descriptor; check writable.
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
                var arrUpdateExistingLabel = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, arrUpdateExistingLabel);
                // Not writable — silent no-op.
                il.Emit(OpCodes.Ret);
                // Preserve descriptor attributes on an ordinary value update.
                il.MarkLabel(arrUpdateExistingLabel);
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(arrDefineNewLabel);

                EmitDefineDataDescriptorFromValue(il, runtime);
            }
            il.Emit(OpCodes.Ret);
        }

        // $TSFunction handler: ECMA-262 §10.1.9 [[Set]] honors non-extensibility for new
        // properties. Gate via PDSCanAddProperty so `Object.preventExtensions(fn); fn.x = v`
        // silently no-ops (non-strict). Existing PDS entries still update.
        il.MarkLabel(tsFunctionSetLabel);
        {
            // Existing function properties retain their descriptor shape.
            // The previous unconditional DefineDataDescriptor replaced
            // non-writable/accessor descriptors with a fresh W/E/C=true data
            // descriptor on ordinary assignment.
            var tsFnSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, tsFnSetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
            var tsFnNoSetterLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnNoSetterLabel);
            EmitInvokePdsSetterWithValueAndReturn(il, runtime, tsFnSetterLocal);
            il.MarkLabel(tsFnNoSetterLabel);

            // Freezing changes every data property's effective [[Writable]]
            // to false without mutating the stable stored descriptor. Accessor
            // setters were handled above and remain callable after freeze.
            var tsFnNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, tsFnNotFrozenLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnNotFrozenLabel);

            var tsFnExistingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsFnExistingDescLocal);
            var tsFnDefineNewLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsFnExistingDescLocal);
            il.Emit(OpCodes.Brfalse, tsFnDefineNewLabel);
            il.Emit(OpCodes.Ldloc, tsFnExistingDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            var tsFnUpdateExistingLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, tsFnUpdateExistingLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnUpdateExistingLabel);
            il.Emit(OpCodes.Ldloc, tsFnExistingDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsFnDefineNewLabel);
            // Function `name` and `length` are intrinsic own data properties with
            // [[Writable]] false. It is synthesized by Get/descriptor helpers
            // rather than stored in PDS, so an absent PDS entry must not be
            // mistaken for permission to create a writable shadow.
            var tsFnNotIntrinsicLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, tsFnNotIntrinsicLengthLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnNotIntrinsicLengthLabel);
            var tsFnNotIntrinsicNameLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "name");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, tsFnNotIntrinsicNameLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnNotIntrinsicNameLabel);
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

            // A $Object's dictionary fast path must still honor an own PDS
            // data descriptor. Boxed String exotic indices/length and
            // Object.defineProperty-created read-only slots live in PDS even
            // though TSObject.SetProperty itself only sees the dictionary.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSIsWritable);
            il.Emit(OpCodes.Brfalse, nullLabel);

            // Keep a writable PDS data descriptor synchronized with the
            // $Object dictionary. Descriptor-aware reads (including gOPD and
            // GetProperty's PDS-first path) otherwise keep observing the old
            // value after an ordinary assignment even though the backing
            // dictionary was updated.
            var tsObjDescriptorLocal = il.DeclareLocal(
                runtime.CompiledPropertyDescriptorType);
            var tsObjRawStoreLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsObjDescriptorLocal);
            il.Emit(OpCodes.Ldloc, tsObjDescriptorLocal);
            il.Emit(OpCodes.Brfalse, tsObjRawStoreLabel);
            il.Emit(OpCodes.Ldloc, tsObjDescriptorLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt,
                runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.MarkLabel(tsObjRawStoreLabel);
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
        // Existing dictionary properties remain writable on a merely
        // non-extensible object. PDSCanAddProperty answers whether a NEW key
        // may be created, so bypass it when ordinary backing storage already
        // owns the key (OrdinarySetWithOwnDescriptor step 3.d).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, doSetLabel);
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

        // Dictionary-backed objects may also carry a PDS data descriptor for
        // the same key. Keep both stores synchronized: descriptor-aware reads
        // observe the PDS value, while ordinary reads use the dictionary.
        var dictDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var dictRawStoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, dictDescriptorLocal);
        il.Emit(OpCodes.Ldloc, dictDescriptorLocal);
        il.Emit(OpCodes.Brfalse, dictRawStoreLabel);
        il.Emit(OpCodes.Ldloc, dictDescriptorLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);

        il.MarkLabel(dictRawStoreLabel);

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

        // Proxy [[Set]] must run before receiver-shape dispatch. Preserve the
        // proxy as Receiver so a handler's Reflect.set(target, key, value,
        // receiver) observes the receiver's [[GetOwnProperty]] and
        // [[DefineOwnProperty]] methods.
        if (_features.UsesProxy)
        {
            var strictSetNotProxyLabel = il.DefineLabel();
            var strictSetProxyLabel = il.DefineLabel();
            EmitProxyTypeCheck(
                il, () => il.Emit(OpCodes.Ldarg_0),
                strictSetProxyLabel, strictSetNotProxyLabel);
            il.MarkLabel(strictSetProxyLabel);
            EmitProxySetCompiledCall(
                il, runtime,
                () => il.Emit(OpCodes.Ldarg_0),
                () => il.Emit(OpCodes.Ldarg_1),
                () => il.Emit(OpCodes.Ldarg_2),
                () => il.Emit(OpCodes.Ldarg_0));
            var strictSetProxySucceededLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, strictSetProxySucceededLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, nullLabel);
            GuestErrorEmitter.ThrowTypeError(
                il, runtime, "Proxy set trap returned false");
            il.MarkLabel(strictSetProxySucceededLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(strictSetNotProxyLabel);
        }

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
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionSetStrictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
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

            // String-keyed writes to canonical array indexes must use the
            // indexed OrdinarySet path.  Array mutators spell their target
            // indexes as strings, and routing those writes through the named
            // property store skipped inherited Array.prototype accessors.
            var arrayNamedPropertyLabel = il.DefineLabel();
            var arrayPropertyIndexLocal = il.DeclareLocal(_types.UInt32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSArrayType);
            il.Emit(OpCodes.Brfalse, arrayNamedPropertyLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, arrayPropertyIndexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
            il.Emit(OpCodes.Brfalse, arrayNamedPropertyLabel);
            il.Emit(OpCodes.Ldloc, arrayPropertyIndexLocal);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Beq, arrayNamedPropertyLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, arrayPropertyIndexLocal);
            il.Emit(OpCodes.Box, _types.UInt32);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, arrayNamedPropertyLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Brtrue, arrayNamedPropertyLabel);
            var arrayIndexedRawStoreLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, arrayPropertyIndexLocal);
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
            il.Emit(OpCodes.Brtrue, arrayIndexedRawStoreLabel);
            var arrayInheritedSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, arrayInheritedSetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
            il.Emit(OpCodes.Brfalse, arrayIndexedRawStoreLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, arrayInheritedSetterLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(arrayIndexedRawStoreLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, arrayPropertyIndexLocal);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, runtime.TSArraySetStrictLong);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(arrayNamedPropertyLabel);

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

        // $TSFunction handler: ordinary [[Set]] over PDS-backed function
        // properties, including accessors and effective integrity-level state.
        il.MarkLabel(tsFunctionSetStrictLabel);
        {
            var tsFnStrictSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, tsFnStrictSetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
            var tsFnStrictNoSetterLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnStrictNoSetterLabel);
            EmitInvokePdsSetterWithValueAndReturn(il, runtime, tsFnStrictSetterLocal);
            il.MarkLabel(tsFnStrictNoSetterLabel);

            var tsFnStrictDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsFnStrictDescriptorLocal);
            var tsFnStrictNewPropertyLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsFnStrictDescriptorLocal);
            il.Emit(OpCodes.Brfalse, tsFnStrictNewPropertyLabel);

            // Getter-only/accessor-with-undefined-setter and non-writable data
            // descriptors reject assignment. A frozen data property is also
            // effectively non-writable even if its stored bit remains true.
            var tsFnStrictRejectLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsFnStrictDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, tsFnStrictRejectLabel);
            il.Emit(OpCodes.Ldloc, tsFnStrictDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, tsFnStrictRejectLabel);
            il.Emit(OpCodes.Ldloc, tsFnStrictDescriptorLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, tsFnStrictRejectLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brtrue, tsFnStrictRejectLabel);
            il.Emit(OpCodes.Ldloc, tsFnStrictDescriptorLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsFnStrictRejectLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, nullLabel);
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of function");

            il.MarkLabel(tsFnStrictNewPropertyLabel);
            // The synthesized intrinsic function `name` and `length` properties are
            // non-writable even though it has no backing PDS entry. A strict
            // Set must reject it instead of defining a writable shadow.
            var tsFnStrictNotIntrinsicLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, tsFnStrictNotIntrinsicLengthLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, nullLabel);
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of function");
            il.MarkLabel(tsFnStrictNotIntrinsicLengthLabel);
            var tsFnStrictNotIntrinsicNameLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "name");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, tsFnStrictNotIntrinsicNameLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, nullLabel);
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of function");
            il.MarkLabel(tsFnStrictNotIntrinsicNameLabel);
            var tsFnStrictCanAddLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
            il.Emit(OpCodes.Brtrue, tsFnStrictCanAddLabel);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brfalse, nullLabel);
            EmitThrowTypeErrorWithName(il, runtime, "Cannot add property '", "' to a non-extensible function");
            il.MarkLabel(tsFnStrictCanAddLabel);
            EmitDefineDataDescriptorFromValue(il, runtime);
            il.Emit(OpCodes.Ret);
        }

        // $Object - honor PDS accessors/read-only data properties before the
        // TSObject dictionary fast path.
        il.MarkLabel(sharpTSObjectLabel);
        var sharpSetterLocal = il.DeclareLocal(_types.Object);
        var sharpNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, sharpSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, sharpNoSetterLabel);
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, sharpSetterLocal);
        il.MarkLabel(sharpNoSetterLabel);

        // Object-literal accessors are stored in $Object's native _setters
        // dictionary rather than PDS. They remain callable after freeze, so
        // delegate before the receiver-wide PDSIsWritable integrity check.
        var sharpDelegateToObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasSetter);
        il.Emit(OpCodes.Brtrue, sharpDelegateToObjectLabel);

        var sharpWritableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        il.Emit(OpCodes.Brtrue, sharpWritableLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, nullLabel);
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");
        il.MarkLabel(sharpWritableLabel);
        il.MarkLabel(sharpDelegateToObjectLabel);
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

        // Frozen data properties reject writes, but accessor setters remain
        // callable after Object.freeze.
        var strictFrozenSetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, strictFrozenSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brtrue, doSetLabel);

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

        // Keep PDS-backed data descriptors and dictionary storage synchronized.
        var strictDictDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var strictDictRawStoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, strictDictDescriptorLocal);
        il.Emit(OpCodes.Ldloc, strictDictDescriptorLocal);
        il.Emit(OpCodes.Brfalse, strictDictRawStoreLabel);
        il.Emit(OpCodes.Ldloc, strictDictDescriptorLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);

        il.MarkLabel(strictDictRawStoreLabel);

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
        var symCheckObjectStateLabel = il.DefineLabel();
        var symStateTmp = il.DeclareLocal(_types.Object);
        var cwtTryGetValue = _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType());

        // Existing symbol descriptors can reject strict writes independently
        // of the receiver's extensibility state.
        var strictSymDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var strictSymValueLocal = il.DeclareLocal(_types.Object);
        var strictSymDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var strictSymSetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, strictSymDictLocal);
        il.Emit(OpCodes.Ldloc, strictSymDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, strictSymValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, symCheckObjectStateLabel);
        il.Emit(OpCodes.Ldloc, strictSymValueLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Stloc, strictSymDescriptorLocal);
        il.Emit(OpCodes.Ldloc, strictSymDescriptorLocal);
        il.Emit(OpCodes.Brfalse, symCheckObjectStateLabel);
        il.Emit(OpCodes.Ldloc, strictSymDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, strictSymSetterLocal);
        il.Emit(OpCodes.Ldloc, strictSymSetterLocal);
        var strictSymNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictSymNoSetterLabel);
        il.Emit(OpCodes.Ldloc, strictSymSetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        // A callable accessor setter remains writable after freeze. Route
        // directly to invocation, bypassing the receiver integrity-level
        // checks that apply only to data writes.
        var strictSymInvokeSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictSymInvokeSetterLabel);
        il.MarkLabel(strictSymNoSetterLabel);
        il.Emit(OpCodes.Ldloc, strictSymDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, symThrowLabel);
        il.Emit(OpCodes.Ldloc, strictSymSetterLocal);
        il.Emit(OpCodes.Brtrue, symThrowLabel); // explicit undefined setter
        il.Emit(OpCodes.Ldloc, strictSymDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, symThrowLabel);
        il.Emit(OpCodes.Br, symCheckObjectStateLabel);

        il.MarkLabel(strictSymInvokeSetterLabel);
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, strictSymSetterLocal);

        il.MarkLabel(symCheckObjectStateLabel);

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

        // $Array indexed writes must perform OrdinarySet's descriptor step
        // before touching dense storage.  Array mutators use SetIndexStrict for
        // their spec-mandated Set(..., Throw=true) operations, so bypassing the
        // PDS here skipped indexed accessors installed by defineProperty and
        // silently overwrote getter-only/non-writable elements.  The non-strict
        // SetIndex path already observes these descriptors; mirror that contract
        // here while retaining strict-mode rejection semantics.
        il.MarkLabel(sharpTSArrayLabel);
        var strictArrayKeyLocal = il.DeclareLocal(_types.String);
        var strictArrayDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var strictArraySetterLocal = il.DeclareLocal(_types.Object);
        var strictArrayRawStoreLabel = il.DefineLabel();
        var strictArrayRejectLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, strictArrayKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictArrayKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, strictArrayDescriptorLocal);
        il.Emit(OpCodes.Ldloc, strictArrayDescriptorLocal);
        il.Emit(OpCodes.Brfalse, strictArrayRawStoreLabel);

        // A callable setter handles the write with the array as receiver.
        il.Emit(OpCodes.Ldloc, strictArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, strictArraySetterLocal);
        il.Emit(OpCodes.Ldloc, strictArraySetterLocal);
        var strictArrayNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictArrayNoSetterLabel);
        il.Emit(OpCodes.Ldloc, strictArraySetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, strictArrayRejectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictArraySetterLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strictArrayNoSetterLabel);
        // Getter-only accessors and non-writable data descriptors reject a
        // Throw=true write.  In the (rare) sloppy call to this helper, retain
        // the normal silent failure behavior.
        il.Emit(OpCodes.Ldloc, strictArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, strictArrayRejectLabel);
        il.Emit(OpCodes.Ldloc, strictArrayDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, strictArrayRejectLabel);
        il.Emit(OpCodes.Br, strictArrayRawStoreLabel);

        il.MarkLabel(strictArrayRejectLabel);
        il.Emit(OpCodes.Ldarg_3);
        var strictArraySloppyReturnLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictArraySloppyReturnLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot assign to read only array element");
        il.MarkLabel(strictArraySloppyReturnLabel);
        il.Emit(OpCodes.Ret);

        // Stage E.2 M6: widened from TSArraySetStrict (int) to
        // TSArraySetStrictLong so large indexes do not truncate.
        il.MarkLabel(strictArrayRawStoreLabel);
        var strictArrayNoInheritedSetterLabel = il.DefineLabel();
        var strictArrayInheritedSetterLocal = il.DeclareLocal(_types.Object);
        var strictArrayPrototypeLocal = il.DeclareLocal(_types.Object);
        var strictArrayPrototypeLoop = il.DefineLabel();
        var strictArrayNextPrototype = il.DefineLabel();

        // An existing dense own element shadows the prototype chain. For a
        // hole, however, OrdinarySet must walk every inherited object looking
        // for an indexed accessor before creating a new own element.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt64", _types.Object));
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brtrue, strictArrayNoInheritedSetterLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Stloc, strictArrayPrototypeLocal);

        il.MarkLabel(strictArrayPrototypeLoop);
        il.Emit(OpCodes.Ldloc, strictArrayPrototypeLocal);
        il.Emit(OpCodes.Brfalse, strictArrayNoInheritedSetterLabel);
        il.Emit(OpCodes.Ldloc, strictArrayPrototypeLocal);
        il.Emit(OpCodes.Ldloc, strictArrayKeyLocal);
        il.Emit(OpCodes.Ldloca, strictArrayInheritedSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, strictArrayNextPrototype);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictArrayInheritedSetterLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strictArrayNextPrototype);
        il.Emit(OpCodes.Ldloc, strictArrayPrototypeLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, strictArrayPrototypeLocal);
        il.Emit(OpCodes.Br, strictArrayPrototypeLoop);

        il.MarkLabel(strictArrayNoInheritedSetterLabel);
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
        // List/$Arguments strict indexed writes use the same PDS descriptor
        // state as Object.defineProperty. The old path unboxed the key as a
        // Double and wrote straight to List.set_Item, so a string key such as
        // "0" threw InvalidCastException before writable:false could produce
        // the required guest TypeError.
        var strictListKeyLocal = il.DeclareLocal(_types.String);
        var strictListIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, strictListKeyLocal);

        // Non-canonical/non-integer keys are ordinary named properties.
        var strictListNumericLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, strictListKeyLocal);
        il.Emit(OpCodes.Ldloca, strictListIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var strictListNamedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictListNamedLabel);
        il.Emit(OpCodes.Ldloc, strictListIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, strictListNamedLabel);
        il.Emit(OpCodes.Ldloc, strictListKeyLocal);
        il.Emit(OpCodes.Ldloca, strictListIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, strictListNumericLabel);

        il.MarkLabel(strictListNamedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictListKeyLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strictListNumericLabel);

        // Check if frozen - in strict mode, throw TypeError.
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

        var strictListDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var strictListSetterLocal = il.DeclareLocal(_types.Object);
        var strictListCanCreateLabel = il.DefineLabel();
        var strictListRawStoreLabel = il.DefineLabel();
        var strictListRejectLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictListKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, strictListDescriptorLocal);
        il.Emit(OpCodes.Ldloc, strictListDescriptorLocal);
        il.Emit(OpCodes.Brfalse, strictListCanCreateLabel);

        // Accessor setter wins.
        il.Emit(OpCodes.Ldloc, strictListDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, strictListSetterLocal);
        il.Emit(OpCodes.Ldloc, strictListSetterLocal);
        var strictListNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictListNoSetterLabel);
        il.Emit(OpCodes.Ldloc, strictListSetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, strictListRejectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictListSetterLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strictListNoSetterLabel);
        il.Emit(OpCodes.Ldloc, strictListDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, strictListRejectLabel);
        il.Emit(OpCodes.Ldloc, strictListDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, strictListRejectLabel);
        // Keep descriptor-backed reads and the live List slot synchronized.
        il.Emit(OpCodes.Ldloc, strictListDescriptorLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Br, strictListRawStoreLabel);

        il.MarkLabel(strictListCanCreateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, strictListKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, strictListRejectLabel);
        il.Emit(OpCodes.Br, strictListRawStoreLabel);

        il.MarkLabel(strictListRejectLabel);
        il.Emit(OpCodes.Ldarg_3);
        var strictListSloppyReturnLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, strictListSloppyReturnLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "Cannot assign to read only arguments element");
        il.MarkLabel(strictListSloppyReturnLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(strictListRawStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldloc, strictListIndexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetArrayElement);
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
