using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Error objects and subtypes. Runs against both interpreter and compiler.
/// </summary>
public class ErrorTests
{
    #region Constructor Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_NoArgs_CreatesErrorWithEmptyMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new Error();
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error\n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_WithMessage_CreatesErrorWithMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new Error('Something went wrong');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error\nSomething went wrong\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_CalledWithoutNew_StillCreatesError(ExecutionMode mode)
    {
        var source = @"
            let e = Error('Without new');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error\nWithout new\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_ToString_FormatsCorrectly(ExecutionMode mode)
    {
        var source = @"
            let e = new Error('Test error');
            console.log(e.toString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error: Test error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_ToString_NoMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new Error();
            console.log(e.toString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_HasStackProperty(ExecutionMode mode)
    {
        var source = @"
            let e = new Error('Test');
            console.log(typeof e.stack);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    #endregion

    #region Error Subtype Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeError_CreatesWithCorrectName(ExecutionMode mode)
    {
        var source = @"
            let e = new TypeError('Invalid type');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError\nInvalid type\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RangeError_CreatesWithCorrectName(ExecutionMode mode)
    {
        var source = @"
            let e = new RangeError('Out of range');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("RangeError\nOut of range\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReferenceError_CreatesWithCorrectName(ExecutionMode mode)
    {
        var source = @"
            let e = new ReferenceError('Undefined variable');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ReferenceError\nUndefined variable\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SyntaxError_CreatesWithCorrectName(ExecutionMode mode)
    {
        var source = @"
            let e = new SyntaxError('Unexpected token');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("SyntaxError\nUnexpected token\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void URIError_CreatesWithCorrectName(ExecutionMode mode)
    {
        var source = @"
            let e = new URIError('Invalid URI');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("URIError\nInvalid URI\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EvalError_CreatesWithCorrectName(ExecutionMode mode)
    {
        var source = @"
            let e = new EvalError('Eval failed');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("EvalError\nEval failed\n", output);
    }

    #endregion

    #region Error Subtype Without New

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeError_CalledWithoutNew_StillCreatesError(ExecutionMode mode)
    {
        var source = @"
            let e = TypeError('Without new');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError\nWithout new\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RangeError_CalledWithoutNew_StillCreatesError(ExecutionMode mode)
    {
        var source = @"
            let e = RangeError('Without new');
            console.log(e.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("RangeError\n", output);
    }

    #endregion

    #region AggregateError Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AggregateError_WithErrors_CreatesCorrectly(ExecutionMode mode)
    {
        var source = @"
            let errors = [new Error('First'), new Error('Second')];
            let e = new AggregateError(errors, 'Multiple errors');
            console.log(e.name);
            console.log(e.message);
            console.log(e.errors.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("AggregateError\nMultiple errors\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AggregateError_WithEmptyArray_CreatesCorrectly(ExecutionMode mode)
    {
        var source = @"
            let e = new AggregateError([], 'No errors');
            console.log(e.name);
            console.log(e.message);
            console.log(e.errors.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("AggregateError\nNo errors\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AggregateError_DefaultMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new AggregateError([new Error('Test')]);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("All promises were rejected\n", output);
    }

    #endregion

    #region Mutable Properties Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_NameIsMutable(ExecutionMode mode)
    {
        var source = @"
            let e = new Error('Test');
            e.name = 'CustomError';
            console.log(e.name);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("CustomError\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_MessageIsMutable(ExecutionMode mode)
    {
        var source = @"
            let e = new Error('Original');
            e.message = 'Modified';
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Modified\n", output);
    }

    #endregion

    #region Throw/Catch Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_CanBeThrown(ExecutionMode mode)
    {
        var source = @"
            try {
                throw new Error('Thrown error');
            } catch (e) {
                console.log(e.name);
                console.log(e.message);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error\nThrown error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeError_CanBeThrown(ExecutionMode mode)
    {
        var source = @"
            try {
                throw new TypeError('Type error thrown');
            } catch (e) {
                console.log(e.name);
                console.log(e.message);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError\nType error thrown\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Error_RethrowPreservesProperties(ExecutionMode mode)
    {
        var source = @"
            try {
                try {
                    throw new RangeError('Inner error');
                } catch (inner) {
                    inner.message = 'Modified in inner';
                    throw inner;
                }
            } catch (outer) {
                console.log(outer.name);
                console.log(outer.message);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("RangeError\nModified in inner\n", output);
    }

    #endregion
}
