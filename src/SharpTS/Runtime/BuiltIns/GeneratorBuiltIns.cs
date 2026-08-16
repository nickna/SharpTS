using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for SharpTSGenerator instances.
/// Falls through to IteratorBuiltIns for ES2025 Iterator Helper methods.
/// </summary>
public static class GeneratorBuiltIns
{
    /// <summary>
    /// Gets a built-in member for a generator.
    /// </summary>
    /// <param name="generator">The generator instance.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member as a BuiltInMethod or property value, or null if not found.</returns>
    public static object? GetMember(SharpTSGenerator generator, string name)
    {
        return name switch
        {
            "next" => BuiltInMethod.CreateV2("next", 0, 1, static (_, receiver, args) =>
            {
                if (receiver.ToObject() is SharpTSGenerator gen)
                {
                    // ECMA-262 §27.5.3.3: the argument becomes the result of the
                    // resumed yield. A bare next() resumes with undefined.
                    object? sent = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                    return RuntimeValue.FromObject(gen.Next(sent));
                }
                throw new Exception("Runtime Error: next() called on non-generator.");
            }),
            "return" => BuiltInMethod.CreateV2("return", 0, 1, static (_, receiver, args) =>
            {
                if (receiver.ToObject() is SharpTSGenerator gen)
                {
                    // A bare return() resumes with the value undefined (ECMA-262 §27.5.3.4).
                    object? value = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                    return RuntimeValue.FromObject(gen.Return(value));
                }
                throw new Exception("Runtime Error: return() called on non-generator.");
            }),
            "throw" => BuiltInMethod.CreateV2("throw", 0, 1, static (_, receiver, args) =>
            {
                if (receiver.ToObject() is SharpTSGenerator gen)
                {
                    // A bare throw() throws undefined (ECMA-262 §27.5.3.4).
                    object? error = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
                    return RuntimeValue.FromObject(gen.Throw(error));
                }
                throw new Exception("Runtime Error: throw() called on non-generator.");
            }),
            // ES2025 Iterator Helpers - create wrapper methods that convert generator to iterator internally.
            // We can't just delegate to IteratorBuiltIns.GetMember because EvaluateGetOnFallback
            // re-binds the returned method to the original generator (line 393 in Interpreter.Properties.cs),
            // which would override the iterator binding and cause an InvalidCastException.
            "map" or "filter" or "take" or "drop" or "flatMap" or "reduce"
                or "toArray" or "forEach" or "some" or "every" or "find" =>
                CreateIteratorHelperWrapper(name),
            _ => null
        };
    }

    /// <summary>
    /// Creates a BuiltInMethod wrapper that converts the generator receiver to an iterator
    /// before delegating to IteratorBuiltIns. The wrapper accepts SharpTSGenerator as receiver
    /// (since EvaluateGetOnFallback re-binds to the original object), converts it internally,
    /// and then calls the real iterator method.
    /// </summary>
    private static BuiltInMethod CreateIteratorHelperWrapper(string name)
    {
        // Determine arity based on method
        int minArity = name switch
        {
            "toArray" => 0,
            "reduce" => 1,
            _ => name is "take" or "drop" ? 1 : 1
        };
        int maxArity = name switch
        {
            "toArray" => 0,
            "reduce" => 2,
            _ => name is "take" or "drop" ? 1 : 1
        };

        return BuiltInMethod.CreateV2(name, minArity, maxArity, (interp, receiver, args) =>
        {
            // Wrap the generator as an iterator, then delegate to IteratorBuiltIns
            var iter = WrapAsIterator((SharpTSGenerator)receiver.ToObject()!);
            var method = IteratorBuiltIns.GetMember(iter, name);
            if (method is BuiltInMethod bm)
                return bm.CallV2(interp, args);
            throw new Exception($"Runtime Error: Iterator method '{name}' not found.");
        });
    }

    /// <summary>
    /// Wraps a generator as a SharpTSIterator for iterator helper method delegation.
    /// </summary>
    private static SharpTSIterator WrapAsIterator(SharpTSGenerator gen)
    {
        return new SharpTSIterator(GeneratorToEnumerable(gen));
    }

    private static IEnumerable<object?> GeneratorToEnumerable(SharpTSGenerator gen)
    {
        while (true)
        {
            var result = gen.Next();
            if (result.Done) yield break;
            yield return result.Value;
        }
    }
}
