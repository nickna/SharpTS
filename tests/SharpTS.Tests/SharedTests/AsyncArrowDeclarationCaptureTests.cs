using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncArrowDeclarationCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["no-capture", """
            const run=async()=>{function f(){return 7}console.log(f())};run();
            """, "7\n"];
        yield return ["captured-local", """
            const run=async()=>{const p={field:7};function f(){return p}console.log(f().field)};run();
            """, "7\n"];
        yield return ["literal-getter", """
            const run=async()=>{let value=7;const p={get field(){return value}};console.log(p.field)};run();
            """, "7\n"];
        yield return ["capture-after-await", """
            const run=async()=>{const log:string[]=[];function f(){log.push('f');return 7}await Promise.resolve(0);console.log(f(),log.join(','))};run();
            """, "7 f\n"];
        yield return ["sync-arrow-control", """
            const run=()=>{const p={field:7};function f(){return p}console.log(f().field)};run();
            """, "7\n"];
        yield return ["named-async-control", """
            async function run(){const p={field:7};function f(){return p}console.log(f().field)}run();
            """, "7\n"];
        yield return ["live-owner-and-declaration-writes", """
            const run=async()=>{let n=1;function f(){n++;return n}n=4;await Promise.resolve(0);console.log(f(),n)};run();
            """, "5 5\n"];
        yield return ["captured-parameter-and-default", """
            const run=async(n:number=3)=>{function f(){return n}console.log(f());n=7;await Promise.resolve(0);console.log(f())};run();
            """, "3\n7\n"];
        yield return ["independent-returned-functions", """
            const make=async(n:number)=>{function f(){return ++n}await Promise.resolve(0);return f};async function main(){const a=await make(1),b=await make(10);console.log(a(),b(),a())}main();
            """, "2 11 3\n"];
        yield return ["sibling-async-read", """
            const run=async()=>{let n=1;function f(){n=7}const read=async()=>n;f();console.log(await read(),n)};run();
            """, "7 7\n"];
        yield return ["sibling-ordinary-functions", """
            const run=async()=>{let n=1;function write(){n=7}function read(){return n}write();await Promise.resolve(0);console.log(read(),n)};run();
            """, "7 7\n"];
        yield return ["nested-async-arrow-owner", """
            async function outer(){const run=async()=>{const p={field:7};function f(){return p}await Promise.resolve(0);return f().field};console.log(await run())}outer();
            """, "7\n"];
        yield return ["function-value-alias", """
            const run=async()=>{const p={field:7};function f(){return p}const saved=f;await Promise.resolve(0);console.log(saved===f,saved().field)};run();
            """, "true 7\n"];
        yield return ["ordinary-loop-cell-control", """
            const run=async()=>{const out:Array<()=>number>=[];for(let i=0;i<3;i++){function f(){return i}out.push(f)}await Promise.resolve(0);console.log(out[0](),out[1](),out[2]())};run();
            """, "0 1 2\n"];
        yield return ["captured-default-and-later-default", """
            const run=async(x:number=7,y:number=x)=>{function read(){return x}console.log(read(),y);await Promise.resolve(0);console.log(read(),y)};run();
            """, "7 7\n7 7\n"];
        yield return ["supplied-and-explicit-undefined-parameters", """
            const run=async(x:number=7,y:number=x)=>{function read(){return x}await Promise.resolve(0);console.log(read(),y)};async function main(){await run(3);await run(undefined)}main();
            """, "3 3\n7 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryDeclarationsShareTheActualAsyncArrowEnvironment(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
