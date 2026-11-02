using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayIntrinsicLengthReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["constructor-lengths", "console.log(new Uint8Array(3).length,new Int32Array([3,5]).length);", "3 2\n"];
        yield return ["view-intrinsic-metadata", "const buffer=new ArrayBuffer(8);const value:any=new Uint8Array(buffer,2,3);console.log(value.length,value.byteLength,value.byteOffset);", "3 3 2\n"];
        yield return ["numeric-loop", "const values=new Uint8Array([2,3,4]);let sum=0;for(let i=0;i<values.length;i++)sum+=values[i];console.log(sum,values.length,values[1]);", "9 3 3\n"];
        yield return ["all-element-kinds", "console.log(new Int8Array(2).length,new Uint8Array(2).length,new Uint8ClampedArray(2).length,new Int16Array(2).length,new Uint16Array(2).length,new Int32Array(2).length,new Uint32Array(2).length,new Float32Array(2).length,new Float64Array(2).length,new BigInt64Array(2).length,new BigUint64Array(2).length);", "2 2 2 2 2 2 2 2 2 2 2\n"];
        yield return ["async-and-parameter", "function count(value:any){return value.length}async function run(){const value=new Uint16Array(3);await Promise.resolve(0);console.log(count(value),value.length)}run();", "3 3\n"];
        yield return ["unrelated-array-prototype-length", "const prototype:any=Array.prototype;prototype.length=7;const value:any=new Uint8Array(2);console.log(value.length,[1,2].length);", "2 2\n"];
        yield return ["prototype-descriptor-control", "const d=Object.getOwnPropertyDescriptor(Array.prototype,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "0 true false false\n"];
        yield return ["ordinary-length-controls", "const values=[1,2,3];console.log(values.length,values['length'],Reflect.get(values,'length'),'abc'.length,new Uint8Array(0).length);", "3 3 3 3 0\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ViewLengthPrecedesUnrelatedArrayPrototypeMetadata(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    // Compiled nonnumeric computed-key lookup has its separate existing P3 #3054.
    public static IEnumerable<object[]> InterpretedPaths()
    {
        yield return ["view-length-paths", "const buffer=new ArrayBuffer(8);const value:any=new Uint8Array(buffer,2,3);console.log(value.length,value['length'],Reflect.get(value,'length'),value.byteLength,value.byteOffset);", "3 3 3 3 2\n"];
        yield return ["unrelated-array-prototype-length", "const prototype:any=Array.prototype;prototype.length=7;const value:any=new Uint8Array(2);console.log(value.length,value['length'],Reflect.get(value,'length'),[1,2].length);", "2 2 2 2\n"];
    }

    [Theory, MemberData(nameof(InterpretedPaths))]
    public void InterpretedComputedPathsReadTheSameIntrinsicLength(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
