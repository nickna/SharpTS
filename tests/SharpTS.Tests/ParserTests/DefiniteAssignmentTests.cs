using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Parser-level tests for the definite assignment assertion syntax (let x!: type).
/// Verifies correct AST construction and parse errors.
/// </summary>
public class DefiniteAssignmentTests
{
    private List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    private Exception? GetParseError(string source)
    {
        try
        {
            Parse(source);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    #region Variable Declarations

    [Fact]
    public void Parser_DefiniteAssignment_Variable_WithNumberType()
    {
        var source = "let x!: number;";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);

        Assert.Equal("x", varStmt.Name.Lexeme);
        Assert.Equal("number", varStmt.TypeAnnotation);
        Assert.Null(varStmt.Initializer);
        Assert.True(varStmt.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Variable_WithStringType()
    {
        var source = "let name!: string;";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);

        Assert.Equal("name", varStmt.Name.Lexeme);
        Assert.Equal("string", varStmt.TypeAnnotation);
        Assert.True(varStmt.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Variable_WithArrayType()
    {
        var source = "let items!: number[];";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);

        Assert.Equal("items", varStmt.Name.Lexeme);
        Assert.Equal("number[]", varStmt.TypeAnnotation);
        Assert.True(varStmt.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Variable_WithCustomType()
    {
        var source = "let user!: User;";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);

        Assert.Equal("user", varStmt.Name.Lexeme);
        Assert.Equal("User", varStmt.TypeAnnotation);
        Assert.True(varStmt.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Variable_WithUnionType()
    {
        var source = "let value!: string | number;";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);

        Assert.Equal("value", varStmt.Name.Lexeme);
        Assert.Equal("string | number", varStmt.TypeAnnotation);
        Assert.True(varStmt.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_RegularVariable_NoDefiniteAssignment()
    {
        var source = "let x: number = 5;";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);

        Assert.False(varStmt.HasDefiniteAssignmentAssertion);
        Assert.NotNull(varStmt.Initializer);
    }

    #endregion

    #region Class Field Declarations

    [Fact]
    public void Parser_DefiniteAssignment_Field_Simple()
    {
        var source = @"
class MyClass {
    name!: string;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("name", field.Name.Lexeme);
        Assert.Equal("string", field.TypeAnnotation);
        Assert.Null(field.Initializer);
        Assert.True(field.HasDefiniteAssignmentAssertion);
        Assert.False(field.IsOptional);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Field_WithModifiers()
    {
        var source = @"
class MyClass {
    private readonly id!: number;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("id", field.Name.Lexeme);
        Assert.True(field.HasDefiniteAssignmentAssertion);
        Assert.Equal(AccessModifier.Private, field.Access);
        Assert.True(field.IsReadonly);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Field_Static()
    {
        var source = @"
class MyClass {
    static instance!: MyClass;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("instance", field.Name.Lexeme);
        Assert.True(field.HasDefiniteAssignmentAssertion);
        Assert.True(field.IsStatic);
    }

    [Fact]
    public void Parser_RegularField_NoDefiniteAssignment()
    {
        var source = @"
class MyClass {
    name: string;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.False(field.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_OptionalField_NoDefiniteAssignment()
    {
        var source = @"
class MyClass {
    name?: string;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.True(field.IsOptional);
        Assert.False(field.HasDefiniteAssignmentAssertion);
    }

    #endregion

    #region Parse Errors - Variable Declarations

    [Fact]
    public void Parser_DefiniteAssignment_Error_NoTypeAnnotation()
    {
        var source = "let x!;";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("Definite assignment assertion '!' requires a type annotation", error.Message);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Error_WithInitializer()
    {
        var source = "let x!: number = 5;";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("Definite assignment assertion '!' cannot be used with an initializer", error.Message);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Error_WithConst()
    {
        var source = "const x!: number;";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'const' declarations cannot use definite assignment assertion", error.Message);
    }

    #endregion

    #region Parse Errors - Field Declarations

    [Fact]
    public void Parser_DefiniteAssignment_Field_Error_WithOptional()
    {
        var source = @"
class MyClass {
    name?!: string;
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("A property cannot be both optional and have a definite assignment assertion", error.Message);
    }

    [Fact]
    public void Parser_DefiniteAssignment_Field_Error_WithInitializer()
    {
        var source = @"
class MyClass {
    name!: string = ""default"";
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("Definite assignment assertion '!' cannot be used with an initializer", error.Message);
    }

    #endregion

    #region Multiple Declarations

    [Fact]
    public void Parser_DefiniteAssignment_MultipleVariables()
    {
        var source = @"
let x!: number;
let y: string = ""hello"";
let z!: boolean;
";
        var statements = Parse(source);

        Assert.Equal(3, statements.Count);

        var varX = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.True(varX.HasDefiniteAssignmentAssertion);

        var varY = Assert.IsType<Stmt.Var>(statements[1]);
        Assert.False(varY.HasDefiniteAssignmentAssertion);

        var varZ = Assert.IsType<Stmt.Var>(statements[2]);
        Assert.True(varZ.HasDefiniteAssignmentAssertion);
    }

    [Fact]
    public void Parser_DefiniteAssignment_MultipleFields()
    {
        var source = @"
class MyClass {
    a!: number;
    b: string;
    c?: boolean;
    d!: number[];
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Equal(4, classStmt.Fields.Count);

        Assert.True(classStmt.Fields[0].HasDefiniteAssignmentAssertion);   // a
        Assert.False(classStmt.Fields[1].HasDefiniteAssignmentAssertion);  // b
        Assert.False(classStmt.Fields[2].HasDefiniteAssignmentAssertion);  // c (optional)
        Assert.True(classStmt.Fields[3].HasDefiniteAssignmentAssertion);   // d
    }

    #endregion

    #region Class Expression

    [Fact]
    public void Parser_DefiniteAssignment_ClassExpression()
    {
        var source = @"
let MyClass = class {
    name!: string;
};";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        var classExpr = Assert.IsType<Expr.ClassExpr>(varStmt.Initializer);

        Assert.Single(classExpr.Fields);
        var field = classExpr.Fields[0];

        Assert.True(field.HasDefiniteAssignmentAssertion);
    }

    #endregion
}
