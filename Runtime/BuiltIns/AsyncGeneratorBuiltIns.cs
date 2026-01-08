using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for SharpTSAsyncGenerator instances.
/// </summary>
public static class AsyncGeneratorBuiltIns
{
    /// <summary>
    /// Gets a built-in member for an async generator.
    /// </summary>
    /// <param name="generator">The async generator instance.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member as a BuiltInAsyncMethod or property value, or null if not found.</returns>
    public static object? GetMember(SharpTSAsyncGenerator generator, string name)
    {
        return name switch
        {
            "next" => new BuiltInAsyncMethod("next", 0, 0, async (_, receiver, _) =>
            {
                if (receiver is SharpTSAsyncGenerator gen)
                {
                    return await gen.Next();
                }
                throw new Exception("Runtime Error: next() called on non-async-generator.");
            }),
            "return" => new BuiltInAsyncMethod("return", 0, 1, async (_, receiver, args) =>
            {
                if (receiver is SharpTSAsyncGenerator gen)
                {
                    object? value = args.Count > 0 ? args[0] : null;
                    return await gen.Return(value);
                }
                throw new Exception("Runtime Error: return() called on non-async-generator.");
            }),
            "throw" => new BuiltInAsyncMethod("throw", 0, 1, async (_, receiver, args) =>
            {
                if (receiver is SharpTSAsyncGenerator gen)
                {
                    object? error = args.Count > 0 ? args[0] : null;
                    return await gen.Throw(error);
                }
                throw new Exception("Runtime Error: throw() called on non-async-generator.");
            }),
            _ => null
        };
    }
}
