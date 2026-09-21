using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct IsConstructorInputs(
        EmittedFunctionBindingRuntime FunctionBindings,
        EmittedFunctionValueRuntime FunctionValues,
        Type UndefinedType,
        MethodBuilder InvokeMethodUnwrapped
    );

    private readonly record struct ReflectGetInputs(MethodInfo InvokeMethodUnwrapped,
        MethodInfo ObjectGetOwnPropertyDescriptor, MethodInfo HasOwnPropertyHelperMethod,
        MethodInfo GetFunctionMethod, MethodInfo InvokeMethodValue, FieldInfo UndefinedInstance, Type UndefinedType,
        MethodInfo GetProperty);

    private readonly record struct ReflectDeletePropertyInputs(MethodInfo InvokeMethodUnwrapped,
        MethodInfo ObjectGetOwnPropertyDescriptor, MethodInfo ToJsString, MethodInfo ObjectIsExtensible,
        MethodInfo DeleteProperty, Type UndefinedType, MethodInfo GetProperty);

    private readonly record struct ReflectPreventExtensionsInputs(MethodInfo InvokeMethodUnwrapped,
        MethodInfo ObjectPreventExtensions, MethodInfo ObjectIsExtensible, MethodInfo GetProperty);

    private readonly record struct ReflectSetInputs(ProxySetCallInputs ProxySet,
        MethodInfo ObjectGetOwnPropertyDescriptor, MethodInfo IsFrozen, MethodInfo HasOwnPropertyHelperMethod,
        MethodInfo ToJsString, MethodInfo ObjectGetPrototypeOf, MethodInfo IsTruthy, MethodInfo InvokeMethodValue,
        Type UndefinedType, MethodInfo GetProperty, MethodInfo SetProperty);

    private readonly record struct ReflectSetPrototypeOfInputs(MethodInfo CreateException,
        ConstructorInfo TypeErrorConstructor, MethodInfo InvokeMethodUnwrapped, MethodInfo ObjectGetPrototypeOf,
        MethodInfo ObjectSetPrototypeOf, MethodInfo ObjectIsExtensible, Type UndefinedType, Type TSSymbolType,
        MethodInfo GetProperty);

    private readonly record struct ReflectDefinePropertyInputs(MethodInfo InvokeMethodUnwrapped,
        MethodInfo ObjectGetOwnPropertyDescriptor, MethodInfo HasOwnPropertyHelperMethod, MethodInfo ToJsString,
        MethodInfo ObjectDefineProperty, MethodInfo ObjectIsExtensible, MethodInfo GetProperty);

    private readonly record struct ReflectOwnKeysInputs(MethodInfo CreateException,
        ConstructorInfo TypeErrorConstructor, ProxyOwnKeysCallInputs ProxyOwnKeys,
        MethodInfo GetOrdinaryOwnPropertyKeys, MethodInfo GetOwnPropertySymbols, MethodInfo GetOwnPropertyNames,
        Type UndefinedType, Type TSSymbolType);

    private readonly record struct ReflectApplyInputs(MethodInfo InvokeMethodValue);

    private readonly record struct ReflectConstructInputs(MethodInfo CreateException,
        ConstructorInfo TypeErrorConstructor, MethodInfo InvokeMethodUnwrapped, MethodInfo CreateErrorFromTypeOrNull,
        MethodInfo ToNumber, MethodInfo IsConstructorMethod, MethodInfo NewOnFunction, Type UndefinedType,
        MethodInfo GetProperty, EmittedDataViewRuntime? DataView, EmittedPromiseRuntime? Promise);

    private readonly record struct ReflectValueFormMethodsInputs(MethodInfo Get, MethodInfo Set,
        MethodInfo DefineProperty, MethodInfo ObjectGetOwnPropertyDescriptor, MethodInfo ToJsString,
        MethodInfo ObjectGetPrototypeOf, MethodInfo ObjectIsExtensible, MethodInfo HasIn);

    private void EmitReflectGet(TypeBuilder typeBuilder, EmittedReflectRuntime reflect, ReflectGetInputs inputs)
    {
        var method = reflect.Get;
        var il = method.GetILGenerator();

        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "TrapGetCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_6);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stelem_Ref);
                EmitDelegate(2, reflect.Get,
                    typeof(Func<object, string, object, object?>));
                EmitDelegate(3, inputs.GetProperty,
                    typeof(Func<object, string, object?>));
                EmitDelegate(4, inputs.GetFunctionMethod,
                    typeof(Func<object, string, object?>));
                EmitDelegate(5, inputs.ObjectGetOwnPropertyDescriptor,
                    typeof(Func<object, object, object?>));

                void EmitDelegate(int slot, MethodInfo target, Type delegateType)
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
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notProxyLabel);
        var descriptorLocal = il.DeclareLocal(_types.Object);
        var ordinaryGetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brfalse, ordinaryGetLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, ordinaryGetLabel);

        var noGetterFieldLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Call, inputs.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brfalse, noGetterFieldLabel);
        var getterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Stloc, getterLocal);
        var undefinedGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Brfalse, undefinedGetterLabel);
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedGetterLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, inputs.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(undefinedGetterLabel);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noGetterFieldLabel);
        il.MarkLabel(ordinaryGetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReflectDeleteProperty(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        ReflectDeletePropertyInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectDeleteProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]);
        reflectNamespace.DeleteProperty = method;
        var il = method.GetILGenerator();
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.ToJsString);
        il.Emit(OpCodes.Stloc, keyLocal);

        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "TrapDeletePropertyCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_5);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, keyLocal);
                il.Emit(OpCodes.Stelem_Ref);
                EmitDelegate(1, inputs.DeleteProperty,
                    typeof(Func<object, string, bool>));
                EmitDelegate(2, inputs.ObjectGetOwnPropertyDescriptor,
                    typeof(Func<object, object, object?>));
                EmitDelegate(3, inputs.ObjectIsExtensible,
                    typeof(Func<object, bool>));
                EmitDelegate(4, inputs.GetProperty,
                    typeof(Func<object, string, object?>));

                void EmitDelegate(int slot, MethodInfo target, Type delegateType)
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
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notProxyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, inputs.DeleteProperty);
        var deletedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, deletedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, inputs.ObjectGetOwnPropertyDescriptor);
        var descriptorLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brfalse, deletedLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, deletedLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(deletedLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReflectPreventExtensions(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        ReflectPreventExtensionsInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectPreventExtensions",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        reflectNamespace.PreventExtensions = method;
        var il = method.GetILGenerator();

        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "TrapPreventExtensionsCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_3);
                il.Emit(OpCodes.Newarr, _types.Object);
                EmitDelegate(0, inputs.ObjectPreventExtensions,
                    typeof(Func<object, object?>));
                EmitDelegate(1, inputs.ObjectIsExtensible,
                    typeof(Func<object, bool>));
                EmitDelegate(2, inputs.GetProperty,
                    typeof(Func<object, string, object?>));

                void EmitDelegate(int slot, MethodInfo target, Type delegateType)
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
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notProxyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.ObjectPreventExtensions);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ReflectSet: (object target, object key, object? value, object receiver) → bool.
    /// Preserves the receiver for Proxy/OrdinarySet observable operations.
    /// </summary>
    private void EmitReflectSet(TypeBuilder typeBuilder, EmittedReflectAssignment assignment, ReflectSetInputs inputs)
    {
        var method = assignment.Set;

        var il = method.GetILGenerator();
        // Check if target is null
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);

        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.ToJsString);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Proxy target: perform its [[Set]] with the caller-provided receiver.
        var proxyTargetLabel = il.DefineLabel();
        var notProxyTargetLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0),
            proxyTargetLabel, notProxyTargetLabel);
        il.MarkLabel(proxyTargetLabel);
        EmitProxySetCompiledCall(
            il, inputs.ProxySet,
            () => il.Emit(OpCodes.Ldarg_0),
            () => il.Emit(OpCodes.Ldloc, keyLocal),
            () => il.Emit(OpCodes.Ldarg_2),
            () => il.Emit(OpCodes.Ldarg_3));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyTargetLabel);

        // OrdinarySetWithOwnDescriptor: an own accessor on the target invokes
        // its setter with Receiver, rather than defining a data property on
        // Receiver. This is the forwarding path used when a Proxy has no set
        // trap and is also observable through Object.create(proxy).
        var targetDescriptorLocal = il.DeclareLocal(_types.Object);
        var targetDescriptorMissingLabel = il.DefineLabel();
        var ordinaryDataSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, inputs.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, targetDescriptorLocal);
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Brfalse, targetDescriptorMissingLabel);
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, targetDescriptorMissingLabel);

        var noSetterFieldLabel = il.DefineLabel();
        var checkTargetDataDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Call, inputs.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brfalse, noSetterFieldLabel);
        var setterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Ldstr, "set");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Stloc, setterLocal);
        var hasSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, setterLocal);
        il.Emit(OpCodes.Brfalse, hasSetterLabel);
        il.Emit(OpCodes.Ldloc, setterLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        var invokeSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, invokeSetterLabel);
        il.MarkLabel(hasSetterLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(invokeSetterLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, setterLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, inputs.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noSetterFieldLabel);
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Ldstr, "get");
        il.Emit(OpCodes.Call, inputs.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brfalse, checkTargetDataDescriptorLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // A present, non-writable data descriptor makes OrdinarySet return
        // false. This covers intrinsic descriptors such as RegExp.global,
        // String wrapper indices/length, and function name/length.
        il.MarkLabel(checkTargetDataDescriptorLabel);
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Call, inputs.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brfalse, ordinaryDataSetLabel);
        il.Emit(OpCodes.Ldloc, targetDescriptorLocal);
        il.Emit(OpCodes.Ldstr, "writable");
        il.Emit(OpCodes.Call, inputs.GetProperty);
        il.Emit(OpCodes.Call, inputs.IsTruthy);
        il.Emit(OpCodes.Brtrue, ordinaryDataSetLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // No own descriptor: continue OrdinarySet on the target prototype,
        // retaining the original Receiver. This is how inherited read-only
        // accessors such as RegExp.prototype.global reject assignment.
        il.MarkLabel(targetDescriptorMissingLabel);
        var targetPrototypeLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, targetPrototypeLocal);
        il.Emit(OpCodes.Ldloc, targetPrototypeLocal);
        il.Emit(OpCodes.Brfalse, ordinaryDataSetLabel);
        il.Emit(OpCodes.Ldloc, targetPrototypeLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, ordinaryDataSetLabel);
        il.Emit(OpCodes.Ldloc, targetPrototypeLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, assignment.Set);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(ordinaryDataSetLabel);

        // Check if target is frozen
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.IsFrozen);
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFrozenLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFrozenLabel);

        // The common no-explicit-receiver case can use the existing ordinary
        // property store directly.
        var distinctReceiverLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Bne_Un, distinctReceiverLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, inputs.SetProperty);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // With a distinct receiver, OrdinarySet defines/updates the property
        // on Receiver. Probe its own descriptor first (observable for Proxy),
        // then send a partial or complete descriptor through
        // [[DefineOwnProperty]] as required.
        il.MarkLabel(distinctReceiverLabel);
        var receiverDescriptorLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, inputs.ObjectGetOwnPropertyDescriptor);
        il.Emit(OpCodes.Stloc, receiverDescriptorLocal);

        var receiverMissingLabel = il.DefineLabel();
        var descriptorReadyLabel = il.DefineLabel();
        var descriptorLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, receiverDescriptorLocal);
        il.Emit(OpCodes.Brfalse, receiverMissingLabel);
        il.Emit(OpCodes.Ldloc, receiverDescriptorLocal);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brtrue, receiverMissingLabel);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Br, descriptorReadyLabel);

        il.MarkLabel(receiverMissingLabel);

        // CreateDataProperty performs Receiver.[[DefineOwnProperty]] directly.
        // Do not preflight [[IsExtensible]]: a Proxy receiver must observe only
        // its defineProperty trap here, and that trap owns the invariant check.
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        void EmitTrueDescriptorField(string name)
        {
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.DictionaryStringObject, "set_Item"));
        }
        EmitTrueDescriptorField("writable");
        EmitTrueDescriptorField("enumerable");
        EmitTrueDescriptorField("configurable");

        il.MarkLabel(descriptorReadyLabel);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Call, assignment.DefineProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ReflectSetPrototypeOf: (object target, object? proto) → bool
    /// Tries to set prototype; returns false if not extensible.
    /// </summary>
    private void EmitReflectSetPrototypeOf(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        FieldBuilder prototypeStoreField, FieldBuilder nonExtensibleObjectsField, ReflectSetPrototypeOfInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectSetPrototypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        reflectNamespace.SetPrototypeOf = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Boolean);

        // Check if target is null
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);

        // Validate proto before invoking the target's [[SetPrototypeOf]]. This
        // completion must not be converted to Reflect's boolean false result.
        // CLR null represents JavaScript null and is the only primitive allowed.
        var protoValidLabel = il.DefineLabel();
        var invalidProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, protoValidLabel);
        void RejectProtoType(Type primitiveType)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, primitiveType);
            il.Emit(OpCodes.Brtrue, invalidProtoLabel);
        }
        RejectProtoType(inputs.UndefinedType);
        RejectProtoType(_types.Boolean);
        RejectProtoType(_types.Double);
        RejectProtoType(_types.Int32);
        RejectProtoType(_types.String);
        RejectProtoType(inputs.TSSymbolType);
        RejectProtoType(_types.BigInteger);
        il.Emit(OpCodes.Br, protoValidLabel);
        il.MarkLabel(invalidProtoLabel);
        GuestErrorEmitter.ThrowError(
            il, inputs.CreateException, inputs.TypeErrorConstructor, "Reflect.setPrototypeOf prototype must be an object or null");
        il.MarkLabel(protoValidLabel);

        // Reflect returns the proxy [[SetPrototypeOf]] boolean directly. Do
        // not preflight [[IsExtensible]]: the proxy algorithm calls it only
        // after a truthy trap result, and that ordering is observable.
        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "TrapSetPrototypeOfCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_5);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stelem_Ref);
                EmitDelegate(1, inputs.ObjectSetPrototypeOf,
                    typeof(Func<object, object?, object?>));
                EmitDelegate(2, inputs.ObjectIsExtensible,
                    typeof(Func<object, bool>));
                EmitDelegate(3, inputs.ObjectGetPrototypeOf,
                    typeof(Func<object, object?>));
                EmitDelegate(4, inputs.GetProperty,
                    typeof(Func<object, string, object?>));

                void EmitDelegate(int slot, MethodInfo target, Type delegateType)
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
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyLabel);

        // ObjectSetPrototypeOf performs SameValue(proto, current) before its
        // extensibility check, so a non-extensible object succeeds when the
        // requested prototype is already current. Other ordinary rejection is
        // translated to Reflect's false result.
        // try { ObjectSetPrototypeOf(target, proto); return true; }
        // catch { return false; }
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.ObjectSetPrototypeOf);
        il.Emit(OpCodes.Pop); // ObjectSetPrototypeOf returns the object; discard

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ReflectDefineProperty: (object target, object key, object descriptor) → bool
    /// Tries to define a property; returns false on failure.
    /// </summary>
    private void EmitReflectDefineProperty(TypeBuilder typeBuilder, EmittedReflectAssignment assignment,
        ReflectDefinePropertyInputs inputs)
    {
        var method = assignment.DefineProperty;

        // The late-bound SharpTSProxy bridge accepts a delegate returning
        // object. A bool-returning method pointer is not delegate-compatible
        // with that signature, so expose a tiny boxing adapter for recursive
        // trapless forwarding through nested proxies.
        var objectAdapter = typeBuilder.DefineMethod(
            "ReflectDefinePropertyObjectAdapter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        assignment.DefinePropertyObjectAdapter = objectAdapter;
        var adapterIl = objectAdapter.GetILGenerator();
        adapterIl.Emit(OpCodes.Ldarg_0);
        adapterIl.Emit(OpCodes.Ldarg_1);
        adapterIl.Emit(OpCodes.Ldarg_2);
        adapterIl.Emit(OpCodes.Call, method);
        adapterIl.Emit(OpCodes.Box, _types.Boolean);
        adapterIl.Emit(OpCodes.Ret);

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Boolean);

        // Proxy [[DefineOwnProperty]] returns the trap boolean directly, but
        // invariant violations and abrupt trap completions must propagate.
        // Do this before the ordinary Object.defineProperty wrapper below,
        // whose catch converts only ordinary definition rejection to false.
        var reflectDefineKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.ToJsString);
        il.Emit(OpCodes.Stloc, reflectDefineKeyLocal);
        var proxyDefineLabel = il.DefineLabel();
        var ordinaryDefineLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0),
            proxyDefineLabel, ordinaryDefineLabel);
        il.MarkLabel(proxyDefineLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "TrapDefinePropertyCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_7);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, reflectDefineKeyLocal);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Stelem_Ref);
                EmitDelegate(2, assignment.DefinePropertyObjectAdapter,
                    typeof(Func<object, object, object, object?>));
                EmitDelegate(3, inputs.ObjectGetOwnPropertyDescriptor,
                    typeof(Func<object, object, object?>));
                EmitDelegate(4, inputs.ObjectIsExtensible,
                    typeof(Func<object, bool>));
                EmitDelegate(5, inputs.GetProperty,
                    typeof(Func<object, string, object?>));
                EmitDelegate(6, inputs.HasOwnPropertyHelperMethod,
                    typeof(Func<object, string, bool>));

                void EmitDelegate(int slot, MethodInfo target, Type delegateType)
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
        il.Emit(OpCodes.Ret);
        il.MarkLabel(ordinaryDefineLabel);

        // try { ObjectDefineProperty(target, key, descriptor); return true; }
        // catch { return false; }
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, inputs.ObjectDefineProperty);
        il.Emit(OpCodes.Pop); // ObjectDefineProperty returns the object; discard

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ReflectOwnKeys: (object target) → object (List of keys including symbol keys)
    /// </summary>
    private void EmitReflectOwnKeys(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        ReflectOwnKeysInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectOwnKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        reflectNamespace.OwnKeys = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var validTargetLabel = il.DefineLabel();
        var invalidTargetLabel = il.DefineLabel();

        // Create result list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Reflect methods require an Object target; unlike Object.ownKeys-like
        // helpers they do not apply ToObject to primitives.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, invalidTargetLabel);
        Type[] primitiveTargetTypes =
        [
            inputs.UndefinedType, _types.String, _types.Boolean,
            _types.Byte, _types.SByte, _types.Int16, _types.UInt16,
            _types.Int32, _types.UInt32, _types.Int64, _types.UInt64,
            _types.Single, _types.Double, _types.Decimal, _types.BigInteger,
            inputs.TSSymbolType,
        ];
        foreach (Type primitiveType in primitiveTargetTypes)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, primitiveType);
            il.Emit(OpCodes.Brtrue, invalidTargetLabel);
        }
        il.Emit(OpCodes.Br, validTargetLabel);
        il.MarkLabel(invalidTargetLabel);
        GuestErrorEmitter.ThrowError(
            il, inputs.CreateException, inputs.TypeErrorConstructor, "Reflect.ownKeys called on non-object");
        il.MarkLabel(validTargetLabel);

        // Reflect.ownKeys consumes the complete [[OwnPropertyKeys]] list. A
        // proxy trap may freely interleave strings and Symbols, so dispatch it
        // once and return that validated list verbatim. Splitting this through
        // getOwnPropertyNames/getOwnPropertySymbols would both invoke the trap
        // twice and reorder the result by key kind.
        var notProxyLabel = il.DefineLabel();
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyOwnKeysCompiledCall(
            il, inputs.ProxyOwnKeys, () => il.Emit(OpCodes.Ldarg_0));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyLabel);

        // OrdinaryOwnPropertyKeys already applies canonical array-index order,
        // preserves chronological string order, and appends Symbols. Reuse it
        // for every ordinary carrier instead of rebuilding dictionaries here.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.GetOrdinaryOwnPropertyKeys);
        il.Emit(OpCodes.Ret);

        // Anonymous revocation functions are represented as Func<object[],object>.
        // Their own-key order follows OrdinaryOwnPropertyKeys for functions:
        // length before name.
        var notDelegateFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Brfalse, notDelegateFunctionLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDelegateFunctionLabel);

        // Check if target is Dictionary<string, object?>
        var isDictLabel = il.DefineLabel();
        var notDictLabel = il.DefineLabel();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brtrue, isDictLabel);
        il.Emit(OpCodes.Br, notDictLabel);

        // Dictionary path: iterate keys
        il.MarkLabel(isDictLabel);
        {
            var enumeratorType = typeof(Dictionary<string, object?>.Enumerator);
            var keyValuePairType = _types.KeyValuePairStringObject;
            var enumeratorLocal = il.DeclareLocal(enumeratorType);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.DictionaryStringObject, "GetEnumerator"));
            il.Emit(OpCodes.Stloc, enumeratorLocal);

            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
            il.Emit(OpCodes.Brfalse, loopEnd);

            // Get current key and add to result
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
            var kvpLocal = il.DeclareLocal(keyValuePairType);
            il.Emit(OpCodes.Stloc, kvpLocal);
            il.Emit(OpCodes.Ldloca, kvpLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, loopStart);

            il.MarkLabel(loopEnd);
        }

        // Also get symbol keys via GetOwnPropertySymbols
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.GetOwnPropertySymbols);
        // GetOwnPropertySymbols returns a List<object?>; add all to result
        var symbolList = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, symbolList);
        var noSymbolsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symbolList);
        il.Emit(OpCodes.Brfalse, noSymbolsLabel);

        // AddRange
        var addRangeMethod = _types.GetMethod(_types.ListOfObject, "AddRange");
        if (addRangeMethod != null)
        {
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, symbolList);
            il.Emit(OpCodes.Callvirt, addRangeMethod);
        }
        il.MarkLabel(noSymbolsLabel);

        // Return result wrapped in list
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Non-dict path: use GetKeys for string keys
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.GetOwnPropertyNames);
        // GetKeys returns a List<object?> (array of keys); add all to result
        var keysResult = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, keysResult);
        var noKeysLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keysResult);
        il.Emit(OpCodes.Brfalse, noKeysLabel);

        if (addRangeMethod != null)
        {
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, keysResult);
            il.Emit(OpCodes.Callvirt, addRangeMethod);
        }
        il.MarkLabel(noKeysLabel);

        // Also get symbol keys for non-dict path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.GetOwnPropertySymbols);
        var symbolList2 = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, symbolList2);
        var noSymbols2Label = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symbolList2);
        il.Emit(OpCodes.Brfalse, noSymbols2Label);

        if (addRangeMethod != null)
        {
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, symbolList2);
            il.Emit(OpCodes.Callvirt, addRangeMethod);
        }
        il.MarkLabel(noSymbols2Label);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ReflectApply: (object target, object? thisArg, object argsList) → object?
    /// Converts argsList to object[] and invokes target with thisArg.
    /// </summary>
    private void EmitReflectApply(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        ReflectApplyInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectApply",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        reflectNamespace.Apply = method;

        var il = method.GetILGenerator();
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // Convert argsList (arg2) to object[]
        // Check if it's a List<object?>
        var isListLabel = il.DefineLabel();
        var gotArgsLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brtrue, isListLabel);

        // Not a list - use empty args
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Br, gotArgsLabel);

        // Is a list - call ToArray()
        il.MarkLabel(isListLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray")!);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.MarkLabel(gotArgsLabel);

        il.Emit(OpCodes.Ldarg_1); // thisArg
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, inputs.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.IsConstructor(object fn) -&gt; bool</c>: returns true iff
    /// <paramref name="fn"/> has the [[Construct]] internal slot per ECMA-262.
    /// In compiled mode:
    /// <list type="bullet">
    /// <item>System.Type (a compiled class reference) → true.</item>
    /// <item>$TSFunction wrapping a method whose declaring type is <c>$Runtime</c>
    ///   (built-in helper like <c>Array.prototype.filter</c>) → false.</item>
    /// <item>$TSFunction wrapping anything else (user function decl) → true.</item>
    /// <item>$BoundTSFunction → recurse on the inner target.</item>
    /// <item>Anything else (null, primitives, plain objects) → false.</item>
    /// </list>
    /// Used by <c>Reflect.construct</c>'s newTarget validation and the Test262
    /// <c>isConstructor.js</c> harness which checks via
    /// <c>Reflect.construct(emptyFn, [], target)</c>.
    /// </summary>
    private void EmitIsConstructor(
        TypeBuilder typeBuilder,
        EmittedFunctionIntrospectionRuntime functionIntrospection,
        IsConstructorInputs inputs
    )
    {
        var method = typeBuilder.DefineMethod(
            "IsConstructor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        functionIntrospection.IsConstructor = method;

        var il = method.GetILGenerator();

        // null/undefined → false
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNullLabel);

        var notUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefinedLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notUndefinedLabel);

        // A Proxy has [[Construct]] exactly when its (possibly nested) target
        // does. Revocation is observable and must throw here.
        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "HasConstructableTarget", () =>
            {
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, functionIntrospection.IsConstructor);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, bool>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
            });
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyLabel);

        // System.Type (class reference) → true
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTypeLabel);

        // $TSFunction → check method's declaring type
        var notTSFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // var mi = ((TSFunction)fn).GetMethodInfo();
        // if (mi == null) return true; // user function decl missing metadata, default callable
        // var dt = mi.DeclaringType;
        // if (dt != null && dt.Name == "$Runtime") return false; // built-in helper
        // return true;
        var miLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.FunctionValues.Type);
        il.Emit(OpCodes.Callvirt, inputs.FunctionValues.GetMethodInfo);
        il.Emit(OpCodes.Stloc, miLocal);
        var miNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Brfalse, miNullLabel);
        // dt = mi.DeclaringType
        var dtLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "DeclaringType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, dtLocal);
        var dtNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dtLocal);
        il.Emit(OpCodes.Brfalse, dtNullLabel);
        // Compare dt.Name against the set of emitted-runtime helper class
        // names. Built-in protocol methods (e.g. RegExp.prototype[Symbol.split]
        // lives on $RegExp; Array.prototype.* on $Array) are NOT constructors
        // per ECMA-262. Test262's not-a-constructor.js harness probes via
        // `isConstructor(RegExp.prototype[Symbol.split])` → must be false.
        // User code lives on $Program / $Module / $DC* / class types and
        // must remain constructable, so we don't use a `$`-prefix shortcut.
        var dtNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, dtLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "Name").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, dtNameLocal);

        var notRuntimeLabel = il.DefineLabel();
        var stringEqMethod = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        string[] runtimeHelperClasses =
        [
            "$Runtime", "$RegExp", "$Array", "$Object", "$Promise", "$TSPromise",
            "$Date", "$TSDate", "$Map", "$Set", "$WeakMap", "$WeakSet", "$WeakRef",
            "$Error", "$TypeError", "$RangeError", "$ReferenceError",
            "$SyntaxError", "$URIError", "$EvalError", "$AggregateError",
            "$Buffer", "$Headers", "$Hash", "$Hmac", "$NodeError",
            "$FinalizationRegistry", "$DataView", "$ArrayBuffer",
            "$PromiseFinallyFunctions", "$PromiseFinallyValueThunk",
        ];
        var declaringTypeIsRuntimeLabel = il.DefineLabel();
        foreach (var name in runtimeHelperClasses)
        {
            il.Emit(OpCodes.Ldloc, dtNameLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, stringEqMethod);
            il.Emit(OpCodes.Brtrue, declaringTypeIsRuntimeLabel);
        }
        il.Emit(OpCodes.Br, notRuntimeLabel);

        il.MarkLabel(declaringTypeIsRuntimeLabel);
        // Declaring type is an emitted-runtime helper class. One exception
        // (#224): the Intl factory helpers (CreateIntlNumberFormat etc.) ARE
        // constructors per ECMA-402 — `new Intl.NumberFormat(...)` through a
        // value-position alias routes them into NewOnFunction, whose
        // IsConstructor gate would otherwise reject them.
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "CreateIntl");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", _types.String));
        il.Emit(OpCodes.Brtrue, notRuntimeLabel);
        // Node exposes TLSSocket and createSecureContext as constructible functions.
        // Restrict the exception to their exact runtime factories.
        il.Emit(OpCodes.Ldloc, dtNameLocal);
        il.Emit(OpCodes.Ldstr, "$Runtime");
        il.Emit(OpCodes.Call, stringEqMethod);
        var notTlsConstructor = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTlsConstructor);
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "TlsCreateSocket");
        il.Emit(OpCodes.Call, stringEqMethod);
        il.Emit(OpCodes.Brtrue, notRuntimeLabel);
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "TlsCreateSecureContext");
        il.Emit(OpCodes.Call, stringEqMethod);
        il.Emit(OpCodes.Brtrue, notRuntimeLabel);
        il.MarkLabel(notTlsConstructor);
        // → not constructable
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notRuntimeLabel);
        il.MarkLabel(dtNullLabel);
        il.MarkLabel(miNullLabel);
        // Default for $TSFunction: constructable
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTSFunctionLabel);

        // $BoundTSFunction has [[Construct]] exactly when its target does.
        // The emitted wrapper retains that target, so recurse through it just
        // like the Proxy case above (ECMA-262 BoundFunctionCreate).
        var notBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, inputs.FunctionBindings.BoundType);
        il.Emit(OpCodes.Brfalse, notBoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, inputs.FunctionBindings.BoundType);
        il.Emit(OpCodes.Ldfld, inputs.FunctionBindings.BoundTargetField);
        il.Emit(OpCodes.Call, functionIntrospection.IsConstructor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoundLabel);

        // Default: not constructable
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ReflectConstruct: (object target, object argsList, object? newTarget) → object?
    /// Per ECMA-262 §28.1.4: throws TypeError if target or newTarget is not a
    /// constructor. Used by Test262's isConstructor.js harness via
    /// <c>Reflect.construct(emptyFn, [], target)</c>.
    /// Converts argsList to object[] and invokes target as a constructor.
    /// </summary>
    private void EmitReflectConstruct(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        ReflectConstructInputs inputs)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectConstruct",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        reflectNamespace.Construct = method;

        var il = method.GetILGenerator();
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var newTargetLocal = il.DeclareLocal(_types.Object);

        // Validate target via IsConstructor — throws TypeError if not constructable.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.IsConstructorMethod);
        var targetOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, targetOkLabel);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor, "Reflect.construct: target is not a constructor");
        il.MarkLabel(targetOkLabel);

        // newTarget defaults to target if null/undefined.
        var haveNewTargetLabel = il.DefineLabel();
        var newTargetSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, haveNewTargetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, newTargetLocal);
        il.Emit(OpCodes.Br, newTargetSetLabel);
        il.MarkLabel(haveNewTargetLabel);
        // Distinguish $Undefined from a real value
        var notUndefNewTargetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefNewTargetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, newTargetLocal);
        il.Emit(OpCodes.Br, newTargetSetLabel);
        il.MarkLabel(notUndefNewTargetLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, newTargetLocal);
        il.MarkLabel(newTargetSetLabel);

        // Validate newTarget via IsConstructor — throws TypeError if not constructable.
        il.Emit(OpCodes.Ldloc, newTargetLocal);
        il.Emit(OpCodes.Call, inputs.IsConstructorMethod);
        var newTargetOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, newTargetOkLabel);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor, "Reflect.construct: newTarget is not a constructor");
        il.MarkLabel(newTargetOkLabel);

        // Convert argsList (arg1) to object[]
        var isListLabel = il.DefineLabel();
        var gotArgsLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brtrue, isListLabel);

        // Not a list - use empty args
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Br, gotArgsLabel);

        // Is a list - call ToArray()
        il.MarkLabel(isListLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray")!);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.MarkLabel(gotArgsLabel);

        var proxyTargetLabel = il.DefineLabel();
        var notProxyTargetLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0),
            proxyTargetLabel, notProxyTargetLabel);
        il.MarkLabel(proxyTargetLabel);
        EmitProxyMethodCallUnwrapped(
            il, inputs.InvokeMethodUnwrapped, () => il.Emit(OpCodes.Ldarg_0),
            "TrapConstructCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_4);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, argsLocal);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldloc, newTargetLocal);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, inputs.NewOnFunction);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, object?[], object?>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_3);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, inputs.GetProperty);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, string, object?>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
            });
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProxyTargetLabel);

        // Check if target is a System.Type (compiled class reference)
        var isTypeLabel = il.DefineLabel();
        var notTypeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, isTypeLabel);

        // Not a Type - use the ordinary function-construction protocol.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, inputs.NewOnFunction);
        il.Emit(OpCodes.Ret);

        // Is a Type - use Activator.CreateInstance(type, args)
        il.MarkLabel(isTypeLabel);

        // Emitted native-error types require the JS-facing factory for
        // optional arguments and own message/cause descriptors. A null result
        // means this is some other Type and normal construction continues.
        var notErrorTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, inputs.CreateErrorFromTypeOrNull);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notErrorTypeLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorTypeLabel);
        il.Emit(OpCodes.Pop);

        // %Promise% also has a JavaScript-facing adapter rather than a CLR
        // constructor that accepts its executor. Most importantly, the missing
        // executor must throw before GetPrototypeFromConstructor(newTarget), so
        // a bound newTarget with an abrupt prototype getter cannot mask the
        // required TypeError (ECMA-262 Promise constructor steps 2-3).
        if (inputs.Promise is not null)
        {
            var notPromiseTypeLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            // The value-position %Promise% token is Task<object?>; $TSPromise
            // is the wrapper used for subclass instances.
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "op_Equality", _types.Type, _types.Type));
            il.Emit(OpCodes.Brfalse, notPromiseTypeLabel);

            var promiseHasExecutorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bgt, promiseHasExecutorLabel);
            GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor, "Promise executor is not callable");
            il.MarkLabel(promiseHasExecutorLabel);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Call, inputs.Promise.FromExecutor);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notPromiseTypeLabel);
        }

        // Built-in DataView uses a JavaScript-facing constructor adapter rather
        // than a CLR constructor whose signature Activator can bind directly.
        // Dispatch through that adapter so ToNumber/default-argument handling and,
        // critically, the offset-vs-buffer validation happen before any eventual
        // GetPrototypeFromConstructor(newTarget) work (ECMA-262 DataView steps 3-10).
        if (inputs.DataView is { } dataView)
        {
            var notDataViewTypeLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Ldtoken, dataView.Type);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "op_Equality", _types.Type, _types.Type));
            il.Emit(OpCodes.Brfalse, notDataViewTypeLabel);

        var dataViewHasBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, dataViewHasBufferLabel);
        GuestErrorEmitter.ThrowError(il, inputs.CreateException, inputs.TypeErrorConstructor, "DataView constructor requires a buffer");
        il.MarkLabel(dataViewHasBufferLabel);

        // buffer
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);

        // byteOffset (default 0, otherwise ToNumber)
        var dataViewDefaultOffsetLabel = il.DefineLabel();
        var dataViewHaveOffsetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, dataViewDefaultOffsetLabel);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, inputs.ToNumber);
        il.Emit(OpCodes.Br, dataViewHaveOffsetLabel);
        il.MarkLabel(dataViewDefaultOffsetLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.MarkLabel(dataViewHaveOffsetLabel);

        // byteLength (missing/undefined is represented as null by the adapter)
        var dataViewNoLengthLabel = il.DefineLabel();
        var dataViewHaveLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, dataViewNoLengthLabel);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, dataViewHaveLengthLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(dataViewNoLengthLabel);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(dataViewHaveLengthLabel);
            il.Emit(OpCodes.Call, dataView.Create);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notDataViewTypeLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Activator), "CreateInstance", _types.Type, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits callable value-form adapters for the Reflect namespace. The
    /// direct <c>Reflect.x(...)</c> fast paths already own argument evaluation;
    /// these adapters serve property reads, aliases, and the bare Reflect
    /// singleton while preserving whether an optional receiver/newTarget was
    /// actually supplied.
    /// </summary>
    private void EmitReflectValueFormMethods(TypeBuilder typeBuilder, EmittedReflectNamespace reflectNamespace,
        ReflectValueFormMethodsInputs inputs)
    {
        MethodBuilder Define(string name, Action<ILGenerator> emitBody)
        {
            var method = typeBuilder.DefineMethod(
                $"ReflectValueForm_{name}",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.ObjectArray]);
            emitBody(method.GetILGenerator());
            reflectNamespace.RegisterValueFormMethod(name, method);
            return method;
        }

        static void EmitArgument(ILGenerator il, int index)
        {
            var present = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Bgt, present);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(present);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(done);
        }

        static void EmitArgumentOrLocal(
            ILGenerator il, int index, LocalBuilder fallback)
        {
            var present = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Bgt, present);
            il.Emit(OpCodes.Ldloc, fallback);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(present);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(done);
        }

        static void BoxBoolean(ILGenerator il) => il.Emit(OpCodes.Box, typeof(bool));

        Define("apply", il =>
        {
            EmitArgument(il, 0);
            EmitArgument(il, 1);
            EmitArgument(il, 2);
            il.Emit(OpCodes.Call, reflectNamespace.Apply);
            il.Emit(OpCodes.Ret);
        });
        Define("construct", il =>
        {
            EmitArgument(il, 0);
            EmitArgument(il, 1);
            EmitArgument(il, 2);
            il.Emit(OpCodes.Call, reflectNamespace.Construct);
            il.Emit(OpCodes.Ret);
        });
        Define("defineProperty", il =>
        {
            EmitArgument(il, 0);
            EmitArgument(il, 1);
            EmitArgument(il, 2);
            il.Emit(OpCodes.Call, inputs.DefineProperty);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
        Define("deleteProperty", il =>
        {
            EmitArgument(il, 0);
            EmitArgument(il, 1);
            il.Emit(OpCodes.Call, reflectNamespace.DeleteProperty);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
        Define("get", il =>
        {
            var target = il.DeclareLocal(_types.Object);
            EmitArgument(il, 0);
            il.Emit(OpCodes.Stloc, target);
            il.Emit(OpCodes.Ldloc, target);
            EmitArgument(il, 1);
            il.Emit(OpCodes.Call, inputs.ToJsString);
            EmitArgumentOrLocal(il, 2, target);
            il.Emit(OpCodes.Call, inputs.Get);
            il.Emit(OpCodes.Ret);
        });
        Define("getOwnPropertyDescriptor", il =>
        {
            EmitArgument(il, 0);
            EmitArgument(il, 1);
            il.Emit(OpCodes.Call, inputs.ObjectGetOwnPropertyDescriptor);
            il.Emit(OpCodes.Ret);
        });
        Define("getPrototypeOf", il =>
        {
            EmitArgument(il, 0);
            il.Emit(OpCodes.Call, inputs.ObjectGetPrototypeOf);
            il.Emit(OpCodes.Ret);
        });
        Define("has", il =>
        {
            // HasIn is keyed as (propertyKey, target).
            EmitArgument(il, 1);
            EmitArgument(il, 0);
            il.Emit(OpCodes.Call, inputs.HasIn);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
        Define("isExtensible", il =>
        {
            EmitArgument(il, 0);
            il.Emit(OpCodes.Call, inputs.ObjectIsExtensible);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
        Define("ownKeys", il =>
        {
            EmitArgument(il, 0);
            il.Emit(OpCodes.Call, reflectNamespace.OwnKeys);
            il.Emit(OpCodes.Ret);
        });
        Define("preventExtensions", il =>
        {
            EmitArgument(il, 0);
            il.Emit(OpCodes.Call, reflectNamespace.PreventExtensions);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
        Define("set", il =>
        {
            var target = il.DeclareLocal(_types.Object);
            EmitArgument(il, 0);
            il.Emit(OpCodes.Stloc, target);
            il.Emit(OpCodes.Ldloc, target);
            EmitArgument(il, 1);
            EmitArgument(il, 2);
            EmitArgumentOrLocal(il, 3, target);
            il.Emit(OpCodes.Call, inputs.Set);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
        Define("setPrototypeOf", il =>
        {
            EmitArgument(il, 0);
            EmitArgument(il, 1);
            il.Emit(OpCodes.Call, reflectNamespace.SetPrototypeOf);
            BoxBoolean(il);
            il.Emit(OpCodes.Ret);
        });
    }
}
