using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DescriptorWriteFeatureGatingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["helper-replaces-array-prototype-method", "const prototype:any=Array.prototype;function replace(value:any){value.push=function(){return 17}}replace(prototype);const values:any=[];console.log(values.push(7),values.length);", "17 0\n"];
        yield return ["strict-own-data-remains-live", "'use strict';const target:any={x:1,get marker(){return 0}};target.x=7;console.log(target.x,Object.getOwnPropertyDescriptor(target,'x')!.value);", "7 7\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void SelectedDescriptorWritesRetainActualValues(string name, string source, string expected, ExecutionMode mode)
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
