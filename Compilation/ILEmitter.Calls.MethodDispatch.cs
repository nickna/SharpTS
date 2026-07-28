using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Instance method routing and dispatch for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitMethodCall(
        Expr.Get methodGet,
        List<Expr> arguments,
        List<string>? genericTypeArguments = null,
        TypeSystem.TypeInfo? contextualResultType = null)
    {
        string methodName = methodGet.Name.Lexeme;

        // Promoted typed-array local push (#857/#860): append unboxed elements directly to the
        // bare List<T> via the typed helper — bypassing ArrayEmitter's $Array unwrap/copy and the
        // per-element boxing. Handled here (not in ArrayEmitter) because EnsureDouble/EnsureBoolean
        // live on the main emitter. Only fires when the receiver is a promoted local (typed slot).
        if (methodName == "push" && !methodGet.Optional && methodGet.Object is Expr.Variable pushVar
            && _ctx.TryGetPromotedArrayLocal(pushVar.Name.Lexeme) is { } pushProm)
        {
            EmitPromotedArrayPush(pushProm.Local, pushProm.Descriptor, arguments);
            return;
        }

        // Escaping number[] push (number[] unboxing): the receiver is a statically `number[]`
        // expression that is NOT a promoted local — so its runtime value is a $Array (numeric or boxed,
        // never List<double>: the promoted-local branch above + the escape analyzer guarantee that).
        // Append each UNBOXED double via $Array.PushDouble (mode-checked: a numeric array appends into its
        // double[] store with no boxing and STAYS numeric; a boxed array delegates to the boxed Set). The
        // generic ArrayEmitter path would instead unwrap the array (EmitGetListFromArrayOrList), deopting a
        // numeric receiver before the append. Gated so numeric == boxed: every arg must be statically
        // `number` (EmitExpressionAsDouble coerces; an any/undefined-admitting arg would store a coerced
        // double where the boxed path preserves the value/undefined sentinel). Sync-only path (ILEmitter):
        // a push arg cannot contain await/yield here, so the $Array ref on the stack during arg evaluation
        // never strands across a suspension (cf. the promoted-local push, which likewise has no guard).
        // push() with no args falls through to the generic path.
        if (methodName == "push" && !methodGet.Optional && arguments.Count > 0
            && ArrayElements.Resolve(_ctx.TypeMap?.Get(methodGet.Object)) is { Kind: ArrayElementsKind.Double }
            && arguments.All(a => IsNumericType(_ctx.TypeMap?.Get(a))))
        {
            EmitEscapingNumberArrayPush(methodGet.Object, arguments);
            return;
        }

        // Promoted string-accumulator charCodeAt (#857): read the StringBuilder's UTF-16 code unit
        // directly via its [int] indexer (== JS charCodeAt); out-of-range yields NaN (JS semantics).
        // Must intercept before the string-method path, which would emit the slot as a string receiver.
        if (methodName == "charCodeAt" && !methodGet.Optional && methodGet.Object is Expr.Variable ccVar
            && _ctx.TryGetPromotedStringAccumulator(ccVar.Name.Lexeme) is { } ccSb)
        {
            EmitPromotedStringCharCodeAt(ccSb, arguments);
            return;
        }

        // Statically-typed string receiver charCodeAt (lexer / interpreter hot path, e.g. brainfuck's
        // `program.charCodeAt(ip)`): the receiver is known to be a CLR string, so call StringCharCodeAt
        // directly with the index passed UNBOXED via EmitExpressionAsDouble (proper ToNumber, no
        // box→ToNumber round-trip) and leave the result as a raw double (SetStackType) instead of boxing.
        // The common numeric consumer (`=== 43`, `sum + …`) then pays no box/unbox; a boxed consumer
        // re-boxes lazily via EmitBoxIfNeeded. Mirrors the #859 promoted-accumulator path for plain
        // strings; the any-typed receiver still routes through the boxed EmitStringOnlyMethodCall path.
        if (methodName == "charCodeAt" && !methodGet.Optional && arguments.Count <= 1
            && _ctx.TypeMap?.Get(methodGet.Object) is TypeSystem.TypeInfo.String)
        {
            EmitExpression(methodGet.Object);
            EmitBoxIfNeeded(methodGet.Object);
            IL.Emit(OpCodes.Castclass, _ctx.Types.String);
            if (arguments.Count > 0) EmitExpressionAsDouble(arguments[0]);
            else IL.Emit(OpCodes.Ldc_R8, 0.0);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.StringCharCodeAt);
            SetStackType(StackType.Double);
            return;
        }

        // Promoted number[] local reduce (#861 typed-HOF pipeline): `arr.reduce((a,x)=>…, init)` over a
        // List<double> with a non-capturing double(double,double) reducer → bind the arrow's typed static
        // method DIRECTLY to Func<double,double,double> (no boxed adapter) and drive ArrayReduceDouble —
        // zero per-element boxing. The analyzer gates promotion on the SAME criteria, so a promoted
        // List<double> reduced here is always typeable. Other reduce shapes never promote (stay $Array).
        if (methodName == "reduce" && !methodGet.Optional && methodGet.Object is Expr.Variable redVar
            && _ctx.TryGetPromotedArrayLocal(redVar.Name.Lexeme) is { Descriptor.Kind: ArrayElementsKind.Double } redProm
            && arguments.Count == 2 && arguments[0] is Expr.ArrowFunction redArrow
            && !_ctx.DisplayClasses.ContainsKey(redArrow)
            && _ctx.ArrowMethods.TryGetValue(redArrow, out var redMethod)
            && redMethod.ReturnType == _ctx.Types.Double
            && redMethod.GetParameters() is { Length: 2 } redPs
            && redPs[0].ParameterType == _ctx.Types.Double && redPs[1].ParameterType == _ctx.Types.Double)
        {
            var func3 = typeof(Func<double, double, double>);
            IL.Emit(OpCodes.Ldloc, redProm.Local);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Ldftn, redMethod);
            IL.Emit(OpCodes.Newobj, func3.GetConstructor([typeof(object), typeof(IntPtr)])!);
            EmitExpressionAsDouble(arguments[1]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ArrayReduceDouble);
            SetStackType(StackType.Double);
            return;
        }

        // Try direct dispatch for known class instance methods
        TypeSystem.TypeInfo? objType = _ctx.TypeMap?.Get(methodGet.Object);
        if (TryEmitDirectMethodCall(
                methodGet.Object, objType, methodName, arguments,
                genericTypeArguments, contextualResultType))
            return;

        // Timeout instance methods: ref(), unref()
        if (objType is TypeSystem.TypeInfo.Timeout && methodName is "ref" or "unref")
        {
            EmitExpression(methodGet.Object);
            EmitBoxIfNeeded(methodGet.Object);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSTimeoutType);
            IL.Emit(OpCodes.Callvirt, methodName == "ref" ? _ctx.Runtime!.TSTimeoutRef : _ctx.Runtime!.TSTimeoutUnref);
            SetStackUnknown();
            return;
        }

        // Promise instance methods: then(), catch(), finally()
        // These work on Task<object?> returned by async functions
        if (methodName is "then" or "catch" or "finally")
        {
            // Check if we know it's a Promise at compile time
            if (objType is TypeSystem.TypeInfo.Promise)
            {
                EmitPromiseInstanceMethodCall(methodGet.Object, methodName, arguments);
                return;
            }
        }

        // Type-first dispatch: Use TypeEmitterRegistry if we have type information
        if (objType != null && _ctx.TypeEmitterRegistry != null)
        {
            var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                return;

            // Handle union types - try emitters for member types
            // BUT for methods that exist on multiple types, use runtime dispatch instead
            if (objType is TypeSystem.TypeInfo.Union union)
            {
                bool hasBufferMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Buffer);
                bool hasStringMember = union.Types.Any(t => t is TypeSystem.TypeInfo.String or TypeSystem.TypeInfo.StringLiteral);
                bool hasArrayMember = union.Types.Any(t => t is TypeSystem.TypeInfo.Array);

                // Count how many types in the union could have this method
                // Methods like includes, indexOf, slice exist on multiple types
                bool isAmbiguousMethod = methodName is "slice" or "concat" or "includes" or "indexOf"
                    or "toString" or "valueOf";
                int typesWithMethod = 0;
                if (hasBufferMember && isAmbiguousMethod) typesWithMethod++;
                if (hasStringMember && isAmbiguousMethod) typesWithMethod++;
                if (hasArrayMember && isAmbiguousMethod) typesWithMethod++;

                // If multiple types could have this method, skip type-specific emitters
                // and let runtime dispatch handle it below
                if (typesWithMethod <= 1)
                {
                    // Try buffer emitter if union contains buffer
                    if (hasBufferMember)
                    {
                        var bufferStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Buffer());
                        if (bufferStrategy != null && bufferStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                            return;
                    }

                    // Try string emitter if union contains string
                    if (hasStringMember)
                    {
                        var stringStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.String());
                        if (stringStrategy != null && stringStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                            return;
                    }

                    // Try array emitter if union contains array
                    if (hasArrayMember)
                    {
                        var arrayStrategy = _ctx.TypeEmitterRegistry.GetStrategy(new TypeSystem.TypeInfo.Array(new TypeSystem.TypeInfo.Any()));
                        if (arrayStrategy != null && arrayStrategy.TryEmitMethodCall(this, methodGet.Object, methodName, arguments))
                            return;
                    }
                }
            }
        }

        // Methods that exist on both strings and arrays - runtime dispatch for any/unknown/union types
        // Note: String and Array types are handled by TypeEmitterRegistry above
        // startsWith/endsWith are string-only but included here for Any-typed values
        // substring/charAt are string-only but need runtime dispatch for union types (string | undefined)
        if (methodName is "slice" or "concat" or "includes" or "indexOf" or "startsWith" or "endsWith"
            or "substring" or "charAt")
        {
            EmitAmbiguousMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // BigInt instance methods. bigint is a primitive (no boxed wrapper object),
        // so route toString/valueOf/toLocaleString to dedicated runtime helpers —
        // critically so `toString(radix)` honors the radix instead of falling through
        // to BigInteger.ToString() (which ignores the argument).
        if (objType is TypeSystem.TypeInfo.BigInt or TypeSystem.TypeInfo.BigIntLiteral && methodName is "toString" or "toLocaleString" or "valueOf")
        {
            EmitBigIntMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Number instance methods - runtime dispatch for any/unknown types
        if (methodName is "toFixed" or "toPrecision" or "toExponential" or "valueOf" or "toString")
        {
            // Check if we know it's a number at compile time
            if (objType is TypeSystem.TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or TypeSystem.TypeInfo.NumberLiteral)
            {
                EmitNumberMethodCallDirect(methodGet.Object, methodName, arguments);
                return;
            }
            // For unknown types, use runtime dispatch
            EmitNumberMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Note: Date, Map, Set, WeakMap, WeakSet, RegExp methods are handled by TypeEmitterRegistry above
        // For Any types, we need runtime dispatch for Map methods since TypeEmitterRegistry won't catch them
        if (methodName is "get" or "set" or "has" or "delete" or "clear" or "keys" or "values" or "entries" or "forEach")
        {
            EmitMapMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // String-only methods - runtime dispatch for any/unknown types
        // When type is known as string, TypeEmitterRegistry's StringEmitter handles these
        // For Any types, we need to check at runtime if the receiver is a string
        if (methodName is "padEnd" or "padStart" or "trim" or "trimStart" or "trimEnd"
            or "toUpperCase" or "toLowerCase" or "replace" or "replaceAll" or "split"
            or "match" or "search" or "repeat" or "charCodeAt" or "at" or "lastIndexOf"
            or "normalize" or "localeCompare")
        {
            EmitStringOnlyMethodCall(methodGet.Object, methodName, arguments);
            return;
        }

        // Iterator protocol (.next() / .return() / .throw()) for any/unknown receivers.
        // Array .values()/.keys()/.entries() return a bare IEnumerator<object>
        // that has no JS-shaped next/value/done members, so the generic
        // GetProperty+InvokeMethodValue fallback below would resolve `next` to
        // undefined and yield null. IteratorProtocolCall synthesizes the result
        // object for enumerator receivers and falls back to the normal dynamic
        // dispatch for everything else (generators, user iterators). Routing
        // `throw` here too gives a bare it.throw() the same undefined default as
        // next/return for generator receivers (#619), instead of the null the
        // generic path would pad. Typed iterators are already handled by the
        // type-first dispatch above, so only any/unknown receivers reach here.
        if (methodName is "next" or "return" or "throw")
        {
            EmitExpression(methodGet.Object);
            EmitBoxIfNeeded(methodGet.Object);
            IL.Emit(OpCodes.Ldstr, methodName);
            IL.Emit(OpCodes.Ldc_I4, arguments.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
            for (int i = 0; i < arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(arguments[i]);
                EmitBoxIfNeeded(arguments[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            IL.Emit(OpCodes.Call, _ctx.Runtime!.IteratorProtocolCall);
            SetStackUnknown();
            return;
        }

        // For object method calls, we need to pass the receiver as 'this'
        // Stack order: receiver, function, args

        // Emit receiver object once and store in a local to avoid double evaluation
        EmitExpression(methodGet.Object);
        EmitBoxIfNeeded(methodGet.Object);
        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, receiverLocal);

        // ECMA-262 §13.3.2.1 / §13.3.6.1: the member-access callee `recv.method`
        // runs RequireObjectCoercible(recv) before ArgumentListEvaluation, so an
        // undefined receiver throws TypeError *before* the arguments' side effects
        // fire. GetProperty/InvokeMethodValue would otherwise defer the throw until
        // after the args are built. (test262 .../call/11.2.3-3_3)
        if (!methodGet.Optional)
            EmitThrowIfReceiverUndefined(receiverLocal, methodName);

        // Load receiver for InvokeMethodValue's first argument
        IL.Emit(OpCodes.Ldloc, receiverLocal);

        // Get the method/function value from the object using same receiver
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Ldstr, methodName);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

        // Create args array. A `...spread` argument has to be flattened into the array before
        // dispatch — otherwise the callee sees the iterable itself as one argument, which then
        // silently lands in a rest parameter as a single nested element (#1282). The pooled
        // fast path below can't serve that case (the expanded length isn't known statically and
        // ExpandCallArgs allocates its own array), so spreads take the shared helper instead.
        if (arguments.Any(a => a is Expr.Spread))
        {
            EmitArgsArrayWithSpread(arguments);
        }
        else
        {
            // For arity > 0, route through the per-thread $CallArgsPool to skip per-call
            // newarr — the dispatch chain (InvokeMethodValue → $TSFunction.Invoke →
            // MethodInvoker.Invoke) reads values out of the array without retaining a
            // reference, so cross-call reuse on the same thread is sound.
            if (arguments.Count == 0)
            {
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
            }
            else
            {
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CallArgsPoolGet);
            }

            for (int i = 0; i < arguments.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(arguments[i]);
                EmitBoxIfNeeded(arguments[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }

        // Call InvokeMethodValue(receiver, function, args) to bind 'this'
        IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeMethodValue);
    }

    /// <summary>
    /// Try to emit a direct method call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectMethodCall(Expr receiver, TypeSystem.TypeInfo? receiverType,
        string methodName, List<Expr> arguments, List<string>? genericTypeArguments,
        TypeSystem.TypeInfo? contextualResultType)
    {
        // Only handle Instance types (e.g., let p: Person = ...)
        if (receiverType is not TypeSystem.TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type.
        // Use ResolvedClassType so chained expressions like sb.append(...).append(...)
        // — whose return type was captured during signature collection while the class
        // was still a MutableClass — resolve to the frozen Class here.
        string? simpleClassName = instance.ResolvedClassType switch
        {
            TypeSystem.TypeInfo.Class c => c.Name,
            TypeSystem.TypeInfo.MutableClass mc => mc.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Check if this is an external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(simpleClassName, out var externalType))
        {
            EmitExternalInstanceMethodCall(
                receiver, externalType, methodName, arguments,
                genericTypeArguments, contextualResultType);
            return true;
        }

        // Resolve to qualified name for multi-module compilation
        string className = _ctx.ResolveClassName(simpleClassName);

        // Also check if the qualified name is an external type
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out externalType))
        {
            EmitExternalInstanceMethodCall(
                receiver, externalType, methodName, arguments,
                genericTypeArguments, contextualResultType);
            return true;
        }

        // Look up the method in the class hierarchy
        var methodBuilder = _ctx.ResolveInstanceMethod(className, methodName);
        if (methodBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Generic classes need instantiated tokens (Stack<!T>), only expressible inside
        // the class's own bodies; otherwise fall back to runtime dispatch (#178)
        if (!EmitterTypeHelpers.TryResolveInstanceDispatch(
                classType, methodBuilder, _ctx.EmittingTypeBuilder, out var castType, out var callTarget))
            return false;

        // Get target parameter types for proper conversion
        var targetParams = methodBuilder.GetParameters();
        int expectedParamCount = targetParams.Length;

        // Detect rest parameter: the ParameterTypeResolver compiles rest params to a
        // single trailing List<object>, so a final parameter of that type is a rest
        // marker. Without this, calling e.g. `this.debug("a", "b")` on a method
        // declared `debug(..._)` would push 2 object args for a method expecting 1
        // List<object> argument — the CLR rejects the resulting IL as an invalid
        // program. (Matches the loose function-call dispatch in
        // ExpressionEmitterBase.CallHelpers.cs / EmitRestParameterCall.)
        bool hasRestParam = expectedParamCount > 0 &&
            targetParams[expectedParamCount - 1].ParameterType == typeof(List<object>);
        int regularParamCount = hasRestParam ? expectedParamCount - 1 : expectedParamCount;

        // Spreads can only be lowered statically inside the trailing rest region, where the
        // runtime flattens them into the rest list. Anywhere else their runtime length decides
        // which value lands in which parameter slot, which this positional emit cannot know —
        // so hand those shapes to the dynamic dispatch path (it flattens via ExpandCallArgs)
        // instead of passing the spread array itself as a single argument (#1282). Bail before
        // emitting any IL: once the receiver is pushed, returning false would strand it.
        for (int i = 0; i < Math.Min(arguments.Count, regularParamCount); i++)
        {
            if (arguments[i] is Expr.Spread)
                return false;
        }
        if (!hasRestParam && arguments.Any(a => a is Expr.Spread))
            return false;

        // Emit: ((ClassName)receiver).method(args)
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, castType);

        // Emit leading regular arguments with type conversion.
        int regularArgsToEmit = Math.Min(arguments.Count, regularParamCount);
        for (int i = 0; i < regularArgsToEmit; i++)
        {
            var arg = arguments[i];
            EmitExpression(arg);
            EmitConversionForParameter(arg, targetParams[i].ParameterType);
        }

        // Pad omitted trailing arguments. An optional/defaulted param uses an object slot, so it is
        // padded with the `undefined` sentinel — observable via typeof for a plain optional and
        // firing the entry prologue for a defaulted one. (#739/#705)
        for (int i = regularArgsToEmit; i < regularParamCount; i++)
        {
            EmitOmittedArgument(targetParams[i].ParameterType);
        }

        if (hasRestParam)
        {
            // Collect all trailing args into a List<object> via the runtime CreateArray
            // helper (which wraps an object[] into the List<object> rest marker type).
            int restArgsCount = Math.Max(0, arguments.Count - regularParamCount);
            bool restHasSpread = false;
            for (int i = 0; i < restArgsCount; i++)
            {
                if (arguments[regularParamCount + i] is Expr.Spread) { restHasSpread = true; break; }
            }

            // With a spread present, spill each trailing arg (spreads spill their inner
            // expression) so the isSpread companion array can be built without re-evaluating
            // anything, then let ExpandCallArgs flatten in place. Without this a `...xs`
            // argument lands in the rest list as one nested array rather than its elements.
            LocalBuilder[]? restLocals = null;
            if (restHasSpread)
            {
                restLocals = new LocalBuilder[restArgsCount];
                for (int i = 0; i < restArgsCount; i++)
                {
                    var restArg = arguments[regularParamCount + i];
                    var inner = restArg is Expr.Spread sp ? sp.Expression : restArg;
                    EmitExpression(inner);
                    EmitBoxIfNeeded(inner);
                    var tmp = IL.DeclareLocal(_ctx.Types.Object);
                    IL.Emit(OpCodes.Stloc, tmp);
                    restLocals[i] = tmp;
                }
            }

            IL.Emit(OpCodes.Ldc_I4, restArgsCount);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
            for (int i = 0; i < restArgsCount; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                if (restLocals != null)
                {
                    IL.Emit(OpCodes.Ldloc, restLocals[i]);
                }
                else
                {
                    var arg = arguments[regularParamCount + i];
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }
                IL.Emit(OpCodes.Stelem_Ref);
            }

            if (restHasSpread)
            {
                IL.Emit(OpCodes.Ldc_I4, restArgsCount);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Boolean);
                for (int i = 0; i < restArgsCount; i++)
                {
                    if (arguments[regularParamCount + i] is not Expr.Spread) continue;
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    IL.Emit(OpCodes.Ldc_I4_1);
                    IL.Emit(OpCodes.Stelem_I1);
                }
                EmitExpandCallArgs();
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
        }
        else
        {
            // Extra arguments beyond declared parameters — JS semantics drop them,
            // but preserve evaluation order by emitting+popping the extras.
            for (int i = regularParamCount; i < arguments.Count; i++)
            {
                EmitExpression(arguments[i]);
                IL.Emit(OpCodes.Pop);
            }
        }

        // Emit the virtual call
        IL.Emit(OpCodes.Callvirt, callTarget);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Emits a non-virtual call to a base class method for super.method() expressions.
    /// Uses OpCodes.Call instead of Callvirt to bypass virtual dispatch and invoke the
    /// base class implementation directly, preventing infinite recursion.
    /// </summary>
    protected override bool TryEmitSuperMethodCall(string methodName, List<Expr> arguments)
    {
        // Resolve the superclass name - try multiple sources:
        // 1. CurrentSuperclassName (set in constructor context)
        // 2. ClassRegistry superclass lookup (works in method body context)
        // 3. Class expression superclass mapping
        string? superclassName = _ctx.CurrentSuperclassName;

        if (superclassName == null && _ctx.CurrentClassName != null)
        {
            // Try qualified name first (includes namespace), then simple name
            superclassName = _ctx.ClassRegistry?.GetSuperclass(
                _ctx.CurrentClassBuilder?.FullName ?? _ctx.CurrentClassName)
                ?? _ctx.ClassRegistry?.GetSuperclass(_ctx.CurrentClassName);
        }

        if (superclassName == null && _ctx.CurrentClassExpr != null)
        {
            _ctx.ClassExprSuperclass?.TryGetValue(_ctx.CurrentClassExpr, out superclassName);
        }

        if (superclassName == null)
            return false;

        // superclassName from ClassRegistry is already qualified; from CurrentSuperclassName it's simple.
        // ResolveClassName is idempotent for already-qualified names, so always safe to call.
        string resolvedSuperName = _ctx.ResolveClassName(superclassName);

        // Look up the method directly on the superclass (not walking up — that's what
        // ResolveInstanceMethod does, but we specifically want the super's version)
        var methodBuilder = _ctx.ResolveInstanceMethod(resolvedSuperName, methodName);
        if (methodBuilder == null)
            return false;

        // Generic superclasses need the member referenced through the instantiated base
        // (e.g. Base<float64>::count) — an open MethodDef token is not executable (#178)
        if (!EmitterTypeHelpers.TryResolveSuperCall(
                _ctx.CurrentClassBuilder, methodBuilder, _ctx.EmittingTypeBuilder, out var superTarget))
            return false;

        var methodParams = methodBuilder.GetParameters();

        // Emit: this.Base::method(args) via non-virtual Call
        IL.Emit(OpCodes.Ldarg_0);  // load 'this'

        for (int i = 0; i < arguments.Count; i++)
        {
            EmitExpression(arguments[i]);
            if (i < methodParams.Length)
            {
                EmitConversionForParameter(arguments[i], methodParams[i].ParameterType);
            }
            else
            {
                EmitBoxIfNeeded(arguments[i]);
            }
        }

        // Pad omitted trailing arguments (object slot → `undefined` sentinel). (#739/#705)
        for (int i = arguments.Count; i < methodParams.Length; i++)
        {
            EmitOmittedArgument(methodParams[i].ParameterType);
        }

        // Use Call (NOT Callvirt) to bypass virtual dispatch
        IL.Emit(OpCodes.Call, superTarget);
        SetStackUnknown();
        return true;
    }

    // Promise instance method calls (.then/.catch/.finally) are emitted by
    // ExpressionEmitterBase.EmitPromiseInstanceMethodCall — shared with the
    // state-machine emitters. It unwraps $Promise receivers (incl. #242
    // Promise subclasses) to their task and wraps results back into the
    // receiver's subclass type.
}
