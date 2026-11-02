using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayConstructorBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["local-constructor-original", "function run(){class Uint8Array{value=7}const a:Uint8Array=new Uint8Array();console.log(a.value)}run();", "7\n"];
        yield return ["signed-local-constructor", "function run(){class Int8Array{constructor(public value:string){}}const a:Int8Array=new Int8Array(\"x\");console.log(a.value)}run();", "x\n"];
        yield return ["bigint-family-local-constructor", "function run(){class BigInt64Array{value=9}const a:BigInt64Array=new BigInt64Array();console.log(a.value)}run();", "9\n"];
        yield return ["generic-local-constructor", "function run(){class Uint16Array<T>{constructor(public value:T){}}const a:Uint16Array<string>=new Uint16Array<string>(\"x\");console.log(a.value)}run();", "x\n"];
        yield return ["constructor-parameter", "function make(Uint8Array:new()=>{value:number}){console.log(new Uint8Array().value)}class Box{value=9}make(Box);", "9\n"];
        yield return ["class-expression-binding", "function run(){const Uint8Array=class{value=9};const a=new Uint8Array();console.log(a.value)}run();", "9\n"];
        yield return ["source-constructor-four-arguments", "function run(){class Uint8Array{value:number;constructor(a:number,b:number,c:number,d:number){this.value=a+b+c+d}}console.log(new Uint8Array(1,2,3,4).value)}run();", "10\n"];
        yield return ["source-constructor-second-string", "function run(){class Float64Array{value:string;constructor(a:string,b:string){this.value=a+b}}console.log(new Float64Array(\"a\",\"b\").value)}run();", "ab\n"];
        yield return ["native-global-remains-intrinsic", "const a=new Uint8Array([7]);function run(){class Uint8Array{value=9}console.log(new Uint8Array().value)}run();console.log(a[0]);", "9\n7\n"];
        yield return ["captured-local-constructor", "function run(){class Uint8Array{value=9}const make=()=>new Uint8Array();console.log(make().value)}run();", "9\n"];
        yield return ["derived-source-constructor", "function run(){class Parent{value=7}class Uint8Array extends Parent{}const a:Uint8Array=new Uint8Array();console.log(a.value)}run();", "7\n"];
        yield return ["ordinary-constructor-control", "class Box{value=7}const a:Box=new Box();console.log(a.value);", "7\n"];
        yield return ["lexical-any-nonconstructor-runtime", "function run(){const Uint8Array:any=7;try{new Uint8Array()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ActualConstructorBindingSelectsTheResult(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> Rejections()
    {
        yield return ["source-argument-type", "function run(){class Uint8Array{constructor(value:string){}}new Uint8Array(7)}"];
        yield return ["source-required-argument", "function run(){class Uint8Array{constructor(value:string){}}new Uint8Array()}"];
        yield return ["source-generic-constraint", "function run(){class Uint8Array<T extends number>{constructor(value:T){}}new Uint8Array<string>(\"x\")}"];
        yield return ["nonconstructor-binding", "function run(){const Uint8Array=7;new Uint8Array()}"];
        yield return ["native-arity-retained", "new Uint8Array(1,2,3,4);"];
        yield return ["native-offset-type-retained", "new Uint8Array(new ArrayBuffer(4),\"wrong\");"];
    }

    [Theory, MemberData(nameof(Rejections))]
    public void ActualConstructorSignaturesRemainChecked(string name, string source)
    {
        Assert.NotEmpty(name);
        foreach (bool noLib in new[] { false, true })
        {
            string path = Path.GetFullPath(Path.Combine("typed-array-constructor-bindings", "main.ts"));
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                TypeScriptProgramOptions.Default with { NoLib = noLib, Lib = noLib ? [] : ["lib.es2022.d.ts", "lib.dom.d.ts"] });
            var entry = resolver.LoadProgram(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            var typeMap = checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            if (name == "nonconstructor-binding")
            {
                // Project policy defers non-class constructability to runtime;
                // the selected local value must never acquire an intrinsic result.
                Assert.Empty(checker.GetDiagnostics());
                var reads = new ConstructionReads();
                foreach (var statement in entry.Statements) reads.Visit(statement);
                Assert.IsType<TypeInfo.Any>(typeMap.Get(Assert.Single(reads.Expressions)));
            }
            else Assert.NotEmpty(checker.GetDiagnostics());
        }
    }
    private sealed class ConstructionReads : AstVisitorBase
    {
        internal List<Expr.New> Expressions { get; } = [];
        protected override void VisitNew(Expr.New expression)
        {
            Expressions.Add(expression);
            base.VisitNew(expression);
        }
    }
}
