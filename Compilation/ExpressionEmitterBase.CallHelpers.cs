using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.CallHandlers;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    /// <summary>
    /// Returns the hoisted 'this' field from the state machine builder, if available.
    /// Override in emitters that support class context (async methods, async generators).
    /// Arrow functions return null since they don't have their own 'this'.
    /// </summary>
    protected virtual FieldBuilder? GetThisField() => null;

    /// <summary>
    /// Shared call handler registry used by all emitter types.
    /// Handlers are stateless — all state comes from IEmitterContext.Context.
    /// </summary>
    protected static readonly CallHandlerRegistry _callHandlers = new();

    #region Shared Call Dispatch

    /// <summary>
    /// Shared EmitCall implementation used by all emitter types.
    /// Uses the handler chain for pattern-based dispatch, then falls through
    /// to remaining cases (super, class statics, method calls, direct function calls).
    /// </summary>
    protected virtual void EmitCall(Expr.Call c)
    {
        // CommonJS: literal require('./literal') → direct $GetExports() call.
        // Placed at the top so it works in async/generator state machine emitters too
        // (which inherit this method without overriding the prefix dispatch).
        if (TryEmitCjsRequireCall(c))
            return;

        if (TryEmitFunctionReturnThisIdiom(c))
            return;

        // Handler chain covers: console methods, fetch, parseInt/parseFloat/isNaN/isFinite,
        // setTimeout/clearTimeout/setInterval/clearInterval/queueMicrotask, Symbol/BigInt/Date/Error,
        // Date.now(), Math/JSON/Object/Array/Number/Promise/Symbol statics, built-in module methods,
        // __objectRest
        if (_callHandlers.TryHandle(this, c))
            return;

        // Optional-chain method calls (a.b?.m(x)) short-circuit to undefined
        // when a link is nullish — must be explicit now that InvokeMethodValue
        // throws for non-callable callees (#260).
        if (TryEmitOptionalChainMethodCall(c))
            return;

        // Special case: super.method() call — emit non-virtual Call to base method
        if (c.Callee is Expr.Super superMethodExpr && superMethodExpr.Method != null && superMethodExpr.Method.Lexeme != "constructor")
        {
            if (TryEmitSuperMethodCall(superMethodExpr.Method.Lexeme, c.Arguments))
                return;
        }

        // Handle structuredClone() global function
        if (c.Callee is Expr.Variable scVar && scVar.Name.Lexeme == "structuredClone")
        {
            EmitStructuredClone(c.Arguments);
            return;
        }

        // Handle fetch() with type assertions/groupings unwrapped
        if (c.Callee is not Expr.Variable)
        {
            Expr fetchCallee = c.Callee;
            bool unwrapped = true;
            while (unwrapped)
            {
                unwrapped = false;
                if (fetchCallee is Expr.TypeAssertion ta2) { fetchCallee = ta2.Expression; unwrapped = true; }
                if (fetchCallee is Expr.Grouping g2) { fetchCallee = g2.Expression; unwrapped = true; }
            }
            if (fetchCallee is Expr.Variable fetchVar && fetchVar.Name.Lexeme == "fetch")
            {
                EmitFetchCall(c.Arguments);
                return;
            }
        }

        // Handle Expr.Get callee patterns (module.promises, class statics, instance methods)
        if (c.Callee is Expr.Get methodGet)
        {
            if (TryEmitGetCalleeViaBaseClass(c, methodGet))
                return;

            // For state machine emitters: fall through to function value call
            // ILEmitter overrides EmitCall and routes to EmitMethodCall instead
        }

        // Check if it's a built-in module method binding (e.g., import { readFile } from 'fs/promises')
        if (c.Callee is Expr.Variable builtInVar &&
            Ctx.BuiltInModuleMethodBindings?.TryGetValue(builtInVar.Name.Lexeme, out var binding) == true)
        {
            var builtInEmitter = Ctx.BuiltInModuleEmitterRegistry?.GetEmitter(binding.ModuleName);
            if (builtInEmitter != null && builtInEmitter.TryEmitMethodCall(this, binding.MethodName, c.Arguments))
            {
                SetStackUnknown();
                return;
            }
        }

        // Direct function call with full support: generics, rest params, overloads, spreads
        if (c.Callee is Expr.Variable funcVar)
        {
            string resolvedFuncName = Ctx.ResolveFunctionName(funcVar.Name.Lexeme);

            // Imported functions must go through TopLevelStaticVars (cross-module tokens don't work)
            bool isImportedFunction = Ctx.TopLevelStaticVars?.ContainsKey(funcVar.Name.Lexeme) == true;

            // A more-local binding of the same name (parameter, local, captured var, or a
            // hoisted nested function declaration) must shadow a module top-level function,
            // matching JS lexical scoping and the interpreter (#607). The variable-read path
            // (EmitVariable) already resolves the local binding before the top-level Functions
            // branch; the direct-call path bypasses the resolver, so without this guard a call
            // `h()` would silently hit the top-level `h` instead of the in-scope `h`. Defer to
            // the inner-function direct call (below) or the function-value call (fallback),
            // both of which honor the resolver's precedence.
            bool shadowedByLocalBinding = Resolver.HasVariable(funcVar.Name.Lexeme);

            // Exception: a same-module top-level function referenced from within a nested
            // closure is materialized into a display-class capture field, so HasVariable
            // reports it — but it is the function itself, not a shadowing binding. Routing it
            // through the value-call path would drop the direct call's typed-parameter
            // conversions (e.g. the `path` stdlib's `posix.join` method calls module helper
            // `posixJoin(parts: string[])`, whose List<string> parameter a value call cannot
            // satisfy from a List<object>). Keep the direct call unless a genuine parameter or
            // local of the same name actually shadows it.
            if (shadowedByLocalBinding &&
                Ctx.Functions.ContainsKey(resolvedFuncName) &&
                Ctx.CapturedFields?.ContainsKey(funcVar.Name.Lexeme) == true &&
                !Ctx.TryGetParameter(funcVar.Name.Lexeme, out _) &&
                !Ctx.Locals.HasLocal(funcVar.Name.Lexeme))
            {
                shadowedByLocalBinding = false;
            }

            if (!shadowedByLocalBinding && !isImportedFunction && Ctx.Functions.TryGetValue(resolvedFuncName, out var methodBuilder))
            {
                MethodInfo targetMethod = methodBuilder;

                // Generic function instantiation
                if (Ctx.IsGenericFunction?.TryGetValue(resolvedFuncName, out var isGeneric) == true && isGeneric)
                {
                    if (c.TypeArgs != null && c.TypeArgs.Count > 0)
                    {
                        Type[] typeArgs = c.TypeArgs.Select(ResolveTypeArg).ToArray();
                        targetMethod = methodBuilder.MakeGenericMethod(typeArgs);
                    }
                    else
                    {
                        var genericParams = Ctx.FunctionGenericParams![resolvedFuncName];
                        Type[] inferredArgs = new Type[genericParams.Length];
                        for (int i = 0; i < genericParams.Length; i++)
                        {
                            var baseConstraint = genericParams[i].BaseType;
                            inferredArgs[i] = (baseConstraint != null && !Types.IsObject(baseConstraint))
                                ? baseConstraint
                                : Types.Object;
                        }
                        targetMethod = methodBuilder.MakeGenericMethod(inferredArgs);
                    }
                }

                var paramCount = targetMethod.GetParameters().Length;

                // Rest parameter handling
                (int RestParamIndex, int RegularParamCount) restInfo = default;
                bool hasRestParam = Ctx.FunctionRestParams?.TryGetValue(resolvedFuncName, out restInfo) == true;

                if (hasRestParam)
                {
                    EmitRestParameterCall(c.Arguments, restInfo.RegularParamCount, targetMethod.GetParameters());
                }
                else
                {
                    // Overload resolution: find matching overload for argument count
                    if (c.Arguments.Count < paramCount &&
                        Ctx.FunctionOverloads != null &&
                        Ctx.FunctionOverloads.TryGetValue(resolvedFuncName, out var overloads))
                    {
                        var matchingOverload = overloads.FirstOrDefault(o =>
                            o.GetParameters().Length == c.Arguments.Count);
                        if (matchingOverload != null)
                        {
                            targetMethod = matchingOverload;
                            paramCount = c.Arguments.Count;
                        }
                    }

                    // Store each converted arg to a temp for await-safety
                    // (async emitters may yield during EmitExpression).
                    //
                    // When a later argument can suspend (await/yield), spill each earlier argument
                    // to a *registered, boxed* object local: a parameter-typed IL local does not
                    // survive a deferred MoveNext re-entry (only state-machine fields do, #400), so
                    // the earlier arg would read back as null and the typed reload also fails IL
                    // verify (#436). Without a suspending argument — every call in the synchronous
                    // ILEmitter, and await-free calls in async/generator bodies — keep the cheaper
                    // parameter-typed locals (the JIT collapses the store/load and value-typed args
                    // stay unboxed).
                    var targetParams = targetMethod.GetParameters();
                    bool awaitSafe = AnyContainsSuspension(c.Arguments);
                    List<(LocalBuilder Local, Type ParamType, bool Boxed)> callArgTemps = [];

                    for (int i = 0; i < c.Arguments.Count; i++)
                    {
                        var arg = c.Arguments[i];
                        Type paramType = i < targetParams.Length ? targetParams[i].ParameterType : Types.Object;
                        if (arg is Expr.Spread spread)
                        {
                            // A spread in a fixed-arity (non-rest) call is passed as one boxed value.
                            if (awaitSafe)
                            {
                                callArgTemps.Add((SpillBoxed(spread.Expression), Types.Object, true));
                                continue;
                            }
                            EmitExpression(spread.Expression);
                            EnsureBoxed();
                            paramType = Types.Object;
                        }
                        else if (awaitSafe)
                        {
                            callArgTemps.Add((SpillConvertedArg(arg, paramType), paramType, true));
                            continue;
                        }
                        else
                        {
                            EmitExpression(arg);
                            if (i < targetParams.Length)
                                EmitConversionForParameter(arg, targetParams[i].ParameterType);
                            else
                                EnsureBoxed();
                        }
                        var temp = IL.DeclareLocal(paramType);
                        IL.Emit(OpCodes.Stloc, temp);
                        callArgTemps.Add((temp, paramType, false));
                    }

                    for (int i = c.Arguments.Count; i < paramCount; i++)
                    {
                        var pType = targetParams[i].ParameterType;
                        if (pType == Types.Object)
                        {
                            // Missing optional args default to undefined (JS spec)
                            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
                        }
                        else
                        {
                            EmitDefaultForType(pType);
                        }
                        // Defaults are emitted after every real argument, so no suspension can
                        // follow — a plain parameter-typed local is sufficient (Boxed = false).
                        var temp = IL.DeclareLocal(pType);
                        IL.Emit(OpCodes.Stloc, temp);
                        callArgTemps.Add((temp, pType, false));
                    }

                    // If the callee's body references JS `arguments`, publish the raw
                    // user args (exactly what the caller wrote — no null padding for
                    // optional slots, no declared-arity trimming) to the $TSFunction
                    // thread-static so the callee's prologue can reconstruct the full
                    // arguments object including extras past the declared arity (#64).
                    // Done *before* loading args onto the stack so we don't disturb
                    // the evaluation order for the call itself.
                    if (Ctx.FunctionsCapturingArguments?.Contains(resolvedFuncName) == true &&
                        Ctx.Runtime?.CurrentArgumentsField != null)
                    {
                        int publishCount = c.Arguments.Count;
                        IL.Emit(OpCodes.Ldc_I4, publishCount);
                        IL.Emit(OpCodes.Newarr, Types.Object);
                        for (int i = 0; i < publishCount; i++)
                        {
                            IL.Emit(OpCodes.Dup);
                            IL.Emit(OpCodes.Ldc_I4, i);
                            IL.Emit(OpCodes.Ldloc, callArgTemps[i].Local);
                            // Await-safe temps are already boxed objects; only parameter-typed
                            // value-type temps need boxing for the object[] arguments array.
                            if (!callArgTemps[i].Boxed && callArgTemps[i].ParamType.IsValueType)
                                IL.Emit(OpCodes.Box, callArgTemps[i].ParamType);
                            IL.Emit(OpCodes.Stelem_Ref);
                        }
                        IL.Emit(OpCodes.Stsfld, Ctx.Runtime.CurrentArgumentsField);
                    }

                    // Load args back onto stack for the call — but only up to the
                    // callee's declared arity. Extras are still evaluated (temps
                    // assigned above preserve side effects) but discarded per JS
                    // spec: "any extra arguments are simply ignored." Without this
                    // guard, passing more args than declared leaves orphans on
                    // the stack and produces InvalidProgramException at load time
                    // (#65, prerequisite for #64's zero-declared-param shape).
                    int loadCount = Math.Min(callArgTemps.Count, paramCount);
                    for (int i = 0; i < loadCount; i++)
                    {
                        IL.Emit(OpCodes.Ldloc, callArgTemps[i].Local);
                        // Await-safe temps hold a boxed object — coerce back to the declared
                        // parameter slot (unbox value types / downcast reference types).
                        if (callArgTemps[i].Boxed)
                            EmitCoerceBoxedToType(callArgTemps[i].ParamType);
                    }
                }

                IL.Emit(OpCodes.Call, targetMethod);
                BoxReturnValueIfNeeded(targetMethod.ReturnType);
                return;
            }
        }

        // Inner function direct call (recursive self-calls and sibling inner function calls)
        if (c.Callee is Expr.Variable innerFuncVar &&
            Ctx.InnerFunctionMethodsByName?.TryGetValue(innerFuncVar.Name.Lexeme, out var innerMethod) == true)
        {
            EmitInnerFunctionDirectCall(innerFuncVar.Name.Lexeme, innerMethod, c.Arguments);
            return;
        }

        // Non-escaping direct-call arrow (#858): `add(i)` where `add` is a non-escaping const-bound
        // CAPTURING arrow stored as a bare display-class instance in a typed local (see
        // EmitVarDeclaration). Emit a direct `callvirt Invoke` with unboxed typed args, skipping the
        // per-call $TSFunction wrapper + reflective InvokeMethodValue dispatch. Gated on the in-scope
        // slot's CLR type matching the display class, so a same-named binding in another scope (a
        // parameter, an object local, an async state-machine field) never hits this path.
        if (TryEmitDirectCallArrow(c))
            return;

        // Function value call with spread support
        EmitFunctionValueCall(c);
    }

    /// <summary>
    /// Fast path for a non-escaping const-bound arrow invoked by name (#858 and its follow-up). Returns
    /// false (caller falls back to the generic value-call) unless every guard holds: the callee is a
    /// bare, non-optional variable flagged by <see cref="NonEscapingArrowLocalAnalyzer"/> with a known
    /// arrow method; the call has no spread / type arguments and matches the declared arity; and no
    /// argument can suspend (defensive for state-machine emitters that inherit this method but never
    /// produce the binding). Two shapes are handled, both keyed on the *actual in-scope binding* so a
    /// same-named parameter/local in another scope can never match:
    /// <list type="bullet">
    ///   <item><b>capturing</b> — the arrow has a display class; the binding stored the bare display
    ///   instance in a typed local. Keyed on that local's CLR type; emitted as `callvirt Invoke` with
    ///   the instance as receiver.</item>
    ///   <item><b>non-capturing</b> — the arrow is a static method with no instance; the binding tagged
    ///   its in-scope slot with the arrow node. Keyed on that tag; emitted as a direct `call` (no
    ///   receiver).</item>
    /// </list>
    /// Either way value-typed params (double/bool) stay unboxed — no per-call boxing or object[].
    /// </summary>
    private bool TryEmitDirectCallArrow(Expr.Call c)
    {
        if (c.Callee is not Expr.Variable arrowVar || c.Optional)
            return false;
        if (c.TypeArgs is { Count: > 0 })
            return false;
        if (Ctx.DirectCallArrowBindings == null ||
            !Ctx.DirectCallArrowBindings.TryGetValue(arrowVar.Name.Lexeme, out var arrow) ||
            !Ctx.ArrowMethods.TryGetValue(arrow, out var method))
            return false;

        // Resolve and validate the in-scope binding. Capturing → typed display-class local; non-capturing
        // → tagged marker slot. A miss on either keying means this name is NOT our optimized binding here.
        LocalBuilder? receiver = null;
        if (Ctx.DisplayClasses.TryGetValue(arrow, out var displayClass))
        {
            if (!Ctx.Locals.TryGetLocal(arrowVar.Name.Lexeme, out var arrowLocal) ||
                arrowLocal.LocalType != displayClass)
                return false;
            receiver = arrowLocal;
        }
        else
        {
            if (!Ctx.Locals.TryGetTag(arrowVar.Name.Lexeme, out var tag) || !ReferenceEquals(tag, arrow))
                return false;
        }

        var parameters = method.GetParameters();
        if (c.Arguments.Count != parameters.Length)
            return false;
        if (c.Arguments.Any(a => a is Expr.Spread) || AnyContainsSuspension(c.Arguments))
            return false;

        // Receiver (display instance) first for the capturing/instance call; none for the static call.
        // Then each argument converted to its declared parameter slot.
        if (receiver != null)
            IL.Emit(OpCodes.Ldloc, receiver);
        for (int i = 0; i < c.Arguments.Count; i++)
        {
            EmitExpression(c.Arguments[i]);
            EmitConversionForParameter(c.Arguments[i], parameters[i].ParameterType);
        }
        IL.Emit(receiver != null ? OpCodes.Callvirt : OpCodes.Call, method);
        BoxReturnValueIfNeeded(method.ReturnType);
        return true;
    }

    /// <summary>
    /// Emits a method call whose name (e.g. <c>charAt</c>) collides with a built-in string
    /// method, but whose static receiver type is <i>not</i> known to be a string. The
    /// naive "fall back to the string strategy" path crashes at runtime with
    /// <c>InvalidCastException</c> when the receiver is actually a user class with its
    /// own <c>charAt</c> (e.g. yaml's Lexer). Instead, emit dynamic method lookup that
    /// preserves <c>this</c>: <c>InvokeMethodValue(obj, GetProperty(obj, methodName), args)</c>.
    /// String receivers still resolve correctly — <c>GetProperty</c> routes string method
    /// lookups to the built-in string prototype, and <c>InvokeMethodValue</c> binds the
    /// receiver so the built-in can read it back.
    /// </summary>
    private void EmitDynamicMethodCallPreservingThis(Expr obj, string methodName, List<Expr> arguments)
    {
        EmitExpression(obj);
        EnsureBoxed();
        // SpillStoreObject (not a plain local) so the receiver survives a MoveNext re-entry when a
        // later argument suspends (#400/#614); in the synchronous emitter it is just a plain local.
        var objLocal = _helpers.SpillStoreObject();

        // Fast path: if receiver is a string at runtime, dispatch to the string-strategy path
        // directly. GetProperty(str, methodName) returns null for everything except "length",
        // so the generic path below would produce null → InvokeMethodValue(_, null, _) → null.
        // This matters in generator/async bodies where TypeMap typically can't prove the
        // receiver is a string.
        if (IsRuntimeDispatchableStringMethod(methodName))
        {
            // The string fast path and the generic GetProperty path both contain the arguments and
            // are mutually exclusive at runtime. When an argument can suspend, spill the arguments
            // once here so each `await`/`yield` is emitted exactly once — emitting them in both
            // branches would emit the suspension twice and desync the await-state counter (#614).
            LocalBuilder[]? argLocals = AnyContainsSuspension(arguments)
                ? arguments.Select(SpillBoxed).ToArray()
                : null;

            var notStringLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Isinst, Types.String);
            IL.Emit(OpCodes.Brfalse, notStringLabel);

            EmitRuntimeStringMethod(objLocal, methodName, arguments, argLocals);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(notStringLabel);
            EmitGetPropertyAndInvoke(objLocal, methodName, arguments, argLocals);
            IL.MarkLabel(endLabel);
            SetStackUnknown();
            return;
        }

        EmitGetPropertyAndInvoke(objLocal, methodName, arguments);
        SetStackUnknown();
    }

    private void EmitGetPropertyAndInvoke(LocalBuilder objLocal, string methodName, List<Expr> arguments, LocalBuilder[]? argLocals = null)
    {
        // ECMA-262 §13.3.2.1: evaluating the member-access callee `recv.method`
        // performs RequireObjectCoercible(recv), so an undefined receiver throws
        // TypeError *before* ArgumentListEvaluation (§13.3.6.1 step 2 runs before
        // the callability check in steps 3/4). $Runtime.GetProperty returns
        // undefined for a nullish base (so optional chains can short-circuit),
        // which would otherwise defer the throw past the arguments and run their
        // side effects. Enforce coercibility here, on the non-optional method-call
        // path. (test262 language/expressions/call/11.2.3-3_3)
        EmitThrowIfReceiverUndefined(objLocal, methodName);

        // Resolve the method first (property lookup precedes argument evaluation, per spec) and
        // store it, so the receiver and resolved fn aren't left on the IL stack while arguments are
        // evaluated — a later argument can suspend (await/yield), which requires a clear stack.
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty); // → fn
        var fnLocal = _helpers.SpillStoreObject();

        // When the caller hasn't pre-spilled the arguments and one can suspend, spill them now
        // (after the lookup) so the suspension happens with a clear stack (#614/#413).
        argLocals ??= AnyContainsSuspension(arguments)
            ? arguments.Select(SpillBoxed).ToArray()
            : null;

        IL.Emit(OpCodes.Ldloc, objLocal);                // receiver for InvokeMethodValue
        IL.Emit(OpCodes.Ldloc, fnLocal);                 // resolved fn
        EmitBoxedArgsArray(arguments, argLocals);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeMethodValue);
    }

    /// <summary>
    /// True for compile-time null-placeholder globals — globals that emit CLR
    /// <c>null</c> and rely on special-cased property access (currently just
    /// <c>process</c>, see <c>EmitVariable</c>'s <c>process</c> branch). An
    /// uncovered property on such a placeholder yields <c>null</c>/<c>undefined</c>
    /// via <c>GetProperty</c>'s null branch rather than a <c>TypeError</c>, because
    /// the receiver is a stand-in for a host object whose full reification isn't
    /// emitted — not a runtime JS <c>null</c>. Exempting it preserves the
    /// pre-#735/#733 behavior for <c>process.X</c> reads/writes the dedicated
    /// emitters don't cover (#735/#733).
    /// </summary>
    protected static bool IsNullPlaceholderGlobal(Expr receiver) =>
        receiver is Expr.Variable v && v.Name.Lexeme == "process";

    /// <summary>
    /// Emits a guard that throws <c>TypeError</c> when <paramref name="objLocal"/>
    /// holds <c>$Undefined</c> or CLR <c>null</c> — the RequireObjectCoercible step a
    /// member-access callee performs before its arguments are evaluated. Missing-property reads
    /// (e.g. <c>o.bar</c> where <c>bar</c> is absent) yield <c>$Undefined</c>, so
    /// this catches <c>o.bar.gar(sideEffect())</c> before the side effect runs.
    ///
    /// Compiled sloppy-mode <c>this</c> is no longer represented as bare CLR <c>null</c>:
    /// it resolves to the globalThis sentinel (see <see cref="LocalVariableResolver.LoadThis"/>
    /// and <c>InvokeWithThis</c>), so a CLR <c>null</c> receiver here is an unambiguous
    /// genuine JS <c>null</c> and is rejected with the "Cannot read properties of null"
    /// message (#735). Symbols/primitives are coercible and pass through.
    /// </summary>
    protected void EmitThrowIfReceiverUndefined(LocalBuilder objLocal, string methodName, bool isWrite = false)
    {
        var action = isWrite ? "set" : "read";
        var actionIng = isWrite ? "setting" : "reading";
        var undefinedLabel = IL.DefineLabel();
        var nullLabel = IL.DefineLabel();
        var okLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
        IL.Emit(OpCodes.Brtrue, undefinedLabel);          // $Undefined → throw "undefined"

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Brfalse, nullLabel);              // CLR null → throw "null"

        IL.Emit(OpCodes.Br, okLabel);

        IL.MarkLabel(undefinedLabel);
        IL.Emit(OpCodes.Ldstr, $"Cannot {action} properties of undefined ({actionIng} '{methodName}')");
        GuestErrorEmitter.ThrowErrorFromStack(IL, Ctx.Runtime!, Ctx.Runtime!.TSTypeErrorCtor);

        IL.MarkLabel(nullLabel);
        IL.Emit(OpCodes.Ldstr, $"Cannot {action} properties of null ({actionIng} '{methodName}')");
        GuestErrorEmitter.ThrowErrorFromStack(IL, Ctx.Runtime!, Ctx.Runtime!.TSTypeErrorCtor);

        IL.MarkLabel(okLabel);
    }

    /// <summary>
    /// ECMA-262 RequireObjectCoercible for a NON-optional member READ (<c>o.x</c>):
    /// with the boxed receiver on top of the stack, throws a guest <c>TypeError</c>
    /// when it is <c>$Undefined</c> or CLR <c>null</c>, leaving the receiver on the
    /// stack otherwise. The message distinguishes the two ("Cannot read properties of
    /// undefined|null (reading '<paramref name="propertyName"/>')"). The read-expression
    /// analog of <see cref="EmitThrowIfReceiverUndefined"/> (the method-call-callee guard)
    /// and the compiled counterpart of the interpreter's guard. Compiled sloppy-mode
    /// <c>this</c> now resolves to the globalThis sentinel instead of CLR <c>null</c>,
    /// so a null receiver here is an unambiguous genuine JS <c>null</c> (#735). Fixes
    /// #701 (undefined) and #735 (null). Callers MUST short-circuit the optional-chain
    /// case (<c>o?.x</c>) before calling this.
    /// </summary>
    protected void EmitThrowIfUndefinedReceiverOnStack(string propertyName)
    {
        var undefinedLabel = IL.DefineLabel();
        var nullLabel = IL.DefineLabel();
        var okLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
        IL.Emit(OpCodes.Brtrue, undefinedLabel);          // $Undefined → throw "undefined"

        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brfalse, nullLabel);              // CLR null → throw "null"

        IL.Emit(OpCodes.Br, okLabel);

        IL.MarkLabel(undefinedLabel);
        IL.Emit(OpCodes.Pop);                             // discard receiver
        IL.Emit(OpCodes.Ldstr, $"Cannot read properties of undefined (reading '{propertyName}')");
        GuestErrorEmitter.ThrowErrorFromStack(IL, Ctx.Runtime!, Ctx.Runtime!.TSTypeErrorCtor);

        IL.MarkLabel(nullLabel);
        IL.Emit(OpCodes.Pop);                             // discard receiver
        IL.Emit(OpCodes.Ldstr, $"Cannot read properties of null (reading '{propertyName}')");
        GuestErrorEmitter.ThrowErrorFromStack(IL, Ctx.Runtime!, Ctx.Runtime!.TSTypeErrorCtor);

        IL.MarkLabel(okLabel);
    }

    /// <summary>
    /// Bracket-access (<c>o[k]</c>) counterpart of
    /// <see cref="EmitThrowIfUndefinedReceiverOnStack"/>: throws a guest
    /// <c>TypeError</c> when <paramref name="objLocal"/> holds <c>$Undefined</c> or
    /// CLR <c>null</c>, splicing the runtime key (<paramref name="keyLocal"/>, a boxed
    /// <see cref="object"/>) into the message to match Node ("Cannot read properties of
    /// undefined|null (reading '&lt;key&gt;')"). Leaves the stack untouched. Callers MUST
    /// short-circuit the optional-chain case first. (#701 undefined / #735 null)
    /// </summary>
    protected void EmitThrowIfUndefinedIndexReceiver(LocalBuilder objLocal, LocalBuilder keyLocal, bool isWrite = false)
    {
        var prefixUndef = isWrite
            ? "Cannot set properties of undefined (setting '"
            : "Cannot read properties of undefined (reading '";
        var prefixNull = isWrite
            ? "Cannot set properties of null (setting '"
            : "Cannot read properties of null (reading '";
        var undefinedLabel = IL.DefineLabel();
        var nullLabel = IL.DefineLabel();
        var okLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
        IL.Emit(OpCodes.Brtrue, undefinedLabel);          // $Undefined → throw "undefined"

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Brfalse, nullLabel);              // CLR null → throw "null"

        IL.Emit(OpCodes.Br, okLabel);

        // "<prefix>" + key + "')" — Concat(object, object) is null-safe
        // (a null key stringifies to "").
        IL.MarkLabel(undefinedLabel);
        IL.Emit(OpCodes.Ldstr, prefixUndef);
        IL.Emit(OpCodes.Ldloc, keyLocal);
        IL.Emit(OpCodes.Call, Types.StringConcatObjectObject);
        IL.Emit(OpCodes.Ldstr, "')");
        IL.Emit(OpCodes.Call, Types.StringConcat2);
        GuestErrorEmitter.ThrowErrorFromStack(IL, Ctx.Runtime!, Ctx.Runtime!.TSTypeErrorCtor);

        IL.MarkLabel(nullLabel);
        IL.Emit(OpCodes.Ldstr, prefixNull);
        IL.Emit(OpCodes.Ldloc, keyLocal);
        IL.Emit(OpCodes.Call, Types.StringConcatObjectObject);
        IL.Emit(OpCodes.Ldstr, "')");
        IL.Emit(OpCodes.Call, Types.StringConcat2);
        GuestErrorEmitter.ThrowErrorFromStack(IL, Ctx.Runtime!, Ctx.Runtime!.TSTypeErrorCtor);

        IL.MarkLabel(okLabel);
    }

    /// <summary>
    /// Builds an <c>object[]</c> of the boxed call arguments on the stack. Stack: [] -&gt; [object[]].
    /// When <paramref name="argLocals"/> is non-null each element is loaded from those pre-spilled,
    /// already-boxed locals (await-safe: the arguments were evaluated and spilled by the caller, so
    /// no suspension is re-emitted here); otherwise each argument expression is emitted inline.
    /// </summary>
    private void EmitBoxedArgsArray(List<Expr> arguments, LocalBuilder[]? argLocals)
    {
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            if (argLocals != null)
            {
                IL.Emit(OpCodes.Ldloc, argLocals[i]);
            }
            else
            {
                EmitExpression(arguments[i]);
                EnsureBoxed();
            }
            IL.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static bool IsRuntimeDispatchableStringMethod(string methodName) => methodName is
        "substring" or "substr" or "charAt" or "charCodeAt" or "toUpperCase" or "toLowerCase"
        or "trim" or "trimStart" or "trimEnd" or "startsWith" or "endsWith"
        or "repeat" or "padStart" or "padEnd" or "at";

    /// <param name="argLocals">
    /// Pre-spilled, already-boxed argument locals to read from instead of re-emitting the argument
    /// expressions. The caller passes these when an argument can suspend so the <c>await</c>/<c>yield</c>
    /// is emitted exactly once (#614); null means emit the arguments inline.
    /// </param>
    private void EmitRuntimeStringMethod(LocalBuilder objLocal, string methodName, List<Expr> arguments, LocalBuilder[]? argLocals = null)
    {
        // Stack entry: (nothing — objLocal is the receiver). Push the string, then args, then call.
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, Types.String);

        switch (methodName)
        {
            case "substring":
                // StringSubstring takes (string, object[]): pack args into object[].
                EmitBoxedArgsArray(arguments, argLocals);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringSubstring);
                break;

            case "substr":
                EmitBoxedArgsArray(arguments, argLocals);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringSubstr);
                break;

            case "toUpperCase":
                IL.Emit(OpCodes.Callvirt, Types.GetMethodNoParams(Types.String, "ToUpper"));
                break;

            case "toLowerCase":
                IL.Emit(OpCodes.Callvirt, Types.GetMethodNoParams(Types.String, "ToLower"));
                break;

            case "trim":
                IL.Emit(OpCodes.Callvirt, Types.GetMethodNoParams(Types.String, "Trim"));
                break;

            default:
                // Other known methods: fall back to GetProperty+Invoke; the caller still
                // returns a value, just not via the direct runtime method.
                IL.Emit(OpCodes.Pop); // pop the cast-to-string receiver we pushed
                EmitGetPropertyAndInvoke(objLocal, methodName, arguments, argLocals);
                break;
        }
    }

    /// <summary>
    /// Emits arguments for a function call with rest parameter, handling spreads: loads the
    /// leading regular arguments, then builds the trailing rest array.
    /// </summary>
    /// <remarks>
    /// Every argument is spilled to a local up front via <see cref="SpillBoxed"/> before any
    /// array is assembled. State-machine emitters can suspend (<c>await</c>) inside any
    /// argument; assembling the rest array inline would leave the array reference (and the
    /// regular args) on the IL evaluation stack across the suspension, which produces invalid
    /// IL (<c>PathStackDepth</c>, #413). SpillBoxed also registers each value so it survives
    /// the MoveNext re-entry (#400). In non-suspending emitters (ILEmitter) these are plain
    /// locals the JIT collapses away.
    /// </remarks>
    private void EmitRestParameterCall(List<Expr> arguments, int regularCount, ParameterInfo[] targetParams)
    {
        bool hasSpreads = arguments.Any(a => a is Expr.Spread);

        // Spill every argument (spreads spill their inner expression) before touching the stack.
        var argLocals = new LocalBuilder[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
            argLocals[i] = SpillBoxed(arguments[i] is Expr.Spread spread ? spread.Expression : arguments[i]);

        // Load regular arguments (before rest param) from their locals, coercing each boxed
        // object back to the parameter's declared CLR type — free-function params are emitted
        // with their real type (e.g. string, double), so passing a bare object would fail
        // verification (StackUnexpected). The rest List<object> always takes boxed elements.
        for (int i = 0; i < Math.Min(regularCount, arguments.Count); i++)
        {
            IL.Emit(OpCodes.Ldloc, argLocals[i]);
            if (i < targetParams.Length)
                EmitCoerceBoxedToType(targetParams[i].ParameterType);
        }

        // Pad regular args with nulls if needed
        for (int i = arguments.Count; i < regularCount; i++)
            IL.Emit(OpCodes.Ldnull);

        // Build rest parameter array from remaining arguments
        int restArgsCount = Math.Max(0, arguments.Count - regularCount);
        if (hasSpreads && restArgsCount > 0)
        {
            EmitSpreadArrayFromLocals(arguments, argLocals, regularCount, restArgsCount);
            EmitExpandCallArgs();
            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateArray);
        }
        else if (restArgsCount > 0)
        {
            IL.Emit(OpCodes.Ldc_I4, restArgsCount);
            IL.Emit(OpCodes.Newarr, Types.Object);
            for (int i = 0; i < restArgsCount; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldloc, argLocals[regularCount + i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateArray);
        }
        else
        {
            // Empty rest array
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, Types.Object);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateArray);
        }
    }

    /// <summary>
    /// Emits an args array and isSpread bool array from pre-spilled argument locals (a slice of
    /// <paramref name="argLocals"/> starting at <paramref name="offset"/>). Leaves both arrays
    /// on the stack (args array first, then isSpread array on top). Reads from locals rather
    /// than re-emitting expressions so no <c>await</c> can suspend while the arrays are stacked
    /// (#413); the caller is responsible for having spilled the values.
    /// </summary>
    private void EmitSpreadArrayFromLocals(List<Expr> arguments, LocalBuilder[] argLocals, int offset, int count)
    {
        IL.Emit(OpCodes.Ldc_I4, count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, argLocals[offset + i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // isSpread bool array
        IL.Emit(OpCodes.Ldc_I4, count);
        IL.Emit(OpCodes.Newarr, Types.Boolean);
        for (int i = 0; i < count; i++)
        {
            if (arguments[offset + i] is Expr.Spread)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stelem_I1);
            }
        }
    }

    /// <summary>
    /// Coerces the boxed object currently on the stack to <paramref name="targetType"/>:
    /// unbox for value types, downcast for reference types, no-op for object. Used when
    /// loading a previously-spilled (boxed) argument into a typed parameter slot.
    /// </summary>
    private void EmitCoerceBoxedToType(Type targetType)
    {
        if (targetType == typeof(object) || targetType == Types.Object)
            return;
        if (targetType.IsValueType)
            IL.Emit(OpCodes.Unbox_Any, targetType);
        else
            IL.Emit(OpCodes.Castclass, targetType);
    }

    /// <summary>
    /// Emits <paramref name="arg"/>, converts it to <paramref name="paramType"/> (preserving
    /// <see cref="EmitConversionForParameter"/>'s union/implicit-conversion semantics), then boxes
    /// the result and spills it to a <em>registered</em> object local so a suspension (await/yield)
    /// in a <em>later</em> argument persists it across the MoveNext re-entry (#400/#436/#439). Load
    /// it back with <c>Ldloc</c> + <see cref="EmitCoerceBoxedToType"/>(paramType). Value types are
    /// boxed explicitly (not via <c>EnsureBoxed</c>) because the conversion leaves them unboxed with
    /// an untracked stack type after an <c>unbox.any</c>.
    /// </summary>
    private LocalBuilder SpillConvertedArg(Expr arg, Type paramType)
    {
        EmitExpression(arg);
        EmitConversionForParameter(arg, paramType);
        if (paramType.IsValueType)
            IL.Emit(OpCodes.Box, paramType);
        return _helpers.SpillStoreObject();
    }

    /// <summary>
    /// True when evaluating any of <paramref name="exprs"/> can suspend the enclosing state machine
    /// (contains an <c>await</c> or <c>yield</c>). Used to gate await-safe argument spilling: the
    /// fast typed-local call path is only unsafe when a later argument can suspend, so non-suspending
    /// calls (every call in the synchronous ILEmitter, and await-free calls in async/generator
    /// bodies) keep their cheaper codegen (#436).
    /// </summary>
    protected static bool AnyContainsSuspension(IEnumerable<Expr> exprs)
    {
        foreach (var e in exprs)
            if (ExprContainsSuspension(e))
                return true;
        return false;
    }

    /// <summary>
    /// True when evaluating <paramref name="expr"/> can suspend the enclosing state machine. Only
    /// <see cref="Expr.Await"/>/<see cref="Expr.Yield"/> suspend; this walks every composite that
    /// can nest one. Nested function/arrow and class-expression bodies are NOT traversed — their
    /// <c>await</c>/<c>yield</c> belong to their own state machine, not the one being emitted. New
    /// expression containers must be added here (an unhandled type conservatively reports "no
    /// suspension", which would re-expose the spill bug if it can actually nest one).
    /// </summary>
    protected static bool ExprContainsSuspension(Expr expr) => expr switch
    {
        Expr.Await or Expr.Yield => true,
        Expr.Comma c => ExprContainsSuspension(c.Left) || ExprContainsSuspension(c.Right),
        Expr.Binary b => ExprContainsSuspension(b.Left) || ExprContainsSuspension(b.Right),
        Expr.Logical l => ExprContainsSuspension(l.Left) || ExprContainsSuspension(l.Right),
        Expr.NullishCoalescing n => ExprContainsSuspension(n.Left) || ExprContainsSuspension(n.Right),
        Expr.Ternary t => ExprContainsSuspension(t.Condition) || ExprContainsSuspension(t.ThenBranch) || ExprContainsSuspension(t.ElseBranch),
        Expr.Grouping g => ExprContainsSuspension(g.Expression),
        Expr.Unary u => ExprContainsSuspension(u.Right),
        Expr.Delete d => ExprContainsSuspension(d.Operand),
        Expr.Call call => ExprContainsSuspension(call.Callee) || AnyContainsSuspension(call.Arguments),
        Expr.New nw => ExprContainsSuspension(nw.Callee) || AnyContainsSuspension(nw.Arguments),
        Expr.CallPrivate cp => ExprContainsSuspension(cp.Object) || AnyContainsSuspension(cp.Arguments),
        Expr.Get get => ExprContainsSuspension(get.Object),
        Expr.GetPrivate gp => ExprContainsSuspension(gp.Object),
        Expr.Set s => ExprContainsSuspension(s.Object) || ExprContainsSuspension(s.Value),
        Expr.SetPrivate sp => ExprContainsSuspension(sp.Object) || ExprContainsSuspension(sp.Value),
        Expr.GetIndex gi => ExprContainsSuspension(gi.Object) || ExprContainsSuspension(gi.Index),
        Expr.SetIndex si => ExprContainsSuspension(si.Object) || ExprContainsSuspension(si.Index) || ExprContainsSuspension(si.Value),
        Expr.Assign a => ExprContainsSuspension(a.Value),
        Expr.CompoundAssign ca => ExprContainsSuspension(ca.Value),
        Expr.CompoundSet cs => ExprContainsSuspension(cs.Object) || ExprContainsSuspension(cs.Value),
        Expr.CompoundSetIndex csi => ExprContainsSuspension(csi.Object) || ExprContainsSuspension(csi.Index) || ExprContainsSuspension(csi.Value),
        Expr.LogicalAssign la => ExprContainsSuspension(la.Value),
        Expr.LogicalSet lst => ExprContainsSuspension(lst.Object) || ExprContainsSuspension(lst.Value),
        Expr.LogicalSetIndex lsi => ExprContainsSuspension(lsi.Object) || ExprContainsSuspension(lsi.Index) || ExprContainsSuspension(lsi.Value),
        Expr.PrefixIncrement pi => ExprContainsSuspension(pi.Operand),
        Expr.PostfixIncrement pi => ExprContainsSuspension(pi.Operand),
        Expr.ArrayLiteral al => AnyContainsSuspension(al.Elements),
        Expr.ObjectLiteral ol => ol.Properties.Any(p =>
            (p.Key is Expr.ComputedKey ck && ExprContainsSuspension(ck.Expression)) || ExprContainsSuspension(p.Value)),
        Expr.TemplateLiteral tl => AnyContainsSuspension(tl.Expressions),
        Expr.Spread sp => ExprContainsSuspension(sp.Expression),
        Expr.TypeAssertion ta => ExprContainsSuspension(ta.Expression),
        Expr.Satisfies sa => ExprContainsSuspension(sa.Expression),
        Expr.NonNullAssertion nn => ExprContainsSuspension(nn.Expression),
        Expr.DynamicImport di => ExprContainsSuspension(di.PathExpression),
        // Leaves (Literal/Variable/This/Super/ImportMeta/RegexLiteral) and lambda/class
        // boundaries (ArrowFunction/ClassExpr) cannot surface a suspension to the current frame.
        _ => false
    };

    /// <summary>
    /// True when any statement in <paramref name="statements"/> can suspend the enclosing state
    /// machine. Statement analogue of <see cref="AnyContainsSuspension(IEnumerable{Expr})"/>.
    /// </summary>
    protected static bool AnyStmtContainsSuspension(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
            if (StmtContainsSuspension(stmt))
                return true;
        return false;
    }

    /// <summary>
    /// True when executing <paramref name="stmt"/> can suspend the enclosing state machine (contains an
    /// <c>await</c>, a <c>yield</c>, or an implicit <c>for await…of</c> suspension). Every state-machine
    /// family — sync generator, async function/arrow, and async generator — shares this single walker so
    /// the hand-maintained copies can no longer drift apart; that drift repeatedly shipped illegal
    /// <c>BranchIntoTry</c> resume labels (InvalidProgramException) when a suspension inside a <c>try</c>
    /// was under-reported and took the real-IL try path (#631 / #850 / #914). Expression suspension is
    /// delegated to <see cref="ExprContainsSuspension"/> (the paired walker that was unified first); nested
    /// function/arrow/class bodies are not traversed there — their await/yield belong to their own state
    /// machine. A <c>for await…of</c> always suspends on its implicit next()/return() awaits (#631/#697)
    /// even with no explicit await in the iterable or body; a plain generator can never parse a
    /// <c>for await</c>, so its <c>fo.IsAsync</c> arm is vacuously false.
    /// </summary>
    protected static bool StmtContainsSuspension(Stmt stmt) => stmt switch
    {
        Stmt.Expression e => ExprContainsSuspension(e.Expr),
        Stmt.Var v => v.Initializer != null && ExprContainsSuspension(v.Initializer),
        Stmt.Const c => ExprContainsSuspension(c.Initializer),
        Stmt.Return r => r.Value != null && ExprContainsSuspension(r.Value),
        Stmt.If i => ExprContainsSuspension(i.Condition)
            || StmtContainsSuspension(i.ThenBranch)
            || (i.ElseBranch != null && StmtContainsSuspension(i.ElseBranch)),
        Stmt.While w => ExprContainsSuspension(w.Condition) || StmtContainsSuspension(w.Body),
        Stmt.DoWhile dw => StmtContainsSuspension(dw.Body) || ExprContainsSuspension(dw.Condition),
        Stmt.For f => (f.Initializer != null && StmtContainsSuspension(f.Initializer))
            || (f.Condition != null && ExprContainsSuspension(f.Condition))
            || (f.Increment != null && ExprContainsSuspension(f.Increment))
            || StmtContainsSuspension(f.Body),
        Stmt.ForOf fo => fo.IsAsync || ExprContainsSuspension(fo.Iterable) || StmtContainsSuspension(fo.Body),
        Stmt.ForIn fi => ExprContainsSuspension(fi.Object) || StmtContainsSuspension(fi.Body),
        Stmt.Block b => AnyStmtContainsSuspension(b.Statements),
        Stmt.Sequence seq => AnyStmtContainsSuspension(seq.Statements),
        Stmt.LabeledStatement ls => StmtContainsSuspension(ls.Statement),
        Stmt.Switch s => s.Cases.Any(c => ExprContainsSuspension(c.Value) || AnyStmtContainsSuspension(c.Body))
            || (s.DefaultBody != null && AnyStmtContainsSuspension(s.DefaultBody)),
        Stmt.TryCatch t => AnyStmtContainsSuspension(t.TryBlock)
            || (t.CatchBlock != null && AnyStmtContainsSuspension(t.CatchBlock))
            || (t.FinallyBlock != null && AnyStmtContainsSuspension(t.FinallyBlock)),
        Stmt.Throw th => ExprContainsSuspension(th.Value),
        Stmt.Print p => ExprContainsSuspension(p.Expr),
        _ => false
    };

    /// <summary>
    /// Emits the ExpandCallArgs call with Symbol.iterator and runtime type arguments.
    /// Expects args array and isSpread array on the stack.
    /// </summary>
    private void EmitExpandCallArgs()
    {
        IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.SymbolIterator);
        IL.Emit(OpCodes.Ldtoken, Ctx.Runtime!.RuntimeType);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ExpandCallArgs);
    }

    /// <summary>
    /// Evaluates <paramref name="args"/> and leaves a single <c>object[]</c> of the boxed
    /// argument values on the IL stack. No spread expansion — use
    /// <see cref="EmitArgsArrayWithSpread"/> when arguments may contain <see cref="Expr.Spread"/>.
    /// When <paramref name="argLocals"/> is supplied (await-safe pre-spilled args, #850) each
    /// element is loaded from its local rather than re-evaluated — building the array inline
    /// would strand the partly built array on the IL stack across a suspending argument.
    /// </summary>
    public void EmitArgsArray(IReadOnlyList<Expr> args, LocalBuilder[]? argLocals = null)
    {
        IL.Emit(OpCodes.Ldc_I4, args.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < args.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            if (argLocals != null)
            {
                IL.Emit(OpCodes.Ldloc, argLocals[i]);
            }
            else
            {
                EmitExpression(args[i]);
                // Interface dispatch — matches the retired per-file copies, which
                // called through IEmitterContext (ILEmitter's type-aware override
                // vs the base's EnsureBoxed default).
                ((IEmitterContext)this).EmitBoxIfNeeded(args[i]);
            }
            IL.Emit(OpCodes.Stelem_Ref);
        }
    }

    /// <summary>
    /// Evaluates <paramref name="args"/> and leaves a single <c>object[]</c> on the IL stack with
    /// any <see cref="Expr.Spread"/> arguments flattened in place (via the emitted-runtime
    /// <c>ExpandCallArgs</c>, which understands numeric <c>$Array</c> + arbitrary iterables). Each
    /// argument is evaluated and boxed into a temp local <em>before</em> any array is built, so an
    /// <c>await</c>/<c>yield</c> in a later argument never strands a partly-built array on the stack
    /// (#413). Shared by <see cref="EmitFunctionValueCall"/> and the variadic <c>Math.*</c> emitter
    /// so <c>Math.max(...arr)</c> expands spreads identically to <c>f(...arr)</c> (#951).
    /// </summary>
    public void EmitArgsArrayWithSpread(IReadOnlyList<Expr> args)
    {
        // Evaluate all arguments into temps first (await-safe for async emitters)
        List<LocalBuilder> argTemps = [];
        List<bool> isSpread = [];
        foreach (var arg in args)
        {
            if (arg is Expr.Spread spread)
            {
                EmitExpression(spread.Expression);
                EnsureBoxed();
                isSpread.Add(true);
            }
            else
            {
                EmitExpression(arg);
                EnsureBoxed();
                isSpread.Add(false);
            }
            var temp = IL.DeclareLocal(Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Build args array from temps (both paths leave object[] on the stack).
        IL.Emit(OpCodes.Ldc_I4, argTemps.Count);
        IL.Emit(OpCodes.Newarr, Types.Object);
        for (int i = 0; i < argTemps.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, argTemps[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        if (!isSpread.Any(s => s))
            return;

        // Spread case: build the isSpread bool array and flatten via ExpandCallArgs.
        IL.Emit(OpCodes.Ldc_I4, argTemps.Count);
        IL.Emit(OpCodes.Newarr, Types.Boolean);
        for (int i = 0; i < argTemps.Count; i++)
        {
            if (isSpread[i])
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stelem_I1);
            }
        }

        EmitExpandCallArgs();
    }

    /// <summary>
    /// Tries to handle an Expr.Get callee via shared dispatch patterns:
    /// module.promises, class statics, Promise.then/catch/finally, type-first dispatch,
    /// fallback string/array methods, ambiguous methods.
    /// Returns true if handled, false to fall through to emitter-specific instance method dispatch.
    /// ILEmitter calls this before its EmitMethodCall; state machine emitters use it as their
    /// only Expr.Get dispatch (with InvokeMethodValue as the final fallback).
    /// </summary>
    protected bool TryEmitGetCalleeViaBaseClass(Expr.Call c, Expr.Get methodGet)
    {
        // Handler chain: static types, Date.now, built-in modules, process streams,
        // globalThis chaining, imported/class-expr/this statics, etc.
        if (_callHandlers.TryHandle(this, c))
            return true;

        // module.promises.methodName() (fs.promises, dns.promises, stream.promises)
        if (methodGet.Object is Expr.Get promisesGet &&
            promisesGet.Name.Lexeme == "promises" &&
            promisesGet.Object is Expr.Variable promisesModuleVar &&
            Ctx.BuiltInModuleNamespaces != null &&
            Ctx.BuiltInModuleNamespaces.TryGetValue(promisesModuleVar.Name.Lexeme, out var promisesModuleName) &&
            Ctx.BuiltInModuleEmitterRegistry?.GetEmitter(promisesModuleName + "/promises") is { } promisesEmitter)
        {
            if (promisesEmitter.TryEmitMethodCall(this, methodGet.Name.Lexeme, c.Arguments))
            {
                SetStackUnknown();
                return true;
            }
        }

        // Class.staticMethod() with generic class support
        if (methodGet.Object is Expr.Variable classVar &&
            Ctx.Classes.TryGetValue(Ctx.ResolveClassName(classVar.Name.Lexeme), out var classBuilder))
        {
            string resolvedClassName = Ctx.ResolveClassName(classVar.Name.Lexeme);
            if (Ctx.ClassRegistry!.TryGetCallableStaticMethod(resolvedClassName, methodGet.Name.Lexeme, classBuilder, out var callableMethod))
            {
                var staticMethodParams = callableMethod!.GetParameters();
                var paramCount = staticMethodParams.Length;

                // When a later argument can suspend, spill each argument to a registered, boxed
                // object local first — emitting them directly onto the IL stack would leave the
                // earlier args (and partial evaluation) stacked across the await/yield, which is
                // invalid IL and loses values across the MoveNext re-entry (#439). Await-free calls
                // (all of synchronous mode) keep the direct on-stack emission.
                if (AnyContainsSuspension(c.Arguments))
                {
                    var argLocals = new LocalBuilder[c.Arguments.Count];
                    for (int i = 0; i < c.Arguments.Count; i++)
                    {
                        Type pType = i < staticMethodParams.Length ? staticMethodParams[i].ParameterType : Types.Object;
                        argLocals[i] = i < staticMethodParams.Length
                            ? SpillConvertedArg(c.Arguments[i], pType)
                            : SpillBoxed(c.Arguments[i]);
                    }
                    for (int i = 0; i < c.Arguments.Count; i++)
                    {
                        IL.Emit(OpCodes.Ldloc, argLocals[i]);
                        EmitCoerceBoxedToType(i < staticMethodParams.Length ? staticMethodParams[i].ParameterType : Types.Object);
                    }
                }
                else
                {
                    for (int i = 0; i < c.Arguments.Count; i++)
                    {
                        EmitExpression(c.Arguments[i]);
                        if (i < staticMethodParams.Length)
                            EmitConversionForParameter(c.Arguments[i], staticMethodParams[i].ParameterType);
                        else
                            EnsureBoxed();
                    }
                }

                for (int i = c.Arguments.Count; i < paramCount; i++)
                    EmitOmittedArgument(staticMethodParams[i].ParameterType);

                IL.Emit(OpCodes.Call, callableMethod);
                SetStackUnknown();
                return true;
            }

            // Promise subclasses (#242) inherit the Promise static side:
            // emit the base Promise static (leaves Task<object?> on the
            // stack), then construct the subclass around it via its executor
            // constructor — PromiseFromExecutor adopts a raw task, so the
            // result is a subclass-typed promise (NewPromiseCapability-lite).
            if (methodGet.Name.Lexeme is "resolve" or "reject" or "all" or "race" or "allSettled" or "any" or "withResolvers"
                && Ctx.ClassRegistry.IsPromiseSubclass(resolvedClassName)
                && TryEmitDerivedPromiseStatic(resolvedClassName, classBuilder, methodGet.Name.Lexeme, c.Arguments))
            {
                return true;
            }
        }

        // Promise instance methods: promise.then/catch/finally
        string methodName = methodGet.Name.Lexeme;
        if (methodName is "then" or "catch" or "finally")
        {
            EmitPromiseInstanceMethodCall(methodGet.Object, methodName, c.Arguments);
            return true;
        }

        // Direct dispatch for known class instance methods
        if (TryEmitDirectMethodCall(methodGet.Object, methodName, c.Arguments))
            return true;

        // Type-first dispatch via TypeEmitterRegistry
        var objType = Ctx.TypeMap?.Get(methodGet.Object);
        if (objType != null && Ctx.TypeEmitterRegistry != null)
        {
            var strategy = Ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                return true;

            // Union type handling
            if (objType is TypeSystem.TypeInfo.Union union)
            {
                bool hasBufferMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Buffer);
                bool hasStringMember = union.Types.Any(t => t is TypeSystem.TypeInfo.String or TypeSystem.TypeInfo.StringLiteral);
                bool hasArrayMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Array);

                bool isAmbiguousMethod = methodName is "slice" or "concat" or "includes" or "indexOf" or "toString";
                int typesWithMethod = 0;
                if (hasBufferMember && isAmbiguousMethod) typesWithMethod++;
                if (hasStringMember && isAmbiguousMethod) typesWithMethod++;
                if (hasArrayMember && isAmbiguousMethod) typesWithMethod++;

                if (typesWithMethod <= 1)
                {
                    if (hasBufferMember)
                    {
                        var bufferStrategy = Ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Buffer());
                        if (bufferStrategy != null && bufferStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                            return true;
                    }
                    if (hasStringMember)
                    {
                        var stringStrategy = Ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                        if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                            return true;
                    }
                    if (hasArrayMember)
                    {
                        var arrayStrategy = Ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                        if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                            return true;
                    }
                }
            }
        }

        // Fallback: method name-based dispatch for known built-in methods, BUT only when
        // the receiver's static type doesn't already point to a user-defined class. A
        // JS class can legitimately define its own `charAt`/`slice`/etc. (yaml's Lexer
        // does exactly this: `charAt(n) { return this.buffer[this.pos + n]; }`); if we
        // fell through to the string strategy for those, we'd emit `castclass string`
        // on the Lexer instance and blow up at runtime with InvalidCastException.
        // `null`/`Any`/`Unknown` still fall through — those are the genuinely-ambiguous
        // callsites this fallback was added for.
        bool receiverIsUserClassLike = objType is TypeSystem.TypeInfo.Instance
            or TypeSystem.TypeInfo.Class
            or TypeSystem.TypeInfo.GenericClass
            or TypeSystem.TypeInfo.MutableClass
            or TypeSystem.TypeInfo.Interface
            or TypeSystem.TypeInfo.GenericInterface
            or TypeSystem.TypeInfo.Record
            or TypeSystem.TypeInfo.InstantiatedGeneric;

        if (Ctx.TypeEmitterRegistry != null && !receiverIsUserClassLike)
        {
            if (methodName is "charAt" or "substring" or "substr" or "toUpperCase" or "toLowerCase"
                or "trim" or "replace" or "split" or "startsWith" or "endsWith"
                or "repeat" or "padStart" or "padEnd" or "charCodeAt" or "lastIndexOf"
                or "trimStart" or "trimEnd" or "replaceAll" or "at" or "match" or "search")
            {
                // When the receiver's type is statically known to be a String (or a
                // union/literal that resolves to string), use the direct string-strategy
                // path. Otherwise — e.g., objType is null/Any/Unknown, which happens
                // frequently for values flowing through CJS imports — emit a runtime
                // `isinst string` dispatch so a user class with its own `charAt` is
                // routed to dynamic method invocation instead of a cast-to-string crash.
                bool receiverIsKnownString = objType is TypeSystem.TypeInfo.String or TypeSystem.TypeInfo.StringLiteral;
                if (receiverIsKnownString)
                {
                    var stringStrategy = Ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                    if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                        return true;
                }
                else
                {
                    EmitDynamicMethodCallPreservingThis(methodGet.Object, methodName, c.Arguments);
                    return true;
                }
            }

            if (methodName is "pop" or "shift" or "unshift" or "map" or "filter"
                or "push" or "find" or "findIndex" or "some" or "every" or "reduce" or "join"
                or "reverse" or "fill")
            {
                var arrayStrategy = Ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, c.Arguments))
                    return true;
            }
        }

        // Ambiguous methods (slice, concat, includes, indexOf)
        if (methodName is "slice" or "concat" or "includes" or "indexOf")
        {
            EmitAmbiguousMethodCall(methodGet.Object, methodName, c.Arguments);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits a function value call with full spread argument support.
    /// Uses temp locals for await-safety (async emitters may yield during EmitExpression).
    /// </summary>
    private void EmitFunctionValueCall(Expr.Call c)
    {
        LocalBuilder calleeLocal;

        // #923: a direct method-style value-call `o.m(...)` must bind `this` to the receiver. The
        // generic value-call below passes a null receiver (correct for a detached call like `f()`),
        // which drops `this` — wrong for object-literal methods and the lifted generator /
        // async-generator stubs that read `this` via the `_currentFunctionThis` thread-local
        // InvokeWithThis sets (#775). The synchronous ILEmitter handles Expr.Get callees in its own
        // EmitMethodCall, which threads the receiver; this base path is taken by the four
        // state-machine emitters (async fn / async arrow / async gen / sync gen), so they need the
        // same. Restrict to a non-optional Expr.Get whose receiver carries no TypeEmitter strategy —
        // exactly the case EmitGet itself resolves through the plain dynamic GetProperty fallback
        // (ExpressionEmitterBase.EmitGet) — so the resolved callee value is identical to today while
        // the receiver is now preserved. Strategy-resolved callees, detached `f()` (Expr.Variable),
        // and `arr[i]()` (Expr.Index) keep the null-receiver path below.
        LocalBuilder? receiverLocal = null;
        if (c.Callee is Expr.Get methodGet && !HasOptionalLink(methodGet) && !ReceiverHasGetStrategy(methodGet.Object))
        {
            // Evaluate the receiver once, spill it (a MoveNext re-entry from a suspending argument
            // wipes plain IL locals, #400), enforce RequireObjectCoercible before the arguments'
            // side effects fire (ECMA-262 §13.3.6.1, mirroring EmitMethodCall/EmitGet), then resolve
            // the method off the same receiver and pass it through to InvokeMethodValue below.
            EmitExpression(methodGet.Object);
            EnsureBoxed();
            receiverLocal = _helpers.SpillStoreObject();
            EmitThrowIfReceiverUndefined(receiverLocal, methodGet.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Ldstr, methodGet.Name.Lexeme);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            calleeLocal = _helpers.SpillStoreObject();
        }
        else
        {
            // Store callee in temp (await-safe)
            EmitExpression(c.Callee);
            EnsureBoxed();
            calleeLocal = IL.DeclareLocal(Types.Object);
            IL.Emit(OpCodes.Stloc, calleeLocal);
        }

        // Optional link in the callee chain (a?.[0](), (x?.f)()): a nullish
        // callee means the chain short-circuited — yield undefined instead of
        // letting InvokeMethodValue throw (#260). The Expr.Get-callee shape is
        // intercepted earlier by TryEmitOptionalChainMethodCall.
        Label? optChainNullishLabel = null;
        Label? optChainEndLabel = null;
        if (HasOptionalLink(c.Callee))
        {
            optChainNullishLabel = IL.DefineLabel();
            optChainEndLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, calleeLocal);
            IL.Emit(OpCodes.Brfalse, optChainNullishLabel.Value);
            IL.Emit(OpCodes.Ldloc, calleeLocal);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
            IL.Emit(OpCodes.Brtrue, optChainNullishLabel.Value);
        }

        // Evaluate the arguments and leave a (spread-expanded) object[] on the stack.
        EmitArgsArrayWithSpread(c.Arguments);

        // Call InvokeMethodValue(receiver, function, args). The receiver is the spilled `o` for a
        // threaded `o.m()` (#923, above), else null for a detached call.
        var argsLocal = IL.DeclareLocal(Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, argsLocal);
        if (receiverLocal != null)
            IL.Emit(OpCodes.Ldloc, receiverLocal);
        else
            IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldloc, calleeLocal);
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeMethodValue);

        if (optChainNullishLabel != null)
        {
            IL.Emit(OpCodes.Br, optChainEndLabel!.Value);
            IL.MarkLabel(optChainNullishLabel.Value);
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
            IL.MarkLabel(optChainEndLabel.Value);
        }

        SetStackUnknown();
    }

    /// <summary>
    /// True when reading <c>obj.&lt;member&gt;</c> would resolve through a <see cref="TypeEmitterRegistry"/>
    /// strategy (a static-type property getter, e.g. <c>Math.PI</c>, or a type-first instance getter,
    /// e.g. <c>AbortController.signal</c>) rather than the plain dynamic <c>GetProperty</c> fallback.
    /// Mirrors the two strategy checks <see cref="EmitGet"/> performs before that fallback. The #923
    /// receiver-threading in <see cref="EmitFunctionValueCall"/> only fires when this is false, so the
    /// resolved callee value stays identical to today for strategy-backed receivers (which keep the
    /// pre-existing null-receiver behavior) and only changes for genuinely dynamic receivers.
    /// </summary>
    private bool ReceiverHasGetStrategy(Expr obj)
    {
        if (Ctx.TypeEmitterRegistry == null)
            return false;
        if (obj is Expr.Variable v && Ctx.TypeEmitterRegistry.GetStaticStrategy(v.Name.Lexeme) != null)
            return true;
        var objType = Ctx.TypeMap?.Get(obj);
        return objType != null && Ctx.TypeEmitterRegistry.GetStrategy(objType) != null;
    }

    /// <summary>
    /// lodash/core-js global-detection idiom: <c>Function('return this')()</c>.
    /// The Function constructor isn't supported in either mode, but this probe
    /// just means "give me globalThis". Compiled mode represents globalThis in
    /// value position as the runtime sentinel (#271, see EmitVariable), so emit
    /// the same value — packages probing via
    /// <c>freeGlobal || freeSelf || Function('return this')()</c> get a real
    /// root object whose <c>.Object</c>/<c>.Math</c> resolve to real constructors.
    /// </summary>
    protected bool TryEmitFunctionReturnThisIdiom(Expr.Call c)
    {
        if (c.Arguments.Count == 0
            && c.Callee is Expr.Call inner
            && inner.Callee is Expr.Variable { Name.Lexeme: "Function" }
            && inner.Arguments.Count == 1
            && inner.Arguments[0] is Expr.Literal { Value: string body }
            && body.Trim() == "return this")
        {
            IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.GlobalThisSingletonField);
            SetStackUnknown();
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when the expression chain contains an optional link (<c>?.</c>),
    /// mirroring the interpreter's HasOptionalInChain: a call at the end of such
    /// a chain short-circuits to undefined when the callee resolves nullish
    /// (ECMA-262 §13.3 OptionalExpression) instead of throwing.
    /// </summary>
    protected static bool HasOptionalLink(Expr expr) => expr switch
    {
        Expr.Get g => g.Optional || HasOptionalLink(g.Object),
        Expr.GetIndex gi => gi.Optional || HasOptionalLink(gi.Object),
        Expr.Call call => call.Optional || HasOptionalLink(call.Callee),
        Expr.Grouping gr => HasOptionalLink(gr.Expression),
        Expr.TypeAssertion ta => HasOptionalLink(ta.Expression),
        _ => false
    };

    /// <summary>
    /// Intercepts a method call whose callee chain contains an optional link
    /// (<c>a.b?.m(x)</c>, <c>a?.b.m(x)</c>) and emits a receiver-preserving
    /// dynamic dispatch that short-circuits to undefined when the receiver or
    /// resolved method is nullish. Before #260 these shapes leaned on
    /// InvokeMethodValue's silent null for the short-circuit; now that the
    /// fallback throws, the chain semantics must be emitted explicitly.
    /// Returns false when the call doesn't match (callee isn't a Get with an
    /// optional link, or the call itself is optional — EmitOptionalCall owns
    /// <c>?.()</c>).
    /// </summary>
    protected bool TryEmitOptionalChainMethodCall(Expr.Call c)
    {
        if (c.Optional || c.Callee is not Expr.Get g || !HasOptionalLink(g))
            return false;

        // When an argument can suspend, the receiver and resolved fn are live across that
        // suspension, so spill them to *registered* locals (which persist to fields across the
        // MoveNext re-entry, #400) and assemble the args array from pre-spilled locals rather than
        // inline (#439). Synchronous mode never suspends, so SpillStoreObject is a plain local and
        // the inline array build is kept.
        bool awaitSafe = AnyContainsSuspension(c.Arguments);

        EmitExpression(g.Object);
        EnsureBoxed();
        var recvLocal = _helpers.SpillStoreObject();

        var nullishLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Brfalse, nullishLabel);
        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
        IL.Emit(OpCodes.Brtrue, nullishLabel);

        bool stringMethod = IsRuntimeDispatchableStringMethod(g.Name.Lexeme);

        if (stringMethod && awaitSafe)
        {
            // A string-named method with a suspending argument needs the args resolved *after* the
            // short-circuit decision yet emitted exactly once. Handled in its own block (#614/#627).
            EmitOptionalChainStringMethodAwaitSafe(c, g, recvLocal, nullishLabel, endLabel);
        }
        else
        {
            // Pre-spilled, already-boxed argument locals shared by the string fast path and the
            // generic path below. Stays null unless an argument can suspend.
            LocalBuilder[]? argLocals = null;

            // GetProperty can't resolve most string prototype methods (see
            // EmitDynamicMethodCallPreservingThis) — keep its string fast path. Without a suspending
            // argument the args are emitted inline; the two paths are mutually exclusive at runtime,
            // and with no await/yield, duplicating the (side-effect-free-to-the-counter) arg IL in
            // both is harmless. The generic branch's fn-nullish check below still short-circuits
            // before its inline args run, so a missing method never evaluates the args.
            if (stringMethod)
            {
                var notStringLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Ldloc, recvLocal);
                IL.Emit(OpCodes.Isinst, Types.String);
                IL.Emit(OpCodes.Brfalse, notStringLabel);
                EmitRuntimeStringMethod(recvLocal, g.Name.Lexeme, c.Arguments, argLocals);
                IL.Emit(OpCodes.Br, endLabel);
                IL.MarkLabel(notStringLabel);
            }

            // fn = GetProperty(recv, name); nullish fn short-circuits to undefined,
            // matching the interpreter's HasOptionalInChain rule.
            IL.Emit(OpCodes.Ldloc, recvLocal);
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            var fnLocal = _helpers.SpillStoreObject();
            IL.Emit(OpCodes.Ldloc, fnLocal);
            IL.Emit(OpCodes.Brfalse, nullishLabel);
            IL.Emit(OpCodes.Ldloc, fnLocal);
            IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
            IL.Emit(OpCodes.Brtrue, nullishLabel);

            // InvokeMethodValue(recv, fn, args) — args evaluated only on the non-nullish path, per
            // spec. For a non-string method spill the args here, after the method lookup, so an await
            // in a later arg doesn't suspend with the receiver, fn, and the partially-built array all
            // stacked (#439); this preserves the lookup-before-args evaluation order.
            if (awaitSafe)
                argLocals = c.Arguments.Select(SpillBoxed).ToArray();
            IL.Emit(OpCodes.Ldloc, recvLocal);
            IL.Emit(OpCodes.Ldloc, fnLocal);
            EmitBoxedArgsArray(c.Arguments, argLocals);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeMethodValue);
            IL.Emit(OpCodes.Br, endLabel);
        }

        IL.MarkLabel(nullishLabel);
        IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);

        IL.MarkLabel(endLabel);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits the optional-chain dispatch for a string-named method (<c>substring</c>, <c>charAt</c>,
    /// …) when an argument can suspend (<c>await</c>/<c>yield</c>). Two constraints collide here:
    /// <list type="bullet">
    /// <item>The string fast path (<see cref="EmitRuntimeStringMethod"/>, no method-existence check —
    /// the method always exists on a real string) and the generic <c>GetProperty</c>+
    /// <c>InvokeMethodValue</c> path are mutually exclusive at runtime, but the suspending argument
    /// must be emitted <em>exactly once</em>; emitting it in both branches desyncs the await-state
    /// counter and crashes the compiler (#614).</item>
    /// <item>When the receiver is a non-string object that lacks the method, the chain short-circuits
    /// to <c>undefined</c> and the argument must stay <em>unevaluated</em> — matching the interpreter,
    /// which short-circuits before evaluating arguments (#627).</item>
    /// </list>
    /// So the dispatch is decided and the generic fn null-checked <em>before</em> any argument runs
    /// (short-circuiting straight to <paramref name="nullishLabel"/> when the method is missing), then
    /// the argument(s) are evaluated once in a shared block both live dispatches reach. The receiver
    /// kind is re-tested with <c>isinst string</c> after the evaluation rather than stashed in a flag,
    /// because <paramref name="recvLocal"/> persists across the suspension but a plain bool local would
    /// reset on MoveNext re-entry.
    ///
    /// Note: like the rest of <see cref="TryEmitOptionalChainMethodCall"/> this keeps SharpTS's
    /// pre-existing <c>HasOptionalInChain</c> quirk — a missing method on a non-nullish receiver yields
    /// <c>undefined</c> rather than throwing <c>TypeError</c> (ECMA-262 would evaluate the args and
    /// throw). Both modes share that quirk; this only realigns <em>when</em> the args run.
    /// </summary>
    private void EmitOptionalChainStringMethodAwaitSafe(Expr.Call c, Expr.Get g, LocalBuilder recvLocal, Label nullishLabel, Label endLabel)
    {
        var evalArgsLabel = IL.DefineLabel();
        var genericDispatchLabel = IL.DefineLabel();

        // String receiver → the method exists, so skip the lookup and go straight to arg eval.
        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Isinst, Types.String);
        IL.Emit(OpCodes.Brtrue, evalArgsLabel);

        // Non-string receiver: resolve the method generically; a nullish fn short-circuits to
        // undefined WITHOUT evaluating the argument (the parity fix — #627).
        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
        var fnLocal = _helpers.SpillStoreObject();
        IL.Emit(OpCodes.Ldloc, fnLocal);
        IL.Emit(OpCodes.Brfalse, nullishLabel);
        IL.Emit(OpCodes.Ldloc, fnLocal);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
        IL.Emit(OpCodes.Brtrue, nullishLabel);

        // Shared block both live dispatches reach: evaluate the suspending argument(s) exactly once.
        IL.MarkLabel(evalArgsLabel);
        var argLocals = c.Arguments.Select(SpillBoxed).ToArray();

        // Re-test the receiver kind (recvLocal survives the suspension) to pick the dispatch.
        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Isinst, Types.String);
        IL.Emit(OpCodes.Brfalse, genericDispatchLabel);
        EmitRuntimeStringMethod(recvLocal, g.Name.Lexeme, c.Arguments, argLocals);
        IL.Emit(OpCodes.Br, endLabel);

        // Non-string receiver with a real method: InvokeMethodValue(recv, fn, args). fnLocal was
        // stored on the non-string path above (the only way to reach this dispatch at runtime).
        IL.MarkLabel(genericDispatchLabel);
        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Ldloc, fnLocal);
        EmitBoxedArgsArray(c.Arguments, argLocals);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeMethodValue);
        IL.Emit(OpCodes.Br, endLabel);
    }

    /// <summary>
    /// Emits an optional call expression (?.()). Evaluates the callee, checks for
    /// null/undefined, and either short-circuits to undefined or invokes via InvokeMethodValue.
    /// </summary>
    private void EmitOptionalCall(Expr.Call c)
    {
        // Evaluate callee and store in local
        EmitExpression(c.Callee);
        EnsureBoxed();
        var calleeLocal = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, calleeLocal);

        var nullishLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Check for null
        IL.Emit(OpCodes.Ldloc, calleeLocal);
        IL.Emit(OpCodes.Brfalse, nullishLabel);

        // Check for undefined
        IL.Emit(OpCodes.Ldloc, calleeLocal);
        IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
        IL.Emit(OpCodes.Brtrue, nullishLabel);

        // Not nullish — evaluate arguments (await-safe, spreads flattened) and invoke
        EmitArgsArrayWithSpread(c.Arguments);

        // InvokeMethodValue(receiver=null, function, args)
        var argsLocal = IL.DeclareLocal(Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, argsLocal);
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldloc, calleeLocal);
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeMethodValue);
        IL.Emit(OpCodes.Br, endLabel);

        // Nullish path: push undefined
        IL.MarkLabel(nullishLabel);
        IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);

        IL.MarkLabel(endLabel);
        SetStackUnknown();
    }

    #endregion

    #region Call Helpers

    protected bool TryEmitDirectMethodCall(Expr receiver, string methodName, List<Expr> arguments)
    {
        string? simpleClassName = null;

        // Path A: TypeMap resolved the receiver to a class Instance — the normal case for
        // ILEmitter and for user code whose types we fully inferred.
        // Use ResolvedClassType so chained call returns (whose type was captured while the
        // class was still a MutableClass) resolve to the frozen Class here.
        var receiverType = Ctx.TypeMap?.Get(receiver);
        if (receiverType is TypeSystem.TypeInfo.Instance instance)
        {
            simpleClassName = instance.ResolvedClassType switch
            {
                TypeSystem.TypeInfo.Class classType => classType.Name,
                TypeSystem.TypeInfo.MutableClass mc => mc.Name,
                _ => null
            };
        }

        // Path B: `this` inside a state-machine body (generator/async `MoveNext`). The
        // TypeMap entry for the original `Expr.This` node is often null here — the node
        // was type-checked in the class-method body, then re-used verbatim inside the
        // lowered state-machine body where no fresh type-check runs. CurrentClassName
        // tells us which class we're lowering from, which is enough to route the call
        // through the user's own method (e.g. yaml's `*end() { ...; yield* this.pop(); }`
        // sees `pop` on Parser and would otherwise crash — "pop" in the array-method
        // fallback list would send it through ArrayPop with the Parser receiver cast to
        // a null `List<object>`).
        if (simpleClassName is null && receiver is Expr.This && Ctx.CurrentClassName is { } className1)
        {
            simpleClassName = className1;
        }

        if (simpleClassName == null)
            return false;

        string className = Ctx.ResolveClassName(simpleClassName);
        var methodBuilder = Ctx.ResolveInstanceMethod(className, methodName);
        if (methodBuilder == null)
            return false;

        if (!Ctx.Classes.TryGetValue(className, out var classType2))
            return false;

        // Generic classes need instantiated tokens (Stack<!T>), only expressible inside
        // the class's own bodies — never the case for state-machine emitters, where
        // EmittingTypeBuilder is null. Fall back to runtime dispatch there (#178).
        if (!EmitterTypeHelpers.TryResolveInstanceDispatch(
                classType2, methodBuilder, Ctx.EmittingTypeBuilder, out var castType, out var callTarget))
            return false;

        var methodParams = methodBuilder.GetParameters();
        int expectedParamCount = methodParams.Length;

        // Spill the receiver and every argument to object locals, then load from the locals. When
        // any argument can suspend (await/yield), spill through SpillBoxed so the locals are
        // *registered* and persist across the MoveNext re-entry — otherwise an await in a later
        // argument loses the earlier values and crashes (#400/#439). The suspending path spills the
        // receiver first (JS left-to-right: receiver before arguments); the non-suspending path
        // keeps the prior args-then-receiver order so observable side-effect ordering is unchanged.
        LocalBuilder recvLocal;
        var argLocals = new LocalBuilder[arguments.Count];
        if (AnyContainsSuspension(arguments))
        {
            recvLocal = SpillBoxed(receiver);
            for (int i = 0; i < arguments.Count; i++)
                argLocals[i] = SpillBoxed(arguments[i]);
        }
        else
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                EmitExpression(arguments[i]);
                EnsureBoxed();
                var temp = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, temp);
                argLocals[i] = temp;
            }
            EmitExpression(receiver);
            EnsureBoxed();
            recvLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, recvLocal);
        }

        IL.Emit(OpCodes.Ldloc, recvLocal);
        IL.Emit(OpCodes.Castclass, castType);
        // Coerce each boxed argument to its declared parameter slot: unbox value types AND downcast
        // reference types. The previous unbox-only logic left a bare object on the stack for a typed
        // reference parameter (e.g. `string`), which the JIT tolerated but `--verify` rejected with
        // StackUnexpected (#439) — both the await and the plain call shapes.
        for (int i = 0; i < argLocals.Length; i++)
        {
            IL.Emit(OpCodes.Ldloc, argLocals[i]);
            if (i < methodParams.Length)
                EmitCoerceBoxedToType(methodParams[i].ParameterType);
        }

        for (int i = arguments.Count; i < expectedParamCount; i++)
        {
            EmitOmittedArgument(methodParams[i].ParameterType);
        }

        IL.Emit(OpCodes.Callvirt, callTarget);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits a non-virtual call to a base class method for super.method().
    /// Returns false by default (arrow functions don't support super).
    /// Override in emitters that have access to a hoisted 'this' field.
    /// </summary>
    protected virtual bool TryEmitSuperMethodCall(string methodName, List<Expr> arguments)
    {
        var thisField = GetThisField();

        string? superclassName = Ctx.CurrentSuperclassName;

        if (superclassName == null && Ctx.CurrentClassName != null)
        {
            superclassName = Ctx.ClassRegistry?.GetSuperclass(
                Ctx.CurrentClassBuilder?.FullName ?? Ctx.CurrentClassName)
                ?? Ctx.ClassRegistry?.GetSuperclass(Ctx.CurrentClassName);
        }

        if (superclassName == null && Ctx.CurrentClassExpr != null)
        {
            Ctx.ClassExprSuperclass?.TryGetValue(Ctx.CurrentClassExpr, out superclassName);
        }

        if (superclassName == null)
            return false;

        string resolvedSuperName = Ctx.ResolveClassName(superclassName);
        var methodBuilder = Ctx.ResolveInstanceMethod(resolvedSuperName, methodName);
        if (methodBuilder == null)
            return false;

        // Generic superclasses need the member referenced through the instantiated base
        // (e.g. Base<float64>::count) — an open MethodDef token is not executable (#178)
        if (!EmitterTypeHelpers.TryResolveSuperCall(
                Ctx.CurrentClassBuilder, methodBuilder, Ctx.EmittingTypeBuilder, out var superTarget))
            return false;

        var methodParams = methodBuilder.GetParameters();

        List<LocalBuilder> argTemps = [];
        foreach (var arg in arguments)
        {
            EmitExpression(arg);
            EnsureBoxed();
            var temp = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, temp);
            argTemps.Add(temp);
        }

        // Load hoisted 'this' from state machine
        if (thisField != null)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, thisField);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        for (int i = 0; i < argTemps.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, argTemps[i]);
            if (i < methodParams.Length)
            {
                var targetType = methodParams[i].ParameterType;
                if (targetType.IsValueType && targetType != typeof(object))
                {
                    IL.Emit(OpCodes.Unbox_Any, targetType);
                }
            }
        }

        for (int i = arguments.Count; i < methodParams.Length; i++)
        {
            EmitOmittedArgument(methodParams[i].ParameterType);
        }

        IL.Emit(OpCodes.Call, superTarget);
        SetStackUnknown();
        return true;
    }

    #region Global Function Helpers

    protected void EmitGlobalParseInt(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        if (arguments.Count > 1) { EmitExpression(arguments[1]); EnsureBoxed(); } else { IL.Emit(OpCodes.Ldc_I4, 10); IL.Emit(OpCodes.Box, Types.Int32); }
        IL.Emit(OpCodes.Call, Ctx.Runtime!.NumberParseInt);
        IL.Emit(OpCodes.Box, Types.Double);
    }

    protected void EmitGlobalParseFloat(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.NumberParseFloat);
        IL.Emit(OpCodes.Box, Types.Double);
    }

    protected void EmitGlobalIsNaN(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GlobalIsNaN);
        IL.Emit(OpCodes.Box, Types.Boolean);
    }

    protected void EmitGlobalIsFinite(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.GlobalIsFinite);
        IL.Emit(OpCodes.Box, Types.Boolean);
    }

    protected void EmitStructuredClone(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        EmitBoxedArgOrNull(arguments, 1);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.StructuredCloneClone);
    }

    protected void EmitSetTimeout(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        if (arguments.Count > 1) { EmitExpressionAsDouble(arguments[1]); } else { IL.Emit(OpCodes.Ldc_R8, 0.0); }
        EmitTimerArgsArray(arguments, 2);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetTimeout);
        SetStackUnknown();
    }

    protected void EmitClearTimeout(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClearTimeout);
        IL.Emit(OpCodes.Ldnull);
    }

    protected void EmitSetInterval(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        if (arguments.Count > 1) { EmitExpressionAsDouble(arguments[1]); } else { IL.Emit(OpCodes.Ldc_R8, 0.0); }
        EmitTimerArgsArray(arguments, 2);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.SetInterval);
        SetStackUnknown();
    }

    protected void EmitClearInterval(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.ClearInterval);
        IL.Emit(OpCodes.Ldnull);
    }

    protected void EmitQueueMicrotask(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.QueueMicrotask);
        IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
    }

    private void EmitTimerArgsArray(List<Expr> arguments, int startIndex)
    {
        if (arguments.Count > startIndex)
        {
            IL.Emit(OpCodes.Ldc_I4, arguments.Count - startIndex);
            IL.Emit(OpCodes.Newarr, Types.Object);
            for (int i = startIndex; i < arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i - startIndex);
                EmitExpression(arguments[i]);
                EnsureBoxed();
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Newarr, Types.Object);
        }
    }

    protected void EmitSymbolCall(List<Expr> arguments)
    {
        if (arguments.Count == 0) { IL.Emit(OpCodes.Ldnull); }
        else { EmitExpression(arguments[0]); IL.Emit(OpCodes.Call, Ctx.Runtime!.Stringify); }
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSSymbolCtor);
    }

    protected void EmitBigIntCall(List<Expr> arguments)
    {
        if (arguments.Count != 1)
            throw new Diagnostics.Exceptions.CompileException("BigInt() requires exactly one argument.");
        EmitExpression(arguments[0]);
        EnsureBoxed();
        IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateBigInt);
        SetStackUnknown();
    }

    protected void EmitErrorCall(string errorTypeName, List<Expr> arguments)
    {
        IL.Emit(OpCodes.Ldstr, errorTypeName);
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EnsureBoxed();
            IL.Emit(OpCodes.Stelem_Ref);
        }
        IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateError);
    }

    #endregion

    protected void EmitFetchCall(List<Expr> arguments)
    {
        EmitBoxedArgOrNull(arguments, 0);
        EmitBoxedArgOrNull(arguments, 1);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.Fetch);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an inherited Promise static called off a Promise subclass (#242):
    /// `MyPromise.resolve(v)` → base Promise static produces the Task&lt;object?&gt;,
    /// then `newobj MyPromise(task)` — the subclass's executor constructor —
    /// yields the subclass-typed promise (PromiseFromExecutor adopts a raw
    /// task in place of an executor). Returns false (emitting nothing) when
    /// the subclass constructor can't accept the task as its first argument.
    /// </summary>
    protected bool TryEmitDerivedPromiseStatic(string resolvedClassName, Type classBuilder, string methodName, List<Expr> arguments)
    {
        if (Ctx.TypeEmitterRegistry?.GetStaticStrategy("Promise") is not { } promiseStatics)
            return false;

        // withResolvers returns a {promise, resolve, reject} object, not a
        // task — delegate to the base emitter without subclass wrapping.
        if (methodName == "withResolvers")
        {
            if (!promiseStatics.TryEmitStaticCall(this, methodName, arguments))
                return false;
            SetStackUnknown();
            return true;
        }

        var subclassCtor = Ctx.ClassRegistry!.GetConstructorByQualifiedName(resolvedClassName);
        if (subclassCtor == null)
            return false;
        var ctorParams = subclassCtor.GetParameters();
        if (ctorParams.Length == 0 || ctorParams[0].ParameterType != typeof(object))
            return false;

        if (!promiseStatics.TryEmitStaticCall(this, methodName, arguments))
            return false;

        // Stack: Task<object?> — the constructor's first (executor) argument.
        // Generic subclasses (MyPromise<T>) need the constructor token bound
        // to a constructed instantiation; type args are erased to object.
        System.Reflection.ConstructorInfo ctorToCall = subclassCtor;
        if (classBuilder.IsGenericTypeDefinition)
        {
            var typeArgs = classBuilder.GetGenericArguments().Select(_ => typeof(object)).ToArray();
            ctorToCall = TypeBuilder.GetConstructor(classBuilder.MakeGenericType(typeArgs), subclassCtor);
        }

        for (int i = 1; i < ctorParams.Length; i++)
            EmitOmittedArgument(ctorParams[i].ParameterType);

        IL.Emit(OpCodes.Newobj, ctorToCall);
        SetStackUnknown();
        return true;
    }

    protected void EmitPromiseInstanceMethodCall(Expr promise, string methodName, List<Expr> arguments)
    {
        // Receivers may be raw Task<object?> OR $Promise objects (#242 Promise
        // subclasses derive from $Promise) — keep the receiver for derived-result
        // wrapping and unwrap to the task for the PromiseThen/Catch/Finally
        // state machines.
        EmitExpression(promise);
        EnsureBoxed();
        var promiseReceiverLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, promiseReceiverLocal);
        IL.Emit(OpCodes.Ldloc, promiseReceiverLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.UnwrapPromiseReceiverMethod);

        switch (methodName)
        {
            case "then":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.PromiseThen);
                break;
            case "catch":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.PromiseCatch);
                break;
            case "finally":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.PromiseFinally);
                break;
        }

        // Subclass receivers get subclass-typed results (species-lite, #242).
        IL.Emit(OpCodes.Ldloc, promiseReceiverLocal);
        IL.Emit(OpCodes.Call, Ctx.Runtime!.WrapDerivedPromiseResultMethod);
        SetStackUnknown();
    }

    protected void EmitAmbiguousMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        EmitExpression(obj);
        EnsureBoxed();
        var objLocal = IL.DeclareLocal(typeof(object));
        IL.Emit(OpCodes.Stloc, objLocal);

        var isStringLabel = IL.DefineLabel();
        var isListLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, typeof(string));
        IL.Emit(OpCodes.Brtrue, isStringLabel);
        IL.Emit(OpCodes.Br, isListLabel);

        // String path
        IL.MarkLabel(isStringLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, typeof(string));
        switch (methodName)
        {
            case "includes":
                // StringIncludes takes (string, object, object): self,
                // searchString, position. Helper handles IsRegExp / ToJsString
                // / JsToInt32 internally.
                if (arguments.Count > 0) { EmitExpression(arguments[0]); EnsureBoxed(); }
                else { IL.Emit(OpCodes.Ldstr, ""); }
                if (arguments.Count > 1) { EmitExpression(arguments[1]); EnsureBoxed(); }
                else { IL.Emit(OpCodes.Ldnull); }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringIncludes);
                IL.Emit(OpCodes.Box, typeof(bool));
                break;
            case "indexOf":
                // ECMA-262 §22.1.3.8 step 3: searchString = ? ToString(searchString).
                if (arguments.Count > 0) { EmitExpression(arguments[0]); EnsureBoxed(); IL.Emit(OpCodes.Call, Ctx.Runtime!.ToJsString); }
                else { IL.Emit(OpCodes.Ldstr, "undefined"); }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringIndexOf);
                IL.Emit(OpCodes.Box, typeof(double));
                break;
            case "lastIndexOf":
                if (arguments.Count > 0) { EmitExpression(arguments[0]); EnsureBoxed(); IL.Emit(OpCodes.Call, Ctx.Runtime!.ToJsString); }
                else { IL.Emit(OpCodes.Ldstr, "undefined"); }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringLastIndexOf);
                IL.Emit(OpCodes.Box, typeof(double));
                break;
            case "slice":
                // StringSlice(string str, object[] args). argCount derived from args.Length.
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]); EnsureBoxed();
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringSlice);
                break;
            case "concat":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]); EnsureBoxed();
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StringConcat);
                break;
        }
        IL.Emit(OpCodes.Br, doneLabel);

        // List path
        IL.MarkLabel(isListLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, typeof(List<object>));
        switch (methodName)
        {
            case "includes":
                EmitBoxedArgOrNull(arguments, 0);
                // ArrayIncludes already returns a boxed bool — do not re-box
                // (double-boxing reinterprets the object reference as a bool).
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ArrayIncludes);
                break;
            case "indexOf":
            case "lastIndexOf":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Call, methodName == "indexOf" ? Ctx.Runtime!.ArrayIndexOf : Ctx.Runtime!.ArrayLastIndexOf);
                IL.Emit(OpCodes.Box, typeof(double));
                break;
            case "slice":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]); EnsureBoxed();
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ArraySlice);
                break;
            case "concat":
                // ECMA-262: concat(...items) is variadic. Pass args as object[]
                // so each argument spreads (Array/List) or appends individually.
                // A `...spread` arg is flattened first (#952) so concat sees the
                // expanded elements, not the source array as one nested item.
                EmitArgsArrayWithSpread(arguments);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ArrayConcat);
                break;
        }

        IL.MarkLabel(doneLabel);
        SetStackUnknown();
    }

    protected void EmitDefaultForType(Type type)
    {
        if (type == typeof(double)) { IL.Emit(OpCodes.Ldc_R8, 0.0); }
        else if (type == typeof(int)) { IL.Emit(OpCodes.Ldc_I4_0); }
        else if (type == typeof(bool)) { IL.Emit(OpCodes.Ldc_I4_0); }
        else if (type == typeof(float)) { IL.Emit(OpCodes.Ldc_R4, 0.0f); }
        else if (type == typeof(long)) { IL.Emit(OpCodes.Ldc_I8, 0L); }
        else if (type.IsValueType)
        {
            var local = IL.DeclareLocal(type);
            IL.Emit(OpCodes.Ldloca, local);
            IL.Emit(OpCodes.Initobj, type);
            IL.Emit(OpCodes.Ldloc, local);
        }
        else { IL.Emit(OpCodes.Ldnull); }
    }

    /// <summary>
    /// Emits the value for a trailing parameter slot that the call site omits. JS semantics: an
    /// omitted argument is <c>undefined</c>, so an <c>object</c> slot — which every optional or
    /// value-type-defaulted parameter uses after widening (#705/#739) — is padded with the
    /// <c>$Undefined</c> sentinel. That is observable through <c>typeof</c>/<c>=== undefined</c> for a
    /// plain optional (#739) and fires the default-parameter prologue for a defaulted one
    /// (#705/#723/#737). A non-<c>object</c> slot (a required value-type slot, or a not-widened
    /// reference-typed default such as <c>string</c>) takes its CLR default — for a defaulted
    /// reference slot the prologue still fires on the resulting null. Mirrors the existing
    /// $TSFunction.Invoke value-call padding and <see cref="EmitPrivateCallUndefinedPadding"/>.
    /// Public (unlike the sibling <see cref="EmitDefaultForType"/>) so the ILCompiler can pad an
    /// implicit base-constructor call directly; also satisfies <see cref="IEmitterContext"/>.
    /// </summary>
    public void EmitOmittedArgument(Type slotType)
    {
        if (slotType == Types.Object)
            EmitUndefinedConstant();
        else
            EmitDefaultForType(slotType);
    }

    /// <summary>
    /// Emits conversion from the current stack value to the target parameter type.
    /// Handles boxing for object, unboxing for value types, union types, and pass-through for matching types.
    /// </summary>
    protected void EmitConversionForParameter(Expr expr, Type targetType)
    {
        // If target is object, box value types
        if (targetType == typeof(object))
        {
            EnsureBoxed();
            return;
        }

        // Check if the expression produces a matching type
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double && targetType == typeof(double))
                return;
            if (lit.Value is bool && targetType == typeof(bool))
                return;
            if (lit.Value is string && targetType == typeof(string))
                return;
        }

        // If stack has a known type matching target, no conversion needed
        if (StackType == StackType.Double && targetType == typeof(double))
            return;
        if (StackType == StackType.Boolean && targetType == typeof(bool))
            return;

        // Check if target is a union type using marker interface
        if (UnionTypeHelper.IsUnionType(targetType))
        {
            // Determine source type from stack or expression
            Type? sourceType = StackType switch
            {
                StackType.Double => typeof(double),
                StackType.Boolean => typeof(bool),
                StackType.String => typeof(string),
                _ => null
            };

            if (sourceType == null && expr is Expr.Literal exprLit)
            {
                sourceType = exprLit.Value switch
                {
                    double => typeof(double),
                    string => typeof(string),
                    bool => typeof(bool),
                    _ => null
                };
            }

            if (sourceType != null && Ctx.UnionGenerator != null)
            {
                var implicitOp = Ctx.UnionGenerator.GetImplicitConversion(targetType, sourceType);
                if (implicitOp != null)
                {
                    IL.Emit(OpCodes.Call, implicitOp);
                    return;
                }
            }

            // Fallback: box and create default union
            EnsureBoxed();
            var valueLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, valueLocal);
            var unionLocal = IL.DeclareLocal(targetType);
            IL.Emit(OpCodes.Ldloca, unionLocal);
            IL.Emit(OpCodes.Initobj, targetType);
            IL.Emit(OpCodes.Ldloc, unionLocal);
            return;
        }

        // If target is a value type and we have an object, unbox
        if (targetType.IsValueType)
        {
            if (StackType != StackType.Double && StackType != StackType.Boolean)
                IL.Emit(OpCodes.Unbox_Any, targetType);
            return;
        }

        // Target is a non-object reference type. EmitExpression can leave a broader `object` on the
        // stack — an `any`-typed value, or a GetIndex/GetProperty result — that the callee's typed
        // string slot would reject at IL verification even though the JIT tolerates it. Emit the
        // downcast the verifier needs (#1246). Only `string` qualifies: it is the one non-object
        // reference slot whose runtime value is genuinely a CLR instance of the slot. Other reference
        // targets carry a boxed-differently runtime value ($TSFunction for a Delegate, $Array for a
        // List, $Promise for a Task — the last widened to `object` at the parameter slot by
        // ParameterTypeResolver), so a castclass would throw; they keep their prior object store.
        if (Types.IsString(targetType) && StackType != StackType.String)
        {
            EnsureBoxed();
            IL.Emit(OpCodes.Castclass, targetType);
        }
    }

    /// <summary>
    /// Handles return value from a direct function call — boxes value types, pushes null for void.
    /// </summary>
    protected void BoxReturnValueIfNeeded(Type returnType)
    {
        if (returnType == typeof(void))
        {
            // A void-returning method used in a value context yields `undefined` (ECMA-262),
            // not C# null (= JS null): a TS `void` function still produces `undefined` when its
            // result is read — e.g. an off-the-end function compiled to a void slot. The helper
            // pushes the $Undefined sentinel (with a null fallback for standalone) and sets the
            // stack type to Unknown. #563
            EmitUndefinedConstant();
        }
        else if (Types.IsDouble(returnType))
        {
            SetStackType(StackType.Double);
        }
        else if (Types.IsBoolean(returnType))
        {
            SetStackType(StackType.Boolean);
        }
        else if (returnType.IsValueType)
        {
            IL.Emit(OpCodes.Box, returnType);
            SetStackUnknown();
        }
        else if (Types.IsString(returnType))
        {
            StackType = StackType.String;
        }
        else
        {
            SetStackUnknown();
        }
    }

    /// <summary>
    /// Emits a super() constructor call in a derived class.
    /// </summary>
    protected void EmitSuperConstructorCall(ConstructorBuilder parentCtor, List<Expr> arguments)
    {
        IL.Emit(OpCodes.Ldarg_0);

        var ctorParams = parentCtor.GetParameters();
        for (int i = 0; i < arguments.Count; i++)
        {
            EmitExpression(arguments[i]);
            if (i < ctorParams.Length)
                EmitConversionForParameter(arguments[i], ctorParams[i].ParameterType);
            else
                EnsureBoxed();
        }

        for (int i = arguments.Count; i < ctorParams.Length; i++)
            EmitOmittedArgument(ctorParams[i].ParameterType);

        ConstructorInfo ctorToCall = parentCtor;
        Type? baseType = Ctx.CurrentClassBuilder?.BaseType;
        if (baseType != null && baseType.IsGenericType && baseType.IsConstructedGenericType)
            ctorToCall = EmitterTypeHelpers.ResolveConstructor(baseType, parentCtor);

        IL.Emit(OpCodes.Call, ctorToCall);
        IL.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a call to an async function, synchronously awaiting the result.
    /// </summary>
    protected void EmitAsyncFunctionCall(System.Reflection.MethodInfo asyncMethod, List<Expr> arguments)
    {
        var asyncMethodParams = asyncMethod.GetParameters();
        var paramCount = asyncMethodParams.Length;

        for (int i = 0; i < arguments.Count; i++)
        {
            EmitExpression(arguments[i]);
            if (i < asyncMethodParams.Length)
                EmitConversionForParameter(arguments[i], asyncMethodParams[i].ParameterType);
            else
                EnsureBoxed();
        }

        for (int i = arguments.Count; i < paramCount; i++)
            EmitOmittedArgument(asyncMethodParams[i].ParameterType);

        IL.Emit(OpCodes.Call, asyncMethod);

        Type returnType = asyncMethod.ReturnType;
        if (returnType == Types.Task || returnType.FullName == "System.Threading.Tasks.Task")
        {
            var getAwaiter = Types.GetMethod(Types.Task, "GetAwaiter");
            var awaiterType = Types.TaskAwaiter;
            var getResult = Types.GetMethod(awaiterType, "GetResult");

            var taskLocal = IL.DeclareLocal(Types.Task);
            IL.Emit(OpCodes.Stloc, taskLocal);
            IL.Emit(OpCodes.Ldloca, taskLocal);
            IL.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = IL.DeclareLocal(awaiterType);
            IL.Emit(OpCodes.Stloc, awaiterLocal);
            IL.Emit(OpCodes.Ldloca, awaiterLocal);
            IL.Emit(OpCodes.Call, getResult);

            IL.Emit(OpCodes.Ldnull);
        }
        else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1")
        {
            var getAwaiter = returnType.GetMethod("GetAwaiter")!;
            var awaiterType = getAwaiter.ReturnType;
            var getResult = awaiterType.GetMethod("GetResult")!;

            var taskLocal = IL.DeclareLocal(returnType);
            IL.Emit(OpCodes.Stloc, taskLocal);
            IL.Emit(OpCodes.Ldloca, taskLocal);
            IL.Emit(OpCodes.Call, getAwaiter);

            var awaiterLocal = IL.DeclareLocal(awaiterType);
            IL.Emit(OpCodes.Stloc, awaiterLocal);
            IL.Emit(OpCodes.Ldloca, awaiterLocal);
            IL.Emit(OpCodes.Call, getResult);

            Type resultType = returnType.GetGenericArguments()[0];
            if (resultType.IsValueType)
                IL.Emit(OpCodes.Box, resultType);
        }
    }

    /// <summary>
    /// Emits a direct call to an inner function method (recursive or sibling).
    /// </summary>
    protected void EmitInnerFunctionDirectCall(string funcName, MethodBuilder innerMethod, List<Expr> arguments)
    {
        bool isCapturing = Ctx.InnerFunctionDisplayClassesByName?.ContainsKey(funcName) == true;

        if (isCapturing)
            IL.Emit(OpCodes.Ldarg_0);

        var parameters = innerMethod.GetParameters();
        for (int i = 0; i < arguments.Count; i++)
        {
            EmitExpression(arguments[i]);
            if (i < parameters.Length)
                EmitConversionForParameter(arguments[i], parameters[i].ParameterType);
            else
                EnsureBoxed();
        }

        for (int i = arguments.Count; i < parameters.Length; i++)
            EmitOmittedArgument(parameters[i].ParameterType);

        IL.Emit(isCapturing ? OpCodes.Callvirt : OpCodes.Call, innerMethod);
        SetStackUnknown();
    }

    /// <summary>
    /// Tries to emit process.stdin.read(), process.stdout.write(), process.stderr.write().
    /// </summary>
    protected bool TryEmitProcessStreamCall(Expr.Call c)
    {
        if (c.Callee is not Expr.Get methodGet)
            return false;
        if (methodGet.Object is not Expr.Get streamGet)
            return false;
        if (streamGet.Object is not Expr.Variable processVar || processVar.Name.Lexeme != "process")
            return false;

        string streamName = streamGet.Name.Lexeme;
        string methodName = methodGet.Name.Lexeme;

        switch (streamName)
        {
            case "stdin" when methodName == "read":
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StdinRead);
                SetStackUnknown();
                return true;

            case "stdout" when methodName == "write":
                if (c.Arguments.Count > 0) { EmitExpression(c.Arguments[0]); EnsureBoxed(); }
                else { IL.Emit(OpCodes.Ldstr, ""); }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StdoutWrite);
                SetStackUnknown();
                return true;

            case "stderr" when methodName == "write":
                if (c.Arguments.Count > 0) { EmitExpression(c.Arguments[0]); EnsureBoxed(); }
                else { IL.Emit(OpCodes.Ldstr, ""); }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.StderrWrite);
                SetStackUnknown();
                return true;

            default:
                return false;
        }
    }

    #endregion
}
