using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using SharpTS.Compilation;
using SharpTS.Sdk.Tasks;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class SdkNativePublishTests
{
    private const string AssemblyName = "SdkNativeSmoke";
    private static string HostRid => NativeAotProjectOptions.HostRuntimeIdentifier!;

    [Fact]
    public void OrdinaryManagedSdkBuildAndPublishKeepDirectGuestOutput()
    {
        using var consumer = CreateConsumer("console.log(42);", native: false);
        AssertSuccess(Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release"));
        string assembly = consumer.GetPath($"bin/Release/net10.0/{AssemblyName}.dll");
        Assert.Equal("42\n", Dotnet(consumer.Path, assembly).StandardOutput.Replace("\r\n", "\n"));
        Assert.False(Directory.Exists(consumer.GetPath("obj/Release/net10.0/sharpts-native")));
        string publish = consumer.GetPath("managed publish");
        AssertSuccess(Dotnet(consumer.Path, "publish", "Smoke.csproj", "-c", "Release", "--no-restore", "-o", publish));
        Assert.True(File.Exists(Path.Combine(publish, $"{AssemblyName}.runtimeconfig.json")));
        Assert.Equal("42\n", Dotnet(consumer.Path, Path.Combine(publish, $"{AssemblyName}.dll")).StandardOutput.Replace("\r\n", "\n"));
        Assert.False(File.Exists(Path.Combine(publish, "Guest.dll")));
    }

    [Theory]
    [InlineData("SharpTSTarget", true)]
    [InlineData("PublishAot", false)]
    public void NativeBuildUsesExportedWrapperAndGuestAndPreservesPreviousAttempts(string implicitProperty, bool debugSymbols)
    {
        using var consumer = CreateConsumer("console.log(42);");
        string projectPath = consumer.GetPath("Smoke.csproj");
        var project = XDocument.Load(projectPath);
        project.Descendants(implicitProperty).Single().Remove();
        project.Descendants("DebugSymbols").Single().Value = debugSymbols ? "true" : "false";
        project.Descendants("DebugType").Single().Value = debugSymbols ? "portable" : "none";
        project.Save(projectPath);
        AssertSuccess(Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release"));
        string nativeRoot = consumer.GetPath($"obj/Release/net10.0/{HostRid}/sharpts-native");
        string first = Assert.Single(Directory.GetDirectories(nativeRoot));
        Assert.True(File.Exists(Path.Combine(first, "Program.cs")));
        Assert.True(File.Exists(Path.Combine(first, "NativeApp.csproj")));
        Assert.Equal(debugSymbols, File.Exists(Path.Combine(first, "guest", "Guest.pdb")));
        string originalGuest = Path.Combine(first, "guest", "Guest.dll");
        byte[] originalBytes = File.ReadAllBytes(originalGuest);
        File.WriteAllText(Path.Combine(first, "user-note.txt"), "keep this inspection note");
        string managedWrapper = consumer.GetPath($"bin/Release/net10.0/{HostRid}/{AssemblyName}.dll");
        Assert.Equal("42\n", Dotnet(consumer.Path, managedWrapper).StandardOutput.Replace("\r\n", "\n"));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(managedWrapper)!, "Guest.dll")));
        using (var stream = File.OpenRead(managedWrapper))
        using (var pe = new PEReader(stream))
            Assert.NotNull(pe.PEHeaders.CorHeader); // Build is managed; Publish owns native linking.

        AssertSuccess(Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release", "--no-restore"));
        Assert.Equal(2, Directory.GetDirectories(nativeRoot).Length);
        Assert.Equal(originalBytes, File.ReadAllBytes(originalGuest));
        Assert.Equal("keep this inspection note", File.ReadAllText(Path.Combine(first, "user-note.txt")));
        AssertSuccess(Dotnet(consumer.Path, "clean", "Smoke.csproj", "-c", "Release"));
        Assert.False(File.Exists(managedWrapper));
        Assert.True(File.Exists(originalGuest));
        Assert.Equal("keep this inspection note", File.ReadAllText(Path.Combine(first, "user-note.txt")));
    }

    [Fact]
    public void NativePublishProducesStrictExecutableWithArgumentsExitCodeAndSymbols()
    {
        using var consumer = CreateConsumer("console.log(42);console.log(process.argv.slice(2).join('|'));process.exitCode=7;");
        string publish = consumer.GetPath("native publish");
        var result = Dotnet(consumer.Path, "publish", "Smoke.csproj", "-c", "Release", "-r", HostRid, "-o", publish);
        AssertSuccess(result);
        Assert.DoesNotContain(": warning ", result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        string executable = Path.Combine(publish, AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(executable));
        Assert.Empty(Directory.GetFiles(publish, "*.dll"));
        Assert.Empty(Directory.GetFiles(publish, "*.runtimeconfig.json"));
        if (OperatingSystem.IsWindows())
        {
            using var stream = File.OpenRead(executable);
            using var pe = new PEReader(stream);
            Assert.Null(pe.PEHeaders.CorHeader);
            Assert.False(pe.HasMetadata);
            Assert.True(File.Exists(Path.Combine(publish, AssemblyName + ".pdb")));
        }
        else if (OperatingSystem.IsLinux())
            Assert.True(File.Exists(Path.Combine(publish, AssemblyName + ".dbg")));
        else if (OperatingSystem.IsMacOS())
            Assert.True(Directory.Exists(Path.Combine(publish, AssemblyName + ".dSYM")));

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = publish, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("first");
        start.ArgumentList.Add("two words");
        start.Environment["PATH"] = "";
        foreach (string key in start.Environment.Keys.Where(key => key.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        var run = TestProcess.Run(start, TimeSpan.FromSeconds(30), "SDK native guest without dotnet discovery");
        Assert.Equal(7, run.ExitCode);
        Assert.Equal("42\nfirst|two words\n", run.StandardOutput.Replace("\r\n", "\n"));
        Assert.Empty(run.StandardError);
    }

    [Theory]
    [InlineData("Reference", "Unsupported.Library")]
    [InlineData("ProjectReference", "Other.csproj")]
    [InlineData("PackageReference", "Unsupported.Native.Closure")]
    public void UnsupportedReferencesFailWithTheirKindAndNameBeforeGuestExport(string kind, string name)
    {
        var item = new XElement(kind, new XAttribute("Include", name));
        if (kind == "PackageReference") item.Add(new XAttribute("Version", "1.0.0"));
        using var consumer = CreateConsumer("console.log(42);", item: item);
        if (kind == "ProjectReference")
            consumer.CreateFile(name, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var result = Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release");
        Assert.NotEqual(0, result.ExitCode);
        string diagnostics = result.StandardOutput + result.StandardError;
        Assert.Contains("SHARPTS402", diagnostics);
        Assert.Contains(kind, diagnostics);
        Assert.Contains(name, diagnostics);
        Assert.Empty(Directory.GetFiles(consumer.Path, "native-project.json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, "emitDeclarationOnly")]
    [InlineData(true, "emitDeclarationOnly")]
    public void EffectiveNoOutputConfigRefusesBeforeExport(bool inherited, string flag)
    {
        using var consumer = CreateConsumer("export const answer=42;");
        string config = "{\"compilerOptions\":{\"" + flag + "\":true,\"declaration\":true}}";
        if (inherited)
        {
            consumer.CreateFile("base.json", config);
            consumer.CreateFile("tsconfig.json", "{\"extends\":\"./base.json\"}");
        }
        else consumer.CreateFile("tsconfig.json", config);
        var result = Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(flag, result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(consumer.Path, "native-project.json", SearchOption.AllDirectories));
        Assert.False(File.Exists(consumer.GetPath("main.d.ts")));
    }

    [Theory]
    [InlineData("RuntimeIdentifier", "", "explicit RuntimeIdentifier")]
    [InlineData("SharpTSGcProfile", "adaptive", "workstation")]
    [InlineData("IlcTreatWarningsAsErrors", "false", "strict trim/AOT")]
    [InlineData("NoWarn", "IL2075", "strict trim/AOT")]
    [InlineData("NoWarn", "2075", "strict trim/AOT")]
    [InlineData("NoWarn", "IL2121", "strict trim/AOT")]
    [InlineData("NoWarn", "2121", "strict trim/AOT")]
    [InlineData("WarningsNotAsErrors", "IL2075", "strict trim/AOT")]
    [InlineData("AssemblyName", "Guest", "reserved")]
    [InlineData("AssemblyName", "gUeSt", "reserved")]
    [InlineData("SharpTSOutputFileName", "Other.dll", "managed DLL output only")]
    [InlineData("SharpTSOutputPath", "custom-output/", "managed DLL output only")]
    public void InvalidNativeProfileRefusesBeforeExport(string property, string value, string diagnostic)
    {
        using var consumer = CreateConsumer("console.log(42);");
        var result = Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release", $"-p:{property}={value}");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SHARPTSSDK001", result.StandardOutput + result.StandardError);
        Assert.Contains(diagnostic, result.StandardOutput + result.StandardError);
        Assert.Empty(Directory.GetFiles(consumer.Path, "native-project.json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("System.Console", "build")]
    [InlineData("sYsTeM.cOnSoLe", "publish")]
    public void FrameworkAssemblyNamesRefuseBeforeWrapperCompilationAndNativeLinking(string name, string command)
    {
        using var consumer = CreateConsumer("console.log(42);");
        var result = Dotnet(consumer.Path, command, "Smoke.csproj", "-c", "Release", $"-p:AssemblyName={name}");
        Assert.NotEqual(0, result.ExitCode);
        string diagnostics = result.StandardOutput + result.StandardError;
        Assert.Contains("SHARPTSSDK001", diagnostics);
        Assert.Contains("shadows a resolved native guest reference", diagnostics);
        Assert.Contains(name, diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Generating native code", diagnostics);
        Assert.False(File.Exists(consumer.GetPath($"bin/Release/net10.0/{HostRid}/{name}.dll")));
    }

    internal static TempTestDirectory CreateConsumer(string source, bool native = true, XElement? item = null)
    {
        var consumer = CliTestHelper.CreateTempDirectory();
        string root = RepositoryRoot();
        string sdk = Path.Combine(root, "src", "SharpTS.Sdk", "Sdk");
        var properties = new XElement("PropertyGroup",
            new XElement("TargetFramework", "net10.0"), new XElement("AssemblyName", AssemblyName),
            new XElement("SharpTSEntryPoint", "main.ts"), new XElement("SharpTSVerifyIL", "true"),
            new XElement("SharpTSCompilerExe", Path.Combine(AppContext.BaseDirectory, "SharpTS.dll")),
            new XElement("SharpTSTasksAssembly", typeof(ReadTsConfigTask).Assembly.Location));
        if (native)
            properties.Add(new XElement("SharpTSTarget", "native-aot"), new XElement("PublishAot", "true"),
                new XElement("RuntimeIdentifier", HostRid), new XElement("DebugSymbols", "true"), new XElement("DebugType", "portable"));
        var project = new XElement("Project", new XElement("Import", new XAttribute("Project", Path.Combine(sdk, "Sdk.props"))), properties);
        if (item is not null) project.Add(new XElement("ItemGroup", item));
        project.Add(new XElement("Import", new XAttribute("Project", Path.Combine(sdk, "Sdk.targets"))));
        consumer.CreateFile("Smoke.csproj", project.ToString());
        consumer.CreateFile("main.ts", source);
        foreach (string policy in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            consumer.CreateFile(policy, "<Project />");
        File.Copy(Path.Combine(root, "global.json"), consumer.GetPath("global.json"));
        return consumer;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "SharpTS.Sdk", "Sdk", "Sdk.targets"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the SDK source for integration tests.");
    }

    internal static TestProcessResult Dotnet(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (arguments[0] is "build" or "publish" or "clean")
        {
            start.ArgumentList.Add("-m:2");
            start.ArgumentList.Add("-nr:false");
            start.ArgumentList.Add("-p:UseSharedCompilation=false");
        }
        return TestProcess.Run(start, TimeSpan.FromMinutes(5), "SharpTS.Sdk build/publish integration");
    }

    internal static void AssertSuccess(TestProcessResult result) =>
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
}
