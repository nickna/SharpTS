// Adapted from PE-Packer PR #38 under the repository MIT LICENSE.
using PEPacker;
using System.Diagnostics;
using SharpTS.Cli;
using SharpTS.ProcessTreeFixture;
using Xunit;
using static SharpTS.Cli.AuthenticodeSigner;

namespace SharpTS.Tests.CliTests;

public class AuthenticodeSignerTests
{
    [Fact]
    public void SignToolArgumentsKeepPathsAndPasswordsAsSingleArguments()
    {
        var options = new AuthenticodeSigningOptions { Certificate = "certificate with spaces.pfx", Password = "a quote \" and $()", DigestAlgorithm = "SHA256" };
        var invocation = CreateInvocation("original.exe", "signed file.exe", options, new("signtool.exe", ToolKind.SignTool));
        Assert.False(invocation.StartInfo.UseShellExecute);
        Assert.Equal(new[] { "sign", "/fd", "sha256", "/f", Path.GetFullPath(options.Certificate), "/p", options.Password,
            "/tr", AuthenticodeSigningOptions.DefaultTimestampUrl, "/td", "sha256", "signed file.exe" }, invocation.StartInfo.ArgumentList);
        Assert.DoesNotContain(options.Password, options.ToString());
    }

    [Fact]
    public void ThumbprintAndOfflineSigningDoNotRequestPfxPasswordOrTimestamp()
    {
        var hash = new string('a', 40);
        var options = new AuthenticodeSigningOptions { Certificate = "sha1:" + hash, TimestampUrl = null };
        var invocation = CreateInvocation("original.exe", "signed.exe", options, new("signtool.exe", ToolKind.SignTool));
        Assert.Equal(new[] { "sign", "/fd", "sha256", "/sha1", hash, "signed.exe" }, invocation.StartInfo.ArgumentList);
    }

    [Fact]
    public void OsslSignCodeReceivesPasswordOnStdinAndUsesRfc3161Timestamp()
    {
        var options = new AuthenticodeSigningOptions { Certificate = "test.pfx", Password = "secret" };
        var invocation = CreateInvocation("original.exe", "signed.exe", options, new("osslsigncode", ToolKind.OsslSignCode));
        Assert.Equal("secret", invocation.StandardInput);
        Assert.DoesNotContain("secret", invocation.StartInfo.ArgumentList);
        Assert.Equal(new[] { "sign", "-h", "sha256", "-pkcs12", Path.GetFullPath("test.pfx"), "-readpass", "/dev/stdin",
            "-ts", AuthenticodeSigningOptions.DefaultTimestampUrl, "-in", "original.exe", "-out", "signed.exe" }, invocation.StartInfo.ArgumentList);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ToolFailuresPreserveOriginalAndRemoveStagingFiles(int toolKind)
    {
        var kind = (ToolKind)toolKind;
        using var fixture = new Fixture();
        var original = File.ReadAllBytes(fixture.Executable);
        var options = fixture.Options with { Password = "do-not-log-me" };
        var error = await Assert.ThrowsAsync<PEPackerException>(() => SignCoreAsync(fixture.Executable, options, new("fake", kind), (invocation, _) =>
        {
            File.WriteAllText(invocation.StartInfo.ArgumentList[^1], "partial signed output");
            return Task.FromResult(new ToolResult(1, "", "bad password do-not-log-me"));
        }));
        Assert.DoesNotContain("do-not-log-me", error.Message);
        Assert.Contains("[redacted]", error.Message);
        Assert.Equal(original, File.ReadAllBytes(fixture.Executable));
        Assert.Equal(2, Directory.GetFiles(fixture.Root).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SuccessfulToolReplacesOriginalOnlyAfterCompletion(int toolKind)
    {
        var kind = (ToolKind)toolKind;
        using var fixture = new Fixture();
        var original = File.ReadAllBytes(fixture.Executable);
        await SignCoreAsync(fixture.Executable, fixture.Options, new("fake", kind), (invocation, _) =>
        {
            Assert.Equal(original, File.ReadAllBytes(fixture.Executable));
            File.WriteAllText(invocation.StartInfo.ArgumentList[^1], "signed");
            return Task.FromResult(new ToolResult(0, "", ""));
        });
        Assert.Equal("signed", File.ReadAllText(fixture.Executable));
        Assert.Equal(2, Directory.GetFiles(fixture.Root).Length);
    }

    [Fact]
    public async Task TimeoutAndCancellationPreserveOriginal()
    {
        using var fixture = new Fixture();
        var original = File.ReadAllBytes(fixture.Executable);
        var options = fixture.Options with { Timeout = TimeSpan.FromMilliseconds(30) };
        var error = await Assert.ThrowsAsync<PEPackerException>(() => SignCoreAsync(fixture.Executable, options,
            new("fake", ToolKind.SignTool), async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new(0, "", ""); }));
        Assert.Contains("timeout", error.Message);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SignCoreAsync(fixture.Executable, fixture.Options,
            new("fake", ToolKind.SignTool), (_, _) => throw new Exception("must not run"), cancellation.Token));
        Assert.Equal(original, File.ReadAllBytes(fixture.Executable));
    }

    [Fact]
    public void InvalidInputsAreRejectedBeforeToolInvocation()
    {
        using var fixture = new Fixture();
        Assert.Throws<PEPackerException>(() => Validate(fixture.Executable, fixture.Options with { Certificate = "sha1:no" }, ToolKind.SignTool));
        Assert.Throws<PEPackerException>(() => Validate(fixture.Executable, fixture.Options with { Certificate = "sha1:" + new string('a', 40) }, ToolKind.OsslSignCode));
        Assert.Throws<PEPackerException>(() => Validate(fixture.Executable, fixture.Options with { TimestampUrl = "file:///tmp/tsa" }, ToolKind.SignTool));
        Assert.Throws<PEPackerException>(() => Validate(fixture.Executable, fixture.Options with { DigestAlgorithm = "sha1" }, ToolKind.SignTool));
        File.WriteAllText(fixture.Executable, "ELF");
        Assert.Contains("Windows PE", Assert.Throws<PEPackerException>(() => Validate(fixture.Executable, fixture.Options, ToolKind.SignTool)).Message);
    }

    [Theory]
    [InlineData(" surrounding-spaces ")]
    [InlineData("\twhitespace-secret\r\n")]
    public async Task RedactionPrecedesDiagnosticWhitespaceNormalization(string password)
    {
        using var fixture = new Fixture();
        var error = await Assert.ThrowsAsync<PEPackerException>(() => SignCoreAsync(fixture.Executable,
            fixture.Options with { Password = password }, new("fake", ToolKind.SignTool),
            (_, _) => Task.FromResult(new ToolResult(1, "", password))));
        Assert.Contains("[redacted]", error.Message);
        Assert.DoesNotContain(password.Trim(), error.Message);
    }

    [Fact]
    public async Task CancellationStopsTheOwnedSigningProcessTree()
    {
        using var fixture = new Fixture();
        string pidFile = Path.Combine(fixture.Root, "child.pid");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);
        start.ArgumentList.Add("parent-with-pid-file");
        start.ArgumentList.Add(pidFile);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<ToolResult> run = RunToolAsync(new(start, null), cancellation.Token);
        Process? child = null;
        try
        {
            var ready = Stopwatch.StartNew();
            while ((!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0) && ready.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
            Assert.True(File.Exists(pidFile) && new FileInfo(pidFile).Length > 0, "Signing fixture did not create its child.");
            child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile)));
            var elapsed = Stopwatch.StartNew();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await run.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await child.WaitForExitAsync(exited.Token);
            Assert.True(child.HasExited);
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(7)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpts-signing-" + Guid.NewGuid().ToString("N"));
        public string Executable => Path.Combine(Root, "hello.exe");
        public AuthenticodeSigningOptions Options => new() { Certificate = Path.Combine(Root, "test.pfx"), TimestampUrl = null };
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            File.Copy(typeof(AuthenticodeSigner).Assembly.Location, Executable);
            File.WriteAllText(Path.Combine(Root, "test.pfx"), "fixture");
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
