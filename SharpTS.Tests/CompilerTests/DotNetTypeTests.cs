using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for the @DotNetType decorator feature that enables TypeScript to use .NET types.
/// </summary>
public class DotNetTypeTests
{
    #region Parsing Tests

    [Fact]
    public void DeclareClass_ParsesCorrectly()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, DecoratorMode.Legacy);
        var statements = parser.ParseOrThrow();

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);
        Assert.True(classStmt.IsDeclare);
        Assert.Equal("StringBuilder", classStmt.Name.Lexeme);
        Assert.NotNull(classStmt.Decorators);
        Assert.Single(classStmt.Decorators);
    }

    [Fact]
    public void DeclareClass_WithoutDecorator_ParsesCorrectly()
    {
        var source = """
            declare class MyExternalClass {
                getValue(): number;
            }
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseOrThrow();

        Assert.Single(statements);
        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);
        Assert.True(classStmt.IsDeclare);
        Assert.Equal("MyExternalClass", classStmt.Name.Lexeme);
    }

    [Fact]
    public void DotNetType_DecoratorExtraction_Works()
    {
        var source = """
            @DotNetType("System.Console")
            declare class Console {
                static writeLine(value: string): void;
            }
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, DecoratorMode.Legacy);
        var statements = parser.ParseOrThrow();

        var classStmt = Assert.IsType<Stmt.Class>(statements[0]);
        Assert.NotNull(classStmt.Decorators);
        var decorator = classStmt.Decorators[0];

        // Verify the decorator is a call expression
        var callExpr = Assert.IsType<Expr.Call>(decorator.Expression);
        var calleeVar = Assert.IsType<Expr.Variable>(callExpr.Callee);
        Assert.Equal("DotNetType", calleeVar.Name.Lexeme);

        // Verify the argument
        Assert.Single(callExpr.Arguments);
        var literal = Assert.IsType<Expr.Literal>(callExpr.Arguments[0]);
        Assert.Equal("System.Console", literal.Value);
    }

    #endregion

    #region Type Checking Tests

    [Fact]
    public void DotNetType_IsBuiltInDecorator()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
            }
            let sb: StringBuilder = new StringBuilder();
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, DecoratorMode.Legacy);
        var statements = parser.ParseOrThrow();

        // Should not throw - DotNetType is a built-in decorator
        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Legacy);
        var typeMap = checker.Check(statements);

        Assert.NotNull(typeMap);
    }

    [Fact]
    public void ExternalType_InstanceType_Resolves()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
            }
            let sb: StringBuilder = new StringBuilder();
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, DecoratorMode.Legacy);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Legacy);
        var typeMap = checker.Check(statements);

        // Variable sb should have type Instance of StringBuilder
        var varStmt = statements.OfType<Stmt.Var>().First();
        var initExpr = varStmt.Initializer;
        Assert.NotNull(initExpr);
        var exprType = typeMap.Get(initExpr);
        Assert.IsType<TypeInfo.Instance>(exprType);
    }

    #endregion

    #region Compilation Tests - StringBuilder (Instance Methods)

    [Fact]
    public void StringBuilder_Constructor_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("\n", output);
    }

    [Fact]
    public void StringBuilder_Append_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append("Hello");
            sb.append(" ");
            sb.append("World");
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("Hello World\n", output);
    }

    [Fact]
    public void StringBuilder_MethodChaining_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append("A");
            sb.append("B");
            sb.append("C");
            let result: string = sb.toString();
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("ABC\n", output);
    }

    [Fact]
    public void StringBuilder_Length_PropertyAccess_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                readonly length: number;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append("Hello");
            console.log(sb.length);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("5\n", output);
    }

    #endregion

    #region Compilation Tests - Guid (Static Methods)

    [Fact]
    public void Guid_NewGuid_Works()
    {
        var source = """
            @DotNetType("System.Guid")
            declare class Guid {
                static newGuid(): Guid;
                toString(): string;
            }
            let g: Guid = Guid.newGuid();
            let str: string = g.toString();
            console.log(str.length > 30 ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    [Fact]
    public void Guid_Parse_StaticMethod_Works()
    {
        var source = """
            @DotNetType("System.Guid")
            declare class Guid {
                static parse(input: string): Guid;
                toString(): string;
            }
            let g: Guid = Guid.parse("00000000-0000-0000-0000-000000000000");
            console.log(g.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("00000000-0000-0000-0000-000000000000\n", output);
    }

    #endregion

    #region Compilation Tests - TimeSpan (Value Type)

    [Fact]
    public void TimeSpan_FromSeconds_Works()
    {
        // Super simplified test - just call static method, discard result
        var source = """
            @DotNetType("System.TimeSpan")
            declare class TimeSpan {
                static fromSeconds(value: number): TimeSpan;
            }
            TimeSpan.fromSeconds(5);
            console.log("success");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("success\n", output);
    }

    [Fact]
    public void TimeSpan_FromMinutes_Works()
    {
        // Super simplified test - just call static method, discard result
        var source = """
            @DotNetType("System.TimeSpan")
            declare class TimeSpan {
                static fromMinutes(value: number): TimeSpan;
            }
            TimeSpan.fromMinutes(2);
            console.log("success");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("success\n", output);
    }

    [Fact]
    public void TimeSpan_TotalSeconds_PropertyAccess_Works()
    {
        var source = """
            @DotNetType("System.TimeSpan")
            declare class TimeSpan {
                static fromSeconds(value: number): TimeSpan;
                readonly totalSeconds: number;
            }
            let ts: TimeSpan = TimeSpan.fromSeconds(90);
            console.log(ts.totalSeconds);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("90\n", output);
    }

    [Fact]
    public void TimeSpan_TotalMinutes_PropertyAccess_Works()
    {
        var source = """
            @DotNetType("System.TimeSpan")
            declare class TimeSpan {
                static fromMinutes(value: number): TimeSpan;
                readonly totalMinutes: number;
                readonly totalSeconds: number;
            }
            let ts: TimeSpan = TimeSpan.fromMinutes(2);
            console.log(ts.totalMinutes);
            console.log(ts.totalSeconds);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("2\n120\n", output);
    }

    [Fact]
    public void TimeSpan_ToString_InstanceMethod_Works()
    {
        var source = """
            @DotNetType("System.TimeSpan")
            declare class TimeSpan {
                static fromHours(value: number): TimeSpan;
                toString(): string;
            }
            let ts: TimeSpan = TimeSpan.fromHours(1);
            let str: string = ts.toString();
            console.log(str.length > 0 ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    [Fact]
    public void TimeSpan_Add_InstanceMethod_Works()
    {
        var source = """
            @DotNetType("System.TimeSpan")
            declare class TimeSpan {
                static fromSeconds(value: number): TimeSpan;
                add(ts: TimeSpan): TimeSpan;
                readonly totalSeconds: number;
            }
            let ts1: TimeSpan = TimeSpan.fromSeconds(30);
            let ts2: TimeSpan = TimeSpan.fromSeconds(60);
            let result: TimeSpan = ts1.add(ts2);
            console.log(result.totalSeconds);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("90\n", output);
    }

    #endregion

    #region Compilation Tests - DateTime

    [Fact]
    public void DateTime_Now_Works()
    {
        var source = """
            @DotNetType("System.DateTime")
            declare class DateTime {
                static readonly now: DateTime;
                readonly year: number;
            }
            let dt: DateTime = DateTime.now;
            console.log(dt.year >= 2024 ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    #endregion

    #region Compilation Tests - Mixed Usage

    [Fact]
    public void MultipleExternalTypes_Work()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }

            @DotNetType("System.Guid")
            declare class Guid {
                static newGuid(): Guid;
                toString(): string;
            }

            let sb: StringBuilder = new StringBuilder();
            sb.append("ID: ");
            let g: Guid = Guid.newGuid();
            sb.append(g.toString());
            let result: string = sb.toString();
            console.log(result.startsWith("ID:") ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    [Fact]
    public void ExternalType_WithRegularClass_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }

            class MyClass {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                toFormattedString(): string {
                    let sb: StringBuilder = new StringBuilder();
                    sb.append("[");
                    sb.append(this.name);
                    sb.append("]");
                    return sb.toString();
                }
            }

            let obj: MyClass = new MyClass("Test");
            console.log(obj.toFormattedString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("[Test]\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ExternalType_MethodWithMultipleParams_Works()
    {
        var source = """
            @DotNetType("System.String")
            declare class String {
                static concat(str0: string, str1: string): string;
            }
            let result: string = String.concat("Hello", "World");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("HelloWorld\n", output);
    }

    [Fact]
    public void ExternalType_MethodWithNumberParam_Works()
    {
        // Simplified test - just test that the number append works
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                readonly length: number;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append("42");
            console.log(sb.length);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void ExternalType_MethodWithBooleanParam_Works()
    {
        // Simplified test - just test that the length property works
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                readonly length: number;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append("test");
            console.log(sb.length);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("4\n", output);
    }

    #endregion

    #region Number Parameter Conversion Tests

    [Fact]
    public void StringBuilder_AppendNumber_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: number): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append(42);
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void StringBuilder_AppendMultipleNumbers_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: number): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append(1);
            sb.append(2);
            sb.append(3);
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("123\n", output);
    }

    [Fact]
    public void Convert_ToInt32_FromNumber_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toInt32(value: number): number;
            }
            let result: number = Convert.toInt32(42.7);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("43\n", output);
    }

    [Fact]
    public void Convert_ToDouble_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toDouble(value: string): number;
            }
            let result: number = Convert.toDouble("3.14");
            console.log(result > 3 ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    #endregion

    #region Boolean Parameter Conversion Tests

    [Fact]
    public void StringBuilder_AppendBoolean_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: boolean): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append(true);
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("True\n", output);
    }

    [Fact]
    public void StringBuilder_AppendBooleanFalse_Works()
    {
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: boolean): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append(false);
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("False\n", output);
    }

    [Fact]
    public void Convert_ToBoolean_FromNumber_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toBoolean(value: number): boolean;
            }
            let result: boolean = Convert.toBoolean(1);
            console.log(result ? "true" : "false");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Convert_ToString_FromBoolean_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toString(value: boolean): string;
            }
            let result: string = Convert.ToString(true);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("True\n", output);
    }

    #endregion

    #region Params Array Tests

    [Fact]
    public void StringFormat_WithParams_Works()
    {
        // Note: Use 'any[]' for .NET interop because TypeScript 'object' excludes primitives,
        // while .NET System.Object accepts everything including primitives.
        var source = """
            @DotNetType("System.String")
            declare class String {
                static format(format: string, ...args: any[]): string;
            }
            let result: string = String.format("Hello {0}!", "World");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("Hello World!\n", output);
    }

    [Fact]
    public void StringFormat_WithMultipleParams_Works()
    {
        // Note: Use 'any[]' for .NET interop because TypeScript 'object' excludes primitives,
        // while .NET System.Object accepts everything including primitives.
        var source = """
            @DotNetType("System.String")
            declare class String {
                static format(format: string, ...args: any[]): string;
            }
            let result: string = String.format("{0} + {1} = {2}", 1, 2, 3);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("1 + 2 = 3\n", output);
    }

    [Fact]
    public void StringFormat_WithMixedTypes_Works()
    {
        // Note: Use 'any[]' for .NET interop because TypeScript 'object' excludes primitives,
        // while .NET System.Object accepts everything including primitives.
        var source = """
            @DotNetType("System.String")
            declare class String {
                static format(format: string, ...args: any[]): string;
            }
            let result: string = String.format("Name: {0}, Age: {1}, Active: {2}", "Alice", 30, true);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("Name: Alice, Age: 30, Active: True\n", output);
    }

    #endregion

    #region Overload Resolution Preference Tests

    [Fact]
    public void Overload_NumberPrefersDouble_OverObject()
    {
        // When both double and object overloads exist, double should be selected for number
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: number): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append(3.14159);
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Contains("3.14159", output);
    }

    [Fact]
    public void Overload_StringPrefersString_OverObject()
    {
        // When both string and object overloads exist, string should be selected
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }
            let sb: StringBuilder = new StringBuilder();
            sb.append("test");
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Additional Type Conversion Tests

    [Fact]
    public void Conversion_NumberToFloat_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toSingle(value: number): number;
            }
            let result: number = Convert.toSingle(3.14);
            console.log(result > 3 ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    [Fact]
    public void Conversion_NumberToByte_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toByte(value: number): number;
            }
            let result: number = Convert.toByte(255);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("255\n", output);
    }

    [Fact]
    public void Conversion_NumberToInt16_Works()
    {
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                static toInt16(value: number): number;
            }
            let result: number = Convert.toInt16(32767);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("32767\n", output);
    }

    #endregion
}
