using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for function type annotations in various contexts.
/// Covers function type return types, optional parameters, rest parameters, and nested function types.
/// </summary>
public class FunctionTypeAnnotationTests
{
    #region Helpers

    private static Expr.ArrowFunction ParseArrowFromVarOrConst(string source)
    {
        var statements = TestHarness.ParseOrThrow(source);
        Assert.Single(statements);
        var initializer = statements[0] switch
        {
            Stmt.Var v => v.Initializer,
            Stmt.Const c => c.Initializer,
            _ => throw new InvalidOperationException($"Expected Var or Const, got {statements[0].GetType()}")
        };
        return Assert.IsType<Expr.ArrowFunction>(initializer);
    }

    #endregion

    #region Arrow Functions with Function Type Return

    [Fact]
    public void Arrow_FunctionTypeReturn_Simple()
    {
        // Arrow function that returns another function type
        var arrow = ParseArrowFromVarOrConst("const fn = (factor: number): (x: number) => number => factor * x;");
        Assert.Single(arrow.Parameters);
        Assert.Equal("factor", arrow.Parameters[0].Name.Lexeme);
        Assert.Equal("number", arrow.Parameters[0].Type);
        Assert.Equal("(number) => number", arrow.ReturnType);
    }

    [Fact]
    public void Arrow_FunctionTypeReturn_MultipleParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (a: string, b: number) => boolean => true;");
        Assert.Empty(arrow.Parameters);
        Assert.Equal("(string, number) => boolean", arrow.ReturnType);
    }

    [Fact]
    public void Arrow_FunctionTypeReturn_WithBlockBody()
    {
        var source = """
            const multiplier = (factor: number): (x: number) => number => {
                return (x: number): number => factor * x;
            };
            """;
        var arrow = ParseArrowFromVarOrConst(source);
        Assert.Single(arrow.Parameters);
        Assert.Equal("(number) => number", arrow.ReturnType);
        Assert.NotNull(arrow.BlockBody);
    }

    [Fact]
    public void Arrow_FunctionTypeReturn_NoReturnParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): () => void => () => {};");
        Assert.Empty(arrow.Parameters);
        Assert.Equal("() => void", arrow.ReturnType);
    }

    #endregion

    #region Function Type with Optional Parameters

    [Fact]
    public void FunctionType_OptionalParam()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (x?: number) => void => () => {};");
        Assert.Equal("(number?) => void", arrow.ReturnType);
    }

    [Fact]
    public void FunctionType_MixedOptionalParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (a: string, b?: number) => void => () => {};");
        Assert.Equal("(string, number?) => void", arrow.ReturnType);
    }

    [Fact]
    public void FunctionType_MultipleOptionalParams()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (a?: string, b?: number) => boolean => () => true;");
        Assert.Equal("(string?, number?) => boolean", arrow.ReturnType);
    }

    #endregion

    #region Function Type with Rest Parameters

    [Fact]
    public void FunctionType_RestParam()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (...args: number[]) => void => () => {};");
        Assert.Equal("(...number[]) => void", arrow.ReturnType);
    }

    [Fact]
    public void FunctionType_MixedWithRestParam()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (a: string, ...rest: any[]) => void => () => {};");
        Assert.Equal("(string, ...any[]) => void", arrow.ReturnType);
    }

    #endregion

    #region Function Type with this Parameter

    [Fact]
    public void FunctionType_ThisParam()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (this: Window, e: Event) => void => function() {};");
        Assert.Equal("(this: Window, Event) => void", arrow.ReturnType);
    }

    #endregion

    #region Nested Function Types

    [Fact]
    public void FunctionType_Nested_FunctionParam()
    {
        // A function that takes a function parameter
        var arrow = ParseArrowFromVarOrConst("const apply = (): (fn: (x: number) => number, val: number) => number => (fn, val) => fn(val);");
        Assert.Equal("((number) => number, number) => number", arrow.ReturnType);
    }

    [Fact]
    public void FunctionType_Nested_DeepNesting()
    {
        // Function returning a function returning a function
        var arrow = ParseArrowFromVarOrConst("const fn = (): () => () => number => () => () => 42;");
        Assert.Equal("() => () => number", arrow.ReturnType);
    }

    #endregion

    #region Variable/Const Declarations with Function Type

    [Fact]
    public void VarDecl_FunctionTypeAnnotation()
    {
        var statements = TestHarness.ParseOrThrow("let handler: (e: Event) => void;");
        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Equal("handler", varStmt.Name.Lexeme);
        Assert.Equal("(Event) => void", varStmt.TypeAnnotation);
    }

    [Fact]
    public void ConstDecl_FunctionTypeAnnotation()
    {
        var statements = TestHarness.ParseOrThrow("const callback: (x: number, y: number) => boolean = (a, b) => a > b;");
        Assert.Single(statements);
        var constStmt = Assert.IsType<Stmt.Const>(statements[0]);
        Assert.Equal("callback", constStmt.Name.Lexeme);
        Assert.Equal("(number, number) => boolean", constStmt.TypeAnnotation);
    }

    #endregion

    #region Arrow as Callback with Function Type Return

    [Fact]
    public void Arrow_CallbackArg_FunctionTypeReturn()
    {
        var statements = TestHarness.ParseOrThrow("arr.map((x): (y: number) => number => y => x * y);");
        var exprStmt = Assert.IsType<Stmt.Expression>(statements[0]);
        var call = Assert.IsType<Expr.Call>(exprStmt.Expr);
        Assert.Single(call.Arguments);
        var arrow = Assert.IsType<Expr.ArrowFunction>(call.Arguments[0]);
        Assert.Equal("(number) => number", arrow.ReturnType);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void FunctionType_ThreeNamedParams()
    {
        // Function type with three named parameters
        var statements = TestHarness.ParseOrThrow("let fn: (a: number, b: string, c: boolean) => void;");
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Equal("(number, string, boolean) => void", varStmt.TypeAnnotation);
    }

    [Fact]
    public void FunctionType_AllNamedParams()
    {
        // All params have names and types
        var statements = TestHarness.ParseOrThrow("let fn: (a: number, b: string) => void;");
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Equal("(number, string) => void", varStmt.TypeAnnotation);
    }

    [Fact]
    public void Arrow_FunctionTypeReturn_VoidReturn()
    {
        var arrow = ParseArrowFromVarOrConst("const fn = (): (x: number) => void => () => {};");
        Assert.Equal("(number) => void", arrow.ReturnType);
    }

    #endregion

    #region Untyped (implicit-any) parameters in function types

    [Fact]
    public void FunctionType_BareParameterName_Parses()
    {
        // `(x) => number`: x is a parameter name with implicit `any`, not a grouped type.
        Assert.NotEmpty(TestHarness.ParseOrThrow("let f: (x) => number;"));
    }

    [Fact]
    public void FunctionType_MultipleBareParameters_Parses()
    {
        Assert.NotEmpty(TestHarness.ParseOrThrow("let f: (x, y) => number;"));
    }

    [Fact]
    public void FunctionType_MixedTypedAndBareParameters_Parses()
    {
        Assert.NotEmpty(TestHarness.ParseOrThrow("let f: (x: number, y) => number;"));
    }

    [Fact]
    public void FunctionType_BareOptionalParameter_Parses()
    {
        // `(x?) => number`: optional parameter with implicit `any`.
        Assert.NotEmpty(TestHarness.ParseOrThrow("let f: (x?) => number;"));
    }

    [Fact]
    public void FunctionType_BareParameterInIndexSignature_Parses()
    {
        // The function type appears as an index-signature value: `[k: string]: (x) => number`.
        Assert.NotEmpty(TestHarness.ParseOrThrow("interface I { [k: string]: (x) => number; }"));
    }

    [Fact]
    public void GroupedType_StillParses_NotMistakenForFunctionType()
    {
        // Regression guard: a grouped/parenthesized type with no trailing `=>` stays a grouped type.
        Assert.NotEmpty(TestHarness.ParseOrThrow("let a: (number | string)[];"));
        Assert.NotEmpty(TestHarness.ParseOrThrow("let b: (Foo);"));
    }

    #endregion
}
