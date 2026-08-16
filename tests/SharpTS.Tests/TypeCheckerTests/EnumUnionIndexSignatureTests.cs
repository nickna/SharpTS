using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// #894: a union property type is checked against a string index signature member-wise, but a
/// numeric enum constituent absorbed by a sibling <c>number</c> (or string enum by <c>string</c>)
/// must be dropped first — tsc reduces <c>e | number</c> to <c>number</c>, so it is assignable to a
/// <em>different</em> numeric enum index (E2). enum→enum is nominal, so the un-reduced <c>e</c> arm
/// would wrongly emit a spurious TS2411.
///
/// TS2411 is recorded (not thrown) since commit b2896eb8, so these assert on the diagnostic
/// collection via <see cref="TypeChecker.CheckWithRecovery"/> rather than Assert.ThrowsAny.
/// </summary>
public class EnumUnionIndexSignatureTests
{
    private static IReadOnlyList<Diagnostic> Diagnostics(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        var parseResult = new Parser(tokens).Parse();
        Assert.True(parseResult.IsSuccess, "source should parse for a type-check test");
        return new TypeChecker().CheckWithRecovery(parseResult.Statements).Diagnostics;
    }

    private static bool HasTs2411(IEnumerable<Diagnostic> diags) =>
        diags.Any(d => d.TsCode == "TS2411");

    [Fact]
    public void NumericEnumUnionNumber_AgainstDifferentNumericEnumIndex_NoError()
    {
        // (e | number) reduces to number; number is assignable to the E2 index → no TS2411 (#894).
        var source = """
            enum e { e1, e2 }
            enum E2 { A }
            interface I14 {
                [x: string]: E2;
                foo2: e | number;
            }
            """;
        Assert.DoesNotContain(Diagnostics(source), d => d.TsCode == "TS2411");
    }

    [Fact]
    public void StringNumberUnion_AgainstNumericEnumIndex_StillErrors()
    {
        // No enum constituent → no reduction; string is not assignable to E2 → TS2411 stays.
        var source = """
            enum E2 { A }
            interface I14 {
                [x: string]: E2;
                foo: string | number;
            }
            """;
        Assert.True(HasTs2411(Diagnostics(source)));
    }

    [Fact]
    public void BareEnum_AgainstDifferentEnumIndex_StillErrors()
    {
        // Bare enum (no union) is untouched by the reduction; enum→enum is nominal → TS2411 stays.
        // Guards against regressing enumIsNotASubtypeOfAnythingButNumber.ts.
        var source = """
            enum E { A }
            enum E2 { A }
            interface I {
                [x: string]: E2;
                foo: E;
            }
            """;
        Assert.True(HasTs2411(Diagnostics(source)));
    }

    [Fact]
    public void NumericEnumUnionNumber_AgainstNumberIndex_NoError()
    {
        // Already correct before the fix; pin that it stays correct (e and number both → number).
        var source = """
            enum e { e1, e2 }
            interface I2 {
                [x: string]: number;
                foo2: e | number;
            }
            """;
        Assert.DoesNotContain(Diagnostics(source), d => d.TsCode == "TS2411");
    }

    [Fact]
    public void NumericEnumUnionNumber_AgainstStringIndex_StillErrors()
    {
        // (e | number) reduces to number; number is not assignable to a string index → TS2411 stays.
        var source = """
            enum e { e1, e2 }
            interface I3 {
                [x: string]: string;
                foo2: e | number;
            }
            """;
        Assert.True(HasTs2411(Diagnostics(source)));
    }
}
