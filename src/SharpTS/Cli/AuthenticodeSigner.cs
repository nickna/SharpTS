// Adapted from PE-Packer PR #38, commit 2be414c686dc867f010f73a3648809325b8e58ad.
// Copyright (c) 2026 Nick Nassiri. Licensed under the repository MIT LICENSE.
using PEPacker;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace SharpTS.Cli;

/// <summary>Signs a finished PE file using SignTool or osslsigncode without changing the input on failure.</summary>
internal static class AuthenticodeSigner
{
    internal enum ToolKind { SignTool, OsslSignCode }
    internal sealed record Tool(string Path, ToolKind Kind);
    internal sealed record Invocation(ProcessStartInfo StartInfo, string? StandardInput);
    internal sealed record ToolResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// Signs a temporary copy and replaces <paramref name="executablePath"/> only after success.
    /// Does not import certificates or change a trust store. Cancellation and tool errors preserve the original file.
    /// </summary>
    public static Task SignAsync(string executablePath, AuthenticodeSigningOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return SignCoreAsync(executablePath, options, ResolveTool(options), RunToolAsync, cancellationToken);
    }

    internal static async Task SignCoreAsync(string executablePath, AuthenticodeSigningOptions options, Tool tool,
        Func<Invocation, CancellationToken, Task<ToolResult>> runTool, CancellationToken cancellationToken = default)
    {
        var original = Path.GetFullPath(executablePath);
        Validate(original, options, tool.Kind);
        cancellationToken.ThrowIfCancellationRequested();
        var staged = Path.Combine(Path.GetDirectoryName(original)!, $".{Path.GetFileNameWithoutExtension(original)}.{Guid.NewGuid():N}.signed{Path.GetExtension(original)}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);
        try
        {
            // SignTool edits in place. osslsigncode requires distinct input/output paths.
            if (tool.Kind == ToolKind.SignTool)
                File.Copy(original, staged);
            var invocation = CreateInvocation(original, staged, options, tool);
            var result = await runTool(invocation, timeout.Token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var diagnostic = Redact($"{result.Error}\n{result.Output}", options.Password).Trim();
                if (diagnostic.Length > 4096) diagnostic = diagnostic[..4096];
                throw new PEPackerException(
                    $"Authenticode signing failed (exit {result.ExitCode}). Check the certificate, private key, password, expiration, and timestamp service. {diagnostic}".Trim());
            }
            if (!File.Exists(staged) || new FileInfo(staged).Length == 0)
                throw new PEPackerException("The signing tool reported success without producing a signed file.");
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(staged, original, overwrite: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PEPackerException($"Authenticode signing exceeded its {options.Timeout.TotalSeconds:g} second timeout. The original executable was preserved.");
        }
        catch (Win32Exception ex)
        {
            // Do not expose ProcessStartInfo or its arguments, which may contain a PFX password.
            throw new PEPackerException($"Could not start the signing tool '{tool.Path}'. Install the required signing tool or set ToolPath.", ex);
        }
        finally
        {
            try { File.Delete(staged); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Redact(string diagnostic, string? password) => string.IsNullOrEmpty(password)
        ? diagnostic : diagnostic.Replace(password, "[redacted]", StringComparison.Ordinal);

    internal static void Validate(string path, AuthenticodeSigningOptions options, ToolKind kind)
    {
        if (!File.Exists(path)) throw new PEPackerException($"Executable not found: {path}");
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (pe.PEHeaders.PEHeader is null) throw new BadImageFormatException();
        }
        catch (BadImageFormatException)
        {
            throw new PEPackerException("Authenticode signing requires a Windows PE executable. ELF and Mach-O outputs cannot be Authenticode-signed.");
        }
        if (options.Timeout <= TimeSpan.Zero || options.Timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new PEPackerException("The signing timeout must be positive and less than 49 days.");
        if (options.DigestAlgorithm.ToLowerInvariant() is not ("sha256" or "sha384" or "sha512"))
            throw new PEPackerException("The signing digest must be sha256, sha384, or sha512.");
        if (kind == ToolKind.OsslSignCode && options.Password?.IndexOfAny(['\r', '\n']) >= 0)
            throw new PEPackerException("osslsigncode reads its password as one line; use a PKCS#12 password without line breaks.");
        if (options.TimestampUrl is { } timestamp &&
            (!Uri.TryCreate(timestamp, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new PEPackerException("The RFC 3161 timestamp server must be an absolute HTTP or HTTPS URL without credentials.");
        if (string.IsNullOrWhiteSpace(options.Certificate))
            throw new PEPackerException("A signing certificate (.pfx/.p12 file or sha1: thumbprint) is required.");
        if (options.Certificate.StartsWith("sha1:", StringComparison.OrdinalIgnoreCase))
        {
            if (kind != ToolKind.SignTool)
                throw new PEPackerException("Certificate-store thumbprints require Windows SignTool. Use a .pfx/.p12 file with osslsigncode.");
            var thumbprint = options.Certificate[5..];
            if (thumbprint.Length != 40 || thumbprint.Any(c => !Uri.IsHexDigit(c)))
                throw new PEPackerException("A sha1: certificate thumbprint must contain exactly 40 hexadecimal digits.");
            if (options.Password is not null)
                throw new PEPackerException("A signing password applies only to .pfx/.p12 files, not certificate-store thumbprints.");
        }
        else if (!File.Exists(options.Certificate))
            throw new PEPackerException($"Signing certificate file not found: {options.Certificate}");
    }

    internal static Invocation CreateInvocation(string original, string staged, AuthenticodeSigningOptions options, Tool tool)
    {
        var start = new ProcessStartInfo(tool.Path)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        void Add(params string[] values) { foreach (var value in values) start.ArgumentList.Add(value); }
        var digest = options.DigestAlgorithm.ToLowerInvariant();
        string? input = null;
        Add("sign");
        if (tool.Kind == ToolKind.SignTool)
        {
            Add("/fd", digest);
            if (options.Certificate.StartsWith("sha1:", StringComparison.OrdinalIgnoreCase))
                Add("/sha1", options.Certificate[5..]);
            else
            {
                Add("/f", Path.GetFullPath(options.Certificate));
                if (options.Password is not null) Add("/p", options.Password);
            }
            if (options.TimestampUrl is { } timestamp) Add("/tr", timestamp, "/td", digest);
            Add(staged);
        }
        else
        {
            Add("-h", digest, "-pkcs12", Path.GetFullPath(options.Certificate), "-readpass", "/dev/stdin");
            // Send the PKCS#12 password over stdin rather than exposing it in process arguments.
            // Older packaged osslsigncode versions treat '-' as a literal filename.
            input = options.Password ?? "";
            if (options.TimestampUrl is { } timestamp) Add("-ts", timestamp);
            Add("-in", original, "-out", staged);
        }
        return new(start, input);
    }

    internal static async Task<ToolResult> RunToolAsync(Invocation invocation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = invocation.StartInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            if (invocation.StandardInput is { } input)
                await process.StandardInput.WriteLineAsync(input.AsMemory(), token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            var pipes = Task.WhenAll(output, error);
            try { await pipes.WaitAsync(cleanup.Token).ConfigureAwait(false); }
            catch { ObserveLateFailure(pipes); }
            throw;
        }
    }

    private static void ObserveLateFailure(Task task)
    {
        _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal static Tool ResolveTool(AuthenticodeSigningOptions options)
    {
        var kind = OperatingSystem.IsWindows() ? ToolKind.SignTool : ToolKind.OsslSignCode;
        if (options.ToolPath is { Length: > 0 } explicitPath) return new(explicitPath, kind);
        var name = kind == ToolKind.SignTool ? "signtool.exe" : "osslsigncode";
        var path = FindInPath(name);
        if (path is not null) return new(path, kind);
        if (kind == ToolKind.SignTool)
        {
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            var architectures = new[] { System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "x64", "arm64", "x86" }.Distinct();
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)).Distinct())
            {
                var sdkBin = Path.Combine(root, "Windows Kits", "10", "bin");
                foreach (var version in VersionDirectories(sdkBin))
                    foreach (var architecture in architectures)
                    {
                        var candidate = Path.Combine(version, architecture, name);
                        if (File.Exists(candidate)) return new(candidate, kind);
                    }
            }
            // Some tool installations place SignTool alongside the .NET SDK's tools.
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(dotnet)) dotnet = Path.GetDirectoryName(FindInPath("dotnet.exe"));
            if (dotnet is not null)
                foreach (var version in VersionDirectories(Path.Combine(dotnet, "sdk")))
                    foreach (var relative in new[] { name, Path.Combine("tools", name) })
                    {
                        var candidate = Path.Combine(version, relative);
                        if (File.Exists(candidate)) return new(candidate, kind);
                    }
            throw new PEPackerException("Authenticode signing requires signtool.exe. Install the Windows SDK Signing Tools or put signtool.exe on PATH. See https://learn.microsoft.com/windows/win32/seccrypto/signtool.");
        }
        throw new PEPackerException("Authenticode signing requires osslsigncode on PATH. Install it with your package manager (for example, apt install osslsigncode or brew install osslsigncode). See https://github.com/mtrojnar/osslsigncode.");
    }

    private static IEnumerable<string> VersionDirectories(string root) => Directory.Exists(root)
        ? Directory.EnumerateDirectories(root).Select(path => (Path: path, Parsed: System.Version.TryParse(Path.GetFileName(path).Split('-')[0], out var parsed) ? parsed : null))
            .Where(item => item.Parsed is not null).OrderByDescending(item => item.Parsed).Select(item => item.Path)
        : [];

    private static string? FindInPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory.Trim('"'), name)).FirstOrDefault(File.Exists);
}
