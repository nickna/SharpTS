using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ImportedNamespaceFunctionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            import {Library} from "./lib.ts";const library:any=Library;console.log(Library.base,library.read(),library===Library);
            """, """
            export namespace Library {export const base=8;export function read(){return base+1;}}
            """, "8 9 true\n" };
        yield return new object[] { "constant-control", """
            import {Library} from "./lib.ts";const library:any=Library;console.log(Library.value,library===Library);
            """, """
            export namespace Library {export const value=8;}
            """, "8 true\n" };
        yield return new object[] { "direct", """
            import {Library} from "./lib.ts";console.log(Library.base,Library.read());
            """, """
            export namespace Library {export const base=8;export function read(){return base+1;}}
            """, "8 9\n" };
        yield return new object[] { "named-export", """
            import {Library} from "./lib.ts";const library:any=Library;console.log(library.base,library.read(),library===Library);
            """, """
            namespace Library {export const base=8;export function read(){return base+1;}}export {Library};
            """, "8 9 true\n" };
        yield return new object[] { "nested", """
            import {Library} from "./lib.ts";const inner:any=Library.Inner;console.log(inner.base,inner.read(),inner===Library.Inner);
            """, """
            export namespace Library {export namespace Inner {export const base=8;export function read(){return base+1;}}}
            """, "8 9 true\n" };
        yield return new object[] { "generator", """
            import {Library} from "./lib.ts";const library:any=Library;console.log([...library.values()].join(","),library===Library);
            """, """
            export namespace Library {const base=8;export function* values(){yield base;yield base+1;}}
            """, "8,9 true\n" };
        yield return new object[] { "async", """
            import {Library} from "./lib.ts";const library:any=Library;library.read().then((value:any)=>console.log(value,library===Library));
            """, """
            export namespace Library {const base=8;export async function read(){await Promise.resolve(0);return base+1;}}
            """, "9 true\n" };
        yield return new object[] { "merged", """
            import {Library} from "./lib.ts";const library:any=Library;console.log(library.base,library.read(),library===Library);
            """, """
            export namespace Library {export const base=8;}export namespace Library {export function read(){return base+1;}}
            """, "8 9 true\n" };
        yield return new object[] { "renamed", """
            import {PublicLibrary as Library} from "./lib.ts";const library:any=Library;console.log(library.base,library.read(),library===Library);
            """, """
            namespace Library {export const base=8;export function read(){return base+1;}}export {Library as PublicLibrary};
            """, "8 9 true\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void ImportedNamespaceValuesRetainCallableMembers(string source, string library, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source, ["lib.ts"] = library };
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
