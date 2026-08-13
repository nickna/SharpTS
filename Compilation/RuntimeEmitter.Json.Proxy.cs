using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitInvokeMethodUnwrapped(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("InvokeMethodUnwrapped",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.MethodBase, _types.Object, _types.ObjectArray]);
        runtime.InvokeMethodUnwrapped = method;
        var il = method.GetILGenerator();
        var result = il.DeclareLocal(_types.Object);
        var invocationException = il.DeclareLocal(_types.TargetInvocationException);
        var innerException = il.DeclareLocal(_types.Exception);
        var foreignThrowValue = il.DeclareLocal(_types.Object);
        var done = il.DefineLabel();
        var haveInnerException = il.DefineLabel();
        var rethrowInnerException = il.DefineLabel();
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Leave, done);
        il.BeginCatchBlock(typeof(TargetInvocationException));
        il.Emit(OpCodes.Stloc, invocationException);

        // MethodInfo.Invoke wraps every exception. Most emitted helpers can simply
        // rethrow the inner exception, but Proxy traps execute through the interpreter
        // and therefore surface guest throws as SharpTS's host-side ThrowException.
        // Translate its TypeError value into this standalone assembly's $TypeError so
        // constructor/prototype identity survives the interpreter/compiler boundary.
        il.Emit(OpCodes.Ldloc, invocationException);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, innerException);
        il.Emit(OpCodes.Ldloc, innerException);
        il.Emit(OpCodes.Brtrue, haveInnerException);
        il.Emit(OpCodes.Ldloc, invocationException);
        il.Emit(OpCodes.Stloc, innerException);

        il.MarkLabel(haveInnerException);
        il.Emit(OpCodes.Ldloc, innerException);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "FullName").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Exceptions.ThrowException");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, rethrowInnerException);

        // value = inner.GetType().GetProperty("Value").GetValue(inner)
        il.Emit(OpCodes.Ldloc, innerException);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Value");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Ldloc, innerException);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, foreignThrowValue);

        // Only translate the runtime TypeError class here. Other foreign thrown
        // values retain the established host-exception fallback semantics.
        il.Emit(OpCodes.Ldloc, foreignThrowValue);
        il.Emit(OpCodes.Brfalse, rethrowInnerException);
        il.Emit(OpCodes.Ldloc, foreignThrowValue);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "FullName").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.SharpTSTypeError");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, rethrowInnerException);

        // Read the foreign error's Message and construct the emitted equivalent.
        il.Emit(OpCodes.Ldloc, foreignThrowValue);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Message");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Ldloc, foreignThrowValue);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Newobj, runtime.TSTypeErrorCtor);
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(rethrowInnerException);
        il.Emit(OpCodes.Ldloc, innerException);
        il.Emit(OpCodes.Throw);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL that materializes a SharpTSProxy into a Dictionary&lt;string, object?&gt; by
    /// dispatching the proxy's [[OwnPropertyKeys]] (TrapOwnKeys) and [[Get]] (TrapGet)
    /// traps. Used by JSON.stringify so the existing dict-iteration path can serialize
    /// the proxy without each call site needing trap awareness.
    ///
    /// On entry: <paramref name="valueLocal"/> holds the proxy reference.
    /// On exit (via fall-through): <paramref name="valueLocal"/> holds the materialized
    /// Dictionary&lt;string, object?&gt; and execution continues at the caller's instruction.
    /// On non-proxy: branches to <paramref name="notProxyLabel"/> with valueLocal unchanged.
    ///
    /// Uses late-bound reflection (Type.GetType(..., SharpTS)) to avoid embedding a
    /// SharpTS.dll reference in the emitted assembly per the standalone-DLL constraint.
    /// </summary>
    private void EmitProxyMaterializeForJson(
        ILGenerator il, LocalBuilder valueLocal, Label notProxyLabel, EmittedRuntime runtime)
    {
        var proxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(il, () => il.Emit(OpCodes.Ldloc, valueLocal), proxyLabel, notProxyLabel);

        il.MarkLabel(proxyLabel);

        // proxyType = Type.GetType("SharpTS.Runtime.Types.SharpTSProxy, SharpTS")
        var proxyTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldstr, "SharpTS.Runtime.Types.SharpTSProxy, SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, proxyTypeLocal);

        // keys = (List<string>)proxyType.GetMethod("TrapOwnKeys").Invoke(proxy, new object[]{ null })
        // TrapOwnKeys throws if the proxy is revoked — surfaces the spec-required TypeError.
        var keysLocal = il.DeclareLocal(_types.ListOfString);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapOwnKeys");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        // [0] = null (Interpreter) — already null from Newarr
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Castclass, _types.ListOfString);
        il.Emit(OpCodes.Stloc, keysLocal);

        // Cache trapGet MethodInfo across the loop.
        var trapGetMiLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, proxyTypeLocal);
        il.Emit(OpCodes.Ldstr, "TrapGet");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, trapGetMiLocal);

        // dict = new Dictionary<string, object?>();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, dictLocal);

        // for (int i = 0; i < keys.Count; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfString, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // k = keys[i]
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, keyLocal);

        // v = trapGetMi.Invoke(proxy, new object[]{ k, null })
        var valTmpLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, trapGetMiLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Stelem_Ref);
        // [1] = null (Interpreter) — already null from Newarr
        il.Emit(OpCodes.Call, runtime.InvokeMethodUnwrapped);
        il.Emit(OpCodes.Stloc, valTmpLocal);

        // dict[k] = v
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, valTmpLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // valueLocal = dict — caller will continue down the dict-stringify path.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
    }
}
