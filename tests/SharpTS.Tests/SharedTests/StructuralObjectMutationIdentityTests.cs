using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StructuralObjectMutationIdentityTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (name, declaration) in new[]
        {
            ("ordinary", "function write(box:{value:number})"),
            ("constrained", "function write<T extends {value:number}>(box:T)"),
            ("intersection", "function write<T>(box:T&{value:number})"),
            ("interface", "interface Base{value:number;} interface View extends Base{} function write(box:View)"),
            ("generic-interface", "interface View<T>{value:T;} function write(box:View<number>)"),
            ("generic-interface-base", "interface Base<T>{value:T;} interface View<T> extends Base<T>{} function write(box:View<number>)"),
            ("generic-interface-ordinary-base", "interface Base{value:number;} interface View<T> extends Base{extra?:T;} function write(box:View<number>)"),
            ("generic-interface-explicit-default-argument", "interface View<T=number>{value:T;} function write(box:View<number>)"),
            ("optional", "interface View{value?:number;} function write(box:View)"),
        })
            yield return [name, declaration + "{box.value=4;return box;} const box={value:1,label:'kept'};const returned=write(box);console.log(returned===box);console.log(returned.value,box.value,box.label);", "true\n4 4 kept\n"];
        yield return ["reordered", "function write(box:{value:number}){box.value=4;} const box={label:'kept',value:1};write(box);console.log(box.value,box.label);", "4 kept\n"];
        yield return ["local-alias", "const box={value:1,label:'kept'};const view:{value:number}=box;view.value=4;console.log(view===box,view.value,box.value);", "true 4 4\n"];
        yield return ["asserted-extra-member", "function write(box:{value:number;missing:number}){box.value=4;}const box={value:1};write(box as {value:number;missing:number});console.log(box.value);", "4\n"];
        yield return ["readonly-view", "const box={value:1,label:'kept'} as const;const view:{value:number}=box;view.value=4;console.log(box.value,box.label);", "4 kept\n"];
        yield return ["bracket", "function write(box:{value:number}){box['value']=4;} const box={value:1,label:'kept'};write(box);console.log(box.value);", "4\n"];
        yield return ["compound", "function write(box:{value:number}){box.value+=3;} const box={value:1,label:'kept'};write(box);console.log(box.value);", "4\n"];
        yield return ["nested", "function write(root:{child:{value:number}}){root.child.value=4;} const box={child:{value:1,label:'kept'},outer:true};write(box);console.log(box.child.value,box.child.label,box.outer);", "4 kept true\n"];
        yield return ["generic-interface-payload", "interface View<T>{payload:T;} function write(root:View<{value:number}>){root.payload.value=4;} const box={value:1,label:'kept'};write({payload:box});console.log(box.value,box.label);", "4 kept\n"];
        yield return ["nested-generic-interface-payload", "interface Slot<T>{value:T;} interface View<T>{payload:Slot<T>;} function write(root:View<number>){root.payload.value=4;} const box={value:1,label:'kept'};write({payload:box});console.log(box.value,box.label);", "4 kept\n"];
        yield return ["array-element", "function write(items:{value:number}[]){items[0].value=4;} const box={value:1,label:'kept'};write([box]);console.log(box.value);", "4\n"];
        yield return ["arrow-control", "const write=(box:{value:number})=>{box.value=4;};const box={value:1,label:'kept'};write(box);console.log(box.value);", "4\n"];
    }

    public static IEnumerable<object[]> ParityCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void StructuralViewsPublishMutationsOnTheOriginalObject(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void StructuralMutationProgramsHaveVerifiedIl(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(expected);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void StructuralMutationRemainsVisibleInStandaloneOutput()
    {
        const string source = "function write(box:{value:number}){box.value=4;return box;} const box={value:1,label:'kept'};const result=write(box);console.log(result===box,result.value,box.value);";
        Assert.Equal("true 4 4\n", TestHarness.RunCompiledStandalone(source));
    }
}
