using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComputedClassFieldKeyLegalityTests
{
    public const string ComputedNameMessage =
        "A computed property name in a class property declaration must have a simple literal type or a 'unique symbol' type.";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["expression-any", "const C = class { [Symbol()]: any; };\n"];
        yield return ["declaration-any", "class C { [Symbol()]: any; }\n"];
    }

    public static IEnumerable<object[]> ProfileCases() => Cases()
        .SelectMany(row => new[] { false, true }
            .Select(strict => new object[] { row[0], row[1], strict }));

    [Theory, MemberData(nameof(ProfileCases))]
    public void NonUniqueSymbolFieldsReportOneComputedNameDiagnostic(
        string name, string source, bool strict)
    {
        Assert.NotEmpty(name);
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document).ParseOrThrow();
        var checker = strict ? new TypeChecker(TypeCheckerOptions.Strict) : new TypeChecker();
        var diagnostic = Assert.Single(checker.CheckWithRecovery(statements, document).Diagnostics);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("TS1166", diagnostic.TsCode);
        Assert.Equal(ComputedNameMessage, diagnostic.Message);
        Assert.Equal(1, diagnostic.Line);
    }
}
