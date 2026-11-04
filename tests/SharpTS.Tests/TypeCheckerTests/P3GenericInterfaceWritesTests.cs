using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3GenericInterfaceWritesTests
{
    [Theory]
    [InlineData("box['value'] = 'next';", null)]
    [InlineData("box['value'] = 2;", "TS2322")]
    [InlineData("box['locked'] = 'next';", "TS2540")]
    public void LiteralWritesUseSubstitutedMemberTypesAndReadonlyMetadata(string write, string? code)
    {
        string source = """
            interface Box<T> { value: T; readonly locked: T; }
            interface Factory { new<T>(value: T): Box<T>; }
            declare const Factory: Factory;
            const box = new Factory<string>('first');
            """ + write;
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics;
        if (code is null) Assert.Empty(diagnostics);
        else Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == code);
    }
}
