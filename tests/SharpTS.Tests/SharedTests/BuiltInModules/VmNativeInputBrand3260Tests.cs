using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmNativeInputBrand3260Tests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["array-string-tag", "import * as vm from 'node:vm';const value:any=[1];value[Symbol.toStringTag]='Custom';console.log(vm.runInNewContext('Object.prototype.toString.call(value)',{value}));", "[object Custom]\n"];
        yield return ["array-tag-getter-once", "import * as vm from 'node:vm';const value:any=[1];let calls=0;Object.defineProperty(value,Symbol.toStringTag,{get(){calls++;console.log(this===value);return 'Custom';},configurable:true});console.log(vm.runInNewContext('Object.prototype.toString.call(value)',{value}),calls);", "true\n[object Custom] 1\n"];
        yield return ["nonstring-array-tag", "import * as vm from 'node:vm';const value:any=[1];value[Symbol.toStringTag]=17;console.log(vm.runInNewContext('Object.prototype.toString.call(value)',{value}));", "[object Array]\n"];
        yield return ["nonstring-error-tag", "import * as vm from 'node:vm';const value:any=new Error('x');value[Symbol.toStringTag]=17;console.log(vm.runInNewContext('Object.prototype.toString.call(value)',{value}));", "[object Error]\n"];
        yield return ["tag-getter-abrupt", "import * as vm from 'node:vm';const value:any=[1];const marker={};let calls=0;Object.defineProperty(value,Symbol.toStringTag,{get(){calls++;throw marker;},configurable:true});try{vm.runInNewContext('Object.prototype.toString.call(value)',{value});}catch(error){console.log(error===marker,calls);}", "true 1\n"];
        yield return ["error", "import * as vm from 'node:vm';const value:any=new Error('outside');console.log(vm.runInNewContext('[typeof value,value instanceof Error,value instanceof TypeError,Array.isArray(value),String(value),Object.prototype.toString.call(value)].join(\"|\")',{value}));", "object|false|false|false|Error: outside|[object Error]\n"];
        yield return ["typeerror", "import * as vm from 'node:vm';const value:any=new TypeError('outside');console.log(vm.runInNewContext('[typeof value,value instanceof Error,value instanceof TypeError,Array.isArray(value),String(value),Object.prototype.toString.call(value)].join(\"|\")',{value}));", "object|false|false|false|TypeError: outside|[object Error]\n"];
        yield return ["array", "import * as vm from 'node:vm';const value:any=[1,2];console.log(vm.runInNewContext('[typeof value,value instanceof Error,value instanceof TypeError,Array.isArray(value),String(value),Object.prototype.toString.call(value)].join(\"|\")',{value}));", "object|false|false|true|1,2|[object Array]\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesActualInputBrandsAndTags(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
