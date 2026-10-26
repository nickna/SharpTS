using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class OpaqueStructuralBoundaryProofTests
{
    [Theory]
    [InlineData("function write(root:{payload:any}){const view=root.payload as {value:number;missing:number};view.value=4;}", "write({payload:mutable});")]
    [InlineData("interface Envelope<T>{payload:T;}function write(root:Envelope<any>){const view=root.payload as {value:number;missing:number};view.value=4;}", "write({payload:mutable});")]
    [InlineData("class Envelope{payload:any;}function write(root:Envelope){const view=root.payload as {value:number;missing:number};view.value=4;}", "write({payload:mutable});")]
    [InlineData("class Base{payload:any;}class Envelope extends Base{}function write(root:Envelope){const view=root.payload as {value:number;missing:number};view.value=4;}", "write({payload:mutable});")]
    [InlineData("class Envelope<T>{payload:T;constructor(payload:T){this.payload=payload;}}function write(root:Envelope<any>){const view=root.payload as {value:number;missing:number};view.value=4;}", "write({payload:mutable});")]
    [InlineData("function write(items:[any]){const view=items[0] as {value:number;missing:number};view.value=4;}", "write([mutable]);")]
    public void ActualNestedArgumentShapesLoseTheirProofWithoutDisablingUnrelatedCarriers(string declaration, string call)
    {
        string source = declaration + "const mutable={value:1,label:'kept'};const stable={count:2,label:'safe'};" + call + "const value=mutable.value;const count=stable.count;";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, types);
        var mutable = Assert.Single(features.CompactObjectRecordShapes, entry => entry.Value.Fields.Any(field => field.Key == "value"));
        var stable = Assert.Single(features.CompactObjectRecordShapes, entry => entry.Value.Fields.Any(field => field.Key == "count"));
        Assert.False(features.PotentiallyMaterializesUnknownCompactObjectRecordShape);
        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(mutable.Key));
        Assert.True(features.CanAssumeCompactObjectRecordIsUnmaterialized(stable.Key));
    }
}
