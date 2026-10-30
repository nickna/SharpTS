using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using PEPacker;
using SharpTS.Cli;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeManagedReferenceClosureTests
{
    [Fact]
    public void CompleteExactGraphIsOwnedAndReplacementCannotChangeExportOrRevision()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string first = directory.GetPath("renamed.dll");
        string second = directory.GetPath("B.dll");
        byte[] original = Image("A", "B");
        File.WriteAllBytes(first, original); File.WriteAllBytes(second, Image("B"));
        var closure = NativeManagedReferenceClosure.Capture(directory.Path, [first, second], null);
        NativeGuestAssemblyClosure.ValidateImage(Image("Guest", "A"), closure);
        File.WriteAllBytes(first, Image("Changed"));
        var entries = closure.Export(directory.Path);
        Assert.Equal(original, File.ReadAllBytes(directory.GetPath("references/A.dll")));
        Assert.Equal(2, entries.Count);
        byte[] guest = Image("Guest");
        var revision = NativeGuestRevision.Compute(guest, null, entries);
        Assert.Equal(revision, NativeGuestRevision.Compute(guest, null, entries.Reverse().ToArray()));
        Assert.NotEqual(revision.RevisionSha256, NativeGuestRevision.Compute(guest, null,
            entries.Select(entry => entry.Name == "A" ? entry with { Sha256 = new string('0', 64) } : entry).ToArray()).RevisionSha256);
        using var loader = closure.CreateLoader();
        Assert.Empty(loader.MetadataFailures);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-version")]
    [InlineData("duplicate")]
    [InlineData("Guest")]
    [InlineData("sYsTeM.cOnSoLe")]
    [InlineData("SharpTS")]
    [InlineData("NativeApp")]
    [InlineData("reference")]
    [InlineData("net8")]
    [InlineData("fake-target")]
    [InlineData("x86")]
    [InlineData("module")]
    [InlineData("internalcall")]
    [InlineData("resource")]
    public void UnprovedInputsAreNamedRefusals(string kind)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string name = kind is "Guest" or "sYsTeM.cOnSoLe" or "SharpTS" or "NativeApp" ? kind : "A";
        byte[] image = Image(name, kind is "missing" or "wrong-version" ? "B" : null,
            referenceOnly: kind == "reference", target: kind == "net8" ? ".NETCoreApp,Version=v8.0" : ".NETCoreApp,Version=v10.0",
            flags: kind == "x86" ? CorFlags.ILOnly | CorFlags.Requires32Bit : CorFlags.ILOnly,
            module: kind == "module", resource: kind == "resource", fakeTargetAttribute: kind == "fake-target", internalCall: kind == "internalcall");
        directory.CreateFile("marker", "keep");
        File.WriteAllBytes(directory.GetPath("A.dll"), image);
        var paths = new List<string> { directory.GetPath("A.dll") };
        if (kind is "duplicate" or "wrong-version")
        {
            File.WriteAllBytes(directory.GetPath("other.dll"), Image(kind == "duplicate" ? "a" : "B", version: new Version(2, 0, 0, 0)));
            paths.Add(directory.GetPath("other.dll"));
        }
        var error = Assert.Throws<CompileException>(() => NativeManagedReferenceClosure.Capture(directory.Path, paths, null));
        Assert.Equal(DiagnosticCode.NativeGuestExternalReferences, error.Diagnostic.Code);
        Assert.Equal("keep", File.ReadAllText(directory.GetPath("marker")));
    }

    [Fact]
    public void PackagesRefuseBeforeRestoreAndCapturedFilesAreRecheckedBeforeCacheAdmission()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("sharpts.json", "{\"packages\":{\"Unproved.Package\":\"1.0.0\"}}");
        Assert.Equal(DiagnosticCode.NativeGuestExternalReferences, Assert.Throws<CompileException>(() =>
            NativeManagedReferenceClosure.Capture(directory.Path, [], null)).Diagnostic.Code);
        Assert.False(Directory.Exists(directory.GetPath("obj")));
        File.Delete(directory.GetPath("sharpts.json"));
        File.WriteAllBytes(directory.GetPath("A.dll"), Image("A"));
        var closure = NativeManagedReferenceClosure.Capture(directory.Path, [directory.GetPath("A.dll")], null);
        var entries = closure.Export(directory.Path);
        File.WriteAllText(directory.GetPath("native-project.json"), JsonSerializer.Serialize(new
        {
            managedReferences = entries.Select(entry => new { name = entry.Name, path = entry.Path, sha256 = entry.Sha256,
                sourcePath = entry.SourcePath, identity = entry.Identity, dependencies = entry.Dependencies })
        }));
        Assert.Single(NativeReferenceManifest.ReadAndVerify(directory.Path));
        File.WriteAllBytes(directory.GetPath("references/A.dll"), Image("A"));
        Assert.Throws<InvalidDataException>(() => NativeReferenceManifest.ReadAndVerify(directory.Path));
    }

    public static IEnumerable<object[]> DeviceNames() => new[]
    {
        "NUL", "nUl", "NUL.extra", "CON", "CON.foo", "PRN", "AUX", "com1", "LPT9", "COM\u00b9", "LPT\u00b2", "LPT\u00b3", "NUL .extra"
    }.Select(name => new object[] { name });

    [Theory, MemberData(nameof(DeviceNames))]
    public void DeviceAssemblyNamesRefuseAtCaptureWithoutExportOrDeviceAccess(string name)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string input = directory.GetPath("ordinary-renamed-input.dll");
        File.WriteAllBytes(input, Image(name));
        var error = Assert.Throws<CompileException>(() => NativeManagedReferenceClosure.Capture(directory.Path, [input], null));
        Assert.Equal(DiagnosticCode.NativeGuestExternalReferences, error.Diagnostic.Code);
        Assert.Contains("file name", error.Message);
        Assert.False(Directory.Exists(directory.GetPath("references")));
    }

    [Theory, MemberData(nameof(DeviceNames))]
    public void DeviceManifestNamesRefuseBeforeResolvingOrReadingReferencePaths(string name)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        File.WriteAllText(directory.GetPath("native-project.json"), JsonSerializer.Serialize(new
        {
            managedReferences = new[] { new { name, path = "references/" + name + ".dll", sha256 = new string('0', 64),
                sourcePath = "unused.dll", identity = name, dependencies = Array.Empty<string>() } }
        }));
        var error = Assert.Throws<InvalidDataException>(() => NativeReferenceManifest.ReadAndVerify(directory.Path));
        Assert.Contains("unsupported assembly file name", error.Message);
        Assert.False(Directory.Exists(directory.GetPath("references")));
    }

    [Theory]
    [InlineData("CONSOLE")]
    [InlineData("NULibrary")]
    [InlineData("COM10")]
    [InlineData("Vendor.CON")]
    [InlineData("Vendor.LPT1")]
    public void OrdinaryAssemblyNamesRemainSupported(string name) => Assert.True(NativeReferenceFileName.IsSupported(name));

    private static byte[] Image(string name, string? dependency = null, bool referenceOnly = false,
        string target = ".NETCoreApp,Version=v10.0", CorFlags flags = CorFlags.ILOnly,
        bool module = false, bool resource = false, Version? version = null, bool fakeTargetAttribute = false, bool internalCall = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var assembly = metadata.AddAssembly(metadata.GetOrAddString(name), version ?? new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        Assert.True(EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity("System.Runtime", out var framework));
        var runtime = metadata.AddAssemblyReference(metadata.GetOrAddString(framework.Name), framework.Version, default,
            metadata.GetOrAddBlob(framework.PublicKeyToken.ToArray()), 0, default);
        void Attribute(string ns, string attributeName, string? argument)
        {
            var scope = fakeTargetAttribute && attributeName == "TargetFrameworkAttribute" ?
                metadata.AddAssemblyReference(metadata.GetOrAddString("Unproved.Attributes"), new Version(1, 0, 0, 0), default, default, 0, default) : runtime;
            var type = metadata.AddTypeReference(scope, metadata.GetOrAddString(ns), metadata.GetOrAddString(attributeName));
            var ctor = metadata.AddMemberReference(type, metadata.GetOrAddString(".ctor"), metadata.GetOrAddBlob(
                argument is null ? new byte[] { 0x20, 0, 1 } : [0x20, 1, 1, 0x0e]));
            var value = new BlobBuilder(); value.WriteUInt16(1);
            if (argument is not null) value.WriteSerializedString(argument);
            value.WriteUInt16(0); metadata.AddCustomAttribute(assembly, ctor, metadata.GetOrAddBlob(value));
        }
        Attribute("System.Runtime.Versioning", "TargetFrameworkAttribute", target);
        if (referenceOnly) Attribute("System.Runtime.CompilerServices", "ReferenceAssemblyAttribute", null);
        if (dependency is not null) metadata.AddAssemblyReference(metadata.GetOrAddString(dependency), new Version(1, 0, 0, 0), default, default, 0, default);
        if (module) metadata.AddModuleReference(metadata.GetOrAddString("unproved-native"));
        if (resource) metadata.AddManifestResource(ManifestResourceAttributes.Public, metadata.GetOrAddString("unproved-resource"), default, 0);
        if (internalCall) metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.Runtime | MethodImplAttributes.InternalCall, metadata.GetOrAddString("UnprovedNativeBody"),
            metadata.GetOrAddBlob(new byte[] { 0, 0, 1 }), 0, MetadataTokens.ParameterHandle(1));
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: flags);
        var image = new BlobBuilder(); builder.Serialize(image); return image.ToArray();
    }
}
