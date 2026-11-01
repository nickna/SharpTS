using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayUnionReadValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["numeric-union-original", "function read(flag:boolean):number{const a=flag?new Int8Array(1):new Uint8Array(1);return a[1]}console.log(read(true)===undefined,read(false)===undefined);", "true true\n"];
        yield return ["numeric-local", "function run(flag:boolean){const a=flag?new Int8Array(1):new Uint8Array(1);let value:number=a[1];console.log(typeof value,value===undefined,Number.isNaN(value));}run(true);run(false);", "undefined true false\nundefined true false\n"];
        yield return ["numeric-argument", "function see(value:number){console.log(typeof value,value===undefined)}function run(flag:boolean){const a=flag?new Int8Array(1):new Uint8Array(1);see(a[1])}run(true);run(false);", "undefined true\nundefined true\n"];
        yield return ["forwarded-return", "function read(flag:boolean):number{const a=flag?new Int8Array(1):new Uint8Array(1);return a[1]}function forward(flag:boolean):number{return read(flag)}console.log(forward(true)===undefined,typeof forward(false));", "true undefined\n"];
        yield return ["arrow-expression-return", "const read=(flag:boolean):number=>(flag?new Int8Array(1):new Uint8Array(1))[1];console.log(read(true)===undefined,read(false)===undefined);", "true true\n"];
        yield return ["wrapped-read", "function read(flag:boolean):number{const a=flag?new Int8Array(1):new Uint8Array(1);return ((a[1] as number)!)}console.log(read(true)===undefined,read(false)===undefined);", "true true\n"];
        yield return ["ternary-pass-through", "function read(flag:boolean):number{const a=flag?new Int8Array([7]):new Uint8Array([9]);return flag?a[1]:a[0]}console.log(read(true)===undefined,read(false));", "true 9\n"];
        yield return ["nullish-consumer", "function read(flag:boolean):number{const a=flag?new Int8Array([7]):new Uint8Array([9]);return a[1]??5}console.log(read(true),read(false));", "5 5\n"];
        yield return ["arithmetic-consumer", "function read(flag:boolean):number{const a=flag?new Int8Array([7]):new Uint8Array([9]);return a[1]+1}console.log(Number.isNaN(read(true)),Number.isNaN(read(false)));", "true true\n"];
        yield return ["known-length-member", "function measureLength(flag:boolean):number{const a=flag?new Int8Array(1):new Uint8Array(2);return a[\"length\"]}console.log(measureLength(true),measureLength(false));", "1 2\n"];
        yield return ["bigint-union-read-return", "function read(flag:boolean):bigint{const a=flag?new BigInt64Array([7n]):new BigUint64Array([9n]);return a[1]}console.log(read(true)===undefined,typeof read(false));", "true undefined\n"];
        yield return ["bigint-union-argument-error", "function add(value:bigint){try{console.log(value+1n)}catch(e){console.log(e instanceof TypeError)}}function run(flag:boolean){const a=flag?new BigInt64Array([7n]):new BigUint64Array([9n]);add(a[1])}run(true);run(false);", "true\ntrue\n"];
        yield return ["bigint-union-forwarded-return", "function read(flag:boolean):bigint{const a=flag?new BigInt64Array([7n]):new BigUint64Array([9n]);return a[1]}function forward(flag:boolean):bigint{return read(flag)}console.log(forward(true)===undefined,typeof forward(false));", "true undefined\n"];
        yield return ["three-numeric-kinds", "function read(kind:number):number{const a=kind===0?new Int8Array(1):kind===1?new Uint8Array(1):new Float64Array(1);return a[1]}console.log(read(0)===undefined,read(1)===undefined,read(2)===undefined);", "true true true\n"];
        yield return ["subview-physical-length", "function read(flag:boolean):number{const buffer=new ArrayBuffer(8);const a=flag?new Int8Array(buffer,1,1):new Uint8Array(buffer,2,1);return a[1]}console.log(read(true)===undefined,read(false)===undefined);", "true true\n"];
        yield return ["valid-and-ordinary-control", "function typed(flag:boolean):number{const a=flag?new Int8Array([7]):new Uint8Array([9]);return a[0]}function ordinary(flag:boolean):number{const a=flag?[7]:[9];return a[0]}console.log(typed(true),typed(false),ordinary(true),ordinary(false));", "7 9 7 9\n"];
    }

    // The literal named-member compiler path retains existing P3 #3054.
    // Its unchanged source still runs in the raw 32-lane CLI acceptance matrix.
    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        ((string)row[0] == "known-length-member"
            ? new[] { ExecutionMode.Interpreted }
            : new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }).Select(mode =>
                new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void TypedArrayUnionElementsKeepRawMissingValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
