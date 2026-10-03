using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            namespace Values {export const value=3;}const values:any=Values;values.extra=8;values["value"]=9;console.log(values.extra,values.value);
            """, "8 9\n" };
        yield return new object[] { "live-binding", """
            namespace Counter {export let value=1;export function increment(){value++;}export function read(){return value;}}const counter:any=Counter;console.log(counter.value,Counter.value);Counter.increment();console.log(counter.value,Counter.value,Counter.read());
            """, "1 1\n2 2 2\n" };
        yield return new object[] { "dynamic-write", """
            namespace State {export let value=2;export function read(){return value;}}const state:any=State;state["value"]=9;console.log(state.value,State.value,State.read());
            """, "9 9 9\n" };
        yield return new object[] { "dot-and-index", """
            namespace State {export let value=2;}const state:any=State;state.value=7;console.log(state["value"],State.value);state["added"]=8;console.log(state.added);state.added=9;console.log(state["added"]);
            """, "7 7\n8\n9\n" };
        yield return new object[] { "nested", """
            namespace Outer {export namespace Inner {export let value=2;export function read(){return value;}}}const inner:any=Outer.Inner;inner.value=7;console.log(inner.value,Outer.Inner.value,Outer.Inner.read());
            """, "7 7 7\n" };
        yield return new object[] { "function-overwrite", """
            namespace Values {export function read(){return 3;}}const values:any=Values;values.read=()=>9;console.log(values.read(),Values.read());
            """, "9 9\n" };
        yield return new object[] { "unrelated-control", """
            namespace Left {export let value=1;}namespace Right {export let value=2;}const left:any=Left;left.value=7;console.log(left.value,Left.value,Right.value);
            """, "7 7 2\n" };
        yield return new object[] { "strict", """
            "use strict";namespace State {export let value=2;export function read(){return value;}}const state:any=State;state.extra=8;state["value"]=9;console.log(state.extra,state.value,State.read());
            """, "8 9 9\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void NamespacePropertyWritesStayVisibleThroughAliasesAndBindings(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
