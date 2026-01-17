using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ErrorTests
{
    // ========== Constructor Tests ==========

    [Fact]
    public void Error_NoArgs_CreatesErrorWithEmptyMessage()
    {
        var source = @"
            let e = new Error();
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Error\n\n", output);
    }

    [Fact]
    public void Error_WithMessage_CreatesErrorWithMessage()
    {
        var source = @"
            let e = new Error('Something went wrong');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Error\nSomething went wrong\n", output);
    }

    [Fact]
    public void Error_CalledWithoutNew_StillCreatesError()
    {
        var source = @"
            let e = Error('Without new');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Error\nWithout new\n", output);
    }

    [Fact]
    public void Error_ToString_FormatsCorrectly()
    {
        var source = @"
            let e = new Error('Test error');
            console.log(e.toString());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Error: Test error\n", output);
    }

    [Fact]
    public void Error_ToString_NoMessage()
    {
        var source = @"
            let e = new Error();
            console.log(e.toString());
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Error\n", output);
    }

    [Fact]
    public void Error_HasStackProperty()
    {
        var source = @"
            let e = new Error('Test');
            console.log(typeof e.stack);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("string\n", output);
    }

    // ========== Error Subtype Tests ==========

    [Fact]
    public void TypeError_CreatesWithCorrectName()
    {
        var source = @"
            let e = new TypeError('Invalid type');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("TypeError\nInvalid type\n", output);
    }

    [Fact]
    public void RangeError_CreatesWithCorrectName()
    {
        var source = @"
            let e = new RangeError('Out of range');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("RangeError\nOut of range\n", output);
    }

    [Fact]
    public void ReferenceError_CreatesWithCorrectName()
    {
        var source = @"
            let e = new ReferenceError('Undefined variable');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ReferenceError\nUndefined variable\n", output);
    }

    [Fact]
    public void SyntaxError_CreatesWithCorrectName()
    {
        var source = @"
            let e = new SyntaxError('Unexpected token');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("SyntaxError\nUnexpected token\n", output);
    }

    [Fact]
    public void URIError_CreatesWithCorrectName()
    {
        var source = @"
            let e = new URIError('Invalid URI');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("URIError\nInvalid URI\n", output);
    }

    [Fact]
    public void EvalError_CreatesWithCorrectName()
    {
        var source = @"
            let e = new EvalError('Eval failed');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("EvalError\nEval failed\n", output);
    }

    // ========== Error Subtype Without New ==========

    [Fact]
    public void TypeError_CalledWithoutNew_StillCreatesError()
    {
        var source = @"
            let e = TypeError('Without new');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("TypeError\nWithout new\n", output);
    }

    [Fact]
    public void RangeError_CalledWithoutNew_StillCreatesError()
    {
        var source = @"
            let e = RangeError('Without new');
            console.log(e.name);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("RangeError\n", output);
    }

    // ========== AggregateError Tests ==========

    [Fact]
    public void AggregateError_WithErrors_CreatesCorrectly()
    {
        var source = @"
            let errors = [new Error('First'), new Error('Second')];
            let e = new AggregateError(errors, 'Multiple errors');
            console.log(e.name);
            console.log(e.message);
            console.log(e.errors.length);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("AggregateError\nMultiple errors\n2\n", output);
    }

    [Fact]
    public void AggregateError_WithEmptyArray_CreatesCorrectly()
    {
        var source = @"
            let e = new AggregateError([], 'No errors');
            console.log(e.name);
            console.log(e.message);
            console.log(e.errors.length);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("AggregateError\nNo errors\n0\n", output);
    }

    [Fact]
    public void AggregateError_DefaultMessage()
    {
        var source = @"
            let e = new AggregateError([new Error('Test')]);
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("All promises were rejected\n", output);
    }

    // ========== Mutable Properties Tests ==========

    [Fact]
    public void Error_NameIsMutable()
    {
        var source = @"
            let e = new Error('Test');
            e.name = 'CustomError';
            console.log(e.name);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("CustomError\n", output);
    }

    [Fact]
    public void Error_MessageIsMutable()
    {
        var source = @"
            let e = new Error('Original');
            e.message = 'Modified';
            console.log(e.message);
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Modified\n", output);
    }

    // ========== Throw/Catch Tests ==========

    [Fact]
    public void Error_CanBeThrown()
    {
        var source = @"
            try {
                throw new Error('Thrown error');
            } catch (e) {
                console.log(e.name);
                console.log(e.message);
            }
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Error\nThrown error\n", output);
    }

    [Fact]
    public void TypeError_CanBeThrown()
    {
        var source = @"
            try {
                throw new TypeError('Type error thrown');
            } catch (e) {
                console.log(e.name);
                console.log(e.message);
            }
        ";
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("TypeError\nType error thrown\n", output);
    }

    [Fact]
    public void Error_RethrowPreservesProperties()
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("RangeError\nModified in inner\n", output);
    }
}
