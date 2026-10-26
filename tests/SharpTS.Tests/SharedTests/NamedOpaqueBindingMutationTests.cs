using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamedOpaqueBindingMutationTests
{
    public static IEnumerable<object[]> Sources()
    {
        foreach (var (declaration, binding) in new[]
        {
            ("", "const envelope:{payload:any}={payload:box};"),
            ("interface Envelope{payload:any;}", "const envelope:Envelope={payload:box};"),
            ("interface Envelope<T>{payload:T;}", "const envelope:Envelope<any>={payload:box};"),
            ("class Envelope{payload:any;}", "const envelope:Envelope={payload:box};"),
            ("class Envelope<T>{payload:T;constructor(payload:T){this.payload=payload;}}", "const envelope:Envelope<any>={payload:box};"),
            ("type Envelope<T>={payload:T};", "const envelope:Envelope<any>={payload:box};"),
            ("interface Base<T>{payload:T;}interface Envelope<T> extends Base<T>{}", "const envelope:Envelope<any>={payload:box};"),
            ("", "let envelope:{payload:any}={payload:null};envelope={payload:box};"),
            ("", "let envelope:{payload:any}|null=null;envelope??={payload:box};"),
        })
            yield return [declaration + "function write(root:{payload:any}){const view=root.payload as {value:number;missing:number};view.value=4;}const box={value:1,label:'kept'};" + binding + "write(envelope);console.log(box.value,box.label);"];
    }

    public static IEnumerable<object[]> RuntimeCases() => Sources().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.SelectMany(mode =>
            new[] { false, true }.Select(modules => new object[] { row[0], mode, modules })));

    [Theory, MemberData(nameof(RuntimeCases))]
    public void NamedOpaqueAnnotationsProtectTheirActualInitializerCarriers(string source, ExecutionMode mode, bool modules)
        => Assert.Equal("4 kept\n", modules
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode)
            : TestHarness.Run(source, mode));

    public static IEnumerable<object[]> IlCases() => Sources().SelectMany(row =>
        new[] { false, true }.Select(modules => new object[] { row[0], modules }));

    [Theory, MemberData(nameof(IlCases))]
    public void NamedOpaqueInitializerProgramsHaveVerifiedIl(string source, bool modules)
        => Assert.Empty(modules
            ? TestHarness.CompileModulesAndVerifyOnly(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts")
            : TestHarness.CompileAndVerifyOnly(source));
}
