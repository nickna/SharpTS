using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EnumValueBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "computed", """
            enum Flags {One=1,Two=1<<1,All=One|Two}let key:number=Flags.All;console.log(Flags[key]);
            """, "All\n" };
        yield return new object[] { "computed_caught", """
            enum Flags {One=1,Two=1<<1,All=One|Two}let key:number=Flags.All;try{console.log(Flags[key]);}catch(e){console.log("computed-miss");}console.log(Flags.One,Flags.Two,Flags.All);
            """, "All\n1 2 3\n" };
        yield return new object[] { "alias", """
            enum Values {First=2,Second=3}const alias:any=Values;let key:number=3;console.log(alias[key],alias.First,alias===Values);
            """, "Second 2 true\n" };
        yield return new object[] { "alias_caught", """
            enum Values {First=2,Second=3}try{const alias:any=Values;let key:number=3;console.log(alias[key],alias.First,alias===Values);}catch(e){console.log("alias-failed");}console.log(Values.First,Values.Second);
            """, "Second 2 true\n2 3\n" };
        yield return new object[] { "generator", """
            enum Values {First=2,Second=3}function* names(key:number){yield Values[key];}console.log(names(3).next().value);
            """, "Second\n" };
        yield return new object[] { "generator_caught", """
            enum Values {First=2,Second=3}function* names(key:number){yield Values[key];}try{console.log(names(3).next().value);}catch(e){console.log("generator-failed");}
            """, "Second\n" };
        yield return new object[] { "generator_local", """
            enum Values {First=2,Second=3}function* names(){let key:number=3;yield Values[key];}console.log(names().next().value);
            """, "Second\n" };
        yield return new object[] { "negative_caught", """
            enum Values {Negative=-2,Half=0.5}let key:number=-2;try{console.log(Values[key]);}catch(e){console.log("negative-miss");}key=0.5;try{console.log(Values[key]);}catch(e){console.log("fraction-miss");}console.log(Values.Negative,Values.Half);
            """, "Negative\nHalf\n-2 0.5\n" };
        yield return new object[] { "runtime-initializer-order", """
            let calls=0;function next():number{calls++;console.log("next",calls);return calls+2;}enum E{A=next(),B=next(),C=A|B}const alias:any=E;console.log(E.A,E.B,E.C,calls,alias===E);console.log(alias[3],alias[4],alias[7]);
            """, "next 1\nnext 2\n3 4 7 2 true\nA B C\n" };
        yield return new object[] { "namespace-alias-identity", """
            namespace N{export enum E{A=1,B=A<<1,C=A|B}export const original=E;}const ns:any=N;const alias:any=ns.E;console.log(alias===ns.original,alias===N.E,alias[3],N.E.C);
            """, "true true C 3\n" };
        yield return new object[] { "generator-alias-across-yields", """
            enum E{A=2,B=3}function* names(){const alias:any=E;yield alias[2];yield alias===E;yield E[3];}const g=names();console.log(g.next().value,g.next().value,g.next().value);
            """, "A true B\n" };
        yield return new object[] { "string-and-duplicate-objects", """
            enum Labels{A="a",B="b"}enum Numeric{A=2,B=2,C=3}const text:any=Labels;const numeric:any=Numeric;console.log(text===Labels,text.A,text.a===undefined,numeric===Numeric,numeric[2],numeric.C);
            """, "true a true true B 3\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> ModuleCases()
    {
        yield return new object[] { "module-computed-alias", """
            import {Flags,alias} from './lib.ts';import * as lib from './lib.ts';const value:any=Flags;console.log(value===alias,value===lib.Flags,value[3],Flags.All);
            """, """
            export enum Flags{One=1,Two=One<<1,All=One|Two}export const alias=Flags;
            """, "true true All 3\n" };
        yield return new object[] { "module-generator", """
            import {names,Values} from './lib.ts';const alias:any=Values;const g=names();console.log(g.next().value,g.next().value,alias[3]);
            """, """
            export enum Values{First=2,Second=3}export function* names(){const alias:any=Values;yield alias[2];yield alias===Values;}
            """, "First true Second\n" };
    }
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledEnumValuesPreserveMappingsAndIdentity(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(ModuleCases))]
    public void CompiledModulesShareEnumValues(string name, string main, string library, string expected)
    {
        Assert.Equal(expected, TestHarness.RunModules(new() { ["main.ts"] = main, ["lib.ts"] = library }, "main.ts", ExecutionMode.Compiled));
    }
}
