using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class ExternalGenericMetadataRuntimeTests
{
    public static IEnumerable<object[]> Controls()
    {
        yield return ["generic-method", "static Identity<T>(value:T):T;", "Api.Identity<string>(\"generic\")", "generic\n"];
        yield return ["inferred-method", "static Identity<T>(value:T):T;", "Api.Identity(\"inferred\")", "inferred\n"];
        yield return ["generic-array", "static First<T>(value:T[]):T;", "Api.First<string>([\"array\",\"b\"])", "array\n"];
        yield return ["generic-params", "static Many<T>(...value:T[]):T;", "Api.Many<string>(\"params\",\"b\")", "params\n"];
        yield return ["generic-constraint", "static Comparable<T>(value:T):T;", "Api.Comparable<string>(\"constrained\")", "constrained\n"];
    }

    [Theory]
    [MemberData(nameof(Controls))]
    public void CoreTypeArgumentsVerifyAndExecute(string control, string declaration, string expression, string expected)
    {
        new ExternalSignatureRuntimeTests().OrdinaryExplicitReferenceCallsVerifyAndExecute(control,
            "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { " + declaration + " } console.log(" + expression + ");", expected);
    }
    [Fact]
    public void ConstructedOwnerConstraintKeepsItsActualStringContext()
    {
        new ExternalSignatureRuntimeTests().OrdinaryExplicitReferenceCallsVerifyAndExecute("closed-owner-generic",
            "@DotNetType(\"ExternalSignatureFixture.Box<string>\") declare class Box { constructor(v:string); Constrained<T>(v:T):T; } const box=new Box(\"first\"); console.log(box.Constrained<string>(\"owner\"));", "owner\n");
    }

    [Theory]
    [InlineData("ClassOnly<number>(3)")]
    [InlineData("StructOnly<string>(\"bad\")")]
    [InlineData("NewOnly<string>(\"bad\")")]
    [InlineData("Comparable<object>({})")]
    public void InvalidGenericConstraintsFailBeforeSaving(string expression)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "@DotNetType(\"ExternalSignatureFixture.Api\") declare class Api { static ClassOnly<T>(v:T):T; static StructOnly<T>(v:T):T; static NewOnly<T>(v:T):T; static Comparable<T>(v:T):T; } console.log(Api." + expression + ");");
        var fixture = Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
        var compiled = CliTestHelper.RunCli($"--no-tsconfig --noLib --compile main.ts --ref-asm --verify -o out.dll -r \"{fixture}\"", directory.Path);
        Assert.NotEqual(0, compiled.ExitCode);
        Assert.Contains("No generic instantiation", compiled.StandardOutput + compiled.StandardError);
        Assert.False(File.Exists(directory.GetPath("out.dll")));
    }
}
