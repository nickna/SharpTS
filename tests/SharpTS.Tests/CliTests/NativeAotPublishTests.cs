using System.Diagnostics;
using System.Reflection.PortableExecutable;
using SharpTS.Cli;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.ProcessTreeFixture;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CliTests;

public sealed class NativeAotPublishTests
{
    private static string Rid => NativeAotProjectOptions.HostRuntimeIdentifier!;

    [Fact]
    public void NativeTargetSelectsGuestDllAndExplicitExecutableOutput()
    {
        var command = Assert.IsType<ParsedCommand.Compile>(new CommandLineParser().Parse([
            "--compile", "app.ts", "--target", "native-aot", "--runtime", Rid, "-o", "native app"]));
        Assert.Equal(OutputTarget.Dll, command.CompileOptions.Target);
        Assert.Equal(new NativeAotPublishOptions("native app", Rid), command.CompileOptions.NativePublish);
        Assert.Null(command.CompileOptions.NativeProject);
        Assert.True(command.CompileOptions.UseReferenceAssemblies);
        Assert.Equal("native app", command.OutputFile);
    }

    [Theory]
    [InlineData("--noEmit")]
    [InlineData("--hosted")]
    [InlineData("--pack")]
    [InlineData("--emitDeclarationOnly")]
    [InlineData("--showConfig")]
    public void NativeTargetRejectsConflictingOutputModes(string conflict)
    {
        Assert.IsType<ParsedCommand.Error>(new CommandLineParser().Parse([
            "--compile", "app.ts", "--target", "native-aot", "--runtime", Rid, "-o", "native app", conflict]));
    }

    [Fact]
    public void NativeTargetRequiresAnOutputAndMatchingRidAndKeepsInspectSeparate()
    {
        var parser = new CommandLineParser();
        Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "app.ts", "-t", "native-aot", "--runtime", Rid]));
        Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "app.ts", "-t", "native-aot", "-o", "app"]));
        Assert.IsType<ParsedCommand.Error>(parser.Parse(["-c", "app.ts", "-t", "native-aot", "--runtime", Rid, "-o", "app", "--emit-native-project", "project"]));
        var signed = parser.Parse(["-c", "app.ts", "-t", "native-aot", "--runtime", Rid, "-o", "app", "--sign", "fixture.pfx"]);
        if (OperatingSystem.IsWindows()) Assert.NotNull(Assert.IsType<ParsedCommand.Compile>(signed).CompileOptions.Signing);
        else Assert.Contains("Windows native output", Assert.IsType<ParsedCommand.Error>(signed).Message);
    }

    [Theory]
    [InlineData("Guest")]
    [InlineData("guest")]
    [InlineData("System.Console")]
    [InlineData("System.Private.CoreLib")]
    public void WrapperNamesCannotShadowTheGuestOrFramework(string name)
    {
        string destination = Path.Combine(Path.GetTempPath(), "sharpts_reserved_" + Guid.NewGuid().ToString("N"));
        var error = Assert.Throws<SharpTS.Diagnostics.Exceptions.CompileException>(() =>
            NativeAotProjectExporter.ValidateDestination(new(destination, Rid) { AssemblyName = name }));
        Assert.Equal(SharpTS.Diagnostics.DiagnosticCode.NativeGuestPublishOutput, error.Diagnostic.Code);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void ActualSavedDependencyNamesAreReservedCaseInsensitively()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-wrapper-name");
        try
        {
            var guest = Guest("console.log(42);", false);
            var options = new NativeAotProjectOptions(Path.Combine(root, "project"), Rid) { AssemblyName = "system.console" };
            var error = Assert.Throws<SharpTS.Diagnostics.Exceptions.CompileException>(() => NativeAotProjectExporter.Export(options,
                NativeAotCompatibilityReport.Analyze(SharpTSRuntimeRequirements.None, [], false), path => File.WriteAllBytes(path, guest.Assembly)));
            Assert.Equal(SharpTS.Diagnostics.DiagnosticCode.NativeGuestPublishOutput, error.Diagnostic.Code);
            Assert.False(File.Exists(Path.Combine(options.OutputDirectory, "NativeApp.csproj")));
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuestRevisionPreservesSemanticChangesAndCoherentDebugIdentity(bool debug)
    {
        var first = Guest("console.log(42);", debug);
        var second = Guest("console.log(42);", debug);
        Assert.NotEqual(first.Assembly, second.Assembly);
        var one = NativeGuestRevision.Compute(first.Assembly, first.Pdb);
        var two = NativeGuestRevision.Compute(second.Assembly, second.Pdb);
        Assert.Equal(one.RevisionSha256, two.RevisionSha256);
        Assert.NotEqual(one.ImageSha256, two.ImageSha256);
        Assert.Equal(one.PdbSha256, two.PdbSha256);
        foreach (string source in new[] { "console.log(43);", "function extra(value:boolean):boolean{return value;} console.log(extra(true));", "enum Extra { First, Second } console.log(Extra.Second);" })
        {
            var changed = Guest(source, debug);
            Assert.NotEqual(one.RevisionSha256, NativeGuestRevision.Compute(changed.Assembly, changed.Pdb).RevisionSha256);
        }
        if (debug)
        {
            using var reader = new PEReader(new MemoryStream(first.Assembly));
            using var other = new PEReader(new MemoryStream(second.Assembly));
            Assert.Equal(reader.ReadCodeViewDebugDirectoryData(Assert.Single(reader.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.CodeView)).Guid,
                other.ReadCodeViewDebugDirectoryData(Assert.Single(other.ReadDebugDirectory(), entry => entry.Type == DebugDirectoryEntryType.CodeView)).Guid);
        }
    }

    [Fact]
    public void GuestRevisionRejectsNativeOrMalformedImages()
    {
        Assert.Throws<BadImageFormatException>(() => NativeGuestRevision.Compute([1, 2, 3]));
        Assert.Throws<BadImageFormatException>(() => NativeGuestRevision.Compute(File.ReadAllBytes(Environment.ProcessPath!)));
    }

    [Theory]
    [InlineData("ILC : Trim analysis error IL2075: owner lookup is unproved [project.csproj]", "IL2075", "error")]
    [InlineData("source.cs(2,3): warning CS0168: unused local [project.csproj]", "CS0168", "warning")]
    [InlineData("  ILC: Method '[Guest]$Program.Main()' will always throw because: Failed to load type 'System.Console' from assembly 'System.Console, Version=1.0.0.0, Culture=neutral, PublicKey=null'", "SHARPTS408", "error")]
    [InlineData("ILC: Method '[Guest]$Program.Run()' will always throw because: Invalid IL or CLR metadata", "SHARPTS408", "error")]
    public void ToolDiagnosticsRetainOriginalCodesAndRawText(string line, string code, string severity)
    {
        var diagnostic = Assert.IsType<NativePublishDiagnostic>(NativePublishDiagnostic.Parse(line));
        Assert.Equal(code, diagnostic.Code); Assert.Equal(severity, diagnostic.Severity); Assert.Equal(line, diagnostic.RawText);
    }

    [Fact]
    public void ToolDiagnosticsPreserveWindowsSourceLocations()
    {
        var diagnostic = NativePublishDiagnostic.Parse(@"C:\work\source.cs(20,3): error CS1234: source failure [NativeApp.csproj]");
        Assert.Equal(@"C:\work\source.cs(20,3)", diagnostic!.Origin);
    }

    [Fact]
    public async Task SdkProcessDrainsBothPipesAndReportsActualExit()
    {
        var start = Fixture("dual-output");
        using var output = new StringWriter(); using var error = new StringWriter();
        var result = await NativePublishProcess.RunAsync(start, output, error, CancellationToken.None);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("stdout-end", output.ToString()); Assert.Contains("stderr-end", error.ToString());
    }

    [Fact]
    public async Task SdkCancellationTerminatesTheOwnedChildProcessTree()
    {
        string path = Path.Combine(Path.GetTempPath(), "sharpts_native_pid_" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativePublishProcess.RunAsync(Fixture("parent-with-pid-file", path), TextWriter.Null, TextWriter.Null, cancellation.Token));
            Assert.True(File.Exists(path));
            int child = int.Parse(File.ReadAllText(path));
            try { using var process = Process.GetProcessById(child); Assert.True(process.WaitForExit(5000)); }
            catch (ArgumentException) { }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StagedSigningFailureAndCancelledPromotionPreservePriorOutput()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-output-test");
        try
        {
            string output = Path.Combine(root, "app.exe"); File.WriteAllText(output, "prior");
            using var workspace = new NativePublishWorkspace(new(output, "win-x64"), root);
            Directory.CreateDirectory(workspace.PublishDirectory); File.WriteAllText(Path.Combine(workspace.PublishDirectory, "app.exe"), "unsigned");
            await Assert.ThrowsAsync<IOException>(() => NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace,
                new() { Certificate = "fixture.pfx", Password = "not-logged" }, CancellationToken.None,
                (_, _, _) => throw new IOException("fixture signing failed")));
            Assert.Equal("prior", File.ReadAllText(output));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, null, new CancellationToken(true)));
            Assert.Equal("prior", File.ReadAllText(output));
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task PromotionTracksItsOwnSymbolsAndSignsFreshCopiesWithoutChangingUnsignedArtifacts()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-output-test");
        try
        {
            string output = Path.Combine(root, "app.exe");
            using var workspace = new NativePublishWorkspace(new(output, "win-x64"), root);
            Directory.CreateDirectory(workspace.PublishDirectory);
            string unsigned = Path.Combine(workspace.PublishDirectory, "app.exe"); File.WriteAllText(unsigned, "unsigned");
            string pdb = Path.Combine(workspace.PublishDirectory, "app.pdb"); File.WriteAllText(pdb, "coherent symbols");
            int signs = 0;
            Task Sign(string file, AuthenticodeSigningOptions _, CancellationToken token) { token.ThrowIfCancellationRequested(); File.AppendAllText(file, " signed" + ++signs); return Task.CompletedTask; }
            var signing = new AuthenticodeSigningOptions { Certificate = "fixture.pfx" };
            await NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, signing, CancellationToken.None, Sign);
            await NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, signing, CancellationToken.None, Sign);
            Assert.Equal("unsigned signed2", File.ReadAllText(output)); Assert.Equal("unsigned", File.ReadAllText(unsigned));
            Assert.Equal("coherent symbols", File.ReadAllText(Path.Combine(root, "app.pdb")));
            File.Delete(pdb);
            await NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, null, CancellationToken.None);
            Assert.False(File.Exists(Path.Combine(root, "app.pdb")));
            File.WriteAllText(pdb, "new native symbols");
            await NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, null, CancellationToken.None);
            File.WriteAllText(Path.Combine(root, "app.pdb"), "user modified symbols");
            File.Delete(pdb);
            await Assert.ThrowsAsync<IOException>(() => NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, null, CancellationToken.None));
            Assert.Equal("user modified symbols", File.ReadAllText(Path.Combine(root, "app.pdb")));
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    private static ProcessStartInfo Fixture(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet"); start.ArgumentList.Add(typeof(ProcessTreeFixtureMarker).Assembly.Location);
        foreach (string argument in arguments) start.ArgumentList.Add(argument); return start;
    }

    [Fact]
    public async Task ConcurrentDifferentPublishesCannotInterleaveTheSameOutputPromotion()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-output-lock-test");
        try
        {
            using var workspace = new NativePublishWorkspace(new(Path.Combine(root, "app.exe"), "win-x64"), root);
            Directory.CreateDirectory(workspace.PublishDirectory); File.WriteAllText(Path.Combine(workspace.PublishDirectory, "app.exe"), "unsigned");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Sign(string file, AuthenticodeSigningOptions _, CancellationToken token)
            { started.SetResult(); await release.Task.WaitAsync(token); File.AppendAllText(file, " signed"); }
            var first = NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace,
                new() { Certificate = "fixture.pfx" }, CancellationToken.None, Sign);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativePublishOutput.PromoteAsync(workspace.PublishDirectory, "app.exe", workspace, null, cancellation.Token));
            }
            finally { release.SetResult(); await first; }
            Assert.Equal("unsigned signed", File.ReadAllText(workspace.Options.OutputPath));
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task CacheLocksValidateCompleteArtifactPairsAndRepairOnlyOwnedEntries()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-cache-test");
        try
        {
            string name = OperatingSystem.IsWindows() ? "app.exe" : "app";
            using var workspace = new NativePublishWorkspace(new(Path.Combine(root, name), Rid), root);
            var guest = Guest("console.log(42);", true);
            NativeAotProjectExporter.Export(workspace.ProjectOptions, NativeAotCompatibilityReport.Analyze(SharpTSRuntimeRequirements.None, [], false), path =>
            { File.WriteAllBytes(path, guest.Assembly); File.WriteAllBytes(Path.ChangeExtension(path, ".pdb"), guest.Pdb!); });
            File.WriteAllText(Path.Combine(workspace.ProjectDirectory, "global.json"), "{}");
            var revision = NativeGuestRevision.Compute(guest.Assembly, guest.Pdb);
            string key = NativePublishCache.Key(workspace, revision, "toolchain-one");
            Assert.NotEqual(key, NativePublishCache.Key(workspace, revision, "toolchain-two"));
            Directory.CreateDirectory(workspace.PublishDirectory);
            File.Copy(Environment.ProcessPath!, Path.Combine(workspace.PublishDirectory, name));
            File.WriteAllText(Path.Combine(workspace.PublishDirectory, "app.pdb"), "native symbols");
            using (await NativePublishCache.LockAsync(root, key, CancellationToken.None))
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativePublishCache.LockAsync(root, key, cancellation.Token));
                NativePublishCache.Store(root, key, workspace, name, revision);
            }
            string artifacts = Assert.IsType<string>(NativePublishCache.Find(root, key, name));
            Assert.Equal(guest.Assembly, File.ReadAllBytes(Path.Combine(root, "entries", key, "guest", "Guest.dll")));
            Assert.Equal(guest.Pdb, File.ReadAllBytes(Path.Combine(root, "entries", key, "guest", "Guest.pdb")));
            File.AppendAllText(Path.Combine(artifacts, "app.pdb"), "damaged");
            Assert.Null(NativePublishCache.Find(root, key, name));
            NativePublishCache.Store(root, key, workspace, name, revision);
            Assert.NotNull(NativePublishCache.Find(root, key, name));
            if (!OperatingSystem.IsWindows())
            {
                string native = Path.Combine(artifacts, name);
                var originalMode = File.GetUnixFileMode(native);
                File.SetUnixFileMode(native, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Assert.Null(NativePublishCache.Find(root, key, name));
                NativePublishCache.Store(root, key, workspace, name, revision);
                Assert.Equal(originalMode, File.GetUnixFileMode(native));
            }
            string unknown = Path.Combine(root, "entries", new string('a', 64)); Directory.CreateDirectory(unknown);
            File.WriteAllText(Path.Combine(unknown, "user.txt"), "preserve");
            NativePublishCache.Store(root, new string('a', 64), workspace, name, revision);
            Assert.Equal("preserve", File.ReadAllText(Path.Combine(unknown, "user.txt")));
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task CachePreparationDeadlineTerminatesItsOwnedProbeAndDisablesReuse()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-cache-deadline");
        try
        {
            string pidFile = Path.Combine(root, "child.pid");
            int keys = 0;
            async Task<(string Fingerprint, bool CanReuse)> Probe(CancellationToken token)
            {
                await NativePublishProcess.RunAsync(Fixture("parent-with-pid-file", pidFile), TextWriter.Null, TextWriter.Null, token);
                return ("incomplete", true);
            }
            using var output = new StringWriter();
            using var preparation = await NativePublishCachePreparation.PrepareAsync(Probe,
                _ => { keys++; return new string('a', 64); }, root, output, false,
                CancellationToken.None, TimeSpan.FromSeconds(3));
            Assert.False(preparation.CanReuse);
            Assert.Empty(preparation.Key);
            Assert.Equal(0, keys);
            Assert.Contains("timed out", output.ToString());
            Assert.True(File.Exists(pidFile));
            int child = int.Parse(File.ReadAllText(pidFile));
            try { using var process = Process.GetProcessById(child); Assert.True(process.WaitForExit(5000)); }
            catch (ArgumentException) { }
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task OptionalCacheLockTimeoutPreservesEntriesAndDoesNotKeepTheLock()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-cache-lock-deadline");
        try
        {
            string key = new('a', 64);
            string prior = Path.Combine(root, "prior.txt"); File.WriteAllText(prior, "preserve");
            using var output = new StringWriter();
            using (await NativePublishCache.LockAsync(root, key, CancellationToken.None))
            using (var preparation = await NativePublishCachePreparation.PrepareAsync(
                _ => Task.FromResult(("complete", true)), _ => key, root, output, false,
                CancellationToken.None, TimeSpan.FromMilliseconds(100)))
            {
                Assert.False(preparation.CanReuse);
                Assert.Empty(preparation.Key);
                // The optional deadline can expire before the worker reaches
                // the lock phase; either path must report the timeout fallback.
                Assert.Contains("Native cache preparation timed out; publishing without artifact reuse.", output.ToString());
                Assert.Equal("preserve", File.ReadAllText(prior));
            }
            using var available = await NativePublishCache.LockAsync(root, key, CancellationToken.None);
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task LateFingerprintCompletionCannotAuthorizeAKeyAfterTheDeadline()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-cache-late-probe");
        try
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<(string, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
            int keys = 0;
            async Task<(string, bool)> Fingerprint(CancellationToken token)
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                return await release.Task.ConfigureAwait(false);
            }
            var pending = NativePublishCachePreparation.PrepareAsync(Fingerprint,
                _ => { keys++; return new string('a', 64); }, root, TextWriter.Null, true,
                CancellationToken.None, TimeSpan.FromMilliseconds(100));
            try
            {
                // The deadline starts before Task.Run. A queued worker can expire
                // without ever invoking the fingerprint; both states disable reuse.
                await Task.WhenAny(cancelled.Task, pending);
                release.TrySetResult(("late", true));
                using var preparation = await pending;
                Assert.False(preparation.CanReuse);
                Assert.Empty(preparation.Key);
                Assert.Equal(0, keys);
            }
            finally
            {
                release.TrySetResult(("late", true));
                using var cleanup = await pending;
            }
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task CallerCancellationStillCancelsInsteadOfStartingAnUncachedPublish()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-cache-caller-cancel");
        try
        {
            using var caller = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<(string, bool)> Probe(CancellationToken token)
            { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return ("cancelled", true); }
            var pending = NativePublishCachePreparation.PrepareAsync(Probe, _ => new string('a', 64),
                root, TextWriter.Null, true, caller.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await caller.CancelAsync();
            bool cancelled = false;
            try { using var unexpected = await pending; }
            catch (OperationCanceledException) { cancelled = true; }
            Assert.True(cancelled);
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    [Fact]
    public async Task CompletedCachePreparationOwnsItsLockUntilPublicationFinishes()
    {
        string root = NativePublishWorkspace.CreateOwned(Path.GetTempPath(), "native-cache-complete");
        try
        {
            string key = new('a', 64);
            using (var preparation = await NativePublishCachePreparation.PrepareAsync(
                _ => Task.FromResult(("complete", true)), _ => key, root, TextWriter.Null, true,
                CancellationToken.None))
            {
                Assert.True(preparation.CanReuse);
                Assert.Equal(key, preparation.Key);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativePublishCache.LockAsync(root, key, cancellation.Token));
            }
            using var available = await NativePublishCache.LockAsync(root, key, CancellationToken.None);
        }
        finally { NativePublishWorkspace.DeleteOwned(root); }
    }

    private static (byte[] Assembly, byte[]? Pdb) Guest(string source, bool debug)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("Guest", false, true, null, null, null, OutputTarget.Dll) { EmitDebugSymbols = debug };
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, map);
        var artifacts = compiler.SaveArtifacts("Guest.pdb");
        using var stream = new MemoryStream(artifacts.Assembly); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return (artifacts.Assembly, artifacts.Pdb);
    }
}
