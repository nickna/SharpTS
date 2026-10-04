using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class PrefixedStreamImportTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            import {Readable} from "node:stream";async function run(){const stream:any=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "original-catch", """
            import {Readable} from "node:stream";async function run(){try{const stream:any=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}catch(e:any){console.log("caught",e.name,e.message);}}run();
            """, "6\n" };
        yield return new object[] { "original-typed", """
            import {Readable} from "node:stream";async function run(){const stream=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "original-push", """
            import {Readable} from "node:stream";async function run(){const stream:any=new Readable({objectMode:true});stream.push(1);stream.push(2);stream.push(3);stream.push(null);let total=0;for await(const n of stream)total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "original-bare", """
            import {Readable} from "stream";async function run(){const stream=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}run();
            """, "6\n" };
        yield return new object[] { "original-stage", """
            import {Readable} from "node:stream";async function run(){try{console.log("before-from");const stream:any=Readable.from([1,2,3]);console.log("after-from");let total=0;for await(const n of stream)total+=n;console.log(total);}catch(e:any){console.log("caught",e.name,e.message);}}run();
            """, "before-from\nafter-from\n6\n" };
        yield return new object[] { "bare-push", """
            import {Readable} from "stream";async function run(){const stream:any=new Readable({objectMode:true});stream.push(1);stream.push(2);stream.push(3);stream.push(null);let total=0;for await(const n of stream)total+=n;console.log(total);}run();
            """, "6\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);

    [Theory, MemberData(nameof(SourceCases))]
    public void PrefixedAndBareStreamImportsRetainReadableStaticCalls(string source, string expected)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }
}
