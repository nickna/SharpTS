using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $EventEmitter class for standalone EventEmitter support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSEventEmitter
/// </summary>
public partial class RuntimeEmitter
{
    // The string key that the errorMonitor symbol stringifies to (see
    // SharpTSEventEmitter.ErrorMonitorKey). Kept byte-identical so interp and
    // compiled store/dispatch errorMonitor listeners under the same key.
    private const string ErrorMonitorKey = "Symbol(nodejs.events.errorMonitor)";

    // Open-generic BCL methods are resolved once and confined to this emission.
    private sealed record EventEmitterGenericMethods(
        MethodInfo ListCountGetter,
        MethodInfo ListGetItem,
        MethodInfo ListRemoveAt,
        MethodInfo ListRemove,
        MethodInfo ListAdd,
        MethodInfo ListInsert,
        MethodInfo ListToArray,
        MethodInfo DictTryGetValue,
        MethodInfo DictRemove,
        MethodInfo DictClear,
        MethodInfo DictAdd,
        MethodInfo DictKeysGetter);

    private void EmitTSEventEmitterClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var events = runtime.EventEmitter;
        // First, emit the ListenerWrapper nested type
        EmitListenerWrapperType(moduleBuilder, events);

        // Define class: public class $EventEmitter (not sealed - stream types inherit from it)
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$EventEmitter",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        events.Type = typeBuilder;

        // Field: private Dictionary<string, List<ListenerWrapper>> _events
        var listType = _types.MakeGenericType(_types.ListOpen, events.ListenerWrapperType);
        var dictType = _types.MakeGenericType(_types.DictionaryOpen, _types.String, listType);
        events.EventsField = typeBuilder.DefineField("_events", dictType, FieldAttributes.Private);

        // Resolve open-generic methods at the original construction boundary.
        var methods = CreateEventEmitterGenericMethods();

        // Field: private int _maxListeners = 0
        events.MaxListenersField = typeBuilder.DefineField("_maxListeners", _types.Int32, FieldAttributes.Private);

        // Field: private bool _captureRejections = false (#1099)
        events.CaptureRejectionsField = typeBuilder.DefineField("_captureRejections", _types.Boolean, FieldAttributes.Private);

        // Static field: public static int DefaultMaxListeners = 10
        var defaultMaxListenersField = typeBuilder.DefineField(
            "DefaultMaxListeners",
            _types.Int32,
            FieldAttributes.Public | FieldAttributes.Static
        );
        events.DefaultMaxListeners = defaultMaxListenersField;

        // Constructor: public $EventEmitter()
        EmitTSEventEmitterCtor(typeBuilder, events, dictType, listType);

        // Virtual hook must be defined before AddListenerInternal (which calls it)
        EmitTSEventEmitterOnListenerAdded(typeBuilder, events);

        // Instance methods - AddListenerInternal must be defined first as it's called by On/Once/Prepend methods
        EmitTSEventEmitterAddListenerInternal(methods, typeBuilder, events, listType, dictType);
        // #1099 helpers. Emit and RouteCaptureRejection are mutually recursive
        // (Emit invokes RouteCaptureRejection; RouteCaptureRejection re-emits
        // 'error'), so define RouteCaptureRejection's handle before Emit's body
        // and fill its body afterwards.
        EmitTSEventEmitterEnableCaptureRejections(typeBuilder, events);
        DefineTSEventEmitterRouteCaptureRejection(typeBuilder, events);
        EmitTSEventEmitterOn(typeBuilder, events, listType);
        EmitTSEventEmitterOnce(typeBuilder, events, listType);
        EmitTSEventEmitterOff(methods, typeBuilder, events, listType, dictType);
        EmitTSEventEmitterEmit(methods, typeBuilder, runtime, listType);
        FillTSEventEmitterRouteCaptureRejection(runtime);
        EmitTSEventEmitterRemoveAllListeners(methods, typeBuilder, events, dictType);
        EmitTSEventEmitterListeners(methods, typeBuilder, runtime, listType);
        EmitTSEventEmitterListenerCount(methods, typeBuilder, events, listType);
        EmitTSEventEmitterEventNames(methods, typeBuilder, runtime, dictType);
        EmitTSEventEmitterPrependListener(typeBuilder, events, listType);
        EmitTSEventEmitterPrependOnceListener(typeBuilder, events, listType);
        EmitTSEventEmitterSetMaxListeners(typeBuilder, events);
        EmitTSEventEmitterGetMaxListeners(typeBuilder, events);

        // Aliases for Node.js compatibility (used by runtime dispatch when type is not EventEmitter)
        EmitTSEventEmitterAddListener(typeBuilder, events);
        EmitTSEventEmitterRemoveListener(typeBuilder, events);
        EmitTSEventEmitterRawListeners(typeBuilder, runtime);

        // Set static constructor to initialize DefaultMaxListeners
        EmitTSEventEmitterStaticCtor(typeBuilder, defaultMaxListenersField);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Resolve methods from open generic BCL types for this emission.
    /// These are used with TypeBuilder.GetMethod to get the closed generic methods.
    /// </summary>
    private static EventEmitterGenericMethods CreateEventEmitterGenericMethods()
    {
        // List<T> methods from open generic type
        var openListType = typeof(List<>);
        var listCountGetter = openListType.GetProperty("Count")!.GetGetMethod()!;
        var listGetItem = openListType.GetMethod("get_Item", [typeof(int)])!;
        var listRemoveAt = openListType.GetMethod("RemoveAt", [typeof(int)])!;
        var listRemove = openListType.GetMethod("Remove", openListType.GetGenericArguments())!;
        var listAdd = openListType.GetMethod("Add", openListType.GetGenericArguments())!;
        var listInsert = openListType.GetMethod("Insert", [typeof(int), openListType.GetGenericArguments()[0]])!;
        var listToArray = openListType.GetMethod("ToArray", Type.EmptyTypes)!;

        // Dictionary<TKey, TValue> methods from open generic type
        var openDictType = typeof(Dictionary<,>);
        var dictGenericArgs = openDictType.GetGenericArguments();
        var dictTryGetValue = openDictType.GetMethod("TryGetValue", [dictGenericArgs[0], dictGenericArgs[1].MakeByRefType()])!;
        var dictRemove = openDictType.GetMethod("Remove", [dictGenericArgs[0]])!;
        var dictClear = openDictType.GetMethod("Clear", Type.EmptyTypes)!;
        var dictAdd = openDictType.GetMethod("Add", [dictGenericArgs[0], dictGenericArgs[1]])!;
        var dictKeysGetter = openDictType.GetProperty("Keys")!.GetGetMethod()!;
        return new EventEmitterGenericMethods(
            listCountGetter, listGetItem, listRemoveAt, listRemove, listAdd, listInsert, listToArray,
            dictTryGetValue, dictRemove, dictClear, dictAdd, dictKeysGetter);
    }

    /// <summary>
    /// Gets a method on a constructed generic type using TypeBuilder.GetMethod.
    /// </summary>
    private static MethodInfo GetListMethod(Type listType, MethodInfo openMethod)
        => EmitterTypeHelpers.ResolveMethod(listType, openMethod);

    /// <summary>
    /// Gets a method on a constructed generic Dictionary type using TypeBuilder.GetMethod.
    /// </summary>
    private static MethodInfo GetDictMethod(Type dictType, MethodInfo openMethod)
        => EmitterTypeHelpers.ResolveMethod(dictType, openMethod);

    private void EmitListenerWrapperType(ModuleBuilder moduleBuilder, EmittedEventEmitterRuntime events)
    {
        // Define nested class: public sealed class $ListenerWrapper
        events.ListenerWrapperType = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ListenerWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Field: public object Listener
        events.ListenerWrapperListenerField = events.ListenerWrapperType.DefineField(
            "Listener", _types.Object, FieldAttributes.Public);

        // Field: public bool Once
        events.ListenerWrapperOnceField = events.ListenerWrapperType.DefineField(
            "Once", _types.Boolean, FieldAttributes.Public);

        // Constructor: public $ListenerWrapper(object listener, bool once)
        var ctor = events.ListenerWrapperType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Boolean]
        );
        events.ListenerWrapperCtor = ctor;

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, events.ListenerWrapperListenerField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, events.ListenerWrapperOnceField);
        il.Emit(OpCodes.Ret);

        events.ListenerWrapperType.CreateType();
    }

    private void EmitTSEventEmitterStaticCtor(TypeBuilder typeBuilder, FieldBuilder defaultMaxListenersField)
    {
        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 10); // DefaultMaxListeners = 10
        il.Emit(OpCodes.Stsfld, defaultMaxListenersField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterCtor(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type dictType, Type listType)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        events.Ctor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _events = new Dictionary<string, List<ListenerWrapper>>()
        // Need to use TypeBuilder.GetConstructor for generic types with TypeBuilder arguments
        il.Emit(OpCodes.Ldarg_0);
        var openDictCtor = typeof(Dictionary<,>).GetConstructor(Type.EmptyTypes)!;
        var dictCtor = EmitterTypeHelpers.ResolveConstructor(dictType, openDictCtor);
        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Stfld, events.EventsField);

        // _maxListeners = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, events.MaxListenersField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterOn(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType)
    {
        // public $EventEmitter On(string eventName, object listener)
        var method = typeBuilder.DefineMethod(
            "On",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        events.On = method;

        var il = method.GetILGenerator();
        // Call AddListenerInternal(eventName, listener, false, false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0); // once = false
        il.Emit(OpCodes.Ldc_I4_0); // prepend = false
        il.Emit(OpCodes.Call, events.AddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterOnce(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "Once",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        events.Once = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1); // once = true
        il.Emit(OpCodes.Ldc_I4_0); // prepend = false
        il.Emit(OpCodes.Call, events.AddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterOff(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "Off",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        events.Off = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners)) return this;
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(dictType, methods.DictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Find and remove the listener (by reference)
        var indexLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var foundLabel = il.DefineLabel();

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // count = listeners.Count
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var countGetter = GetListMethod(listType, methods.ListCountGetter);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        il.MarkLabel(loopStart);
        // if (index >= count) goto loopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (listeners[index].Listener == listener)
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        var getItemMethod = GetListMethod(listType, methods.ListGetItem);
        il.Emit(OpCodes.Callvirt, getItemMethod);
        il.Emit(OpCodes.Ldfld, events.ListenerWrapperListenerField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, foundLabel);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(foundLabel);
        // listeners.RemoveAt(index)
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        var removeAtMethod = GetListMethod(listType, methods.ListRemoveAt);
        il.Emit(OpCodes.Callvirt, removeAtMethod);

        // if (listeners.Count == 0) _events.Remove(eventName)
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Brtrue, loopEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        var removeMethod = GetDictMethod(dictType, methods.DictRemove);
        il.Emit(OpCodes.Callvirt, removeMethod);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(loopEnd);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>public void EnableCaptureRejections()</c>, called from the
    /// <c>new EventEmitter({ captureRejections: true })</c> emit site (#1099).
    /// </summary>
    private void EmitTSEventEmitterEnableCaptureRejections(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        var method = typeBuilder.DefineMethod(
            "EnableCaptureRejections",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);
        events.EnableCaptureRejections = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, events.CaptureRejectionsField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Defines (without a body) <c>private void RouteCaptureRejection(object)</c>.
    /// The body is filled by <see cref="FillTSEventEmitterRouteCaptureRejection"/>
    /// after Emit is defined, since the two are mutually recursive.
    /// </summary>
    private void DefineTSEventEmitterRouteCaptureRejection(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        events.RouteCaptureRejection = typeBuilder.DefineMethod(
            "RouteCaptureRejection",
            MethodAttributes.Private,
            _types.Void,
            [_types.Object]);
    }

    /// <summary>
    /// Fills <c>RouteCaptureRejection</c>: when captureRejections is on and a
    /// listener returned an already-faulted promise, re-emits its rejection as
    /// 'error'. Synchronous-only (SharpTS drains microtasks eagerly, so a
    /// listener that throws is already settled when it returns).
    /// </summary>
    private void FillTSEventEmitterRouteCaptureRejection(EmittedRuntime runtime)
    {
        var events = runtime.EventEmitter;
        var il = events.RouteCaptureRejection.GetILGenerator();
        var ret = il.DefineLabel();

        var taskType = typeof(System.Threading.Tasks.Task);
        var isCompletedGetter = taskType.GetProperty("IsCompleted")!.GetGetMethod()!;
        var isFaultedGetter = taskType.GetProperty("IsFaulted")!.GetGetMethod()!;
        var exceptionGetter = taskType.GetProperty("Exception")!.GetGetMethod()!;
        var innerExceptionGetter = typeof(Exception).GetProperty("InnerException")!.GetGetMethod()!;

        // Extract the underlying Task<object> — a compiled async listener returns
        // a raw Task<object>, while other paths may hand back a $Promise wrapper.
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        var haveTask = il.DefineLabel();
        if (_features.UsesPromise)
        {
            var notPromise = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.RequirePromise().Type);
            il.Emit(OpCodes.Brfalse, notPromise);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.RequirePromise().Type);
            il.Emit(OpCodes.Callvirt, runtime.RequirePromise().TaskGetter);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Br, haveTask);
            il.MarkLabel(notPromise);
        }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, ret);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.MarkLabel(haveTask);

        // if (!task.IsCompleted) return; if (!task.IsFaulted) return;
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Callvirt, isCompletedGetter);
        il.Emit(OpCodes.Brfalse, ret);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Callvirt, isFaultedGetter);
        il.Emit(OpCodes.Brfalse, ret);

        // Exception inner = task.Exception.InnerException;
        var innerLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Callvirt, exceptionGetter);
        il.Emit(OpCodes.Callvirt, innerExceptionGetter);
        il.Emit(OpCodes.Stloc, innerLocal);

        // object reason: a $Promise rejection carries it in .Reason; a raw
        // Task faulted by a guest `throw` carries the guest value in a
        // $ThrownValueException (or legacy __tsValue metadata).
        var reasonLocal = il.DeclareLocal(_types.Object);
        var haveReason = il.DefineLabel();
        if (_features.UsesPromise)
        {
            var notRejected = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, innerLocal);
            il.Emit(OpCodes.Isinst, runtime.RequirePromise().RejectedExceptionType);
            il.Emit(OpCodes.Brfalse, notRejected);
            il.Emit(OpCodes.Ldloc, innerLocal);
            il.Emit(OpCodes.Castclass, runtime.RequirePromise().RejectedExceptionType);
            il.Emit(OpCodes.Callvirt, runtime.RequirePromise().RejectedExceptionReasonGetter);
            il.Emit(OpCodes.Stloc, reasonLocal);
            il.Emit(OpCodes.Br, haveReason);
            il.MarkLabel(notRejected);
        }

        var checkMetadata = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Isinst, runtime.Errors.ThrownValueType);
        il.Emit(OpCodes.Brfalse, checkMetadata);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Castclass, runtime.Errors.ThrownValueType);
        il.Emit(OpCodes.Call, runtime.Errors.ThrownValueValueGetter);
        il.Emit(OpCodes.Stloc, reasonLocal);
        il.Emit(OpCodes.Br, haveReason);
        il.MarkLabel(checkMetadata);
        // reason = inner.Data.Contains("__tsValue") ? inner.Data["__tsValue"] : inner
        var dataGetter = typeof(Exception).GetProperty("Data")!.GetGetMethod()!;
        var dataContains = typeof(System.Collections.IDictionary).GetMethod("Contains", [_types.Object])!;
        var dataGetItem = typeof(System.Collections.IDictionary).GetMethod("get_Item", [_types.Object])!;
        var useInner = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Callvirt, dataGetter);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, dataContains);
        il.Emit(OpCodes.Brfalse, useInner);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Callvirt, dataGetter);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, dataGetItem);
        il.Emit(OpCodes.Stloc, reasonLocal);
        il.Emit(OpCodes.Br, haveReason);
        il.MarkLabel(useInner);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Stloc, reasonLocal);
        il.MarkLabel(haveReason);

        // Disable capture during the routed emit to avoid recursion, then restore.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, events.CaptureRejectionsField);

        // this.Emit("error", new object[]{ reason });
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, reasonLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, events.Emit);
        il.Emit(OpCodes.Pop);

        // Restore capture.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, events.CaptureRejectionsField);

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterEmit(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var events = runtime.EventEmitter;
        // public bool Emit(string eventName, params object[] args)
        var method = typeBuilder.DefineMethod(
            "Emit",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.String, _types.MakeArrayType(_types.Object)]
        );
        events.Emit = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        var wrapperArrayType = _types.MakeArrayType(events.ListenerWrapperType);
        var countGetter = GetListMethod(listType, methods.ListCountGetter);
        var toArrayMethod = GetListMethod(listType, methods.ListToArray);
        var tryGetValueMethod = GetDictMethod(events.EventsField.FieldType, methods.DictTryGetValue);
        var stringEquals = _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!;

        // Local: invoke the listener in `listenerLocal`, leaving its return value on the stack.
        void EmitInvokeLeaveResult(LocalBuilder listenerLocal)
        {
            var isBound = il.DefineLabel();
            var invokeEnd = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listenerLocal);
            il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
            il.Emit(OpCodes.Brtrue, isBound);
            il.Emit(OpCodes.Ldloc, listenerLocal);
            il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
            il.Emit(OpCodes.Br, invokeEnd);
            il.MarkLabel(isBound);
            il.Emit(OpCodes.Ldloc, listenerLocal);
            il.Emit(OpCodes.Castclass, runtime.FunctionBindings.BoundType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.FunctionBindings.BoundInvoke);
            il.MarkLabel(invokeEnd);
        }

        // Null-coalesce args: if (args == null) args = Array.Empty<object>()
        // This handles the case where Emit is called via runtime dispatch (e.g., on $HttpServer)
        // and AdjustArgs pads the missing object[] parameter with null.
        var argsNotNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, argsNotNull);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Starg_S, (byte)2);
        il.MarkLabel(argsNotNull);

        // #1099 errorMonitor pre-dispatch: on 'error', notify errorMonitor
        // listeners first (without satisfying the "handled" check below).
        var skipMonitor = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Call, stringEquals);
        il.Emit(OpCodes.Brfalse, skipMonitor);

        var monListLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldstr, ErrorMonitorKey);
        il.Emit(OpCodes.Ldloca, monListLocal);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, skipMonitor);
        il.Emit(OpCodes.Ldloc, monListLocal);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Brfalse, skipMonitor);

        var monSnapLocal = il.DeclareLocal(wrapperArrayType);
        il.Emit(OpCodes.Ldloc, monListLocal);
        il.Emit(OpCodes.Callvirt, toArrayMethod);
        il.Emit(OpCodes.Stloc, monSnapLocal);

        var monIndex = il.DeclareLocal(_types.Int32);
        var monLen = il.DeclareLocal(_types.Int32);
        var monLoop = il.DefineLabel();
        var monEnd = il.DefineLabel();
        var monListener = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, monIndex);
        il.Emit(OpCodes.Ldloc, monSnapLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, monLen);
        il.MarkLabel(monLoop);
        il.Emit(OpCodes.Ldloc, monIndex);
        il.Emit(OpCodes.Ldloc, monLen);
        il.Emit(OpCodes.Bge, monEnd);
        il.Emit(OpCodes.Ldloc, monSnapLocal);
        il.Emit(OpCodes.Ldloc, monIndex);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldfld, events.ListenerWrapperListenerField);
        il.Emit(OpCodes.Stloc, monListener);
        EmitInvokeLeaveResult(monListener);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, monIndex);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, monIndex);
        il.Emit(OpCodes.Br, monLoop);
        il.MarkLabel(monEnd);
        il.MarkLabel(skipMonitor);

        // if (!_events.TryGetValue(eventName, out var listeners)) return false;
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // if (listeners.Count == 0) return false;
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Create snapshot: var snapshot = listeners.ToArray()
        var snapshotLocal = il.DeclareLocal(wrapperArrayType);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Callvirt, toArrayMethod);
        il.Emit(OpCodes.Stloc, snapshotLocal);

        // Iterate through snapshot and call each listener
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipOnceRemoval = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldloc, snapshotLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // var wrapper = snapshot[index]
        var wrapperLocal = il.DeclareLocal(events.ListenerWrapperType);
        il.Emit(OpCodes.Ldloc, snapshotLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, wrapperLocal);

        // if (wrapper.Once) { ... remove from original list ... }
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Ldfld, events.ListenerWrapperOnceField);
        il.Emit(OpCodes.Brfalse, skipOnceRemoval);

        // Remove from original list
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        var removeObjMethod = GetListMethod(listType, methods.ListRemove);
        il.Emit(OpCodes.Callvirt, removeObjMethod);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipOnceRemoval);

        // Call the listener, capturing the result.
        var listenerLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Ldfld, events.ListenerWrapperListenerField);
        il.Emit(OpCodes.Stloc, listenerLocal);

        var resultLocal = il.DeclareLocal(_types.Object);
        EmitInvokeLeaveResult(listenerLocal);
        il.Emit(OpCodes.Stloc, resultLocal);

        // #1099: if captureRejections, route a rejecting async listener to 'error'.
        var skipRoute = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.CaptureRejectionsField);
        il.Emit(OpCodes.Brfalse, skipRoute);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, events.RouteCaptureRejection);
        il.MarkLabel(skipRoute);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, trueLabel);

        il.MarkLabel(falseLabel);
        // #1099 throw-on-unhandled: a direct EventEmitter with no 'error'
        // listeners throws when 'error' is emitted (subclasses stay lenient).
        var skipThrow = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Call, stringEquals);
        il.Emit(OpCodes.Brfalse, skipThrow);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldtoken, events.Type);
        il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])!);
        il.Emit(OpCodes.Bne_Un, skipThrow);
        // reason = args.Length > 0 ? args[0] : "Unhandled 'error' event";
        var useDefaultReason = il.DefineLabel();
        var reasonReady = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, useDefaultReason);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Br, reasonReady);
        il.MarkLabel(useDefaultReason);
        il.Emit(OpCodes.Ldstr, "Unhandled 'error' event");
        il.MarkLabel(reasonReady);
        il.Emit(OpCodes.Call, runtime.Errors.CreateException);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(skipThrow);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterRemoveAllListeners(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "RemoveAllListeners",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        events.RemoveAllListeners = method;

        var il = method.GetILGenerator();
        var clearAllLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (eventName == null) { _events.Clear(); return this; }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, clearAllLabel);

        // _events.Remove(eventName)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        var removeMethod = GetDictMethod(dictType, methods.DictRemove);
        il.Emit(OpCodes.Callvirt, removeMethod);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(clearAllLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        var clearMethod = GetDictMethod(dictType, methods.DictClear);
        il.Emit(OpCodes.Callvirt, clearMethod);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterListeners(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var events = runtime.EventEmitter;
        var method = typeBuilder.DefineMethod(
            "Listeners",
            MethodAttributes.Public,
            runtime.ArrayStorage.Type,
            [_types.String]
        );
        events.Listeners = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners)) return new $Array(new List<object?>())
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(events.EventsField.FieldType, methods.DictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Create List<object?> and populate
        var resultListLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultListLocal);

        // Iterate and add each listener
        var indexLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var countGetter = GetListMethod(listType, methods.ListCountGetter);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // resultList.Add(listeners[index].Listener)
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        var getItemMethod = GetListMethod(listType, methods.ListGetItem);
        il.Emit(OpCodes.Callvirt, getItemMethod);
        il.Emit(OpCodes.Ldfld, events.ListenerWrapperListenerField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return new $Array(resultList)
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Newobj, runtime.ArrayStorage.Ctor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        // return new $Array(new List<object?>())
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Newobj, runtime.ArrayStorage.Ctor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterListenerCount(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "ListenerCount",
            MethodAttributes.Public,
            _types.Double,
            [_types.String]
        );
        events.ListenerCount = method;

        var il = method.GetILGenerator();
        var notFoundLabel = il.DefineLabel();

        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(events.EventsField.FieldType, methods.DictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        il.Emit(OpCodes.Ldloc, listenersLocal);
        var countGetter = GetListMethod(listType, methods.ListCountGetter);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterEventNames(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedRuntime runtime, Type dictType)
    {
        var events = runtime.EventEmitter;
        var method = typeBuilder.DefineMethod(
            "EventNames",
            MethodAttributes.Public,
            runtime.ArrayStorage.Type,
            Type.EmptyTypes
        );
        events.EventNames = method;

        var il = method.GetILGenerator();

        // Create List<object?> to accumulate keys
        var resultListLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultListLocal);

        // foreach (var key in _events.Keys)
        // Get the Keys property and iterate
        var keysProperty = GetDictMethod(dictType, methods.DictKeysGetter);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Callvirt, keysProperty);

        // The keysType is Dictionary<,>.KeyCollection which is a concrete type once we have the closed generic
        // We can use the concrete KeyCollection type for string key
        var keysCollectionType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection), _types.String, events.EventsField.FieldType.GetGenericArguments()[1]);
        var keysEnumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection.Enumerator), _types.String, events.EventsField.FieldType.GetGenericArguments()[1]);

        // GetEnumerator on KeyCollection
        var getEnumeratorMethod = EmitterTypeHelpers.ResolveMethod(keysCollectionType, typeof(Dictionary<,>.KeyCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Call, getEnumeratorMethod);

        var enumeratorLocal = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var moveNextMethod = EmitterTypeHelpers.ResolveMethod(keysEnumeratorType, typeof(Dictionary<,>.KeyCollection.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Call, moveNextMethod);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var getCurrentMethod = EmitterTypeHelpers.ResolveMethod(keysEnumeratorType, typeof(Dictionary<,>.KeyCollection.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, getCurrentMethod);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return new $Array(resultList)
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Newobj, runtime.ArrayStorage.Ctor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterPrependListener(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "PrependListener",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        events.PrependListener = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0); // once = false
        il.Emit(OpCodes.Ldc_I4_1); // prepend = true
        il.Emit(OpCodes.Call, events.AddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterPrependOnceListener(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "PrependOnceListener",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        events.PrependOnceListener = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1); // once = true
        il.Emit(OpCodes.Ldc_I4_1); // prepend = true
        il.Emit(OpCodes.Call, events.AddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterSetMaxListeners(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        var method = typeBuilder.DefineMethod(
            "SetMaxListeners",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Double]
        );
        events.SetMaxListeners = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, events.MaxListenersField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterGetMaxListeners(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        var method = typeBuilder.DefineMethod(
            "GetMaxListeners",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        events.GetMaxListeners = method;

        var il = method.GetILGenerator();
        var useDefaultLabel = il.DefineLabel();

        // if (_maxListeners > 0) return _maxListeners
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.MaxListenersField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, useDefaultLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.MaxListenersField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(useDefaultLabel);
        il.Emit(OpCodes.Ldsfld, events.DefaultMaxListeners);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterAddListenerInternal(EventEmitterGenericMethods methods, TypeBuilder typeBuilder, EmittedEventEmitterRuntime events, Type listType, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "AddListenerInternal",
            MethodAttributes.Private,
            typeBuilder,
            [_types.String, _types.Object, _types.Boolean, _types.Boolean]
        );
        events.AddListenerInternal = method;

        var il = method.GetILGenerator();
        var createListLabel = il.DefineLabel();
        var prependLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners))
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(dictType, methods.DictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, createListLabel);
        il.Emit(OpCodes.Br_S, prependLabel);

        // Create new list
        il.MarkLabel(createListLabel);
        var openListCtor = typeof(List<>).GetConstructor(Type.EmptyTypes)!;
        var listCtor = EmitterTypeHelpers.ResolveConstructor(listType, openListCtor);
        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Stloc, listenersLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, events.EventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var addMethod = GetDictMethod(dictType, methods.DictAdd);
        il.Emit(OpCodes.Callvirt, addMethod);

        il.MarkLabel(prependLabel);
        // Create wrapper: new $ListenerWrapper(listener, once)
        var wrapperLocal = il.DeclareLocal(events.ListenerWrapperType);
        il.Emit(OpCodes.Ldarg_2); // listener
        il.Emit(OpCodes.Ldarg_3); // once
        il.Emit(OpCodes.Newobj, events.ListenerWrapperCtor);
        il.Emit(OpCodes.Stloc, wrapperLocal);

        // if (prepend) listeners.Insert(0, wrapper) else listeners.Add(wrapper)
        il.Emit(OpCodes.Ldarg_S, (byte)4); // prepend
        il.Emit(OpCodes.Brfalse_S, endLabel);

        // Insert at beginning
        var afterAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        var insertMethod = GetListMethod(listType, methods.ListInsert);
        il.Emit(OpCodes.Callvirt, insertMethod);
        il.Emit(OpCodes.Br, afterAddLabel);

        il.MarkLabel(endLabel);
        // Add at end
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        var addItemMethod = GetListMethod(listType, methods.ListAdd);
        il.Emit(OpCodes.Callvirt, addItemMethod);

        il.MarkLabel(afterAddLabel);
        // Call virtual OnListenerAdded(eventName) for subclass notification
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1); // eventName
        il.Emit(OpCodes.Callvirt, events.OnListenerAdded);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits AddListener as an alias for On (Node.js compatibility).
    /// Used by runtime dispatch when the type is not recognized as EventEmitter by TypeEmitterRegistry.
    /// </summary>
    private void EmitTSEventEmitterAddListener(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        var method = typeBuilder.DefineMethod(
            "AddListener",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, events.On);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits RemoveListener as an alias for Off (Node.js compatibility).
    /// </summary>
    private void EmitTSEventEmitterRemoveListener(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        var method = typeBuilder.DefineMethod(
            "RemoveListener",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, events.Off);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits virtual OnListenerAdded(string eventName) - called at end of AddListenerInternal.
    /// Default implementation is empty; $Readable overrides to enter flowing mode on 'data'.
    /// </summary>
    private void EmitTSEventEmitterOnListenerAdded(TypeBuilder typeBuilder, EmittedEventEmitterRuntime events)
    {
        var method = typeBuilder.DefineMethod(
            "OnListenerAdded",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            _types.Void,
            [_types.String]
        );
        events.OnListenerAdded = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ret); // Default: no-op
    }

    /// <summary>
    /// Emits RawListeners as an alias for Listeners (Node.js compatibility).
    /// In Node.js, rawListeners returns unwrapped listeners; our Listeners method
    /// already returns the raw function references, so they are equivalent.
    /// </summary>
    private void EmitTSEventEmitterRawListeners(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var events = runtime.EventEmitter;
        var method = typeBuilder.DefineMethod(
            "RawListeners",
            MethodAttributes.Public,
            runtime.ArrayStorage.Type,
            [_types.String]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, events.Listeners);
        il.Emit(OpCodes.Ret);
    }
}
