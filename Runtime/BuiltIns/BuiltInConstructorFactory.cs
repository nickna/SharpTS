using System.Globalization;
using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Factory for creating built-in JavaScript objects.
/// Centralizes constructor logic that was previously scattered across the Interpreter.
/// </summary>
public static class BuiltInConstructorFactory
{
    /// <summary>
    /// Delegate for built-in constructor handlers.
    /// </summary>
    /// <param name="args">Evaluated constructor arguments.</param>
    /// <returns>The constructed object.</returns>
    public delegate object? ConstructorHandler(IReadOnlyList<object?> args);

    /// <summary>
    /// Registry of simple built-in constructors (those that don't need special handling).
    /// Maps constructor name to handler function.
    /// </summary>
    private static readonly Dictionary<string, ConstructorHandler> _simpleConstructors = new(StringComparer.Ordinal)
    {
        [BuiltInNames.Date] = CreateDate,
        [BuiltInNames.RegExp] = CreateRegExp,
        [BuiltInNames.Map] = CreateMap,
        [BuiltInNames.Set] = CreateSet,
        [BuiltInNames.WeakMap] = _ => new SharpTSWeakMap(),
        [BuiltInNames.WeakSet] = _ => new SharpTSWeakSet(),
        [BuiltInNames.WeakRef] = args => new SharpTSWeakRef(args.Count > 0 ? args[0] : null),
        [BuiltInNames.FinalizationRegistry] = args =>
        {
            if (args.Count < 1 || args[0] is not ISharpTSCallable callback)
                throw new Exception("Runtime Error: FinalizationRegistry constructor requires a callback function.");
            return new SharpTSFinalizationRegistry(callback);
        },
        [BuiltInNames.EventEmitter] = CreateEventEmitter,
        [BuiltInNames.AbortController] = _ => new SharpTSAbortController(),
        [BuiltInNames.Headers] = CreateHeaders,
        // URL / URLSearchParams — migrated to stdlib/node/url.ts; no built-in
        // global constructor. Users must `import { URL } from 'url'`.
        [BuiltInNames.Proxy] = args =>
        {
            if (args.Count != 2)
                throw new Exception("Runtime Error: Proxy constructor requires exactly 2 arguments (target, handler).");
            return new SharpTSProxy(args[0]!, args[1]!);
        },
        [BuiltInNames.Request] = CreateRequest,
        [BuiltInNames.Response] = CreateResponse,
        [BuiltInNames.ByteLengthQueuingStrategy] = CreateByteLengthQueuingStrategy,
        [BuiltInNames.CountQueuingStrategy] = CreateCountQueuingStrategy,
        // TextEncoder / TextDecoder — registered here so bare references
        // (`const E = TextEncoder`, `x instanceof TextEncoder`, and stdlib
        // re-exports in util.ts) resolve. `new TextEncoder()` inside user
        // code continues to use the same underlying runtime type.
        [BuiltInNames.TextEncoder] = _ => new SharpTSTextEncoder(),
        [BuiltInNames.TextDecoder] = args =>
        {
            var encoding = args.Count > 0 ? args[0]?.ToString() ?? "utf-8" : "utf-8";
            return new SharpTSTextDecoder(encoding, fatal: false, ignoreBOM: false);
        },
        [BuiltInNames.Blob] = CreateBlob,
        [BuiltInNames.File] = CreateFile,
    };

    /// <summary>
    /// Checks if a constructor name is any kind of built-in handled by this factory.
    /// Note: Promise is NOT included as it requires special executor function handling.
    /// </summary>
    public static bool IsBuiltIn(string name) =>
        _simpleConstructors.ContainsKey(name) ||
        BuiltInNames.IsTypedArrayName(name) ||
        name == BuiltInNames.MessageChannel ||
        name == BuiltInNames.SharedArrayBuffer ||
        name == BuiltInNames.ArrayBuffer ||
        name == BuiltInNames.BroadcastChannel ||
        name == BuiltInNames.ReadableStream ||
        name == BuiltInNames.WritableStream ||
        name == BuiltInNames.TransformStream ||
        name == "Number" || name == "String" || name == "Boolean";
        // Note: Error types are NOT handled here — they go through SharpTSErrorClass
        // registered in Interpreter.CreateGlobalsLookup()

    /// <summary>
    /// Exposes the simple constructor registry for global variable registration.
    /// </summary>
    public static IReadOnlyDictionary<string, ConstructorHandler> GetConstructors()
        => _simpleConstructors;

    /// <summary>
    /// Creates a built-in object using the appropriate constructor.
    /// </summary>
    /// <param name="name">The constructor name (e.g., "Date", "Map").</param>
    /// <param name="args">Evaluated constructor arguments.</param>
    /// <param name="interpreter">The interpreter instance (needed for some constructors).</param>
    /// <returns>The constructed object, or null if not a recognized built-in.</returns>
    public static object? TryCreate(string name, IReadOnlyList<object?> args, Interpreter? interpreter = null)
    {
        // ECMA-262 §22.2.4.1 `new RegExp(...)`: when the interpreter is available,
        // use the brand-aware path (IsRegExp + regexp-like source/flags via Get,
        // honoring user getters/throws). The static CreateRegExp below stays the
        // fallback for interpreter-less callers.
        if (name == BuiltInNames.RegExp && interpreter != null)
        {
            return RegExpBuiltIns.ConstructRegExp(interpreter, args, isCallForm: false);
        }

        // Primitive wrapper constructors: `new Number(x)`, `new String(x)`, `new Boolean(x)`
        // return boxed SharpTSObjects with __primitiveType / __primitiveValue markers,
        // matching compiled-mode behaviour so typeof is "object" and instanceof works.
        if (name == "Number") return CreateBoxedNumber(args, interpreter);
        if (name == "String") return CreateBoxedString(args, interpreter);
        if (name == "Boolean") return CreateBoxedBoolean(args, interpreter);

        // Check simple constructors first
        if (_simpleConstructors.TryGetValue(name, out var handler))
        {
            return handler(args);
        }

        // Check TypedArray constructors
        if (BuiltInNames.IsTypedArrayName(name))
        {
            return WorkerBuiltIns.GetTypedArrayConstructor(name).Call(interpreter!, args.ToList());
        }

        // Note: Error constructors are handled by SharpTSErrorClass (registered as globals)

        // Check MessageChannel and SharedArrayBuffer (need interpreter)
        if (name == BuiltInNames.MessageChannel)
        {
            return WorkerBuiltIns.MessageChannelConstructor.Call(interpreter!, args.ToList());
        }

        if (name == BuiltInNames.SharedArrayBuffer)
        {
            return WorkerBuiltIns.SharedArrayBufferConstructor.Call(interpreter!, args.ToList());
        }

        if (name == BuiltInNames.ArrayBuffer)
        {
            return WorkerBuiltIns.ArrayBufferConstructor.Call(interpreter!, args.ToList());
        }

        if (name == BuiltInNames.BroadcastChannel)
        {
            // BroadcastChannel needs the interpreter wired so message delivery can be
            // scheduled on the correct event loop.
            if (args.Count < 1)
                throw new Exception("Runtime Error: BroadcastChannel constructor requires a name argument.");
            var channelName = args[0]?.ToString() ?? throw new Exception("Runtime Error: BroadcastChannel name must be a string.");
            return new SharpTSBroadcastChannel(channelName) { OwnerInterpreter = interpreter };
        }

        if (name == BuiltInNames.ReadableStream)
        {
            var src = args.Count > 0 ? args[0] : null;
            var strat = args.Count > 1 ? args[1] : null;
            return new SharpTSReadableStream(interpreter, src, strat);
        }
        if (name == BuiltInNames.WritableStream)
        {
            var sink = args.Count > 0 ? args[0] : null;
            var strat = args.Count > 1 ? args[1] : null;
            return new SharpTSWritableStream(interpreter, sink, strat);
        }
        if (name == BuiltInNames.TransformStream)
        {
            var transformer = args.Count > 0 ? args[0] : null;
            var ws = args.Count > 1 ? args[1] : null;
            var rs = args.Count > 2 ? args[2] : null;
            return new SharpTSTransformStream(interpreter, transformer, ws, rs);
        }

        return null;
    }

    /// <summary>
    /// RuntimeValue-returning overload of TryCreate.
    /// </summary>
    public static RuntimeValue TryCreateRV(string className, List<object?> args, Interpreter interpreter)
        => RuntimeValue.FromBoxed(TryCreate(className, args, interpreter));

    /// <summary>
    /// ECMA-262 §7.1.18 ToObject for the primitive cases: wraps a
    /// <c>string</c>/<c>number</c>/<c>boolean</c> primitive in its boxed wrapper
    /// object (so <c>typeof</c> is <c>"object"</c> and <c>instanceof</c> works),
    /// reusing the same <c>new String/Number/Boolean</c> layout as #360. Every
    /// other value — already-object values, arrays, <c>null</c>, <c>undefined</c>,
    /// symbols, bigint — is returned unchanged; callers that must reject
    /// <c>null</c>/<c>undefined</c> guard before calling. Mirrors compiled mode's
    /// <c>$Runtime.ToObject</c> (see <c>RuntimeEmitter.BoxedPrimitives.EmitToObject</c>).
    /// </summary>
    public static object? ToObject(object? value, Interpreter? interpreter = null) => value switch
    {
        string => CreateBoxedString(new[] { value }, interpreter),
        double => CreateBoxedNumber(new[] { value }, interpreter),
        bool => CreateBoxedBoolean(new[] { value }, interpreter),
        _ => value,
    };

    #region Constructor Implementations

    private static object CreateDate(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
            return new SharpTSDate();

        if (args.Count == 1)
        {
            var arg = args[0];
            return arg switch
            {
                double timestamp => new SharpTSDate(timestamp),
                string dateStr => new SharpTSDate(dateStr),
                SharpTSDate date => new SharpTSDate(date.GetTime()),
                _ => new SharpTSDate()
            };
        }

        // Multiple args: year, month, day?, hours?, minutes?, seconds?, ms?
        int year = args.Count > 0 && args[0] is double y ? (int)y : 0;
        int month = args.Count > 1 && args[1] is double mo ? (int)mo : 0;
        int day = args.Count > 2 && args[2] is double d ? (int)d : 1;
        int hours = args.Count > 3 && args[3] is double h ? (int)h : 0;
        int minutes = args.Count > 4 && args[4] is double mi ? (int)mi : 0;
        int seconds = args.Count > 5 && args[5] is double s ? (int)s : 0;
        int milliseconds = args.Count > 6 && args[6] is double ms ? (int)ms : 0;

        return new SharpTSDate(year, month, day, hours, minutes, seconds, milliseconds);
    }

    private static object CreateRegExp(IReadOnlyList<object?> args)
    {
        // ECMA-262 §22.2.4.1: undefined pattern/flags coerce to "" (NOT the
        // string "undefined" — SharpTSUndefined.ToString() would give that and
        // surface as bogus flags). When pattern is itself a RegExp, copy its
        // source and (when flags is undefined) its flags rather than stringifying
        // it to "/source/flags". Mirrors the compiled RegExpFromArgs/RegExpCoerceArg.
        object? patternArg = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        object? flagsArg = args.Count > 1 ? args[1] : SharpTSUndefined.Instance;
        string pattern, flags;
        if (patternArg is SharpTSRegExp rx)
        {
            pattern = rx.Source;
            flags = flagsArg is null or SharpTSUndefined ? rx.Flags : flagsArg.ToString() ?? "";
        }
        else
        {
            pattern = patternArg is null or SharpTSUndefined ? "" : patternArg.ToString() ?? "";
            flags = flagsArg is null or SharpTSUndefined ? "" : flagsArg.ToString() ?? "";
        }
        return new SharpTSRegExp(pattern, flags);
    }

    private static object CreateMap(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
            return new SharpTSMap();

        // Handle new Map([[k1, v1], [k2, v2], ...])
        if (args[0] is SharpTSArray entriesArray)
            return SharpTSMap.FromEntries(entriesArray);

        return new SharpTSMap();
    }

    private static object CreateSet(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
            return new SharpTSSet();

        // Handle new Set([v1, v2, v3, ...])
        if (args[0] is SharpTSArray valuesArray)
            return SharpTSSet.FromArray(valuesArray);

        return new SharpTSSet();
    }

    private static object CreateByteLengthQueuingStrategy(IReadOnlyList<object?> args)
    {
        return new SharpTSByteLengthQueuingStrategy(ExtractQueuingStrategyHwm(args));
    }

    private static object CreateCountQueuingStrategy(IReadOnlyList<object?> args)
    {
        return new SharpTSCountQueuingStrategy(ExtractQueuingStrategyHwm(args));
    }

    private static double ExtractQueuingStrategyHwm(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is null) return 0.0;
        if (StreamFields.TryGet(args[0], "highWaterMark", out var h))
        {
            return h switch { double d => d, int i => i, long l => l, _ => 0.0 };
        }
        return 0.0;
    }

    private static object CreateEventEmitter(IReadOnlyList<object?> args)
    {
        var emitter = new SharpTSEventEmitter();
        // Node's EventEmitter accepts an optional { captureRejections?: boolean }.
        if (args.Count > 0 && args[0] is SharpTSObject options
            && options.GetProperty("captureRejections") is bool capture && capture)
        {
            emitter.CaptureRejectionsEnabled = true;
        }
        return emitter;
    }

    private static object CreateHeaders(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
            return new SharpTSHeaders();

        // Handle new Headers({ "content-type": "text/html", ... })
        if (args[0] is SharpTSObject obj)
            return new SharpTSHeaders(obj);

        return new SharpTSHeaders();
    }

    private static object CreateBlob(IReadOnlyList<object?> args)
    {
        var parts = args.Count > 0 ? args[0] as IEnumerable<object?> : null;
        var (type, endings) = ReadBlobOptions(args.Count > 1 ? args[1] : null);
        return SharpTSBlob.FromParts(parts, type, endings);
    }

    private static object CreateFile(IReadOnlyList<object?> args)
    {
        var parts = args.Count > 0 ? args[0] as IEnumerable<object?> : null;
        var name = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
        var (type, endings) = ReadBlobOptions(args.Count > 2 ? args[2] : null);
        double lastModified = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (args.Count > 2 && args[2] is SharpTSObject opts && opts.GetProperty("lastModified") is double lm)
            lastModified = lm;
        return SharpTSFile.FromParts(parts, name, type, lastModified, endings);
    }

    private static (string type, string endings) ReadBlobOptions(object? options)
    {
        string type = "";
        string endings = "transparent";
        if (options is SharpTSObject opts)
        {
            type = opts.GetProperty("type")?.ToString() ?? "";
            endings = opts.GetProperty("endings")?.ToString() ?? "transparent";
        }
        return (type, endings);
    }

    private static object CreateRequest(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: Request constructor requires at least 1 argument (url)");

        var url = args[0]?.ToString() ?? "";
        var init = args.Count > 1 ? args[1] as SharpTSObject : null;
        return new SharpTSRequest(url, init);
    }

    private static object CreateResponse(IReadOnlyList<object?> args)
    {
        var body = args.Count > 0 ? args[0] : null;
        var init = args.Count > 1 ? args[1] as SharpTSObject : null;
        return new SharpTSResponse(body, init);
    }

    // ── Boxed primitive wrapper constructors ─────────────────────────────────

    /// <summary>
    /// <c>new Number(x)</c>: ECMA-262 §21.1.2. Returns a <c>SharpTSObject</c>
    /// wrapper with <c>__primitiveType="Number"</c> and <c>__primitiveValue</c>
    /// holding the ToNumber-coerced argument.
    /// </summary>
    private static SharpTSObject CreateBoxedNumber(
        IReadOnlyList<object?> args, Interpreter? interpreter = null)
    {
        var arg = args.Count > 0 ? args[0] : null;
        double value;
        if (arg is SharpTSBigInt bigint)
        {
            // Number is the explicit BigInt-to-Number conversion; unlike implicit
            // ToNumber, both call and construct forms accept it.
            value = NumberBuiltIns.BigIntToNumber(bigint.Value);
        }
        else if (interpreter != null)
        {
            value = interpreter.ToNumberWithPrimitive(arg);
        }
        else
        {
            value = arg switch
            {
                double d => d,
                null => 0.0,
                SharpTSUndefined => double.NaN,
                bool b => b ? 1.0 : 0.0,
                string s => ParseNumberFromString(s),
                _ => double.NaN,
            };
        }
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["__primitiveType"] = "Number",
            ["__primitiveValue"] = value,
        })
        {
            Prototype = interpreter?.GetNumberPrototype(),
        };
    }

    /// <summary>
    /// <c>new String(x)</c>: ECMA-262 §22.1.2. Returns a <c>SharpTSObject</c>
    /// String exotic wrapper with <c>__primitiveType="String"</c>,
    /// <c>__primitiveValue</c>, a <c>length</c> slot, and indexed character slots.
    /// </summary>
    private static SharpTSObject CreateBoxedString(
        IReadOnlyList<object?> args,
        Interpreter? interpreter = null)
    {
        string value;
        if (args.Count == 0)
        {
            value = "";
        }
        else if (interpreter != null)
        {
            value = interpreter.ToStringForBuiltInArgument(args[0]);
        }
        else
        {
            value = args[0] switch
            {
                null => "null",
                SharpTSUndefined => "undefined",
                bool b => b ? "true" : "false",
                double d => RuntimeTypes.Stringify(d),
                string s => s,
                SharpTSArray arr => arr.ToString()!,
                _ => args[0]?.ToString() ?? "",
            };
        }
        var dict = new Dictionary<string, object?>
        {
            ["__primitiveType"] = "String",
            ["__primitiveValue"] = value,
            ["length"] = (double)value.Length,
        };
        for (int i = 0; i < value.Length; i++)
            dict[i.ToString()] = value[i].ToString();
        var wrapper = new SharpTSObject(dict)
        {
            Prototype = interpreter?.GetStringPrototype(),
        };
        // String exotic indexed properties are enumerable but immutable, while
        // `length` is immutable and non-enumerable (§10.4.3 / §22.1.4.1).
        for (int i = 0; i < value.Length; i++)
        {
            wrapper.DefineProperty(i.ToString(), new SharpTSPropertyDescriptor
            {
                Value = value[i].ToString(),
                HasValue = true,
                Writable = false,
                Enumerable = true,
                Configurable = false,
            });
        }
        wrapper.DefineProperty("length", new SharpTSPropertyDescriptor
        {
            Value = (double)value.Length,
            HasValue = true,
            Writable = false,
            Enumerable = false,
            Configurable = false,
        });
        return wrapper;
    }

    /// <summary>
    /// <c>new Boolean(x)</c>: ECMA-262 §20.4.2. Returns a <c>SharpTSObject</c>
    /// wrapper with <c>__primitiveType="Boolean"</c> and the ToBoolean-coerced value.
    /// </summary>
    private static SharpTSObject CreateBoxedBoolean(
        IReadOnlyList<object?> args, Interpreter? interpreter = null)
    {
        var arg = args.Count > 0 ? args[0] : null;
        bool value = RuntimeTypes.IsTruthy(arg);
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["__primitiveType"] = "Boolean",
            ["__primitiveValue"] = value,
        })
        {
            Prototype = interpreter?.GetBooleanPrototype(),
        };
    }

    private static double ParseNumberFromString(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return 0.0;
        if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
        if (s == "-Infinity") return double.NegativeInfinity;
        if (s.Contains("infinity", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        return double.NaN;
    }

    #endregion
}
