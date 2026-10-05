using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedClassDefinitionRuntimeTests
{
    [Fact]
    public void RequiredHandlesCannotBeReadOrCompletedBeforeDeclaration()
    {
        var owner = new EmittedRuntime().ClassDefinitions;
        Assert.Throws<InvalidOperationException>(() => owner.Create);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        Assert.NotSame(owner, new EmittedRuntime().ClassDefinitions);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ClassDefinitions))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterFinalizesFreshDefinitionOwnersAndVerifiableBodies(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedClassDefinitionRuntime>();
        foreach (string source in new[] { "const n = 1;", "async function f() { await Promise.resolve(1); } f();" })
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName($"definitions_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("main");
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(module, features);
            var owner = runtime.ClassDefinitions;
            Assert.True(owner.IsComplete);
            Assert.True(owners.Add(owner));
            Assert.Same(module, owner.Create.Module);
            Assert.Same(owner.Type, owner.Create.DeclaringType);
            Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
            using var bytes = new MemoryStream();
            assembly.Save(bytes);
            bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var loaded = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(reference => reference.Name == "SharpTS.Hosting.Abstractions"));
            Assert.NotNull(loaded.GetType("$ClassDefinition"));
        }
    }
}
