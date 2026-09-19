using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Immutable framework metadata shared by IL emitters. These handles refer only to
/// framework assemblies, never to declarations from a generated assembly. Initialization
/// is process-wide; per-compilation handles belong to the emitted runtime components.
/// </summary>
internal static class FrameworkEmitMetadata
{
    internal static MethodInfo DoubleListAsSpan { get; } =
        EmitGenerics.MakeGenericMethod(
            typeof(System.Runtime.InteropServices.CollectionsMarshal)
                .GetMethod(nameof(System.Runtime.InteropServices.CollectionsMarshal.AsSpan))!,
            typeof(double));
    internal static MethodInfo DoubleSpanSlice { get; } =
        typeof(Span<double>).GetMethod(nameof(Span<double>.Slice), [typeof(int)])!;
    internal static MethodInfo DoubleSpanIndexOf { get; } =
        EmitGenerics.MakeGenericMethod(
            typeof(MemoryExtensions).GetMethods().Single(method =>
                method.Name == nameof(MemoryExtensions.IndexOf) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters() is [var span, var value] &&
                span.ParameterType.IsGenericType &&
                span.ParameterType.GetGenericTypeDefinition() == typeof(Span<>) &&
                value.ParameterType.IsGenericParameter),
            typeof(double));

    /// <summary>
    /// Digest table rows: name, rooted one-shot HashData method, optional
    /// IsSupported getter, and XOF default length.
    /// </summary>
    internal static ImmutableArray<(string Name, MethodInfo HashData, MethodInfo? IsSupported, int XofDefault)> CryptoHashes { get; } =
    [
        ("md5", ((Func<byte[], byte[]>)MD5.HashData).Method, null, -1),
        ("sha1", ((Func<byte[], byte[]>)SHA1.HashData).Method, null, -1),
        ("sha256", ((Func<byte[], byte[]>)SHA256.HashData).Method, null, -1),
        ("sha384", ((Func<byte[], byte[]>)SHA384.HashData).Method, null, -1),
        ("sha512", ((Func<byte[], byte[]>)SHA512.HashData).Method, null, -1),
        ("sha3-256", ((Func<byte[], byte[]>)SHA3_256.HashData).Method,
            typeof(SHA3_256).GetProperty(nameof(SHA3_256.IsSupported))!.GetMethod, -1),
        ("sha3-384", ((Func<byte[], byte[]>)SHA3_384.HashData).Method,
            typeof(SHA3_384).GetProperty(nameof(SHA3_384.IsSupported))!.GetMethod, -1),
        ("sha3-512", ((Func<byte[], byte[]>)SHA3_512.HashData).Method,
            typeof(SHA3_512).GetProperty(nameof(SHA3_512.IsSupported))!.GetMethod, -1),
        ("shake128", ((Func<byte[], int, byte[]>)Shake128.HashData).Method,
            typeof(Shake128).GetProperty(nameof(Shake128.IsSupported))!.GetMethod, 16),
        ("shake256", ((Func<byte[], int, byte[]>)Shake256.HashData).Method,
            typeof(Shake256).GetProperty(nameof(Shake256.IsSupported))!.GetMethod, 32),
    ];
}
