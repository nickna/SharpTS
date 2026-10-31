using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ClrStaticNumericPropertyTests
{
    [Theory, ModeData]
    public void StaticPropertiesUseGuestNumericRepresentation(ExecutionMode mode)
    {
        var source = """
            import { Environment } from "dotnet:System.Environment";
            import { Version } from "dotnet:System.Version";
            const directStatic: any = Environment.currentManagedThreadId;
            const instance: any = new Version(1, 2).major;
            console.log(typeof directStatic, typeof instance);
            console.log(directStatic > 0, directStatic === +directStatic, instance === 1);
            console.log(typeof Environment.newLine);
            """;
        Assert.Equal("number number\ntrue true true\nstring\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["./main.ts"] = source }, "./main.ts", mode));
    }
}
