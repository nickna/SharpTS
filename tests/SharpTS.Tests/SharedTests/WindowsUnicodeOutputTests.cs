namespace SharpTS.Tests.SharedTests;

public sealed class WindowsUnicodeOutputTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "collection_unicode", """
            console.log([..."A😀B"].join(","));function collect(...xs:any[]){console.log(xs.length,xs.join(","));}collect(0,..."😀B",9);
            """, "A,😀,B\n4 0,😀,B,9\n" };
        yield return new object[] { "collection_unicode_units_control", """
            const a=[..."A😀B"];console.log(a.length,a[1].length,a[1].charCodeAt(0),a[1].charCodeAt(1));function collect(...xs:any[]){console.log(xs.length,xs[1].length,xs[1].charCodeAt(0),xs[1].charCodeAt(1));}collect(0,..."😀B",9);
            """, "3 2 55357 56832\n4 2 55357 56832\n" };
        yield return new object[] { "multilingual-text", """
            console.log("é Ω 漢字 😀");console.log("a😀b".length);
            """, "é Ω 漢字 😀\n4\n" };
        yield return new object[] { "module-unicode", """
            export {};console.log("é Ω 漢字 😀");
            """, "é Ω 漢字 😀\n" };
    }
}
