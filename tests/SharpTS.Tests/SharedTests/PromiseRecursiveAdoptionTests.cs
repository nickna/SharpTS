using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseRecursiveAdoptionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original","const p:any=Promise.resolve(7);Object.defineProperty(p,\"constructor\",{value:{}});Object.defineProperty(p,\"then\",{value:7});const q=Promise.resolve(1).then(()=>p);q.then(value=>console.log(\"before\",value===p));q.finally(()=>{}).then(value=>console.log(\"after\",value===p));","before true\nafter true\n"];
        yield return ["rejected-value","const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{value:7});Promise.reject(p).finally(()=>{}).catch(reason=>console.log(reason===p));","true\n"];
        yield return ["noncallable-finally","const p:any=Promise.resolve(7);Object.defineProperty(p,\"then\",{value:7});const q=Promise.resolve(1).then(()=>p);q.finally(7 as any).then(value=>console.log(value===p));","true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RecursiveResolutionKeepsLiveThenAndFinalValue(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
