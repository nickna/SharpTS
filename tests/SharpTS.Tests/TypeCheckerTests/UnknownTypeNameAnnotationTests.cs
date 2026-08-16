using System.Linq;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// TS2304 ("Cannot find name 'X'") for a variable/const annotated with a bare type name that
/// resolves to nothing. Anchors the conformance test
/// <c>assignmentCompatWithStringIndexer3.ts</c> (a class declared inside an inner module is
/// invisible at the outer scope) and pins the gates that keep the previously load-bearing
/// <c>any</c> fallback intact: forward references in hoisted scopes, function-body leniency,
/// qualified names, type aliases, and built-ins must NOT report TS2304.
/// </summary>
public class UnknownTypeNameAnnotationTests
{
    private static bool HasTs2304(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        var parseResult = new Parser(tokens).Parse();
        Assert.True(parseResult.IsSuccess, "source should parse");
        var result = new TypeChecker().CheckWithRecovery(parseResult.Statements);
        return result.Diagnostics.Any(d => d.TsCode == "TS2304");
    }

    [Fact]
    public void Var_BareUnknownTypeName_ReportsTs2304()
    {
        Assert.True(HasTs2304("var a: Nope;"));
    }

    [Fact]
    public void Const_BareUnknownTypeName_ReportsTs2304()
    {
        Assert.True(HasTs2304("const a: Nope = 1;"));
    }

    [Fact]
    public void Var_TypeNestedInModule_InvisibleOutside_ReportsTs2304()
    {
        // The shape of assignmentCompatWithStringIndexer3.ts line 7: the only `A` lives inside the
        // module, so it is not in scope at the top level.
        const string source = """
            var a: A;
            module Generics {
                class A<T> { [x: string]: T; }
            }
            """;
        Assert.True(HasTs2304(source));
    }

    [Fact]
    public void Var_ForwardReferencedTopLevelClass_NoError()
    {
        // Top-level classes are hoisted (as an `any` placeholder) before bodies are checked, so a
        // forward reference is legal — tsc accepts it.
        Assert.False(HasTs2304("var a: A;\nclass A {}"));
    }

    [Fact]
    public void Var_ForwardReferencedTopLevelInterface_NoError()
    {
        Assert.False(HasTs2304("var a: I;\ninterface I { x: number }"));
    }

    [Fact]
    public void Var_UnknownTypeInsideFunctionBody_NoError()
    {
        // Function bodies do NOT pre-register their nested type/class declarations, so a forward
        // reference there resolves to `any`; staying lenient avoids a false positive. (We
        // deliberately under-report here rather than risk regressing forward references.)
        Assert.False(HasTs2304("function f() {\n  var a: A;\n  class A {}\n}"));
    }

    [Fact]
    public void Var_QualifiedName_NoError()
    {
        // Qualified names resolve through the namespace path (permissively to `any`) and are not
        // "cannot find name" candidates.
        const string source = """
            namespace N { export type Id = number; }
            var q: N.Id = 5;
            """;
        Assert.False(HasTs2304(source));
    }

    [Fact]
    public void Var_TypeAlias_NoError()
    {
        Assert.False(HasTs2304("type T = number;\nvar x: T;"));
    }

    [Fact]
    public void Var_KnownBuiltin_NoError()
    {
        Assert.False(HasTs2304("var d: Date;\nvar s: string;\nvar o: Object;"));
    }

    [Fact]
    public void Var_ExplicitAny_NoError()
    {
        Assert.False(HasTs2304("var a: any;"));
    }
}
