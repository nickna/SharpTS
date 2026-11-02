using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayCanonicalStringWriteInterpreterTests
{
    // Interpreter regression; compiler key-write behavior is independently tracked by #3416.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["leading-zero-write-minimal", "const a:any=[9];a['00']=5;console.log(a[0],a['00'],a.length);", "9 5 1\n"];
        yield return ["leading-zero-one", "const a:any=[9,8];a['01']=5;console.log(a[1],a['01'],a.length);", "8 5 2\n"];
        yield return ["long-leading-zero-name", "const a:any=[9];a['04294967294']=5;console.log(a[0],a['04294967294'],a.length);", "9 5 1\n"];
        yield return ["canonical-string-index", "const a:any=[9];a['1']=5;console.log(a[0],a[1],a.length);", "9 5 2\n"];
        yield return ["uint32-limit-name", "const a:any=[9];a['4294967295']=5;console.log(a[0],a['4294967295'],a.length);", "9 5 1\n"];
        yield return ["numeric-negative-zero", "const a:any=[9];a[-0]=5;console.log(a[0],a.length);", "5 1\n"];
        yield return ["object-key-leading-zero", "const a:any=[9];let calls=0;const key:any={toString(){calls++;return '00'}};a[key]=5;console.log(a[0],a['00'],a.length,calls);", "9 5 1 1\n"];
        yield return ["symbol-control", "const a:any=[9],s=Symbol('00');a[s]=5;console.log(a[0],a[s],a.length);", "9 5 1\n"];
        yield return ["leading-zero-own-descriptor", "const a:any=[9];Object.defineProperty(a,'00',{value:3,writable:true,configurable:true});a['00']=5;console.log(a[0],a['00'],Object.getOwnPropertyDescriptor(a,'00')!.value,a.length);", "9 5 5 1\n"];
        yield return ["inherited-leading-zero-setter", "const a:any=[9],p=Object.create(Array.prototype);let value=0,receiver=false;Object.defineProperty(p,'00',{set(v:any){value=v;receiver=this===a}});Object.setPrototypeOf(a,p);a['00']=5;console.log(a[0],value,receiver,Object.hasOwn(a,'00'),a.length);", "9 5 true false 1\n"];
        yield return ["nonextensible-leading-zero-sloppy", "const a:any=[9];Object.preventExtensions(a);a['00']=5;console.log(a[0],Object.hasOwn(a,'00'),a.length);", "9 false 1\n"];
        yield return ["readonly-leading-zero-strict", "'use strict';const a:any=[9];Object.defineProperty(a,'00',{value:3,writable:false});try{a['00']=5}catch(e){console.log(e instanceof TypeError)}console.log(a[0],a['00'],a.length);", "true\n9 3 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void KeepsTheOriginalNamedProperty(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
