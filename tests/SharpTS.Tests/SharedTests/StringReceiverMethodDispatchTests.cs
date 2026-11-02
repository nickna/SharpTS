using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StringReceiverMethodDispatchTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["asserted-error-string-includes", "const value:any=new Error(\"clone\");try{console.log(\"returned\",(value as string).includes(\"clone\"))}catch(e:any){console.log(\"threw\",e instanceof TypeError)}", "threw true\n"];
        yield return ["own-method-receiver", "const value:any={tag:7,includes(this:any,s:string){return this.tag===7&&s===\"q\"}};console.log((value as string).includes(\"q\"));", "true\n"];
        yield return ["annotated-parameter-error", "function use(value:string){try{console.log(\"returned\",value.includes(\"clone\"))}catch(e:any){console.log(\"threw\",e instanceof TypeError)}}const value:any=new Error(\"clone\");use(value);", "threw true\n"];
        yield return ["annotated-local-error", "const value:any=new Error(\"clone\");const text:string=value;try{console.log(\"returned\",text.includes(\"clone\"))}catch(e:any){console.log(\"threw\",e instanceof TypeError)}", "threw true\n"];
        yield return ["annotated-return-error", "function value():string{return new Error(\"clone\") as any}try{console.log(\"returned\",value().includes(\"clone\"))}catch(e:any){console.log(\"threw\",e instanceof TypeError)}", "threw true\n"];
        yield return ["getter-before-argument-and-this", "let log=\"\";const value:any={get includes(){log+=\"g\";return function(this:any,s:string){return this===value&&s===\"x\"}}};function arg(){log+=\"a\";return \"x\"}console.log((value as string).includes(arg()),log);", "true ga\n"];
        yield return ["getter-abrupt-before-argument", "let count=0;const marker={};const value:any={get includes(){throw marker}};function arg(){count++;return \"x\"}try{(value as string).includes(arg())}catch(e){console.log(e===marker,count)}", "true 0\n"];
        yield return ["missing-method-evaluates-arguments", "let count=0;const value:any={};function arg(){count++;return \"x\"}try{(value as string).includes(arg())}catch(e:any){console.log(e instanceof TypeError,count)}", "true 1\n"];
        yield return ["noncallable-method-evaluates-arguments", "let count=0;const value:any={includes:7};function arg(){count++;return \"x\"}try{(value as string).includes(arg())}catch(e:any){console.log(e instanceof TypeError,count)}", "true 1\n"];
        yield return ["nullish-property-fails-before-argument", "let count=0;function arg(){count++;return \"x\"}for(const value of [null,undefined] as any[]){try{(value as string).includes(arg())}catch(e:any){console.log(e instanceof TypeError,count)}}", "true 0\ntrue 0\n"];
        yield return ["missing-method-does-not-coerce-receiver", "let count=0;const value:any={toString(){count++;return \"clone\"}};try{(value as string).includes(\"clone\")}catch(e:any){console.log(e instanceof TypeError,count)}", "true 0\n"];
        yield return ["boxed-string-control", "const value:any=new String(\"abc\");console.log((value as string).includes(\"b\"));", "true\n"];
        yield return ["boxed-string-own-method-wins", "const value:any=new String(\"abc\");value.includes=function(this:any,s:string){return this===value&&s===\"q\"};console.log((value as string).includes(\"q\"));", "true\n"];
        yield return ["primitive-string-hot-control", "const text=\"banana\";console.log(text.includes(\"a\",2),text.indexOf(\"na\",1),text.slice(1,3),text.substring(1,3));", "true 2 an an\n"];
        yield return ["explicit-string-coercion-control", "const value:any=new Error(\"clone\");console.log(String(value).includes(\"clone\"));", "true\n"];
        yield return ["borrowed-string-method-remains-generic", "const value:any=new Error(\"clone\");console.log(String.prototype.includes.call(value,\"clone\"));", "true\n"];
        yield return ["other-string-method-missing", "const value:any=new Error(\"clone\");try{console.log((value as string).charAt(0))}catch(e:any){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["own-trim-method-wins", "const value:any={trim(){return \"custom\"}};console.log((value as string).trim());", "custom\n"];
        yield return ["own-replace-method-wins", "const value:any={replace(a:any,b:any){return \"custom\"}};console.log((value as string).replace(\"x\",\"y\"));", "custom\n"];
        yield return ["async-argument-retains-method-and-receiver", "let log=\"\";const value:any={get includes(){log+=\"g\";return function(this:any,s:string){return this===value&&s===\"x\"}}};async function arg(){log+=\"a\";return \"x\"}async function run(){console.log((value as string).includes(await arg()),log)}run();", "true ga\n"];
        yield return ["optional-nullish-control", "const value:any=null;console.log((value as string|null)?.includes(\"x\")===undefined);", "true\n"];
        yield return ["primitive-prototype-method-live-control", "const prior=String.prototype.includes;try{String.prototype.includes=function(s:string){return s===\"q\"};console.log(\"abc\".includes(\"q\"))}finally{String.prototype.includes=prior}", "true\n"];
        yield return ["local-contract-method-result-identity", "const raw:any={includes(){return 7}};const text:string=raw;function run(){return text.includes(\"q\")}console.log(run());", "7\n"];
        yield return ["forwarded-call-local-method-result", "function value():string{return {includes(){return 7}} as any}const text=value();function run(){return text.includes(\"q\")}console.log(run());", "7\n"];
        yield return ["local-contract-bigint-method-result", "const raw:any={includes(){return 3n}};const text:string=raw;const value:any=text.includes(\"q\");console.log(typeof value,String(value+1n));", "bigint 4\n"];
        yield return ["boxed-default-argument-positions", "const value:any=new String(\"abc\");console.log((value as string).includes(\"a\",1),(value as string).indexOf(\"b\",2));", "false -1\n"];
        yield return ["unsafe-string-receiver-live-prototype", "const value:any=\"abc\";const text:string=value;const prior=String.prototype.includes;try{String.prototype.includes=function(s:string){return s===\"q\"};console.log(text.includes(\"q\"),(value as string).includes(\"q\"))}finally{String.prototype.includes=prior}", "true true\n"];
        yield return ["aliased-any-charcodeat-entry", "function readString(input:string,index:number):number{let code:number=input.charCodeAt(index);return code}function readAny(input:any):number{let code:number=input.charCodeAt(0);return code}const value:any={charCodeAt:readString};try{console.log(\"returned\",readAny(value))}catch(e:any){console.log(\"threw\",e instanceof TypeError)}", "threw true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ActualReceiverSelectsTheMethod(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public void KnownStringReturnsRetainTheirExistingClrSignatures()
    {
        const string source = "function literalText():string{return \"abc\"}function knownText(value:string):string{return value}function forwardedText():string{return knownText(\"abc\")}console.log(literalText().includes(\"b\"),knownText(\"abc\").includes(\"b\"),forwardedText().includes(\"b\"));";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"string_receiver_return_abi_{Guid.NewGuid():N}");
        compiler.Compile(statements, map, new DeadCodeAnalyzer(map).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var methods = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        foreach (var name in new[] { "literalText", "knownText", "forwardedText" })
            Assert.Equal(typeof(object), methods.Single(method => method.Name.EndsWith(name, StringComparison.Ordinal)).ReturnType);
        var known = methods.Single(method => method.Name.EndsWith("knownText", StringComparison.Ordinal));
        Assert.Equal(typeof(string), Assert.Single(known.GetParameters()).ParameterType);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("true true true\n", output);
    }
}
