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
        EmitProxyMethodCall(il, emitLoadObj, "TrapGetCompiled", () =>
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
    internal void EmitProxyGetIndexCheck(ILGenerator il, Action emitLoadObj, Action emitLoadIndex, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        // Convert index to string and call TrapGet
        EmitProxyMethodCall(il, emitLoadObj, "TrapGet", () =>
        {
            // new object[] { index?.ToString() ?? "", null }
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            emitLoadIndex();
            // Convert index to string via ToString
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
            // [1] = null (Interpreter) - already null from Newarr
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
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);
        EmitProxyMethodCall(il, emitLoadObj, "TrapHas", () =>
        {
            // new object[] { key?.ToString() ?? "", null }
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
            // [1] = null (Interpreter) - already null from Newarr
        });
        // TrapHas returns object — apply truthy coercion (JS `in` coerces to boolean)
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Ret);
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

    /// <summary>
    /// Emits a proxy-aware ownKeys check: if obj is a SharpTSProxy, dispatches
    /// TrapOwnKeys (List&lt;string&gt;), upcasts to List&lt;object?&gt; via element-by-element
    /// copy, and returns. The caller's surrounding method must declare its return
    /// type as List&lt;object?&gt;. Falls through to <paramref name="notProxyLabel"/>
    /// otherwise. Used by Object.keys / Object.getOwnPropertyNames.
    /// </summary>
    internal void EmitProxyOwnKeysCheck(ILGenerator il, Action emitLoadObj, Label notProxyLabel)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, emitLoadObj, proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);

        // (List<string>)proxy.GetType().GetMethod("TrapOwnKeys").Invoke(proxy, new object[]{ null })
        var keysListLocal = il.DeclareLocal(_types.ListOfString);
        emitLoadObj();
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "TrapOwnKeys");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        emitLoadObj();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        // [0] = null (Interpreter) — already null from Newarr
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Castclass, _types.ListOfString);
        il.Emit(OpCodes.Stloc, keysListLocal);

        // result = new List<object?>();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (int i = 0; i < keysList.Count; i++) result.Add(keysList[i]);
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfString, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keysListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object]));

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
