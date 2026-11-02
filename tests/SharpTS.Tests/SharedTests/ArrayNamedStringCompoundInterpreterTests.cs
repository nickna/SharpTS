using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayNamedStringCompoundInterpreterTests
{
    // Interpreter regression; compiler key-write behavior is independently tracked by #3416.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-minus-zero-compound-minimal", "const a:any=[9];a['-0']=2;console.log(a['-0']+=3,a['-0'],a[0]);", "5 5 9\n"];
        yield return ["ordinary-string-name", "const a:any=[9];a.tag=3;console.log(a['tag']*=2,a.tag,a[0]);", "6 6 9\n"];
        yield return ["fractional-string-name", "const a:any=[9];a['0.5']=2;console.log(a['0.5']-=1,a['0.5'],a[0]);", "1 1 9\n"];
        yield return ["nan-string-name", "const a:any=[9];a.NaN=2;console.log(a['NaN']+=3,a.NaN,a[0]);", "5 5 9\n"];
        yield return ["negative-string-name", "const a:any=[9];a['-1']=2;console.log(a['-1']*=3,a['-1'],a[0]);", "6 6 9\n"];
        yield return ["get-rhs-put-order-and-receiver", "const a:any=[9],p=Object.create(Array.prototype);const log:string[]=[];let value=2,receiver=true;Object.defineProperty(p,'tag',{get(){log.push('get');receiver=receiver&&this===a;return value},set(v:any){log.push('set');receiver=receiver&&this===a;value=v}});Object.setPrototypeOf(a,p);function rhs(){log.push('rhs');return 3}console.log(a['tag']+=rhs(),value,receiver,Object.hasOwn(a,'tag'),log.join(','),a[0]);", "5 5 true false get,rhs,set 9\n"];
        yield return ["readonly-name-sloppy", "const a:any=[9];let calls=0;Object.defineProperty(a,'tag',{value:2,writable:false});console.log(a['tag']+=++calls,a.tag,calls,a[0]);", "3 2 1 9\n"];
        yield return ["readonly-name-strict", "'use strict';const a:any=[9];let calls=0;Object.defineProperty(a,'tag',{value:2,writable:false});try{a['tag']+=++calls}catch(e){console.log(e instanceof TypeError)}console.log(a.tag,calls,a[0]);", "true\n2 1 9\n"];
        yield return ["nonextensible-existing-name", "const a:any=[9];a.tag=2;Object.preventExtensions(a);console.log(a['tag']+=3,a.tag,a[0]);", "5 5 9\n"];
        yield return ["abrupt-rhs-retains-value", "const a:any=[9];a.tag=2;const marker={};function rhs():any{throw marker}try{a['tag']+=rhs()}catch(e){console.log(e===marker)}console.log(a.tag,a[0]);", "true\n2 9\n"];
        yield return ["canonical-index-control", "const a:any=[9];console.log(a['0']+=2,a[0],a.length);", "11 11 1\n"];
        yield return ["raw-object-key-control", "const a:any=[9];a['-0']=2;const key:any={toString(){return '-0'}};console.log(a[key]+=3,a['-0'],a[0]);", "5 5 9\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void KeepsTheOriginalNamedProperty(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
