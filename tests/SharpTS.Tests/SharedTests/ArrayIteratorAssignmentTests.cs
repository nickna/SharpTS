using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayIteratorAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const source=[1,2];source[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "original-alias", """
            const source=[1,2];const alias:any=source;alias[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "string-array", """
            const source=["a","b"];source[Symbol.iterator]=function*(){yield "x";yield "y";};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "x y\n" };
        yield return new object[] { "readonly-array", """
            const source:ReadonlyArray<number> = [1,2];source[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "tuple", """
            const source:[number,number]=[1,2];source[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "iterator-key-alias", """
            const source=[1,2];const key=Symbol.iterator;source[key]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
        yield return new object[] { "copied-factory", """
            const other=[3,4];other[Symbol.iterator]=function*(){yield 8;yield 9;};const source=[1,2];source[Symbol.iterator]=other[Symbol.iterator];const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
    }

    public static IEnumerable<object[]> TypingOnlyCases()
    {
        yield return new object[] { "iterator-return", """
            const source=[1,2];source[Symbol.iterator]=()=>[8,9].values();const [a,...rest]=source;console.log(a,rest.join(","));
            """, "8 9\n" };
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return new object[] { "wrong-element", """
            function* strings(){yield "wrong";}const source:number[]=[1,2];source[Symbol.iterator]=strings;
            """, "invalid" };
        yield return new object[] { "non-callable", """
            const source:number[]=[1,2];source[Symbol.iterator]=4;
            """, "invalid" };
        yield return new object[] { "array-result", """
            const source:number[]=[1,2];source[Symbol.iterator]=()=>[8,9];
            """, "invalid" };
        yield return new object[] { "next-only-result", """
            const source:number[]=[1,2];source[Symbol.iterator]=()=>({next(){return {value:8,done:false as const};}});
            """, "invalid" };
        yield return new object[] { "async-result", """
            const source:number[]=[1,2];source[Symbol.iterator]=async function*(){yield 8;};
            """, "invalid" };
        yield return new object[] { "required-argument", """
            const source:number[]=[1,2];source[Symbol.iterator]=function*(n:number){yield n;};
            """, "invalid" };
        yield return new object[] { "readonly-index", """
            const source:ReadonlyArray<number> = [1,2];source[0]=4;
            """, "invalid" };
        yield return new object[] { "typed-factory-read", """
            const source:number[]=[1,2];const factory:()=>IterableIterator<string> = source[Symbol.iterator];
            """, "invalid" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> InvalidSourceCases() => InvalidCases().Select(row => new[] { row[1] });
    public static IEnumerable<object[]> InvalidCliCases() => InvalidCases().Select(row => row[..2]);
    public static IEnumerable<object[]> TypingOnlyCliCases() => TypingOnlyCases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void TypedArrayIteratorAssignmentRetainsElementContract(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, MemberData(nameof(InvalidSourceCases))]
    public void InvalidFactoryAndReadonlyNumericWritesStayRejected(string source)
    {
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
