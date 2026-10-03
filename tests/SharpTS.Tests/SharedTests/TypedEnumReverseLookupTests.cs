using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedEnumReverseLookupTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "function_capture", """
            enum Values {First=2,Second=3}function read(key:number):string{return Values[key];}console.log(read(3),read(2));
            """, "Second First\n" };
        yield return new object[] { "loop", """
            enum Values {First,Second,Third}for(let i=0;i<3;i++)console.log(Values[i]);
            """, "First\nSecond\nThird\n" };
        yield return new object[] { "async", """
            enum Values {First=2,Second=3}async function read(key:number){return Values[key];}read(3).then(value=>console.log(value));
            """, "Second\n" };
        yield return new object[] { "loop_any", """
            enum Values {First,Second,Third}for(let i:any=0;i<3;i++)console.log(Values[i]);
            """, "First\nSecond\nThird\n" };
        yield return new object[] { "async_local", """
            enum Values {First=2,Second=3}async function read(){let key:number=3;return Values[key];}read().then(value=>console.log(value));
            """, "Second\n" };
        yield return new object[] { "literal_control", """
            enum Values {First=2,Second=3}enum Flags {One=1,Two=1<<1,All=One|Two}console.log(Values[3],Flags[3]);
            """, "Second All\n" };
        yield return new object[] { "function_any", """
            enum Values {First=2,Second=3}function read(key:any):string{return Values[key];}console.log(read(3),read(2));
            """, "Second First\n" };
        yield return new object[] { "async-suspension", """
            enum E{A=2,B=3}async function read(key:number):Promise<string>{await Promise.resolve(1);return E[key];}read(3).then(value=>console.log(value));
            """, "B\n" };
        yield return new object[] { "async-generator-suspension", """
            enum E{A=2,B=3}async function* names(key:number){await Promise.resolve(1);yield E[key];key=2;yield E[key];}async function run(){for await(const value of names(3))console.log(value);}run();
            """, "B\nA\n" };
    }

    public static IEnumerable<object[]> HostedCases()
    {
        yield return new object[] { "hosted_lookup", """
            enum Values {First=2,Second=3}export function read(key:number):string{return Values[key];}
            """ };
        yield return new object[] { "hosted_computed", """
            enum Flags {One=1,Two=2,All=One|Two}export function read(key:number):string{return Flags[key];}
            """ };
        yield return new object[] { "hosted_local", """
            enum Values {First=2,Second=3}export function read():string{let key:number=3;return Values[key];}
            """ };
        yield return new object[] { "hosted_literal", """
            enum Values {First=2,Second=3}export function read():string{return Values[3];}
            """ };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void CompiledTypedEnumReverseLookupsVerifyAndExecute(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
