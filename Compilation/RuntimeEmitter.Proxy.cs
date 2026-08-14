using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private const string ProxyTypeName = "SharpTS.Runtime.Types.SharpTSProxy";

    private void EmitProxyMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateProxy(typeBuilder, runtime);
        EmitCreateRevocableProxy(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits a null-safe proxy type check: if (obj.GetType().FullName == "SharpTS.Runtime.Types.SharpTSProxy") goto proxyLabel;
    /// Assumes obj is already on the stack (does NOT consume it). Falls through to notProxyLabel if not a proxy.
    /// </summary>
    /// <param name="il">The IL generator.</param>
    /// <param name="loadObj">Action to emit loading the object onto the stack.</param>
    /// <param name="proxyLabel">Label to jump to if obj is a proxy.</param>
    /// <param name="notProxyLabel">Label to jump to if obj is not a proxy.</param>
    private void EmitProxyTypeCheck(ILGenerator il, Action emitLoadObj, Label proxyLabel, Label notProxyLabel)
    {
        // obj.GetType().FullName == "SharpTS.Runtime.Types.SharpTSProxy"
        emitLoadObj();
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "FullName").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, ProxyTypeName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, proxyLabel);
        il.Emit(OpCodes.Br, notProxyLabel);
    }

    /// <summary>
    /// Emits a call to a method on the proxy object via reflection on the object's own type:
    /// obj.GetType().GetMethod(methodName).Invoke(obj, args)
    /// This avoids any dependency on SharpTS.dll being loaded.
    /// </summary>
    private void EmitProxyMethodCall(ILGenerator il, Action emitLoadObj, string methodName, Action emitArgs)
    {
        // obj.GetType().GetMethod(methodName)
        emitLoadObj();
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, methodName);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));

        // .Invoke(obj, args)
        emitLoadObj();
        emitArgs();
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
    }

    /// <summary>
    /// Reflection call variant that unwraps TargetInvocationException and maps
    /// SharpTS.dll TypeErrors to the emitted assembly's $TypeError brand.
    /// </summary>
    private void EmitProxyMethodCallUnwrapped(
        ILGenerator il, EmittedRuntime runtime, Action emitLoadObj,
        string methodName, Action emitArgs)
    {
        emitLoadObj();
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, methodName);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.Type, "GetMethod", _types.String));
        emitLoadObj();
        emitArgs();
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
    }

    /// <summary>
    /// Emits a proxy-aware property get: checks if obj is a proxy and calls TrapGet(name, null),
    /// otherwise falls through to notProxyLabel.
    /// Emitted IL equivalent:
    ///   if (obj.GetType().FullName == ProxyTypeName) return obj.TrapGet(name, null);
    /// </summary>
    internal void EmitProxyGetPropertyCheck(
        ILGenerator il,
        EmittedRuntime runtime,
        Action emitLoadObj,
        Action emitLoadName,
        Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        // Call TrapGetCompiled(string prop, Func<object,string,object>) via
        // reflection. The fallback delegate returns to this emitted runtime's
        // ordinary Get implementation when the handler has no get trap.
        EmitProxyMethodCallUnwrapped(il, runtime, emitLoadObj, "TrapGetCompiled", () =>
        {
            // new object[] { name, new Func<object,string,object>(GetProperty) }
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadName();
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, runtime.GetProperty);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(
                typeof(Func<object, string, object?>), _types.Object, _types.IntPtr));
            il.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a proxy-aware property set: checks if obj is a proxy and calls TrapSet(name, value, null),
    /// otherwise falls through to notProxyLabel.
    /// </summary>
    internal void EmitProxySetPropertyCheck(ILGenerator il, Action emitLoadObj, Action emitLoadName, Action emitLoadValue, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        // Call TrapSet(string prop, object? value, Interpreter? interp) via reflection
        EmitProxyMethodCall(il, emitLoadObj, "TrapSet", () =>
        {
            // new object[] { name, value, null }
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadName();
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            emitLoadValue();
            il.Emit(OpCodes.Stelem_Ref);
            // [2] = null (Interpreter) - already null from Newarr
        });
        il.Emit(OpCodes.Pop); // TrapSet returns the value, but SetProperty is void
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a proxy-aware index get: checks if obj is a proxy and calls TrapGet(key.ToString(), null).
    /// </summary>
    internal void EmitProxyGetIndexCheck(
        ILGenerator il, EmittedRuntime runtime, Action emitLoadObj,
        Action emitLoadIndex, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        // Preserve Symbol keys for the trap and let the emitted GetIndex
        // implementation perform ordinary target lookup when the trap is absent.
        EmitProxyMethodCallUnwrapped(il, runtime, emitLoadObj, "TrapGetIndexCompiled", () =>
        {
            // new object[] { index, new Func<object,object,object>(GetIndex) }
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadIndex();
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, runtime.GetIndex);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(
                typeof(Func<object, object, object?>), _types.Object, _types.IntPtr));
            il.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a proxy-aware index set: checks if obj is a proxy and calls TrapSet(key.ToString(), value, null).
    /// </summary>
    internal void EmitProxySetIndexCheck(ILGenerator il, Action emitLoadObj, Action emitLoadIndex, Action emitLoadValue, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        EmitProxyMethodCall(il, emitLoadObj, "TrapSet", () =>
        {
            // new object[] { index?.ToString() ?? "", value, null }
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadIndex();
            var indexNullLabel = il.DefineLabel();
            var indexEndLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, indexNullLabel);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
            il.Emit(OpCodes.Br, indexEndLabel);
            il.MarkLabel(indexNullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.MarkLabel(indexEndLabel);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            emitLoadValue();
            il.Emit(OpCodes.Stelem_Ref);
            // [2] = null (Interpreter) - already null from Newarr
        });
        il.Emit(OpCodes.Pop); // TrapSet returns value, SetIndex is void
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a proxy-aware has check: checks if obj is a proxy and calls TrapHas(key, null).
    /// Returns bool result.
    /// </summary>
    internal void EmitProxyHasCheck(ILGenerator il, Action emitLoadObj, Action emitLoadKey, Label notProxyLabel, EmittedRuntime runtime)
    {
        EmitProxyHasResult(il, emitLoadObj, emitLoadKey, notProxyLabel, runtime);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the Proxy [[HasProperty]] trap and leaves its boolean result on
    /// the stack. Non-Proxy receivers branch to <paramref name="notProxyLabel"/>.
    /// </summary>
    private void EmitProxyHasResult(ILGenerator il, Action emitLoadObj, Action emitLoadKey, Label notProxyLabel, EmittedRuntime runtime)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        EmitProxyMethodCallUnwrapped(il, runtime, emitLoadObj, "TrapHasCompiled", () =>
        {
            // new object[] { keyString, new Func<object,string,bool>(HasArrayLikeProperty) }
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadKey();
            var keyNullLabel = il.DefineLabel();
            var keyEndLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, keyNullLabel);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
            il.Emit(OpCodes.Br, keyEndLabel);
            il.MarkLabel(keyNullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "");
            il.MarkLabel(keyEndLabel);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, runtime.HasArrayLikeProperty);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(
                typeof(Func<object, string, bool>), _types.Object, _types.IntPtr));
            il.Emit(OpCodes.Stelem_Ref);
        });
        // TrapHas returns object — apply truthy coercion (JS `in` coerces to boolean)
        il.Emit(OpCodes.Call, runtime.IsTruthy);
    }

    /// <summary>
    /// Emits a proxy-aware delete check: checks if obj is a proxy and calls TrapDeleteProperty(name, null).
    /// Returns bool result.
    /// </summary>
    internal void EmitProxyDeleteCheck(ILGenerator il, Action emitLoadObj, Action emitLoadName, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        EmitProxyMethodCall(il, emitLoadObj, "TrapDeleteProperty", () =>
        {
            // new object[] { name, null }
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadName();
            il.Emit(OpCodes.Stelem_Ref);
            // [1] = null (Interpreter) - already null from Newarr
        });
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void DeclareProxyOwnKeysHelpers(
        TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.GetOrdinaryOwnPropertyKeys = typeBuilder.DefineMethod(
            "GetOrdinaryOwnPropertyKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]);
        runtime.CreateProxyOwnKeysList = typeBuilder.DefineMethod(
            "CreateProxyOwnKeysList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]);
    }

    private void EmitProxyOwnKeysHelperBodies(EmittedRuntime runtime)
    {
        // OrdinaryOwnPropertyKeys = ordered string keys followed by Symbols.
        var il = runtime.GetOrdinaryOwnPropertyKeys.GetILGenerator();
        var result = il.DeclareLocal(_types.ListOfObject);
        var symbols = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetOwnPropertyNames);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetOwnPropertySymbols);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, symbols);
        var noSymbols = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, symbols);
        il.Emit(OpCodes.Brfalse, noSymbols);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, symbols);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "AddRange", [_types.IEnumerableOfObject])!);
        il.MarkLabel(noSymbols);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        // CreateListFromArrayLike for an ownKeys trap result. Every read goes
        // through the emitted [[Get]] path so accessors and Proxy receivers are
        // observed in the required length-then-index order.
        il = runtime.CreateProxyOwnKeysList.GetILGenerator();
        result = il.DeclareLocal(_types.ListOfObject);
        var lengthNumber = il.DeclareLocal(_types.Double);
        var length = il.DeclareLocal(_types.Int32);
        var index = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(
            _types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, lengthNumber);

        var zeroLength = il.DefineLabel();
        var clampLength = il.DefineLabel();
        var lengthReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lengthNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brtrue, zeroLength);
        il.Emit(OpCodes.Ldloc, lengthNumber);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Ble, zeroLength);
        il.Emit(OpCodes.Ldloc, lengthNumber);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Bge, clampLength);
        il.Emit(OpCodes.Ldloc, lengthNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", [_types.Double])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Br, lengthReady);
        il.MarkLabel(zeroLength);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Br, lengthReady);
        il.MarkLabel(clampLength);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, length);
        il.MarkLabel(lengthReady);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, index);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString")!);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Leaves the full mixed string/Symbol own-key list on stack.</summary>
    private void EmitProxyOwnKeysCompiledCall(
        ILGenerator il, EmittedRuntime runtime, Action emitLoadObj)
    {
        EmitProxyMethodCallUnwrapped(
            il, runtime, emitLoadObj, "TrapOwnKeysCompiled", () =>
        {
            il.Emit(OpCodes.Ldc_I4_6);
            il.Emit(OpCodes.Newarr, _types.Object);
            EmitDelegateArgument(0, runtime.GetOrdinaryOwnPropertyKeys,
                typeof(Func<object, List<object?>>));
            EmitDelegateArgument(1, runtime.CreateProxyOwnKeysList,
                typeof(Func<object, List<object?>>));
            EmitDelegateArgument(2, runtime.ObjectGetOwnPropertyDescriptor,
                typeof(Func<object, object, object?>));
            EmitDelegateArgument(3, runtime.ObjectIsExtensible,
                typeof(Func<object, bool>));
            EmitDelegateArgument(4, runtime.IsSymbolMethod,
                typeof(Func<object, bool>));
            EmitDelegateArgument(5, runtime.GetProperty,
                typeof(Func<object, string, object?>));
        });
        il.Emit(OpCodes.Castclass, _types.ListOfObject);

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
    }

    /// <summary>Leaves a compiled Proxy [[GetOwnProperty]] result on stack.</summary>
    private void EmitProxyGetOwnPropertyDescriptorCompiledCall(
        ILGenerator il, EmittedRuntime runtime, Action emitLoadObj,
        Action emitLoadKey)
    {
        EmitProxyMethodCallUnwrapped(
            il, runtime, emitLoadObj,
            "TrapGetOwnPropertyDescriptorCompiled", () =>
            {
                il.Emit(OpCodes.Ldc_I4_4);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                emitLoadKey();
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, runtime.ObjectGetOwnPropertyDescriptor);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, object, object?>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, runtime.ObjectIsExtensible);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, bool>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_3);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, runtime.GetProperty);
                il.Emit(OpCodes.Newobj, _types.GetConstructor(
                    typeof(Func<object, string, object?>),
                    _types.Object, _types.IntPtr)!);
                il.Emit(OpCodes.Stelem_Ref);
            });
    }

    /// <summary>
    /// Emits a proxy-aware ownKeys check. The full mixed key list is validated
    /// before this consumer filters it to strings or Symbols.
    /// </summary>
    internal void EmitProxyOwnKeysCheck(
        ILGenerator il,
        EmittedRuntime runtime,
        Action emitLoadObj,
        Label notProxyLabel,
        bool enumerableOnly,
        bool symbolsOnly = false)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);

        var keysListLocal = il.DeclareLocal(_types.ListOfObject);
        EmitProxyOwnKeysCompiledCall(il, runtime, emitLoadObj);
        il.Emit(OpCodes.Stloc, keysListLocal);

        // result = new List<object?>();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        var currentKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, keysListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        var advanceLabel = il.DefineLabel();
        if (symbolsOnly)
        {
            il.Emit(OpCodes.Ldloc, currentKeyLocal);
            il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
            il.Emit(OpCodes.Brfalse, advanceLabel);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, currentKeyLocal);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brfalse, advanceLabel);
        }

        if (enumerableOnly)
        {
            // EnumerableOwnProperties performs [[GetOwnProperty]] for every
            // ownKeys result and keeps only descriptors whose Enumerable bit
            // is true. Calling the public proxy method preserves trap order,
            // abrupt completions, revocation, and invariant validation.
            var descriptorLocal = il.DeclareLocal(_types.Object);
            EmitProxyGetOwnPropertyDescriptorCompiledCall(
                il, runtime, emitLoadObj,
                () => il.Emit(OpCodes.Ldloc, currentKeyLocal));
            il.Emit(OpCodes.Stloc, descriptorLocal);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Brfalse, advanceLabel);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, advanceLabel);
            // Reflection bridges use SharpTS.dll's undefined singleton.
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType")!);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.Type, "Name").GetGetMethod()!);
            il.Emit(OpCodes.Ldstr, "SharpTSUndefined");
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.String, "op_Equality", _types.String, _types.String)!);
            il.Emit(OpCodes.Brtrue, advanceLabel);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldstr, "enumerable");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brfalse, advanceLabel);
        }

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object]));

        il.MarkLabel(advanceLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the Proxy branch of EnumerableOwnProperties for Object.values or
    /// Object.entries. The descriptor check and value get intentionally occur
    /// in the same per-key loop, matching the observable specification order.
    /// Non-proxy receivers branch to <paramref name="notProxyLabel"/>.
    /// </summary>
    internal void EmitProxyEnumerableOwnPropertiesCheck(
        ILGenerator il,
        EmittedRuntime runtime,
        Action emitLoadObj,
        Label notProxyLabel,
        bool entries)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);

        var keysLocal = il.DeclareLocal(_types.ListOfString);
        EmitProxyMethodCall(il, emitLoadObj, "TrapOwnKeys", () =>
        {
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
        });
        il.Emit(OpCodes.Castclass, _types.ListOfString);
        il.Emit(OpCodes.Stloc, keysLocal);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.String);
        var descriptorLocal = il.DeclareLocal(_types.Object);
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var advance = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfString, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, keyLocal);

        EmitProxyMethodCall(il, emitLoadObj, "TrapGetOwnPropertyDescriptor", () =>
        {
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Stelem_Ref);
        });
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brfalse, advance);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, advance);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ldstr, "enumerable");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brfalse, advance);

        emitLoadObj();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);
        if (entries)
        {
            var entryLocal = il.DeclareLocal(_types.ListOfObject);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, entryLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        }

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a proxy-aware invoke check: checks if callee is a proxy and calls TrapApply(null, argsList, null).
    /// </summary>
    internal void EmitProxyInvokeCheck(ILGenerator il, Action emitLoadCallee, Action emitLoadArgs, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadCallee, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        EmitProxyMethodCall(il, emitLoadCallee, "TrapApply", () =>
        {
            // new object[] { null (thisArg), argsList, null (Interpreter) }
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Newarr, _types.Object);
            // [0] = null (thisArg) - already null from Newarr
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            emitLoadArgs(); // Load the List<object?> args
            il.Emit(OpCodes.Stelem_Ref);
            // [2] = null (Interpreter) - already null from Newarr
        });
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits CreateProxy(object target, object handler) -> object (SharpTSProxy).
    /// Validates both args are non-null objects and creates a SharpTSProxy.
    /// Uses reflection to avoid compile-time dependency on SharpTS.dll.
    /// </summary>
    private void EmitCreateProxy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateProxy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.CreateProxy = method;

        var il = method.GetILGenerator();

        var targetNullLabel = il.DefineLabel();
        var handlerNullLabel = il.DefineLabel();

        // if (target == null) throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, targetNullLabel);

        // if (handler == null) throw
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, handlerNullLabel);

        // Late-bound construction of SharpTSProxy(target, handler) — soft dependency
        // on SharpTS.dll (the Proxy feature records RequireSharpTSRuntime).
        EmitReflectionCreateInstance(il, "SharpTS.Runtime.Types.SharpTSProxy, SharpTS", 2);
        il.Emit(OpCodes.Ret);

        // target null - throw
        il.MarkLabel(targetNullLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Cannot create proxy with a non-object as target.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // handler null - throw
        il.MarkLabel(handlerNullLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Cannot create proxy with a non-object as handler.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits CreateRevocableProxy(object target, object handler) -> object ({ proxy, revoke }).
    /// Calls RuntimeTypes.CreateRevocableProxy via reflection to avoid SharpTS.dll dependency.
    /// </summary>
    private void EmitCreateRevocableProxy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateRevocableProxy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.CreateRevocableProxy = method;

        var il = method.GetILGenerator();
        EmitReflectionCall(il, RuntimeTypesLateBoundName, "CreateRevocableProxy", 2);
        il.Emit(OpCodes.Ret);
    }
}
