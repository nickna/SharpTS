using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// A function's return/this type may query one of its own parameters with <c>typeof param</c>
/// (e.g. <c>declare function f(cb: T): typeof cb</c>). The parameters must be in scope while the
/// signature's return type is resolved. Mirrors tsc on the
/// <c>subtypesAndSuperTypes/subtypingWith{Call,Construct}Signatures</c> conformance tests.
/// </summary>
public class TypeofParameterInReturnTypeTests
{
    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        var parseResult = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parseResult.IsSuccess);
        return new TypeChecker(maxErrors: 50).CheckWithRecovery(parseResult.Statements).Diagnostics;
    }

    [Fact]
    public void TypeofParameterInReturnType_Resolves()
    {
        var diags = Check("""
            declare function foo1(cb: (x: number) => void): typeof cb;
            declare function foo1(cb: any): any;
            var r = foo1((x: number) => 1);
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2304");
    }

    [Fact]
    public void TypeofParameterInReturnType_NonAmbientFunction_Resolves()
    {
        var diags = Check("""
            function id(cb: (x: number) => void): typeof cb { return cb; }
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2304");
    }

    [Fact]
    public void TypeofConstructSignatureParameter_Resolves()
    {
        var diags = Check("""
            declare function make(a: new () => object): typeof a;
            declare function make(a: any): any;
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2304");
    }

    [Fact]
    public void TypeofGenuinelyUnknownName_StillErrors()
    {
        // The parameter binding must not mask a real unresolved name.
        var diags = Check("""
            declare function bad(cb: (x: number) => void): typeof notAParam;
            """);
        Assert.Contains(diags, d => d.TsCode == "TS2304");
    }

    [Fact]
    public void ParameterBindingDoesNotLeakIntoBody()
    {
        // `cb` is a parameter; after the signature, a same-named outer query must not resolve to it.
        var diags = Check("""
            declare function f(cb: (x: number) => void): typeof cb;
            var q: typeof cb;
            """);
        // The top-level `typeof cb` has no binding — must still error.
        Assert.Contains(diags, d => d.TsCode == "TS2304");
    }
}
