using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstantiationExpressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "class Box<T>{constructor(public value:T){}}const C=Box<number>;console.log(new C(7).value);", "7\n"];
        yield return ["identity-and-prototypes", "class Box<T>{constructor(public value:T){}}const C=Box<number>,S=Box<string>;console.log(C===Box,S===Box,C.prototype===Box.prototype,S.prototype===Box.prototype,new C(7).constructor===Box,new S('s').constructor===Box);", "true true true true true true\n"];
        yield return ["inherited-constructor", "class Box<T>{constructor(public value:T){}}class Derived<T> extends Box<T>{}const C=Derived<number>;console.log(new C(7).value,C===Derived,C.prototype===Derived.prototype);", "7 true true\n"];
        yield return ["constraints-and-defaults", "class Box<T extends {id:number}={id:number}>{constructor(public value:T){}}const C=Box<{id:number}>;console.log(new C({id:7}).value.id,C===Box);", "7 true\n"];
        yield return ["shadowed-class-binding", "class Box<T>{constructor(public value:T){}}const Outer=Box<number>;{class Box<T>{constructor(public value:T){}read(){return this.value;}}const Inner=Box<string>;console.log(new Inner('s').read(),Inner===Box);}console.log(new Outer(7).value,Outer===Box);", "s true\n7 true\n"];
        yield return ["class-expression", "const Box=class<T>{constructor(public value:T){}};const C=Box<number>;console.log(new C(7).value,C===Box,C.prototype===Box.prototype);", "7 true true\n"];
        yield return ["static-members", "class Box<T>{static label='box';static read(){return this.label;}constructor(public value:T){}}const C=Box<number>;console.log(C.label,C.read(),new C(7).value);", "box box 7\n"];
        yield return ["parenthesized-construction", "class Box<T>{constructor(public value:T){}}console.log(new (Box<number>)(7).value,(Box<number>)===Box);", "7 true\n"];
        yield return ["constructor-alias-operand", "class Box<T>{constructor(public value:T){}}const Alias=Box;const C=Alias<number>;console.log(new C(7).value,C===Alias,C===Box);", "7 true true\n"];
        yield return ["dependent-default", "class Pair<T,U=T>{constructor(public first:T,public second:U){}}const C=Pair<number>;console.log(new C(7,8).first,new C(7,8).second,C===Pair);", "7 8 true\n"];
        yield return ["outer-generic-argument", "class Box<T extends {id:number}>{constructor(public value:T){}}function make<T extends {id:number}>(value:T){const C=Box<T>;return new C(value);}console.log(make({id:7}).value.id);", "7\n"];
        yield return ["constructor-signature-assignment", "class Box<T>{constructor(public value:T){}}const C=Box<number>;const Make:new(value:number)=>Box<number>=C;console.log(new Make(7).value,Make===C);", "7 true\n"];
        yield return ["constructor-with-static-members", "class Box<T>{static label='box';constructor(public value:T){}}const C=Box<number>;const Make:{new(value:number):Box<number>;label:string;prototype:unknown}=C;console.log(new Make(7).value,Make.label,Make===C);", "7 box true\n"];
        yield return ["inherited-constructor-signature", "class Box<T>{constructor(public value:T){}}class Derived<T> extends Box<T>{}const C=Derived<number>;const Make:new(value:number)=>Box<number>=C;console.log(new Make(7).value,Make===Derived);", "7 true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SpecializationKeepsTheOriginalConstructorValue(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(GenericInstantiationDiagnosticTests.CollectStrictModules(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ImportedSpecializedAliasesKeepTheirCheckedAndRuntimeIdentities(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = "export class Box<T>{constructor(public value:T){}}export const Numeric=Box<number>;",
            ["main.ts"] = "import {Box as Imported,Numeric} from './lib';const Strings=Imported<string>;console.log(new Numeric(7).value,new Strings('s').value,Numeric===Imported,Strings===Imported,Numeric.prototype===Imported.prototype);"
        };
        if (mode == ExecutionMode.Compiled)
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("7 s true true true\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
