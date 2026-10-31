using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceArrowOwnershipTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-private-helper", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=()=>console.log(f());}N.run();",
        }, "7\n"];
        yield return ["namespace-local-shadow", new Dictionary<string,string>
        {
            ["lib.ts"] = "export function queueMicrotask(callback:()=>void):void{console.log(\"helper\");callback();}",
            ["main.ts"] = "import {queueMicrotask as enqueue} from \"./lib\";const events:string[]=[];namespace N{export function queueMicrotask(cb:()=>void){console.log(\"namespace\");cb();}export const run=()=>{queueMicrotask(()=>events.push(\"global\"));events.push(\"sync\");enqueue(()=>events.push(\"helper\"));setTimeout(()=>console.log(events.join(\",\")),0);};}N.run();",
        }, "namespace\nhelper\nglobal,sync,helper\n"];
        yield return ["script-helper", new Dictionary<string,string>
        {
            ["main.ts"] = "namespace N{function f(){return 7;}export const run=()=>console.log(f());}N.run();",
        }, "7\n"];
        yield return ["nested-namespace", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace Outer{function f(){return 7;}export namespace Inner{export const run=()=>f();}}console.log(Outer.Inner.run());",
        }, "7\n"];
        yield return ["sibling-namespace", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace A{function f(){return 1;}export const run=()=>f();}namespace B{function f(){return 2;}export const run=()=>f();}console.log(A.run(),B.run());",
        }, "1 2\n"];
        yield return ["parameter-shadow", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export function run(f:()=>number){return ()=>f();}}console.log(N.run(()=>9)());",
        }, "9\n"];
        yield return ["local-shadow", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=()=>{const f=()=>8;return f();};}console.log(N.run());",
        }, "8\n"];
        yield return ["async-standalone", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=async()=>{await 0;console.log(f());};}N.run();",
        }, "7\n"];
        yield return ["nested-async", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export async function run(){const next=async()=>{await 0;console.log(f());};await next();}}N.run();",
        }, "7\n"];
        yield return ["generator-expression", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=function*(){yield f();};}console.log(N.run().next().value);",
        }, "7\n"];
        yield return ["async-generator-expression", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=async function*(){await 0;yield f();};}(async()=>console.log((await N.run().next()).value))();",
        }, "7\n"];
        yield return ["parameter-default", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=(value=f())=>console.log(value);}N.run();",
        }, "7\n"];
        yield return ["function-expression", new Dictionary<string,string>
        {
            ["main.ts"] = "export {};namespace N{function f(){return 7;}export const run=function(){return f();};}console.log(N.run());",
        }, "7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ArrowBodiesKeepTheirLexicalNamespace(string name, Dictionary<string,string> files, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }
}
