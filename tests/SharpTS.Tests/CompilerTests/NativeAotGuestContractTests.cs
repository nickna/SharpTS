using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class NativeAotGuestContractTests
{
    [Fact]
    public void DirectExporterRefusesOtherHostRidsBeforeSavingTheGuest()
    {
        string destination = Path.Combine(Path.GetTempPath(), $"sharpts_native_mismatch_{Guid.NewGuid():N}");
        var compatibility = NativeAotCompatibilityReport.Analyze(SharpTSRuntimeRequirements.None, [], false);
        foreach (string rid in NativeAotProjectOptions.SupportedRuntimeIdentifiers.Where(rid => rid != NativeAotProjectOptions.HostRuntimeIdentifier))
        {
            bool saved = false;
            var error = Assert.Throws<CompileException>(() => NativeAotProjectExporter.Export(
                new(destination, rid), compatibility, _ => saved = true));
            Assert.Equal(DiagnosticCode.NativeGuestTargetMismatch, error.Diagnostic.Code);
            Assert.False(saved);
            Assert.False(Directory.Exists(destination));
        }
    }

    [Theory]
    [InlineData(SharpTSRuntimeRequirements.RuntimeAssembly)]
    [InlineData(SharpTSRuntimeRequirements.FullDependencyClosure)]
    [InlineData(SharpTSRuntimeRequirements.ManagedCompilerHost)]
    public void EveryRuntimeDependencyIsAnExplicitNativeRefusal(SharpTSRuntimeRequirements requirement)
    {
        var report = NativeAotCompatibilityReport.Analyze(requirement, ["vm", "Proxy", "vm"], false);
        Assert.False(report.IsCompatible);
        Assert.Equal(new[] { "Proxy", "vm" }, report.FeatureReasons);
        Assert.Equal(DiagnosticCode.NativeGuestRuntimeDependency, Assert.Single(report.Diagnostics).Code);
    }

    [Fact]
    public void ThirdPartyReferencesAreSeparateFromStandaloneCapability()
    {
        Assert.True(NativeAotCompatibilityReport.Analyze(SharpTSRuntimeRequirements.None, [], false).IsCompatible);
        var report = NativeAotCompatibilityReport.Analyze(SharpTSRuntimeRequirements.None, [], true);
        Assert.False(report.IsCompatible);
        Assert.Equal(DiagnosticCode.NativeGuestExternalReferences, Assert.Single(report.Diagnostics).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleFileFacadeIsDirectVerifiableAndOptIn(bool enabled)
    {
        var statements = new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"native_facade_{Guid.NewGuid():N}", false, true, null, null, null, OutputTarget.Dll);
        if (enabled) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        byte[] assembly = compiler.SaveToBytes();
        using var verificationStream = new MemoryStream(assembly);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(verificationStream));
        using var pe = new PEReader(new MemoryStream(assembly));
        var reader = pe.GetMetadataReader();
        var facades = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Where(type =>
            reader.GetString(type.Namespace) == "SharpTS.Generated" && reader.GetString(type.Name) == "EntryPoint").ToArray();
        if (!enabled) { Assert.Empty(facades); return; }
        var facade = Assert.Single(facades);
        var run = facade.GetMethods().Select(reader.GetMethodDefinition)
            .Single(method => reader.GetString(method.Name) == "Run");
        Assert.Equal("Run", reader.GetString(run.Name));
        Assert.True(run.Attributes.HasFlag(MethodAttributes.Public) && run.Attributes.HasFlag(MethodAttributes.Static));
        var signature = reader.GetBlobReader(run.Signature);
        signature.ReadSignatureHeader();
        Assert.Equal(0, signature.ReadCompressedInteger());
        Assert.Equal(SignatureTypeCode.Int32, signature.ReadSignatureTypeCode());
        Assert.Equal(0x28, pe.GetMethodBody(run.RelativeVirtualAddress).GetILBytes()![0]); // direct call, never reflection
        Assert.DoesNotContain(reader.AssemblyReferences.Select(reader.GetAssemblyReference), reference =>
            reader.GetString(reference.Name) == "SharpTS");
    }
}
