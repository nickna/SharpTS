using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

internal static class NativeUnprovedSourceMetadataFixture
{
    internal static (Assembly Saved, NativeMethodLookupCatalog Methods, NativeHasInMemberLookup Members,
        NativeSourceOwnerCatalog Sources) Build()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("unproved_source_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var types = TypeProvider.Runtime;
        var sources = new NativeSourceOwnerCatalog(module, types);
        var properties = new NativePropertyLookupCatalog(module, types);
        var methods = new NativeMethodLookupCatalog(module, types);
        var members = new NativeHasInMemberLookup(module, types);
        var runtime = new RuntimeEmitter(types, false, true, false, nativeHasInMemberLookup: members.HasMember).EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow()),
            FunctionEmissionPolicy.Full, sourceOwners: sources, nativePropertyGetter: properties.FindGetter,
            nativeMethodLookup: methods.Handles);
        // An actual created output type whose required source interface emission
        // was missed must keep every dependent query on its whole legacy body.
        var missed = module.DefineType("UnregisteredSource", TypeAttributes.Public);
        missed.DefineDefaultConstructor(MethodAttributes.Public); missed.CreateType();
        var parameters = new ClassGenericParameterRegistry(); parameters.CompleteEmission([]);
        sources.CompleteEmission(parameters, [missed]);
        Assert.NotEmpty(sources.Blockers);
        properties.CompleteEmission(sources, runtime);
        methods.CompleteEmission(properties, sources, runtime.Errors, runtime.FunctionValues.Type);
        members.CompleteEmission(methods, sources, runtime);
        using var stream = new MemoryStream(); assembly.Save(stream);
        byte[] bytes = AssemblyReferenceRewriteCompatibility.Rewrite(stream.ToArray(), PEPacker.EmbeddedReferenceAssemblyIndex.Default,
            PEPacker.ReferencePolicy.DroppingReferences("SharpTS"));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return (Assembly.Load(bytes), methods, members, sources);
    }
}
