using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayNoncanonicalStringWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["minimal-negative-zero-original", "const a:any=[9];a['-0']=2;console.log(a[0],a['-0']);", "9 2\n"];
        yield return ["ordinary-array-brand-original", "const a:any=[9];a['-0']=2;a['0.5']=3;a['NaN']=4;console.log(a[0],a['-0'],a['0.5'],a['NaN']);", "9 2 3 4\n"];
        yield return ["sloppy-noncanonical-spellings", "const a:any=[9],keys=['-0','00','01','+0','+1',' 0','0 ','1.0','1e0','0x0'];for(let i=0;i<keys.length;i++)a[keys[i]]=i+20;console.log(a[0],a.length,keys.map(k=>a[k]).join(','));", "9 1 20,21,22,23,24,25,26,27,28,29\n"];
        yield return ["sloppy-minus-zero-compound", "const a:any=[9];Object.defineProperty(a,'-0',{value:2,writable:true,configurable:true});console.log(a['-0']+=3,a['-0'],a[0],a.length);", "5 5 9 1\n"];
        yield return ["sloppy-named-setter-receiver", "const a:any=[9];let value=0,calls=0,receiver=false;Object.defineProperty(a,'-0',{set(v:any){value=v;calls++;receiver=this===a},configurable:true});a['-0']=5;console.log(value,calls,receiver,a[0],a.length);", "5 1 true 9 1\n"];
        yield return ["sloppy-named-readonly", "const a:any=[9];Object.defineProperty(a,'-0',{value:2,writable:false});try{a['-0']=5;console.log('returned')}catch(e){console.log(e instanceof TypeError)}console.log(a['-0'],a[0],a.length);", "returned\n2 9 1\n"];
        yield return ["sloppy-nonextensible-new-name", "const a:any=[9];Object.preventExtensions(a);try{a['-0']=5;console.log('returned')}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,'-0'),a[0],a.length);", "returned\nfalse 9 1\n"];
        yield return ["strict-noncanonical-spellings", "'use strict';const a:any=[9],keys=['-0','00','01','+0','+1',' 0','0 ','1.0','1e0','0x0'];for(let i=0;i<keys.length;i++)a[keys[i]]=i+20;console.log(a[0],a.length,keys.map(k=>a[k]).join(','));", "9 1 20,21,22,23,24,25,26,27,28,29\n"];
        yield return ["strict-minus-zero-compound", "'use strict';const a:any=[9];Object.defineProperty(a,'-0',{value:2,writable:true,configurable:true});console.log(a['-0']+=3,a['-0'],a[0],a.length);", "5 5 9 1\n"];
        yield return ["strict-named-setter-receiver", "'use strict';const a:any=[9];let value=0,calls=0,receiver=false;Object.defineProperty(a,'-0',{set(v:any){value=v;calls++;receiver=this===a},configurable:true});a['-0']=5;console.log(value,calls,receiver,a[0],a.length);", "5 1 true 9 1\n"];
        yield return ["strict-named-readonly", "'use strict';const a:any=[9];Object.defineProperty(a,'-0',{value:2,writable:false});try{a['-0']=5;console.log('returned')}catch(e){console.log(e instanceof TypeError)}console.log(a['-0'],a[0],a.length);", "true\n2 9 1\n"];
        yield return ["strict-nonextensible-new-name", "'use strict';const a:any=[9];Object.preventExtensions(a);try{a['-0']=5;console.log('returned')}catch(e){console.log(e instanceof TypeError)}console.log(Object.hasOwn(a,'-0'),a[0],a.length);", "true\nfalse 9 1\n"];
        yield return ["numeric-negative-zero-is-element-zero", "const a:any=[9];const key:any=-0;a[key]=5;console.log(a[0],a.length,Object.hasOwn(a,'-0'));", "5 1 false\n"];
        yield return ["strict-numeric-negative-zero-is-element-zero", "'use strict';const a:any=[9];const key:any=-0;a[key]=5;console.log(a[0],a.length,Object.hasOwn(a,'-0'));", "5 1 false\n"];
        yield return ["raw-key-captured-once-converted-per-phase", "const a:any=[9];Object.defineProperty(a,'-0',{value:2,writable:true});let targets=0,keys=0,coercions=0,rights=0;function target(){targets++;return a}function key():any{keys++;return {[Symbol.toPrimitive](hint:string){coercions++;return '-0'}}}function rhs(){rights++;return 3}const value=target()[key()]+=rhs();console.log(value,a['-0'],a[0],targets,keys,coercions,rights);", "5 5 9 1 1 2 1\n"];
        yield return ["strict-key-reconversion-minus-zero-to-element", "'use strict';const a:any=[9];Object.defineProperty(a,'-0',{value:2,writable:true});let calls=0;const key:any={[Symbol.toPrimitive](){return ++calls===1?'-0':'0'}};console.log(a[key]+=3,a['-0'],a[0],calls);", "5 2 5 2\n"];
        yield return ["key-abrupt-before-write", "const a:any=[9],marker={};let calls=0;const key:any={[Symbol.toPrimitive](){calls++;throw marker}};try{a[key]=5}catch(e){console.log(e===marker)}console.log(a[0],a.length,calls);", "true\n9 1 1\n"];
        yield return ["strict-inherited-named-setter", "'use strict';const a:any=[9],proto=Object.create(Object.getPrototypeOf(a));let value=0,calls=0,receiver=false;Object.defineProperty(proto,'-0',{set(v:any){value=v;calls++;receiver=this===a}});Object.setPrototypeOf(a,proto);a['-0']=5;console.log(value,calls,receiver,Object.hasOwn(a,'-0'),a[0]);", "5 1 true false 9\n"];
        yield return ["strict-max-valid-index-and-max-length-name", "'use strict';const a:any=[];a['4294967294']=7;a['4294967295']=8;console.log(a.length,a['4294967294'],a['4294967295'],Object.hasOwn(a,'0'));", "4294967295 7 8 false\n"];
        yield return ["sloppy-large-canonical-index", "const a:any=[];a['2147483648']=7;a['02147483648']=8;console.log(a.length,a['2147483648'],a['02147483648'],Object.hasOwn(a,'0'));", "2147483649 7 8 false\n"];
        yield return ["strict-symbol-and-ordinary-object-controls", "'use strict';const a:any=[9],s=Symbol('s');a[s]=7;const o:any={0:9};o['-0']=5;console.log(a[s],a[0],o[0],o['-0']);", "7 9 9 5\n"];
        yield return ["strict-list-spellings-negative-fractional-nan", "'use strict';const a:any=[9];a['-1']=2;a['0.5']=3;a['NaN']=4;a['Infinity']=5;console.log(a[0],a.length,a['-1'],a['0.5'],a['NaN'],a['Infinity']);", "9 1 2 3 4 5\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void NamedStringKeysDoNotAliasPhysicalArrayElements(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
