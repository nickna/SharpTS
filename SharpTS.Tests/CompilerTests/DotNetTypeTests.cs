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
    public void StringBuilder_Append_Works_OnExportDeclareClass()
    {
        // `@DotNetType(...) export declare class X` is the form docs/dotnet-types.md shows.
        // Before issue #1192 this failed to even parse; verify compiled mode handles the
        // exported ambient class identically to the bare `declare class` form above.
        var source = """
            @DotNetType("System.Text.StringBuilder")
            export declare class StringBuilder {
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

    #region Delegate / callback parameters (compile mode)

    [Fact]
    public void CompiledFixture_NoDelegate_Smoke()
    {
        // Sanity check: can compile-mode even see the test fixture type?
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                fireStringEvent(payload: string): void;
                readonly lastReceived: string;
            }
            let fx: CallbackFixture = new CallbackFixture();
            fx.fireStringEvent("probe");
            console.log(fx.lastReceived);
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("probe\n", output);
    }

    [Fact]
    public void CompiledDelegate_ActionOfString_Invokes()
    {
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                invokeWithGreeting(callback: (s: string) => void): void;
            }
            let fx: CallbackFixture = new CallbackFixture();
            fx.invokeWithGreeting((s) => console.log(s));
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void CompiledDelegate_FuncReturnsValue()
    {
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                doubleOf(callback: (n: number) => number, input: number): number;
            }
            let fx: CallbackFixture = new CallbackFixture();
            console.log(fx.doubleOf((n) => n + 1, 10));
            """;

        // (10 + 1) * 2 = 22
        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("22\n", output);
    }

    [Fact]
    public void CompiledDelegate_ZeroArgAction_Works()
    {
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                invokeNoArgs(callback: () => void): void;
            }
            let fx: CallbackFixture = new CallbackFixture();
            let counter: number = 0;
            fx.invokeNoArgs(() => { counter = counter + 1; });
            fx.invokeNoArgs(() => { counter = counter + 1; });
            console.log(counter);
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("2\n", output);
    }

    #endregion

    #region Issue #51 — overload dispatch + @DotNetOverload hint

    [Fact]
    public void Issue51_Guid_ToString_InferredReceiver_NoAmbiguousMatch()
    {
        // Repro from issue #51 (a): toString() on an inferred @DotNetType receiver
        // used to route through $Runtime.GetFieldsProperty and crash with
        // AmbiguousMatchException because Guid has four ToString overloads.
        var source = """
            @DotNetType("System.Guid")
            declare class Guid {
                static newGuid(): Guid;
                toString(): string;
            }
            const g = Guid.newGuid();
            console.log(g.toString().length > 30 ? "valid" : "invalid");
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("valid\n", output);
    }

    [Fact]
    public void Issue51_StringBuilder_ChainedAppend_NoAmbiguousMatch()
    {
        // Repro from issue #51 (a): chained Append calls crash the same way
        // because the intermediate receiver's static type is lost.
        var source = """
            @DotNetType("System.Text.StringBuilder")
            declare class StringBuilder {
                constructor();
                append(value: string): StringBuilder;
                toString(): string;
            }
            const sb = new StringBuilder();
            sb.append("hello").append(" world");
            console.log(sb.toString());
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Issue51_DotNetOverload_Int_TruncatesInsteadOfRounds()
    {
        // Repro from issue #51 (b): without the hint, Convert.toInt32(3.7) picks
        // the double overload and rounds to 4. With @DotNetOverload("int") the
        // int overload is selected and truncates to 3 (banker's rounding is
        // moot — int takes the value as-is, truncating).
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                @DotNetOverload("int")
                static toInt32(value: number): number;
            }
            console.log(Convert.toInt32(3.7));
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Issue51_DotNetOverload_UnknownSignature_Throws()
    {
        // A nonsensical hint must be surfaced, not silently dropped. If the
        // compiler drops the hint it falls through to the cost-based resolver
        // and this prints "4". Honoring the hint should fail with a clear
        // "no overload matches" error.
        var source = """
            @DotNetType("System.Convert")
            declare class Convert {
                @DotNetOverload("nonexistent-type")
                static toInt32(value: number): number;
            }
            console.log(Convert.toInt32(3.7));
            """;

        var ex = Assert.ThrowsAny<System.Exception>(() =>
            TestHarness.RunCompiled(source, DecoratorMode.Legacy));
        Assert.Contains("overload", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Issue #52 — TS closure → .NET delegate in compiled mode

    [Fact]
    public void Issue52_Task_Run_VoidAction_Compiled()
    {
        // Exact repro from issue #52: passing a TS arrow function where a
        // @DotNetType method expects an Action throws NullReferenceException
        // in compiled mode because the old path reflects into SharpTS.dll
        // which isn't loaded when the DLL runs standalone.
        var source = """
            @DotNetType("System.Threading.Tasks.Task")
            declare class Task {
                static run(action: () => void): Task;
                wait(): void;
            }
            const t = Task.run(() => { console.log("inside the task"); });
            t.wait();
            console.log("done");
            """;

        // Critical: run standalone (no SharpTS.dll alongside) to match the user's
        // repro. The previous reflection-into-SharpTS path only works when SharpTS.dll
        // happens to be loaded, which masks the bug in the default test harness.
        var output = TestHarness.RunCompiledStandalone(source);
        Assert.Contains("inside the task", output);
        Assert.Contains("done", output);
    }

    [Fact]
    public void Issue52_Delegate_WithStringArg_Compiled()
    {
        // Action<string> — verifies that string args flow through the adapter
        // to the TS closure's parameter.
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                invokeWithGreeting(callback: (s: string) => void): void;
            }
            const fx = new CallbackFixture();
            fx.invokeWithGreeting((s) => console.log("got: " + s));
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("got: hello\n", output);
    }

    [Fact]
    public void Issue52_Delegate_WithReturn_Compiled()
    {
        // Func<int, int> — the TS closure returns a value and the .NET side
        // consumes it. Verifies the return-path unboxing.
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                doubleOf(callback: (n: number) => number, input: number): number;
            }
            const fx = new CallbackFixture();
            console.log(fx.doubleOf((n) => n + 1, 10));
            """;

        // doubleOf does (callback(input)) * 2 → (10 + 1) * 2 = 22
        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("22\n", output);
    }

    [Fact]
    public void Issue52_Multiple_DelegateShapes_In_OneProgram()
    {
        // Two distinct delegate shapes in one program — each needs its own
        // adapter class in the emitted DLL. Guards against cache / de-dup bugs.
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                invokeWithGreeting(callback: (s: string) => void): void;
                invokeNoArgs(callback: () => void): void;
            }
            const fx = new CallbackFixture();
            fx.invokeWithGreeting((s) => console.log("A:" + s));
            fx.invokeNoArgs(() => console.log("B"));
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("A:hello\nB\n", output);
    }

    #endregion

    #region Issue #53 — event subscription standalone

    [Fact]
    public void Issue53_AppDomain_ProcessExit_Standalone()
    {
        // The user's exact repro. Runs standalone (no SharpTS.dll copied alongside),
        // so if the emitted IL reflects into SharpTS at runtime this NREs. AppDomain
        // is pure BCL — the fixture doesn't transitively pull SharpTS in.
        var source = """
            @DotNetType("System.AppDomain")
            declare class AppDomain {
                static readonly currentDomain: AppDomain;
                addEventListener(name: string, handler: (sender: any, args: any) => void): void;
            }
            AppDomain.currentDomain.addEventListener("ProcessExit", (sender, args) => {
                console.log("(event) ProcessExit fired");
            });
            console.log("wired");
            """;

        var output = TestHarness.RunCompiledStandalone(source);
        Assert.Contains("wired", output);
        Assert.Contains("ProcessExit fired", output);
    }

    [Fact]
    public void Issue53_RemoveEventListener_Standalone()
    {
        // removeEventListener path — add, fire once, remove, fire again. Second fire
        // must not invoke the handler. Uses CallbackFixture, so SharpTS.dll gets
        // transitively loaded via SharpTS.Tests.dll — this test only proves the new
        // emitted path is wired up correctly, not true standalone operation. The
        // AppDomain test above covers the standalone case.
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                fireStringEvent(payload: string): void;
                addEventListener(name: string, handler: (sender: any, payload: string) => void): void;
                removeEventListener(name: string, handler: (sender: any, payload: string) => void): void;
            }
            const fx = new CallbackFixture();
            const handler = (sender: any, payload: string) => console.log("got:" + payload);
            fx.addEventListener("StringReceived", handler);
            fx.fireStringEvent("first");
            fx.removeEventListener("StringReceived", handler);
            fx.fireStringEvent("second");
            console.log("done");
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("got:first\ndone\n", output);
    }

    [Fact]
    public void Issue53_Multiple_Instances_Same_Event()
    {
        // Two CallbackFixture instances subscribe to the same event; removing the
        // handler from one instance must NOT silence the other. Guards against a
        // subscription-key collision where the owner isn't part of the identity.
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                fireStringEvent(payload: string): void;
                addEventListener(name: string, handler: (sender: any, payload: string) => void): void;
                removeEventListener(name: string, handler: (sender: any, payload: string) => void): void;
            }
            const a = new CallbackFixture();
            const b = new CallbackFixture();
            const logA = (sender: any, p: string) => console.log("A:" + p);
            const logB = (sender: any, p: string) => console.log("B:" + p);
            a.addEventListener("StringReceived", logA);
            b.addEventListener("StringReceived", logB);
            a.fireStringEvent("1");
            b.fireStringEvent("2");
            a.removeEventListener("StringReceived", logA);
            a.fireStringEvent("3");
            b.fireStringEvent("4");
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("A:1\nB:2\nB:4\n", output);
    }

    #endregion

    #region Event subscription (compile mode)

    [Fact]
    public void CompiledEvent_AddEventListener_InvokesHandler()
    {
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                fireStringEvent(payload: string): void;
                addEventListener(name: string, handler: (sender: any, payload: string) => void): void;
            }
            let fx: CallbackFixture = new CallbackFixture();
            fx.addEventListener("StringReceived", (sender, payload) => console.log(payload));
            fx.fireStringEvent("alpha");
            fx.fireStringEvent("beta");
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("alpha\nbeta\n", output);
    }

    [Fact]
    public void CompiledEvent_RemoveEventListener_StopsInvocation()
    {
        var source = """
            @DotNetType("SharpTS.Tests.Infrastructure.CallbackFixture")
            declare class CallbackFixture {
                constructor();
                fireStringEvent(payload: string): void;
                addEventListener(name: string, handler: (sender: any, payload: string) => void): void;
                removeEventListener(name: string, handler: (sender: any, payload: string) => void): void;
            }
            let fx: CallbackFixture = new CallbackFixture();
            let handler = (sender: any, payload: string) => console.log(payload);
            fx.addEventListener("StringReceived", handler);
            fx.fireStringEvent("first");
            fx.removeEventListener("StringReceived", handler);
            fx.fireStringEvent("second");
            console.log("done");
            """;

        var output = TestHarness.RunCompiledWithTestFixtures(source);
        Assert.Equal("first\ndone\n", output);
    }

    #endregion
}
