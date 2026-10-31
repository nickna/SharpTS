// Adapted from PE-Packer PR #38, commit 2be414c686dc867f010f73a3648809325b8e58ad.
// Copyright (c) 2026 Nick Nassiri. Licensed under the repository MIT LICENSE.
namespace SharpTS.Cli;

/// <summary>Options for signing a completed Windows PE file.</summary>
public sealed record AuthenticodeSigningOptions
{
    /// <summary>The default RFC 3161 timestamp endpoint.</summary>
    public const string DefaultTimestampUrl = "https://timestamp.digicert.com";

    /// <summary>A PKCS#12 (.pfx/.p12) file or sha1: certificate thumbprint in the Windows current-user My store.</summary>
    public required string Certificate { get; init; }

    /// <summary>The PKCS#12 password. It is never included in diagnostic messages.</summary>
    public string? Password { get; init; }

    /// <summary>The RFC 3161 timestamp URL. Null disables timestamping for offline development tests.</summary>
    public string? TimestampUrl { get; init; } = DefaultTimestampUrl;

    /// <summary>The file digest: sha256 (default), sha384, or sha512.</summary>
    public string DigestAlgorithm { get; init; } = "sha256";

    /// <summary>An optional explicit signtool.exe (Windows) or osslsigncode (other platforms) path.</summary>
    public string? ToolPath { get; init; }

    /// <summary>The maximum signing-tool duration, including timestamping.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Describes the request without exposing its password.</summary>
    public override string ToString() => $"Authenticode signing: {Certificate}, {DigestAlgorithm}, timestamp {TimestampUrl ?? "disabled"}";
}
