using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DefaultReexport3221Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["default-local-alias", "let value=1;export {value as default};export function increment(){value++;}", "", "import current,{increment} from './producer';console.log(current);increment();console.log(current);"];
        yield return ["named-default-import", "let value=1;export {value as default};export function increment(){value++;}", "", "import {default as current,increment} from './producer';console.log(current);increment();console.log(current);"];
        yield return ["default-reexport", "let value=1;export {value as default};export function increment(){value++;}", "export {default,increment} from './producer';", "import current,{increment} from './barrel';console.log(current);increment();console.log(current);"];
        yield return ["named-to-default-reexport", "export let value=1;export function increment(){value++;}", "export {value as default,increment} from './producer';", "import current,{increment} from './barrel';console.log(current);increment();console.log(current);"];
        yield return ["default-import-reexport-alias", "let value=1;export {value as default};export function increment(){value++;}", "import current,{increment} from './producer';export {current as live,increment};", "import {live,increment} from './barrel';console.log(live);increment();console.log(live);"];
        yield return ["mutable-default-function-alias", "let read=()=>1;export {read as default};export function replace(){read=()=>2;}", "", "import read,{replace} from './producer';console.log(read());replace();console.log(read());"];
        yield return ["default-and-named-share-binding", "let value=1;export {value as default,value as current};export function increment(){value++;}", "", "import initial,{current,increment} from './producer';console.log(initial,current);increment();console.log(initial,current);"];
        yield return ["default-expression-remains-snapshot", "let value=1;export default value;export {value};export function increment(){value++;}", "", "import initial,{value,increment} from './producer';console.log(initial,value);increment();console.log(initial,value);"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PreservesDefaultExportTypeAcrossTheActualSource(string name, string producer, string barrel, string consumer)
    {
        Assert.NotEmpty(name);
        Assert.DoesNotContain(Check(producer, barrel, consumer), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OrdinaryNamedExportDoesNotCreateADefault()
    {
        Assert.Contains(Check("export const value=1;", "export {value} from './producer';", "import current from './barrel';console.log(current);"),
            diagnostic => diagnostic.TsCode == "TS1192");
    }

    [Theory]
    [InlineData("export const value=1;", "export {value as default} from './producer';", "import value from './barrel';const text:string=value;")]
    [InlineData("export default 1;", "export {default as value} from './producer';", "import {value} from './barrel';const text:string=value;")]
    public void PreservesTheRealSourceType(string producer, string barrel, string consumer)
    {
        Assert.Contains(Check(producer, barrel, consumer), diagnostic => diagnostic.TsCode == "TS2322");
    }

    private static IReadOnlyList<Diagnostic> Check(string producer, string barrel, string consumer)
    {
        string directory=Path.Combine(Path.GetTempPath(), $"sharpts_default_reexport_{Guid.NewGuid():N}");
        string entry=Path.Combine(directory,"main.ts");
        var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            [entry]=consumer,
            [Path.Combine(directory,"producer.ts")]=producer,
        };
        if(barrel.Length!=0) files[Path.Combine(directory,"barrel.ts")]=barrel;
        var resolver=new ModuleResolver(entry,files);
        var module=resolver.LoadModule(entry);
        var checker=new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(module),resolver);
        return checker.GetDiagnostics();
    }
}
