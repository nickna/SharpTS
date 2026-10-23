using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ObjectDefinitionRuntimeFeatureGatingTests
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("const value = { slot: 1 }; console.log(value.slot);", false)]
    [InlineData(ObjectDefinitionRuntimeReachabilityTests.LiteralAccessorSource, false)]
    [InlineData("const O = Object;", true)]
    [InlineData("const R = Reflect;", true)]
    [InlineData("const define = Object.defineProperty;", true)]
    [InlineData("const { defineProperties: define } = Object;", true)]
    [InlineData("const { constructor: { defineProperty: define } } = {} as any;", true)]
    [InlineData("const key = 'slot'; const value: any = {}; value[key];", true)]
    [InlineData("const value = ({}).constructor;", true)]
    [InlineData("const root = globalThis;", true)]
    [InlineData("const root = global;", true)]
    [InlineData("this.Object;", true)]
    [InlineData("const evaluate = eval;", true)]
    [InlineData("Function('return Object');", true)]
    [InlineData("Object.create(null, {});", true)]
    [InlineData("const value: any = {}; value.__defineGetter__('slot', () => 1);", true)]
    public void DefinitionBodySelectionConservativelyTracksReachability(string source, bool expected)
    {
        Assert.Equal(expected, Detect(source).UsesObjectDefinitionRuntime);
    }

    [Theory]
    [InlineData("const O = Object;")]
    [InlineData("const key = 'slot'; const value: any = {}; value[key];")]
    public void DefinitionReachabilityDoesNotBroadenDescriptorMutationOptimizations(string source)
    {
        var features = Detect(source);
        Assert.True(features.UsesObjectDefinitionRuntime);
        Assert.False(features.UsesDynamicPropertyDescriptors);
    }

    [Fact]
    public void ConservativeFeatureSetsRetainObjectDefinitionBodies()
    {
        Assert.True(new RuntimeFeatureSet().UsesObjectDefinitionRuntime);
        Assert.True(RuntimeFeatureSet.EmitEverything().UsesObjectDefinitionRuntime);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("hosted")]
    [InlineData("late-commonjs")]
    public void ConservativeEmissionBoundariesRetainWorkingDefinitionBodies(string boundary)
    {
        var features = boundary == "default" ? RuntimeFeatureSet.EmitEverything() : Detect("console.log(1);");
        if (boundary != "default") Assert.False(features.UsesObjectDefinitionRuntime);
        // Module classification can enable CommonJS after the detector has run.
        if (boundary == "late-commonjs") features.UsesCjsRequire = true;
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName($"object_definition_gate_{Guid.NewGuid():N}"), typeof(object).Assembly);
        new RuntimeEmitter(TypeProvider.Runtime, emitHosted: boundary == "hosted")
            .EmitAll(builder.DefineDynamicModule("main"), features);
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var assembly = Assembly.Load(bytes.ToArray());
        var runtime = assembly.GetType("$Runtime", throwOnError: true)!;
        var target = new Dictionary<string, object?>();
        Assert.Same(target, runtime.GetMethod("ObjectDefineProperty")!.Invoke(null,
            [target, "first", new Dictionary<string, object?> { ["value"] = 7d }]));
        Assert.Same(target, runtime.GetMethod("ObjectDefineProperties")!.Invoke(null,
            [target, new Dictionary<string, object?>
            {
                ["second"] = new Dictionary<string, object?> { ["value"] = 9d }
            }]));
        Assert.Equal(7d, target["first"]);
        Assert.Equal(9d, target["second"]);
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(
        new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}
