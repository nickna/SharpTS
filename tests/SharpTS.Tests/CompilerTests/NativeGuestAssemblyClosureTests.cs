using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using PEPacker;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeGuestAssemblyClosureTests
{
    [Fact]
    public void ExactFrameworkAndPrivateRuntimeTypeIdentitiesAreAllowed()
    {
        Assert.True(EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity("System.Runtime", out var framework));
        NativeGuestAssemblyClosure.ValidateImage(Image(framework.Name, framework.Version,
            framework.CultureName, framework.PublicKeyToken.ToArray(), framework.Flags & ~AssemblyFlags.PublicKey));
        var core = typeof(object).Assembly.GetName();
        NativeGuestAssemblyClosure.ValidateImage(Image(core.Name!, core.Version!, core.CultureName ?? "", core.GetPublicKeyToken()!, 0));
        NativeGuestAssemblyClosure.ValidateImage(Image(core.Name!, core.Version!, core.CultureName ?? "", core.GetPublicKey()!, AssemblyFlags.PublicKey));
        var regex = typeof(System.Text.RegularExpressions.Regex).Assembly.GetName();
        NativeGuestAssemblyClosure.ValidateImage(Image(regex.Name!, regex.Version!, regex.CultureName ?? "", regex.GetPublicKey()!, AssemblyFlags.PublicKey));
        var uri = typeof(Uri).Assembly.GetName();
        NativeGuestAssemblyClosure.ValidateImage(Image(uri.Name!, uri.Version!, uri.CultureName ?? "", uri.GetPublicKeyToken()!, 0));
    }

    [Theory]
    [InlineData("ZstdSharp", false)]
    [InlineData("System.UnprovedLibrary", false)]
    [InlineData("System.Runtime", true)]
    public void NamesAndAnUnprovedDependencyAreNotAFrameworkClosure(string name, bool frameworkName)
    {
        byte[] token = [];
        if (frameworkName)
        {
            Assert.True(EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(name, out var framework));
            token = framework.PublicKeyToken.ToArray();
        }
        var error = Assert.Throws<CompileException>(() => NativeGuestAssemblyClosure.ValidateImage(Image(name, new Version(99, 0, 0, 0), "", token, 0)));
        Assert.Equal(DiagnosticCode.NativeGuestExternalReferences, error.Diagnostic.Code);
        Assert.Contains(name, error.Message);
        Assert.Contains("--target dll", error.Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FrameworkTokensAndCultureMustMatchTheActualPublishedIdentity(bool wrongToken, bool wrongCulture)
    {
        Assert.True(EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity("System.Runtime", out var framework));
        byte[] token = framework.PublicKeyToken.ToArray();
        if (wrongToken) token[0] ^= 0xff;
        Assert.Throws<CompileException>(() => NativeGuestAssemblyClosure.ValidateImage(Image(framework.Name,
            framework.Version, wrongCulture ? "unproved-culture" : framework.CultureName, token, 0)));
    }

    private static byte[] Image(string name, Version version, string culture, byte[] token, AssemblyFlags flags)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("closure-test"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("closure-test"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddAssemblyReference(metadata.GetOrAddString(name), version, metadata.GetOrAddString(culture),
            metadata.GetOrAddBlob(token), flags, default);
        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder(); builder.Serialize(image); return image.ToArray();
    }
}
