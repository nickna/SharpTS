using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StringPrototypeLengthWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-length-strict-write", "'use strict';const p:any=String.prototype;let threw=false;try{p.length=7}catch(e){threw=e instanceof TypeError}console.log(threw,p.length);", "true 0\n"];
        yield return ["string-length-strict-index", "'use strict';const p:any=String.prototype;let threw=false;try{p['length']=7}catch(e){threw=e instanceof TypeError}console.log(threw,p.length);", "true 0\n"];
        yield return ["string-length-sloppy-write", "const p:any=String.prototype;const result=p.length=7;console.log(result,p.length);", "7 0\n"];
        yield return ["string-length-writable-control", "'use strict';const p:any=String.prototype;Object.defineProperty(p,'probe',{value:3,writable:true,configurable:true});p.probe=7;console.log(p.probe);", "7\n"];
        yield return ["string-length-compound-refusal", "'use strict';const p:any=String.prototype;let threw=false;try{p['length']+=1}catch(e){threw=e instanceof TypeError}console.log(threw,p.length);", "true 0\n"];
        yield return ["string-length-logical-order", "'use strict';const p:any=String.prototype;let calls=0,threw=false;function rhs(){calls++;return 7}try{p.length||=rhs()}catch(e){threw=e instanceof TypeError}p.length&&=rhs();console.log(threw,p.length,calls);", "true 0 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void WritesRespectTheCurrentOwnStringPrototypeDescriptor(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
