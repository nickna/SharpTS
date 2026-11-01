using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public sealed class TypedArrayRawNumericIndexTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["fractional-compound-original", "const a: any = new Uint8Array([9]);\nconsole.log(a[0.5] += 1);\nconsole.log(a[0], a[0.5]);\n", "NaN\n9 undefined\n"];
        yield return ["negative-fraction", "const a:any=new Uint8Array([9]),k:any=-0.5;console.log(a[k],a[k]=5,a[0],a[k]);console.log(a[k]+=1,a[0],a[k]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
        yield return ["nan", "const a:any=new Uint8Array([9]),k:any=NaN;console.log(a[k],a[k]=5,a[0],a[k]);console.log(a[k]+=1,a[0],a[k]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
        yield return ["positive-infinity", "const a:any=new Uint8Array([9]),k:any=Infinity;console.log(a[k],a[k]=5,a[0],a[k]);console.log(a[k]+=1,a[0],a[k]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
        yield return ["negative-infinity", "const a:any=new Uint8Array([9]),k:any=-Infinity;console.log(a[k],a[k]=5,a[0],a[k]);console.log(a[k]+=1,a[0],a[k]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
        yield return ["uint32-overflow", "const a:any=new Uint8Array([9]),k:any=4294967296;console.log(a[k],a[k]=5,a[0],a[k]);console.log(a[k]+=1,a[0],a[k]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
        yield return ["int32-overflow", "const a:any=new Uint8Array([9]),k:any=2147483648;console.log(a[k],a[k]=5,a[0],a[k]);console.log(a[k]+=1,a[0],a[k]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
        yield return ["zero", "const a:any=new Uint8Array([9,3]),k:any=0;console.log(a[k],a[k]+=2,a[k]);", "9 11 11\n"];
        yield return ["numeric-negative-zero", "const a:any=new Uint8Array([9,3]),k:any=-0;console.log(a[k],a[k]+=2,a[k]);", "9 11 11\n"];
        yield return ["last-index", "const a:any=new Uint8Array([9,3]),k:any=1;console.log(a[k],a[k]+=2,a[k]);", "3 5 5\n"];
        yield return ["prefix-invalid", "const a:any=new Uint8Array([9]);console.log(++a[0.5],a[0],a[0.5]);", "NaN 9 undefined\n"];
        yield return ["postfix-invalid", "const a:any=new Uint8Array([9]);console.log(a[0.5]++,a[0],a[0.5]);", "NaN 9 undefined\n"];
        yield return ["invalid-write-converts-once", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 5}};console.log((a[0.5]=value)===value,calls,a[0],a[0.5]);", "true 1 9 undefined\n"];
        yield return ["invalid-compound-rhs-order", "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;console.log(\"coerce\");return 5}};console.log(a[0.5]+=value,calls,a[0]);", "coerce\nNaN 1 9\n"];
        yield return ["bigint-invalid-write-converts-once", "const a:any=new BigInt64Array([9n]);let calls=0;const value:any={valueOf(){calls++;return 5n}};console.log((a[0.5]=value)===value,calls,String(a[0]),a[0.5]);", "true 1 9 undefined\n"];
        yield return ["invalid-write-symbol-abrupt", "const a:any=new Uint8Array([9]);try{a[0.5]=Symbol(\"x\")}catch(e){console.log(e instanceof TypeError)}console.log(a[0],a[0.5]);", "true\n9 undefined\n"];
        yield return ["async-reference-phases", "async function main(){const a:any=new Uint8Array([9]);console.log(a[await Promise.resolve(0.5)]);console.log(a[0.5]+=await Promise.resolve(2));console.log(a[0],a[0.5]);}main();", "undefined\nNaN\n9 undefined\n"];
        yield return ["ordinary-array-control", "const a:any=[9];a[0.5]=5;console.log(a[0.5]+=2,a[0],a.length);", "7 9 1\n"];
        yield return ["strict-invalid-write", "\"use strict\";const a:any=new Uint8Array([9]);console.log(a[0.5]=5,a[0],a[0.5]);", "5 9 undefined\n"];
        yield return ["out-of-bounds-integer", "const a:any=new Uint8Array([9]);console.log(a[1],a[1]=5,a[0],a[1]);console.log(a[1]+=1,a[0],a[1]);", "undefined 5 9 undefined\nNaN 9 undefined\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NumericReferencesValidateBeforeStorageAccess(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
