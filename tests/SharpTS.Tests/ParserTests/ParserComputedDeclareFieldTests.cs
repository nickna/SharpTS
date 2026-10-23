using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class ParserComputedDeclareFieldTests
{
    [Theory]
    [InlineData(false, "declare [keys.erased]: number;", false, false)]
    [InlineData(true, "declare [keys.erased]: number;", false, false)]
    [InlineData(false, "declare readonly [keys.erased]: number;", false, true)]
    [InlineData(true, "declare readonly [keys.erased]: number;", false, true)]
    [InlineData(false, "declare static readonly [keys.erased]: number;", true, true)]
    [InlineData(true, "declare static readonly [keys.erased]: number;", true, true)]
    public void ComputedDeclarationsRetainFieldMetadata(
        bool expression, string member, bool isStatic, bool isReadonly)
    {
        var statement = Assert.Single(Parse(Wrap(expression, member + " actual = 1;")));
        var fields = expression
            ? Assert.IsType<Expr.ClassExpr>(Assert.IsType<Stmt.Const>(statement).Initializer).Fields
            : Assert.IsType<Stmt.Class>(statement).Fields;

        Assert.Equal(2, fields.Count);
        var declaration = fields[0];
        Assert.True(declaration.IsDeclare);
        Assert.Equal(isStatic, declaration.IsStatic);
        Assert.Equal(isReadonly, declaration.IsReadonly);
        Assert.Equal("number", declaration.TypeAnnotation);
        Assert.Null(declaration.Initializer);
        var key = Assert.IsType<Expr.Get>(declaration.ComputedKey);
        Assert.Equal("keys", Assert.IsType<Expr.Variable>(key.Object).Name.Lexeme);
        Assert.Equal("erased", key.Name.Lexeme);

        var ordinary = fields[1];
        Assert.False(ordinary.IsDeclare);
        Assert.False(ordinary.IsStatic);
        Assert.Null(ordinary.ComputedKey);
        Assert.NotNull(ordinary.Initializer);
    }

    [Theory]
    [InlineData(false, "declare ['field'](): void {}", "'declare' modifier is only valid on fields, not methods")]
    [InlineData(true, "declare ['field'](): void {}", "'declare' modifier is only valid on fields, not methods")]
    [InlineData(false, "declare ['field']: number = 1;", "'declare' fields cannot have an initializer")]
    [InlineData(true, "declare ['field']: number = 1;", "'declare' fields cannot have an initializer")]
    public void ComputedMemberDeclarationsRejectMethodsAndInitializers(
        bool expression, string member, string expected)
    {
        var error = Assert.ThrowsAny<Exception>(() => Parse(Wrap(expression, member)));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void AmbientClassComputedMethodSignatureRemainsLegal()
    {
        var declaration = Assert.IsType<Stmt.Class>(Assert.Single(
            Parse("declare class Contract { ['read'](): number; }")));
        Assert.True(declaration.IsDeclare);
        var method = Assert.Single(declaration.Methods);
        Assert.NotNull(method.ComputedKey);
        Assert.Empty(declaration.Fields);
    }

    // Declaration field collection already validates TS1166. Class expressions use
    // a separate checker path; their strict-negative source remains in the issue artifacts.
    [Fact]
    public void NonUniqueSymbolDeclarationKeysRemainASemanticError()
    {
        var source = Wrap(false, "declare [Symbol()]: number;");
        var document = new SourceDocument("computed-declare-symbol.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS1166");
    }

    private static string Wrap(bool expression, string members) => expression
        ? "const Model = class { " + members + " };"
        : "class Model { " + members + " }";

    private static List<Stmt> Parse(string source) => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
}
