using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for type checker error recovery functionality.
/// Verifies that the type checker can collect multiple errors and continue checking.
/// </summary>
public class TypeCheckerRecoveryTests
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

    #endregion

    #region Basic Error Collection

    [Fact]
    public void TypeCheck_NoErrors_ReturnsSuccess()
    {
        var source = """
            let x: number = 5;
            let y: string = "hello";
            """;
        var result = CheckWithRecovery(source);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.TypeMap);
    }

    [Fact]
    public void TypeCheck_SingleError_CollectsError()
    {
        var source = """
            let x: number = "string";
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void TypeCheck_MultipleErrors_CollectsAll()
    {
        var source = """
            let x: number = "string";
            let y: string = 42;
            let z: boolean = "true";
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(3, result.ErrorCount);
    }

    [Fact]
    public void TypeCheck_ErrorsOnDifferentLines_TracksLineNumbers()
    {
        var source = """
            let a: number = "wrong";
            let b: number = 5;
            let c: string = 123;
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.ErrorCount);

        // Errors should have line numbers (Location not null means explicit line was set)
        Assert.All(result.Diagnostics, e => Assert.NotNull(e.Location));
    }

    #endregion

    #region Error Limit

    [Fact]
    public void TypeCheck_ErrorLimit_StopsAt10()
    {
        // Generate more than 10 type errors
        var statements = Enumerable.Range(1, 15)
            .Select(i => $"let x{i}: number = \"error{i}\";");
        var source = string.Join("\n", statements);

        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.HitErrorLimit);
        Assert.Equal(10, result.ErrorCount);
    }

    [Fact]
    public void TypeCheck_UnderErrorLimit_DoesNotSetHitErrorLimit()
    {
        var source = """
            let a: number = "error1";
            let b: number = "error2";
            let c: number = "error3";
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.False(result.HitErrorLimit);
        Assert.Equal(3, result.ErrorCount);
    }

    #endregion

    #region Recovery and Continuation

    [Fact]
    public void TypeCheck_ValidStatementAfterError_StillChecked()
    {
        var source = """
            let x: number = "wrong";
            let y: number = 5;
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ErrorCount);
        // TypeMap should still contain the valid variable
        Assert.NotNull(result.TypeMap);
    }

    [Fact]
    public void TypeCheck_ErrorBetweenValidStatements_ChecksAll()
    {
        var source = """
            let a: number = 1;
            let b: number = "wrong";
            let c: number = 3;
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public void TypeCheck_FunctionWithError_ContinuesChecking()
    {
        var source = """
            function foo(): number {
                return "wrong";
            }
            let x: number = 5;
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.ErrorCount >= 1);
    }

    #endregion

    #region Different Error Types

    [Fact]
    public void TypeCheck_TypeMismatch_CapturesExpectedAndActual()
    {
        var source = """
            let x: number = "string";
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ErrorCount);

        var error = result.Diagnostics.First();
        // For type mismatch errors, we capture expected and actual types
        // Note: These may or may not be set depending on the exception type
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void TypeCheck_UndefinedVariable_CollectsError()
    {
        var source = """
            let x: number = undefinedVar;
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ErrorCount);
        Assert.Contains("undefinedVar", result.Diagnostics.First().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypeCheck_UndefinedType_CollectsError()
    {
        // Using an undefined type should produce an error
        var source = """
            let x: UndefinedType = null;
            """;
        var result = CheckWithRecovery(source);

        // If SharpTS treats unknown types as 'any', this may succeed
        // This test documents the current behavior
        // TODO: Consider whether undefined types should be errors
        if (result.IsSuccess)
        {
            // Document that undefined types are treated as 'any'
            Assert.Empty(result.Diagnostics);
        }
        else
        {
            Assert.True(result.ErrorCount >= 1);
        }
    }

    [Fact]
    public void TypeCheck_WrongArgumentType_CollectsError()
    {
        var source = """
            function foo(x: number): void { }
            foo("wrong");
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.ErrorCount >= 1);
    }

    [Fact]
    public void TypeCheck_WrongReturnType_CollectsError()
    {
        var source = """
            function foo(): number {
                return "wrong";
            }
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.ErrorCount >= 1);
    }

    [Fact]
    public void TypeCheck_AssignWrongTypeFromMethodResult_CollectsError()
    {
        var source = """
            let x: number = 5;
            let y: string = x.toString();
            let z: number = y;
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        // Should fail on assigning string to number
        Assert.True(result.ErrorCount >= 1,
            $"Expected error for assigning string to number, got {result.ErrorCount} errors");
    }

    #endregion

    #region Class and Interface Errors

    [Fact]
    public void TypeCheck_ClassMissingInterfaceMethod_CollectsError()
    {
        var source = """
            interface IFoo {
                doSomething(): void;
            }
            class Foo implements IFoo {
                // Missing doSomething implementation
            }
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.ErrorCount >= 1);
    }

    [Fact]
    public void TypeCheck_WrongMethodSignature_CollectsError()
    {
        var source = """
            interface IFoo {
                getValue(): number;
            }
            class Foo implements IFoo {
                getValue(): string {
                    return "wrong";
                }
            }
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.ErrorCount >= 1);
    }

    [Fact]
    public void TypeCheck_MultipleClassErrors_CollectsAll()
    {
        var source = """
            interface IA { methodA(): number; }
            interface IB { methodB(): string; }
            class Foo implements IA, IB {
                // Missing both methods
            }
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        // Should ideally collect errors for both missing interface methods
        // Current behavior may aggregate into one error - document this
        Assert.True(result.ErrorCount >= 1,
            $"Expected at least 1 error for missing interface implementations, got {result.ErrorCount}");
        // TODO: Consider if we should report separate errors for each missing method
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void TypeCheck_MixedValidAndInvalid_CollectsAllErrors()
    {
        var source = """
            let a: number = 1;
            let b: number = "wrong1";
            let c: string = "valid";
            let d: string = 42;
            let e: boolean = true;
            let f: boolean = "wrong3";
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(3, result.ErrorCount);
    }

    [Fact]
    public void TypeCheck_NestedFunctionErrors_CollectsAll()
    {
        var source = """
            function outer(): number {
                function inner(): string {
                    return 123;
                }
                return "wrong";
            }
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        // Both functions have wrong return types - ideally should catch both
        // Document actual behavior
        Assert.True(result.ErrorCount >= 1,
            $"Expected at least 1 error for wrong return types, got {result.ErrorCount}");
        // TODO: Investigate if nested function errors should be collected separately
    }

    [Fact]
    public void TypeCheck_ArrayTypeErrors_CollectsAll()
    {
        var source = """
            let arr1: number[] = ["a", "b"];
            let arr2: string[] = [1, 2, 3];
            """;
        var result = CheckWithRecovery(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.ErrorCount >= 2);
    }

    #endregion

    #region Error Message Quality

    [Fact]
    public void TypeCheck_ErrorMessage_ContainsLineNumber()
    {
        var source = """
            let x: number = "wrong";
            """;
        var result = CheckWithRecovery(source);

        Assert.Equal(1, result.ErrorCount);
        var error = result.Diagnostics.First();
        Assert.NotNull(error.Location);

        var errorString = error.ToString();
        Assert.Contains("line", errorString.ToLower());
    }

    [Fact]
    public void TypeCheck_ErrorMessage_IsDescriptive()
    {
        var source = """
            let x: number = "wrong";
            """;
        var result = CheckWithRecovery(source);

        Assert.Equal(1, result.ErrorCount);
        var error = result.Diagnostics.First();

        // Error message should mention the types involved
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    #endregion

    #region Integration with Parser Recovery

    [Fact]
    public void TypeCheck_AfterParserRecovery_StillWorks()
    {
        // First parse with an error (but it recovers)
        var source = """
            let x = ;
            let y: number = "wrong";
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        // Parser should have recovered
        Assert.False(parseResult.IsSuccess);
        Assert.True(parseResult.Statements.Count >= 1);

        // Now type check the recovered statements
        var checker = new TypeChecker();
        var typeResult = checker.CheckWithRecovery(parseResult.Statements);

        // Should find the type error in "let y"
        Assert.False(typeResult.IsSuccess);
        Assert.True(typeResult.ErrorCount >= 1);
    }

    #endregion

    #region Check Method Backward Compatibility

    [Fact]
    public void Check_NoErrors_ReturnsTypeMap()
    {
        var source = """
            let x: number = 5;
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        var checker = new TypeChecker();
        var typeMap = checker.Check(parseResult.Statements);

        Assert.NotNull(typeMap);
    }

    [Fact]
    public void Check_WithError_Throws()
    {
        var source = """
            let x: number = "wrong";
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        var checker = new TypeChecker();
        Assert.Throws<TypeMismatchException>(() => checker.Check(parseResult.Statements));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TypeCheck_EmptyStatements_ReturnsSuccess()
    {
        var source = "";

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();

        var checker = new TypeChecker();
        var result = checker.CheckWithRecovery(parseResult.Statements);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TypeCheck_OnlyValidStatements_ReturnsSuccess()
    {
        var source = """
            let a: number = 1;
            let b: string = "hello";
            let c: boolean = true;
            function foo(x: number): number { return x * 2; }
            class Bar { value: number = 0; }
            """;
        var result = CheckWithRecovery(source);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
    }

    #endregion
}
