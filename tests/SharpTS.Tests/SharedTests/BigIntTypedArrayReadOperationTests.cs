using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BigIntTypedArrayReadOperationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["bigint-missing-read-add", "const a=new BigInt64Array([9n]);try{console.log(a[1]+1n)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["biguint-missing-read-multiply", "const a=new BigUint64Array([9n]);try{console.log(a[1]*1n)}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["valid-typed-bigint", "const a=new BigInt64Array([9n]);console.log(String(a[0]+1n),String(a[0]*2n),a[0]===9n);", "10 18 true\n"];
        yield return ["valid-typed-biguint", "const a=new BigUint64Array([9n]);console.log(String(a[0]-1n),String(a[0]>>1n),a[0]===9n);", "8 4 true\n"];
        yield return ["missing-right-operand", "const a=new BigInt64Array([9n]);try{console.log(1n+a[1])}catch(e){console.log(e instanceof TypeError)}", "true\n"];
        yield return ["missing-divisor-and-valid-zero-error", "const a=new BigInt64Array([9n]);try{console.log(1n/a[1])}catch(e){console.log(e instanceof TypeError)}try{console.log(a[0]/0n)}catch(e){console.log(e instanceof RangeError)}", "true\ntrue\n"];
        yield return ["written-any-control", "const a:any=new BigInt64Array([9n]);console.log(String(a[0]+1n));try{console.log(a[1]+1n)}catch(e){console.log(e instanceof TypeError)}", "10\ntrue\n"];
        yield return ["local-missing-value", "const a=new BigInt64Array([9n]);const value=a[1];console.log(typeof value,value===undefined);try{console.log(value+1n)}catch(e){console.log(e instanceof TypeError)}", "undefined true\ntrue\n"];
        yield return ["returned-missing-observation", "function read():bigint{const a=new BigInt64Array([9n]);return a[1]}console.log(typeof read(),read()===undefined);", "undefined true\n"];
        yield return ["typed-argument-consumer", "function add(value:bigint):bigint{return value+1n}const a=new BigInt64Array([9n]);console.log(String(add(a[0])));try{console.log(add(a[1]))}catch(e){console.log(e instanceof TypeError)}", "10\ntrue\n"];
        yield return ["typed-forwarding-consumer", "function forward(value:bigint):bigint{return value}const a=new BigUint64Array([9n]);console.log(String(forward(a[0])*2n));try{console.log(forward(a[1])*1n)}catch(e){console.log(e instanceof TypeError)}", "18\ntrue\n"];
        yield return ["typed-argument-observation", "function observe(value:bigint){console.log(typeof value,value===undefined)}const a=new BigUint64Array([9n]);observe(a[0]);observe(a[1]);", "bigint false\nundefined true\n"];
        yield return ["index-once-before-rhs", "const a=new BigInt64Array([9n]);let log=\"\";function index():number{log+=\"i\";return 1}function rhs():bigint{log+=\"r\";return 1n}try{console.log(a[index()]+rhs())}catch(e){console.log(e instanceof TypeError,log)}", "true ir\n"];
        yield return ["existing-raw-unary-semantics", "const a=new BigInt64Array([9n]);console.log(String(-a[0]),String(~a[0]),Number.isNaN(-a[1]),String(~a[1]));", "-9 -10 true -1\n"];
        yield return ["number-valid-control", "const a=new Int32Array([9]);function numeric(value:number):number{return value+1}console.log(a[0]+1,numeric(2));", "10 3\n"];
        yield return ["missing-string-concatenation", "const a=new BigInt64Array([9n]);console.log(a[1]+\"x\",\"x\"+a[1]);", "undefinedx xundefined\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void MissingBigIntElementsUseGuestOperations(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
