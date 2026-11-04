using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedSourceFieldLifecycleRuntimeTests
{
    [Fact]
    public void ReusedEmitterKeepsSourceContractsInTheirSelectedOutput()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: false,
            emitNativeCallables: false, emitNativeOwnProperties: true);
        var contracts = new HashSet<Type>();
        foreach (bool classes in new[] { true, false, true })
        {
            var assembly = new PersistedAssemblyBuilder(
                new AssemblyName($"source_fields_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("main");
            var source = classes ? "class Item {} Object.defineProperty({}, 'x', {value: 1});" :
                "Object.defineProperty({}, 'x', {value: 1});";
            var features = new RuntimeFeatureDetector().Detect(
                new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(module, features);
            if (classes)
            {
                var contract = Assert.IsType<EmittedSourceDataDefinitionRuntime>(runtime.ObjectFields.SourceDataDefinition);
                Assert.Same(module, contract.Contract.Module);
                Assert.True(contracts.Add(contract.Contract));
            }
            else Assert.Null(runtime.ObjectFields.SourceDataDefinition);
            using var bytes = new MemoryStream();
            assembly.Save(bytes);
            bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var loaded = Assembly.Load(bytes.ToArray());
            Assert.Equal(classes, loaded.GetType("<>SourceDataDefinition") is not null);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), reference =>
                reference.Name == "SharpTS" || reference.Name!.StartsWith("source_fields_", StringComparison.Ordinal));
            var value = new Dictionary<string, object?>();
            var rt = loaded.GetType("$Runtime")!;
            rt.GetMethod("ObjectDefineProperty")!.Invoke(null,
                [value, "x", new Dictionary<string, object?> { ["value"] = 7d, ["configurable"] = true }]);
            Assert.Equal(7d, rt.GetMethod("GetProperty")!.Invoke(null, [value, "x"]));
            Assert.Equal(true, rt.GetMethod("DeleteProperty")!.Invoke(null, [value, "x"]));
        }
    }
}
