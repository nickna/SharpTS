using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ForAwaitElementInferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            async function run(){let total=0;for await(const n of [Promise.resolve(2),Promise.resolve(4)])total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "original-generator", """
            async function* values(){for await(const n of [Promise.resolve(2),Promise.resolve(3)])yield n+1;}async function run(){let total=0;for await(const n of values())total+=n;console.log(total);}run();
            """, "7\n" };
        yield return new object[] { "any-control", """
            async function run(){const values:any=[Promise.resolve(2),Promise.resolve(4)];let total=0;for await(const n of values)total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "any-generator-control", """
            async function* values(){const items:any=[Promise.resolve(2),Promise.resolve(3)];for await(const n of items)yield n+1;}async function run(){let total=0;for await(const n of values())total+=n;console.log(total);}run();
            """, "7\n" };
        yield return new object[] { "mixed-union", """
            async function run(){let total=0;for await(const value of [Promise.resolve(2),4,Promise.resolve(3)]){const n:number=value;total+=n;}console.log(total);}run();
            """, "9\n" };
        yield return new object[] { "readonly-union", """
            async function run(){const items:ReadonlyArray<Promise<number>|number> = [Promise.resolve(2),4];let total=0;for await(const value of items){const n:number=value;total+=n;}console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "sync-generator", """
            function* values(){yield Promise.resolve(2);yield Promise.resolve(4);}
            async function run(){let total=0;for await(const value of values()){const n:number=value;total+=n;}console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "set-union", """
            async function run(){const items=new Set<Promise<number>|number>([Promise.resolve(2),4]);let total=0;for await(const value of items){const n:number=value;total+=n;}console.log(total);}run();
            """, "6\n" };
    }

    public static IEnumerable<object[]> TypeCases()
    {
        yield return new object[] { "iterable-union", """
            async function accept(items:Iterable<Promise<number>|number>):Promise<void>{for await(const value of items){const result:number=value;}}
            console.log("accepted");
            """, "accepted\n" };
        yield return new object[] { "nested-promise", """
            async function accept(items:Iterable<Promise<Promise<number>>>):Promise<void>{for await(const value of items){const result:number=value;}}
            console.log("accepted");
            """, "accepted\n" };
        yield return new object[] { "async-iterable", """
            async function accept(items:AsyncIterable<Promise<number>>):Promise<void>{for await(const value of items){const result:number=value;}}
            console.log("accepted");
            """, "accepted\n" };
        yield return new object[] { "ordinary-for-of", """
            function accept(items:Promise<number>[]):void{for(const value of items){const result:Promise<number> = value;}}
            console.log("accepted");
            """, "accepted\n" };
        yield return new object[] { "tuple-members", """
            async function accept(items:Map<string,Promise<number>>):Promise<void>{for await(const value of items){const key:string=value[0];const result:Promise<number> = value[1];}}
            console.log("accepted");
            """, "accepted\n" };
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return new object[] { "awaited-to-promise", """
            async function accept(items:Promise<number>[]):Promise<void>{for await(const value of items){const result:Promise<number> = value;}}
            """, "invalid" };
        yield return new object[] { "ordinary-to-number", """
            function accept(items:Promise<number>[]):void{for(const value of items){const result:number=value;}}
            """, "invalid" };
        yield return new object[] { "awaited-to-string", """
            async function accept(items:Promise<number>[]):Promise<void>{for await(const value of items){const result:string=value;}}
            """, "invalid" };
        yield return new object[] { "mixed-string", """
            async function accept(items:Array<Promise<number>|string>):Promise<void>{for await(const value of items){const result:number=value;}}
            """, "invalid" };
        yield return new object[] { "readonly-to-promise", """
            async function accept(items:ReadonlyArray<Promise<number>>):Promise<void>{for await(const value of items){const result:Promise<number> = value;}}
            """, "invalid" };
        yield return new object[] { "async-to-promise", """
            async function accept(items:AsyncIterable<Promise<number>>):Promise<void>{for await(const value of items){const result:Promise<number> = value;}}
            """, "invalid" };
        yield return new object[] { "awaited-string", """
            async function accept(items:Promise<string>[]):Promise<void>{for await(const value of items){const result:number=value;}}
            """, "invalid" };
    }

    public static IEnumerable<object[]> IndependentCases()
    {
        yield return new object[] { "awaited-member-independent", """
            async function accept():Promise<void>{for await(const value of [Promise.resolve(2)]){value.then((n:number)=>n);}}
            """, "invalid" };
        yield return new object[] { "primitive-member-independent", """
            function accept(value:number):void{value.then((n:number)=>n);}
            """, "invalid" };
    }

    public static IEnumerable<object[]> ParityCases() => Cases().Concat(TypeCases()).Select(row => row[1..]);

    public static IEnumerable<object[]> InvalidSourceCases() => InvalidCases().Select(row => new[] { row[1] });

    public static IEnumerable<object[]> InvalidCliCases() => InvalidCases().Select(row => row[..2]);

    [Theory, MemberData(nameof(ParityCases))]
    public void ForAwaitBindsAwaitedElementAndForOfKeepsPromises(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(InvalidSourceCases))]
    public void InvalidBindingsRemainTypeErrors(string source)
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
