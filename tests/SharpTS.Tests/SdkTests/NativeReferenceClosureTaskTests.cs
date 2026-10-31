using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Build.Utilities;
using SharpTS.Sdk.Tasks;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.SdkTests;

public sealed class NativeReferenceClosureTaskTests
{
    [Theory]
    [InlineData("exact")]
    [InlineData("changed-implementation")]
    [InlineData("omitted")]
    [InlineData("foreign-framework-claim")]
    [InlineData("changed-framework")]
    [InlineData("empty-resolution")]
    public void FinalResolutionRequiresEveryCapturedIdentityAndExactBytes(string control)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        Directory.CreateDirectory(directory.GetPath("references"));
        string captured = directory.GetPath("references/Vendor.dll");
        string selected = directory.GetPath("Selected.dll");
        string guest = directory.GetPath("Guest.dll");
        string framework = directory.GetPath("System.Runtime.dll");
        byte[] original = Image("Vendor");
        File.WriteAllBytes(captured, original); File.WriteAllBytes(guest, Image("Guest"));
        File.WriteAllBytes(framework, Image("System.Runtime"));
        File.WriteAllBytes(selected, control == "changed-implementation" ? Image("Vendor") : original);
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(directory.GetPath("native-project.json"), JsonSerializer.Serialize(new
        {
            guestSha256 = Hash(guest),
            managedReferences = new[] { new { name = "Vendor", path = "references/Vendor.dll", sha256 = Hash(captured), sourcePath = selected, identity = "Vendor, Version=1.0.0.0", dependencies = Array.Empty<string>() } },
            frameworkReferences = new[] { new { name = "System.Runtime", sha256 = Hash(framework) } }
        }));
        var resolved = new List<Microsoft.Build.Framework.ITaskItem> { new TaskItem(guest), new TaskItem(framework) };
        if (control != "omitted") resolved.Add(new TaskItem(selected));
        if (control == "changed-framework") File.WriteAllBytes(framework, Image("System.Runtime"));
        if (control == "foreign-framework-claim")
        {
            string foreign = directory.GetPath("Foreign.dll"); File.WriteAllBytes(foreign, Image("Foreign"));
            var item = new TaskItem(foreign); item.SetMetadata("FrameworkReferenceName", "Microsoft.NETCore.App"); resolved.Add(item);
        }
        var engine = new MockBuildEngine();
        var task = new NativeReferenceClosureTask { ProjectDirectory = directory.Path, ResolvedReferences = control == "empty-resolution" ? null : resolved.ToArray(), ValidateResolution = true, BuildEngine = engine };
        Assert.Equal(control == "exact", task.Execute());
        if (control == "exact")
        {
            Assert.Empty(engine.Errors);
            var reference = Assert.Single(task.CapturedReferences);
            Assert.Equal(Path.GetFullPath(captured), reference.GetMetadata("HintPath"));
        }
        else Assert.NotEmpty(engine.Errors);
    }

    private static byte[] Image(string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder(); builder.Serialize(image); return image.ToArray();
    }
}
