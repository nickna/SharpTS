using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Compilation;

/// <summary>
/// Shared built-in constructor emission for all emitters (ILEmitter + state machine emitters).
/// This centralizes constructor dispatch that was previously duplicated (and incomplete) across
/// AsyncMoveNextEmitter, AsyncArrowMoveNextEmitter, GeneratorMoveNextEmitter, and
/// AsyncGeneratorMoveNextEmitter.
/// </summary>
public abstract partial class ExpressionEmitterBase
{
    /// <summary>
    /// Extracts a qualified class name from a callee expression.
    /// Returns (namespaceParts, className) where namespaceParts is empty for simple names.
    /// </summary>
    protected static (List<string> namespaceParts, string className) ExtractQualifiedNameFromCallee(Expr callee)
    {
        List<string> parts = [];
        CollectGetChainParts(callee, parts);

        if (parts.Count == 0)
            return ([], "");

        var namespaceParts = parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : new List<string>();
        var className = parts[^1];
        return (namespaceParts, className);
    }

    private static void CollectGetChainParts(Expr expr, List<string> parts)
    {
        switch (expr)
        {
            case Expr.Variable v:
                parts.Add(v.Name.Lexeme);
                break;
            case Expr.Get g:
                CollectGetChainParts(g.Object, parts);
                parts.Add(g.Name.Lexeme);
                break;
        }
    }

    /// <summary>
    /// Emits an expression and ensures the result is a double on the stack.
    /// Optimizes literal doubles/ints by pushing directly.
    /// </summary>
    public virtual void EmitExpressionAsDouble(Expr expr)
    {
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            IL.Emit(OpCodes.Ldc_R8, d);
            SetStackType(StackType.Double);
        }
        else if (expr is Expr.Literal intLit && intLit.Value is int i)
        {
            IL.Emit(OpCodes.Ldc_R8, (double)i);
            SetStackType(StackType.Double);
        }
        else
        {
            EmitExpression(expr);
            EnsureDouble();
        }
    }

    /// <summary>
    /// Emits a boxed argument expression, or null if the argument index exceeds the count.
    /// </summary>
    private void EmitBoxedArgOrNull(List<Expr> arguments, int index)
    {
        if (index < arguments.Count)
        {
            EmitExpression(arguments[index]);
            EnsureBoxed();
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
    }

    /// <summary>
    /// Emits a two-boxed-argument constructor pattern (arg0 or null, arg1 or null, then call/newobj).
    /// </summary>
    private void EmitTwoArgConstructor(List<Expr> arguments, System.Reflection.MethodInfo method, bool useNewobj = false)
    {
        EmitBoxedArgOrNull(arguments, 0);
        EmitBoxedArgOrNull(arguments, 1);
        IL.Emit(useNewobj ? OpCodes.Newobj : OpCodes.Call, method);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits a two-boxed-argument constructor pattern using Newobj for ConstructorInfo.
    /// </summary>
    private void EmitTwoArgConstructor(List<Expr> arguments, System.Reflection.ConstructorInfo ctor)
    {
        EmitBoxedArgOrNull(arguments, 0);
        EmitBoxedArgOrNull(arguments, 1);
        IL.Emit(OpCodes.Newobj, ctor);
        SetStackUnknown();
    }

    /// <summary>
    /// True when <paramref name="arg"/> is a literal <c>{ captureRejections: true }</c>
    /// options object (used to enable EventEmitter rejection capture at compile time).
    /// </summary>
    private static bool HasCaptureRejectionsTrue(Expr arg)
    {
        if (arg is not Expr.ObjectLiteral obj) return false;
        foreach (var p in obj.Properties)
        {
            if (p.IsSpread) continue;
            var key = p.Key switch
            {
                Expr.IdentifierKey ik => ik.Name.Lexeme,
                Expr.LiteralKey lk => lk.Literal.Lexeme,
                _ => null
            };
            if (key == "captureRejections" && p.Value is Expr.Literal { Value: true })
                return true;
        }
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a built-in type constructor.
    /// Returns true if the constructor was handled, false to fall through to user-class resolution.
    /// </summary>
    protected virtual bool TryEmitBuiltInConstructor(string className, List<Expr> arguments)
    {
        switch (className)
        {
            // Blob/File: interpreter-complete (SharpTSBlob/SharpTSFile); the compiled
            // $Blob/$File IL type (parts-concatenating ctor + Promise/stream-returning
            // methods) is a documented follow-up (#1159). Emit a clear runtime error
            // rather than the misleading "undefined variable Blob".
            case "Blob":
            case "File":
                IL.Emit(OpCodes.Ldstr, className + " is not yet supported in compiled mode (interpreter only); see SharpTS #1159");
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSTypeErrorCtor);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateException);
                IL.Emit(OpCodes.Throw);
                IL.Emit(OpCodes.Ldnull); // unreachable; balances the expression's result slot
                SetStackUnknown();
                return true;

            // --- No-arg constructors ---
            case "WeakMap":
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateWeakMap);
                SetStackUnknown();
                return true;

            case "WeakSet":
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateWeakSet);
                SetStackUnknown();
                return true;

            case "EventEmitter":
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSEventEmitterCtor);
                // #1099: honor a literal { captureRejections: true } option. Only
                // the object-literal form is recognized (the overwhelmingly common
                // usage); a dynamic options object is a documented bound.
                if (arguments.Count > 0 && HasCaptureRejectionsTrue(arguments[0]))
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSEventEmitterEnableCaptureRejections);
                }
                SetStackUnknown();
                return true;

            // AsyncLocalStorage — no longer pattern-matched globally. Users must
            // `import { AsyncLocalStorage } from 'async_hooks'` (ESM-strict);
            // the class is a pure-TS class in stdlib/node/async_hooks.ts that
            // wraps the underlying $AsyncLocalStorage instance via primitive:async_hooks.
            case "Resolver":
                IL.Emit(OpCodes.Call, Ctx.Runtime!.DnsResolverFactory);
                SetStackUnknown();
                return true;

            // net.BlockList / net.SocketAddress (#1069). Guarded on the emitted
            // ctor being present (UsesNet) so a user class with the same name
            // still resolves when the net module is not in play.
            case "BlockList" when Ctx.Runtime?.BlockListCtor != null:
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.BlockListCtor);
                SetStackUnknown();
                return true;

            case "SocketAddress" when Ctx.Runtime?.SocketAddressCtor != null:
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.SocketAddressCtor);
                SetStackUnknown();
                return true;

            case "AbortController":
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateAbortController);
                SetStackUnknown();
                return true;

            case "TextEncoder":
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSTextEncoderCtor);
                SetStackUnknown();
                return true;

            case "PassThrough":
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSPassThroughCtor);
                SetStackUnknown();
                return true;

            case "MessageChannel":
                IL.Emit(OpCodes.Call, Ctx.Runtime!.TSMessageChannelCtor);
                SetStackUnknown();
                return true;

            // BroadcastChannel(name) — pure-IL emitted $BroadcastChannel class.
            // Inherited by all four async/generator state-machine emitters via
            // ExpressionEmitterBase, so no per-emitter duplication is needed.
            case "BroadcastChannel":
                if (arguments.Count != 1)
                    throw new CompileException("BroadcastChannel constructor requires exactly 1 argument (name).");
                EmitExpression(arguments[0]);
                EnsureBoxed();
                IL.Emit(OpCodes.Call, Ctx.Runtime!.Stringify);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.BroadcastChannelCtor);
                SetStackUnknown();
                return true;

            // --- Single boxed arg (null if missing) ---
            case "WeakRef":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateWeakRef);
                SetStackUnknown();
                return true;

            case "FinalizationRegistry":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateFinalizationRegistry);
                SetStackUnknown();
                return true;

            case "Headers":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSHeadersCtor);
                SetStackUnknown();
                return true;

            // Pure-IL emitted $ReadableStream (Task 3 of the tech debt paydown).
            // PipeTo/PipeThrough/Tee are emitted as synchronous pump loops
            // (Task.GetAwaiter().GetResult()) — see RuntimeEmitter.ReadableStream.cs.
            case "ReadableStream":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.ReadableStreamCtor);
                SetStackUnknown();
                return true;

            // Pure-IL emitted $WritableStream (Task 3 of the tech debt paydown).
            case "WritableStream":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.WritableStreamCtor);
                SetStackUnknown();
                return true;

            // Pure-IL emitted $TransformStream (Task 3 of the tech debt paydown).
            case "TransformStream":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                EmitBoxedArgOrNull(arguments, 2);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TransformStreamCtor);
                SetStackUnknown();
                return true;

            // Pure-IL emitted strategies (Task 3 of the tech debt paydown).
            // The emitted ctor extracts highWaterMark from the options dict
            // inline and stores it in a typed field.
            case "ByteLengthQueuingStrategy":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.ByteLengthQueuingStrategyCtor);
                SetStackUnknown();
                return true;

            case "CountQueuingStrategy":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.CountQueuingStrategyCtor);
                SetStackUnknown();
                return true;

            // URL / URLSearchParams — no compile-time built-in. Users must
            // `import { URL, URLSearchParams } from 'url'`; `new URL(...)` then
            // resolves through normal variable lookup to the TS stdlib class.

            // --- Two boxed args (null if missing) ---
            case "Proxy":
                EmitTwoArgConstructor(arguments, Ctx.Runtime!.CreateProxy);
                return true;

            case "Request":
                EmitTwoArgConstructor(arguments, Ctx.Runtime!.TSRequestCtor);
                return true;

            case "Response":
                EmitTwoArgConstructor(arguments, Ctx.Runtime!.TSResponseCtor);
                return true;

            // --- Date (multi-arg) ---
            case "Date":
                EmitNewDateConstructor(arguments);
                return true;

            // --- Object(v) constructor — ECMA-262 19.1.1.1 ToObject ---
            // `new Object(undefined|null)` → empty $Object.
            // `new Object(prim)` for primitives → routes through ToObject.
            // For Boolean/Number/String primitives, build the matching boxed
            // wrapper via NewBoxedPrimitive so prototype-chain lookups land
            // on the right singleton. For non-primitives, the value is
            // returned as-is (ECMA-262 step 2 of ToObject).
            case "Object":
                if (arguments.Count == 0)
                {
                    IL.Emit(OpCodes.Newobj, Ctx.Types.GetDefaultConstructor(Ctx.Types.DictionaryStringObject));
                    IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSObjectCtor);
                    SetStackUnknown();
                    return true;
                }
                EmitExpression(arguments[0]);
                EnsureBoxed();
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ToObjectMethod);
                SetStackUnknown();
                return true;

            // --- String / Number / Boolean primitive wrappers ---
            // ECMA-262: `new String(v)` / `new Number(v)` / `new Boolean(v)`
            // construct boxed primitive wrappers. Stage 4z19: build a $Object
            // wrapper with __primitiveType + __primitiveValue marker fields,
            // PDS-linked to the matching prototype singleton. Tests that probe
            // `obj instanceof Boolean` see true via the marker; tests that do
            // `obj.length = 2; obj[0] = 11` work via $Object's indexed set.
            //
            // String now wraps too (issue #91): StringEmitter's direct
            // dispatch unwraps via $Runtime.UnwrapStringReceiver, and borrowed
            // dispatch flows through CoercePrimitiveArgs+ToJsString which
            // already reads the __primitiveValue marker. So `(new String("x"))
            // .charAt(0)` and `String.prototype.charAt.call(wrapper, 0)` both
            // resolve to the underlying primitive.
            case "String":
                IL.Emit(OpCodes.Ldstr, "String");
                if (arguments.Count == 0)
                    IL.Emit(OpCodes.Ldstr, "");
                else
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.ToJsString);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.NewBoxedPrimitiveMethod);
                SetStackUnknown();
                return true;

            case "Number":
                IL.Emit(OpCodes.Ldstr, "Number");
                if (arguments.Count == 0)
                {
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                    IL.Emit(OpCodes.Box, Ctx.Types.Double);
                }
                else
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.ToNumber);
                    IL.Emit(OpCodes.Box, Ctx.Types.Double);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.NewBoxedPrimitiveMethod);
                SetStackUnknown();
                return true;

            case "Boolean":
                IL.Emit(OpCodes.Ldstr, "Boolean");
                if (arguments.Count == 0)
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, Ctx.Types.Boolean);
                }
                else
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.IsTruthy);
                    IL.Emit(OpCodes.Box, Ctx.Types.Boolean);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.NewBoxedPrimitiveMethod);
                SetStackUnknown();
                return true;

            // --- Map/Set with optional entries ---
            case "Map":
                if (arguments.Count == 0)
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateMap);
                else
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateMapFromEntries);
                }
                SetStackUnknown();
                return true;

            case "Set":
                if (arguments.Count == 0)
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateSet);
                else
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateSetFromArray);
                }
                SetStackUnknown();
                return true;

            // --- RegExp ---
            case "RegExp":
                EmitNewRegExpConstructor(arguments);
                return true;

            // --- Promise ---
            case "Promise":
                if (arguments.Count != 1)
                    throw new CompileException("Promise constructor requires exactly 1 argument (executor function).");
                EmitExpression(arguments[0]);
                EnsureBoxed();
                IL.Emit(OpCodes.Call, Ctx.Runtime!.PromiseFromExecutor);
                SetStackUnknown();
                return true;

            // --- TextDecoder ---
            case "TextDecoder":
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Call, Ctx.Runtime!.Stringify);
                }
                else
                {
                    IL.Emit(OpCodes.Ldnull);
                }
                IL.Emit(OpCodes.Ldc_I4_0); // fatal: false
                IL.Emit(OpCodes.Ldc_I4_0); // ignoreBOM: false
                IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSTextDecoderCtor);
                SetStackUnknown();
                return true;

            // StringDecoder migrated to stdlib/node/string_decoder.ts — user's `new StringDecoder()`
            // now resolves to the TS class via the standard user-class constructor path.

            // --- PerformanceObserver ---
            // PerformanceObserver — no longer pattern-matched globally. Users must
            // `import { PerformanceObserver } from 'perf_hooks'` (ESM-strict);
            // the class is a pure-TS class in stdlib/node/perf_hooks.ts.
            // --- SharedArrayBuffer / ArrayBuffer ---
            case "SharedArrayBuffer":
                if (arguments.Count == 0)
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                else
                    EmitExpressionAsDouble(arguments[0]);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.TSSharedArrayBufferCtor);
                SetStackUnknown();
                return true;

            case "ArrayBuffer":
                if (arguments.Count == 0)
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                else
                    EmitExpressionAsDouble(arguments[0]);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.TSArrayBufferCtor);
                SetStackUnknown();
                return true;

            // --- Array (issue #61): JS-spec dispatch for new Array(…) / Array(…).
            // Emit args as object[] and route through $Runtime.ArrayConstructor
            // which handles: 0 args → []; 1 numeric arg → length-n nulls; 1
            // non-numeric arg → [arg]; N args → [a, b, c, …].
            case "Array":
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, Ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.ArrayConstructor);
                SetStackUnknown();
                return true;

            // --- DataView ---
            case "DataView":
                if (arguments.Count == 0)
                    throw new CompileException("DataView constructor requires at least 1 argument (buffer).");
                EmitExpression(arguments[0]);
                EnsureBoxed();
                if (arguments.Count > 1)
                    EmitExpressionAsDouble(arguments[1]);
                else
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                EmitBoxedArgOrNull(arguments, 2);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.TSDataViewCtor);
                SetStackUnknown();
                return true;

            // --- Worker ---
            case "Worker":
                if (arguments.Count == 0)
                    throw new CompileException("Worker constructor requires at least 1 argument (filename).");
                // A Worker runs an interpreter for its child script and is
                // constructed by reflection into SharpTS.dll, so the runtime must
                // be co-located (#354). Record the soft dependency so the CLI
                // copies SharpTS.dll next to the output.
                Ctx.Runtime!.RequireSharpTSRuntime("Worker");
                EmitExpression(arguments[0]);
                EnsureBoxed();
                IL.Emit(OpCodes.Call, Ctx.Runtime!.Stringify);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Ldnull); // parentInterpreter (null in compiled code)
                IL.Emit(OpCodes.Call, Ctx.Runtime!.TSWorkerCtor);
                SetStackUnknown();
                return true;

            // --- http.Agent ---
            case "Agent":
                EmitBoxedArgOrNull(arguments, 0);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.HttpAgentFactory);
                SetStackUnknown();
                return true;

            // --- vm.Script ---
            case "Script":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.VmNewScript);
                SetStackUnknown();
                return true;

            // --- vm.SourceTextModule ---
            case "SourceTextModule":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.VmNewSourceTextModule);
                SetStackUnknown();
                return true;

            // --- vm.SyntheticModule ---
            case "SyntheticModule":
                EmitBoxedArgOrNull(arguments, 0);
                EmitBoxedArgOrNull(arguments, 1);
                EmitBoxedArgOrNull(arguments, 2);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.VmNewSyntheticModule);
                SetStackUnknown();
                return true;

            // --- Stream constructors ---
            case "Readable":
                EmitNewReadableConstructor(arguments);
                return true;

            case "Writable":
                EmitNewWritableConstructor(arguments);
                return true;

            case "Duplex":
                EmitNewDuplexConstructor(arguments);
                return true;

            case "Transform":
                EmitNewTransformConstructor(arguments);
                return true;

            default:
                // Check for error types (Error, TypeError, RangeError, etc.)
                if (BuiltInNames.IsErrorTypeName(className))
                {
                    EmitNewErrorConstructor(className, arguments);
                    return true;
                }

                // Check for TypedArray types
                if (BuiltInNames.IsTypedArrayName(className))
                {
                    EmitNewTypedArrayConstructor(className, arguments);
                    return true;
                }

                return false;
        }
    }

    /// <summary>
    /// Attempts to emit an Intl namespace constructor.
    /// Returns true if handled.
    /// </summary>
    protected bool TryEmitIntlConstructor(List<string> namespaceParts, string className, List<Expr> arguments)
    {
        if (namespaceParts is not ["Intl"])
            return false;

        var runtime = Ctx.Runtime!;
        System.Reflection.MethodInfo? method = className switch
        {
            "NumberFormat" => runtime.CreateIntlNumberFormat,
            "DateTimeFormat" => runtime.CreateIntlDateTimeFormat,
            "Collator" => runtime.CreateIntlCollator,
            "PluralRules" => runtime.CreateIntlPluralRules,
            "RelativeTimeFormat" => runtime.CreateIntlRelativeTimeFormat,
            "ListFormat" => runtime.CreateIntlListFormat,
            "Segmenter" => runtime.CreateIntlSegmenter,
            "DisplayNames" => runtime.CreateIntlDisplayNames,
            _ => null
        };

        if (method == null)
            return false;

        EmitTwoArgConstructor(arguments, method);
        return true;
    }

    /// <summary>
    /// Attempts to emit a module-qualified constructor (e.g., new util.TextEncoder()).
    /// Returns true if handled.
    /// </summary>
    protected bool TryEmitModuleQualifiedConstructor(List<string> namespaceParts, string className, List<Expr> arguments)
    {
        if (namespaceParts.Count != 1)
            return false;

        return className switch
        {
            "TextEncoder" => TryEmitBuiltInConstructor("TextEncoder", arguments),
            "TextDecoder" => TryEmitBuiltInConstructor("TextDecoder", arguments),
            // "StringDecoder" — migrated to stdlib/node/string_decoder.ts.
            "PerformanceObserver" => TryEmitBuiltInConstructor("PerformanceObserver", arguments),
            "BroadcastChannel" => TryEmitBuiltInConstructor("BroadcastChannel", arguments),
            "ReadableStream" => TryEmitBuiltInConstructor("ReadableStream", arguments),
            "WritableStream" => TryEmitBuiltInConstructor("WritableStream", arguments),
            "TransformStream" => TryEmitBuiltInConstructor("TransformStream", arguments),
            "ByteLengthQueuingStrategy" => TryEmitBuiltInConstructor("ByteLengthQueuingStrategy", arguments),
            "CountQueuingStrategy" => TryEmitBuiltInConstructor("CountQueuingStrategy", arguments),
            "Agent" => TryEmitAgentConstructor(arguments),
            "Resolver" => TryEmitResolverConstructor(),
            "BlockList" => TryEmitBuiltInConstructor("BlockList", arguments),
            "SocketAddress" => TryEmitBuiltInConstructor("SocketAddress", arguments),
            _ => false
        };
    }

    private bool TryEmitAgentConstructor(List<Expr> arguments)
    {
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EnsureBoxed();
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }
        IL.Emit(OpCodes.Call, Ctx.Runtime!.HttpAgentFactory);
        _helpers.SetStackUnknown();
        return true;
    }

    private bool TryEmitResolverConstructor()
    {
        IL.Emit(OpCodes.Call, Ctx.Runtime!.DnsResolverFactory);
        _helpers.SetStackUnknown();
        return true;
    }

    #region Private constructor helpers

    private void EmitNewDateConstructor(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateDateNoArgs);
                break;
            case 1:
                EmitExpression(arguments[0]);
                EnsureBoxed();
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateDateFromValue);
                break;
            default:
                // new Date(year, month, day?, hours?, minutes?, seconds?, ms?)
                for (int i = 0; i < 7; i++)
                {
                    if (i < arguments.Count)
                        EmitExpressionAsDouble(arguments[i]);
                    else
                        IL.Emit(OpCodes.Ldc_R8, i == 2 ? 1.0 : 0.0);
                }
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateDateFromComponents);
                break;
        }
        SetStackUnknown();
    }

    private void EmitNewRegExpConstructor(List<Expr> arguments)
    {
        // ECMA-262 §22.2.4.1: new RegExp(pattern, flags)
        //   - If pattern is a RegExp object: P = pattern.[[OriginalSource]];
        //     if flags is undefined, F = pattern.[[OriginalFlags]] else ToString(flags).
        //   - Else: P = pattern undefined ? "" : ToString(pattern).
        //   - F = flags undefined ? "" : ToString(flags).
        // Route both args through a single helper that does the RegExp-copy
        // branch correctly — passing a $RegExp to Stringify would have built
        // `/source/flags` and the constructor would then treat that string as
        // the new source.

        switch (arguments.Count)
        {
            case 0:
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Call, Ctx.Runtime!.CreateRegExpWithFlags);
                break;
            case 1:
                EmitExpression(arguments[0]);
                EnsureBoxed();
                IL.Emit(OpCodes.Ldnull);          // flags = undefined / not supplied
                IL.Emit(OpCodes.Call, Ctx.Runtime!.RegExpFromArgs);
                break;
            default:
                EmitExpression(arguments[0]);
                EnsureBoxed();
                EmitExpression(arguments[1]);
                EnsureBoxed();
                IL.Emit(OpCodes.Call, Ctx.Runtime!.RegExpFromArgs);
                break;
        }
        SetStackUnknown();
    }

    private void EmitNewErrorConstructor(string errorTypeName, List<Expr> arguments)
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
        SetStackUnknown();
    }

    private void EmitNewTypedArrayConstructor(string typeName, List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            if (Ctx.Runtime!.TypedArrayFromObjectHelpers.TryGetValue(typeName, out var helper))
            {
                IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Call, helper);
            }
            else
                throw new CompileException($"Missing TypedArray helper for: {typeName}");
        }
        else if (arguments.Count == 1)
        {
            EmitExpression(arguments[0]);
            EnsureBoxed();
            if (Ctx.Runtime!.TypedArrayFromObjectHelpers.TryGetValue(typeName, out var helper))
                IL.Emit(OpCodes.Call, helper);
            else
                throw new CompileException($"Missing TypedArray helper for: {typeName}");
        }
        else
        {
            if (Ctx.Runtime!.TypedArrayFromBufferHelpers.TryGetValue(typeName, out var bufferHelper))
            {
                EmitExpression(arguments[0]);
                EnsureBoxed();
                if (arguments.Count > 1)
                    EmitExpressionAsDouble(arguments[1]);
                else
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                EmitBoxedArgOrNull(arguments, 2);
                IL.Emit(OpCodes.Call, bufferHelper);
            }
            else
                throw new CompileException($"Missing TypedArray buffer helper for: {typeName}");
        }
        SetStackUnknown();
    }

    private void EmitNewReadableConstructor(List<Expr> arguments)
    {
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSReadableCtor);
        if (arguments.Count > 0)
        {
            var instanceLocal = IL.DeclareLocal(Ctx.Runtime!.TSReadableType);
            IL.Emit(OpCodes.Stloc, instanceLocal);
            EmitExpression(arguments[0]);
            EnsureBoxed();
            var optionsLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, optionsLocal);
            var endLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Brfalse, endLabel);

            // Extract 'objectMode' property
            var skipObjectModeLabel = IL.DefineLabel();
            var afterObjectModeLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "objectMode");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, skipObjectModeLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Call, Ctx.Runtime!.TSReadableSetObjectMode);
            IL.Emit(OpCodes.Br, afterObjectModeLabel);
            IL.MarkLabel(skipObjectModeLabel);
            IL.Emit(OpCodes.Pop);
            IL.MarkLabel(afterObjectModeLabel);

            // Extract 'highWaterMark' property and call SetHighWaterMark
            {
                var skipHwmLabel = IL.DefineLabel();
                var hwmValLocal = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Ldloc, optionsLocal);
                IL.Emit(OpCodes.Ldstr, "highWaterMark");
                IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
                IL.Emit(OpCodes.Stloc, hwmValLocal);
                // Skip if null
                IL.Emit(OpCodes.Ldloc, hwmValLocal);
                IL.Emit(OpCodes.Brfalse, skipHwmLabel);
                // Skip if $Undefined
                IL.Emit(OpCodes.Ldloc, hwmValLocal);
                IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
                IL.Emit(OpCodes.Brtrue, skipHwmLabel);
                // Convert to int via Convert.ToInt32
                IL.Emit(OpCodes.Ldloc, instanceLocal);
                IL.Emit(OpCodes.Ldloc, hwmValLocal);
                IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
                IL.Emit(OpCodes.Call, Ctx.Runtime!.TSReadableSetHighWaterMark!);
                IL.MarkLabel(skipHwmLabel);
            }

            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }
        SetStackUnknown();
    }

    private void EmitNewWritableConstructor(List<Expr> arguments)
    {
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSWritableCtor);
        if (arguments.Count > 0)
        {
            var optionsNullLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            var instanceLocal = IL.DeclareLocal(Ctx.Runtime!.TSWritableType);
            IL.Emit(OpCodes.Stloc, instanceLocal);
            EmitExpression(arguments[0]);
            EnsureBoxed();
            var optionsLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, optionsLocal);
            IL.Emit(OpCodes.Brfalse, optionsNullLabel);

            // Extract 'write' callback
            var skipWriteLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "write");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            var writeCallbackLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, writeCallbackLocal);
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            IL.Emit(OpCodes.Brfalse, skipWriteLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSWritableType.GetMethod("SetWriteCallback")!);
            IL.MarkLabel(skipWriteLabel);

            // Extract 'final' callback
            var skipFinalLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "final");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            var finalCallbackLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, finalCallbackLocal);
            IL.Emit(OpCodes.Ldloc, finalCallbackLocal);
            IL.Emit(OpCodes.Brfalse, skipFinalLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, finalCallbackLocal);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSWritableType.GetMethod("SetFinalCallback")!);
            IL.MarkLabel(skipFinalLabel);

            // Extract 'objectMode' — check if value is boxed true (not just non-null)
            {
                var skipObjectModeLabel = IL.DefineLabel();
                var objectModeValLocal = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Ldloc, optionsLocal);
                IL.Emit(OpCodes.Ldstr, "objectMode");
                IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
                IL.Emit(OpCodes.Stloc, objectModeValLocal);
                // Check if value is boxed Boolean true
                IL.Emit(OpCodes.Ldloc, objectModeValLocal);
                IL.Emit(OpCodes.Isinst, typeof(bool));
                IL.Emit(OpCodes.Brfalse, skipObjectModeLabel);
                IL.Emit(OpCodes.Ldloc, objectModeValLocal);
                IL.Emit(OpCodes.Unbox_Any, typeof(bool));
                IL.Emit(OpCodes.Brfalse, skipObjectModeLabel);
                IL.Emit(OpCodes.Ldloc, instanceLocal);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSWritableType.GetMethod("SetObjectMode")!);
                IL.MarkLabel(skipObjectModeLabel);
            }

            // Extract 'autoDestroy' — check if value is boxed true
            {
                var skipAutoDestroyLabel = IL.DefineLabel();
                var autoDestroyValLocal = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Ldloc, optionsLocal);
                IL.Emit(OpCodes.Ldstr, "autoDestroy");
                IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
                IL.Emit(OpCodes.Stloc, autoDestroyValLocal);
                IL.Emit(OpCodes.Ldloc, autoDestroyValLocal);
                IL.Emit(OpCodes.Isinst, typeof(bool));
                IL.Emit(OpCodes.Brfalse, skipAutoDestroyLabel);
                IL.Emit(OpCodes.Ldloc, autoDestroyValLocal);
                IL.Emit(OpCodes.Unbox_Any, typeof(bool));
                IL.Emit(OpCodes.Brfalse, skipAutoDestroyLabel);
                IL.Emit(OpCodes.Ldloc, instanceLocal);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSWritableType.GetMethod("SetAutoDestroy")!);
                IL.MarkLabel(skipAutoDestroyLabel);
            }

            // Extract 'highWaterMark'
            {
                var skipHwmLabel = IL.DefineLabel();
                var hwmValLocal = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Ldloc, optionsLocal);
                IL.Emit(OpCodes.Ldstr, "highWaterMark");
                IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
                IL.Emit(OpCodes.Stloc, hwmValLocal);
                IL.Emit(OpCodes.Ldloc, hwmValLocal);
                IL.Emit(OpCodes.Brfalse, skipHwmLabel);
                IL.Emit(OpCodes.Ldloc, hwmValLocal);
                IL.Emit(OpCodes.Isinst, Ctx.Runtime!.UndefinedType);
                IL.Emit(OpCodes.Brtrue, skipHwmLabel);
                IL.Emit(OpCodes.Ldloc, instanceLocal);
                IL.Emit(OpCodes.Ldloc, hwmValLocal);
                IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
                IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSWritableType.GetMethod("SetHighWaterMark")!);
                IL.MarkLabel(skipHwmLabel);
            }

            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(optionsNullLabel);
            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }
        SetStackUnknown();
    }

    private void EmitNewDuplexConstructor(List<Expr> arguments)
    {
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSDuplexCtor);
        if (arguments.Count > 0)
        {
            var optionsLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            var instanceLocal = IL.DeclareLocal(Ctx.Runtime!.TSDuplexType);
            IL.Emit(OpCodes.Stloc, instanceLocal);
            EmitExpression(arguments[0]);
            EnsureBoxed();
            var optionsLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, optionsLocal);
            IL.Emit(OpCodes.Brfalse, optionsLabel);

            // Extract 'write' callback
            var skipWriteLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "write");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            var writeCallbackLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, writeCallbackLocal);
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            IL.Emit(OpCodes.Brfalse, skipWriteLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSDuplexType.GetMethod("SetWriteCallback")!);
            IL.MarkLabel(skipWriteLabel);

            // Extract 'objectMode'
            var skipObjectModeLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "objectMode");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, skipObjectModeLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSDuplexType.GetMethod("SetObjectMode")!);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(skipObjectModeLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(optionsLabel);
            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }
        SetStackUnknown();
    }

    private void EmitNewTransformConstructor(List<Expr> arguments)
    {
        IL.Emit(OpCodes.Newobj, Ctx.Runtime!.TSTransformCtor);
        if (arguments.Count > 0)
        {
            var optionsLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            var instanceLocal = IL.DeclareLocal(Ctx.Runtime!.TSTransformType);
            IL.Emit(OpCodes.Stloc, instanceLocal);
            EmitExpression(arguments[0]);
            EnsureBoxed();
            var optionsLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, optionsLocal);
            IL.Emit(OpCodes.Brfalse, optionsLabel);

            // Get 'transform' callback
            var afterTransformLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "transform");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            var transformCallbackLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, transformCallbackLocal);
            IL.Emit(OpCodes.Ldloc, transformCallbackLocal);
            IL.Emit(OpCodes.Brfalse, afterTransformLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, transformCallbackLocal);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSTransformType.GetMethod("SetTransformCallback")!);
            IL.MarkLabel(afterTransformLabel);

            // Get 'flush' callback
            var afterFlushLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "flush");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            var flushCallbackLocal = IL.DeclareLocal(typeof(object));
            IL.Emit(OpCodes.Stloc, flushCallbackLocal);
            IL.Emit(OpCodes.Ldloc, flushCallbackLocal);
            IL.Emit(OpCodes.Brfalse, afterFlushLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, flushCallbackLocal);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSTransformType.GetMethod("SetFlushCallback")!);
            IL.MarkLabel(afterFlushLabel);

            // Extract 'objectMode' (Transform extends Duplex)
            var skipObjectModeLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, optionsLocal);
            IL.Emit(OpCodes.Ldstr, "objectMode");
            IL.Emit(OpCodes.Call, Ctx.Runtime!.GetProperty);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, skipObjectModeLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Callvirt, Ctx.Runtime!.TSDuplexType.GetMethod("SetObjectMode")!);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(skipObjectModeLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(optionsLabel);
            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }
        SetStackUnknown();
    }

    #endregion
}
