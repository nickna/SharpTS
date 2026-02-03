namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Math

    public static double Random()
    {
        return _random.NextDouble();
    }

    #endregion

    #region Enums

    /// <summary>
    /// Get enum member name by value with caching.
    /// Keys and values arrays define the reverse mapping (passed once, cached by enumName).
    /// </summary>
    public static string GetEnumMemberName(string enumName, double value, double[] keys, string[] values)
    {
        if (!_enumReverseCache.TryGetValue(enumName, out var reverse))
        {
            reverse = [];
            for (int i = 0; i < keys.Length; i++)
            {
                reverse[keys[i]] = values[i];
            }
            _enumReverseCache[enumName] = reverse;
        }

        return reverse.TryGetValue(value, out var name) ? name : throw new Exception($"Value {value} not found in enum '{enumName}'");
    }

    #endregion

    #region Template Literals

    public static string ConcatTemplate(object?[] parts)
    {
        return string.Concat(parts.Select(Stringify));
    }

    /// <summary>
    /// Invokes a tagged template literal function.
    /// Creates a TemplateStringsArray-like object with cooked/raw strings and calls the tag function.
    /// </summary>
    /// <remarks>
    /// Handles both the compiler's TSFunction type and the emitted TSFunction type by using
    /// reflection to find and invoke the Invoke method.
    /// For compiled mode, creates a List&lt;object?&gt; with a "raw" property that compiled code can access.
    /// </remarks>
    public static object? InvokeTaggedTemplate(
        object? tag,
        object?[] cookedStrings,
        string[] rawStrings,
        object?[] expressions)
    {
        // For compiled mode, we need a List<object?> that the emitted ArrayJoin etc can work with
        // Create a special list that also has a "raw" property accessible via dynamic lookup
        var stringsArray = new TemplateStringsList(cookedStrings.ToList(), rawStrings.ToList());

        // Build args: [stringsArray, ...expressions]
        var args = new object?[1 + expressions.Length];
        args[0] = stringsArray;
        Array.Copy(expressions, 0, args, 1, expressions.Length);

        // Invoke tag function
        // Check if it's the compiler's TSFunction type
        if (tag is TSFunction func)
        {
            return func.Invoke(args);
        }

        // Check if it's a Delegate
        if (tag is Delegate del)
        {
            return del.DynamicInvoke(args);
        }

        // Check if it's the emitted TSFunction type (different type, same pattern)
        // Use reflection to find and invoke the Invoke method
        if (tag != null)
        {
            var invokeMethod = tag.GetType().GetMethod("Invoke", [typeof(object?[])]);
            if (invokeMethod != null)
            {
                return invokeMethod.Invoke(tag, [args]);
            }
        }

        throw new Exception("TypeError: Tagged template tag must be a function.");
    }

    /// <summary>
    /// A List&lt;object&gt; subclass that also exposes a "raw" property for template strings.
    /// This allows compiled code to use standard List methods (join, etc.) while also
    /// accessing the raw strings property.
    /// Uses List&lt;object&gt; (non-nullable) to match the emitted runtime's type expectations.
    /// </summary>
    public class TemplateStringsList : List<object>
    {
        private readonly List<object> _rawStrings;

        public TemplateStringsList(List<object?> cookedStrings, List<string> rawStrings)
        {
            // Add cooked strings, converting null to a placeholder string
            foreach (var s in cookedStrings)
            {
                Add(s ?? "undefined");  // ES spec: invalid escape sequences become undefined
            }
            _rawStrings = rawStrings.Cast<object>().ToList();
        }

        /// <summary>
        /// The raw (unprocessed) strings from the template literal.
        /// </summary>
        public List<object> raw => _rawStrings;
    }

    /// <summary>
    /// Implements String.raw for compiled tagged template literals.
    /// Returns the raw string with substitution values interleaved.
    /// </summary>
    public static string StringRaw(string[] rawStrings, object?[] expressions)
    {
        if (rawStrings.Length == 0)
            return "";

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < rawStrings.Length; i++)
        {
            result.Append(rawStrings[i]);
            if (i < expressions.Length)
            {
                result.Append(expressions[i]?.ToString() ?? "");
            }
        }
        return result.ToString();
    }

    #endregion

    #region Resource Disposal

    /// <summary>
    /// Disposes a resource using Symbol.dispose if available.
    /// Called at the end of a using declaration's scope.
    /// </summary>
    /// <param name="resource">The resource to dispose.</param>
    /// <param name="disposeSymbol">The Symbol.dispose symbol to look up.</param>
    public static void DisposeResource(object? resource, object disposeSymbol)
    {
        // Null/undefined resources are skipped
        if (resource == null)
            return;

        // Get the symbol dictionary for this object
        var symbolDict = _symbolStorage.GetOrCreateValue(resource);

        // Try to get the dispose method from the symbol dictionary
        if (!symbolDict.TryGetValue(disposeSymbol, out var disposeMethod) || disposeMethod == null)
        {
            // Check for .NET IDisposable as fallback
            if (resource is IDisposable disposable)
            {
                disposable.Dispose();
            }
            // No disposal method - silently skip (TypeScript allows this)
            return;
        }

        // Call the dispose method
        if (disposeMethod is TSFunction func)
        {
            // Call with resource as implicit `this` - TSFunction.Invoke will handle binding
            // For object literals with methods, we need to set up the this context
            func.Invoke([]);
        }
        else if (disposeMethod is Delegate del)
        {
            del.DynamicInvoke([]);
        }
        else
        {
            // Try reflection for other function types
            var invokeMethod = disposeMethod.GetType().GetMethod("Invoke", [typeof(object?[])]);
            invokeMethod?.Invoke(disposeMethod, [Array.Empty<object?>()]);
        }
    }

    #endregion

    #region Array Method Binding

    /// <summary>
    /// Gets a property from a List, returning bound array methods for join, push, pop, etc.
    /// This enables dynamic array method calls when the type is 'any'.
    /// </summary>
    public static object? GetListProperty(List<object> list, string name)
    {
        return name switch
        {
            "length" => (double)list.Count,
            "join" => new BoundArrayMethod(list, "join"),
            "push" => new BoundArrayMethod(list, "push"),
            "pop" => new BoundArrayMethod(list, "pop"),
            "shift" => new BoundArrayMethod(list, "shift"),
            "unshift" => new BoundArrayMethod(list, "unshift"),
            "slice" => new BoundArrayMethod(list, "slice"),
            "indexOf" => new BoundArrayMethod(list, "indexOf"),
            "includes" => new BoundArrayMethod(list, "includes"),
            "concat" => new BoundArrayMethod(list, "concat"),
            "reverse" => new BoundArrayMethod(list, "reverse"),
            "map" => new BoundArrayMethod(list, "map"),
            "filter" => new BoundArrayMethod(list, "filter"),
            "forEach" => new BoundArrayMethod(list, "forEach"),
            "find" => new BoundArrayMethod(list, "find"),
            "findIndex" => new BoundArrayMethod(list, "findIndex"),
            "some" => new BoundArrayMethod(list, "some"),
            "every" => new BoundArrayMethod(list, "every"),
            "reduce" => new BoundArrayMethod(list, "reduce"),
            "raw" => (list is TemplateStringsList tsl) ? tsl.raw : null,
            _ => null
        };
    }

    /// <summary>
    /// A bound array method that can be invoked dynamically.
    /// Used for dynamically-typed array method calls.
    /// </summary>
    public class BoundArrayMethod
    {
        private readonly List<object> _list;
        private readonly string _methodName;

        public BoundArrayMethod(List<object> list, string methodName)
        {
            _list = list;
            _methodName = methodName;
        }

        /// <summary>
        /// Invoke the bound array method with the given arguments.
        /// </summary>
        public object? Invoke(params object?[] args)
        {
            return _methodName switch
            {
                "join" => Join(args),
                "push" => Push(args),
                "pop" => Pop(),
                "shift" => Shift(),
                "unshift" => Unshift(args),
                "slice" => Slice(args),
                "indexOf" => IndexOf(args),
                "includes" => Includes(args),
                "concat" => Concat(args),
                "reverse" => Reverse(),
                _ => throw new Exception($"Array method '{_methodName}' not implemented for dynamic dispatch")
            };
        }

        private string Join(object?[] args)
        {
            var separator = args.Length > 0 && args[0] != null ? Stringify(args[0]) : ",";
            return string.Join(separator, _list.Select(Stringify));
        }

        private double Push(object?[] args)
        {
            foreach (var arg in args)
                _list.Add(arg!);
            return _list.Count;
        }

        private object? Pop()
        {
            if (_list.Count == 0) return null;
            var last = _list[^1];
            _list.RemoveAt(_list.Count - 1);
            return last;
        }

        private object? Shift()
        {
            if (_list.Count == 0) return null;
            var first = _list[0];
            _list.RemoveAt(0);
            return first;
        }

        private double Unshift(object?[] args)
        {
            for (int i = args.Length - 1; i >= 0; i--)
                _list.Insert(0, args[i]!);
            return _list.Count;
        }

        private List<object> Slice(object?[] args)
        {
            int start = args.Length > 0 && args[0] is double d ? (int)d : 0;
            int end = args.Length > 1 && args[1] is double e ? (int)e : _list.Count;
            if (start < 0) start = Math.Max(0, _list.Count + start);
            if (end < 0) end = Math.Max(0, _list.Count + end);
            return _list.Skip(start).Take(end - start).ToList();
        }

        private double IndexOf(object?[] args)
        {
            if (args.Length == 0) return -1;
            var item = args[0];
            for (int i = 0; i < _list.Count; i++)
            {
                if (StrictEquals(_list[i], item))
                    return i;
            }
            return -1;
        }

        private bool Includes(object?[] args)
        {
            if (args.Length == 0) return false;
            var item = args[0];
            return _list.Any(e => StrictEquals(e, item));
        }

        private List<object> Concat(object?[] args)
        {
            var result = new List<object>(_list);
            foreach (var arg in args)
            {
                if (arg is List<object> otherList)
                    result.AddRange(otherList);
                else if (arg != null)
                    result.Add(arg);
            }
            return result;
        }

        private List<object> Reverse()
        {
            _list.Reverse();
            return _list;
        }
    }

    #endregion
}
