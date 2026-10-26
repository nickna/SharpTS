using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NamedOpaqueBindingProofTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedOpaqueBindingDoesNotContaminateASameNamedStableShadow(bool modules)
    {
        const string source = "function write(root:{payload:any}){const view=root.payload as {value:number;missing:number};view.value=4;}function safe(){const envelope={count:2,label:'safe'};return envelope.count;}const box={value:1,label:'kept'};const envelope:{payload:any}={payload:box};write(envelope);console.log(box.value,safe());";
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        RuntimeFeatureSet features;
        if (!modules)
        {
            var statements = TestHarness.ParseOrThrow(source);
            features = new RuntimeFeatureDetector().Detect(statements, checker.Check(statements));
        }
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "opaque-binding-proof-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            var types = checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            features = new RuntimeFeatureDetector().Detect(entry.Statements, types);
        }
        var changed = Assert.Single(features.CompactObjectRecordShapes, shape => shape.Value.Fields.Any(field => field.Key == "value"));
        var stable = Assert.Single(features.CompactObjectRecordShapes, shape => shape.Value.Fields.Any(field => field.Key == "count"));
        Assert.False(features.PotentiallyMaterializesUnknownCompactObjectRecordShape);
        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(changed.Key));
        Assert.True(features.CanAssumeCompactObjectRecordIsUnmaterialized(stable.Key));
    }
}
