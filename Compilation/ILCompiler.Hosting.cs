#pragma warning disable SHARPTS_HOSTING001

using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics.CodeAnalysis;
using SharpTS.Hosting;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitHostedAbi(
        MethodBuilder initializeCore,
        bool initializerAcceptsRuntime = false,
        bool initializerReturnsTask = false)
    {
        if (!_hosted || _hostedFactoryType is not null)
            return;

        const BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        Type baseType = typeof(SharpTSHostedRuntimeBase);

        _hostedRuntimeType = EmitTypeDefinitions.DefineType(
            _moduleBuilder,
            "$SharpTSHostedRuntime",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            baseType);

        Type[] constructorParameters =
        [
            typeof(ISharpTSHostDispatcher),
            typeof(ISharpTSHostLifetime),
            typeof(ISharpTSHostedErrorSink),
        ];
        var runtimeCtor = _hostedRuntimeType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            constructorParameters);
        var il = runtimeCtor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, baseType.GetConstructor(
            instanceNonPublic,
            binder: null,
            constructorParameters,
            modifiers: null)!);
        il.Emit(OpCodes.Ret);

        EmitHostedInitializeOverride(
            initializeCore, baseType, initializerAcceptsRuntime, initializerReturnsTask);
        EmitHostedBooleanHook(
            "TryRunOneGuestMacrotask",
            baseType,
            hookIl =>
            {
                hookIl.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
                hookIl.Emit(OpCodes.Callvirt, _runtime.EventLoopTryRunOne);
            });
        EmitHostedBooleanProperty(
            "HasGuestMacrotasks",
            baseType,
            hookIl =>
            {
                hookIl.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
                hookIl.Emit(OpCodes.Callvirt, _runtime.EventLoopHasQueuedCallbacks);
            });
        EmitHostedVoidHook(
            "DrainGuestMicrotasks",
            baseType,
            hookIl => hookIl.Emit(OpCodes.Call, _runtime.ProcessMicrotasks));
        EmitHostedBooleanProperty(
            "HasGuestMicrotasks",
            baseType,
            hookIl => hookIl.Emit(OpCodes.Call, _runtime.HasMicrotasks));
        EmitHostedBooleanHook(
            "TryRunOneGuestTimer",
            baseType,
            hookIl => hookIl.Emit(OpCodes.Call, _runtime.ProcessOnePendingTimer));
        EmitHostedTimerDelayOverride(baseType);
        EmitHostedVoidHook(
            "RejectGuestWork",
            baseType,
            hookIl => hookIl.Emit(OpCodes.Call, _runtime.EventLoopRejectHosted));
        EmitHostedVoidHook(
            "CancelGuestResources",
            baseType,
            hookIl =>
            {
                hookIl.Emit(OpCodes.Call, _runtime.CancelAllTimers);
                hookIl.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
                hookIl.Emit(OpCodes.Callvirt, _runtime.EventLoopClearHosted);
            });
        EmitHostedLifecycleOverride("EmitGuestBeforeExit", baseType, _runtime.ProcessEmitHostedBeforeExit);
        EmitHostedLifecycleOverride("EmitGuestExit", baseType, _runtime.ProcessEmitHostedExit);

        _hostedFactoryType = EmitTypeDefinitions.DefineType(
            _moduleBuilder,
            "SharpTSHostedProgramFactory",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [typeof(ISharpTSHostedProgramFactory)]);

        var factoryCtor = _hostedFactoryType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        il = factoryCtor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ret);

        PropertyInfo abiProperty = typeof(ISharpTSHostedProgramFactory).GetProperty(
            nameof(ISharpTSHostedProgramFactory.AbiVersion))!;
        var abiGetter = _hostedFactoryType.DefineMethod(
            "get_AbiVersion",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig |
                MethodAttributes.SpecialName,
            _types.Int32,
            Type.EmptyTypes);
        il = abiGetter.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, SharpTSHostedAbi.CurrentVersion);
        il.Emit(OpCodes.Ret);
        _hostedFactoryType.DefineMethodOverride(abiGetter, abiProperty.GetMethod!);
        var emittedAbiProperty = _hostedFactoryType.DefineProperty(
            nameof(ISharpTSHostedProgramFactory.AbiVersion),
            PropertyAttributes.None,
            _types.Int32,
            Type.EmptyTypes);
        emittedAbiProperty.SetGetMethod(abiGetter);

        MethodInfo createContract = typeof(ISharpTSHostedProgramFactory).GetMethod(
            nameof(ISharpTSHostedProgramFactory.Create))!;
        var create = _hostedFactoryType.DefineMethod(
            nameof(ISharpTSHostedProgramFactory.Create),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(ISharpTSHostedRuntime),
            constructorParameters);
        il = create.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Newobj, runtimeCtor);
        il.Emit(OpCodes.Ret);
        _hostedFactoryType.DefineMethodOverride(create, createContract);

        ConstructorInfo markerCtor = typeof(SharpTSHostedProgramAttribute).GetConstructor(
            [typeof(int), typeof(Type)])!;
        _assemblyBuilder.SetCustomAttribute(
            markerCtor,
            CustomAttributeEncoder.Encode(
                markerCtor,
                SharpTSHostedAbi.CurrentVersion,
                _hostedFactoryType));

        EmitHostedNativeAotSuppressions();
    }

    private void EmitHostedNativeAotSuppressions()
    {
        const string justification =
            "The GUI SDK roots the complete generated guest assembly because the emitted " +
            "JavaScript runtime performs name-based dispatch over its own generated members.";
        string[] diagnosticIds =
        [
            "IL2026", "IL2055", "IL2059", "IL2067",
            "IL2070", "IL2072", "IL2075", "IL3050",
        ];
        ConstructorInfo constructor = typeof(UnconditionalSuppressMessageAttribute).GetConstructor(
            [typeof(string), typeof(string)])!;
        PropertyInfo justificationProperty = typeof(UnconditionalSuppressMessageAttribute).GetProperty(
            nameof(UnconditionalSuppressMessageAttribute.Justification))!;
        foreach (string diagnosticId in diagnosticIds)
        {
            _assemblyBuilder.SetCustomAttribute(
                constructor,
                CustomAttributeEncoder.Encode(
                    constructor,
                    ["Trimming/AOT", diagnosticId],
                    (justificationProperty, justification)));
        }
    }

    private void EmitHostedInitializeOverride(
        MethodBuilder initializeCore,
        Type baseType,
        bool initializerAcceptsRuntime,
        bool initializerReturnsTask)
    {
        MethodInfo contract = baseType.GetMethod(
            "InitializeGuestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var method = _hostedRuntimeType!.DefineMethod(
            contract.Name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(Task),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.EventLoopConfigureHosted);
        if (initializerAcceptsRuntime)
            il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, initializeCore);
        if (!initializerReturnsTask)
            il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        _hostedRuntimeType.DefineMethodOverride(method, contract);
    }

    private void EmitHostedBooleanHook(string name, Type baseType, Action<ILGenerator> emitBody)
    {
        MethodInfo contract = baseType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var method = _hostedRuntimeType!.DefineMethod(
            name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);
        _hostedRuntimeType.DefineMethodOverride(method, contract);
    }

    private void EmitHostedBooleanProperty(string name, Type baseType, Action<ILGenerator> emitBody)
    {
        MethodInfo contract = baseType.GetProperty(
            name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!;
        var getter = _hostedRuntimeType!.DefineMethod(
            contract.Name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig |
                MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes);
        var il = getter.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);
        _hostedRuntimeType.DefineMethodOverride(getter, contract);
        var property = _hostedRuntimeType.DefineProperty(
            name, PropertyAttributes.None, _types.Boolean, Type.EmptyTypes);
        property.SetGetMethod(getter);
    }

    private void EmitHostedVoidHook(string name, Type baseType, Action<ILGenerator> emitBody)
    {
        MethodInfo contract = baseType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var method = _hostedRuntimeType!.DefineMethod(
            name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(void),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);
        _hostedRuntimeType.DefineMethodOverride(method, contract);
    }

    private void EmitHostedLifecycleOverride(string name, Type baseType, MethodBuilder target)
    {
        MethodInfo contract = baseType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var method = _hostedRuntimeType!.DefineMethod(
            name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(void),
            [_types.Int32]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        _hostedRuntimeType.DefineMethodOverride(method, contract);
    }

    private void EmitHostedTimerDelayOverride(Type baseType)
    {
        MethodInfo contract = baseType.GetMethod(
            "GetNextGuestTimerDelay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Type nullableTimeSpan = typeof(TimeSpan?);
        var method = _hostedRuntimeType!.DefineMethod(
            contract.Name,
            MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            nullableTimeSpan,
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        var delay = il.DeclareLocal(_types.Int32);
        var none = il.DefineLabel();
        il.Emit(OpCodes.Call, _runtime.GetNextTimerDelay);
        il.Emit(OpCodes.Stloc, delay);
        il.Emit(OpCodes.Ldloc, delay);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, none);
        il.Emit(OpCodes.Ldloc, delay);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, typeof(TimeSpan).GetMethod(
            nameof(TimeSpan.FromMilliseconds), [typeof(double)])!);
        il.Emit(OpCodes.Newobj, nullableTimeSpan.GetConstructor([typeof(TimeSpan)])!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(none);
        var empty = il.DeclareLocal(nullableTimeSpan);
        il.Emit(OpCodes.Ldloca, empty);
        il.Emit(OpCodes.Initobj, nullableTimeSpan);
        il.Emit(OpCodes.Ldloc, empty);
        il.Emit(OpCodes.Ret);
        _hostedRuntimeType.DefineMethodOverride(method, contract);
    }
}
