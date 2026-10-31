using System.Globalization;
using System.Numerics;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BigIntPropertyKeyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const obj:any={'0':1},key:any=0n;console.log(obj[key],Object.getOwnPropertyDescriptor(obj,key)?.value);", "1 1\n"];
        yield return ["signed-and-large", "const obj:any={},zero:any=0n,negative:any=-3n,large:any=9007199254740993123456789n;obj[zero]='zero';obj[negative]='negative';obj[large]='large';console.log(obj['0'],obj['-3'],obj['9007199254740993123456789']);console.log(Object.getOwnPropertyDescriptor(obj,negative)?.value);", "zero negative large\nnegative\n"];
        yield return ["descriptor-and-reflect", "const obj:any={},key:any=-2n;Object.defineProperty(obj,key,{value:7,configurable:true});console.log(Object.hasOwn(obj,'-2'),Reflect.get(obj,key),Reflect.getOwnPropertyDescriptor(obj,key)?.value);console.log(Reflect.deleteProperty(obj,key),Object.hasOwn(obj,'-2'));", "true 7 7\ntrue false\n"];
        yield return ["object-to-bigint", "const obj:any={'2':8},events:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){events.push(hint);return 2n;}};console.log(Reflect.get(obj,key),events.join(','));", "8 string\n"];
        yield return ["literal-string-distinct", "const obj:any={},key:any=2n;obj['2n']=1;obj[key]=2;console.log(obj['2'],obj['2n'],Object.keys(obj).join(','));", "2 1 2,2n\n"];
        yield return ["number-alias", "const obj:any={},key:any=0n;obj[-0]=1;console.log(obj[key]);obj[key]=2;console.log(obj[0]);", "1\n2\n"];
        yield return ["symbol-distinct", "const obj:any={},key:any=0n,symbol=Symbol('0');obj[symbol]=3;obj[key]=4;console.log(obj[symbol],obj['0'],Object.getOwnPropertySymbols(obj)[0]===symbol);", "3 4 true\n"];
        yield return ["indexed-delete", "const obj:any={'3':1},key:any=3n;console.log(delete obj[key],Object.hasOwn(obj,'3'),Object.hasOwn(obj,key));", "true false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void UsesDecimalPrimitiveStringsWithoutLiteralSuffixes(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted)
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
    [Fact]
    public void RawPrimitiveRepresentationsPreserveIdentityAndDisplay()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "NEG";
        CultureInfo.CurrentCulture = culture;
        try
        {
            var wrapped = new SharpTSBigInt(new BigInteger(-3));
            string display = wrapped.ToString();
            Assert.Equal("-3", PropertyKeyConverter.ToPropertyKeyString(wrapped));
            Assert.Equal("-3", PropertyKeyConverter.ToPropertyKeyString(new BigInteger(-3)));
            Assert.Equal("0", PropertyKeyConverter.ToPropertyKeyString(new SharpTSBigInt(BigInteger.Zero)));
            Assert.Equal("0", PropertyKeyConverter.ToPropertyKeyString(-0d));
            Assert.Equal("0n", PropertyKeyConverter.ToPropertyKeyString("0n"));
            Assert.Equal("true", PropertyKeyConverter.ToPropertyKeyString(true));
            Assert.Equal("null", PropertyKeyConverter.ToPropertyKeyString(null));
            Assert.Equal("undefined", PropertyKeyConverter.ToPropertyKeyString(SharpTSUndefined.Instance));
            Assert.Same(SharpTSSymbol.Iterator, PropertyKeyConverter.ToPropertyKey(SharpTSSymbol.Iterator));
            Assert.Equal(display, wrapped.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

}
