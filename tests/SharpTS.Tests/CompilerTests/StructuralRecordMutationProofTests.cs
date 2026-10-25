using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StructuralRecordMutationProofTests
{
    [Theory]
    [InlineData("function write(box:{value:number}){box.value=4;}")]
    [InlineData("function write<T extends {value:number}>(box:T){box.value=4;}")]
    [InlineData("function write<T>(box:T&{value:number}){box.value=4;}")]
    [InlineData("interface View{value:number;} function write(box:View){box.value=4;}")]
    [InlineData("interface View<T>{value:T;} function write(box:View<number>){box.value=4;}")]
    [InlineData("interface Base<T>{value:T;} interface View<T> extends Base<T>{} function write(box:View<number>){box.value=4;}")]
    [InlineData("interface Base{value:number;} interface View<T> extends Base{extra?:T;} function write(box:View<number>){box.value=4;}")]
    [InlineData("interface View<T=number>{value:T;} function write(box:View<number>){box.value=4;}")]
    public void NarrowViewsInvalidateAllPossibleCarriersWhileUnrelatedShapesRemainStable(string declaration)
    {
        string source = declaration + "const mutable={value:1,label:'kept'};const stable={count:2,label:'safe'};write(mutable);const value=mutable.value;const count=stable.count;";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, types);
        var mutable = Assert.Single(features.CompactObjectRecordShapes, entry => entry.Value.Fields.Any(field => field.Key == "value"));
        var stable = Assert.Single(features.CompactObjectRecordShapes, entry => entry.Value.Fields.Any(field => field.Key == "count"));
        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(mutable.Key));
        Assert.True(features.CanAssumeCompactObjectRecordIsUnmaterialized(stable.Key));
    }

    [Theory]
    [InlineData("interface View<T>{payload:T;} function write(root:View<{value:number}>){root.payload.value=4;}")]
    [InlineData("interface Slot<T>{value:T;} interface View<T>{payload:Slot<T>;} function write(root:View<number>){root.payload.value=4;}")]
    public void GenericPayloadSubstitutionProtectsNestedCarriersWithoutDisablingUnrelatedShapes(string declaration)
    {
        string source = declaration + "const mutable={value:1,label:'kept'};const stable={count:2,label:'safe'};write({payload:mutable});const value=mutable.value;const count=stable.count;";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, types);
        var mutable = Assert.Single(features.CompactObjectRecordShapes, entry => entry.Value.Fields.Any(field => field.Key == "value"));
        var stable = Assert.Single(features.CompactObjectRecordShapes, entry => entry.Value.Fields.Any(field => field.Key == "count"));
        Assert.False(features.PotentiallyMaterializesUnknownCompactObjectRecordShape);
        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(mutable.Key));
        Assert.True(features.CanAssumeCompactObjectRecordIsUnmaterialized(stable.Key));
    }

    [Fact]
    public void IndexOnlyGenericViewsUseTheConservativeFallback()
    {
        const string source = "interface View<T>{[key:string]:T;} function write(box:View<number>){box.value=4;}const mutable={value:1,label:2};const stable={count:2,label:3};write(mutable);console.log(mutable.value,stable.count);";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, types);
        Assert.True(features.PotentiallyMaterializesUnknownCompactObjectRecordShape);
        Assert.All(features.CompactObjectRecordShapes, entry =>
            Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(entry.Key)));
    }
}
