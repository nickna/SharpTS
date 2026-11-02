using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncArrowDeclarationRelayTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-descendant-relay", """
            const run=async()=>{const p={field:7};function f(){function g(){return p}return g()}await Promise.resolve(0);console.log(f().field)};run();
            """, "7\n"];
        yield return ["live-write-through-intermediary", """
            const run=async()=>{let n=1;function f(){function g(){return ++n}return g()}n=7;await Promise.resolve(0);console.log(f(),n)};run();
            """, "8 8\n"];
        yield return ["independent-returned-descendants", """
            const make=async(n:number)=>{function f(){function g(){return ++n}return g}await Promise.resolve(0);return f()};async function main(){const a=await make(1),b=await make(10);console.log(a(),b(),a())}main();
            """, "2 11 3\n"];
        yield return ["two-ordinary-intermediaries", """
            const run=async()=>{const p={field:7};function f(){function g(){function h(){return p}return h()}return g()}await Promise.resolve(0);console.log(f().field)};run();
            """, "7\n"];
        yield return ["sync-arrow-intermediary", """
            const run=async()=>{const p={field:7};function f(){const middle=()=>{function g(){return p}return g()};return middle()}await Promise.resolve(0);console.log(f().field)};run();
            """, "7\n"];
        yield return ["default-parameter-owner", """
            const run=async(n:number=7)=>{function f(){function g(){return n}return g}await Promise.resolve(0);console.log(f()())};run();
            """, "7\n"];
        yield return ["local-shadow-has-distinct-owner", """
            const run=async()=>{const n=7;function f(){const n=9;function g(){return n}return g()}function read(){return n}await Promise.resolve(0);console.log(f(),read())};run();
            """, "9 7\n"];
        yield return ["mixed-owner-fields", """
            const run=async(seed:number=3)=>{let n=4;const p={field:7};function f(){function g(){return seed+n+p.field}return g()}await Promise.resolve(0);console.log(f())};run();
            """, "14\n"];
        yield return ["no-suspension-owner", """
            const run=async()=>{const p={field:7};function f(){function g(){return p}return g()}console.log(f().field)};run();
            """, "7\n"];
        yield return ["synchronous-owner-control", """
            const run=()=>{const p={field:7};function f(){function g(){return p}return g()}console.log(f().field)};run();
            """, "7\n"];
        yield return ["parameter-default-through-sync-arrow", """
            function enclosing(){const value='deep';const middle=()=>{function make(read:any=()=>value){var value='body';return [read,()=>value]}return make()};return middle()}const pair:any=enclosing();console.log(pair[0](),pair[1]());
            """, "deep body\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryDescendantsRetainTheirActualAsyncArrowOwner(
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
