using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $EventEmitter class for standalone EventEmitter support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSEventEmitter
/// </summary>
public partial class RuntimeEmitter
{
    private FieldBuilder _tsEventEmitterEventsField = null!;
    private FieldBuilder _tsEventEmitterMaxListenersField = null!;
    private TypeBuilder _tsEventEmitterListenerWrapperType = null!;
    private FieldBuilder _tsEventEmitterListenerWrapperListener = null!;
    private FieldBuilder _tsEventEmitterListenerWrapperOnce = null!;

    // Cached method infos from open generic types for TypeBuilder.GetMethod
    private MethodInfo _listCountGetter = null!;
    private MethodInfo _listGetItem = null!;
    private MethodInfo _listRemoveAt = null!;
    private MethodInfo _listRemove = null!;
    private MethodInfo _listAdd = null!;
    private MethodInfo _listInsert = null!;
    private MethodInfo _listToArray = null!;
    private MethodInfo _dictTryGetValue = null!;
    private MethodInfo _dictRemove = null!;
    private MethodInfo _dictClear = null!;
    private MethodInfo _dictAdd = null!;
    private MethodInfo _dictKeysGetter = null!;

    private void EmitTSEventEmitterClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // First, emit the ListenerWrapper nested type
        EmitListenerWrapperType(moduleBuilder, runtime);

        // Define class: public class $EventEmitter (not sealed - stream types inherit from it)
        var typeBuilder = moduleBuilder.DefineType(
            "$EventEmitter",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSEventEmitterType = typeBuilder;

        // Field: private Dictionary<string, List<ListenerWrapper>> _events
        var listType = _types.MakeGenericType(_types.ListOpen, _tsEventEmitterListenerWrapperType);
        var dictType = _types.MakeGenericType(_types.DictionaryOpen, _types.String, listType);
        _tsEventEmitterEventsField = typeBuilder.DefineField("_events", dictType, FieldAttributes.Private);

        // Cache method infos from open generic types for later use with TypeBuilder.GetMethod
        CacheGenericMethodInfos(listType, dictType);

        // Field: private int _maxListeners = 0
        _tsEventEmitterMaxListenersField = typeBuilder.DefineField("_maxListeners", _types.Int32, FieldAttributes.Private);

        // Static field: public static int DefaultMaxListeners = 10
        var defaultMaxListenersField = typeBuilder.DefineField(
            "DefaultMaxListeners",
            _types.Int32,
            FieldAttributes.Public | FieldAttributes.Static
        );
        runtime.TSEventEmitterDefaultMaxListeners = defaultMaxListenersField;

        // Constructor: public $EventEmitter()
        EmitTSEventEmitterCtor(typeBuilder, runtime, dictType, listType);

        // Instance methods - AddListenerInternal must be defined first as it's called by On/Once/Prepend methods
        EmitTSEventEmitterAddListenerInternal(typeBuilder, runtime, listType, dictType);
        EmitTSEventEmitterOn(typeBuilder, runtime, listType);
        EmitTSEventEmitterOnce(typeBuilder, runtime, listType);
        EmitTSEventEmitterOff(typeBuilder, runtime, listType, dictType);
        EmitTSEventEmitterEmit(typeBuilder, runtime, listType);
        EmitTSEventEmitterRemoveAllListeners(typeBuilder, runtime, dictType);
        EmitTSEventEmitterListeners(typeBuilder, runtime, listType);
        EmitTSEventEmitterListenerCount(typeBuilder, runtime, listType);
        EmitTSEventEmitterEventNames(typeBuilder, runtime, dictType);
        EmitTSEventEmitterPrependListener(typeBuilder, runtime, listType);
        EmitTSEventEmitterPrependOnceListener(typeBuilder, runtime, listType);
        EmitTSEventEmitterSetMaxListeners(typeBuilder, runtime);
        EmitTSEventEmitterGetMaxListeners(typeBuilder, runtime);

        // Set static constructor to initialize DefaultMaxListeners
        EmitTSEventEmitterStaticCtor(typeBuilder, defaultMaxListenersField);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Cache method infos from open generic types.
    /// These are used with TypeBuilder.GetMethod to get the closed generic methods.
    /// </summary>
    private void CacheGenericMethodInfos(Type listType, Type dictType)
    {
        // List<T> methods from open generic type
        var openListType = typeof(List<>);
        _listCountGetter = openListType.GetProperty("Count")!.GetGetMethod()!;
        _listGetItem = openListType.GetMethod("get_Item", [typeof(int)])!;
        _listRemoveAt = openListType.GetMethod("RemoveAt", [typeof(int)])!;
        _listRemove = openListType.GetMethod("Remove", openListType.GetGenericArguments())!;
        _listAdd = openListType.GetMethod("Add", openListType.GetGenericArguments())!;
        _listInsert = openListType.GetMethod("Insert", [typeof(int), openListType.GetGenericArguments()[0]])!;
        _listToArray = openListType.GetMethod("ToArray", Type.EmptyTypes)!;

        // Dictionary<TKey, TValue> methods from open generic type
        var openDictType = typeof(Dictionary<,>);
        var dictGenericArgs = openDictType.GetGenericArguments();
        var valueType = dictGenericArgs[1]; // TValue
        _dictTryGetValue = openDictType.GetMethod("TryGetValue", [dictGenericArgs[0], dictGenericArgs[1].MakeByRefType()])!;
        _dictRemove = openDictType.GetMethod("Remove", [dictGenericArgs[0]])!;
        _dictClear = openDictType.GetMethod("Clear", Type.EmptyTypes)!;
        _dictAdd = openDictType.GetMethod("Add", [dictGenericArgs[0], dictGenericArgs[1]])!;
        _dictKeysGetter = openDictType.GetProperty("Keys")!.GetGetMethod()!;
    }

    /// <summary>
    /// Gets a method on a constructed generic type using TypeBuilder.GetMethod.
    /// </summary>
    private static MethodInfo GetListMethod(Type listType, MethodInfo openMethod)
        => TypeBuilder.GetMethod(listType, openMethod);

    /// <summary>
    /// Gets a method on a constructed generic Dictionary type using TypeBuilder.GetMethod.
    /// </summary>
    private static MethodInfo GetDictMethod(Type dictType, MethodInfo openMethod)
        => TypeBuilder.GetMethod(dictType, openMethod);

    private void EmitListenerWrapperType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define nested class: public sealed class $ListenerWrapper
        _tsEventEmitterListenerWrapperType = moduleBuilder.DefineType(
            "$ListenerWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Field: public object Listener
        _tsEventEmitterListenerWrapperListener = _tsEventEmitterListenerWrapperType.DefineField(
            "Listener", _types.Object, FieldAttributes.Public);

        // Field: public bool Once
        _tsEventEmitterListenerWrapperOnce = _tsEventEmitterListenerWrapperType.DefineField(
            "Once", _types.Boolean, FieldAttributes.Public);

        // Constructor: public $ListenerWrapper(object listener, bool once)
        var ctor = _tsEventEmitterListenerWrapperType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Boolean]
        );
        runtime.TSListenerWrapperCtor = ctor;

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsEventEmitterListenerWrapperListener);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _tsEventEmitterListenerWrapperOnce);
        il.Emit(OpCodes.Ret);

        _tsEventEmitterListenerWrapperType.CreateType();
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

    private void EmitTSEventEmitterCtor(TypeBuilder typeBuilder, EmittedRuntime runtime, Type dictType, Type listType)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TSEventEmitterCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _events = new Dictionary<string, List<ListenerWrapper>>()
        // Need to use TypeBuilder.GetConstructor for generic types with TypeBuilder arguments
        il.Emit(OpCodes.Ldarg_0);
        var openDictCtor = typeof(Dictionary<,>).GetConstructor(Type.EmptyTypes)!;
        var dictCtor = TypeBuilder.GetConstructor(dictType, openDictCtor);
        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Stfld, _tsEventEmitterEventsField);

        // _maxListeners = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsEventEmitterMaxListenersField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterOn(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        // public $EventEmitter On(string eventName, object listener)
        var method = typeBuilder.DefineMethod(
            "On",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        runtime.TSEventEmitterOn = method;

        var il = method.GetILGenerator();
        // Call AddListenerInternal(eventName, listener, false, false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0); // once = false
        il.Emit(OpCodes.Ldc_I4_0); // prepend = false
        il.Emit(OpCodes.Call, runtime.TSEventEmitterAddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterOnce(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "Once",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        runtime.TSEventEmitterOnce = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1); // once = true
        il.Emit(OpCodes.Ldc_I4_0); // prepend = false
        il.Emit(OpCodes.Call, runtime.TSEventEmitterAddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterOff(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "Off",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        runtime.TSEventEmitterOff = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners)) return this;
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(dictType, _dictTryGetValue);
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
        var countGetter = GetListMethod(listType, _listCountGetter);
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
        var getItemMethod = GetListMethod(listType, _listGetItem);
        il.Emit(OpCodes.Callvirt, getItemMethod);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterListenerWrapperListener);
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
        var removeAtMethod = GetListMethod(listType, _listRemoveAt);
        il.Emit(OpCodes.Callvirt, removeAtMethod);

        // if (listeners.Count == 0) _events.Remove(eventName)
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Brtrue, loopEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        var removeMethod = GetDictMethod(dictType, _dictRemove);
        il.Emit(OpCodes.Callvirt, removeMethod);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(loopEnd);
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterEmit(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        // public bool Emit(string eventName, params object[] args)
        var method = typeBuilder.DefineMethod(
            "Emit",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.String, _types.MakeArrayType(_types.Object)]
        );
        runtime.TSEventEmitterEmit = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners)) return false;
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(_tsEventEmitterEventsField.FieldType, _dictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // if (listeners.Count == 0) return false;
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var countGetter = GetListMethod(listType, _listCountGetter);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Create snapshot: var snapshot = listeners.ToArray()
        var snapshotLocal = il.DeclareLocal(_types.MakeArrayType(_tsEventEmitterListenerWrapperType));
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var toArrayMethod = GetListMethod(listType, _listToArray);
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
        var wrapperLocal = il.DeclareLocal(_tsEventEmitterListenerWrapperType);
        il.Emit(OpCodes.Ldloc, snapshotLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, wrapperLocal);

        // if (wrapper.Once) { ... remove from original list ... }
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterListenerWrapperOnce);
        il.Emit(OpCodes.Brfalse, skipOnceRemoval);

        // Remove from original list
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        var removeObjMethod = GetListMethod(listType, _listRemove);
        il.Emit(OpCodes.Callvirt, removeObjMethod);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipOnceRemoval);

        // Call the listener: handle both $TSFunction and $BoundTSFunction
        var listenerLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterListenerWrapperListener);
        il.Emit(OpCodes.Stloc, listenerLocal);

        var isBoundLabel = il.DefineLabel();
        var invokeEndLabel = il.DefineLabel();

        // Check if listener is $BoundTSFunction (check this first since it's less common)
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundLabel);

        // Default: assume $TSFunction
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldarg_2); // args array
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, invokeEndLabel);

        // isBoundLabel: call $BoundTSFunction.Invoke
        il.MarkLabel(isBoundLabel);
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldarg_2); // args array
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(invokeEndLabel);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, trueLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterRemoveAllListeners(TypeBuilder typeBuilder, EmittedRuntime runtime, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "RemoveAllListeners",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        runtime.TSEventEmitterRemoveAllListeners = method;

        var il = method.GetILGenerator();
        var clearAllLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (eventName == null) { _events.Clear(); return this; }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, clearAllLabel);

        // _events.Remove(eventName)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        var removeMethod = GetDictMethod(dictType, _dictRemove);
        il.Emit(OpCodes.Callvirt, removeMethod);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(clearAllLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        var clearMethod = GetDictMethod(dictType, _dictClear);
        il.Emit(OpCodes.Callvirt, clearMethod);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterListeners(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "Listeners",
            MethodAttributes.Public,
            runtime.TSArrayType,
            [_types.String]
        );
        runtime.TSEventEmitterListeners = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners)) return new $Array(new List<object?>())
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(_tsEventEmitterEventsField.FieldType, _dictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Create List<object?> and populate
        var resultListLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultListLocal);

        // Iterate and add each listener
        var indexLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var countGetter = GetListMethod(listType, _listCountGetter);
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
        var getItemMethod = GetListMethod(listType, _listGetItem);
        il.Emit(OpCodes.Callvirt, getItemMethod);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterListenerWrapperListener);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return new $Array(resultList)
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        // return new $Array(new List<object?>())
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterListenerCount(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "ListenerCount",
            MethodAttributes.Public,
            _types.Double,
            [_types.String]
        );
        runtime.TSEventEmitterListenerCount = method;

        var il = method.GetILGenerator();
        var notFoundLabel = il.DefineLabel();

        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(_tsEventEmitterEventsField.FieldType, _dictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        il.Emit(OpCodes.Ldloc, listenersLocal);
        var countGetter = GetListMethod(listType, _listCountGetter);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterEventNames(TypeBuilder typeBuilder, EmittedRuntime runtime, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "EventNames",
            MethodAttributes.Public,
            runtime.TSArrayType,
            Type.EmptyTypes
        );
        runtime.TSEventEmitterEventNames = method;

        var il = method.GetILGenerator();

        // Create List<object?> to accumulate keys
        var resultListLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultListLocal);

        // foreach (var key in _events.Keys)
        // Get the Keys property and iterate
        var keysProperty = GetDictMethod(dictType, _dictKeysGetter);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Callvirt, keysProperty);

        // The keysType is Dictionary<,>.KeyCollection which is a concrete type once we have the closed generic
        // We can use the concrete KeyCollection type for string key
        var keysCollectionType = typeof(Dictionary<,>.KeyCollection).MakeGenericType(_types.String, _tsEventEmitterEventsField.FieldType.GetGenericArguments()[1]);
        var keysEnumeratorType = typeof(Dictionary<,>.KeyCollection.Enumerator).MakeGenericType(_types.String, _tsEventEmitterEventsField.FieldType.GetGenericArguments()[1]);

        // GetEnumerator on KeyCollection
        var getEnumeratorMethod = TypeBuilder.GetMethod(keysCollectionType, typeof(Dictionary<,>.KeyCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Call, getEnumeratorMethod);

        var enumeratorLocal = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var moveNextMethod = TypeBuilder.GetMethod(keysEnumeratorType, typeof(Dictionary<,>.KeyCollection.Enumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Call, moveNextMethod);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var getCurrentMethod = TypeBuilder.GetMethod(keysEnumeratorType, typeof(Dictionary<,>.KeyCollection.Enumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, getCurrentMethod);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return new $Array(resultList)
        il.Emit(OpCodes.Ldloc, resultListLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterPrependListener(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "PrependListener",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        runtime.TSEventEmitterPrependListener = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0); // once = false
        il.Emit(OpCodes.Ldc_I4_1); // prepend = true
        il.Emit(OpCodes.Call, runtime.TSEventEmitterAddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterPrependOnceListener(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType)
    {
        var method = typeBuilder.DefineMethod(
            "PrependOnceListener",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String, _types.Object]
        );
        runtime.TSEventEmitterPrependOnceListener = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1); // once = true
        il.Emit(OpCodes.Ldc_I4_1); // prepend = true
        il.Emit(OpCodes.Call, runtime.TSEventEmitterAddListenerInternal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterSetMaxListeners(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetMaxListeners",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Double]
        );
        runtime.TSEventEmitterSetMaxListeners = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, _tsEventEmitterMaxListenersField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterGetMaxListeners(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetMaxListeners",
            MethodAttributes.Public,
            _types.Double,
            Type.EmptyTypes
        );
        runtime.TSEventEmitterGetMaxListeners = method;

        var il = method.GetILGenerator();
        var useDefaultLabel = il.DefineLabel();

        // if (_maxListeners > 0) return _maxListeners
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterMaxListenersField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, useDefaultLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterMaxListenersField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(useDefaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.TSEventEmitterDefaultMaxListeners);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSEventEmitterAddListenerInternal(TypeBuilder typeBuilder, EmittedRuntime runtime, Type listType, Type dictType)
    {
        var method = typeBuilder.DefineMethod(
            "AddListenerInternal",
            MethodAttributes.Private,
            typeBuilder,
            [_types.String, _types.Object, _types.Boolean, _types.Boolean]
        );
        runtime.TSEventEmitterAddListenerInternal = method;

        var il = method.GetILGenerator();
        var createListLabel = il.DefineLabel();
        var prependLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (!_events.TryGetValue(eventName, out var listeners))
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listenersLocal);
        var tryGetValueMethod = GetDictMethod(dictType, _dictTryGetValue);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);
        il.Emit(OpCodes.Brfalse, createListLabel);
        il.Emit(OpCodes.Br_S, prependLabel);

        // Create new list
        il.MarkLabel(createListLabel);
        var openListCtor = typeof(List<>).GetConstructor(Type.EmptyTypes)!;
        var listCtor = TypeBuilder.GetConstructor(listType, openListCtor);
        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Stloc, listenersLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsEventEmitterEventsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        var addMethod = GetDictMethod(dictType, _dictAdd);
        il.Emit(OpCodes.Callvirt, addMethod);

        il.MarkLabel(prependLabel);
        // Create wrapper: new $ListenerWrapper(listener, once)
        var wrapperLocal = il.DeclareLocal(_tsEventEmitterListenerWrapperType);
        il.Emit(OpCodes.Ldarg_2); // listener
        il.Emit(OpCodes.Ldarg_3); // once
        il.Emit(OpCodes.Newobj, runtime.TSListenerWrapperCtor);
        il.Emit(OpCodes.Stloc, wrapperLocal);

        // if (prepend) listeners.Insert(0, wrapper) else listeners.Add(wrapper)
        il.Emit(OpCodes.Ldarg_S, (byte)4); // prepend
        il.Emit(OpCodes.Brfalse_S, endLabel);

        // Insert at beginning
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        var insertMethod = GetListMethod(listType, _listInsert);
        il.Emit(OpCodes.Callvirt, insertMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(endLabel);
        // Add at end
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        var addItemMethod = GetListMethod(listType, _listAdd);
        il.Emit(OpCodes.Callvirt, addItemMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }
}
