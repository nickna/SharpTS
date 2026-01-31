using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Validates that data passed to workers (workerData, postMessage) doesn't contain functions.
/// </summary>
/// <remarks>
/// Workers require explicit data passing via the structured clone algorithm.
/// Functions and closures cannot be cloned and must be rejected at compile time.
/// This validator is used during type checking to provide early error messages.
/// </remarks>
public static class WorkerDataValidator
{
    /// <summary>
    /// Validates that a type can be serialized for worker communication.
    /// </summary>
    /// <param name="type">The type to validate.</param>
    /// <param name="context">Description of the context (e.g., "workerData", "postMessage").</param>
    /// <exception cref="Exception">Thrown with a type error if the type is not serializable.</exception>
    public static void ValidateSerializable(TypeInfo type, string context)
    {
        var (isValid, reason) = CheckSerializable(type, new HashSet<TypeInfo>());
        if (!isValid)
        {
            throw new Exception($"Type Error: Cannot pass {reason} to worker via {context}.\n" +
                               "Functions are not serializable for cross-thread transfer.\n" +
                               "Use plain objects, arrays, and primitives only.");
        }
    }

    /// <summary>
    /// Validates an expression's type for worker data transfer.
    /// </summary>
    /// <param name="expr">The expression being validated.</param>
    /// <param name="type">The type of the expression.</param>
    /// <param name="context">Description of the context.</param>
    public static void ValidateExpression(Expr expr, TypeInfo type, string context)
    {
        // Arrow functions are obviously non-serializable
        if (expr is Expr.ArrowFunction)
        {
            throw new Exception($"Type Error: Cannot pass arrow function to worker via {context}.\n" +
                               "Functions are not serializable for cross-thread transfer.");
        }

        // Validate the type
        ValidateSerializable(type, context);
    }

    /// <summary>
    /// Checks if a type is serializable, returning the reason if not.
    /// </summary>
    private static (bool IsValid, string? Reason) CheckSerializable(TypeInfo type, HashSet<TypeInfo> seen)
    {
        // Prevent infinite recursion
        if (!seen.Add(type))
            return (true, null);

        return type switch
        {
            // Primitives are always serializable
            TypeInfo.Primitive => (true, null),
            TypeInfo.StringLiteral => (true, null),
            TypeInfo.NumberLiteral => (true, null),
            TypeInfo.BooleanLiteral => (true, null),
            TypeInfo.String => (true, null),
            TypeInfo.BigInt => (true, null),
            TypeInfo.Null => (true, null),
            TypeInfo.Undefined => (true, null),
            TypeInfo.Any => (true, null), // any could be anything, allow it (runtime will catch)
            TypeInfo.Unknown => (true, null), // same for unknown

            // SharedArrayBuffer is shared by reference (the whole point!)
            TypeInfo.SharedArrayBuffer => (true, null),

            // TypedArrays are serializable (views are recreated)
            TypeInfo.TypedArray => (true, null),

            // Buffer is serializable (bytes are copied)
            TypeInfo.Buffer => (true, null),

            // Date, RegExp, Error are serializable
            TypeInfo.Date => (true, null),
            TypeInfo.RegExp => (true, null),
            TypeInfo.Error => (true, null),

            // Map and Set are serializable
            TypeInfo.Map map => CheckMapSerializable(map, seen),
            TypeInfo.Set set => CheckSerializable(set.ElementType, seen),

            // Arrays are serializable if their elements are
            TypeInfo.Array arr => CheckSerializable(arr.ElementType, seen),

            // Tuples are serializable if all elements are
            TypeInfo.Tuple tuple => CheckTupleSerializable(tuple, seen),

            // Records/objects are serializable if all properties are
            TypeInfo.Record rec => CheckRecordSerializable(rec, seen),

            // Functions are NOT serializable
            TypeInfo.Function => (false, "function"),
            TypeInfo.GenericFunction => (false, "function"),
            TypeInfo.OverloadedFunction => (false, "function"),
            TypeInfo.GenericOverloadedFunction => (false, "function"),

            // Class instances are NOT serializable (they have methods)
            TypeInfo.Instance => (false, "class instance"),
            TypeInfo.Class => (false, "class constructor"),
            TypeInfo.GenericClass => (false, "class"),
            TypeInfo.MutableClass => (false, "class"),

            // Promises are NOT serializable
            TypeInfo.Promise => (false, "Promise"),

            // Symbols are NOT serializable (unique identity)
            TypeInfo.Symbol => (false, "Symbol"),
            TypeInfo.UniqueSymbol => (false, "unique symbol"),

            // WeakMap/WeakSet are NOT serializable
            TypeInfo.WeakMap => (false, "WeakMap"),
            TypeInfo.WeakSet => (false, "WeakSet"),

            // Generators/Iterators are NOT serializable
            TypeInfo.Generator => (false, "Generator"),
            TypeInfo.AsyncGenerator => (false, "AsyncGenerator"),
            TypeInfo.Iterator => (false, "Iterator"),

            // EventEmitter is NOT serializable
            TypeInfo.EventEmitter => (false, "EventEmitter"),

            // Worker is NOT serializable
            TypeInfo.Worker => (false, "Worker"),

            // MessagePort can be transferred but not in workerData
            // (it's serializable only in the transfer list context)
            TypeInfo.MessagePort => (false, "MessagePort (must be in transferList)"),

            // Union types - check all members
            TypeInfo.Union union => CheckUnionSerializable(union, seen),

            // Intersection types - check all members
            TypeInfo.Intersection inter => CheckIntersectionSerializable(inter, seen),

            // Interfaces - check if callable or has function members
            TypeInfo.Interface iface => CheckInterfaceSerializable(iface, seen),
            TypeInfo.GenericInterface => (false, "generic interface (may contain methods)"),

            // InstantiatedGeneric - depends on what it is
            TypeInfo.InstantiatedGeneric ig => CheckInstantiatedGenericSerializable(ig, seen),

            // Type parameters - can't know at compile time, allow it
            TypeInfo.TypeParameter => (true, null),

            // Void and never - technically not passable but also not errors
            TypeInfo.Void => (true, null),
            TypeInfo.Never => (true, null),

            // Other types - allow by default (runtime will catch issues)
            _ => (true, null)
        };
    }

    private static (bool, string?) CheckMapSerializable(TypeInfo.Map map, HashSet<TypeInfo> seen)
    {
        var keyCheck = CheckSerializable(map.KeyType, seen);
        if (!keyCheck.IsValid)
            return (false, $"Map with non-serializable key type ({keyCheck.Reason})");

        var valueCheck = CheckSerializable(map.ValueType, seen);
        if (!valueCheck.IsValid)
            return (false, $"Map with non-serializable value type ({valueCheck.Reason})");

        return (true, null);
    }

    private static (bool, string?) CheckTupleSerializable(TypeInfo.Tuple tuple, HashSet<TypeInfo> seen)
    {
        foreach (var elem in tuple.Elements)
        {
            var check = CheckSerializable(elem.Type, seen);
            if (!check.IsValid)
                return (false, $"tuple containing {check.Reason}");
        }
        return (true, null);
    }

    private static (bool, string?) CheckRecordSerializable(TypeInfo.Record rec, HashSet<TypeInfo> seen)
    {
        foreach (var (name, fieldType) in rec.Fields)
        {
            var check = CheckSerializable(fieldType, seen);
            if (!check.IsValid)
                return (false, $"object with non-serializable property '{name}' ({check.Reason})");
        }
        return (true, null);
    }

    private static (bool, string?) CheckUnionSerializable(TypeInfo.Union union, HashSet<TypeInfo> seen)
    {
        foreach (var member in union.FlattenedTypes)
        {
            var check = CheckSerializable(member, seen);
            if (!check.IsValid)
                return check; // Return the specific non-serializable type
        }
        return (true, null);
    }

    private static (bool, string?) CheckIntersectionSerializable(TypeInfo.Intersection inter, HashSet<TypeInfo> seen)
    {
        foreach (var member in inter.FlattenedTypes)
        {
            var check = CheckSerializable(member, seen);
            if (!check.IsValid)
                return check;
        }
        return (true, null);
    }

    private static (bool, string?) CheckInterfaceSerializable(TypeInfo.Interface iface, HashSet<TypeInfo> seen)
    {
        // Callable interfaces are functions
        if (iface.IsCallable)
            return (false, $"callable interface '{iface.Name}'");

        // Check all members for function types
        foreach (var (name, memberType) in iface.GetAllMembers())
        {
            var check = CheckSerializable(memberType, seen);
            if (!check.IsValid)
                return (false, $"interface '{iface.Name}' with non-serializable member '{name}'");
        }

        return (true, null);
    }

    private static (bool, string?) CheckInstantiatedGenericSerializable(TypeInfo.InstantiatedGeneric ig, HashSet<TypeInfo> seen)
    {
        // Check the base generic definition
        var defCheck = ig.GenericDefinition switch
        {
            TypeInfo.GenericClass => (false, "generic class instance"),
            TypeInfo.GenericInterface gi when gi.IsCallable => (false, "callable generic interface"),
            _ => (true, (string?)null)
        };

        if (!defCheck.Item1)
            return defCheck;

        // Check type arguments
        foreach (var arg in ig.TypeArguments)
        {
            var check = CheckSerializable(arg, seen);
            if (!check.IsValid)
                return check;
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if a type can be transferred (not cloned) to a worker.
    /// Only MessagePort is transferable.
    /// </summary>
    public static bool IsTransferable(TypeInfo type)
    {
        return type is TypeInfo.MessagePort;
    }
}
