namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared parsing of the Node stream <c>write</c>/<c>end</c> argument shapes,
/// which recurred verbatim across Writable, Duplex, and Transform (#1138).
/// </summary>
internal static class StreamArgs
{
    /// <summary>
    /// Parses <c>write(chunk, encoding?, callback?)</c>: <c>args[0]</c> is always the
    /// chunk; the optional encoding is a string and the optional callback is a function.
    /// </summary>
    public static (object? chunk, string? encoding, ISharpTSCallable? callback) ParseWrite(
        ReadOnlySpan<RuntimeValue> args)
    {
        object? chunk = args.Length > 0 ? args[0].ToObject() : null;
        var (encoding, callback) = ParseEncodingAndCallback(args, fromIndex: 1);
        return (chunk, encoding, callback);
    }

    /// <summary>
    /// Parses <c>end(chunk?, encoding?, callback?)</c>: <c>args[0]</c> may be the final
    /// chunk or, if it is a function, the completion callback.
    /// </summary>
    public static (object? chunk, string? encoding, ISharpTSCallable? callback) ParseEnd(
        ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            return (null, null, null);

        if (args[0].ToObject() is ISharpTSCallable cb0)
            return (null, null, cb0);

        object? chunk = args[0].ToObject();
        var (encoding, callback) = ParseEncodingAndCallback(args, fromIndex: 1);
        return (chunk, encoding, callback);
    }

    /// <summary>
    /// Parses an optional <c>(encoding?, callback?)</c> tail starting at
    /// <paramref name="fromIndex"/>: a string is the encoding (and a following function
    /// the callback); a function alone is the callback.
    /// </summary>
    private static (string? encoding, ISharpTSCallable? callback) ParseEncodingAndCallback(
        ReadOnlySpan<RuntimeValue> args, int fromIndex)
    {
        if (args.Length <= fromIndex)
            return (null, null);

        if (args[fromIndex].IsString)
        {
            string encoding = args[fromIndex].AsStringUnsafe();
            ISharpTSCallable? callback = args.Length > fromIndex + 1 && args[fromIndex + 1].ToObject() is ISharpTSCallable cb
                ? cb
                : null;
            return (encoding, callback);
        }

        return (null, args[fromIndex].ToObject() as ISharpTSCallable);
    }
}
