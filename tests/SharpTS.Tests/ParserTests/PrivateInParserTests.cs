using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class PrivateInParserTests
{
    private static Expr ParseExpression(string source)
        => Assert.IsType<Stmt.Expression>(Assert.Single(TestHarness.ParseOrThrow(source))).Expr;

    [Fact]
    public void BarePrivateNameFormsAPresenceExpression()
    {
        var expression = Assert.IsType<Expr.PrivateIn>(ParseExpression("#value in object;"));
        Assert.Equal("#value", expression.Name.Lexeme);
        Assert.Equal("object", Assert.IsType<Expr.Variable>(expression.Object).Name.Lexeme);
    }

    [Fact]
    public void PresenceRightOperandIncludesShiftAndArithmeticExpressions()
    {
        var expression = Assert.IsType<Expr.PrivateIn>(ParseExpression("#value in object + other << 1;"));
        var shift = Assert.IsType<Expr.Binary>(expression.Object);
        Assert.Equal(TokenType.LESS_LESS, shift.Operator.Type);
        Assert.Equal(TokenType.PLUS, Assert.IsType<Expr.Binary>(shift.Left).Operator.Type);
    }

    [Fact]
    public void PresenceBindsBeforeEqualityAndLogicalOperators()
    {
        var logical = Assert.IsType<Expr.Logical>(ParseExpression("#value in object === true && enabled;"));
        Assert.Equal(TokenType.AND_AND, logical.Operator.Type);
        var equality = Assert.IsType<Expr.Binary>(logical.Left);
        Assert.Equal(TokenType.EQUAL_EQUAL_EQUAL, equality.Operator.Type);
        Assert.IsType<Expr.PrivateIn>(equality.Left);
    }

    [Theory]
    [InlineData("#value in object in other;", TokenType.IN)]
    [InlineData("#value in object < other;", TokenType.LESS)]
    [InlineData("#value in object instanceof Other;", TokenType.INSTANCEOF)]
    public void FollowingRelationalOperatorsAssociateToTheLeft(string source, TokenType operation)
    {
        var expression = Assert.IsType<Expr.Binary>(ParseExpression(source));
        Assert.Equal(operation, expression.Operator.Type);
        Assert.IsType<Expr.PrivateIn>(expression.Left);
    }

    [Fact]
    public void PrivateNameAndInMayBeSeparatedByALineTerminator()
        => Assert.IsType<Expr.PrivateIn>(ParseExpression("#value\nin object;"));

    [Theory]
    [InlineData("#value;")]
    [InlineData("(#value) in object;")]
    [InlineData("!#value in object;")]
    [InlineData("delete #value in object;")]
    [InlineData("#value + 1;")]
    [InlineData("#value === object;")]
    [InlineData("object in #value;")]
    [InlineData("object < #value in object;")]
    [InlineData("#value in #other in object;")]
    [InlineData("#value in;")]
    [InlineData("#value in object = true;")]
    [InlineData("(#value in object) = true;")]
    [InlineData("#value in object += true;")]
    public void PrivateNamesAreRejectedOutsideTheBarePresenceProduction(string expression)
    {
        var source = $$"""
            class Owner {
                #value: number = 1;
                #other: number = 2;
                check(object: any): void { {{expression}} }
            }
            """;
        var result = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData("for (#value in object) {}")]
    [InlineData("for (let #value in object) {}")]
    public void PrivatePresenceCannotBeAForInBinding(string statement)
    {
        var source = $$"""
            class Owner {
                #value: number = 1;
                check(object: any): void { {{statement}} }
            }
            """;
        Assert.False(new Parser(new Lexer(source).ScanTokens()).Parse().IsSuccess);
    }

    [Fact]
    public void ParenthesizedPresenceCanInitializeATraditionalForLoop()
    {
        var source = """
            class Owner {
                #value: number = 1;
                check(object: any): void {
                    for (let present = (#value in object); present; present = false) {}
                }
            }
            """;
        Assert.Single(TestHarness.ParseOrThrow(source));
    }

    [Theory]
    [InlineData("for (let present = #value in object; ; ) {}")]
    [InlineData("for (var present = true, other = #value in object; ; ) {}")]
    [InlineData("for (#value in object; ; ) {}")]
    [InlineData("for (present = #value in object; ; ) {}")]
    [InlineData("for (present = false, #value in object; ; ) {}")]
    [InlineData("for (let present = true && #value in object; ; ) {}")]
    [InlineData("for (let present = true ? false : #value in object; ; ) {}")]
    [InlineData("for (let present = #value in object ? true : false; ; ) {}")]
    [InlineData("for (let present = () => #value in object; ; ) {}")]
    [InlineData("for ([present] = #value in object; ; ) {}")]
    public void UnparenthesizedPresenceIsRejectedInNoInInitializerPositions(string statement)
    {
        var source = $$"""
            class Owner {
                #value: number = 1;
                check(object: any): void { let present: any; {{statement}} }
            }
            """;
        var result = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("must be parenthesized"));
    }

    [Theory]
    [InlineData("for (let present = (#value in object); ; ) {}")]
    [InlineData("for (let present = consume(#value in object); ; ) {}")]
    [InlineData("for (let present = [#value in object]; ; ) {}")]
    [InlineData("for (let present = { value: #value in object }; ; ) {}")]
    [InlineData("for (let present = object[#value in object]; ; ) {}")]
    [InlineData("for (let present = new Other(#value in object); ; ) {}")]
    [InlineData("for (let present = true ? #value in object : false; ; ) {}")]
    [InlineData("for (let present = `${#value in object}`; ; ) {}")]
    [InlineData("for (let present = () => (#value in object); ; ) {}")]
    [InlineData("for (let present = () => { return #value in object; }; ; ) {}")]
    [InlineData("for ([present = #value in object] = array; ; ) {}")]
    [InlineData("for (let present = true; #value in object; ) {}")]
    [InlineData("for (let present = true; ; present = #value in object) {}")]
    [InlineData("for (let key in (#value in object ? object : {})) {}")]
    [InlineData("for (let item of (#value in object ? array : [])) {}")]
    public void NestedInEnabledExpressionsAndOtherForClausesRemainAllowed(string statement)
    {
        var source = $$"""
            class Owner {
                #value: number = 1;
                check(object: any): void { let present: any; {{statement}} }
            }
            """;
        Assert.Single(TestHarness.ParseOrThrow(source));
    }

    [Fact]
    public void ExistingOrdinaryInInitializerParsingIsPreserved()
        => Assert.Single(TestHarness.ParseOrThrow("for (let present = 'value' in object; ; ) {}"));

    [Fact]
    public void LexicalPrivateNamesOutsideClassBodiesAreRejected()
    {
        var exception = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow("#value in {};")));
        Assert.Equal("TS18016", exception.Diagnostic.TsCode);
    }

    [Fact]
    public void NestedClassDeclarationsCanResolveLexicalOuterPrivateNames()
        => new TypeChecker().Check(TestHarness.ParseOrThrow("""
            class Outer {
                #outer = 1;
                make() { class Inner { has(value: any) { return #outer in value; } } return Inner; }
            }
            """));

    [Theory]
    [InlineData("class Owner { check(object: any): boolean { return #missing in object; } }")]
    [InlineData("class Owner { value: number = 1; check(object: any): boolean { return #value in object; } }")]
    [InlineData("class Base { #value: number = 1; } class Owner extends Base { check(object: any): boolean { return #value in object; } }")]
    public void UndeclaredPrivateNamesCannotBeSynthesizedFromPublicOrInheritedNames(string source)
    {
        var exception = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2339", exception.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("string")]
    [InlineData("boolean")]
    [InlineData("bigint")]
    [InlineData("symbol")]
    [InlineData("null")]
    [InlineData("undefined")]
    [InlineData("number | object")]
    public void PrimitivePresenceReceiversAreRejected(string type)
    {
        var source = $$"""
            class Owner {
                #value: number = 1;
                check(object: {{type}}): boolean { return #value in object; }
            }
            """;
        var exception = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal("TS2322", exception.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("", "TS2322")]
    [InlineData(" extends any", "TS2322")]
    [InlineData(" extends number", "TS2322")]
    [InlineData(" extends number | object", "TS2322")]
    public void GenericPresenceReceiversMustHaveObjectCompatibleConstraints(string constraint, string expectedCode)
    {
        var source = $$"""
            class Owner<T{{constraint}}> {
                #value = 1;
                has(value: T) { return #value in value; }
            }
            """;
        var exception = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow(source)));
        Assert.Equal(expectedCode, exception.Diagnostic.TsCode);
    }

    [Fact]
    public void UnknownPresenceReceiversAreRejected()
    {
        var exception = Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker().Check(TestHarness.ParseOrThrow("""
                class Owner { #value = 1; has(value: unknown) { return #value in value; } }
                """)));
        Assert.Equal("TS18046", exception.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("{}")]
    public void ObjectCompatibleGenericConstraintsAreAllowed(string constraint)
        => new TypeChecker().Check(TestHarness.ParseOrThrow($$"""
            class Owner<T extends {{constraint}}> { #value = 1; has(value: T) { return #value in value; } }
            """));
}
