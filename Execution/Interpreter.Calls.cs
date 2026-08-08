using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpTS.Execution;

// Note: This file uses InterpreterException for runtime errors

public partial class Interpreter
{
    private static ISharpTSCallable BindBuiltInStaticCallReceiver(
        string namespaceName, ISharpTSCallable method)
        => namespaceName == BuiltInNames.Promise && method is BuiltInAsyncMethod promiseMethod
            ? promiseMethod.Bind(PromiseGlobalValue)
            : method;

    /// <summary>
    /// Calls a built-in method with pooled argument list to reduce allocations.
    /// Uses V2 (RuntimeValue) fast path when the method supports it, avoiding
    /// the legacy wrapper overhead.
    /// </summary>
    private async ValueTask<object?> CallBuiltInWithPooledArgs(
        IEvaluationContext ctx,
        ISharpTSCallable method,
        IReadOnlyList<Expr> arguments)
    {
        // Spread args have a runtime-unknown count; expand them into a flat list
        // before dispatch so they don't leak through as a single Object-kind arg
        // (matches the sync CallBuiltInSync path, #951). A span can't cross an
        // await, so all arguments are evaluated into the list first.
        if (HasSpreadArgs(arguments))
        {
            if (method is BuiltInMethod bmSpread && bmSpread.HasV2Implementation)
            {
                var expanded = new List<RuntimeValue>(arguments.Count);
                foreach (var arg in arguments)
                {
                    if (arg is Expr.Spread spread)
                    {
                        var spreadValue = (await ctx.EvaluateExprAsync(spread.Expression)).ToObject();
                        foreach (var el in GetIterableElements(spreadValue))
                            expanded.Add(RuntimeValue.FromBoxed(el));
                    }
                    else
                    {
                        expanded.Add(await ctx.EvaluateExprAsync(arg));
                    }
                }
                return bmSpread.CallV2(this, CollectionsMarshal.AsSpan(expanded)).ToObject();
            }

            var spreadList = ArgumentListPool.Rent();
            try
            {
                foreach (var arg in arguments)
                {
                    if (arg is Expr.Spread spread)
                    {
                        var spreadValue = (await ctx.EvaluateExprAsync(spread.Expression)).ToObject();
                        spreadList.AddRange(GetIterableElements(spreadValue));
                    }
                    else
                    {
                        spreadList.Add((await ctx.EvaluateExprAsync(arg)).ToObject());
                    }
                }
                return method.Call(this, spreadList);
            }
            finally
            {
                ArgumentListPool.Return(spreadList);
            }
        }

        // V2 fast path: use RuntimeValue span instead of List<object?>
        if (method is BuiltInMethod bm && bm.HasV2Implementation)
        {
            var argCount = arguments.Count;
            var rented = ArrayPool<RuntimeValue>.Shared.Rent(Math.Max(argCount, 1));
            try
            {
                for (int i = 0; i < argCount; i++)
                {
                    rented[i] = await ctx.EvaluateExprAsync(arguments[i]);
                }
                return bm.CallV2(this, rented.AsSpan(0, argCount)).ToObject();
            }
            finally
            {
                ArrayPool<RuntimeValue>.Shared.Return(rented);
            }
        }

        // Legacy path
        var pooledList = ArgumentListPool.Rent();
        try
        {
            foreach (var arg in arguments)
            {
                pooledList.Add((await ctx.EvaluateExprAsync(arg)).ToObject());
            }
            return method.Call(this, pooledList);
        }
        finally
        {
            ArgumentListPool.Return(pooledList);
        }
    }

    /// <summary>
    /// Core implementation for evaluating function/method calls, shared between sync and async paths.
    /// Handles all special cases: console.log, built-in methods, Symbol, BigInt, Date, Error, timers, etc.
    /// </summary>
    /// <param name="ctx">The evaluation context for evaluating arguments.</param>
    /// <param name="call">The call expression AST node.</param>
    /// <returns>A ValueTask containing the return value of the called function.</returns>
    private async ValueTask<object?> EvaluateCallCore(IEvaluationContext ctx, Expr.Call call)
    {
        // Handle console.* methods
        if (call.Callee is Expr.Variable v && v.Name.Lexeme.StartsWith(BuiltInNames.ConsolePrefix, StringComparison.Ordinal))
        {
            var methodName = v.Name.Lexeme[(BuiltInNames.Console.Length + 1)..];
            var method = BuiltInRegistry.Instance.GetStaticMethod(BuiltInNames.Console, methodName);
            if (method != null)
            {
                return await CallBuiltInWithPooledArgs(ctx, method, call.Arguments);
            }
        }

        // Handle globalThis.console.* calls
        if (call.Callee is Expr.Get chainedGet &&
            chainedGet.Object is Expr.Get innerGet &&
            innerGet.Object is Expr.Variable globalThisVar &&
            globalThisVar.Name.Lexeme == BuiltInNames.GlobalThis &&
            innerGet.Name.Lexeme == BuiltInNames.Console)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(BuiltInNames.Console, chainedGet.Name.Lexeme);
            if (method != null)
            {
                return await CallBuiltInWithPooledArgs(ctx, method, call.Arguments);
            }
        }

        // Handle globalThis.<namespace>.<method>() calls (e.g., globalThis.Math.floor())
        if (call.Callee is Expr.Get gtChainedGet &&
            gtChainedGet.Object is Expr.Get gtInnerGet &&
            gtInnerGet.Object is Expr.Variable gtVar &&
            gtVar.Name.Lexeme == BuiltInNames.GlobalThis)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(gtInnerGet.Name.Lexeme, gtChainedGet.Name.Lexeme);
            if (method != null)
            {
                return await CallBuiltInWithPooledArgs(ctx, method, call.Arguments);
            }
        }

        // Handle built-in static methods: Object.keys(), Array.isArray(), JSON.parse(), etc.
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable nsVar)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (method != null)
            {
                method = BindBuiltInStaticCallReceiver(nsVar.Name.Lexeme, method);
                return await CallBuiltInWithPooledArgs(ctx, method, call.Arguments);
            }
        }

        // Handle global functions via registry (Symbol, BigInt, parseInt, setTimeout, Error types, etc.)
        // Skip if the variable is shadowed by a module import (e.g., stdlib 'timers' / 'timers/promises'
        // re-exports setTimeout as a SharpTSFunction, which implements ISharpTSCallable).
        if (call.Callee is Expr.Variable globalVar)
        {
            var gvName = globalVar.Name.Lexeme;
            bool isShadowed = _environment.TryGet(gvName, out var shadowVal) &&
                shadowVal.ToObject() is ISharpTSCallable or ISharpTSAsyncCallable or Runtime.BuiltIns.BuiltInMethod;
            if (!isShadowed)
            {
                if (GlobalFunctionRegistry.Instance.TryGetHandlerV2(gvName, out var handlerV2))
                {
                    Func<Expr, ValueTask<RuntimeValue>> evalWrapper = expr => ctx.EvaluateExprAsync(expr);
                    return await handlerV2!(evalWrapper, call.Arguments, this);
                }
            }
        }

        object? callee;
        if (call.Callee is Expr.Get extensionGet &&
            _currentModule is { DotNetExtensionTypes.Count: > 0 })
        {
            object? receiver = (await ctx.EvaluateExprAsync(extensionGet.Object)).ToObject();
            callee = TryBindDotNetExtension(receiver, extensionGet.Name.Lexeme, out var extension)
                ? extension
                : EvaluateGetOnObject(extensionGet, receiver).ToObject();
        }
        else if (call.Callee is Expr.Get memberGet)
        {
            object? receiver = (await ctx.EvaluateExprAsync(memberGet.Object)).ToObject();
            callee = EvaluateGetOnObject(memberGet, receiver).ToObject();
            // Descriptor-defined callable data properties are returned raw so
            // reference identity holds. Bind their dynamic receiver only for
            // an actual member call (`obj.method()`), where JavaScript creates
            // the Reference Record that supplies `this`.
            callee = BindMemberCallReceiver(callee, receiver);
        }
        else if (call.Callee is Expr.GetIndex memberIndex)
        {
            object? receiver = (await ctx.EvaluateExprAsync(memberIndex.Object)).ToObject();
            if (memberIndex.Optional && receiver is (null or SharpTSUndefined))
            {
                callee = SharpTSUndefined.Instance;
            }
            else
            {
                object? key = (await ctx.EvaluateExprAsync(memberIndex.Index)).ToObject();
                callee = PerformIndexGet(memberIndex.Object, receiver, key).ToObject();
                callee = BindMemberCallReceiver(callee, receiver);
            }
        }
        else
        {
            callee = (await ctx.EvaluateExprAsync(call.Callee)).ToObject();
        }

        // Optional call: return undefined if callee is nullish.
        // Per ECMA-262 §13.3.9, the short-circuit extends to the entire chain.
        if ((call.Optional || HasOptionalInChain(call.Callee))
            && (callee == null || callee is Runtime.Types.SharpTSUndefined))
        {
            return SharpTSUndefined.Instance;
        }

        var argumentsList = ArgumentListPool.Rent();
        try
        {
            foreach (Expr argument in call.Arguments)
            {
                if (argument is Expr.Spread spread)
                {
                    object? spreadValue = (await ctx.EvaluateExprAsync(spread.Expression)).ToObject();
                    // Use GetIterableElements to support custom iterables with Symbol.iterator
                    argumentsList.AddRange(GetIterableElements(spreadValue));
                }
                else
                {
                    argumentsList.Add((await ctx.EvaluateExprAsync(argument)).ToObject());
                }
            }

            if (callee is not ISharpTSCallable function)
            {
                if ((call.Optional || HasOptionalInChain(call.Callee))
                    && (callee == null || callee is Runtime.Types.SharpTSUndefined))
                {
                    return Runtime.Types.SharpTSUndefined.Instance;
                }
                // ECMA-262: invoking a non-callable surfaces as TypeError.
                // Wrap so guest `try/catch` sees `e.constructor === TypeError`.
                throw new ThrowException(new SharpTSTypeError(
                    $"{GetTypeofString(callee)} is not a function"));
            }

            // Per ECMA-262 §10.2.1: missing arguments become undefined; do not
            // reject calls for user-defined functions. Built-in methods enforce
            // their own min-arity in BuiltInMethod.Call.
            if (function is DotNetMethod dotNetMethod)
            {
                IReadOnlyList<Type>? explicitTypes = call.TypeArgs is { Count: > 0 }
                    ? ResolveDotNetGenericTypeArguments(call.TypeArgs)
                    : null;
                return dotNetMethod.CallWithTypeHints(
                    this,
                    argumentsList,
                    ResolveDotNetArgumentTypeHints(call.Arguments),
                    explicitTypes,
                    ResolveDotNetResultTypeHint(call));
            }
            return function.Call(this, argumentsList);
        }
        finally
        {
            ArgumentListPool.Return(argumentsList);
        }
    }

    /// <summary>
    /// Walks the expression to determine whether any <c>?.</c> in its postfix
    /// chain could have short-circuited. Per ECMA-262 §13.3.9 OptionalExpression,
    /// the entire chain short-circuits if any optional step returns null/undefined.
    /// </summary>
    private static bool HasOptionalInChain(Expr expr)
    {
        while (true)
        {
            switch (expr)
            {
                case Expr.Get g:
                    if (g.Optional) return true;
                    expr = g.Object;
                    continue;
                case Expr.GetIndex gi:
                    if (gi.Optional) return true;
                    expr = gi.Object;
                    continue;
                case Expr.Call c:
                    if (c.Optional) return true;
                    expr = c.Callee;
                    continue;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Calls a built-in method with sync evaluation.
    /// Uses V2 (RuntimeValue) fast path when the method supports it.
    /// </summary>
    private RuntimeValue CallBuiltInSync(ISharpTSCallable method, IReadOnlyList<Expr> arguments)
    {
        // Spread args (\`Math.max(...arr)\`) have a runtime-unknown count, so the
        // ArrayPool-by-arity fast path can't hold them. Expand into a flat list
        // (the same GetIterableElements expansion the user-function boxed path
        // uses) before dispatching — otherwise the spread array leaks through as
        // a single Object-kind arg and Math.max's AsNumber() throws (#951).
        if (HasSpreadArgs(arguments))
        {
            if (method is BuiltInMethod bmSpread && bmSpread.HasV2Implementation)
            {
                var expanded = new List<RuntimeValue>(arguments.Count);
                foreach (var arg in arguments)
                {
                    if (arg is Expr.Spread spread)
                    {
                        foreach (var el in GetIterableElements(Evaluate(spread.Expression)))
                            expanded.Add(RuntimeValue.FromBoxed(el));
                    }
                    else
                    {
                        expanded.Add(EvaluateRV(arg));
                    }
                }
                return bmSpread.CallV2(this, CollectionsMarshal.AsSpan(expanded));
            }

            var spreadList = ArgumentListPool.Rent();
            try
            {
                foreach (var arg in arguments)
                {
                    if (arg is Expr.Spread spread)
                        spreadList.AddRange(GetIterableElements(Evaluate(spread.Expression)));
                    else
                        spreadList.Add(Evaluate(arg));
                }
                return RuntimeValue.FromBoxed(method.Call(this, spreadList));
            }
            finally
            {
                ArgumentListPool.Return(spreadList);
            }
        }

        // V2 fast path — no boxing at all
        if (method is BuiltInMethod bm && bm.HasV2Implementation)
        {
            var argCount = arguments.Count;
            var rented = ArrayPool<RuntimeValue>.Shared.Rent(Math.Max(argCount, 1));
            try
            {
                for (int i = 0; i < argCount; i++)
                {
                    rented[i] = EvaluateRV(arguments[i]);
                }
                return bm.CallV2(this, rented.AsSpan(0, argCount));
            }
            finally
            {
                ArrayPool<RuntimeValue>.Shared.Return(rented);
            }
        }

        // Legacy path
        var pooledList = ArgumentListPool.Rent();
        try
        {
            foreach (var arg in arguments)
            {
                pooledList.Add(Evaluate(arg));
            }
            return RuntimeValue.FromBoxed(method.Call(this, pooledList));
        }
        finally
        {
            ArgumentListPool.Return(pooledList);
        }
    }

    /// <summary>
    /// Evaluates a function or method call expression.
    /// </summary>
    /// <param name="call">The call expression AST node.</param>
    /// <returns>The return value of the called function.</returns>
    /// <remarks>
    /// Handles special cases for <c>console.log</c>, <c>Object.*</c> static methods,
    /// and the internal <c>__objectRest</c> helper. Supports spread arguments.
    /// Validates arity before invoking the callable.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/functions.html">TypeScript Functions</seealso>
    private RuntimeValue EvaluateCall(Expr.Call call)
    {
        // Handle console.* methods
        if (call.Callee is Expr.Variable v && v.Name.Lexeme.StartsWith(BuiltInNames.ConsolePrefix, StringComparison.Ordinal))
        {
            var methodName = v.Name.Lexeme[(BuiltInNames.Console.Length + 1)..];
            var method = BuiltInRegistry.Instance.GetStaticMethod(BuiltInNames.Console, methodName);
            if (method != null)
            {
                return CallBuiltInSync(method, call.Arguments);
            }
        }

        // Handle globalThis.console.* calls
        if (call.Callee is Expr.Get chainedGet &&
            chainedGet.Object is Expr.Get innerGet &&
            innerGet.Object is Expr.Variable globalThisVar &&
            globalThisVar.Name.Lexeme == BuiltInNames.GlobalThis &&
            innerGet.Name.Lexeme == BuiltInNames.Console)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(BuiltInNames.Console, chainedGet.Name.Lexeme);
            if (method != null)
            {
                return CallBuiltInSync(method, call.Arguments);
            }
        }

        // Handle globalThis.<namespace>.<method>() calls (e.g., globalThis.Math.floor())
        if (call.Callee is Expr.Get gtChainedGet &&
            gtChainedGet.Object is Expr.Get gtInnerGet &&
            gtInnerGet.Object is Expr.Variable gtVar &&
            gtVar.Name.Lexeme == BuiltInNames.GlobalThis)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(gtInnerGet.Name.Lexeme, gtChainedGet.Name.Lexeme);
            if (method != null)
            {
                return CallBuiltInSync(method, call.Arguments);
            }
        }

        // Handle built-in static methods: Object.keys(), Array.isArray(), JSON.parse(), etc.
        if (call.Callee is Expr.Get get &&
            get.Object is Expr.Variable nsVar)
        {
            var method = BuiltInRegistry.Instance.GetStaticMethod(nsVar.Name.Lexeme, get.Name.Lexeme);
            if (method != null)
            {
                method = BindBuiltInStaticCallReceiver(nsVar.Name.Lexeme, method);
                return CallBuiltInSync(method, call.Arguments);
            }
        }

        // Handle global functions via registry (Symbol, BigInt, parseInt, setTimeout, Error types, etc.)
        // Skip if the variable is shadowed by a module import (e.g., stdlib 'timers' / 'timers/promises'
        // re-exports setTimeout as a SharpTSFunction, which implements ISharpTSCallable).
        if (call.Callee is Expr.Variable globalVar)
        {
            var gvName = globalVar.Name.Lexeme;
            bool isShadowed = _environment.TryGet(gvName, out var shadowVal) &&
                shadowVal.ToObject() is ISharpTSCallable or ISharpTSAsyncCallable or Runtime.BuiltIns.BuiltInMethod;
            if (!isShadowed)
            {
                if (GlobalFunctionRegistry.Instance.TryGetHandlerV2(gvName, out var handlerV2))
                {
                    _syncEvalWrapperV2Cached ??= expr => _syncContext.EvaluateExprAsync(expr);
                    return handlerV2!(_syncEvalWrapperV2Cached, call.Arguments, this).GetAwaiter().GetResult();
                }
            }
        }

        object? callee;
        if (call.Callee is Expr.Get extensionGet &&
            _currentModule is { DotNetExtensionTypes.Count: > 0 })
        {
            object? receiver = Evaluate(extensionGet.Object);
            callee = TryBindDotNetExtension(receiver, extensionGet.Name.Lexeme, out var extension)
                ? extension
                : EvaluateGetOnObject(extensionGet, receiver).ToObject();
        }
        else if (call.Callee is Expr.Get memberGet)
        {
            object? receiver = Evaluate(memberGet.Object);
            callee = EvaluateGetOnObject(memberGet, receiver).ToObject();
            callee = BindMemberCallReceiver(callee, receiver);
        }
        else if (call.Callee is Expr.GetIndex memberIndex)
        {
            object? receiver = Evaluate(memberIndex.Object);
            if (memberIndex.Optional && receiver is (null or SharpTSUndefined))
            {
                callee = SharpTSUndefined.Instance;
            }
            else
            {
                object? key = Evaluate(memberIndex.Index);
                callee = PerformIndexGet(memberIndex.Object, receiver, key).ToObject();
                callee = BindMemberCallReceiver(callee, receiver);
            }
        }
        else
        {
            callee = Evaluate(call.Callee);
        }

        // Optional call: return undefined if callee is nullish.
        // Per ECMA-262 §13.3.9, the short-circuit extends to the entire chain:
        // if any `?.` earlier in the callee expression returned null/undefined,
        // this call must also short-circuit.
        if ((call.Optional || HasOptionalInChain(call.Callee))
            && (callee == null || callee is Runtime.Types.SharpTSUndefined))
        {
            return RuntimeValue.Undefined;
        }

        // V2 fast path: no spread args — zero boxing (CallV2 is on the interface;
        // unmigrated implementors run through the boxing DIM bridge)
        if (callee is ISharpTSCallable v2Callee &&
            callee is not DotNetMethod &&
            !HasSpreadArgs(call.Arguments))
        {
            int argCount = call.Arguments.Count;
            var rented = ArrayPool<RuntimeValue>.Shared.Rent(Math.Max(argCount, 1));
            try
            {
                for (int i = 0; i < argCount; i++)
                {
                    rented[i] = EvaluateRV(call.Arguments[i]);
                }
                return v2Callee.CallV2(this, rented.AsSpan(0, argCount));
            }
            finally
            {
                ArrayPool<RuntimeValue>.Shared.Return(rented);
            }
        }

        // Boxed path: spread args (element lists come from object? iterables) or
        // non-callable callee (TypeError reporting below)
        var argumentsList = ArgumentListPool.Rent();
        try
        {
            foreach (Expr argument in call.Arguments)
            {
                if (argument is Expr.Spread spread)
                {
                    object? spreadValue = Evaluate(spread.Expression);
                    // Use GetIterableElements to support custom iterables with Symbol.iterator
                    argumentsList.AddRange(GetIterableElements(spreadValue));
                }
                else
                {
                    argumentsList.Add(Evaluate(argument));
                }
            }

            if (callee is not ISharpTSCallable function)
            {
                if ((call.Optional || HasOptionalInChain(call.Callee))
                    && (callee == null || callee is Runtime.Types.SharpTSUndefined))
                {
                    return RuntimeValue.Undefined;
                }
                // ECMA-262: invoking a non-callable surfaces as TypeError.
                throw new ThrowException(new SharpTSTypeError(
                    $"{GetTypeofString(callee)} is not a function"));
            }

            // Per ECMA-262 §10.2.1: missing arguments become undefined; do not
            // reject calls for user-defined functions.
            object? result;
            if (function is DotNetMethod dotNetMethod)
            {
                IReadOnlyList<Type>? explicitTypes = call.TypeArgs is { Count: > 0 }
                    ? ResolveDotNetGenericTypeArguments(call.TypeArgs)
                    : null;
                result = dotNetMethod.CallWithTypeHints(
                    this,
                    argumentsList,
                    ResolveDotNetArgumentTypeHints(call.Arguments),
                    explicitTypes,
                    ResolveDotNetResultTypeHint(call));
            }
            else
            {
                result = function.Call(this, argumentsList);
            }
            return RuntimeValue.FromBoxed(result);
        }
        finally
        {
            ArgumentListPool.Return(argumentsList);
        }
    }

    private bool TryBindDotNetExtension(
        object? receiver,
        string memberName,
        out DotNetExtensionMethod extension)
    {
        if (receiver is DotNetInstance instance &&
            _currentModule is { DotNetExtensionTypes.Count: > 0 } module &&
            DotNetTypeRegistry.GetMethods(
                instance.Type, memberName, isStatic: false).Length == 0 &&
            DotNetExtensionMethodResolver.GetReceiverClosedCandidates(
                module.DotNetExtensionTypes, memberName, instance.Type).Length > 0)
        {
            extension = new DotNetExtensionMethod(
                instance, module.DotNetExtensionTypes, memberName);
            return true;
        }

        extension = null!;
        return false;
    }

    private Type[] ResolveDotNetGenericTypeArguments(IReadOnlyList<string> typeArguments)
    {
        var resolved = new Type[typeArguments.Count];
        for (int i = 0; i < typeArguments.Count; i++)
        {
            string typeArgument = typeArguments[i];
            Type? importedType = _environment.TryGet(typeArgument, out var value) &&
                                 value.ToObject() is DotNetClass dotNetClass
                ? dotNetClass.Type
                : null;
            resolved[i] = importedType ?? DotNetTypeRegistry.ResolveFriendly(typeArgument)
                ?? throw new InvalidOperationException(
                    $"Could not resolve CLR generic type argument '{typeArgument}'.");
        }
        return resolved;
    }

    private Type?[]? ResolveDotNetArgumentTypeHints(IReadOnlyList<Expr> arguments)
    {
        if (_typeMap == null || HasSpreadArgs(arguments))
            return null;

        var result = new Type?[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            if (_typeMap.Get(arguments[i]) is { } type &&
                DotNetTypeSynthesizer.TryGetClrArgumentType(type, out var clr))
            {
                result[i] = clr;
            }
        }
        return result;
    }

    private Type? ResolveDotNetResultTypeHint(Expr.Call call) =>
        _typeMap?.Get(call) is { } type &&
        DotNetTypeSynthesizer.TryGetClrArgumentType(type, out var clr)
            ? clr
            : null;

    /// <summary>
    /// Checks if any arguments are spread expressions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSpreadArgs(IReadOnlyList<Expr> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] is Expr.Spread) return true;
        }
        return false;
    }

    /// <summary>
    /// Evaluates a binary operator expression.
    /// </summary>
    /// <param name="binary">The binary expression AST node.</param>
    /// <returns>The result of applying the operator to both operands.</returns>
    /// <remarks>
    /// Supports arithmetic (+, -, *, /, %, **), comparison (&gt;, &lt;, &gt;=, &lt;=, ==, ===, !=, !==),
    /// bitwise (&amp;, |, ^, &lt;&lt;, &gt;&gt;, &gt;&gt;&gt;), and special operators (in, instanceof).
    /// </remarks>
    /// <seealso href="https://developer.mozilla.org/en-US/docs/Web/JavaScript/Guide/Expressions_and_operators">MDN Expressions and Operators</seealso>
    private RuntimeValue EvaluateBinary(Expr.Binary binary)
    {
        var leftRV = EvaluateRV(binary.Left);
        var rightRV = EvaluateRV(binary.Right);

        // Fast path: both operands are numbers — avoid boxing entirely
        if (leftRV.IsNumber && rightRV.IsNumber)
        {
            double l = leftRV.AsNumber(), r = rightRV.AsNumber();
            var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);
            switch (desc)
            {
                case OperatorDescriptor.Plus:
                    return RuntimeValue.FromNumber(l + r);
                case OperatorDescriptor.Arithmetic:
                    return RuntimeValue.FromNumber(EvaluateArithmetic(binary.Operator.Type, l, r));
                case OperatorDescriptor.Power:
                    return RuntimeValue.FromNumber(Math.Pow(l, r));
                case OperatorDescriptor.Comparison:
                    return RuntimeValue.FromBoolean(EvaluateComparison(binary.Operator.Type, l, r));
                case OperatorDescriptor.Equality eq:
                    // ECMA-262 7.2.16: NaN is never strictly equal to anything
                    // (including itself). Use IEEE 754 `==` which returns false
                    // for NaN comparisons; Double.Equals is .NET-specific and
                    // treats NaN as equal to itself.
                    bool equal = l == r;
                    return RuntimeValue.FromBoolean(eq.IsNegated ? !equal : equal);
                case OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift:
                    return RuntimeValue.FromNumber(EvaluateBitwise(binary.Operator.Type, JsToInt32(l), JsToInt32(r)));
                case OperatorDescriptor.UnsignedRightShift:
                    return RuntimeValue.FromNumber((double)(JsToUint32(l) >> (JsToInt32(r) & 0x1F)));
            }
        }

        // Slow path: BigInt, string concatenation, mixed types, in, instanceof
        return EvaluateBinaryOperationRV(binary.Operator, leftRV, rightRV);
    }

    /// <summary>
    /// RuntimeValue-native binary operation — avoids boxing for BigInt, string concat, equality, etc.
    /// </summary>
    private RuntimeValue EvaluateBinaryOperationRV(Token op, RuntimeValue leftRV, RuntimeValue rightRV)
    {
        // instanceof/in are valid with any operand — fall through to the normal path below
        // so a bigint operand gets unboxed to object and reaches EvaluateInstanceof/EvaluateIn.
        if (op.Type is not (TokenType.INSTANCEOF or TokenType.IN))
        {
            // Check for BigInt (Kind == BigInt, not Object)
            System.Numerics.BigInteger? leftBigInt = leftRV.IsBigInt ? leftRV.AsBigInt().Value : null;
            System.Numerics.BigInteger? rightBigInt = rightRV.IsBigInt ? rightRV.AsBigInt().Value : null;

            if (leftBigInt.HasValue || rightBigInt.HasValue)
            {
                return EvaluateBigIntBinaryRV(op.Type, leftRV, rightRV, leftBigInt, rightBigInt);
            }
        }

        object? left = leftRV.ToObject();
        object? right = rightRV.ToObject();

        if (TryEvaluateDotNetBinary(op.Type, left, right, out var clrResult))
            return clrResult;

        var desc = SemanticOperatorResolver.Resolve(op.Type);

        return desc switch
        {
            // EvaluatePlus handles string concat, object ToPrimitive concat, and
            // numeric addition with ToNumber coercion (undefined→NaN, null→0,
            // booleans→0/1) — a raw (double) cast crashed with
            // InvalidCastException on any-typed undefined operands (#190).
            OperatorDescriptor.Plus => RuntimeValue.FromBoxed(EvaluatePlus(left, right)),
            OperatorDescriptor.Arithmetic => RuntimeValue.FromNumber(EvaluateArithmetic(op.Type, CoerceToNumber(left), CoerceToNumber(right))),
            OperatorDescriptor.Power => RuntimeValue.FromNumber(Math.Pow(CoerceToNumber(left), CoerceToNumber(right))),
            // JS AbstractRelationalComparison: string vs string → lexicographic.
            OperatorDescriptor.Comparison =>
                left is string ls && right is string rs
                    ? RuntimeValue.FromBoolean(EvaluateStringComparison(op.Type, ls, rs))
                    : RuntimeValue.FromBoolean(EvaluateComparison(op.Type, CoerceToNumber(left), CoerceToNumber(right))),
            OperatorDescriptor.Equality eq => RuntimeValue.FromBoolean(EvaluateEquality(left, right, eq.IsStrict, eq.IsNegated)),
            OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift =>
                RuntimeValue.FromNumber(EvaluateBitwise(op.Type, ToInt32(left), ToInt32(right))),
            OperatorDescriptor.UnsignedRightShift => RuntimeValue.FromNumber((double)(ToUint32(left) >> (ToInt32(right) & 0x1F))),
            OperatorDescriptor.In => RuntimeValue.FromBoxed(EvaluateIn(left, right)),
            OperatorDescriptor.InstanceOf => RuntimeValue.FromBoxed(EvaluateInstanceof(left, right)),
            _ => RuntimeValue.Undefined
        };
    }

    private bool TryEvaluateDotNetBinary(
        TokenType token,
        object? left,
        object? right,
        out RuntimeValue result)
    {
        Type? leftType = left is DotNetInstance leftInstance ? leftInstance.Type : null;
        Type? rightType = right is DotNetInstance rightInstance ? rightInstance.Type : null;
        var methods = DotNetOperatorResolver.GetBinaryCandidates(token, leftType, rightType);
        if (methods.Length == 0)
        {
            result = default;
            return false;
        }

        var arguments = new List<object?> { left, right };
        var candidate = DotNetMethodResolver.ResolveMethod(methods, arguments);
        var method = (System.Reflection.MethodInfo)candidate.Method;
        var invokeArgs = DotNetMethod.BuildInvokeArgs(
            method.GetParameters(), arguments, candidate, this);
        object? value = DotNetInstance.InvokeWithMapping(() => method.Invoke(null, invokeArgs));
        result = RuntimeValue.FromBoxed(DotNetMarshaller.WrapReturn(value, method.ReturnType));
        return true;
    }

    /// <summary>
    /// Evaluates arithmetic operators (-, *, /, %).
    /// </summary>
    private static double EvaluateArithmetic(TokenType op, double left, double right) => op switch
    {
        TokenType.MINUS => left - right,
        TokenType.STAR => left * right,
        TokenType.SLASH => left / right,
        TokenType.PERCENT => left % right,
        _ => throw new InterpreterException($"Unknown arithmetic operator: {op}")
    };

    /// <summary>
    /// Evaluates comparison operators (&lt;, &gt;, &lt;=, &gt;=).
    /// </summary>
    private static bool EvaluateComparison(TokenType op, double left, double right) => op switch
    {
        TokenType.LESS => left < right,
        TokenType.GREATER => left > right,
        TokenType.LESS_EQUAL => left <= right,
        TokenType.GREATER_EQUAL => left >= right,
        _ => throw new InterpreterException($"Unknown comparison operator: {op}")
    };

    /// <summary>
    /// Evaluates string relational comparison using lexicographic ordering
    /// (JS AbstractRelationalComparison when both operands are strings).
    /// </summary>
    private static bool EvaluateStringComparison(TokenType op, string left, string right)
    {
        int cmp = string.CompareOrdinal(left, right);
        return op switch
        {
            TokenType.LESS => cmp < 0,
            TokenType.GREATER => cmp > 0,
            TokenType.LESS_EQUAL => cmp <= 0,
            TokenType.GREATER_EQUAL => cmp >= 0,
            _ => throw new InterpreterException($"Unknown comparison operator: {op}")
        };
    }

    /// <summary>
    /// Evaluates equality operators (==, ===, !=, !==).
    /// </summary>
    private bool EvaluateEquality(object? left, object? right, bool isStrict, bool isNegated)
    {
        bool result = isStrict ? IsStrictEqual(left, right) : IsEqual(left, right);
        return isNegated ? !result : result;
    }

    /// <summary>
    /// Evaluates bitwise operators (&amp;, |, ^, &lt;&lt;, &gt;&gt;).
    /// </summary>
    private static double EvaluateBitwise(TokenType op, int left, int right) => op switch
    {
        TokenType.AMPERSAND => left & right,
        TokenType.PIPE => left | right,
        TokenType.CARET => left ^ right,
        TokenType.LESS_LESS => left << (right & 0x1F),
        TokenType.GREATER_GREATER => left >> (right & 0x1F),
        _ => throw new InterpreterException($"Unknown bitwise operator: {op}")
    };

    private RuntimeValue EvaluateBigIntBinaryRV(TokenType op, RuntimeValue leftRV, RuntimeValue rightRV,
        System.Numerics.BigInteger? leftBi, System.Numerics.BigInteger? rightBi)
    {
        // `+` with a string operand is string concatenation, not numeric add
        // (ECMA-262 13.15.3: ToPrimitive both, and if either is a String, concat).
        // bigint is already primitive; ToString(bigint) is the bare numeric form.
        if (op == TokenType.PLUS && (leftRV.IsString || rightRV.IsString))
        {
            object? l = leftRV.ToObject();
            object? r = rightRV.ToObject();
            return RuntimeValue.FromString(string.Concat(
                l as string ?? Stringify(l),
                r as string ?? Stringify(r)));
        }

        // Equality operators allow mixed types (bigint with anything).
        if (BigIntOperatorHelper.IsEqualityOperator(op))
        {
            if (leftBi.HasValue && rightBi.HasValue)
                return BigIntOperatorHelper.EvaluateBinaryRV(op, leftBi.Value, rightBi.Value);

            bool isNegated = op is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;
            bool equal;
            if (op is TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL)
            {
                // Strict ===/!==: a bigint is never the same type as a non-bigint.
                equal = false;
            }
            else
            {
                // Loose ==/!=: ECMA-262 7.2.15 bigint-vs-(number|string|boolean|object).
                var bi = leftBi ?? rightBi!.Value;
                var other = leftBi.HasValue ? rightRV.ToObject() : leftRV.ToObject();
                equal = LooseEqualsBigInt(bi, other);
            }
            return RuntimeValue.FromBoolean(isNegated ? !equal : equal);
        }

        // Relational comparison permits a BigInt/Number pair. Compare their
        // mathematical values without converting the BigInt to double (which
        // would lose precision above 2^53). Mixed arithmetic remains invalid.
        if (BigIntOperatorHelper.IsComparisonOperator(op)
            && leftBi.HasValue != rightBi.HasValue)
        {
            double number = leftBi.HasValue ? rightRV.AsNumber() : leftRV.AsNumber();
            int? comparison = CompareBigIntAndNumber(
                leftBi ?? rightBi!.Value, number);
            if (comparison is null)
                return RuntimeValue.False;
            int ordered = leftBi.HasValue ? comparison.Value : -comparison.Value;
            return RuntimeValue.FromBoolean(op switch
            {
                TokenType.LESS => ordered < 0,
                TokenType.LESS_EQUAL => ordered <= 0,
                TokenType.GREATER => ordered > 0,
                TokenType.GREATER_EQUAL => ordered >= 0,
                _ => false,
            });
        }

        // All other operators require both to be bigint.
        if (!leftBi.HasValue || !rightBi.HasValue)
            throw new InterpreterException("Cannot mix bigint and other types in operations.");

        return BigIntOperatorHelper.EvaluateBinaryRV(op, leftBi.Value, rightBi.Value);
    }

    private static int? CompareBigIntAndNumber(
        System.Numerics.BigInteger bigint, double number)
    {
        if (double.IsNaN(number)) return null;
        if (double.IsPositiveInfinity(number)) return -1;
        if (double.IsNegativeInfinity(number)) return 1;

        var integerPart = TruncateDoubleToBigInteger(number);
        int comparison = bigint.CompareTo(integerPart);
        if (comparison != 0 || number == Math.Truncate(number))
            return comparison;

        // BigInteger(double) truncates toward zero. If the integer values tie,
        // the fractional sign determines which mathematical value is larger.
        return number > 0 ? -1 : 1;
    }

    private static System.Numerics.BigInteger TruncateDoubleToBigInteger(double number)
    {
        long bits = BitConverter.DoubleToInt64Bits(number);
        bool negative = bits < 0;
        int exponentBits = (int)((bits >> 52) & 0x7ff);
        if (exponentBits == 0)
            return System.Numerics.BigInteger.Zero;

        int exponent = exponentBits - 1023;
        if (exponent < 0)
            return System.Numerics.BigInteger.Zero;
        ulong significand = ((ulong)bits & 0x000f_ffff_ffff_ffffUL)
            | 0x0010_0000_0000_0000UL;
        System.Numerics.BigInteger integer = exponent >= 52
            ? new System.Numerics.BigInteger(significand) << (exponent - 52)
            : new System.Numerics.BigInteger(significand >> (52 - exponent));
        return negative ? -integer : integer;
    }

    /// <summary>
    /// ECMA-262 7.2.15 IsLooselyEqual for a bigint vs a non-bigint operand:
    /// bigint==Number compares mathematical values (false for NaN/±Infinity and any
    /// non-integral double); bigint==String parses the string as an integer literal
    /// (false when it is not a valid one); bigint==Boolean coerces the boolean to
    /// 0n/1n; an Object operand is reduced via ToPrimitive and retried; null/undefined
    /// (and any other type) are never loosely equal to a bigint.
    /// </summary>
    private bool LooseEqualsBigInt(System.Numerics.BigInteger bi, object? other)
    {
        switch (other)
        {
            case SharpTSBigInt sbi:
                return bi == sbi.Value;
            case System.Numerics.BigInteger obi:
                return bi == obi;
            case double d:
                if (double.IsNaN(d) || double.IsInfinity(d) || d != Math.Floor(d))
                    return false;
                return new System.Numerics.BigInteger(d) == bi;
            case bool b:
                return bi == (b ? System.Numerics.BigInteger.One : System.Numerics.BigInteger.Zero);
            case string s:
                return TryStringToBigInt(s, out var parsed) && bi == parsed;
            case SharpTSObject:
                return LooseEqualsBigInt(bi, ToPrimitive(other, PrimitiveHint.Default));
            default:
                return false;
        }
    }

    /// <summary>
    /// ECMA-262 7.1.14 StringToBigInt (decimal subset): a trimmed integer literal,
    /// optionally signed; an empty/whitespace string is 0n. Returns false (the spec's
    /// "undefined") when the string is not a valid literal. Uses the same BCL
    /// <c>BigInteger.TryParse</c> the compiled <c>$Runtime.BigIntLooseEquals</c> emits,
    /// so both modes coerce identically (radix-prefixed string literals like "0xa" are
    /// a shared, documented gap — they compare unequal in both modes).
    /// </summary>
    private static bool TryStringToBigInt(string s, out System.Numerics.BigInteger value)
    {
        string t = s.Trim();
        if (t.Length == 0) { value = System.Numerics.BigInteger.Zero; return true; }
        return System.Numerics.BigInteger.TryParse(t, out value);
    }

    /// <summary>
    /// Converts a boxed double to a 32-bit signed integer per ECMA-262 ToInt32.
    /// </summary>
    /// <seealso href="https://tc39.es/ecma262/#sec-toint32">ECMAScript ToInt32</seealso>
    private int ToInt32(object? value) => JsToInt32(ToNumberWithPrimitive(value));

    /// <summary>
    /// Converts a boxed double to a 32-bit unsigned integer per ECMA-262 ToUint32.
    /// </summary>
    /// <seealso href="https://tc39.es/ecma262/#sec-touint32">ECMAScript ToUint32</seealso>
    internal uint ToUint32(object? value) => JsToUint32(ToNumberWithPrimitive(value));

    // ECMA-262 ToInt32 / ToUint32 on a double. Unlike C#'s saturating (int) cast,
    // non-finite → 0 and out-of-range doubles wrap modulo 2^32. Mirrors JsToInt32 in
    // Compilation/RuntimeEmitter.CoreUtilities.cs so interpreted and compiled modes agree.
    private static int JsToInt32(double n)
    {
        if (!double.IsFinite(n)) return 0;
        double t = n >= 0 ? Math.Floor(n) : Math.Ceiling(n);
        double mod = t - Math.Floor(t / 4294967296.0) * 4294967296.0;
        return mod >= 2147483648.0 ? (int)(mod - 4294967296.0) : (int)mod;
    }

    private static uint JsToUint32(double n)
    {
        if (!double.IsFinite(n)) return 0;
        double t = n >= 0 ? Math.Floor(n) : Math.Ceiling(n);
        return (uint)(t - Math.Floor(t / 4294967296.0) * 4294967296.0);
    }

    /// <summary>
    /// Evaluates the <c>instanceof</c> operator.
    /// </summary>
    /// <param name="left">The instance to check.</param>
    /// <param name="right">The class to check against.</param>
    /// <returns><c>true</c> if the instance is of the specified class or a subclass; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Walks up the inheritance chain to check if the instance's class or any superclass
    /// matches the target class name.
    /// </remarks>
    /// <seealso href="https://www.typescriptlang.org/docs/handbook/2/narrowing.html#instanceof-narrowing">TypeScript instanceof Narrowing</seealso>
    /// <summary>
    /// ECMA-262 §7.3.21 OrdinaryHasInstance — walks <paramref name="left"/>'s
    /// __proto__ chain looking for a link === <paramref name="ctor"/>.prototype.
    /// Capped at depth 64 to prevent runaway loops on cyclic chains. Used by
    /// both the SharpTSFunction and SharpTSArrowFunction instanceof arms.
    /// </summary>
    private static bool InstanceOfByPrototype(object? left, ISharpTSCallable ctor)
    {
        object? protoObj = ctor switch
        {
            SharpTSFunction fn when fn.TryGetProperty("prototype", out var p) => p,
            SharpTSArrowFunction af when af.TryGetProperty("prototype", out var p2) => p2,
            _ => null,
        };
        if (protoObj is null) return false;
        object? current = left;
        for (int i = 0; i < 64 && current is SharpTSObject curObj; i++)
        {
            if (curObj.HasProperty("__proto__")
                && curObj.GetProperty("__proto__") is var p
                && ReferenceEquals(p, protoObj))
            {
                return true;
            }
            current = curObj.HasProperty("__proto__") ? curObj.GetProperty("__proto__") : null;
            if (ReferenceEquals(current, curObj)) break;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="value"/> is a guest Object (non-primitive) —
    /// the test underlying <c>x instanceof Object</c>. The primitives (undefined,
    /// null, boolean, number, string, symbol, bigint) are the only values that
    /// are NOT objects; callables/arrays/typed-arrays/namespaces are all objects.
    /// </summary>
    private static bool IsJsObject(object? value) => value switch
    {
        null => false,                                   // `null instanceof Object` === false
        SharpTSUndefined => false,
        bool or double or string => false,
        SharpTSSymbol or SharpTSBigInt or System.Numerics.BigInteger => false,
        _ => true,
    };

    private static bool IsBoxedPrimitiveOfType(object? value, string typeTag) =>
        value is SharpTSObject obj
        && obj.HasProperty("__primitiveType")
        && obj.GetProperty("__primitiveType") is string pt
        && pt == typeTag;

    private object EvaluateInstanceof(object? left, object? right)
    {
        // `x instanceof Object` — the `Object` global resolves to the namespace
        // singleton (no SharpTSClass on the RHS), so brand-check structurally:
        // every non-primitive guest value is an Object. Mirrors ECMA-262
        // OrdinaryHasInstance reaching %Object.prototype% at the top of every
        // ordinary object's prototype chain (#334).
        if (right is SharpTSObjectNamespace)
            return IsJsObject(left);

        // `x instanceof Number/String/Boolean` — the RHS resolves to the namespace
        // singleton. A boxed wrapper produced by `new Number/String/Boolean` has
        // a __primitiveType marker; primitive values (bare doubles, strings, bools)
        // are NOT instances per ECMA-262 §20.3.3.3 / §21.1.3 / §22.1.3.
        if (right is SharpTSNumberNamespace)
            return IsBoxedPrimitiveOfType(left, "Number");
        if (right is SharpTSStringNamespace)
            return IsBoxedPrimitiveOfType(left, "String");
        if (right is SharpTSBooleanNamespace)
            return IsBoxedPrimitiveOfType(left, "Boolean");

        // The Function global is represented by a namespace-style callable
        // rather than SharpTSBuiltInConstructor. OrdinaryHasInstance for it
        // accepts every callable guest value, including declarations,
        // expressions/arrows, classes, and native built-ins.
        if (right is SharpTSFunctionGlobal)
            return left is ISharpTSCallable;

        // Bare-value constructors for the built-in binary types (ArrayBuffer,
        // SharedArrayBuffer, DataView, typed arrays) are plain ISharpTSCallables,
        // so they carry their own instance predicate (#334).
        if (right is IBuiltInTypeConstructor binaryCtor)
            return binaryCtor.IsInstance(left);

        // Built-in constructor instanceof: val instanceof Map, val instanceof Set, etc.
        if (right is SharpTSBuiltInConstructor ctor)
        {
            return ctor.Name switch
            {
                "Map" => left is SharpTSMap,
                "Set" => left is SharpTSSet,
                "WeakMap" => left is SharpTSWeakMap,
                "WeakSet" => left is SharpTSWeakSet,
                "Date" => left is SharpTSDate,
                "RegExp" => left is SharpTSRegExp,
                "TextEncoder" => left is SharpTSTextEncoder,
                "TextDecoder" => left is SharpTSTextDecoder,
                "Promise" => left is SharpTSPromise || left is System.Threading.Tasks.Task,
                "Buffer" => left is SharpTSBuffer,
                // The AbortSignal global is a namespace-style sentinel (no public
                // constructor) — brand-check the runtime instance type (#246).
                "AbortSignal" => left is SharpTSAbortSignal,
                "AbortController" => left is SharpTSAbortController,
                // A boxed Symbol wrapper (`Object(sym)`) carries the __primitiveType
                // marker; a bare SharpTSSymbol is not a SharpTSObject so stays false,
                // per ECMA-262 OrdinaryHasInstance (#449).
                "Symbol" => IsBoxedPrimitiveOfType(left, "Symbol"),
                _ => false
            };
        }

        // Buffer is registered as a namespace singleton (SharpTSBufferConstructor),
        // not as a SharpTSBuiltInConstructor, so `x instanceof Buffer` lands here
        // with the singleton object on the RHS. Match it explicitly.
        if (right is SharpTSBufferConstructor)
            return left is SharpTSBuffer;

        // Web-streams constructor wrapper singletons (the same values
        // stream/web exports and the ReadableStream/WritableStream/
        // TransformStream globals bind — #208).
        if (right is SharpTSReadableStreamConstructor)
            return left is SharpTSReadableStream;
        if (right is SharpTSWritableStreamConstructor)
            return left is SharpTSWritableStream;
        if (right is SharpTSTransformStreamConstructor)
            return left is SharpTSTransformStream;

        // Array is registered as a namespace singleton (SharpTSArrayGlobal), not
        // as a SharpTSBuiltInConstructor — so `arr instanceof Array` lands here.
        // Real arrays AND the Array.prototype dict itself satisfy the check
        // (ECMA-262 23.1.3 — Array.prototype is itself an Array exotic object).
        if (right is SharpTSArrayGlobal)
            return left is SharpTSArray and not SharpTSArguments
                || left is SharpTSArrayPrototype;

        // Constructor-function instanceof (JS `new Func()` pattern).
        // An object is `instanceof Func` when any link in its prototype
        // chain === Func.prototype.
        if (right is SharpTSFunction ctorFn)
            return InstanceOfByPrototype(left, ctorFn);
        // Function expressions (SharpTSArrowFunction with HasOwnThis) work
        // the same way — the test262 RegExp Symbol.split species-* tests
        // install function expressions as their species, and their
        // [[Construct]] result is checked via `instanceof species`.
        if (right is SharpTSArrowFunction arrCtor && arrCtor.HasOwnThis)
            return InstanceOfByPrototype(left, arrCtor);

        if (right is not SharpTSClass targetClass)
            return false;

        // Standard path: SharpTSInstance — walk the class chain
        if (left is SharpTSInstance instance)
        {
            SharpTSClass? current = instance.GetClass();
            while (current != null)
            {
                if (current.Name == targetClass.Name) return true;
                current = current.Superclass;
            }
            return false;
        }

        // Array subclass instances (#233): real SharpTSArrays carrying a guest
        // class — walk its chain (`m instanceof Array` is handled by the
        // SharpTSArrayGlobal arm above via `left is SharpTSArray`).
        if (left is SharpTSArraySubclassInstance subclassArray)
        {
            SharpTSClass? current = subclassArray.Klass;
            while (current != null)
            {
                if (current.Name == targetClass.Name) return true;
                current = current.Superclass;
            }
            return false;
        }

        // Promise subclass instances (#242): real SharpTSPromises carrying a
        // guest class — walk its chain (`p instanceof Promise` is handled by
        // the SharpTSBuiltInConstructor arm above via `left is SharpTSPromise`).
        if (left is SharpTSPromiseSubclassInstance subclassPromise)
        {
            SharpTSClass? current = subclassPromise.Klass;
            while (current != null)
            {
                if (current.Name == targetClass.Name) return true;
                current = current.Superclass;
            }
            return false;
        }

        // Legacy path: SharpTSError instances created via BuiltInConstructorFactory
        // (not SharpTSInstance, but should still pass instanceof Error/TypeError/etc.)
        if (left is SharpTSError error && targetClass is SharpTSErrorClass)
        {
            // Walk the error type hierarchy: TypeError extends Error, etc.
            var errorName = error.ErrorTypeName; // e.g. "TypeError"
            var targetName = targetClass.Name; // e.g. "Error"
            if (errorName == targetName) return true;
            // All built-in error subtypes extend Error
            if (targetName == "Error") return true;
            return false;
        }

        return false;
    }

    /// <summary>
    /// Evaluates a logical operator expression (AND/OR) with short-circuit evaluation.
    /// Uses RuntimeValue throughout to avoid boxing on the fast path.
    /// </summary>
    private RuntimeValue EvaluateLogical(Expr.Logical logical)
    {
        var left = EvaluateRV(logical.Left);
        if (logical.Operator.Type == TokenType.OR_OR)
            return IsTruthy(left) ? left : EvaluateRV(logical.Right);
        return !IsTruthy(left) ? left : EvaluateRV(logical.Right);
    }

    /// <summary>
    /// Evaluates the nullish coalescing operator (<c>??</c>).
    /// Uses RuntimeValue throughout — IsNullish check avoids boxing.
    /// </summary>
    private RuntimeValue EvaluateNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var left = EvaluateRV(nc.Left);
        return left.IsNullish ? EvaluateRV(nc.Right) : left;
    }

    /// <summary>
    /// Evaluates a ternary conditional expression (<c>?:</c>).
    /// Uses RuntimeValue — IsTruthy check on condition avoids boxing.
    /// </summary>
    private RuntimeValue EvaluateTernary(Expr.Ternary ternary)
    {
        var condition = EvaluateRV(ternary.Condition);
        return IsTruthy(condition) ? EvaluateRV(ternary.ThenBranch) : EvaluateRV(ternary.ElseBranch);
    }

    // ===================== Async Core Methods =====================

    /// <summary>
    /// Async version of logical operation core logic.
    /// Uses lazy evaluation via Func delegate to preserve short-circuit semantics.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateLogicalCoreAsync(
        TokenType op,
        ValueTask<RuntimeValue> leftTask,
        Func<ValueTask<RuntimeValue>> evaluateRightAsync)
    {
        var left = await leftTask;
        if (op == TokenType.OR_OR)
            return IsTruthy(left) ? left : await evaluateRightAsync();
        return !IsTruthy(left) ? left : await evaluateRightAsync();
    }

    /// <summary>
    /// Async version of nullish coalescing core logic.
    /// Uses lazy evaluation via Func delegate to preserve short-circuit semantics.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateNullishCoalescingCoreAsync(
        ValueTask<RuntimeValue> leftTask,
        Func<ValueTask<RuntimeValue>> evaluateRightAsync)
    {
        var left = await leftTask;
        return left.IsNullish
            ? await evaluateRightAsync()
            : left;
    }

    /// <summary>
    /// Async version of ternary operation core logic.
    /// Uses lazy evaluation via Func delegates to ensure only one branch is evaluated.
    /// </summary>
    private async ValueTask<RuntimeValue> EvaluateTernaryCoreAsync(
        ValueTask<RuntimeValue> conditionTask,
        Func<ValueTask<RuntimeValue>> evalThenAsync,
        Func<ValueTask<RuntimeValue>> evalElseAsync)
    {
        var condition = await conditionTask;
        return IsTruthy(condition)
            ? await evalThenAsync()
            : await evalElseAsync();
    }

    /// <summary>
    /// RuntimeValue-native compound operator — avoids boxing for numeric operations.
    /// Supports arithmetic (+=, -=, *=, /=, %=) and bitwise (&amp;=, |=, ^=, &lt;&lt;=, &gt;&gt;=, &gt;&gt;&gt;=)
    /// compound operators; string concatenation is handled for +=.
    /// </summary>
    private RuntimeValue ApplyCompoundOperatorRV(TokenType op, RuntimeValue left, RuntimeValue right)
    {
        if (DotNetOperatorResolver.GetBinaryTokenForCompound(op) is { } binaryToken &&
            TryEvaluateDotNetBinary(
                binaryToken, left.ToObject(), right.ToObject(), out var clrResult))
        {
            return clrResult;
        }

        // Fast path: both numeric (most common for compound assignment)
        if (left.IsNumber && right.IsNumber)
        {
            double l = left.AsNumberUnsafe(), r = right.AsNumberUnsafe();
            return op switch
            {
                TokenType.PLUS_EQUAL => RuntimeValue.FromNumber(l + r),
                TokenType.MINUS_EQUAL => RuntimeValue.FromNumber(l - r),
                TokenType.STAR_EQUAL => RuntimeValue.FromNumber(l * r),
                TokenType.SLASH_EQUAL => RuntimeValue.FromNumber(l / r),
                TokenType.PERCENT_EQUAL => RuntimeValue.FromNumber(l % r),
                TokenType.AMPERSAND_EQUAL => RuntimeValue.FromNumber(JsToInt32(l) & JsToInt32(r)),
                TokenType.PIPE_EQUAL => RuntimeValue.FromNumber(JsToInt32(l) | JsToInt32(r)),
                TokenType.CARET_EQUAL => RuntimeValue.FromNumber(JsToInt32(l) ^ JsToInt32(r)),
                TokenType.LESS_LESS_EQUAL => RuntimeValue.FromNumber(JsToInt32(l) << (JsToInt32(r) & 0x1F)),
                TokenType.GREATER_GREATER_EQUAL => RuntimeValue.FromNumber(JsToInt32(l) >> (JsToInt32(r) & 0x1F)),
                TokenType.GREATER_GREATER_GREATER_EQUAL => RuntimeValue.FromNumber(JsToUint32(l) >> (JsToInt32(r) & 0x1F)),
                _ => throw new InterpreterException($"Unknown compound operator: {op}")
            };
        }

        // String concatenation path
        if (op == TokenType.PLUS_EQUAL && left.IsString)
        {
            // ECMA-262 §7.1.17: a Symbol operand cannot be coerced to a string.
            ThrowIfSymbolStringCoercion(right.ToObject());
            return RuntimeValue.FromString(string.Concat(left.AsStringUnsafe(), Stringify(right.ToObject())));
        }

        // Fallback for mixed types
        object? l2 = left.ToObject(), r2 = right.ToObject();
        // ECMA-262 §7.1.17: a string `+=` Symbol operand cannot coerce to string.
        if (op == TokenType.PLUS_EQUAL && l2 is string)
            ThrowIfSymbolStringCoercion(r2);
        return op switch
        {
            TokenType.PLUS_EQUAL => RuntimeValue.FromBoxed(
                l2 is string s ? string.Concat(s, Stringify(r2)) : (double)l2! + (double)r2!),
            _ => RuntimeValue.FromNumber(op switch
            {
                TokenType.MINUS_EQUAL => (double)l2! - (double)r2!,
                TokenType.STAR_EQUAL => (double)l2! * (double)r2!,
                TokenType.SLASH_EQUAL => (double)l2! / (double)r2!,
                TokenType.PERCENT_EQUAL => (double)l2! % (double)r2!,
                TokenType.AMPERSAND_EQUAL => ToInt32(l2) & ToInt32(r2),
                TokenType.PIPE_EQUAL => ToInt32(l2) | ToInt32(r2),
                TokenType.CARET_EQUAL => ToInt32(l2) ^ ToInt32(r2),
                TokenType.LESS_LESS_EQUAL => ToInt32(l2) << (ToInt32(r2) & 0x1F),
                TokenType.GREATER_GREATER_EQUAL => ToInt32(l2) >> (ToInt32(r2) & 0x1F),
                TokenType.GREATER_GREATER_GREATER_EQUAL => (double)(ToUint32(l2) >> (ToInt32(r2) & 0x1F)),
                _ => throw new InterpreterException($"Unknown compound operator: {op}")
            })
        };
    }

    // ===================== Shared Builder Helpers =====================

    /// <summary>
    /// Builds a SharpTSArray from evaluated elements, handling spread elements.
    /// Shared between sync and async array evaluation paths.
    /// </summary>
    /// <param name="evaluatedElements">Tuples of (isSpread, evaluatedValue).</param>
    /// <returns>A new SharpTSArray containing all elements.</returns>
    private SharpTSArray BuildArrayFromElements(IEnumerable<(bool isSpread, object? value)> evaluatedElements)
    {
        List<object?> elements = [];
        foreach (var (isSpread, value) in evaluatedElements)
        {
            if (isSpread)
            {
                // Use GetIterableElements to support custom iterables with Symbol.iterator
                elements.AddRange(GetIterableElements(value));
            }
            else
            {
                elements.Add(value);
            }
        }
        return new SharpTSArray(elements);
    }

    /// <summary>
    /// Builds a SharpTSObject from evaluated properties.
    /// Shared between sync and async object evaluation paths.
    /// </summary>
    /// <param name="stringFields">String-keyed properties.</param>
    /// <param name="symbolFields">Symbol-keyed properties.</param>
    /// <returns>A new SharpTSObject with all properties set.</returns>
    private static SharpTSObject BuildObjectFromFields(
        Dictionary<string, object?> stringFields,
        Dictionary<SharpTSSymbol, object?> symbolFields)
    {
        var result = new SharpTSObject(stringFields);
        foreach (var (sym, val) in symbolFields)
        {
            result.SetBySymbol(sym, val);
        }
        return result;
    }

    /// <summary>
    /// Builds a template literal string from strings and evaluated expressions.
    /// Shared between sync and async template literal evaluation paths.
    /// </summary>
    /// <param name="strings">The static string parts of the template.</param>
    /// <param name="evaluatedExprs">The evaluated expression values.</param>
    /// <returns>The interpolated string result.</returns>
    private string BuildTemplateLiteralString(IReadOnlyList<string> strings, IReadOnlyList<object?> evaluatedExprs)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < strings.Count; i++)
        {
            result.Append(strings[i]);
            if (i < evaluatedExprs.Count)
            {
                // ECMA-262 §7.1.17: template-literal interpolation is an
                // implicit ToString coercion — a Symbol substitution throws.
                ThrowIfSymbolStringCoercion(evaluatedExprs[i]);
                result.Append(Stringify(evaluatedExprs[i]));
            }
        }
        return result.ToString();
    }
}
