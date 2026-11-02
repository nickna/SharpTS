using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayReflectIntrinsicLengthTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const value:any=new Uint8Array(3);console.log(Reflect.get(value,'length'));\n", "3\n"];
        yield return ["all-element-kinds", "console.log(Reflect.get(new Int8Array(0),'length'),Reflect.get(new Uint8Array(1),'length'),Reflect.get(new Uint8ClampedArray(2),'length'),Reflect.get(new Int16Array(3),'length'),Reflect.get(new Uint16Array(4),'length'),Reflect.get(new Int32Array(5),'length'),Reflect.get(new Uint32Array(6),'length'),Reflect.get(new Float32Array(7),'length'),Reflect.get(new Float64Array(8),'length'),Reflect.get(new BigInt64Array(9),'length'),Reflect.get(new BigUint64Array(10),'length'));", "0 1 2 3 4 5 6 7 8 9 10\n"];
        yield return ["subview-elements", "const buffer=new ArrayBuffer(16);const value:any=new Uint16Array(buffer,4,3);console.log(Reflect.get(value,'length'));", "3\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReadsIntrinsicViewLength(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted)
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
            return;
        }
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
