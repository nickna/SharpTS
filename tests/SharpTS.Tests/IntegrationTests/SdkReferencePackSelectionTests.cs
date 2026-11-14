using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using SharpTS.Compilation;
using SharpTS.Cli;
using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

[Collection("ExternalAssembly")]
public sealed class SdkReferencePackSelectionTests(ExternalAssemblyFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildAndPublishUseTheSdkSelectedFrameworkBytes(bool native)
    {
        string source = $$"""
            @DotNetType("{{ExternalAssemblyFixture.GreeterTypeName}}")
            declare class Greeter { static Hello(name: string): string; }
            console.log(Greeter.Hello('SDK'));
            """;
        using var consumer = SdkNativePublishTests.CreateConsumer(source, native);
        string pack = consumer.GetPath("selected pack with spaces");
        Directory.CreateDirectory(pack);
        foreach (string path in Directory.GetFiles(SdkResolver.FindReferenceAssembliesPath()!, "*.dll"))
            File.Copy(path, Path.Combine(pack, Path.GetFileName(path)));

        // Same framework identity/API, different bytes: exercise patch drift even
        // on a CI machine with only one installed reference pack.
        string runtime = Path.Combine(pack, "System.Runtime.dll");
        byte[] image = File.ReadAllBytes(runtime);
        using (var pe = new PEReader(new MemoryStream(image)))
        {
            var metadata = pe.GetMetadataReader();
            byte[] mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToByteArray();
            int offset = image.AsSpan().IndexOf(mvid);
            Assert.True(offset >= 0);
            Assert.Equal(-1, image.AsSpan(offset + mvid.Length).IndexOf(mvid));
            Guid.NewGuid().TryWriteBytes(image.AsSpan(offset, mvid.Length));
        }
        File.WriteAllBytes(runtime, image);
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(image));

        var project = XDocument.Load(consumer.GetPath("Smoke.csproj"));
        project.Root!.Add(new XElement("ItemGroup", new XElement("Reference",
            new XAttribute("Include", ExternalAssemblyFixture.GreeterAssemblyName),
            new XElement("HintPath", fixture.GreeterDllPath))));
        project.Root.Add(new XElement("Target", new XAttribute("Name", "SelectTestFrameworkPack"),
            new XAttribute("AfterTargets", "ResolveTargetingPackAssets"),
            new XElement("ItemGroup",
                new XElement("_TestFramework", new XAttribute("Include", "@(Reference)"),
                    new XAttribute("Condition", "'%(Reference.FrameworkReferenceName)' == 'Microsoft.NETCore.App'")),
                new XElement("Reference", new XAttribute("Remove", "@(_TestFramework)")),
                new XElement("Reference", new XAttribute("Include", $"@(_TestFramework->'{pack}/%(Filename)%(Extension)')"),
                    new XElement("FrameworkReferenceName", "Microsoft.NETCore.App"),
                    new XElement("IsImplicitlyDefined", "true"),
                    new XElement("Private", "false")))));
        project.Save(consumer.GetPath("Smoke.csproj"));

        var build = SdkNativePublishTests.Dotnet(consumer.Path, "build", "Smoke.csproj", "-c", "Release");
        SdkNativePublishTests.AssertSuccess(build);
        string ridDirectory = native ? NativeAotProjectOptions.HostRuntimeIdentifier + "/" : "";
        var managed = SdkNativePublishTests.Dotnet(consumer.Path,
            consumer.GetPath($"bin/Release/net10.0/{ridDirectory}SdkNativeSmoke.dll"));
        SdkNativePublishTests.AssertSuccess(managed);
        Assert.Equal("Hello, SDK!\n", managed.StandardOutput.Replace("\r\n", "\n"));

        string publishedDirectory = consumer.GetPath("published");
        SdkNativePublishTests.AssertSuccess(SdkNativePublishTests.Dotnet(consumer.Path,
            "publish", "Smoke.csproj", "-c", "Release", "-o", publishedDirectory));
        if (native)
        {
            string[] manifests = Directory.GetFiles(consumer.Path, "native-project.json", SearchOption.AllDirectories);
            Assert.NotEmpty(manifests);
            foreach (string manifestPath in manifests)
            {
                using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                var capturedRuntime = Assert.Single(manifest.RootElement.GetProperty("frameworkReferences").EnumerateArray(),
                    reference => reference.GetProperty("name").GetString() == "System.Runtime");
                Assert.Equal(expectedHash, capturedRuntime.GetProperty("sha256").GetString());
            }
            string executable = Path.Combine(publishedDirectory, "SdkNativeSmoke" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            NativeAotPublishDriver.ValidateNativeImage(executable, NativeAotProjectOptions.HostRuntimeIdentifier!);
            var result = TestProcess.Run(new ProcessStartInfo(executable),
                TimeSpan.FromSeconds(30), "SDK-selected reference pack native execution");
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StandardError);
            Assert.Equal("Hello, SDK!\n", result.StandardOutput.Replace("\r\n", "\n"));
        }
        else
        {
            var published = SdkNativePublishTests.Dotnet(consumer.Path, Path.Combine(publishedDirectory, "SdkNativeSmoke.dll"));
            SdkNativePublishTests.AssertSuccess(published);
            Assert.Equal("Hello, SDK!\n", published.StandardOutput.Replace("\r\n", "\n"));
        }
    }
}
