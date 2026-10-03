using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EnumMissingPropertyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "missing", """
            enum Values {First=1}let key:number=99;try{console.log(Values[key],typeof Values[key]);}catch(e){console.log("threw");}
            """, "undefined undefined\n" };
        yield return new object[] { "direct-valid-and-missing", """
            enum E{A=1,B=2}console.log(E[1],E[99],typeof E[99],E[99]===undefined);
            """, "A undefined undefined true\n" };
        yield return new object[] { "dynamic-valid-and-missing", """
            enum E{A=-2,B=0.5}let key:any=-2;console.log(E[key]);key=99;console.log(E[key],typeof E[key]);key="0.5";console.log(E[key]);
            """, "A\nundefined undefined\nB\n" };
        yield return new object[] { "alias-valid-and-missing", """
            enum E{A=1,B=2}const alias:any=E;console.log(alias[2],alias[99],typeof alias[99],alias[99]===undefined,alias===E);
            """, "B undefined undefined true true\n" };
        yield return new object[] { "namespace-valid-and-missing", """
            namespace N{export enum E{A=1,B=2}}const ns:any=N;console.log(N.E[1],N.E[99],typeof N.E[99],ns.E[2],ns.E[99]===undefined);
            """, "A undefined undefined B true\n" };
        yield return new object[] { "nonfinite-and-string-misses", """
            enum E{A=0,B=2}const alias:any=E;console.log(alias[NaN]===undefined,alias[Infinity]===undefined,alias["absent"]===undefined,alias["2"],alias.A);
            """, "true true true B 0\n" };
    }

    public static IEnumerable<object[]> ModuleCases()
    {
        yield return new object[] { "named-module", """
            import {E,alias} from './lib.ts';let key:number=99;console.log(E[1],E[key],typeof E[key],alias[2],alias[key]===undefined,alias===E);
            """, """
            export enum E{A=1,B=2}export const alias:any=E;
            """, "A undefined undefined B true true\n" };
        yield return new object[] { "namespace-module", """
            import * as lib from './lib.ts';const alias:any=lib.E;console.log(lib.E[2],lib.E[99],typeof lib.E[99],alias[1],alias[99]===undefined,alias===lib.E);
            """, """
            export enum E{A=1,B=2}
            """, "B undefined undefined A true true\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledEnumMissingPropertiesReturnUndefined(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(ModuleCases))]
    public void CompiledModuleEnumMissingPropertiesReturnUndefined(string name, string main, string library, string expected)
    {
        var actual = TestHarness.RunModules(new() { ["main.ts"] = main, ["lib.ts"] = library }, "main.ts", ExecutionMode.Compiled);
        Assert.True(expected == actual, $"{name}: Expected [{expected}], received [{actual}]");
    }
}
