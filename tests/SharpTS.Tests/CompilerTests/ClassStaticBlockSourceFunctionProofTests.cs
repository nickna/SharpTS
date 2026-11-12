using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassStaticBlockSourceFunctionProofTests
{
    [Theory]
    [InlineData("class Holder")]
    [InlineData("const Holder = class")]
    [InlineData("const Holder = class Owner")]
    public void StaticBlockWritesInvalidateTheSourceFunctionEscapeProof(string declaration)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            type Item = { count: number; values: number[] };
            type Update = { extra: number; count: number };
            function touch(target: Item, update: Update): void {}
            const item: Item = { count: 1, values: [1] };
            {{declaration}} {
                static {
                    touch = Object.assign;
                    touch(item, { extra: 2, count: 9 });
                }
            };
            """);
        var typeMap = new TypeChecker(TypeCheckerOptions.Strict).Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);
        var itemShape = Assert.Single(features.CompactObjectRecordShapes, entry =>
            entry.Value.Fields.Any(field => field.Key == "values"));

        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(itemShape.Key));
    }
}
