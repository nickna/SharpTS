using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

// The emitted intrinsic population order is independent of interpreter inventory dispatch.
[Collection("CompilationService")]
public sealed class NumberPrototypeCreationOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            "number-complete-intrinsic-order",
            "const p:any=Number.prototype;console.log(Reflect.ownKeys(p).join(','),Object.keys(p).length);\n",
            "constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString 0\n"
        };
        yield return new object[]
        {
            "number-integer-string-symbol-order",
            "const p:any=Number.prototype;const a=Symbol('a'),b=Symbol('b');p.b=1;p[10]=2;p.a=3;p[2]=4;p[a]=5;p[b]=6;let result='';for(const k of Reflect.ownKeys(p)){result+=(typeof k==='symbol'?(k===a?'s1':'s2'):k)+',';}console.log(result);\n",
            "2,10,constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString,b,a,s1,s2,\n"
        };
        yield return new object[]
        {
            "number-deletion-and-recreation-order",
            "const p:any=Number.prototype;delete p.constructor;delete p.toString;p.constructor=9;p.toString=7;console.log(Reflect.ownKeys(p).join(','),Object.getOwnPropertyDescriptor(p,'constructor')!.enumerable,Object.getOwnPropertyDescriptor(p,'toString')!.enumerable);\n",
            "toExponential,toFixed,toPrecision,valueOf,toLocaleString,constructor,toString true true\n"
        };
        yield return new object[]
        {
            "own-property-names-order",
            "console.log(Object.getOwnPropertyNames(Number.prototype).join(','));\n",
            "constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString\n"
        };
        yield return new object[]
        {
            "plural-descriptor-order",
            "console.log(Reflect.ownKeys(Object.getOwnPropertyDescriptors(Number.prototype)).join(','));\n",
            "constructor,toExponential,toFixed,toPrecision,toString,valueOf,toLocaleString\n"
        };
        yield return new object[]
        {
            "method-identity-and-attributes",
            "const p:any=Number.prototype;const f=p.toExponential,g=p.valueOf;let attrs=true;for(const name of Object.getOwnPropertyNames(p)){const d=Object.getOwnPropertyDescriptor(p,name)!;attrs=attrs&&d.writable===true&&d.enumerable===false&&d.configurable===true}console.log(f===p.toExponential,g===p.valueOf,f.name,f.length,g.name,g.length,attrs);\n",
            "true true toExponential 1 valueOf 0 true\n"
        };
        yield return new object[]
        {
            "helpers-retain-behavior",
            "const p:any=Number.prototype;console.log(p.toExponential.call(12,1),p.toFixed.call(1.5,0),p.toPrecision.call(1.5,2),p.valueOf.call(3));\n",
            "1.2e+1 2 1.5 3\n"
        };
        yield return new object[]
        {
            "boolean-order-control",
            "console.log(Reflect.ownKeys(Boolean.prototype).join(','));\n",
            "constructor,toString,valueOf\n"
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EmittedIntrinsicKeysKeepCreationOrder(string name, string source, string expected)
    {
        _ = name;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
