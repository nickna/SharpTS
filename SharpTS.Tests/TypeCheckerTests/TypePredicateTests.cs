using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for TypeScript type predicates: "x is T" and "asserts x is T"
/// </summary>
public class TypePredicateTests
{
    #region Helpers

    private static TypeCheckDiagnosticResult CheckWithRecovery(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        if (!parseResult.IsSuccess)
            throw new Exception($"Parse failed: {parseResult.Diagnostics.First()}");

        var checker = new TypeChecker();
        return checker.CheckWithRecovery(parseResult.Statements);
    }

    private static void AssertTypeChecks(string source)
    {
        var result = CheckWithRecovery(source);
        Assert.True(result.IsSuccess, $"Type check failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    private static void AssertTypeCheckFails(string source, string? expectedError = null)
    {
        var result = CheckWithRecovery(source);
        Assert.False(result.IsSuccess, "Expected type check to fail");
        if (expectedError != null)
        {
            Assert.Contains(result.Errors, e => e.Message.Contains(expectedError));
        }
    }

    #endregion

    #region Parser Tests - Regular Type Predicates

    [Fact]
    public void Parse_RegularTypePredicate_IsString()
    {
        var source = """
            function isString(x: unknown): x is string {
                return typeof x === "string";
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void Parse_RegularTypePredicate_IsNumber()
    {
        var source = """
            function isNumber(x: unknown): x is number {
                return typeof x === "number";
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void Parse_RegularTypePredicate_CustomType()
    {
        var source = """
            interface Dog {
                bark(): void;
            }
            function isDog(animal: unknown): animal is Dog {
                return true;
            }
            """;
        AssertTypeChecks(source);
    }

    #endregion

    #region Parser Tests - Assertion Predicates

    [Fact]
    public void Parse_AssertionPredicate_AssertsIsString()
    {
        var source = """
            function assertIsString(x: unknown): asserts x is string {
                if (typeof x !== "string") throw new Error("Not a string");
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void Parse_AssertionPredicate_AssertsNonNull()
    {
        var source = """
            function assertExists(x: unknown): asserts x {
                if (x == null) throw new Error("Value is null or undefined");
            }
            """;
        AssertTypeChecks(source);
    }

    #endregion

    #region Parser Tests - Arrow Functions

    [Fact]
    public void Parse_ArrowFunction_TypePredicate()
    {
        var source = """
            const isNumber = (x: unknown): x is number => typeof x === "number";
            """;
        AssertTypeChecks(source);
    }

    #endregion

    #region Validation Tests - Invalid Parameter Names

    [Fact]
    public void Validate_TypePredicate_InvalidParameterName_Fails()
    {
        var source = """
            function isString(x: unknown): y is string {
                return typeof x === "string";
            }
            """;
        AssertTypeCheckFails(source, "y");
    }

    [Fact]
    public void Validate_AssertsPredicate_InvalidParameterName_Fails()
    {
        var source = """
            function assertIsString(x: unknown): asserts y is string {
                if (typeof x !== "string") throw new Error();
            }
            """;
        AssertTypeCheckFails(source, "y");
    }

    #endregion

    #region Type Narrowing Tests - Regular Predicates

    [Fact]
    public void TypeNarrowing_RegularPredicate_NarrowsInIfBranch()
    {
        var source = """
            function isString(x: unknown): x is string {
                return typeof x === "string";
            }

            function test(value: unknown): void {
                if (isString(value)) {
                    // value should be narrowed to string here
                    let len: number = value.length;
                }
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void TypeNarrowing_RegularPredicate_UnionType()
    {
        var source = """
            function isString(x: unknown): x is string {
                return typeof x === "string";
            }

            function test(value: string | number): void {
                if (isString(value)) {
                    let upper: string = value.toUpperCase();
                }
            }
            """;
        AssertTypeChecks(source);
    }

    #endregion

    #region Type Narrowing Tests - Assertion Predicates

    [Fact]
    public void TypeNarrowing_AssertionPredicate_NarrowsSubsequentCode()
    {
        var source = """
            function assertIsString(x: unknown): asserts x is string {
                if (typeof x !== "string") throw new Error("Not a string");
            }

            function test(value: unknown): void {
                assertIsString(value);
                // value should be narrowed to string after assertion
                let len: number = value.length;
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void TypeNarrowing_AssertsNonNull_RemovesNullUndefined()
    {
        var source = """
            function assertExists(x: unknown): asserts x {
                if (x == null) throw new Error();
            }

            function test(value: string | null | undefined): void {
                assertExists(value);
                // value should be narrowed to string (null/undefined removed)
                let len: number = value.length;
            }
            """;
        AssertTypeChecks(source);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void TypePredicate_WithGenericType()
    {
        var source = """
            function isArray<T>(x: unknown): x is T[] {
                return Array.isArray(x);
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void TypePredicate_MultiplePredicates()
    {
        var source = """
            function isString(x: unknown): x is string {
                return typeof x === "string";
            }

            function isNumber(x: unknown): x is number {
                return typeof x === "number";
            }

            function process(value: unknown): string {
                if (isString(value)) {
                    return value;
                }
                if (isNumber(value)) {
                    return value.toString();
                }
                return "unknown";
            }
            """;
        AssertTypeChecks(source);
    }

    #endregion

    #region Multi-Parameter Predicate Tests

    [Fact]
    public void TypePredicate_OnSecondParameter_NarrowsCorrectly()
    {
        // Test that predicate on second parameter narrows the correct argument
        var source = """
            function checkSecond(flag: boolean, value: unknown): value is string {
                return typeof value === "string";
            }

            function test(x: unknown): void {
                if (checkSecond(true, x)) {
                    // x should be narrowed to string, not the boolean flag
                    let s: string = x;
                }
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void TypePredicate_OnThirdParameter_NarrowsCorrectly()
    {
        // Test that predicate on third parameter narrows the correct argument
        var source = """
            function checkThird(a: number, b: number, value: unknown): value is string {
                return typeof value === "string";
            }

            function test(x: unknown): void {
                if (checkThird(1, 2, x)) {
                    let s: string = x;
                }
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void AssertionPredicate_OnSecondParameter_NarrowsCorrectly()
    {
        // Test that assertion predicate on second parameter narrows the correct argument
        var source = """
            function assertSecond(flag: boolean, value: unknown): asserts value is string {
                if (typeof value !== "string") throw new Error();
            }

            function test(y: unknown): void {
                assertSecond(true, y);
                // y should be narrowed to string after assertion
                let t: string = y;
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void AssertsNonNull_OnSecondParameter_NarrowsCorrectly()
    {
        // Test that asserts x on second parameter narrows the correct argument
        var source = """
            function assertSecondExists(flag: boolean, value: unknown): asserts value {
                if (value == null) throw new Error();
            }

            function test(z: string | null | undefined): void {
                assertSecondExists(true, z);
                // z should have null/undefined removed
                let len: number = z.length;
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void TypePredicate_FirstParameterStillWorks()
    {
        // Regression test to ensure first parameter predicates still work
        var source = """
            function isString(x: unknown): x is string {
                return typeof x === "string";
            }

            function test(value: unknown): void {
                if (isString(value)) {
                    let s: string = value;
                }
            }
            """;
        AssertTypeChecks(source);
    }

    [Fact]
    public void TypePredicate_WrongParameterNotNarrowed()
    {
        // Test that narrowing doesn't happen on the wrong parameter
        var source = """
            function checkSecond(flag: unknown, value: unknown): value is string {
                return typeof value === "string";
            }

            function test(a: unknown, b: unknown): void {
                if (checkSecond(a, b)) {
                    // b should be narrowed to string, a should stay unknown
                    let s: string = b;
                    // This should fail - a is still unknown
                    let t: string = a;
                }
            }
            """;
        AssertTypeCheckFails(source);
    }

    [Fact]
    public void TypePredicate_MultipleParameters_UnionNarrowing()
    {
        // Test union narrowing with multi-parameter predicate
        var source = """
            function validateSecond(mode: "strict" | "loose", value: string | number): value is string {
                return typeof value === "string";
            }

            function test(v: string | number): void {
                if (validateSecond("strict", v)) {
                    let upper: string = v.toUpperCase();
                }
            }
            """;
        AssertTypeChecks(source);
    }

    #endregion
}
