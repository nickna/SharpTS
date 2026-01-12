using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Unit tests for parser-level token splitting of >> and >>> in nested generic type contexts.
/// These tests verify that the parser correctly handles the lexer's compound tokens
/// without going through the full execution pipeline.
/// </summary>
public class NestedGenericsTokenSplittingTests
{
    #region Helpers

    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static string GetVariableType(List<Stmt> statements, string varName)
    {
        var varStmt = statements.OfType<Stmt.Var>().FirstOrDefault(v => v.Name.Lexeme == varName);
        return varStmt?.TypeAnnotation ?? throw new Exception($"Variable {varName} not found");
    }

    private static Stmt.Function GetFunction(List<Stmt> statements, string funcName)
    {
        return statements.OfType<Stmt.Function>().FirstOrDefault(f => f.Name.Lexeme == funcName)
            ?? throw new Exception($"Function {funcName} not found");
    }

    #endregion

    #region Double Nested (>> splitting)

    [Fact]
    public void Parser_DoubleNested_ParsesTypeCorrectly()
    {
        var source = """
            interface Data { value: number; }
            let x: Partial<Readonly<Data>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Data>>", type);
    }

    [Fact]
    public void Parser_DoubleNested_WithMultipleTypeArgs()
    {
        var source = """
            interface Entry<K, V> { key: K; value: V; }
            let x: Partial<Entry<string, number>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Entry<string, number>>", type);
    }

    [Fact]
    public void Parser_DoubleNested_WithArraySuffix()
    {
        var source = """
            interface Item { id: number; }
            let x: Partial<Readonly<Item>>[] = [];
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Item>>[]", type);
    }

    [Fact]
    public void Parser_DoubleNested_FunctionParameter()
    {
        var source = """
            interface Data { x: number; }
            function process(d: Partial<Readonly<Data>>): void {}
            """;
        var statements = Parse(source);
        var func = GetFunction(statements, "process");
        Assert.Single(func.Parameters);
        Assert.Equal("Partial<Readonly<Data>>", func.Parameters[0].Type);
    }

    [Fact]
    public void Parser_DoubleNested_FunctionReturnType()
    {
        var source = """
            interface Config { debug: boolean; }
            function getConfig(): Partial<Readonly<Config>> { return {}; }
            """;
        var statements = Parse(source);
        var func = GetFunction(statements, "getConfig");
        Assert.Equal("Partial<Readonly<Config>>", func.ReturnType);
    }

    #endregion

    #region Triple Nested (>>> splitting)

    [Fact]
    public void Parser_TripleNested_ParsesTypeCorrectly()
    {
        var source = """
            interface Data { value: number; }
            let x: Partial<Readonly<Required<Data>>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Required<Data>>>", type);
    }

    [Fact]
    public void Parser_TripleNested_WithArraySuffix()
    {
        var source = """
            interface Item { id: number; }
            let x: Partial<Readonly<Required<Item>>>[] = [];
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Required<Item>>>[]", type);
    }

    [Fact]
    public void Parser_TripleNested_FunctionParameter()
    {
        var source = """
            interface Settings { enabled: boolean; }
            function apply(s: Partial<Readonly<Required<Settings>>>): void {}
            """;
        var statements = Parse(source);
        var func = GetFunction(statements, "apply");
        Assert.Single(func.Parameters);
        Assert.Equal("Partial<Readonly<Required<Settings>>>", func.Parameters[0].Type);
    }

    #endregion

    #region Quadruple+ Nested (multiple splits)

    [Fact]
    public void Parser_QuadrupleNested_ParsesTypeCorrectly()
    {
        var source = """
            interface Base { val: number; }
            let x: Partial<Readonly<Required<Partial<Base>>>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Required<Partial<Base>>>>", type);
    }

    [Fact]
    public void Parser_FiveLevelNested_ParsesTypeCorrectly()
    {
        var source = """
            interface Core { n: number; }
            let x: Partial<Readonly<Required<Partial<Readonly<Core>>>>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Required<Partial<Readonly<Core>>>>>", type);
    }

    #endregion

    #region Mixed Scenarios

    [Fact]
    public void Parser_MultipleNestedDeclarations()
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            let x: Partial<Readonly<A>> = {};
            let y: Partial<Readonly<Required<B>>> = {};
            """;
        var statements = Parse(source);
        Assert.Equal("Partial<Readonly<A>>", GetVariableType(statements, "x"));
        Assert.Equal("Partial<Readonly<Required<B>>>", GetVariableType(statements, "y"));
    }

    [Fact]
    public void Parser_NestedInUnionType()
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            let x: Partial<Readonly<A>> | Partial<Readonly<B>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<A>> | Partial<Readonly<B>>", type);
    }

    [Fact]
    public void Parser_NestedWithRecord()
    {
        var source = """
            interface Inner { x: number; }
            let x: Record<string, Partial<Readonly<Inner>>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Record<string, Partial<Readonly<Inner>>>", type);
    }

    #endregion

    #region Lexer Produces Compound Tokens (Verification)

    [Fact]
    public void Lexer_ProducesGreaterGreater_ForDoubleAngleBracket()
    {
        var source = "x>>";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        Assert.Equal(3, tokens.Count); // IDENTIFIER, GREATER_GREATER, EOF
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal(TokenType.GREATER_GREATER, tokens[1].Type);
        Assert.Equal(TokenType.EOF, tokens[2].Type);
    }

    [Fact]
    public void Lexer_ProducesGreaterGreaterGreater_ForTripleAngleBracket()
    {
        var source = "x>>>";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        Assert.Equal(3, tokens.Count); // IDENTIFIER, GREATER_GREATER_GREATER, EOF
        Assert.Equal(TokenType.IDENTIFIER, tokens[0].Type);
        Assert.Equal(TokenType.GREATER_GREATER_GREATER, tokens[1].Type);
        Assert.Equal(TokenType.EOF, tokens[2].Type);
    }

    [Fact]
    public void Parser_SplitsGreaterGreater_InTypeContext()
    {
        // This source will have >> tokenized as GREATER_GREATER by the lexer
        // The parser should correctly split it when parsing the nested generic
        var source = "let x: A<B<C>> = {};";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        // Verify lexer produces GREATER_GREATER
        Assert.Contains(tokens, t => t.Type == TokenType.GREATER_GREATER);

        // But parser should still parse correctly
        var parser = new Parser(tokens);
        var statements = parser.Parse();
        var type = GetVariableType(statements, "x");
        Assert.Equal("A<B<C>>", type);
    }

    [Fact]
    public void Parser_SplitsGreaterGreaterGreater_InTypeContext()
    {
        // This source will have >>> tokenized as GREATER_GREATER_GREATER by the lexer
        var source = "let x: A<B<C<D>>> = {};";
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        // Verify lexer produces GREATER_GREATER_GREATER
        Assert.Contains(tokens, t => t.Type == TokenType.GREATER_GREATER_GREATER);

        // But parser should still parse correctly
        var parser = new Parser(tokens);
        var statements = parser.Parse();
        var type = GetVariableType(statements, "x");
        Assert.Equal("A<B<C<D>>>", type);
    }

    #endregion

    #region Regression: Non-Type Contexts Preserve Shift Operators

    [Fact]
    public void Parser_RightShift_ParsesAsBinaryOperator()
    {
        var source = "let x = 16 >> 2;";
        var statements = Parse(source);
        var varStmt = statements.OfType<Stmt.Var>().First();
        var binary = varStmt.Initializer as Expr.Binary;

        Assert.NotNull(binary);
        Assert.Equal(TokenType.GREATER_GREATER, binary!.Operator.Type);
    }

    [Fact]
    public void Parser_UnsignedRightShift_ParsesAsBinaryOperator()
    {
        var source = "let x = 16 >>> 2;";
        var statements = Parse(source);
        var varStmt = statements.OfType<Stmt.Var>().First();
        var binary = varStmt.Initializer as Expr.Binary;

        Assert.NotNull(binary);
        Assert.Equal(TokenType.GREATER_GREATER_GREATER, binary!.Operator.Type);
    }

    [Fact]
    public void Parser_MixedContext_TypeAndExpression()
    {
        var source = """
            interface Num { n: number; }
            let typed: Partial<Readonly<Num>> = { n: 32 };
            let shifted = 16 >> 2;
            """;
        var statements = Parse(source);

        // Verify nested generic type parsed correctly
        Assert.Equal("Partial<Readonly<Num>>", GetVariableType(statements, "typed"));

        // Verify shift operator parsed correctly
        var shiftedStmt = statements.OfType<Stmt.Var>().First(v => v.Name.Lexeme == "shifted");
        var binary = shiftedStmt.Initializer as Expr.Binary;
        Assert.NotNull(binary);
        Assert.Equal(TokenType.GREATER_GREATER, binary!.Operator.Type);
    }

    #endregion

    #region Generic Type Parameters

    [Fact]
    public void Parser_GenericFunctionWithNestedConstraint()
    {
        var source = """
            interface Base { id: number; }
            function process<T extends Partial<Readonly<Base>>>(x: T): void {}
            """;
        var statements = Parse(source);
        var func = GetFunction(statements, "process");

        Assert.NotNull(func.TypeParams);
        Assert.Single(func.TypeParams);
        Assert.Equal("Partial<Readonly<Base>>", func.TypeParams[0].Constraint);
    }

    [Fact]
    public void Parser_GenericClassWithNestedConstraint()
    {
        var source = """
            interface Item { name: string; }
            class Container<T extends Partial<Readonly<Item>>> {
                value: T;
            }
            """;
        var statements = Parse(source);
        var classStmt = statements.OfType<Stmt.Class>().First();

        Assert.NotNull(classStmt.TypeParams);
        Assert.Single(classStmt.TypeParams);
        Assert.Equal("Partial<Readonly<Item>>", classStmt.TypeParams[0].Constraint);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parser_NestedGenerics_SpacedStillWorks()
    {
        // Ensure that the spaced workaround still works for backwards compatibility
        var source = "let x: Partial<Readonly<Data> > = {};";
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Data>>", type);
    }

    [Fact]
    public void Parser_NestedGenerics_EmptyInterface()
    {
        var source = """
            interface Empty {}
            let x: Partial<Readonly<Empty>> = {};
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Empty>>", type);
    }

    [Fact]
    public void Parser_ComplexNestedType()
    {
        var source = """
            interface Data { values: number[]; }
            let x: Partial<Readonly<Data>>[] = [];
            """;
        var statements = Parse(source);
        var type = GetVariableType(statements, "x");
        Assert.Equal("Partial<Readonly<Data>>[]", type);
    }

    #endregion
}
