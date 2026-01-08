using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for SharpTSGenerator instances.
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
            "next" => new BuiltInMethod("next", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSGenerator gen)
                {
                    return gen.Next();
                }
                throw new Exception("Runtime Error: next() called on non-generator.");
            }),
            "return" => new BuiltInMethod("return", 0, 1, (_, receiver, args) =>
            {
                if (receiver is SharpTSGenerator gen)
                {
                    object? value = args.Count > 0 ? args[0] : null;
                    return gen.Return(value);
                }
                throw new Exception("Runtime Error: return() called on non-generator.");
            }),
            "throw" => new BuiltInMethod("throw", 0, 1, (_, receiver, args) =>
            {
                if (receiver is SharpTSGenerator gen)
                {
                    object? error = args.Count > 0 ? args[0] : null;
                    return gen.Throw(error);
                }
                throw new Exception("Runtime Error: throw() called on non-generator.");
            }),
            _ => null
        };
    }
}
