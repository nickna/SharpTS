using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OpaqueStructuralMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        const string view = "const view=root.payload as {value:number;missing:number};view.value=4;";
        foreach (string declaration in new[]
        {
            "function write(root:{payload:any})",
            "function write(root:{payload:unknown})",
            "interface Envelope{payload:any;}function write(root:Envelope)",
            "interface Envelope<T>{payload:T;}function write(root:Envelope<any>)",
            "function write(root:Readonly<{payload:any}>)"
        })
            yield return [declaration + "{" + view + "}const box={value:1,label:'kept'};write({payload:box});console.log(box.value,box.label);"];
        yield return ["function write(root:{payload:{child:any}}){const view=root.payload.child as {value:number;missing:number};view.value=4;}const box={value:1,label:'kept'};write({payload:{child:box}});console.log(box.value,box.label);"];
        yield return ["function write(items:any[]){const view=items[0] as {value:number;missing:number};view.value=4;}const box={value:1,label:'kept'};write([box]);console.log(box.value,box.label);"];
        yield return ["function write(items:[any]){const view=items[0] as {value:number;missing:number};view.value=4;}const box={value:1,label:'kept'};write([box]);console.log(box.value,box.label);"];
    }

    public static IEnumerable<object[]> ParityCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[0], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void OpaqueNestedViewsPublishWritesOnTheOriginalCarrier(string source, ExecutionMode mode)
        => Assert.Equal("4 kept\n", TestHarness.Run(source, mode));

    [Theory, MemberData(nameof(Cases))]
    public void OpaqueBoundaryProgramsHaveValidIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

    [Fact]
    public void AssertedOpaqueViewPreservesIdentityInStandaloneOutput()
    {
        const string source = "function write(root:{payload:any}){const view=root.payload as {value:number;missing:number};view.value=4;return view;}const box={value:1,label:'kept'};const result=write({payload:box});console.log(result===box,result.value,box.value,box.label);";
        Assert.Equal("true 4 4 kept\n", TestHarness.RunCompiledStandalone(source));
    }
}
