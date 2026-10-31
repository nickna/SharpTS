using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StrictObjectDescriptorValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["strict-named", "'use strict';const target:any={x:1,get marker(){return 0}};target.x=7;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "7 7\n"];
        yield return ["strict-numeric", "'use strict';const target:any={0:3,get length(){return 1}};target[0]=7;console.log(target[0],Object.getOwnPropertyDescriptor(target,'0')!.value);", "7 7\n"];
        yield return ["strict-compound", "'use strict';const target:any={x:1,get marker(){return 0}};target.x+=2;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "3 3\n"];
        yield return ["strict-postfix", "'use strict';const target:any={x:1,get marker(){return 0}};const before=target.x++;console.log(before,target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "1 2 2\n"];
        yield return ["strict-function-parameter", "function write(target:any){'use strict';target.x=7;}const target:any={x:1,get marker(){return 0}};write(target);console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "7 7\n"];
        yield return ["strict-readonly-refusal", "'use strict';const target:any={x:1,get marker(){return 0}};Object.defineProperty(target,'x',{writable:false});try{target.x=7}catch(error){console.log(error instanceof TypeError)}console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "true\n1 1\n"];
        yield return ["strict-accessor-setter", "'use strict';let value=1,calls=0;const target:any={get x(){return value},set x(next:number){calls++;value=next}};target.x=7;console.log(target.x,calls,'value' in Object.getOwnPropertyDescriptor(target,'x')!);", "7 1 false\n"];
        yield return ["strict-frozen-accessor-setter", "'use strict';let value=1,calls=0;const target:any={get x(){return value},set x(next:number){calls++;value=next}};Object.freeze(target);target.x=7;console.log(target.x,calls,Object.getOwnPropertyDescriptor(target,'x')!.configurable);", "7 1 false\n"];
        yield return ["strict-sealed-existing-data", "'use strict';const target:any={x:1,get marker(){return 0}};Object.seal(target);target.x=7;const d:any=Object.getOwnPropertyDescriptor(target,'x');console.log(target.x,d.value,d.writable,d.enumerable,d.configurable);", "7 7 true true false\n"];
        yield return ["strict-getter-only-refusal", "'use strict';const target:any={get x(){return 1}};try{target.x=7}catch(error){console.log(error instanceof TypeError)}console.log(target.x,'value' in Object.getOwnPropertyDescriptor(target,'x')!);", "true\n1 false\n"];
        yield return ["sloppy-control", "const target:any={x:1,get marker(){return 0}};target.x=7;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "7 7\n"];
        yield return ["generic-unshift-existing-data", "const target:any={0:3,length:1,get marker(){return 0}};console.log(Array.prototype.unshift.call(target,7),target[0],target[1],target.length,Object.getOwnPropertyDescriptor(target,'0')!.value);", "2 7 3 2 7\n"];
        yield return ["strict-plain-dictionary-control", "'use strict';const target:any={x:1};target.x=7;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "7 7\n"];
        yield return ["unshift-getter-only-refusal", "const f:any=Array.prototype.unshift;let log='';const target:any={get length(){log+='L';return 1},0:3};try{f.call(target,7)}catch(e){console.log(e instanceof TypeError,log,target[0],target[1])}", "true L 7 3\n"];
        yield return ["unshift-throwing-setter", "const f:any=Array.prototype.unshift;let log='';const token:any={};const target:any={get length(){log+='L';return 1},set length(v:number){log+='N'+v;throw token},0:3};try{f.call(target,7)}catch(e){console.log(e===token,log,target[0],target[1])}", "true LN2 7 3\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void StrictWritesKeepOwnDataDescriptorsCurrent(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
