using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public sealed class PrimitiveConstructorCalleeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["number-local", "function run(){const C=7;try{new C()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
        yield return ["typed-name-number-local", "function run(){const Uint8Array=7;try{new Uint8Array()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
        yield return ["boolean-local", "function run(){const C=true;try{new C()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
        yield return ["number-parameter", "function run(C:number){try{new C()}catch(e){console.log(e instanceof TypeError)}}run(7);", "true\n"];
        yield return ["boolean-parameter", "function run(C:boolean){try{new C()}catch(e){console.log(e instanceof TypeError)}}run(true);", "true\n"];
        yield return ["integer-counter", "for(let C=0;C<1;C++){try{new C()}catch(e){console.log(e instanceof TypeError)}}", "true\n"];
        yield return ["captured-number", "function outer(){const C=7;return()=>{try{new C()}catch(e){console.log(e instanceof TypeError)}}}outer()();", "true\n"];
        yield return ["argument-order", "function run(){const C=7;let log=\"\";function mark(x:string){log+=x;return 1}try{new C(mark(\"a\"),mark(\"b\"))}catch(e){console.log(log,e instanceof TypeError)}}run();", "ab true\n"];
        yield return ["argument-abrupt-before-constructability", "function run(){const C=7,marker={};function fail(){throw marker}try{new C(fail())}catch(e){console.log(e===marker)}}run();", "true\n"];
        yield return ["ordinary-object-callee", "function run(){const C={};try{new C()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
        yield return ["function-value-control", "const C:any=function(this:any,v:number){this.value=v};console.log(new C(7).value);", "7\n"];
        yield return ["class-value-control", "class C{constructor(public value:number){}}const D=C;console.log(new D(7).value);", "7\n"];
        yield return ["any-grouped-control", "try{new (7 as any)()}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["string-callee", "function run(){const C=\"x\";try{new C()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
        yield return ["bigint-callee", "function run(){const C=7n;try{new C()}catch(e){console.log(e instanceof TypeError)}}run();", "true\n"];
        yield return ["nullish-callees", "function run(){const A=null,B=undefined;try{new A()}catch(e){console.log(e instanceof TypeError)}try{new B()}catch(e){console.log(e instanceof TypeError)}}run();", "true\ntrue\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void RuntimeConstructorConsumerReceivesBoxedValues(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }

    public static IEnumerable<object[]> OriginalSources()
    {
        yield return ["function run(){const Uint8Array=7;new Uint8Array()}run();"];
        yield return ["function run(){const NotCtor=7;new NotCtor()}run();"];
    }

    [Theory, MemberData(nameof(OriginalSources))]
    public void OriginalUncaughtProgramsHaveValidWholeIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
}
