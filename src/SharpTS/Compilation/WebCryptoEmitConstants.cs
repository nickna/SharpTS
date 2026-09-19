using System.Collections.Immutable;

namespace SharpTS.Compilation;

/// <summary>
/// Immutable algorithm name pairs embedded by WebCrypto emitters. Generated
/// helpers and feature availability remain owned by each WebCrypto runtime.
/// </summary>
internal static class WebCryptoEmitConstants
{
    internal static ImmutableArray<(string Lower, string Web)> HashNames { get; } =
        [("sha1", "SHA-1"), ("sha256", "SHA-256"), ("sha384", "SHA-384"), ("sha512", "SHA-512")];
}
