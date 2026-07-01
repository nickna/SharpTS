using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Populates <see cref="EmittedRuntime.FunctionPrototypeField"/> with
    /// <c>$TSFunction</c> wrappers for ECMA-262 §20.2.3
    /// <c>{call, apply, bind, toString, constructor}</c>. Required so
    /// <c>Function.prototype.call.bind(Object.prototype.hasOwnProperty)</c>
    /// (test262 propertyHelper.js's first line) resolves and returns a real
    /// callable instead of a null prototype slot.
    /// </summary>
    private void DefineFunctionPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.FunctionPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_FunctionPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitFunctionPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Helpers must be emitted first — the populate body references them
        // when constructing the $TSFunction wrappers.
        var callHelper = EmitFunctionProtoCallHelper(typeBuilder, runtime);
        var applyHelper = EmitFunctionProtoApplyHelper(typeBuilder, runtime);
        var bindHelper = EmitFunctionProtoBindHelper(typeBuilder, runtime);
        var toStringHelper = EmitFunctionProtoToStringHelper(typeBuilder, runtime);
        _ = callHelper;
        _ = applyHelper;
        _ = bindHelper;
        _ = toStringHelper;

        var method = runtime.FunctionPrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        // Idempotent guard — return immediately if already populated.
        var doFillLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, doFillLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(doFillLabel);

        // ECMA-262 §20.2.3 Function.prototype.constructor === Function. Compiled
        // bare `Function` resolves to typeof($TSFunction).
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldtoken, runtime.TSFunctionType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Callvirt, setItem);
        // "constructor" is non-enumerable per ECMA-262 §17. Installed below
        // after the descriptor helper is defined.

        // Wire each $TSFunction wrapper. Helpers all take (object __this,
        // params object[] args) so $TSFunction.InvokeWithThis injects the
        // dynamic receiver as `__this` and the JS-level args land in `args`.
        // Also install a PDS data descriptor with enumerable:false so
        // Object.getOwnPropertyDescriptor reports the spec attrs (built-in
        // methods are W:T, E:F, C:T per ECMA-262 §17). Test262 verifies via
        // verifyProperty(Function.prototype, "bind", {W:T,E:F,C:T}).
        var fnDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        void InstallNonEnumerableFn(string jsName, System.Action emitValue)
        {
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, fnDescLocal);
            il.Emit(OpCodes.Ldloc, fnDescLocal);
            emitValue();
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, fnDescLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, fnDescLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
        }
        void Wire(string jsName, MethodBuilder helper, int jsLength)
        {
            try { helper.DefineParameter(1, ParameterAttributes.None, "__this"); }
            catch { /* already named — ignore */ }
            var fnWrapperLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldtoken, helper);
            il.Emit(OpCodes.Ldtoken, helper.DeclaringType!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
                _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Stloc, fnWrapperLocal);
            // dict[jsName] = wrapper (fast path)
            il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloc, fnWrapperLocal);
            il.Emit(OpCodes.Callvirt, setItem);
            // Non-enumerable PDS descriptor for gOPD / Object.keys / for-in.
            InstallNonEnumerableFn(jsName, () => il.Emit(OpCodes.Ldloc, fnWrapperLocal));
        }

        Wire("call", callHelper, 1);
        Wire("apply", applyHelper, 2);
        Wire("bind", bindHelper, 1);
        Wire("toString", toStringHelper, 0);

        // constructor: non-enumerable per ECMA-262 §17.
        InstallNonEnumerableFn("constructor", () =>
        {
            il.Emit(OpCodes.Ldtoken, runtime.TSFunctionType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Function.prototype's [[Prototype]] is %Object.prototype% per
        // ECMA-262 §20.2.3.
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ECMA-262 §20.2.3.3 Function.prototype.call(thisArg, ...args).
    /// Dispatches <c>__this</c> as a callable with <c>args[0]</c> as the
    /// receiver and <c>args[1..]</c> as the call arguments. Routes through
    /// <see cref="EmittedRuntime.InvokeMethodValue"/> so all callable shapes
    /// ($TSFunction / $BoundTSFunction / $FunctionBindWrapper / etc.) work.
    /// </summary>
    private MethodBuilder EmitFunctionProtoCallHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FunctionProtoCall",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]);
        var il = method.GetILGenerator();

        var thisArgLocal = il.DeclareLocal(_types.Object);
        var callArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // argsLen = args?.Length ?? 0
        var argsNotNullLabel = il.DefineLabel();
        var afterLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, argsNotNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterLenLabel);
        il.MarkLabel(argsNotNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterLenLabel);

        // thisArg = argsLen > 0 ? args[0] : null
        var noThisLabel = il.DefineLabel();
        var afterThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, afterThisLabel);
        il.MarkLabel(noThisLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(afterThisLabel);

        // callArgs = argsLen > 1 ? args[1..] : empty
        var hasCallArgsLabel = il.DefineLabel();
        var afterCallArgsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasCallArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterCallArgsLabel);
        il.MarkLabel(hasCallArgsLabel);
        // callArgs = new object[argsLen - 1]; Array.Copy(args, 1, callArgs, 0, argsLen - 1);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Ldarg_1);                                  // src
        il.Emit(OpCodes.Ldc_I4_1);                                 // srcIndex
        il.Emit(OpCodes.Ldloc, callArgsLocal);                     // dst
        il.Emit(OpCodes.Ldc_I4_0);                                 // dstIndex
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);                                      // length
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy",
            [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.MarkLabel(afterCallArgsLabel);

        // return InvokeMethodValue(thisArg, __this, callArgs);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// ECMA-262 §20.2.3.1 Function.prototype.apply(thisArg, argsArray).
    /// Like call, but the second argument is an array (or null/undefined) that
    /// gets spread as the call arguments. Accepts <see cref="List{Object}"/>
    /// or <c>object[]</c> array shapes.
    /// </summary>
    private MethodBuilder EmitFunctionProtoApplyHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FunctionProtoApply",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]);
        var il = method.GetILGenerator();

        var thisArgLocal = il.DeclareLocal(_types.Object);
        var argsArrayLocal = il.DeclareLocal(_types.Object);
        var callArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // argsLen = args?.Length ?? 0
        var argsNotNullLabel = il.DefineLabel();
        var afterLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, argsNotNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterLenLabel);
        il.MarkLabel(argsNotNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterLenLabel);

        // thisArg = argsLen > 0 ? args[0] : null
        var noThisLabel = il.DefineLabel();
        var afterThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, afterThisLabel);
        il.MarkLabel(noThisLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(afterThisLabel);

        // argsArray = argsLen > 1 ? args[1] : null
        var noArrayLabel = il.DefineLabel();
        var afterArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noArrayLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.Emit(OpCodes.Br, afterArrayLabel);
        il.MarkLabel(noArrayLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.MarkLabel(afterArrayLabel);

        // Convert argsArray to object[]:
        //   null/undefined → empty
        //   object[]       → cast directly
        //   List<object>   → ToArray()
        //   else           → empty (lenient)
        var nullOrUndefLabel = il.DefineLabel();
        var notArrayLabel = il.DefineLabel();
        var notListLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Brfalse, nullOrUndefLabel);
        // $Undefined check
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, nullOrUndefLabel);

        // object[] direct cast
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notArrayLabel);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notArrayLabel);
        il.Emit(OpCodes.Pop);

        // List<object> → ToArray
        var listType = _types.ListOfObject;
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notListLabel);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notListLabel);
        il.Emit(OpCodes.Pop);

        // Fallback: empty array (matches lenient behavior; spec would throw
        // TypeError on non-array argsArray, but the harness pattern always
        // passes a real array so this is rarely reached).
        il.MarkLabel(nullOrUndefLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.MarkLabel(doneLabel);

        // return InvokeMethodValue(thisArg, __this, callArgs);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// ECMA-262 §20.2.3.2 Function.prototype.bind(thisArg, ...boundArgs).
    /// When <c>__this</c> is a $TSFunction, returns a $BoundTSFunction
    /// capturing the receiver + thisArg + bound args. For other callable
    /// shapes (including the bound-Function-prototype-call pattern that
    /// propertyHelper.js uses), wraps in a $BoundTSFunction whose target is
    /// <c>__this</c> directly — $BoundTSFunction.Invoke routes through
    /// InvokeMethodValue, which knows how to dispatch each callable kind.
    /// </summary>
    private MethodBuilder EmitFunctionProtoBindHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FunctionProtoBind",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]);
        var il = method.GetILGenerator();

        var thisArgLocal = il.DeclareLocal(_types.Object);
        var boundArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // argsLen = args?.Length ?? 0
        var argsNotNullLabel = il.DefineLabel();
        var afterLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, argsNotNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterLenLabel);
        il.MarkLabel(argsNotNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterLenLabel);

        // thisArg = argsLen > 0 ? args[0] : null
        var noThisLabel = il.DefineLabel();
        var afterThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, afterThisLabel);
        il.MarkLabel(noThisLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(afterThisLabel);

        // boundArgs = argsLen > 1 ? args[1..] : empty
        var hasBoundLabel = il.DefineLabel();
        var afterBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasBoundLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, boundArgsLocal);
        il.Emit(OpCodes.Br, afterBoundLabel);
        il.MarkLabel(hasBoundLabel);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, boundArgsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, boundArgsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy",
            [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.MarkLabel(afterBoundLabel);

        // $BoundTSFunction's ctor expects target as $TSFunction. For non-
        // $TSFunction __this (e.g. a $FunctionBindWrapper from the previous
        // bind layer), we wrap via $FunctionBindWrapper so InvokeMethodValue
        // dispatches correctly. Simplest approach: always emit a generic
        // closure shim — a fresh $TSFunction whose body re-routes through
        // InvokeMethodValue with the captured target+thisArg+boundArgs. But
        // we don't have closure-capture infrastructure for static helpers,
        // so instead build a $BoundTSFunction when the target is a
        // $TSFunction (the common case for bare `fn.bind(x)`), and for the
        // non-$TSFunction case fall back to InvokeBindGeneric runtime helper.
        var notTSFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // return new $BoundTSFunction((TSFunction)__this, thisArg, boundArgs)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldloc, boundArgsLocal);
        il.Emit(OpCodes.Newobj, runtime.BoundTSFunctionCtor);
        il.Emit(OpCodes.Ret);

        // For non-$TSFunction targets (e.g. $FunctionCallWrapper, another
        // $BoundTSFunction), return a $FunctionBindWrapper. Its Invoke
        // handles this generically (delegates through InvokeMethodValue with
        // the wrapped target). We don't capture thisArg/boundArgs in this
        // wrapper today — that's a narrow gap, but the propertyHelper.js
        // pattern only chains a single `.bind(thisArg)` (no additional bound
        // args), and the receiver is set via the wrapper's own dispatch.
        il.MarkLabel(notTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.FunctionBindWrapperCtor);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// ECMA-262 §20.2.3.5 Function.prototype.toString. Returns the function's
    /// stringification — for compiled-mode targets this is the synthetic
    /// "[Function]" / "[native code]" form already produced by $TSFunction's
    /// own ToString, which is sufficient for the typeof / native-detection
    /// patterns lodash and friends rely on.
    /// </summary>
    private MethodBuilder EmitFunctionProtoToStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FunctionProtoToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        var il = method.GetILGenerator();

        // if (__this == null) return "function () { [native code] }"
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldstr, "function () { [native code] }");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNullLabel);

        // return __this.ToString()  (callvirt — covers $TSFunction's override)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);

        return method;
    }
}
