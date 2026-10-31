using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Diagnostics;
using SharpTS.Cli;
using SharpTS.Declaration;
using SharpTS.Runtime.DotNet;
using SharpTS.Testing;
using System.Text.Json;
using System.Xml.Linq;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

[Collection("ExternalAssembly")]
public sealed class NativeManagedReferenceIntegrationTests(ExternalAssemblyFixture fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CliCapturesCompleteTransitiveRuntimeGraphWithoutLoadingItIntoTheHost(bool imports, bool manifestReferences)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string[] references = ExtractChain(directory);
        string source = imports
            ? "import {MainInfo} from 'dotnet:TestPkg.Main'; console.log(MainInfo.Describe());"
            : "@DotNetType(\"TestPkg.Main.MainInfo\") declare class MainInfo { static Describe():string; } console.log(MainInfo.Describe());";
        directory.CreateFile("main.ts", source);
        if (manifestReferences) directory.CreateFile("sharpts.json", JsonSerializer.Serialize(new { references }));
        string arguments = manifestReferences ? "" : $"-r \"{references[0]}\" -r \"{references[1]}\"";
        var result = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} --verify {arguments}", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Contains("IL verification passed", result.StandardOutput);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(directory.GetPath("native/native-project.json")));
        Assert.Equal(2, manifest.RootElement.GetProperty("managedReferences").GetArrayLength());
        File.Delete(references[0]); File.Delete(references[1]);
        SdkNativePublishTests.AssertSuccess(SdkNativePublishTests.Dotnet(directory.GetPath("native"), "build", "NativeApp.csproj", "-c", "Release"));
        var run = SdkNativePublishTests.Dotnet(directory.Path, directory.GetPath($"native/bin/Release/net10.0/{NativeAotProjectOptions.HostRuntimeIdentifier}/NativeApp.dll"));
        SdkNativePublishTests.AssertSuccess(run);
        Assert.Equal("main-1.0 on base-1.0\n", run.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void MissingImplementationDependencyRefusesBeforeExport()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string[] references = ExtractChain(directory);
        directory.CreateFile("main.ts", "console.log(42);");
        var result = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --msbuild-errors --emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} -r \"{references[0]}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SHARPTS402", result.StandardOutput + result.StandardError);
        Assert.Contains("TestPkg.Base", result.StandardOutput + result.StandardError);
        Assert.False(Directory.Exists(directory.GetPath("native")));
    }

    [Fact]
    public void SdkUsesCapturedHintPathBytesAfterResolution()
    {
        string source = "import {MainInfo} from 'dotnet:TestPkg.Main'; console.log(MainInfo.Describe());";
        using var consumer = SdkNativePublishTests.CreateConsumer(source);
        string[] references = ExtractChain(consumer);
        var project = XDocument.Load(consumer.GetPath("Smoke.csproj"));
        project.Root!.Add(new XElement("ItemGroup", references.Select(path => new XElement("Reference",
            new XAttribute("Include", Path.GetFileNameWithoutExtension(path)), new XElement("HintPath", path)))));
        project.Save(consumer.GetPath("Smoke.csproj"));
        SdkNativePublishTests.AssertSuccess(SdkNativePublishTests.Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release"));
        string wrapper = consumer.GetPath($"bin/Release/net10.0/{NativeAotProjectOptions.HostRuntimeIdentifier}/SdkNativeSmoke.dll");
        var run = SdkNativePublishTests.Dotnet(consumer.Path, wrapper);
        SdkNativePublishTests.AssertSuccess(run);
        Assert.Equal("main-1.0 on base-1.0\n", run.StandardOutput.Replace("\r\n", "\n"));
        var exported = Assert.Single(Directory.GetFiles(consumer.Path, "native-project.json", SearchOption.AllDirectories));
        Assert.Equal(2, NativeReferenceManifest.ReadAndVerify(Path.GetDirectoryName(exported)!).Count);
        string publish = consumer.GetPath("native publish");
        var published = SdkNativePublishTests.Dotnet(consumer.Path, "publish", "Smoke.csproj", "-c", "Release", "-r", NativeAotProjectOptions.HostRuntimeIdentifier!, "-o", publish);
        SdkNativePublishTests.AssertSuccess(published);
        Assert.DoesNotContain(": warning ", published.StandardOutput + published.StandardError, StringComparison.OrdinalIgnoreCase);
        string executable = Path.Combine(publish, "SdkNativeSmoke" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        NativeAotPublishDriver.ValidateNativeImage(executable, NativeAotProjectOptions.HostRuntimeIdentifier!);
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["PATH"] = "";
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        var native = TestProcess.Run(start, TimeSpan.FromSeconds(30), "SDK explicit managed closure native execution");
        Assert.Equal(0, native.ExitCode); Assert.Empty(native.StandardError);
        Assert.Equal("main-1.0 on base-1.0\n", native.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void AdmittedImplementationRetainsStrictUpstreamTrimAndAotDiagnostics()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "@DotNetType(\"ExternalNativeWarning.Entry\") declare class Entry { static Run():string; } console.log(Entry.Run());");
        var exported = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} --verify -r \"{fixture.NativeWarningDllPath}\"", directory.Path);
        Assert.True(exported.ExitCode == 0, exported.StandardOutput + exported.StandardError);
        var published = SdkNativePublishTests.Dotnet(directory.GetPath("native"), "publish", "NativeApp.csproj", "-c", "Release");
        Assert.NotEqual(0, published.ExitCode);
        Assert.Contains("IL2026", published.StandardOutput + published.StandardError);
        Assert.Contains("IL3050", published.StandardOutput + published.StandardError);
    }

    [Fact]
    public void CapturedMetadataScopeBypassesRuntimeTypeCacheAndDoesNotLoadWorkspaceCode()
    {
        string name = ExternalAssemblyFixture.GreeterAssemblyName;
        var loadedBefore = AppDomain.CurrentDomain.GetAssemblies().Count(assembly => assembly.GetName().Name == name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var closure = NativeManagedReferenceClosure.Capture(directory.Path, [fixture.GreeterDllPath], null);
        using var loader = closure.CreateLoader();
        using (DotNetTypeRegistry.UseMetadataResolver(loader.TryResolve))
        {
            var type = DotNetTypeRegistry.Resolve(ExternalAssemblyFixture.GreeterTypeName)!;
            Assert.True(type.Assembly.ReflectionOnly);
            var metadata = new TypeInspector().Inspect(type, includeInherited: true);
            Assert.Contains(metadata.StaticMethods, method => method.Name == "Hello");
            var framework = new TypeInspector().Inspect(loader.TryResolve("System.String")!, includeInherited: true);
            Assert.Contains(framework.StaticMethods, method => method.Parameters.Any(parameter => parameter.IsParams));
            Assert.Contains(framework.StaticMethods, method => method.Name == "Copy" && method.Obsolete is not null);
        }
        Assert.Equal(loadedBefore, AppDomain.CurrentDomain.GetAssemblies().Count(assembly => assembly.GetName().Name == name));
    }

    [Fact]
    public void RealPInvokeImplementationRefusesInCliAndSdkBeforeGuestOrNativeOutput()
    {
        using (var pe = new PEReader(File.OpenRead(fixture.NativeImportDllPath)))
        {
            var metadata = pe.GetMetadataReader();
            Assert.Equal(Machine.I386, pe.PEHeaders.CoffHeader.Machine);
            Assert.Equal(CorFlags.ILOnly, pe.PEHeaders.CorHeader!.Flags);
            Assert.Equal(1, metadata.GetTableRowCount(TableIndex.ModuleRef));
            Assert.Equal(1, metadata.GetTableRowCount(TableIndex.ImplMap));
        }
        string source = "@DotNetType(\"UnprovedNativeAssets.NativeProbe\") declare class NativeProbe { static Answer():number; } console.log(NativeProbe.Answer());";
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", source);
        var ordinary = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --ref-asm --verify -o ordinary.dll -r \"{fixture.NativeImportDllPath}\"", directory.Path);
        Assert.True(ordinary.ExitCode == 0, ordinary.StandardOutput + ordinary.StandardError);
        Assert.Contains("IL verification passed", ordinary.StandardOutput);
        foreach (string target in new[] {
            $"--emit-native-project native --runtime {NativeAotProjectOptions.HostRuntimeIdentifier}",
            $"--target native-aot -o native.exe --runtime {NativeAotProjectOptions.HostRuntimeIdentifier}" })
        {
            var result = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --msbuild-errors {target} -r \"{fixture.NativeImportDllPath}\"", directory.Path);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("SHARPTS402", result.StandardOutput + result.StandardError);
            Assert.Contains("unproved module, native, or resource assets", result.StandardOutput + result.StandardError);
        }
        Assert.False(Directory.Exists(directory.GetPath("native")));
        Assert.False(File.Exists(directory.GetPath("native.exe")));
        using var consumer = SdkNativePublishTests.CreateConsumer(source);
        var project = XDocument.Load(consumer.GetPath("Smoke.csproj"));
        project.Root!.Add(new XElement("ItemGroup", new XElement("Reference", new XAttribute("Include", "UnprovedNativeAssets"),
            new XElement("HintPath", fixture.NativeImportDllPath))));
        project.Save(consumer.GetPath("Smoke.csproj"));
        var rejected = SdkNativePublishTests.Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release");
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("SHARPTS402", rejected.StandardOutput + rejected.StandardError);
        Assert.Contains("unproved module, native, or resource assets", rejected.StandardOutput + rejected.StandardError);
        Assert.Empty(Directory.GetFiles(consumer.Path, "Guest.dll", SearchOption.AllDirectories));
    }

    private string[] ExtractChain(TempTestDirectory directory) => new[] { "TestPkg.Main", "TestPkg.Base" }.Select(name =>
    {
        string package = Directory.GetFiles(fixture.PackageSourceDir, "*.nupkg").Single(path => Path.GetFileName(path).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase));
        using var archive = ZipFile.OpenRead(package);
        var entry = archive.GetEntry("lib/net10.0/" + name + ".dll")!;
        string path = directory.GetPath(name + ".dll"); entry.ExtractToFile(path); return path;
    }).ToArray();
}
