using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypedArrayNumericLogicalAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["numeric-logical-original", "const a:any=new Uint8Array([0]);let calls=0;console.log(a[0]||=++calls,a[0],calls);console.log(a[0]||=++calls,a[0],calls);", "1 1 1\n1 1 1\n"];
        yield return ["numeric-and-shortcircuit", "const a:any=new Uint8Array([1]);let calls=0;function rhs(){calls++;return 0}console.log(a[0]&&=rhs(),a[0],calls);console.log(a[0]&&=rhs(),a[0],calls);", "0 0 1\n0 0 1\n"];
        yield return ["numeric-nullish-shortcircuit", "const a:any=new Uint8Array([0]);let calls=0;console.log(a[0]??=++calls,a[0],calls);", "0 0 0\n"];
        yield return ["target-key-rhs-once", "const a:any=new Uint8Array([0]);let log='';function target(){log+='t';return a}function key(){log+='k';return 0}function rhs(){log+='r';return 3}console.log(target()[key()]||=rhs(),a[0],log);", "3 3 tkr\n"];
        yield return ["rhs-abrupt-leaves-storage", "const a:any=new Uint8Array([0]);let calls=0;function rhs(){calls++;throw 'rhs'}try{a[0]||=rhs()}catch(e){console.log(e,a[0],calls)}", "rhs 0 1\n"];
        yield return ["assignment-result-before-uint8-conversion", "const a:any=new Uint8Array([0]);console.log(a[0]||=300,a[0]);", "300 44\n"];
        yield return ["assignment-result-before-clamp", "const a:any=new Uint8ClampedArray([0]);console.log(a[0]||=300,a[0]);", "300 255\n"];
        yield return ["bigint-value-numeric-index", "const a:any=new BigInt64Array([0n]);const result=a[0]||=2n;console.log(result===2n,a[0]===2n);", "true true\n"];
        yield return ["strict-numeric-index", "'use strict';const a:any=new Int16Array([0]);console.log(a[0]||=7,a[0]);", "7 7\n"];
        yield return ["nonextensible-existing-numeric-index", "const a:any=new Uint8Array([0]);Object.preventExtensions(a);console.log(a[0]||=3,a[0],Object.isExtensible(a));", "3 3 false\n"];
        yield return ["ordinary-array-fallback-control", "const a:any=[0];let calls=0;console.log(a[0]||=++calls,a[0],calls);console.log(a[0]||=++calls,a[0],calls);", "1 1 1\n1 1 1\n"];
        yield return ["existing-numeric-compound-control", "const a:any=new Uint8Array([4]);console.log(a[0]+=1,a[0]);", "5 5\n"];
        yield return ["numeric-negative-zero-index", "const a:any=new Float64Array([0]);console.log(a[-0]||=5,a[0]);", "5 5\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void NumericLogicalReferencesKeepTheirValueAndOrder(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
