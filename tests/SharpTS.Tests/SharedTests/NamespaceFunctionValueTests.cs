using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamespaceFunctionValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "members", """
            namespace MathBox {export const base=2;export function add(value:number){return value+base;}}const box:any=MathBox;console.log(MathBox.base,MathBox.add(3),box.base,box.add(4),box===MathBox);
            """, "2 5 2 6 true\n" };
        yield return new object[] { "nested", """
            namespace Outer.Inner {export const value=7;export function read(){return value;}}const outer:any=Outer;console.log(Outer.Inner.value,outer.Inner.read(),outer.Inner===Outer.Inner);
            """, "7 7 true\n" };
        yield return new object[] { "merged", """
            namespace Joined {export const first=3;export function left(){return first;}}namespace Joined {export const second=5;export function right(){return first+second;}}const joined:any=Joined;console.log(joined.first,joined.second,joined.left(),joined.right());
            """, "3 5 3 8\n" };
        yield return new object[] { "live-binding", """
            namespace Counter {export let value=1;export function increment(){value++;}export function read(){return value;}}const counter:any=Counter;console.log(counter.value,Counter.value);Counter.increment();console.log(counter.value,Counter.value,Counter.read());
            """, "1 1\n2 2 2\n" };
        yield return new object[] { "dynamic-write", """
            namespace State {export let value=2;export function read(){return value;}}const state:any=State;state["value"]=9;console.log(state.value,State.value,State.read());
            """, "9 9 9\n" };
        yield return new object[] { "generator-capture", """
            namespace Sequence {const base=4;export function* values(){yield base;yield base+1;}}const sequence:any=Sequence;console.log([...Sequence.values()].join(","),[...sequence.values()].join(","));
            """, "4,5 4,5\n" };
        yield return new object[] { "async-capture", """
            namespace AsyncBox {const base=6;export async function read(){await Promise.resolve(0);return base+1;}}AsyncBox.read().then(value=>console.log(value));
            """, "7\n" };
        yield return new object[] { "function-shape", """
            namespace Values {export function read(){return 3;}}const values:any=Values;console.log(typeof Values.read,typeof values.read,Values.read===values.read);
            """, "function function true\n" };
    }

    public static IEnumerable<object[]> CallableCases() => Cases();
    public static IEnumerable<object[]> CompiledSources() => CallableCases().Select(row => row[1..]);
    public static IEnumerable<object[]> InterpretedSources() => CallableCases()
        .Where(row => (string)row[0] is not ("merged" or "dynamic-write")).Select(row => row[1..]);

    [Theory, MemberData(nameof(CompiledSources))]
    public void CompiledNamespaceFunctionsAreCallableThroughValues(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(InterpretedSources))]
    public void InterpretedNamespaceFunctionsRetainCallability(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
