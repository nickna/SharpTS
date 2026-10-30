using System.Buffers.Binary;
using System.ComponentModel;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Cli;

/// <summary>Drives supported SDK restore/publish; no private ILC command-line contract is used.</summary>
internal static class NativeAotPublishDriver
{
    internal sealed record Result(bool CacheHit, IReadOnlyList<NativePublishDiagnostic> Diagnostics);

    internal static async Task<Result> PublishAsync(NativePublishWorkspace workspace, string workingDirectory,
        TextWriter output, TextWriter error, bool quiet, AuthenticodeSigningOptions? signing = null,
        CancellationToken token = default, string? cacheRoot = null)
    {
        try
        {
            var toolchain = await NativePublishToolchain.ResolveAsync(workingDirectory, token).ConfigureAwait(false);
            toolchain.PinSdk(workspace.ProjectDirectory);
            var restore = await NativePublishProcess.RunAsync(toolchain.Start(workspace.ProjectDirectory,
                "restore", "NativeApp.csproj", "--runtime", workspace.Options.RuntimeIdentifier, "--disable-build-servers", "--nologo", "--verbosity", "minimal"), output, error, token).ConfigureAwait(false);
            if (Failed(restore.ExitCode, restore.Diagnostics)) throw new CompileException(DiagnosticCode.NativeGuestPublish, $"Native SDK restore failed (exit {restore.ExitCode}). See the original SDK diagnostics above.");
            string pdb = Path.ChangeExtension(workspace.GuestPath, ".pdb");
            var revision = NativeGuestRevision.Compute(File.ReadAllBytes(workspace.GuestPath), File.Exists(pdb) ? File.ReadAllBytes(pdb) : null);
            (string Fingerprint, bool CanReuse) fingerprint;
            try { fingerprint = await toolchain.FingerprintAsync(workspace, token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { fingerprint = ("unidentified", false); }
            string key = NativePublishCache.Key(workspace, revision, fingerprint.Fingerprint);
            string root = Path.GetFullPath(cacheRoot ?? NativePublishCache.DefaultRoot);
            string nativeName = workspace.AssemblyName + (workspace.Options.RuntimeIdentifier.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "");
            FileStream? cacheLock = null;
            try
            {
                if (fingerprint.CanReuse) cacheLock = await NativePublishCache.LockAsync(root, key, token).ConfigureAwait(false);
                string? artifacts = fingerprint.CanReuse ? NativePublishCache.Find(root, key, nativeName) : null;
                bool cacheHit = artifacts is not null;
                IReadOnlyList<NativePublishDiagnostic> diagnostics = restore.Diagnostics;
                if (!cacheHit)
                {
                    if (!quiet) output.WriteLine(fingerprint.CanReuse ? "Publishing native output..." : "Publishing native output (final-artifact cache unavailable for this toolchain)...");
                    var publish = await NativePublishProcess.RunAsync(toolchain.Start(workspace.ProjectDirectory,
                        "publish", "NativeApp.csproj", "--no-restore", "-c", "Release", "--runtime", workspace.Options.RuntimeIdentifier,
                        "--output", workspace.PublishDirectory, "--disable-build-servers", "--nologo", "--verbosity", "minimal"), output, error, token).ConfigureAwait(false);
                    diagnostics = restore.Diagnostics.Concat(publish.Diagnostics).ToArray();
                    // The generated project makes individual trim/AOT warnings errors. Keep a
                    // second boundary in case a tool reports one while returning success.
                    if (Failed(publish.ExitCode, diagnostics))
                        throw new CompileException(DiagnosticCode.NativeGuestPublish, $"Native SDK publish failed (exit {publish.ExitCode}). Trim/AOT diagnostics are never suppressed; see the original SDK/ILC output above.");
                    ValidateNativeImage(Path.Combine(workspace.PublishDirectory, nativeName), workspace.Options.RuntimeIdentifier);
                    token.ThrowIfCancellationRequested();
                    artifacts = workspace.PublishDirectory;
                }
                token.ThrowIfCancellationRequested();
                await NativePublishOutput.PromoteAsync(artifacts!, nativeName, workspace, signing, token).ConfigureAwait(false);
                if (!cacheHit && fingerprint.CanReuse)
                {
                    // Only a completed operation enters the unsigned cache. Cache I/O cannot
                    // turn an already committed output into a reported publish failure.
                    try { NativePublishCache.Store(root, key, workspace, nativeName, revision); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                if (!quiet) output.WriteLine($"Published native output to {workspace.Options.OutputPath}{(cacheHit ? " (cache hit)" : "")}");
                return new(cacheHit, diagnostics);
            }
            finally { cacheLock?.Dispose(); }
        }
        catch (Win32Exception)
        { throw new CompileException(DiagnosticCode.NativeGuestToolchain, "Could not start the .NET SDK/native publish toolchain. Install dotnet and the matching native compiler/linker prerequisites."); }
        catch (TimeoutException)
        { throw new CompileException(DiagnosticCode.NativeGuestPublish, "The native SDK process did not finish draining its output. Final output was preserved."); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or JsonException or KeyNotFoundException)
        { throw new CompileException(DiagnosticCode.NativeGuestPublishOutput, "Native output was not promoted: " + exception.Message); }
    }

    private static bool Failed(int exitCode, IReadOnlyList<NativePublishDiagnostic> diagnostics) => exitCode != 0 ||
        diagnostics.Any(diagnostic => diagnostic.Severity.Equals("error", StringComparison.OrdinalIgnoreCase) || diagnostic.Code.StartsWith("IL", StringComparison.OrdinalIgnoreCase));

    internal static void ValidateNativeImage(string path, string rid)
    {
        using var stream = File.OpenRead(path);
        if (rid.StartsWith("win-", StringComparison.Ordinal))
        {
            using var pe = new PEReader(stream);
            var machine = rid.EndsWith("arm64", StringComparison.Ordinal) ? Machine.Arm64 : Machine.Amd64;
            if (pe.PEHeaders.CorHeader is not null || pe.PEHeaders.PEHeader is null || pe.PEHeaders.CoffHeader.Machine != machine ||
                (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) != 0 ||
                (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.ExecutableImage) == 0) throw new BadImageFormatException("Publish did not produce the requested native executable.");
            return;
        }
        Span<byte> header = stackalloc byte[32]; stream.ReadExactly(header);
        if (rid.StartsWith("linux-", StringComparison.Ordinal))
        {
            ushort machine = rid.EndsWith("arm64", StringComparison.Ordinal) ? (ushort)183 : (ushort)62;
            if (!header[..4].SequenceEqual("\u007fELF"u8) || header[4] != 2 || header[5] != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(header[16..]) is not (2 or 3) || BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) != machine)
                throw new BadImageFormatException("Publish did not produce the requested ELF executable.");
        }
        else if (rid.StartsWith("osx-", StringComparison.Ordinal))
        {
            uint machine = rid.EndsWith("arm64", StringComparison.Ordinal) ? 0x0100000cU : 0x01000007U;
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0xfeedfacf || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != machine || BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) != 2)
                throw new BadImageFormatException("Publish did not produce the requested Mach-O executable.");
        }
        else throw new BadImageFormatException("Native executable RID is unsupported.");
    }
}
