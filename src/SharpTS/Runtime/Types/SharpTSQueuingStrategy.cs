using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Base class for WHATWG queuing strategies used by Web Streams.
/// </summary>
/// <remarks>
/// A queuing strategy provides a <c>highWaterMark</c> (desired queue fill
/// target) and a <c>size(chunk)</c> algorithm that measures a single chunk.
/// <see cref="SharpTSReadableStream"/>, <see cref="SharpTSWritableStream"/>,
/// and <see cref="SharpTSTransformStream"/> accept an instance of one of these
/// at construction time (or a plain <see cref="SharpTSObject"/> with the same
/// shape).
/// </remarks>
public abstract class SharpTSQueuingStrategy : ITypeCategorized
{
    public TypeCategory RuntimeCategory => TypeCategory.Unknown;

    /// <summary>The desired target queue fill level.</summary>
    public double HighWaterMark { get; }

    protected SharpTSQueuingStrategy(double highWaterMark)
    {
        HighWaterMark = highWaterMark;
    }

    /// <summary>Measures the size of a chunk under this strategy.</summary>
    public abstract double Size(object? chunk);

    /// <summary>Returns the <c>size</c> method as a SharpTS callable.</summary>
    public abstract object SizeCallable { get; }

    public object? GetMember(string name)
    {
        return name switch
        {
            "highWaterMark" => (object)HighWaterMark,
            "size" => SizeCallable,
            _ => null,
        };
    }
}

/// <summary>WHATWG CountQueuingStrategy — every chunk counts as 1.</summary>
public sealed class SharpTSCountQueuingStrategy : SharpTSQueuingStrategy
{
    public SharpTSCountQueuingStrategy(double highWaterMark) : base(highWaterMark) { }

    public override double Size(object? chunk) => 1.0;

    public override object SizeCallable { get; } = BuiltInMethod.CreateV2("size", 1, static (_, _, _) => RuntimeValue.One);
}

/// <summary>
/// WHATWG ByteLengthQueuingStrategy — a chunk's size is its <c>byteLength</c>
/// property (or .NET <see cref="byte"/>[] length).
/// </summary>
public sealed class SharpTSByteLengthQueuingStrategy : SharpTSQueuingStrategy
{
    public SharpTSByteLengthQueuingStrategy(double highWaterMark) : base(highWaterMark) { }

    public override double Size(object? chunk) => MeasureBytes(chunk);

    public override object SizeCallable { get; } = BuiltInMethod.CreateV2("size", 1, static (_, _, args) =>
    {
        var chunk = args.Length > 0 ? args[0].ToObject() : null;
        return RuntimeValue.FromNumber(MeasureBytes(chunk));
    });

    private static double MeasureBytes(object? chunk)
    {
        return chunk switch
        {
            byte[] bytes => bytes.Length,
            SharpTSBuffer buf => buf.Length,
            SharpTSArrayBuffer ab => ab.ByteLength,
            SharpTSTypedArray ta => ta.ByteLength,
            SharpTSArray arr => arr.Length,
            _ => TryGetByteLength(chunk),
        };
    }

    private static double TryGetByteLength(object? chunk)
    {
        if (chunk is SharpTSObject obj && obj.Fields.TryGetValue("byteLength", out var v))
        {
            return v switch
            {
                double d => d,
                int i => i,
                long l => l,
                _ => 0.0,
            };
        }
        return 0.0;
    }
}
