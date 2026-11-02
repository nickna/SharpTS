using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneBigIntValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "console.log(String(structuredClone(3n)));\n", "3\n"];
        yield return ["negative-bigint", "console.log(String(structuredClone(-4n)));", "-4\n"];
        yield return ["large-bigint", "console.log(String(structuredClone(123456789012345678901234567890n)));", "123456789012345678901234567890\n"];
        yield return ["object-bigint", "const a:any={n:3n};const b:any=structuredClone(a);console.log(String(b.n),b.n===a.n,b===a);", "3 true false\n"];
        yield return ["array-bigint", "const a:any=[3n,-4n];const b:any=structuredClone(a);console.log(String(b[0]),String(b[1]),a===b);", "3 -4 false\n"];
        yield return ["nested-bigint", "const a:any={items:[{n:7n}]};const b:any=structuredClone(a);console.log(String(b.items[0].n),a===b,a.items===b.items);", "7 false false\n"];
        yield return ["empty-transfer-bigint", "console.log(String(structuredClone(8n,{transfer:[]})));", "8\n"];
        yield return ["transfer-outside-bigint", "const b:any=new ArrayBuffer(2);console.log(String(structuredClone(9n,{transfer:[b]})),b.byteLength);", "9 0\n"];
        yield return ["primitive-identity", "const a:any=structuredClone(10n);console.log(typeof a,a===10n);", "bigint true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void BigIntIsCloneable(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
