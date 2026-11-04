using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3DenseNumericArrayUpdateTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["numeric-results", "const a=[-0,3,NaN,Infinity];console.log(Object.is(a[0]++,-0),++a[1],a[1]--,--a[1],a[0],Number.isNaN(a[2]++),++a[3]);", "true 4 4 2 1 true Infinity\n"];
        yield return ["integrity", "const a=Object.freeze([3]);console.log(a[0]++,++a[0],a[0]);function strict(){'use strict';try{a[0]++}catch(error){console.log(error instanceof TypeError)}}strict();const b=Object.seal([4]);console.log(++b[0],b[0]);", "3 4 3\ntrue\n5 5\n"];
        yield return ["own-accessor", "const a=[3];let value=5,gets=0,sets=0;Object.defineProperty(a,'0',{get(){gets++;return value},set(v){sets++;value=v}});console.log(a[0]++,++a[0],value,gets,sets);", "5 7 7 2 2\n"];
        yield return ["hole-and-prototype", "const a=[,];let value=3,gets=0,sets=0;const p=Object.create(Array.prototype);Object.defineProperty(p,'0',{get(){gets++;return value},set(v){sets++;value=v}});Object.setPrototypeOf(a,p);console.log(a[0]++,++a[0],value,gets,sets,Object.hasOwn(a,'0'));", "3 5 5 2 2 false\n"];
        yield return ["conversion-mutates-write-descriptor", "const a=[{}];let writes=0;const value={valueOf(){Object.defineProperty(a,'0',{set(v){writes=v},configurable:true});return 3}};a[0]=value;console.log(a[0]++,writes,a[0]===undefined);", "3 4 true\n"];
        yield return ["bigint-and-object-keys", "const a=[1n];let keys=0;const key={[Symbol.toPrimitive](){keys++;return '0'}};console.log(String(a[key]++),String(++a[0]),String(a[0]),keys);", "1 3 3 2\n"];
        yield return ["strict-unmapped-arguments", "function run(value){'use strict';console.log(arguments[0]++,value,++arguments[0],value)}run(3);", "3 3 5 3\n"];
        yield return ["awaited-reference", "async function run(){const a=[3];console.log((await Promise.resolve(a))[await Promise.resolve(0)]++,++a[0],a[0])}run();", "3 5 5\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void NumericUpdatesPreserveReferenceAndFallbackSemantics(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }
}
