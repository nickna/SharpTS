using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.DotNet;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Main call dispatch and function call emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Lowers a statically known, expression-only eval program into the current
    /// lexical environment. Returns false for declarations/control flow so those
    /// sources continue through the runtime eval bridge.
    /// </summary>
    internal bool TryEmitStaticDirectEval(string source)
    {
        List<Stmt> statements;
        try
        {
            statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        }
        catch
        {
            return false;
        }

        if (statements.Any(statement => statement is not Stmt.Expression))
            return false;

        if (statements.Count == 0)
        {
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            SetStackUnknown();
            return true;
        }

        for (int i = 0; i < statements.Count - 1; i++)
        {
            EmitExpression(((Stmt.Expression)statements[i]).Expr);
            IL.Emit(OpCodes.Pop);
        }

        EmitExpression(((Stmt.Expression)statements[^1]).Expr);
        EnsureBoxed();
        SetStackUnknown();
        return true;
    }

    protected override void EmitCall(Expr.Call c)
    {
        // CommonJS require() lowering is handled by ExpressionEmitterBase.EmitCall
        // (called via base.EmitCall below), so it works in async/generator emitters too.

        // ECMA-262 Array.prototype.*.call(receiver, ...) — rewrite to
        // ArrayX(Materialize(receiver), ...). Intercepted at the syntactic level
        // because compiled mode does not emit a JS-shaped Array.prototype object;
        // real dispatch would need a full $ArrayPrototype surface. Handles the
        // dominant test262 pattern (direct syntactic usage). Aliased access
        // (`var m = Array.prototype.every; m.call(arr, cb)`) is NOT covered.
        // ECMA-262 Array.prototype.*.call(receiver, ...) — rewrite to
        // ArrayX(Materialize(receiver), ...args). Intercepted at the syntactic
        // level because compiled mode does not emit a JS-shaped Array.prototype
        // object. With Stages 0b/0c landed (function-prototype, instanceof, and
        // new-FuncDecl all working correctly), test262 harness asserts fire
        // properly so any false positives surfaced by this pattern matcher
        // reclassify as real Fails — making Test262 numbers meaningful.
        if (TryEmitArrayPrototypeCall(c)) return;

        // ECMA-262 Object.prototype.toString.call(x) — return the proper
        // "[object X]" tag for built-in types. Intercepted at the syntactic
        // level because compiled mode emits Object.prototype as null (no
        // user-callable Object.prototype.toString). Common idiom in test262:
        // `'[object Math]' === Object.prototype.toString.call(Math)`.
        if (TryEmitObjectPrototypeToStringCall(c)) return;

        // Function('return this')() — global-detection probe; see helper doc.
        if (TryEmitFunctionReturnThisIdiom(c)) return;

        // `String(x)` / `Number(x)` / `Boolean(x)` — non-`new` coercion calls.
        // Compiled mode would otherwise resolve `String` as `typeof(string)` and
        // try to "call" the Type, which devolves to Stringify → wrong format for
        // objects with a custom toString. Match the syntactic pattern early so
        // these coerce via ECMA-262 ToString/ToNumber/ToBoolean.
        if (c.Callee is Expr.Variable coerceVar && c.Arguments.Count == 1)
        {
            switch (coerceVar.Name.Lexeme)
            {
                case "String":
                    // StringFromValue, not ToJsString: §22.1.1.1 exempts the
                    // String() call form from ToString's Symbol TypeError.
                    EmitExpression(c.Arguments[0]);
                    EmitBoxIfNeeded(c.Arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StringFromValueMethod);
                    SetStackUnknown();
                    return;
                case "Number":
                    EmitExpression(c.Arguments[0]);
                    EmitBoxIfNeeded(c.Arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ConvertToNumber);
                    IL.Emit(OpCodes.Box, _ctx.Types.Double);
                    SetStackUnknown();
                    return;
                case "Boolean":
                    EmitExpression(c.Arguments[0]);
                    EmitBoxIfNeeded(c.Arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.IsTruthy);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return;
                case "Object":
                    // ECMA-262 §20.1.1.1 Object(value): non-new path routes
                    // through ToObject. Primitives → boxed wrapper; null/
                    // undefined → empty $Object; everything else → arg unchanged.
                    EmitExpression(c.Arguments[0]);
                    EmitBoxIfNeeded(c.Arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.ToObjectMethod);
                    SetStackUnknown();
                    return;
            }
        }
        // ECMA-262 §20.1.1.1 Object(...args): only args[0] is consulted;
        // remaining args are ignored. Zero args → empty $Object (mirroring
        // new Object()). Multi-arg `Object(1, 2, 3)` must coerce just the
        // 1 via ToObject, matching the 1-arg case above. Without this multi-
        // arg fallthrough, the general call dispatch fails to resolve Object
        // and returns null (test262 S15.2.1.1_A3_T1/_T2/_T3 + S15.2.2.1_A1*).
        if (c.Callee is Expr.Variable objVar && objVar.Name.Lexeme == "Object")
        {
            if (c.Arguments.Count == 0)
            {
                IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(_ctx.Types.DictionaryStringObject));
                IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSObjectCtor);
                SetStackUnknown();
                return;
            }
            if (c.Arguments.Count > 1)
            {
                EmitExpression(c.Arguments[0]);
                EmitBoxIfNeeded(c.Arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ToObjectMethod);
                SetStackUnknown();
                return;
            }
        }

        // ECMA-262 non-callable singletons: JSON, Math, Reflect, Atomics
        // are objects without [[Call]] internal method. Calling them must
        // throw TypeError. Pattern-match the syntactic Variable callee so
        // we don't trip on user code that names a local/parameter the
        // same — only fires when the resolver agrees the name resolves
        // to the global (we approximate by checking the JSON/Math/etc.
        // singleton field exists in the runtime and the local table
        // doesn't claim the identifier).
        if (c.Callee is Expr.Variable nonCallableVar)
        {
            var name = nonCallableVar.Name.Lexeme;
            bool isNonCallableSingleton = name switch
            {
                "JSON" or "Math" or "Reflect" or "Atomics" => true,
                _ => false
            };
            if (isNonCallableSingleton
                && _ctx.Locals.GetLocal(name) == null
                && _ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out _) == false)
            {
                IL.Emit(OpCodes.Ldstr, name + " is not a function");
                GuestErrorEmitter.ThrowErrorFromStack(IL, _ctx.Runtime!, _ctx.Runtime!.TSTypeErrorCtor);
                IL.Emit(OpCodes.Ldnull);  // unreachable, balance stack
                SetStackUnknown();
                return;
            }
        }

        // External .NET type static methods (e.g., Console.WriteLine() via @DotNetType)
        // This is ILEmitter-only — requires TypeMapper.ExternalTypes + complex type conversion helpers
        if (c.Callee is Expr.Get externalStaticGet &&
            externalStaticGet.Object is Expr.Variable externalClassVar &&
            _ctx.TypeMapper?.ExternalTypes.TryGetValue(externalClassVar.Name.Lexeme, out var externalType) == true)
        {
            EmitExternalStaticMethodCall(
                externalType, externalStaticGet.Name.Lexeme,
                c.Arguments, c.TypeArgs, _ctx.TypeMap?.Get(c));
            return;
        }

        // For Expr.Get callees: run base class dispatch for handler chain, module.promises,
        // class statics, super.method, Promise.then/catch/finally, etc. If none match,
        // fall through to ILEmitter's EmitMethodCall for optimized instance method dispatch.
        if (c.Callee is Expr.Get methodGet)
        {
            // Handler chain: static types, Date.now, built-in modules, process streams,
            // globalThis chaining, imported/class-expr/this statics
            if (_callHandlers.TryHandle(this, c))
                return;

            // Optional-chain method calls (a.b?.m(x)) short-circuit to undefined
            // when a link is nullish — must be explicit now that InvokeMethodValue
            // throws for non-callable callees (#260).
            if (TryEmitOptionalChainMethodCall(c))
                return;

            // module.promises + Class.staticMethod + inherited Promise statics:
            // shared with TryEmitGetCalleeViaBaseClass so the dispatch rules
            // can't drift (an earlier inline copy here omitted the
            // Promise-subclass arm and shipped a MyP.resolve(1) crash).
            // Extra-arg boxing stays on ILEmitter's EmitBoxIfNeeded via the
            // EnsureBoxedArg override.
            if (TryEmitModulePromisesMethodCall(methodGet, c.Arguments))
                return;

            if (TryEmitClassStaticDispatch(c, methodGet))
                return;

            // Instance method dispatch (Array/String/Map/Promise/etc.)
            EmitMethodCall(
                methodGet, c.Arguments, c.TypeArgs,
                _ctx.TypeMap?.Get(c));
            return;
        }

        // All non-Get call patterns — delegate to base class
        base.EmitCall(c);
    }

    /// <summary>
    /// Resolves a type argument string to a .NET Type for generic instantiation.
    /// </summary>
    protected override Type ResolveTypeArg(string typeArg)
    {
        return typeArg switch
        {
            "number" => _ctx.Types.Double,
            "string" => _ctx.Types.String,
            "boolean" => _ctx.Types.Boolean,
            _ when _ctx.TypeMapper.ExternalTypes.TryGetValue(typeArg, out var external) => external,
            _ when _ctx.TypeMapper.ExternalTypes.TryGetValue(
                _ctx.ResolveClassName(typeArg), out var qualifiedExternal) => qualifiedExternal,
            _ when _ctx.GenericTypeParameters.TryGetValue(typeArg, out var gp) => gp,
            _ when _ctx.Classes.TryGetValue(_ctx.ResolveClassName(typeArg), out var tb) => tb,
            // Ambient scan of every loaded assembly — must stay below the program's own
            // generic parameters and classes. Compiled classes are public types in the
            // global namespace under their bare TypeScript name, so a bare-name scan run
            // any earlier binds `Foo<Bar>` to an unrelated loaded assembly's `Bar`, and
            // the emitted AssemblyRef is unresolvable at runtime. Names reachable here
            // are neither primitives, `@DotNetType`/`dotnet:` imports (both registered in
            // ExternalTypes), nor user classes — in practice fully-qualified CLR names.
            _ when DotNetTypeRegistry.ResolveFriendly(typeArg) is { } resolved => resolved,
            _ => _ctx.Types.Object
        };
    }

    /// <summary>
    /// Detects <c>Object.prototype.toString.call(receiver)</c> and emits
    /// IL that returns the proper ECMA-262 brand string ("[object Math]",
    /// "[object Array]", "[object String]", "[object Null]", "[object Undefined]",
    /// "[object Object]"). Compiled mode doesn't emit a user-callable
    /// Object.prototype.toString, so without this idiom returns `undefined`.
    /// </summary>
    private bool TryEmitObjectPrototypeToStringCall(Expr.Call c)
    {
        // Pattern: Get("call", Get("toString", Get("prototype", Variable("Object"))))
        if (c.Callee is not Expr.Get callGet || callGet.Name.Lexeme != "call")
            return false;
        if (callGet.Object is not Expr.Get toStringGet || toStringGet.Name.Lexeme != "toString")
            return false;
        if (toStringGet.Object is not Expr.Get protoGet || protoGet.Name.Lexeme != "prototype")
            return false;
        if (protoGet.Object is not Expr.Variable objVar || objVar.Name.Lexeme != "Object")
            return false;

        var runtime = _ctx.Runtime!;

        // Push receiver (or undefined if no args)
        if (c.Arguments.Count == 0)
        {
            IL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        }
        else
        {
            // Syntactic shortcut: `Object.prototype.toString.call(arguments)`
            // — `arguments` is bound as List<object> in compiled mode, which
            // would fall through to "[object Array]" in the runtime ladder.
            // Per ECMA-262 sloppy-arguments brand, emit directly.
            if (c.Arguments[0] is Expr.Variable v && v.Name.Lexeme == "arguments")
            {
                IL.Emit(OpCodes.Ldstr, "[object Arguments]");
                return true;
            }
            EmitExpression(c.Arguments[0]);
            EmitBoxIfNeeded(c.Arguments[0]);
        }

        // Delegate to the emitted $Runtime.ObjectProtoToString helper — the
        // single tag-dispatch ladder (RuntimeEmitter.StringPrototypeStubs.cs).
        // This used to be an inlined copy of that ladder at every call site;
        // the copies drifted (the helper was missing $Arguments, this one was
        // missing the function-classification set, #314) and bloated emitted
        // code. The helper is always emitted, so the call is unconditional.
        IL.Emit(OpCodes.Call, runtime.ObjectProtoToStringHelper);
        SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Detects <c>Array.prototype.METHOD.call(receiver, ...args)</c> and emits:
    /// <c>ArrayMETHOD($Runtime.ArrayLikeMaterialize(receiver), ...args)</c>.
    /// Only non-mutating methods are supported — mutating methods (push/pop/shift/
    /// unshift/splice/sort/reverse/copyWithin/fill) need to write indexed
    /// properties back onto the original receiver, which is out of scope here
    /// (matches the interpreter-side boundary in commit 04c4b2b).
    /// </summary>
    private bool TryEmitArrayPrototypeCall(Expr.Call c)
    {
        // Pattern A: Get("call", Get(METHOD, Get("prototype", Variable("Array"))))
        // Pattern B: Get("call", Get(METHOD, ArrayLiteral([])))
        //   The B form covers test262's idiom `[].find.call(receiver, …)` —
        //   semantically identical to A because Array.prototype is the receiver
        //   either way. The literal must be empty (otherwise its elements
        //   would matter to the inherited method's behavior).
        if (c.Callee is not Expr.Get callGet || callGet.Name.Lexeme != "call")
            return false;
        if (callGet.Object is not Expr.Get methodGet)
            return false;

        bool matchedPrototypeForm =
            methodGet.Object is Expr.Get protoGet
            && protoGet.Name.Lexeme == "prototype"
            && protoGet.Object is Expr.Variable arrayVar
            && arrayVar.Name.Lexeme == "Array";

        bool matchedEmptyArrayLiteral =
            methodGet.Object is Expr.ArrayLiteral arrLit
            && arrLit.Elements.Count == 0;

        if (!matchedPrototypeForm && !matchedEmptyArrayLiteral)
            return false;

        var methodName = methodGet.Name.Lexeme;
        var runtime = _ctx.Runtime!;
        // Map method name → runtime MethodBuilder + calling convention.
        // singleArg = one JS arg passed as `object`; argsArray = all JS args
        // packaged into an `object[]`. boxes describes the return-type boxing.
        (MethodInfo Method, string Kind, Type Box)? sig = methodName switch
        {
            "every"         => (runtime.ArrayEvery,      "single",    _ctx.Types.Boolean),
            "some"          => (runtime.ArraySome,       "single",    _ctx.Types.Boolean),
            "filter"        => (runtime.ArrayFilter,     "single",    _ctx.Types.Object),
            "map"           => (runtime.ArrayMap,        "single",    _ctx.Types.Object),
            "forEach"       => (runtime.ArrayForEach,    "single",    _ctx.Types.Object),
            "find"          => (runtime.ArrayFind,       "single",    _ctx.Types.Object),
            "findIndex"     => (runtime.ArrayFindIndex,  "single",    _ctx.Types.Double),
            "findLast"      => (runtime.ArrayFindLast,   "single",    _ctx.Types.Object),
            "findLastIndex" => (runtime.ArrayFindLastIndex,"single",  _ctx.Types.Double),
            "includes"      => (runtime.ArrayIncludes,   "search",    _ctx.Types.Boolean),
            "join"          => (runtime.ArrayJoin,       "single",    _ctx.Types.Object),
            "concat"        => (runtime.ArrayConcat,     "argsArray", _ctx.Types.Object),
            "flat"          => (runtime.ArrayFlat,       "single",    _ctx.Types.Object),
            "flatMap"       => (runtime.ArrayFlatMap,    "single",    _ctx.Types.Object),
            "at"            => (runtime.ArrayAt,         "single",    _ctx.Types.Object),
            "reduce"        => (runtime.ArrayReduce,     "argsArray", _ctx.Types.Object),
            "reduceRight"   => (runtime.ArrayReduceRight,"argsArray", _ctx.Types.Object),
            "slice"         => (runtime.ArraySlice,      "argsArray", _ctx.Types.Object),
            "indexOf"       => (runtime.ArrayIndexOf,    "search",    _ctx.Types.Double),
            "lastIndexOf"   => (runtime.ArrayLastIndexOf,"search",    _ctx.Types.Double),
            "entries"       => (runtime.ArrayEntries,    "noArg",     _ctx.Types.Object),
            "keys"          => (runtime.ArrayKeys,       "noArg",     _ctx.Types.Object),
            "values"        => (runtime.ArrayValues,     "noArg",     _ctx.Types.Object),
            "toReversed"    => (runtime.ArrayToReversed, "noArg",     _ctx.Types.Object),
            "toSorted"      => (runtime.ArrayToSorted,   "single",    _ctx.Types.Object),
            "toSpliced"     => (runtime.ArrayToSpliced,  "argsArray", _ctx.Types.Object),
            "with"          => (runtime.ArrayWith,       "argsArray", _ctx.Types.Object),
            // These prototype-specific helpers implement the generic
            // array-like algorithms directly against the original receiver
            // (Get/Set/Delete + LengthOfArrayLike). They must not go through
            // ArrayLikeMaterialize, which would mutate only a detached list.
            "push"           => (runtime.ArrayPushProto,  "argsArray", _ctx.Types.Double),
            "pop"            => (runtime.ArrayPopProto,   "noArg",     _ctx.Types.Object),
            "shift"          => (runtime.ArrayShiftProto, "noArg",     _ctx.Types.Object),
            "unshift"        => (runtime.ArrayUnshiftProto,"argsArray",_ctx.Types.Double),
            "reverse"        => (runtime.ArrayReverseProto,"noArg",    _ctx.Types.Object),
            "fill"           => (runtime.ArrayFillProto,  "argsArray", _ctx.Types.Object),
            "copyWithin"     => (runtime.ArrayCopyWithinProto,"argsArray",_ctx.Types.Object),
            _ => null,
        };
        if (sig is null)
            return false;
        var (runtimeMethod, kind, boxType) = sig.Value;

        // args[0] is the thisArg (receiver); the rest are the method's own args.
        if (c.Arguments.Count == 0)
        {
            // Array.prototype.X.call() — spec: this is undefined, throw TypeError.
            // Easiest: still emit the materializer on null; it throws.
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Call, runtime.ArrayLikeMaterialize);
            // Unreachable after throw, but keep stack balanced for any dead-code
            // path verification. Load default return and box.
            IL.Emit(OpCodes.Ldnull);
            return true;
        }

        // Save the original receiver into a local, then stash it on the
        // `_currentArrayLikeReceiver` thread-static so `EmitCallbackArgsAndInvoke`
        // reads it as the callback's 3rd arg (O per ECMA-262). Restore after
        // the helper returns so nested calls don't leak.
        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        EmitExpression(c.Arguments[0]);
        EmitBoxIfNeeded(c.Arguments[0]);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Stloc, receiverLocal);

        // toSorted must read the generic receiver's indexed properties exactly
        // once before comparison, and abrupt completion must not leak the
        // observable-receiver thread-static into a later array call. Keep that
        // state management inside one emitted runtime helper.
        if (methodName == "toSorted")
        {
            if (c.Arguments.Count >= 2)
            {
                EmitExpression(c.Arguments[1]);
                EmitBoxIfNeeded(c.Arguments[1]);
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            }
            IL.Emit(OpCodes.Call, runtime.ArrayToSortedGeneric);
            SetStackUnknown();
            return true;
        }

        // The length-changing mutators always perform Set(O, "length", ...,
        // true). String exotic objects expose a non-writable length, including
        // the empty string, so the operation must throw instead of silently
        // falling through the runtime's CLR-string property setter.
        if (methodName is "push" or "pop" or "shift" or "unshift")
        {
            var receiverIsNotString = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Types.String);
            IL.Emit(OpCodes.Brfalse, receiverIsNotString);
            IL.Emit(OpCodes.Pop); // duplicated receiver consumed by direct helper otherwise
            GuestErrorEmitter.ThrowTypeError(IL, runtime, "Cannot assign to read only property 'length' of string");
            IL.MarkLabel(receiverIsNotString);
        }

        // ECMA-262: methods that allocate a new array via ArraySpeciesCreate(O, len)
        // must throw RangeError if len > 2^32 - 1 (ArrayCreate length check).
        // Done BEFORE stashing thread-statics so a throw doesn't leak state.
        // NaN comparisons via Ble_Un fall through (no throw) for non-coercible
        // lengths — those get clamped to 0 / 1M by the materializer below.
        bool createsNewArrayPre = methodGet.Name.Lexeme is "map" or "filter" or "slice"
            or "splice" or "toSpliced" or "with" or "flat" or "flatMap"
            or "toReversed" or "toSorted";
        if (createsNewArrayPre)
        {
            var lengthOkLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Ldstr, "length");
            IL.Emit(OpCodes.Call, runtime.GetProperty);
            IL.Emit(OpCodes.Call, runtime.ToNumber);
            IL.Emit(OpCodes.Ldc_R8, 4294967295.0); // 2^32 - 1
            IL.Emit(OpCodes.Ble_Un, lengthOkLabel);
            // Pop the duplicated receiver since we're going to throw and skip
            // the materialize call that would have consumed it.
            IL.Emit(OpCodes.Pop);
            GuestErrorEmitter.ThrowRangeError(IL, runtime, "Invalid array length");
            IL.MarkLabel(lengthOkLabel);
        }

        var prevReceiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Ldsfld, runtime.CurrentArrayLikeReceiverField);
        IL.Emit(OpCodes.Stloc, prevReceiverLocal);
        // ECMA-262 §23.1.3: O = ToObject(this value). The callback's final
        // "array" argument is O, so a primitive receiver must surface as its
        // wrapper object — `Array.prototype.forEach.call("ab", cb)` passes a
        // String wrapper (`typeof obj === "object"`, `obj instanceof String`),
        // not the bare string. ToObject is identity for objects/arrays and
        // returns an empty object for null/undefined (the spec TypeError still
        // fires later at materialization, before the callback ever reads this).
        // Materialization below keeps using the raw receiver, observationally
        // identical to O for every shape this path supports. (#454)
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Call, runtime.ToObjectMethod);
        IL.Emit(OpCodes.Stsfld, runtime.CurrentArrayLikeReceiverField);

        var methodArgs = c.Arguments.Skip(1).ToList();

        // ECMA-262 lazy-iteration order: callback validation happens AFTER
        // ToLength(O.length) but BEFORE element reads. For the static-
        // missing-callback case (kind=="single" with no methodArgs), read
        // length to trigger any accessor side effects, then throw TypeError
        // without materializing element accessors. Test262 tests like
        // `Array.prototype.every.call(obj)` (no cb) check this ordering via
        // assert(lengthAccessed) + assert(loopAccessed === false).
        // Only applies to methods whose first arg is a REQUIRED callback —
        // includes/join/concat/flat/at take other arg shapes and don't throw
        // when called with no args.
        bool needsCallableFirstArg = methodName is "every" or "some" or "filter"
            or "map" or "forEach" or "find" or "findIndex" or "findLast"
            or "findLastIndex" or "flatMap" or "reduce" or "reduceRight";

        // Helper: emit IL that fires length-side-effects without iterating
        // elements. ECMA-262 LengthOfArrayLike calls Get(O, "length") then
        // ToLength → ToInteger → ToNumber → ToPrimitive(value, "number").
        // For test fixtures that set length to a getter returning an object
        // with a custom toString, both the length getter AND the toString
        // must fire before the IsCallable(callbackfn) check throws. We
        // approximate by calling GetProperty + ToJsString (does ToPrimitive
        // valueOf/toString chain). Stack-in: [], stack-out: [].
        void EmitLengthSideEffect()
        {
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Ldstr, "length");
            IL.Emit(OpCodes.Call, runtime.GetProperty);
            IL.Emit(OpCodes.Call, runtime.ToJsString);
            IL.Emit(OpCodes.Pop);
        }

        if (methodArgs.Count == 0 && needsCallableFirstArg)
        {
            // Pop the duplicated receiver from the stack — we won't be
            // calling materialize.
            IL.Emit(OpCodes.Pop);
            EmitLengthSideEffect();
            // Throw: TypeError("undefined is not a function")
            GuestErrorEmitter.ThrowTypeError(IL, runtime, "undefined is not a function");
            // Unreachable, but keep stack balanced for any dead-code analysis.
            return true;
        }

        // Runtime null/undefined callback check (when user passes the literal
        // undefined or a null-valued variable). Without this, the materializer
        // fires accessor getters on element indices BEFORE the runtime helper
        // validates callbackfn — but ECMA-262 requires "ToLength(O.length) →
        // throw on bad callback → iterate" order. Test262 tests like
        // `Array.prototype.reduceRight.call(obj, undefined)` set
        // Object.defineProperty(obj, "0", {get: side-effect}) and assert the
        // side effect did NOT fire. Read length + ToJsString first (spec
        // wants this access AND any toString side effects on the returned
        // value), then throw without invoking element getters.
        if (needsCallableFirstArg && methodArgs.Count >= 1)
        {
            var cbLocal = IL.DeclareLocal(_ctx.Types.Object);
            EmitExpression(methodArgs[0]);
            EmitBoxIfNeeded(methodArgs[0]);
            IL.Emit(OpCodes.Stloc, cbLocal);

            var throwPath = IL.DefineLabel();
            var cbValid = IL.DefineLabel();
            // null → throw
            IL.Emit(OpCodes.Ldloc, cbLocal);
            IL.Emit(OpCodes.Brfalse, throwPath);
            // $Undefined → throw
            IL.Emit(OpCodes.Ldloc, cbLocal);
            IL.Emit(OpCodes.Isinst, runtime.UndefinedType);
            IL.Emit(OpCodes.Brtrue, throwPath);
            IL.Emit(OpCodes.Br, cbValid);

            IL.MarkLabel(throwPath);
            IL.Emit(OpCodes.Pop); // Pop the duplicated receiver.
            EmitLengthSideEffect();
            GuestErrorEmitter.ThrowTypeError(IL, runtime, "undefined is not a function");

            IL.MarkLabel(cbValid);
            // The callback expression is re-emitted later when methodArgs
            // are materialized into the call. This is a benign double-eval
            // (callbacks are typically bare identifiers / literals); we
            // tolerate it to avoid a methodArgs rewrite.
        }

        // list = ArrayLikeMaterializeForIteration(receiver) for iterator helpers
        // whose IL has been updated to use LoadArrayLikeElement (issue #90).
        // For others (concat / slice / indexOf / includes / etc.), keep the
        // eager ArrayLikeMaterialize — those helpers read list[i] directly and
        // would see null placeholders under lazy mode. The Dup'd receiver is
        // on the stack.
        bool useLazyMaterializer = methodName is "every" or "some" or "filter"
            or "map" or "forEach" or "find" or "findIndex" or "findLast"
            or "findLastIndex" or "flatMap" or "reduce" or "reduceRight"
            or "includes" or "indexOf" or "lastIndexOf"
            or "toReversed" or "toSpliced" or "with";
        // concat itself performs IsConcatSpreadable before consulting length;
        // pre-materializing here would erase the receiver's identity and read
        // length even when @@isConcatSpreadable is false.
        bool usesOriginalReceiver = methodName is "push" or "pop" or "shift"
            or "unshift" or "reverse" or "fill" or "copyWithin"
            or "indexOf" or "lastIndexOf";
        if (methodName != "concat" && !usesOriginalReceiver)
        {
            IL.Emit(OpCodes.Call, useLazyMaterializer
                ? runtime.ArrayLikeMaterializeForIteration
                : runtime.ArrayLikeMaterialize);
        }

        // For iterator methods that accept thisArg (callbackfn, thisArg) per
        // ECMA-262, save the previous _currentCallbackThisArg, stash methodArgs[1]
        // (or null) into the thread-static, then restore after the call.
        // EmitCallbackArgsAndInvoke reads it as the receiver to InvokeMethodValue.
        bool hasThisArgSlot = methodName is "every" or "some" or "filter" or "map"
            or "forEach" or "find" or "findIndex" or "findLast" or "findLastIndex"
            or "flatMap";
        LocalBuilder? prevThisArgLocal = null;
        if (hasThisArgSlot)
        {
            prevThisArgLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Ldsfld, runtime.CurrentCallbackThisArgField);
            IL.Emit(OpCodes.Stloc, prevThisArgLocal);
            if (methodArgs.Count >= 2)
            {
                EmitExpression(methodArgs[1]);
                EmitBoxIfNeeded(methodArgs[1]);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Stsfld, runtime.CurrentCallbackThisArgField);
        }

        switch (kind)
        {
            case "single":
                if (methodArgs.Count > 0)
                {
                    EmitExpression(methodArgs[0]);
                    EmitBoxIfNeeded(methodArgs[0]);
                }
                else
                {
                    if (methodName is "indexOf" or "lastIndexOf")
                        IL.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
                    else
                        IL.Emit(OpCodes.Ldnull);
                }
                break;
            case "search":
                // searchElement + optional fromIndex
                if (methodArgs.Count > 0)
                {
                    EmitExpression(methodArgs[0]);
                    EmitBoxIfNeeded(methodArgs[0]);
                }
                else
                {
                    if (methodName is "includes" or "indexOf" or "lastIndexOf")
                        IL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
                    else
                        IL.Emit(OpCodes.Ldnull);
                }
                if (methodArgs.Count > 1)
                {
                    EmitExpression(methodArgs[1]);
                    EmitBoxIfNeeded(methodArgs[1]);
                }
                else
                {
                    if (methodName is "indexOf" or "lastIndexOf")
                        IL.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
                    else
                        IL.Emit(OpCodes.Ldnull);
                }
                break;
            case "argsArray":
                // Push the method args as an object[], flattening any `...spread` arg in
                // place (#952) — identical to the old inline loop when no spread is present,
                // so concat/reduce/slice/toSpliced/with via Array.prototype.X.call all expand
                // spreads the way the generic call path does.
                EmitArgsArrayWithSpread(methodArgs);
                break;
            case "noArg":
                // Helper takes only the materialized list — no extra args
                // (entries/keys/values).
                break;
        }

        IL.Emit(OpCodes.Call, runtimeMethod);

        // Box numeric returns to match expression-as-value conventions.
        // Void returns (forEach) need a Ldnull pushed so the caller has a
        // value on the stack — otherwise the JIT throws InvalidProgramException.
        // Inspect the actual ReturnType of the helper rather than the boxType
        // sigil so already-boxed `object`-returning helpers (every/some/etc.)
        // don't get a stray extra Box opcode that corrupts strict-equality
        // checks (`Array.prototype.every.call(...) === true` returned false
        // because the double-box yielded a different object identity).
        var rt = runtimeMethod.ReturnType;
        if (rt == typeof(void))
            // Spec: void-returning prototype methods (forEach) return undefined.
            // Push $Undefined.Instance, not C# null — test262 call-with-boolean
            // tests `Array.prototype.forEach.call(true, () => {}) === undefined`.
            IL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        else if (rt == _ctx.Types.Double)
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
        else if (rt == _ctx.Types.Boolean)
            IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
        // else (already _types.Object / List<object> / etc.) → no boxing.

        // Restore the thread-static so nested prototype.call contexts don't leak.
        // Save result in temp, restore field, push result.
        var resultTmp = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, resultTmp);
        IL.Emit(OpCodes.Ldloc, prevReceiverLocal);
        IL.Emit(OpCodes.Stsfld, runtime.CurrentArrayLikeReceiverField);
        if (prevThisArgLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, prevThisArgLocal);
            IL.Emit(OpCodes.Stsfld, runtime.CurrentCallbackThisArgField);
        }
        IL.Emit(OpCodes.Ldloc, resultTmp);

        SetStackUnknown();
        return true;
    }
}
