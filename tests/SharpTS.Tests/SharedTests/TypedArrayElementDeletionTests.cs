using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayElementDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sloppy-index", "const a:any=new Uint8Array([7]);console.log(delete a[0],a[0],Object.hasOwn(a,'0'));", "false 7 true\n"];
        yield return ["strict-index", "'use strict';const a:any=new Uint8Array([7]);let caught=false;try{delete a[0]}catch(e){caught=e instanceof TypeError}console.log(caught,a[0],Object.hasOwn(a,'0'));", "true 7 true\n"];
        yield return ["string-index", "const a:any=new Uint8Array([7]);console.log(delete a['0'],a[0]);", "false 7\n"];
        yield return ["reflect-index", "'use strict';const a:any=new Uint8Array([7]);console.log(Reflect.deleteProperty(a,'0'),a[0]);", "false 7\n"];
        yield return ["absent-indices", "const a:any=new Uint8Array(1);console.log(delete a[1],delete a['-0'],delete a['-1'],delete a['1.5'],delete a['NaN'],delete a['Infinity'],Reflect.deleteProperty(a,'4294967295'));", "true true true true true true true\n"];
        yield return ["ordinary-name", "const a:any=new Uint8Array(1);Object.defineProperty(a,'01',{value:7,configurable:true});console.log(delete a['01'],Object.hasOwn(a,'01'),Object.hasOwn(a,'0'));", "true false true\n"];
        yield return ["empty-array", "const a:any=new Uint8Array(0);console.log(delete a[0],Reflect.deleteProperty(a,'0'));", "true true\n"];
        yield return ["readonly-name-descriptor-companion", "const a:any=new Uint8Array(1);Object.defineProperty(a,'01',{value:7});console.log(delete a['01'],Reflect.deleteProperty(a,'01'),Object.getOwnPropertyDescriptor(a,'01')?.value);", "false false 7\n"];
        yield return ["physical-kind-Int8", "const a:any=new Int8Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Uint8", "const a:any=new Uint8Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Uint8Clamped", "const a:any=new Uint8ClampedArray(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Int16", "const a:any=new Int16Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Uint16", "const a:any=new Uint16Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Int32", "const a:any=new Int32Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Uint32", "const a:any=new Uint32Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Float32", "const a:any=new Float32Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-Float64", "const a:any=new Float64Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-BigInt64", "const a:any=new BigInt64Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["physical-kind-BigUint64", "const a:any=new BigUint64Array(1);const loose=delete a[0];let caught=false;(function(){'use strict';try{delete a[0]}catch(e){caught=e instanceof TypeError}})();console.log(loose,caught,Reflect.deleteProperty(a,'0'),Object.hasOwn(a,'0'));", "false true false true\n"];
        yield return ["length-getter-not-read", "const a:any=new Uint8Array(2);let gets=0;Object.defineProperty(a,'length',{get(){gets++;throw 9}});console.log(delete a[1],Reflect.deleteProperty(a,'1'),gets);", "false false 0\n"];
        yield return ["physical-subview", "const a:any=new Uint8Array(new ArrayBuffer(8),3,2);console.log(Reflect.deleteProperty(a,'1'),Reflect.deleteProperty(a,'2'),Object.keys(a).join(','));", "false true 0,1\n"];
        yield return ["ordinary-array-control", "const a=[7];console.log(delete a[0],Object.hasOwn(a,'0'),a.length);", "true false 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Interpreted(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Theory, MemberData(nameof(Cases))]
    public void Compiled(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
