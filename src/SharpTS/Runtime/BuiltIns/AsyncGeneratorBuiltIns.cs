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
            "next" => new BuiltInAsyncMethod("next", 0, 1, async (_, receiver, args) =>
            {
                if (receiver is SharpTSAsyncGenerator gen)
                {
                    // A resumed `yield` evaluates to the value sent via next(v); an omitted argument is
                    // undefined, not null (ECMA-262 §27.6.3.6).
                    object? sent = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
                    return await gen.Next(sent);
                }
                throw new Exception("Runtime Error: next() called on non-async-generator.");
            }),
            "return" => new BuiltInAsyncMethod("return", 0, 1, async (_, receiver, args) =>
            {
                if (receiver is SharpTSAsyncGenerator gen)
                {
                    // An omitted argument is undefined, not null — return() reports { value: undefined }
                    // (ECMA-262 §27.6.1.3); an explicit return(null) still reports null (#618).
                    object? value = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
                    return await gen.Return(value);
                }
                throw new Exception("Runtime Error: return() called on non-async-generator.");
            }),
            "throw" => new BuiltInAsyncMethod("throw", 0, 1, async (_, receiver, args) =>
            {
                if (receiver is SharpTSAsyncGenerator gen)
                {
                    // An omitted argument is the undefined sentinel, not null (#618).
                    object? error = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
                    return await gen.Throw(error);
                }
                throw new Exception("Runtime Error: throw() called on non-async-generator.");
            }),
            _ => null
        };
    }
}
