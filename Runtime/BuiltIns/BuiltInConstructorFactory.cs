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
        [BuiltInNames.EventEmitter] = _ => new SharpTSEventEmitter(),
    };

    /// <summary>
    /// Checks if a constructor name is a simple built-in that can be handled by this factory.
    /// </summary>
    public static bool IsSimpleBuiltIn(string name) => _simpleConstructors.ContainsKey(name);

    /// <summary>
    /// Checks if a constructor name is any kind of built-in handled by this factory.
    /// Note: Promise is NOT included as it requires special executor function handling.
    /// </summary>
    public static bool IsBuiltIn(string name) =>
        _simpleConstructors.ContainsKey(name) ||
        BuiltInNames.IsTypedArrayName(name) ||
        BuiltInNames.IsErrorTypeName(name) ||
        name == BuiltInNames.MessageChannel ||
        name == BuiltInNames.SharedArrayBuffer;

    /// <summary>
    /// Creates a built-in object using the appropriate constructor.
    /// </summary>
    /// <param name="name">The constructor name (e.g., "Date", "Map").</param>
    /// <param name="args">Evaluated constructor arguments.</param>
    /// <param name="interpreter">The interpreter instance (needed for some constructors).</param>
    /// <returns>The constructed object, or null if not a recognized built-in.</returns>
    public static object? TryCreate(string name, IReadOnlyList<object?> args, Interpreter? interpreter = null)
    {
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

        // Check Error constructors
        if (BuiltInNames.IsErrorTypeName(name))
        {
            return ErrorBuiltIns.CreateError(name, args.ToList());
        }

        // Check MessageChannel and SharedArrayBuffer (need interpreter)
        if (name == BuiltInNames.MessageChannel)
        {
            return WorkerBuiltIns.MessageChannelConstructor.Call(interpreter!, args.ToList());
        }

        if (name == BuiltInNames.SharedArrayBuffer)
        {
            return WorkerBuiltIns.SharedArrayBufferConstructor.Call(interpreter!, args.ToList());
        }

        return null;
    }

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
        var pattern = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
        var flags = args.Count > 1 ? args[1]?.ToString() ?? "" : "";
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

    #endregion
}
