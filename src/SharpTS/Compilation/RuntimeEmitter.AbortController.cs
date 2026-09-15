using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits AbortController/AbortSignal support methods into the $Runtime class.
    /// Uses CancellationTokenSource/CancellationToken for standalone DLL compatibility.
    ///
    /// The emitted $AbortSignal is a Dictionary{string, object?} with keys:
    ///   "_cts" → CancellationTokenSource (or null if not owned)
    ///   "_token" → CancellationToken
    ///   "_reason" → object? (abort reason)
    ///   "_reasonSet" → bool
    ///   "_listeners" → List{object}
    ///   "_onabort" → object? (event handler)
    ///
    /// The emitted $AbortController is a Dictionary{string, object?} with keys:
    ///   "_cts" → CancellationTokenSource
    ///   "_signal" → object (the $AbortSignal dict)
    /// </summary>
    private void EmitAbortControllerMethods(TypeBuilder typeBuilder, EmittedAbortRuntime abort, Action<string> requireRuntime)
    {
        EmitCreateAbortController(typeBuilder, abort);
        EmitAbortControllerAbort(typeBuilder, abort);
        EmitAbortControllerGetSignal(typeBuilder, abort);
        EmitAbortSignalGetAborted(typeBuilder, abort);
        EmitAbortSignalGetReason(typeBuilder, abort);
        EmitAbortSignalGetOnAbort(typeBuilder, abort);
        EmitAbortSignalSetOnAbort(typeBuilder, abort);
        EmitAbortSignalThrowIfAborted(typeBuilder, abort);
        EmitAbortSignalAddEventListener(typeBuilder, abort);
        EmitAbortSignalRemoveEventListener(typeBuilder, abort);
        EmitAbortSignalThisWrappers(typeBuilder, abort);
        EmitAbortSignalStaticAbort(typeBuilder, abort);
        EmitAbortSignalStaticTimeout(typeBuilder, abort);
        EmitAbortSignalStaticAny(typeBuilder, abort, requireRuntime);
    }

    /// <summary>
    /// CreateAbortController() → object
    /// Creates a new abort controller (dict with _cts and _signal).
    /// </summary>
    private void EmitCreateAbortController(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "CreateAbortController",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        abort.CreateController = method;

        var il = method.GetILGenerator();
        var ctsType = _types.CancellationTokenSource;
        var ctType = _types.CancellationToken;
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        // var cts = new CancellationTokenSource()
        var ctsLocal = il.DeclareLocal(ctsType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(ctsType));
        il.Emit(OpCodes.Stloc, ctsLocal);

        // var signal = new Dictionary<string, object?>()
        var signalLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(dictType));
        il.Emit(OpCodes.Stloc, signalLocal);

        // signal["_token"] = cts.Token
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(ctsType, "Token").GetGetMethod()!);
        il.Emit(OpCodes.Box, ctType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_cts"] = null (owned by controller, not signal)
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_cts");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reason"] = null
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reason");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reasonSet"] = false
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reasonSet");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_listeners"] = new List<object>()
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_listeners");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_onabort"] = null
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_onabort");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // var controller = new Dictionary<string, object?>()
        var controllerLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(dictType));
        il.Emit(OpCodes.Stloc, controllerLocal);

        // controller["_cts"] = cts
        il.Emit(OpCodes.Ldloc, controllerLocal);
        il.Emit(OpCodes.Ldstr, "_cts");
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // controller["_signal"] = signal
        il.Emit(OpCodes.Ldloc, controllerLocal);
        il.Emit(OpCodes.Ldstr, "_signal");
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // return controller
        il.Emit(OpCodes.Ldloc, controllerLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortControllerAbort(object controller, object? reason) → object? (null/undefined)
    /// </summary>
    private void EmitAbortControllerAbort(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortControllerAbort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        abort.ControllerAbort = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var ctsType = _types.CancellationTokenSource;

        var doneLabel = il.DefineLabel();

        // if (controller == null) goto done
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // var dict = controller as Dictionary<string,object?>
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) goto done
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // Check if dict has "_cts" key
        var ctsLocal = il.DeclareLocal(ctsType);
        var hasCtsLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_cts");
        il.Emit(OpCodes.Ldloca, hasCtsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "TryGetValue", _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, doneLabel);

        // var cts = (CancellationTokenSource)dict["_cts"]
        il.Emit(OpCodes.Ldloc, hasCtsLocal);
        il.Emit(OpCodes.Isinst, ctsType);
        il.Emit(OpCodes.Stloc, ctsLocal);

        // if (cts == null) goto done
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // if (cts.IsCancellationRequested) goto done
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(ctsType, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // var signal = (Dictionary<string,object?>)dict["_signal"]
        var signalLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_signal");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Stloc, signalLocal);

        // signal["_reason"] = reason ?? "AbortError: The operation was aborted"
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reason");
        il.Emit(OpCodes.Ldarg_1); // reason
        var hasReasonLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasReasonLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        il.MarkLabel(hasReasonLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reasonSet"] = true
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reasonSet");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // cts.Cancel()
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(ctsType, "Cancel", Type.EmptyTypes)!);

        // Fire abort event on signal
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, abort.FireEvent);

        // done:
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortControllerGetSignal(object controller) → object
    /// </summary>
    private void EmitAbortControllerGetSignal(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortControllerGetSignal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        abort.ControllerGetSignal = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;

        // return ((Dictionary<string,object?>)controller)["_signal"]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "_signal");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalGetAborted(object signal) → bool
    /// </summary>
    private void EmitAbortSignalGetAborted(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalGetAborted",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        abort.SignalGetAborted = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var ctType = _types.CancellationToken;

        // var token = (CancellationToken)((Dictionary<string,object?>)signal)["_token"]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Unbox_Any, ctType);
        // token.IsCancellationRequested
        var tokenLocal = il.DeclareLocal(ctType);
        il.Emit(OpCodes.Stloc, tokenLocal);
        il.Emit(OpCodes.Ldloca, tokenLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(ctType, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalGetReason(object signal) → object?
    /// </summary>
    private void EmitAbortSignalGetReason(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalGetReason",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        abort.SignalGetReason = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var ctType = _types.CancellationToken;

        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (_reasonSet) return _reason
        var notSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_reasonSet");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notSetLabel);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_reason");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Ret);

        // not set: if aborted, return default reason, else null
        il.MarkLabel(notSetLabel);
        var notAbortedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Unbox_Any, ctType);
        var tokenLocal = il.DeclareLocal(ctType);
        il.Emit(OpCodes.Stloc, tokenLocal);
        il.Emit(OpCodes.Ldloca, tokenLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(ctType, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notAbortedLabel);

        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notAbortedLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalGetOnAbort(object signal) → object?
    /// </summary>
    private void EmitAbortSignalGetOnAbort(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalGetOnAbort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        abort.SignalGetOnAbort = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "_onabort");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalSetOnAbort(object signal, object? handler) → void
    /// </summary>
    private void EmitAbortSignalSetOnAbort(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalSetOnAbort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        abort.SignalSetOnAbort = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "_onabort");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalThrowIfAborted(object signal) → void
    /// </summary>
    private void EmitAbortSignalThrowIfAborted(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalThrowIfAborted",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        abort.SignalThrowIfAborted = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var ctType = _types.CancellationToken;

        var notAbortedLabel = il.DefineLabel();

        // Check if aborted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Unbox_Any, ctType);
        var tokenLocal = il.DeclareLocal(ctType);
        il.Emit(OpCodes.Stloc, tokenLocal);
        il.Emit(OpCodes.Ldloca, tokenLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(ctType, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notAbortedLabel);

        // Get reason and throw
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, abort.SignalGetReason);
        var reasonLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, reasonLocal);

        // throw new Exception(reason?.ToString() ?? "AbortError: The operation was aborted")
        il.Emit(OpCodes.Ldloc, reasonLocal);
        var hasReasonLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasReasonLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, throwLabel);
        il.MarkLabel(hasReasonLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notAbortedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalAddEventListener(object signal, string type, object listener) → object? (null)
    /// </summary>
    private void EmitAbortSignalAddEventListener(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalAddEventListener",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String, _types.Object]
        );
        abort.SignalAddEventListener = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        var doneLabel = il.DefineLabel();

        // if (type != "abort") return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "abort");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, doneLabel);

        // listeners.Add(listener)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "_listeners");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalRemoveEventListener(object signal, string type, object listener) → object? (null)
    /// </summary>
    private void EmitAbortSignalRemoveEventListener(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalRemoveEventListener",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String, _types.Object]
        );
        abort.SignalRemoveEventListener = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        var doneLabel = il.DefineLabel();

        // if (type != "abort") return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "abort");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, doneLabel);

        // listeners.Remove(listener) - removes first occurrence by reference
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "_listeners");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Remove", _types.Object));
        il.Emit(OpCodes.Pop); // discard bool return

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits `__this`-first wrappers around the AbortSignal method helpers so a
    /// dynamically-typed (`any`) signal receiver can resolve them as callable methods.
    /// The GetProperty signal branch returns these wrapped in a $TSFunction (target=null,
    /// _expectsThis=true via the "__this" first-parameter name), so InvokeMethodValue
    /// injects the receiver. Each returns object (undefined) — addEventListener and
    /// throwIfAborted have no JS return value. (#985)
    /// </summary>
    private void EmitAbortSignalThisWrappers(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        abort.SignalAddEventListenerThis =
            EmitAbortSignalAddRemoveThis(typeBuilder, "AbortSignalAddEventListenerThis", abort.SignalAddEventListener);
        abort.SignalRemoveEventListenerThis =
            EmitAbortSignalAddRemoveThis(typeBuilder, "AbortSignalRemoveEventListenerThis", abort.SignalRemoveEventListener);

        // throwIfAborted(): no args beyond the receiver.
        var throwMethod = typeBuilder.DefineMethod(
            "AbortSignalThrowIfAbortedThis",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        throwMethod.DefineParameter(1, ParameterAttributes.None, "__this");
        abort.SignalThrowIfAbortedThis = throwMethod;
        var tIl = throwMethod.GetILGenerator();
        tIl.Emit(OpCodes.Ldarg_0);
        tIl.Emit(OpCodes.Call, abort.SignalThrowIfAborted); // void
        tIl.Emit(OpCodes.Ldnull);
        tIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits an `(object __this, object type, object listener) → object` wrapper that
    /// normalizes `type` to a string (null-safe) and forwards to the given base helper
    /// `(object signal, string type, object listener) → object`. AdjustArgs pads an
    /// omitted listener with undefined, so calls like addEventListener('abort') are safe.
    /// </summary>
    private MethodBuilder EmitAbortSignalAddRemoveThis(TypeBuilder typeBuilder, string name, MethodBuilder baseHelper)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.DefineParameter(2, ParameterAttributes.None, "type");
        method.DefineParameter(3, ParameterAttributes.None, "listener");

        var il = method.GetILGenerator();

        // typeStr = type == null ? null : type.ToString()
        var typeStrLocal = il.DeclareLocal(_types.String);
        var notNullLabel = il.DefineLabel();
        var haveTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, haveTypeLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.MarkLabel(haveTypeLabel);
        il.Emit(OpCodes.Stloc, typeStrLocal);

        // return baseHelper(__this, typeStr, listener)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, typeStrLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, baseHelper);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// AbortSignalAbort(object? reason) → object (signal dict, already aborted)
    /// </summary>
    private void EmitAbortSignalStaticAbort(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalAbort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        abort.SignalAbort = method;

        var il = method.GetILGenerator();
        var ctsType = _types.CancellationTokenSource;
        var ctType = _types.CancellationToken;
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        // Create a pre-cancelled signal
        var ctsLocal = il.DeclareLocal(ctsType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(ctsType));
        il.Emit(OpCodes.Stloc, ctsLocal);

        // Create signal dict
        var signalLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(dictType));
        il.Emit(OpCodes.Stloc, signalLocal);

        // signal["_token"] = cts.Token
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(ctsType, "Token").GetGetMethod()!);
        il.Emit(OpCodes.Box, ctType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_cts"] = cts (owned)
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_cts");
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reason"] = reason ?? "AbortError: ..."
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reason");
        il.Emit(OpCodes.Ldarg_0);
        var hasReasonLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasReasonLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        il.MarkLabel(hasReasonLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reasonSet"] = true
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reasonSet");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_listeners"] = new List<object>()
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_listeners");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_onabort"] = null
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_onabort");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // cts.Cancel()
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(ctsType, "Cancel", Type.EmptyTypes)!);

        // return signal
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalTimeout(double ms) → object (signal dict)
    /// </summary>
    private void EmitAbortSignalStaticTimeout(TypeBuilder typeBuilder, EmittedAbortRuntime abort)
    {
        var method = typeBuilder.DefineMethod(
            "AbortSignalTimeout",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double]
        );
        abort.SignalTimeout = method;

        var il = method.GetILGenerator();
        var ctsType = _types.CancellationTokenSource;
        var ctType = _types.CancellationToken;
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;
        var timeSpanType = _types.TimeSpan;

        // var cts = new CancellationTokenSource()
        var ctsLocal = il.DeclareLocal(ctsType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(ctsType));
        il.Emit(OpCodes.Stloc, ctsLocal);

        // cts.CancelAfter(TimeSpan.FromMilliseconds(ms))
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(timeSpanType, "FromMilliseconds", _types.Double));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(ctsType, "CancelAfter", timeSpanType));

        // Create signal dict
        var signalLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(dictType));
        il.Emit(OpCodes.Stloc, signalLocal);

        // signal["_token"] = cts.Token
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(ctsType, "Token").GetGetMethod()!);
        il.Emit(OpCodes.Box, ctType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_cts"] = cts (owned)
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_cts");
        il.Emit(OpCodes.Ldloc, ctsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reason"] = "TimeoutError: ..."
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reason");
        il.Emit(OpCodes.Ldstr, "TimeoutError: The operation was aborted due to timeout");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_reasonSet"] = true
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_reasonSet");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_listeners"] = new List<object>()
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_listeners");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // signal["_onabort"] = null
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ldstr, "_onabort");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // return signal
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// AbortSignalAny(object signals) → object (signal dict)
    /// Delegates to RuntimeTypes.AbortSignalAnyCompiled via reflection for proper
    /// CancellationToken linking between input signals and composite signal.
    /// </summary>
    private void EmitAbortSignalStaticAny(TypeBuilder typeBuilder, EmittedAbortRuntime abort, Action<string> requireRuntime)
    {
        // The body below late-binds to RuntimeTypes.AbortSignalAnyCompiled via
        // Type.GetType("…, SharpTS"); its normal execution needs SharpTS.dll present.
        // Record the runtime dependency only when the program actually references
        // AbortSignal.any — the coarse UsesAbortController flag (set by ordinary
        // AbortController / fetch-with-signal usage that is pure IL) would otherwise
        // force an unnecessary SharpTS.dll copy for the common case (#116).
        if (_features.UsesAbortSignalAny)
            requireRuntime("AbortSignal.any");

        var method = typeBuilder.DefineMethod(
            "AbortSignalAny",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        abort.SignalAny = method;

        var il = method.GetILGenerator();
        EmitReflectionCall(il, RuntimeTypesLateBoundName, "AbortSignalAnyCompiled", 1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// FireAbortEvent(Dictionary{string,object?} signal) → void
    /// Iterates listeners list and invokes each TSFunction/BoundTSFunction.
    /// Must be emitted before EmitAbortControllerMethods (which references it).
    /// </summary>
    private void EmitFireAbortEvent(TypeBuilder typeBuilder, EmittedAbortRuntime abort, TypeBuilder functionType, MethodBuilder functionInvoke, TypeBuilder boundFunctionType, MethodBuilder boundFunctionInvoke)
    {
        var method = typeBuilder.DefineMethod(
            "FireAbortEvent",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.DictionaryStringObject]
        );

        // Store so AbortControllerAbort can reference it
        abort.FireEvent = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        // Get listeners list
        var listenersLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "_listeners");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, listenersLocal);

        // for (int i = 0; i < listeners.Count; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopCheck = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCheck);

        il.MarkLabel(loopBody);
        // var listener = listeners[i]
        var listenerLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, listenerLocal);

        // if (listener is TSFunction tsFunc) tsFunc.Invoke(Array.Empty<object?>())
        var notTsFuncLabel = il.DefineLabel();
        var invokeEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Isinst, functionType);
        il.Emit(OpCodes.Brfalse, notTsFuncLabel);

        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Castclass, functionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, functionInvoke);
        il.Emit(OpCodes.Pop); // discard return
        il.Emit(OpCodes.Br, invokeEndLabel);

        // Also check BoundTSFunction
        il.MarkLabel(notTsFuncLabel);
        var notBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Isinst, boundFunctionType);
        il.Emit(OpCodes.Brfalse, notBoundLabel);

        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Castclass, boundFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, boundFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notBoundLabel);
        il.MarkLabel(invokeEndLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCheck);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, listenersLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Blt, loopBody);

        // Also call onabort if set
        var onabortLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "_onabort");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, onabortLocal);

        var noOnAbortLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, onabortLocal);
        il.Emit(OpCodes.Brfalse, noOnAbortLabel);

        // Check TSFunction
        var onAbortNotTsFunc = il.DefineLabel();
        var onAbortEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, onabortLocal);
        il.Emit(OpCodes.Isinst, functionType);
        il.Emit(OpCodes.Brfalse, onAbortNotTsFunc);

        il.Emit(OpCodes.Ldloc, onabortLocal);
        il.Emit(OpCodes.Castclass, functionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, functionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, onAbortEnd);

        // Check BoundTSFunction
        il.MarkLabel(onAbortNotTsFunc);
        il.Emit(OpCodes.Ldloc, onabortLocal);
        il.Emit(OpCodes.Isinst, boundFunctionType);
        il.Emit(OpCodes.Brfalse, onAbortEnd);

        il.Emit(OpCodes.Ldloc, onabortLocal);
        il.Emit(OpCodes.Castclass, boundFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, boundFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(onAbortEnd);
        il.MarkLabel(noOnAbortLabel);
        il.Emit(OpCodes.Ret);
    }
}
