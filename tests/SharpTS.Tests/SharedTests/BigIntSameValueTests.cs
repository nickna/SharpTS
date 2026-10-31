using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BigIntSameValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "console.log(Object.is(31n,31n),Object.is(BigInt('31'),BigInt('31')));", "true true\n"];
        yield return ["literal-and-constructor", "console.log(Object.is(31n,BigInt('31')),Object.is(BigInt('0x1f'),BigInt('0b11111')),Object.is(31n,32n));", "true true false\n"];
        yield return ["large-positive", "console.log(Object.is(340282366920938463463374607431768211457n,BigInt('340282366920938463463374607431768211457')),Object.is(340282366920938463463374607431768211457n,340282366920938463463374607431768211458n));", "true false\n"];
        yield return ["negative", "console.log(Object.is(-31n,BigInt('-31')),Object.is(-31n,31n));", "true false\n"];
        yield return ["zero", "console.log(Object.is(0n,-0n),Object.is(BigInt('0'),0n));", "true true\n"];
        yield return ["mixed-primitives", "console.log(Object.is(31n,31),Object.is(31,31n),Object.is(31n,'31'),Object.is(31n,true),Object.is(0n,null),Object.is(0n,undefined));", "false false false false false false\n"];
        yield return ["held-values", "const first:any=BigInt('31'),second:any=BigInt('31');const values=[first,second];function read(index:number):any{return values[index];}console.log(Object.is(read(0),read(1)),Object.is(first,second));", "true true\n"];
        yield return ["object-identity", "const first={value:31n},second={value:31n};console.log(Object.is(first,first),Object.is(first,second),Object.is(first,31n));", "true false false\n"];
        yield return ["boxed-bigint-identity", "const first=Object(31n),second=Object(31n);console.log(Object.is(first,first),Object.is(first,second),Object.is(first,31n),Object.is(31n,first));", "true false false false\n"];
        yield return ["no-coercion", "let calls=0;const value:any={[Symbol.toPrimitive](){calls++;return 31n;}};console.log(Object.is(value,31n),Object.is(31n,value),calls);", "false false 0\n"];
        yield return ["frozen-descriptor-same-value", "const value:any={};Object.defineProperty(value,'slot',{value:31n,writable:false,configurable:false});console.log(Reflect.defineProperty(value,'slot',{value:BigInt('31')}),Reflect.defineProperty(value,'slot',{value:32n}),Object.is(value.slot,31n));", "true false true\n"];
        yield return ["ordinary-number-control", "console.log(Object.is(NaN,NaN),Object.is(0,-0),Object.is(-0,-0),Object.is('same','same'),Object.is(true,true),Object.is(null,undefined));", "true false true true true false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void BigIntsCompareByValueWithoutCoercion(string name, string source, string expected, ExecutionMode mode)
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
}
