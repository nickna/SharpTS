using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for parsing call signatures and constructor signatures in interfaces.
/// </summary>
public class SignatureParsingTests
{
    private static Stmt ParseDeclaration(string code)
    {
        var lexer = new Lexer(code);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var result = parser.Parse();
        return result.Statements[0];
    }

    [Fact]
    public void CallSignature_BasicParsing()
    {
        var stmt = ParseDeclaration("interface Func { (x: number): string; }");
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.CallSignatures);
        Assert.Single(iface.CallSignatures);
        var sig = iface.CallSignatures[0];
        Assert.Single(sig.Parameters);
        Assert.Equal("x", sig.Parameters[0].Name.Lexeme);
        Assert.Equal("number", sig.Parameters[0].Type);
        Assert.Equal("string", sig.ReturnType);
    }

    [Fact]
    public void CallSignature_MultipleParameters()
    {
        var stmt = ParseDeclaration("interface Adder { (a: number, b: number): number; }");
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.CallSignatures);
        Assert.Single(iface.CallSignatures);
        var sig = iface.CallSignatures[0];
        Assert.Equal(2, sig.Parameters.Count);
        Assert.Equal("a", sig.Parameters[0].Name.Lexeme);
        Assert.Equal("b", sig.Parameters[1].Name.Lexeme);
    }

    [Fact]
    public void CallSignature_OptionalParameter()
    {
        var stmt = ParseDeclaration("interface Greeter { (name: string, greeting?: string): string; }");
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.CallSignatures);
        var sig = iface.CallSignatures[0];
        Assert.False(sig.Parameters[0].IsOptional);
        Assert.True(sig.Parameters[1].IsOptional);
    }

    [Fact]
    public void ConstructorSignature_BasicParsing()
    {
        var stmt = ParseDeclaration("interface Ctor { new (x: number): Point; }");
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.ConstructorSignatures);
        Assert.Single(iface.ConstructorSignatures);
        var sig = iface.ConstructorSignatures[0];
        Assert.Single(sig.Parameters);
        Assert.Equal("x", sig.Parameters[0].Name.Lexeme);
        Assert.Equal("Point", sig.ReturnType);
    }

    [Fact]
    public void ConstructorSignature_MultipleParameters()
    {
        var stmt = ParseDeclaration("interface Ctor { new (x: number, y: number): Point; }");
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.ConstructorSignatures);
        var sig = iface.ConstructorSignatures[0];
        Assert.Equal(2, sig.Parameters.Count);
    }

    [Fact]
    public void ConstructorSignature_OptionalParameter()
    {
        var stmt = ParseDeclaration("interface Ctor { new (name?: string): Greeter; }");
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.ConstructorSignatures);
        var sig = iface.ConstructorSignatures[0];
        Assert.True(sig.Parameters[0].IsOptional);
    }

    [Fact]
    public void Interface_BothCallAndConstructorSignatures()
    {
        var stmt = ParseDeclaration("""
            interface Factory {
                (x: number): string;
                new (name: string): Widget;
            }
            """);
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.CallSignatures);
        Assert.NotNull(iface.ConstructorSignatures);
        Assert.Single(iface.CallSignatures);
        Assert.Single(iface.ConstructorSignatures);
    }

    [Fact]
    public void Interface_MultipleCallSignatures()
    {
        var stmt = ParseDeclaration("""
            interface Overloaded {
                (x: number): number;
                (x: string): string;
            }
            """);
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.CallSignatures);
        Assert.Equal(2, iface.CallSignatures.Count);
    }

    [Fact]
    public void Interface_MembersAndSignatures()
    {
        var stmt = ParseDeclaration("""
            interface Counter {
                (): number;
                value: number;
                reset(): void;
            }
            """);
        var iface = Assert.IsType<Stmt.Interface>(stmt);
        Assert.NotNull(iface.CallSignatures);
        Assert.Single(iface.CallSignatures);
        Assert.Equal(2, iface.Members.Count);
    }

    [Fact]
    public void NewExpression_OnVariable()
    {
        var lexer = new Lexer("new x();");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var result = parser.Parse();
        var exprStmt = Assert.IsType<Stmt.Expression>(result.Statements[0]);
        var newExpr = Assert.IsType<Expr.New>(exprStmt.Expr);
        var callee = Assert.IsType<Expr.Variable>(newExpr.Callee);
        Assert.Equal("x", callee.Name.Lexeme);
    }

    [Fact]
    public void NewExpression_OnMemberAccess()
    {
        var lexer = new Lexer("new Ns.Class();");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var result = parser.Parse();
        var exprStmt = Assert.IsType<Stmt.Expression>(result.Statements[0]);
        var newExpr = Assert.IsType<Expr.New>(exprStmt.Expr);
        var callee = Assert.IsType<Expr.Get>(newExpr.Callee);
        Assert.Equal("Class", callee.Name.Lexeme);
        var obj = Assert.IsType<Expr.Variable>(callee.Object);
        Assert.Equal("Ns", obj.Name.Lexeme);
    }

    [Fact]
    public void NewExpression_WithTypeArguments()
    {
        var lexer = new Lexer("new Box<number>(42);");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var result = parser.Parse();
        var exprStmt = Assert.IsType<Stmt.Expression>(result.Statements[0]);
        var newExpr = Assert.IsType<Expr.New>(exprStmt.Expr);
        Assert.NotNull(newExpr.TypeArgs);
        Assert.Single(newExpr.TypeArgs);
        Assert.Equal("number", newExpr.TypeArgs[0]);
    }

    [Fact]
    public void NewExpression_NestedNamespace()
    {
        var lexer = new Lexer("new A.B.C();");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var result = parser.Parse();
        var exprStmt = Assert.IsType<Stmt.Expression>(result.Statements[0]);
        var newExpr = Assert.IsType<Expr.New>(exprStmt.Expr);
        // Should be a nested Get: (A.B).C
        var calleeGet = Assert.IsType<Expr.Get>(newExpr.Callee);
        Assert.Equal("C", calleeGet.Name.Lexeme);
        var innerGet = Assert.IsType<Expr.Get>(calleeGet.Object);
        Assert.Equal("B", innerGet.Name.Lexeme);
        var rootVar = Assert.IsType<Expr.Variable>(innerGet.Object);
        Assert.Equal("A", rootVar.Name.Lexeme);
    }
}
