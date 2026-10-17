using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ForwardClassConstructorTests
{
    public static TheoryData<string> MissingMembers => new()
    {
        "function read() { return Later.missing; } class Later {}",
        "const read = () => Later.missing; class Later {}",
        "class Earlier { static read() { return Later.missing; } } class Later extends Earlier {}",
        "const Earlier = class { static read() { return Later.missing; } }; class Later {}",
        "function outer() { function read() { return Later.missing; } class Later {} }",
        "const outer = () => { function read() { return Later.missing; } class Later {} };",
        "class Outer { read() { function read() { return Later.missing; } class Later {} } }",
        "const Outer = class { read() { function read() { return Later.missing; } class Later {} } };",
        "function read() { return Later.missing; } class Later<T> { value: T; }",
        "class Later { static present: number = 1; } function outer() { function read() { return Later.present; } class Later {} }",
        "function read() { return Later.missing(); } class Later {}",
    };

    [Theory]
    [MemberData(nameof(MissingMembers))]
    public void DeferredBodyRejectsMissingConstructorMember(string source)
    {
        var exception = Assert.Throws<TypeCheckException>(() => new TypeChecker().Check(Parse(source)));
        Assert.Equal("TS2339", exception.Diagnostic.TsCode);
    }

    [Theory]
    [MemberData(nameof(MissingMembers))]
    public void RecoveryReportsMissingConstructorMemberOnce(string source)
    {
        var result = new TypeChecker().CheckWithRecovery(Parse(source));
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2339");
        Assert.Contains("does not exist", diagnostic.Message);
    }

    [Fact]
    public void DeferredMemberRetainsDeclaredType()
    {
        var exception = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(Parse("""
            function read(): string { return Later.present; }
            class Later { static present: number = 1; }
            """)));
        Assert.Equal("TS2322", exception.Diagnostic.TsCode);
    }

    [Fact]
    public void DeferredMemberRetainsLiteralInitializerType()
    {
        var exception = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(Parse("""
            function read(): string { return Later.present; }
            class Later { static present = 1; }
            """)));
        Assert.Equal("TS2322", exception.Diagnostic.TsCode);
    }

    [Fact]
    public void ForwardHeritageStillReportsDeclarationOrder()
    {
        var result = new TypeChecker().CheckWithRecovery(Parse("""
            function read() { return Later.present; }
            class Earlier extends Later {}
            class Later { static present: number = 1; }
            """));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2449");
    }

    [Fact]
    public void ReusedCheckerDiscardsPriorForwardDeclarationState()
    {
        var checker = new TypeChecker();
        Assert.Throws<TypeCheckException>(() => checker.Check(Parse("""
            function invalid() { return Prior.missing; }
            class Prior {}
            """)));
        var result = checker.CheckWithRecovery(Parse("""
            function read(Current: any): number { return Current.present; }
            function valid(): number { return Current.present; }
            class Current { static present: number = 1; }
            """));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ShadowedClassDeclarationLineDoesNotChangeOuterHeritage()
    {
        var result = new TypeChecker().CheckWithRecovery(Parse("""
            class Base {}
            class Derived extends Base {}
            function local() {
                class Base {}
                class Derived extends Base {}
            }
            class Another extends Base {}
            """));
        Assert.Empty(result.Diagnostics);
    }

    private static List<Stmt> Parse(string source) => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
}
