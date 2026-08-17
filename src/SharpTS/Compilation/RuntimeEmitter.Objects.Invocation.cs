using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a guest-recursion guard at the top of a dynamic-invocation
    /// helper: (1) CheckCancellation, so deep non-loop recursion — which
    /// never crosses a loop backedge — remains cooperatively cancellable;
    /// (2) RuntimeHelpers.TryEnsureSufficientExecutionStack, converting an
    /// imminent (uncatchable, process-killing) CLR StackOverflowException
    /// into a catchable guest RangeError. Issue #180.
    /// </summary>
    private void EmitStackGuard(ILGenerator il, EmittedRuntime runtime)
    {
        if (runtime.CheckCancellationMethod != null)
            il.Emit(OpCodes.Call, runtime.CheckCancellationMethod);
        var stackOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Call, typeof(System.Runtime.CompilerServices.RuntimeHelpers)
            .GetMethod("TryEnsureSufficientExecutionStack", Type.EmptyTypes)!);
        il.Emit(OpCodes.Brtrue, stackOkLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "Maximum call stack size exceeded");
        il.MarkLabel(stackOkLabel);
    }

    /// <summary>
    /// Emits a guest TypeError throw — "&lt;typeof callee&gt; is not a function" —
    /// matching the interpreter's message for invoking a non-callable (#260).
    /// <paramref name="loadCallee"/> must push the callee value onto the stack.
    /// </summary>
    private void EmitThrowNotAFunction(ILGenerator il, EmittedRuntime runtime, Action loadCallee)
    {
        loadCallee();
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, " is not a function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
    }

    private void EmitGetArrayMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetArrayMethod(object arr, string methodName) -> TSFunction or null
        // Maps TypeScript array method names to .NET List methods
        var method = typeBuilder.DefineMethod(
            "GetArrayMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetArrayMethod = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var notArrayLabel = il.DefineLabel();

        // Check if obj is List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notArrayLabel);

        // Map TypeScript method name to .NET method name
        // push -> Add, pop -> RemoveAt(Count-1), etc.
        var pushLabel = il.DefineLabel();
        var popLabel = il.DefineLabel();
        var shiftLabel = il.DefineLabel();

        // Check for "push"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "push");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, pushLabel);

        // Check for "pop"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "pop");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, popLabel);

        // Unknown array method - return null
        il.Emit(OpCodes.Br, nullLabel);

        // Handle push - wrap List.Add as TSFunction
        il.MarkLabel(pushLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        // Handle pop - need special handling since pop returns removed element
        il.MarkLabel(popLabel);
        // For pop, we'll create a TSFunction that wraps a helper method
        // For now, return null and handle pop differently
        il.Emit(OpCodes.Br, nullLabel);

        il.MarkLabel(notArrayLabel);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitInvokeValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        runtime.InvokeValue = method;

        var il = method.GetILGenerator();
        EmitStackGuard(il, runtime);
        var nullLabel = il.DefineLabel();
        var tsFunctionLabel = il.DefineLabel();
        var boundTsFunctionLabel = il.DefineLabel();
        var bindWrapperLabel = il.DefineLabel();
        var callWrapperLabel = il.DefineLabel();
        var applyWrapperLabel = il.DefineLabel();
        var textDecoderDecodeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // ECMA-262 §22.2.6: a RegExp object is not callable — calling one throws
        // TypeError. Checked early so it doesn't fall into the dispatch chain.
        if (runtime.TSRegExpType != null)
        {
            var notRegExpCallee = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpCallee);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "called value is not a function");
            il.MarkLabel(notRegExpCallee);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, boundTsFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Brtrue, bindWrapperLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Brtrue, callWrapperLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Brtrue, applyWrapperLabel);

        if (_features.UsesTextEncoding)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSTextDecoderDecodeMethodType);
            il.Emit(OpCodes.Brtrue, textDecoderDecodeLabel);
        }

        // Stream callback wrappers — only meaningful when Node streams emit.
        var transformCbLabel = il.DefineLabel();
        var writeCallbackWrapperLabel = il.DefineLabel();
        if (_features.UsesNodeStreams)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TransformDoneCallbackType);
            il.Emit(OpCodes.Brtrue, transformCbLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.WriteCallbackWrapperType);
            il.Emit(OpCodes.Brtrue, writeCallbackWrapperLabel);
        }

        var resolveCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.PromiseResolveCallbackType);
        il.Emit(OpCodes.Brtrue, resolveCallbackLabel);

        var rejectCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.PromiseRejectCallbackType);
        il.Emit(OpCodes.Brtrue, rejectCallbackLabel);

        var boundArrayMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brtrue, boundArrayMethodLabel);

        // $BoundTypedArrayMethod (#940) — value-position call (`const f = a.fill; f(x)`).
        Label boundTypedArrayMethodLabel = default;
        if (_features.HasAnyTypedArray)
        {
            boundTypedArrayMethodLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundTypedArrayMethodType);
            il.Emit(OpCodes.Brtrue, boundTypedArrayMethodLabel);
        }

        var boundMapMethodLabel = il.DefineLabel();
        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Brtrue, boundMapMethodLabel);
        }

        var boundSetMethodLabel = il.DefineLabel();
        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Brtrue, boundSetMethodLabel);
        }

        var boundAnyFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brtrue, boundAnyFunctionLabel);

        // Handle Func<object?[], object?> (from CreateBoundMethod in RuntimeTypes.Methods)
        var funcDelegateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Brtrue, funcDelegateLabel);

        // Handle $MethodCallable
        var methodCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.MethodCallableType);
        il.Emit(OpCodes.Brtrue, methodCallableLabel);

        // Built-in type constructors stored as values (issue #61): patterns
        // like lodash's `var Array = context.Array; Array(n)` store the
        // emitted .NET Type token in a local, then call it. Dispatch here
        // to the corresponding runtime constructor helper so the call form
        // produces a real instance rather than null.
        var typeCalleeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeCalleeLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxyInvokeCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), notProxyLabel);

        // ECMA-262 §7.3.13: invoking a non-callable throws TypeError. This was
        // a silent `return null` for years, which masked dispatch regressions
        // (#239) — see #260.
        il.MarkLabel(notProxyLabel);
        EmitThrowNotAFunction(il, runtime, () => il.Emit(OpCodes.Ldarg_0));

        // Type dispatch — currently only Array (IList<object>) has a runtime
        // constructor helper wired up. Other built-in Type constructors
        // (Date, Map, Set, RegExp, Promise, …) called without `new` throw a
        // TypeError below; they can be wired in incrementally as usage
        // patterns surface.
        il.MarkLabel(typeCalleeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", [_types.RuntimeTypeHandle])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        var notArrayTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notArrayTypeLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ArrayConstructor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArrayTypeLabel);

        // $TSSymbol (#234): bare `Symbol` resolves to the $TSSymbol Type token,
        // so the aliased call form (`const f = Symbol; f("desc")`) lands here.
        // Mirrors BuiltInConstructorHandler.EmitSymbol: description is
        // Stringify(args[0]) when present, null otherwise.
        var notSymbolTypeLabel = il.DefineLabel();
        var symbolNoDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldtoken, runtime.TSSymbolType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", [_types.RuntimeTypeHandle])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brfalse, notSymbolTypeLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Brfalse, symbolNoDescLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        var symbolDescriptionLocal = il.DeclareLocal(_types.Object);
        var symbolStringifyDescriptionLabel = il.DefineLabel();
        var symbolDescriptionReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Stloc, symbolDescriptionLocal);
        il.Emit(OpCodes.Ldloc, symbolDescriptionLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, symbolStringifyDescriptionLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, symbolDescriptionReadyLabel);
        il.MarkLabel(symbolStringifyDescriptionLabel);
        il.Emit(OpCodes.Ldloc, symbolDescriptionLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.MarkLabel(symbolDescriptionReadyLabel);
        il.Emit(OpCodes.Newobj, runtime.TSSymbolCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolNoDescLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, runtime.TSSymbolCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSymbolTypeLabel);

        // Object call form (`var f = Object; f(x)` — lodash's overArg ToObject
        // coercion). ECMA-262 §20.1.1.1: undefined/null/missing → fresh plain
        // object; everything else → ToObject. Objects pass through unchanged;
        // primitives are returned as-is (this runtime treats primitives as
        // their own object forms — same convention as the syntactic path).
        var notObjectTypeLabel = il.DefineLabel();
        var objectFreshLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", [_types.RuntimeTypeHandle])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brfalse, notObjectTypeLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Brfalse, objectFreshLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, objectFreshLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, objectFreshLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(objectFreshLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notObjectTypeLabel);

        // String call form (`Array.prototype.map.call(values, String)`). Bare
        // String is represented by the System.String Type token, so dynamic
        // invocation must perform §22.1.1.1 String(value), including the empty
        // string default and Symbol's descriptive-string exception.
        var notStringTypeLabel = il.DefineLabel();
        var stringNoArgLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle",
            [_types.RuntimeTypeHandle])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality",
            [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brfalse, notStringTypeLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Brfalse, stringNoArgLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(stringNoArgLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStringTypeLabel);

        // Emitted ECMAScript classes implement $IHasFields and, unlike legacy
        // function constructors, are never callable.  This path is reached by
        // indirect calls such as `Derived.apply(receiver, args)`.
        var notEmittedClassLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldtoken, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle",
            [_types.RuntimeTypeHandle])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.Type);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", [_types.Type])!);
        il.Emit(OpCodes.Brfalse, notEmittedClassLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Class constructor cannot be invoked without 'new'");
        il.MarkLabel(notEmittedClassLabel);

        // A built-in constructor Type without a call-form helper. These ARE
        // callable in JS, so the #260 throwing fallback must not fire here;
        // unwired ones keep returning null until their call forms are wired
        // in incrementally (#61).
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(tsFunctionLabel);
        // Route through InvokeWithThis(null, args) instead of Invoke(args) so the
        // helper's "__this" first-param check fires for function expressions.
        // Function expressions emit a synthetic "__this" first parameter (Stmt.Function
        // has HasOwnThis=true → ArrowFunctions.cs line 169-179). Calling Invoke
        // directly would AdjustArgs to paramCount=N+1 — the user's args[0]
        // accumulator would slide into the __this slot, shifting everything.
        // InvokeWithThis prepends the thisArg, leaving user args aligned.
        // Behavior unchanged for callables without __this naming: InvokeWithThis
        // sets thread-local _currentThis and calls Invoke unchanged.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldnull);  // thisArg = null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(boundTsFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(bindWrapperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.FunctionBindWrapperInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(callWrapperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.FunctionCallWrapperInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(applyWrapperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.FunctionApplyWrapperInvoke);
        il.Emit(OpCodes.Ret);

        if (_features.UsesTextEncoding)
        {
            il.MarkLabel(textDecoderDecodeLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSTextDecoderDecodeMethodType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.TSTextDecoderDecodeMethodInvoke);
            il.Emit(OpCodes.Ret);
        }

        if (_features.UsesNodeStreams)
        {
            il.MarkLabel(transformCbLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TransformDoneCallbackType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.TransformDoneCallbackInvoke);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(writeCallbackWrapperLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.WriteCallbackWrapperType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.WriteCallbackWrapperInvoke);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(resolveCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.PromiseResolveCallbackType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.PromiseResolveCallbackInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rejectCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.PromiseRejectCallbackType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.PromiseRejectCallbackInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(boundArrayMethodLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.BoundArrayMethodInvoke);
        il.Emit(OpCodes.Ret);

        if (_features.HasAnyTypedArray)
        {
            il.MarkLabel(boundTypedArrayMethodLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundTypedArrayMethodType);
            il.Emit(OpCodes.Ldarg_1);  // args
            il.Emit(OpCodes.Callvirt, runtime.BoundTypedArrayMethodInvoke);
            il.Emit(OpCodes.Ret);
        }

        if (_features.UsesMap)
        {
            il.MarkLabel(boundMapMethodLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.BoundMapMethodInvoke);
            il.Emit(OpCodes.Ret);
        }

        if (_features.UsesSet)
        {
            il.MarkLabel(boundSetMethodLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.BoundSetMethodInvoke);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(boundAnyFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.BoundAnyFunctionInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(funcDelegateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Ldarg_1);  // args
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FuncObjectArrayToObject, "Invoke", _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(methodCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.MethodCallableType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.MethodCallableInvoke);
        il.Emit(OpCodes.Ret);

        // Null callee: `f(x)` where f is null. Throws TypeError per ECMA-262
        // (previously a silent `return null` — #260).
        il.MarkLabel(nullLabel);
        EmitThrowNotAFunction(il, runtime, () => il.Emit(OpCodes.Ldarg_0));
    }

    private void EmitInvokeMethodValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeMethodValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.ObjectArray]  // receiver, function, args
        );
        runtime.InvokeMethodValue = method;

        var il = method.GetILGenerator();
        EmitStackGuard(il, runtime);
        // Check if value is $TSFunction and call InvokeWithThis
        // arg0 = receiver, arg1 = function, arg2 = args
        var notTSFunctionLabel = il.DefineLabel();

        // if (function == null) throw TypeError immediately — we can't fall through to
        // the later Isinst chain because it ends with a proxy check that calls GetType()
        // on the function value, which NREs on null. This was a silent `return null`
        // for years, which masked dispatch regressions like #239 — see #260.
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        EmitThrowNotAFunction(il, runtime, () => il.Emit(OpCodes.Ldarg_1));
        il.MarkLabel(notNullLabel);

        // A reflected MethodBase (a computed symbol-keyed method resolved from the symbol-method
        // registry, #647) is invoked directly: function.Invoke(receiver, args). A static method
        // ignores the receiver. This mirrors the symbol-accessor get/set paths, which invoke the
        // found getter/setter MethodInfo the same way.
        var notMethodBaseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.MethodBase);
        il.Emit(OpCodes.Brfalse, notMethodBaseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.MethodBase);
        il.Emit(OpCodes.Ldarg_0);  // receiver (this)
        il.Emit(OpCodes.Ldarg_2);  // args
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notMethodBaseLabel);

        // ECMA-262 §22.2.6: a RegExp object has no [[Call]] — calling one
        // (`/x/()`, `RegExp("a","g")()`) must throw TypeError. Checked early
        // so it doesn't fall into the dispatch chain.
        if (runtime.TSRegExpType != null)
        {
            var notRegExpCallee = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpCallee);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "called value is not a function");
            il.MarkLabel(notRegExpCallee);
        }

        // if (function is $TSFunction tsFunc)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // return tsFunc.InvokeWithThis(receiver, args)
        il.Emit(OpCodes.Ldarg_0);  // receiver
        il.Emit(OpCodes.Ldarg_2);  // args
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        // Not a TSFunction - handle known callable wrappers without calling InvokeValue
        il.MarkLabel(notTSFunctionLabel);
        il.Emit(OpCodes.Pop);  // Pop the null from isinst
        var notBoundLabel = il.DefineLabel();
        var notBindWrapperLabel = il.DefineLabel();
        var notCallWrapperLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBoundLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBoundLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Brfalse, notBindWrapperLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.FunctionBindWrapperInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBindWrapperLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Brfalse, notCallWrapperLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.FunctionCallWrapperInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notCallWrapperLabel);
        var notApplyWrapperLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Brfalse, notApplyWrapperLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.FunctionApplyWrapperInvoke);
        il.Emit(OpCodes.Ret);

        // Tree-shakable wrapper-type dispatch chain. We thread `currentReject`
        // through each block: each enabled block defines a new "not me" label,
        // marks the current reject as its entry, runs Isinst+Brfalse+body+Ret,
        // and updates currentReject. Gated blocks just leave currentReject
        // unchanged so the previous block's Brfalse target points at the next
        // enabled block's entry.
        var currentReject = notApplyWrapperLabel;

        void EmitWrapperCheck(Type? checkType, MethodInfo? invokeMethod)
        {
            // (Helper used below; see local function definitions further down.)
            var nextReject = il.DefineLabel();
            il.MarkLabel(currentReject);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, checkType!);
            il.Emit(OpCodes.Brfalse, nextReject);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, checkType!);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, invokeMethod!);
            il.Emit(OpCodes.Ret);
            currentReject = nextReject;
        }

        if (_features.UsesTextEncoding)
            EmitWrapperCheck(runtime.TSTextDecoderDecodeMethodType, runtime.TSTextDecoderDecodeMethodInvoke);
        // Stream callback wrappers — gated on UsesNodeStreams.
        if (_features.UsesNodeStreams)
        {
            EmitWrapperCheck(runtime.TransformDoneCallbackType, runtime.TransformDoneCallbackInvoke);
            EmitWrapperCheck(runtime.WriteCallbackWrapperType, runtime.WriteCallbackWrapperInvoke);
        }
        // Promise callbacks — always emitted (Promise infrastructure is core).
        EmitWrapperCheck(runtime.PromiseResolveCallbackType, runtime.PromiseResolveCallbackInvoke);
        EmitWrapperCheck(runtime.PromiseRejectCallbackType, runtime.PromiseRejectCallbackInvoke);

        // After the chain, mark the final reject label so the fall-through code
        // below has somewhere to land.
        il.MarkLabel(currentReject);

        // Check $BoundArrayMethod. ECMA-262 array prototype methods are generic —
        // when reached via prototype-chain walk on a non-list receiver
        // (`f.reduce(...)` where `f.__proto__ === [1,2,3]`), the bound `_list` is
        // the prototype's array, but the spec requires `this = f`. Without the
        // override below we'd silently iterate the prototype's elements and ignore
        // user-set properties on `f` (e.g., `f.length = 0`).
        var notBoundArrayMethodLabel = il.DefineLabel();
        var useOriginalBamLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brfalse, notBoundArrayMethodLabel);

        var bamLocal = il.DeclareLocal(runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Stloc, bamLocal);

        // If receiver is null/undefined → use bamLocal as-is (legacy behavior).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, useOriginalBamLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, useOriginalBamLabel);

        // If receiver === bamLocal._list → use bamLocal as-is.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bamLocal);
        il.Emit(OpCodes.Ldfld, runtime.BoundArrayMethodListField);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, useOriginalBamLabel);

        // Receiver differs from the bound list — materialize and rebuild.
        // Stash the original receiver in `_currentArrayLikeReceiver` so the
        // callback's array slot (per ECMA-262) sees `f`, not the materialized
        // copy. Mirrors the Array.prototype.X.call(receiver, ...) pattern path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, runtime.CurrentArrayLikeReceiverField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ArrayLikeMaterialize);
        il.Emit(OpCodes.Ldloc, bamLocal);
        il.Emit(OpCodes.Ldfld, runtime.BoundArrayMethodNameField);
        il.Emit(OpCodes.Newobj, runtime.BoundArrayMethodCtor);
        il.Emit(OpCodes.Stloc, bamLocal);

        il.MarkLabel(useOriginalBamLabel);
        il.Emit(OpCodes.Ldloc, bamLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.BoundArrayMethodInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBoundArrayMethodLabel);

        // Check $BoundMapMethod — gated on UsesMap (the wrapper type only
        // exists when EmitMapMethods runs).
        var notBoundMapMethodLabel = il.DefineLabel();
        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Brfalse, notBoundMapMethodLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.BoundMapMethodInvoke);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notBoundMapMethodLabel);
        }

        // Check $BoundSetMethod — gated on UsesSet.
        var notBoundSetMethodLabel = il.DefineLabel();
        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Brfalse, notBoundSetMethodLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.BoundSetMethodInvoke);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notBoundSetMethodLabel);
        }

        // Check $BoundAnyFunction (partial-apply wrapper produced by .bind on non-$TSFunction targets)
        var notBoundAnyFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brfalse, notBoundAnyFunctionLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.BoundAnyFunctionInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBoundAnyFunctionLabel);

        // $BoundTypedArrayMethod (#940) — wraps a typed-array bulk method bound to its receiver.
        // The receiver is already captured in the wrapper, so arg0 is ignored (like $MethodCallable).
        if (_features.HasAnyTypedArray)
        {
            var notBoundTypedArrayMethodLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.BoundTypedArrayMethodType);
            il.Emit(OpCodes.Brfalse, notBoundTypedArrayMethodLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.BoundTypedArrayMethodType);
            il.Emit(OpCodes.Ldarg_2);  // args
            il.Emit(OpCodes.Callvirt, runtime.BoundTypedArrayMethodInvoke);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBoundTypedArrayMethodLabel);
        }

        // Handle $MethodCallable (wraps BuiltInMethod from GetMember)
        var notMethodCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.MethodCallableType);
        il.Emit(OpCodes.Brfalse, notMethodCallableLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.MethodCallableType);
        il.Emit(OpCodes.Ldarg_2);  // args
        il.Emit(OpCodes.Callvirt, runtime.MethodCallableInvoke);
        il.Emit(OpCodes.Ret);

        // Handle Func<object?[], object?> (from CreateBoundMethod in RuntimeTypes.Methods)
        il.MarkLabel(notMethodCallableLabel);
        var notFuncLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Brfalse, notFuncLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.FuncObjectArrayToObject);
        il.Emit(OpCodes.Ldarg_2);  // args
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FuncObjectArrayToObject, "Invoke", _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        // Built-in type constructor stored as a value (issue #61): patterns like
        // `const A = Array; A(n)` end up here because `A` is a plain variable
        // holding the Type token. Delegate to InvokeValue which has the Type
        // dispatch — receiver is irrelevant for a constructor-style call.
        il.MarkLabel(notFuncLabel);
        var notTypeCalleeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeCalleeLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTypeCalleeLabel);

        // Proxy apply trap check: if function is a proxy, call TrapApply
        // TrapApply expects List<object?> so convert the object[] args
        var notProxyLabel2 = il.DefineLabel();
        EmitProxyInvokeCheck(il, () => il.Emit(OpCodes.Ldarg_1), () =>
        {
            il.Emit(OpCodes.Ldarg_2); // object[] args
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, [typeof(IEnumerable<object>)])!);
        }, notProxyLabel2);

        il.MarkLabel(notProxyLabel2);

        // Fallback: reflection-based dispatch for SharpTS runtime callables (e.g., BuiltInMethod from vm.compileFunction).
        // Check if function has a "Call" method matching (Interpreter, List<object?>) signature.
        var noCallMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noCallMethodLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Call");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        var callMiLocal = il.DeclareLocal(typeof(MethodInfo));
        il.Emit(OpCodes.Stloc, callMiLocal);
        il.Emit(OpCodes.Ldloc, callMiLocal);
        il.Emit(OpCodes.Brfalse, noCallMethodLabel);

        // Call(interpreter=null, args=new List<object?>(object[]))
        // Wrapped in try/catch so a guest error thrown by the BuiltInMethod (which
        // reflection re-wraps in TargetInvocationException) is unwrapped to the real
        // guest exception — without this, a throwing cross-boundary BuiltInMethod (e.g.
        // vm.Module.link/evaluate failing) escapes guest try/catch as a raw
        // TargetInvocationException and crashes the program.
        var reflectResultLocal = il.DeclareLocal(_types.Object);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, callMiLocal);
        il.Emit(OpCodes.Ldarg_1); // the callable object
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull); // interpreter = null
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        // Convert object[] to List<object?>
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, _types.ListObjectNullableCtor_IEnumerable);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, reflectResultLocal);
        il.BeginCatchBlock(typeof(TargetInvocationException));
        // ex on stack → rethrow ex.InnerException (the real guest error) if present.
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("InnerException")!.GetGetMethod()!);
        var innerLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, innerLocal);
        var rethrowOriginal = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Brfalse, rethrowOriginal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(rethrowOriginal);
        il.Emit(OpCodes.Rethrow);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, reflectResultLocal);
        il.Emit(OpCodes.Ret);

        // Unrecognized non-callable (plain object, number, $Undefined, …):
        // throw TypeError per ECMA-262 instead of silently returning null (#260).
        il.MarkLabel(noCallMethodLabel);
        EmitThrowNotAFunction(il, runtime, () => il.Emit(OpCodes.Ldarg_1));
    }

    /// <summary>
    /// Emits <c>object IteratorProtocolCall(object recv, string name, object[] args)</c>.
    ///
    /// Bridges the JS iterator protocol (<c>.next()</c> / <c>.return()</c>) for
    /// <em>any-typed</em> receivers that are a bare <see cref="IEnumerator{T}"/>
    /// of object — which is exactly what array <c>.values()</c> / <c>.keys()</c> /
    /// <c>.entries()</c> return (via <c>NormalizeToEnumerator</c>). A BCL
    /// enumerator has no JS-shaped <c>next</c>/<c>value</c>/<c>done</c> members,
    /// so the generic <c>GetProperty + InvokeMethodValue</c> fallback resolves
    /// <c>next</c> to <c>undefined</c> and the call yields <c>null</c>. The typed
    /// call path (handled by the iterator emitter) is unaffected; this only kicks
    /// in when the receiver's type is unknown at compile time (e.g. Test262 <c>.js</c>
    /// sources run without type-checking, so <c>var it = arr.values()</c> is
    /// <c>any</c>).
    ///
    /// Non-enumerator receivers fall through to the normal dynamic dispatch,
    /// preserving existing behavior for user objects that carry their own
    /// <c>next</c>/<c>return</c> method (generators, custom iterators).
    /// </summary>
    private void EmitIteratorProtocolCall(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IteratorProtocolCall",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String, _types.ObjectArray]);
        runtime.IteratorProtocolCall = method;

        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var fnLocal = il.DeclareLocal(_types.Object);

        var notEnumeratorLabel = il.DefineLabel();
        var returnBranchLabel = il.DefineLabel();

        // SharpTS generators implement $IGenerator. For an explicit next(v), call
        // $IGenerator.next(value) directly so the sent value reaches the suspended
        // yield with the correct default — a bare next() must resume with undefined,
        // not the null that GetProperty+InvokeMethodValue would pad in for a missing
        // argument (#452). User iterators carrying their own next() keep the
        // GetProperty path below (they don't implement $IGenerator).
        if (runtime.GeneratorInterfaceType != null)
        {
            var notGeneratorNextLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "next");
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, notGeneratorNextLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
            il.Emit(OpCodes.Brfalse, notGeneratorNextLabel);
            // ((​$IGenerator)recv).next(args.Length > 0 ? args[0] : undefined)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
            EmitArgZeroOrUndefined(il, runtime);
            il.Emit(OpCodes.Callvirt, runtime.GeneratorNextMethod);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notGeneratorNextLabel);

            // return(v): forward to $IGenerator.return with the same undefined-default for a missing
            // argument as next — a bare return() must inject `undefined`, not the null a missing
            // reflected argument would pad (#526). User iterators with their own return() keep the
            // GetProperty path below (they do not implement $IGenerator).
            var notGeneratorReturnLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "return");
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, notGeneratorReturnLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
            il.Emit(OpCodes.Brfalse, notGeneratorReturnLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
            EmitArgZeroOrUndefined(il, runtime);
            il.Emit(OpCodes.Callvirt, runtime.GeneratorReturnMethod);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notGeneratorReturnLabel);

            // throw(e): forward to $IGenerator.throw with the same undefined-default for a missing
            // argument as next/return — a bare throw() must inject `undefined`, not the null a missing
            // reflected argument would pad (#619, counterpart of the bare-return() fix in #526). User
            // iterators with their own throw() keep the GetProperty path below (no $IGenerator).
            var notGeneratorThrowLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "throw");
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, notGeneratorThrowLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
            il.Emit(OpCodes.Brfalse, notGeneratorThrowLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
            EmitArgZeroOrUndefined(il, runtime);
            il.Emit(OpCodes.Callvirt, runtime.GeneratorThrowMethod);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notGeneratorThrowLabel);
        }

        // Resolve the JS-level member first. Generators and user-defined
        // iterators expose a real `next`/`return` callable here; only when the
        // receiver has NO such member (a bare BCL enumerator) do we synthesize
        // the result object below. This ordering guarantees generators keep
        // their own next(value)/return(value) semantics — the enumerator branch
        // is a pure rescue for values()/keys()/entries().
        var fn = fnLocal;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, fn);
        // if (fn == null) goto notEnumerator-or-synth
        il.Emit(OpCodes.Ldloc, fn);
        il.Emit(OpCodes.Brfalse, notEnumeratorLabel);
        // if (fn == $Undefined.Instance) goto notEnumerator-or-synth
        il.Emit(OpCodes.Ldloc, fn);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, notEnumeratorLabel);
        // Real JS method present → normal dispatch.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fn);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        // No JS-level next/return. var en = recv as IEnumerator<object>;
        // if (en == null) fall back to normal (null-yielding) dispatch.
        il.MarkLabel(notEnumeratorLabel);
        var synthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumeratorOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, enumLocal);
        il.Emit(OpCodes.Brtrue, synthLabel);
        // Not an enumerator and no JS method → InvokeMethodValue(recv, fn, args).
        // fn is null/undefined here, so this throws TypeError ("undefined is not
        // a function") — matching JS for an explicit `it.next()` / `it.return()`
        // call on a receiver without that member (#260; previously a silent null).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fn);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(synthLabel);

        // throw() on a bare BCL enumerator (no throw member) → TypeError, as in JS — `fn` is the
        // null/undefined member here, so InvokeMethodValue throws "undefined is not a function". Without
        // this guard the next()-synth below would wrongly advance the iterator. Only reachable for an
        // any-typed array iterator; real generators take the $IGenerator throw branch above (#619).
        var notThrowSynthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "throw");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, notThrowSynthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fn);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notThrowSynthLabel);

        // result = new Dictionary<string, object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (name == "return") goto returnBranch
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, returnBranchLabel);

        // --- next(): { value: en.Current, done: false } | { value: undefined, done: true } ---
        var nextDoneLabel = il.DefineLabel();
        var nextEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, nextDoneLabel);

        // result["value"] = en.Current
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Callvirt, setItem);
        // result["done"] = false
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Br, nextEndLabel);

        il.MarkLabel(nextDoneLabel);
        // result["value"] = undefined
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, setItem);
        // result["done"] = true
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);

        il.MarkLabel(nextEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // --- return(value): close the iterator, yield { value: value, done: true } ---
        il.MarkLabel(returnBranchLabel);
        // if (en is IDisposable d) d.Dispose();
        var disposableLocal = il.DeclareLocal(_types.IDisposable);
        var notDisposableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Isinst, _types.IDisposable);
        il.Emit(OpCodes.Stloc, disposableLocal);
        il.Emit(OpCodes.Ldloc, disposableLocal);
        il.Emit(OpCodes.Brfalse, notDisposableLabel);
        il.Emit(OpCodes.Ldloc, disposableLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.MarkLabel(notDisposableLabel);

        // result["value"] = (args != null && args.Length > 0) ? args[0] : undefined
        var useUndefLabel = il.DefineLabel();
        var haveValueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, useUndefLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, useUndefLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Br, haveValueLabel);
        il.MarkLabel(useUndefLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(haveValueLabel);
        il.Emit(OpCodes.Callvirt, setItem);
        // result["done"] = true
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Pushes <c>args.Length &gt; 0 ? args[0] : $Undefined</c> for the single-argument generator
    /// protocol methods, so a bare <c>next()</c>/<c>return()</c> injects <c>undefined</c> rather than
    /// the null a missing reflected argument would pad (#452/#526). Expects the args array in arg2.
    /// </summary>
    private void EmitArgZeroOrUndefined(ILGenerator il, EmittedRuntime runtime)
    {
        var hasArg = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, hasArg);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(hasArg);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.MarkLabel(done);
    }

    private void EmitGetSuperMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetSuperMethod(object instance, string methodName) -> object
        // Finds a method on the parent class using .NET type hierarchy (BaseType)
        // and wraps it in a $TSFunction for invocation.
        var method = typeBuilder.DefineMethod(
            "GetSuperMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetSuperMethod = method;

        var il = method.GetILGenerator();
        var baseTypeLocal = il.DeclareLocal(_types.Type);
        var methodInfoLocal = il.DeclareLocal(typeof(MethodInfo));
        var nullLabel = il.DefineLabel();
        var loopLabel = il.DefineLabel();
        var foundLabel = il.DefineLabel();

        // Check if instance is null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Get instance.GetType().BaseType
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, baseTypeLocal);

        // Walk up the type hierarchy looking for the method
        il.MarkLabel(loopLabel);

        // If baseType is null or System.Object, return null
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, nullLabel);

        // Try to get method: baseType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Ldarg_1); // methodName
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, methodInfoLocal);

        // If method found, wrap in $TSFunction and return
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Brtrue, foundLabel);

        // Method not found at this level - try parent
        il.Emit(OpCodes.Ldloc, baseTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, baseTypeLocal);
        il.Emit(OpCodes.Br, loopLabel);

        // Found the method - wrap in $TSFunction(instance, methodInfo)
        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldarg_0); // instance (target for the bound method)
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}

