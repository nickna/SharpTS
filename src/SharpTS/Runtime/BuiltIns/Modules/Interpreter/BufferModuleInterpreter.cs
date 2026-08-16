using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'buffer' module.
/// </summary>
/// <remarks>
/// Provides the Buffer class plus the module-level helpers and constants
/// (atob/btoa, isUtf8/isAscii, transcode, constants, SlowBuffer). The Buffer
/// constructor is also available globally.
/// </remarks>
public static class BufferModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the buffer module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["Buffer"] = SharpTSBufferConstructor.Instance,

            // Blob/File constructors (also globals). Construction dispatches by the
            // syntactic name `new Blob()`; these markers resolve value-position uses.
            ["Blob"] = SharpTSBlobConstructor.Instance,
            ["File"] = SharpTSFileConstructor.Instance,

            // resolveObjectURL(id) — looks up a blob: URL created via URL.createObjectURL.
            ["resolveObjectURL"] = BuiltInMethod.CreateV2("resolveObjectURL", 1, ResolveObjectURL),

            // Base64 (also exposed as globals atob/btoa)
            ["atob"] = BuiltInMethod.CreateV2("atob", 1, Atob),
            ["btoa"] = BuiltInMethod.CreateV2("btoa", 1, Btoa),

            // Validation helpers
            ["isUtf8"] = BuiltInMethod.CreateV2("isUtf8", 1, IsUtf8),
            ["isAscii"] = BuiltInMethod.CreateV2("isAscii", 1, IsAscii),

            // Encoding conversion
            ["transcode"] = BuiltInMethod.CreateV2("transcode", 3, Transcode),

            // Deprecated unsafe allocation (= Buffer.allocUnsafeSlow)
            ["SlowBuffer"] = BuiltInMethod.CreateV2("SlowBuffer", 1, SlowBuffer),

            // Constants
            ["constants"] = new SharpTSObject(new Dictionary<string, object?>
            {
                ["MAX_LENGTH"] = BufferModuleHelpers.MaxLength,
                ["MAX_STRING_LENGTH"] = BufferModuleHelpers.MaxStringLength,
            }),
            ["kMaxLength"] = BufferModuleHelpers.MaxLength,
            ["kStringMaxLength"] = BufferModuleHelpers.MaxStringLength,
            ["INSPECT_MAX_BYTES"] = BufferModuleHelpers.InspectMaxBytes,
        };
    }

    private static byte[] ToBytes(RuntimeValue value, string method)
    {
        return value.ToObject() switch
        {
            SharpTSBuffer buf => buf.Data,
            SharpTSTypedArray ta => TypedArrayBytes(ta),
            string s => System.Text.Encoding.UTF8.GetBytes(s),
            _ => throw new Exception($"buffer.{method} requires a Buffer, TypedArray, or string argument")
        };
    }

    private static byte[] TypedArrayBytes(SharpTSTypedArray ta)
    {
        var bytes = new byte[ta.ByteLength];
        Array.Copy(ta.Buffer, ta.ByteOffset, bytes, 0, ta.ByteLength);
        return bytes;
    }

    private static RuntimeValue Atob(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("atob requires an argument");
        return RuntimeValue.FromString(BufferModuleHelpers.Atob(args[0].ToObject()?.ToString() ?? ""));
    }

    private static RuntimeValue Btoa(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("btoa requires an argument");
        return RuntimeValue.FromString(BufferModuleHelpers.Btoa(args[0].ToObject()?.ToString() ?? ""));
    }

    private static RuntimeValue IsUtf8(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("isUtf8 requires a Buffer or TypedArray argument");
        return RuntimeValue.FromBoolean(BufferModuleHelpers.IsUtf8(ToBytes(args[0], "isUtf8")));
    }

    private static RuntimeValue IsAscii(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("isAscii requires a Buffer or TypedArray argument");
        return RuntimeValue.FromBoolean(BufferModuleHelpers.IsAscii(ToBytes(args[0], "isAscii")));
    }

    private static RuntimeValue Transcode(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 3)
            throw new Exception("transcode requires (source, fromEncoding, toEncoding)");
        var source = ToBytes(args[0], "transcode");
        var fromEnc = args[1].ToObject()?.ToString() ?? "utf8";
        var toEnc = args[2].ToObject()?.ToString() ?? "utf8";
        return RuntimeValue.FromObject(new SharpTSBuffer(BufferModuleHelpers.Transcode(source, fromEnc, toEnc)));
    }

    private static RuntimeValue SlowBuffer(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsNumber)
            throw new Exception("SlowBuffer requires a size argument");
        return RuntimeValue.FromObject(SharpTSBuffer.AllocUnsafe((int)args[0].AsNumberUnsafe()));
    }

    private static RuntimeValue ResolveObjectURL(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            return RuntimeValue.Undefined;
        var blob = BlobUrlRegistry.Resolve(args[0].AsStringUnsafe());
        return blob != null ? RuntimeValue.FromObject(blob) : RuntimeValue.Undefined;
    }
}
