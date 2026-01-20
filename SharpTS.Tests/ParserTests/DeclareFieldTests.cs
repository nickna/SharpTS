using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Parser-level tests for the declare field modifier syntax.
/// Verifies correct AST construction and parse errors.
/// </summary>
public class DeclareFieldTests
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

    #region Valid Declarations

    [Fact]
    public void Parser_DeclareField_Simple()
    {
        var source = @"
class Model {
    declare id: number;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("id", field.Name.Lexeme);
        Assert.Equal("number", field.TypeAnnotation);
        Assert.Null(field.Initializer);
        Assert.True(field.IsDeclare);
        Assert.False(field.IsStatic);
    }

    [Fact]
    public void Parser_DeclareField_WithReadonly()
    {
        var source = @"
class Model {
    declare readonly name: string;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("name", field.Name.Lexeme);
        Assert.True(field.IsDeclare);
        Assert.True(field.IsReadonly);
    }

    [Fact]
    public void Parser_DeclareField_Static()
    {
        var source = @"
class Model {
    declare static version: string;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("version", field.Name.Lexeme);
        Assert.True(field.IsDeclare);
        Assert.True(field.IsStatic);
    }

    [Fact]
    public void Parser_DeclareField_StaticDeclareOrder()
    {
        // static declare (alternative order)
        var source = @"
class Model {
    static declare count: number;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.Equal("count", field.Name.Lexeme);
        Assert.True(field.IsDeclare);
        Assert.True(field.IsStatic);
    }

    [Fact]
    public void Parser_DeclareField_WithAccessModifiers()
    {
        var source = @"
class Model {
    declare public status: string;
    declare private _internal: number;
    declare protected base: object;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Equal(3, classStmt.Fields.Count);

        Assert.True(classStmt.Fields[0].IsDeclare);
        Assert.Equal(AccessModifier.Public, classStmt.Fields[0].Access);

        Assert.True(classStmt.Fields[1].IsDeclare);
        Assert.Equal(AccessModifier.Private, classStmt.Fields[1].Access);

        Assert.True(classStmt.Fields[2].IsDeclare);
        Assert.Equal(AccessModifier.Protected, classStmt.Fields[2].Access);
    }

    [Fact]
    public void Parser_DeclareField_MixedWithRegularFields()
    {
        var source = @"
class Model {
    declare id: number;
    name: string = ""default"";
    declare static version: string;
    count: number = 0;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Equal(4, classStmt.Fields.Count);

        Assert.True(classStmt.Fields[0].IsDeclare);   // id
        Assert.False(classStmt.Fields[1].IsDeclare);  // name
        Assert.True(classStmt.Fields[2].IsDeclare);   // version
        Assert.False(classStmt.Fields[3].IsDeclare);  // count
    }

    [Fact]
    public void Parser_DeclareField_AllModifiersCombined()
    {
        var source = @"
class Model {
    declare private static readonly config: object;
}";
        var statements = Parse(source);

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);

        Assert.Single(classStmt.Fields);
        var field = classStmt.Fields[0];

        Assert.True(field.IsDeclare);
        Assert.Equal(AccessModifier.Private, field.Access);
        Assert.True(field.IsStatic);
        Assert.True(field.IsReadonly);
    }

    #endregion

    #region Class Expression

    [Fact]
    public void Parser_DeclareField_ClassExpression()
    {
        var source = @"
let Model = class {
    declare id: number;
    declare static version: string;
};";
        var statements = Parse(source);

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        var classExpr = Assert.IsType<Expr.ClassExpr>(varStmt.Initializer);

        Assert.Equal(2, classExpr.Fields.Count);
        Assert.True(classExpr.Fields[0].IsDeclare);
        Assert.False(classExpr.Fields[0].IsStatic);
        Assert.True(classExpr.Fields[1].IsDeclare);
        Assert.True(classExpr.Fields[1].IsStatic);
    }

    #endregion

    #region Parse Errors

    [Fact]
    public void Parser_DeclareField_Error_WithInitializer()
    {
        var source = @"
class Model {
    declare x: number = 5;
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' fields cannot have an initializer", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_Error_WithPrivateIdentifier()
    {
        var source = @"
class Model {
    declare #secret: string;
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' modifier cannot be used with private fields (#name)", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_Error_WithAbstract()
    {
        var source = @"
abstract class Model {
    declare abstract x: number;
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' modifier cannot be used with 'abstract'", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_Error_WithOverride()
    {
        var source = @"
class Base {
    x: number = 0;
}
class Model extends Base {
    declare override x: number;
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' modifier cannot be used with 'override'", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_Error_OnMethod()
    {
        var source = @"
class Model {
    declare doSomething(): void { }
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' modifier is only valid on fields, not methods", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_Error_OnConstructor()
    {
        var source = @"
class Model {
    declare constructor() { }
}";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' modifier is only valid on fields, not methods", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_ClassExpression_Error_WithInitializer()
    {
        var source = @"
let Model = class {
    declare x: number = 5;
};";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' fields cannot have an initializer", error.Message);
    }

    [Fact]
    public void Parser_DeclareField_ClassExpression_Error_OnMethod()
    {
        var source = @"
let Model = class {
    declare doSomething(): void { }
};";
        var error = GetParseError(source);

        Assert.NotNull(error);
        Assert.Contains("'declare' modifier is only valid on fields, not methods", error.Message);
    }

    #endregion
}
