using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DotNetImportedStaticMemberTests
{
    [Theory, ModeData]
    public void ImportedClrStaticMethodsAcceptTheirClrAndGuestNames(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./bridge.ts"] = """
                import { Math as NativeMath } from "dotnet:System";
                export function magnitude(value: number): number { return NativeMath.Abs(value); }
                export function maximum(a: number, b: number): number { return NativeMath.max(a, b); }
                """,
            ["./main.ts"] = """
                import { magnitude, maximum } from "./bridge";
                import { Convert } from "dotnet:System";
                console.log(magnitude(-3), maximum(4, 5), Convert.ToInt32(true));
                """
        };
        Assert.Equal("3 5 1\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory]
    [InlineData("NativeMath.Sqrt(\"text\");", "TS2345")]
    [InlineData("const incompatible: string = NativeMath.Sqrt(1);", "TS2322")]
    public void ImportedClrStaticAliasesKeepTheirSynthesizedSignatures(string use, string code)
    {
        string source = """
            import { Math as NativeMath } from "dotnet:System";
            """ + use;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Theory, ModeData]
    public void SynthesizedClrReferenceParametersAcceptNull(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { String as NativeString } from "dotnet:System";
                function empty(value: string | null): boolean { return NativeString.IsNullOrEmpty(value); }
                console.log(empty(null), empty("value"), NativeString.Compare(null, null));
                """
        };
        Assert.Equal("true false 0\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Fact]
    public void SourceClassShadowingImportedClrClassKeepsAClosedStaticSurface()
    {
        const string source = """
            import { Math as NativeMath } from "dotnet:System";
            function read() {
                class NativeMath { static abs(value: number): number { return value; } }
                return NativeMath.Abs(1);
            }
            """;
        var error = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2339", error.Diagnostic.TsCode);
    }
}
