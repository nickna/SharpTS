using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Error objects and subtypes. Runs against both interpreter and compiler.
/// </summary>
public class ErrorTests
{
    #region Constructor Tests

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Error_ToString_FormatsCorrectly(ExecutionMode mode)
    {
        var source = @"
            let e = new Error('Test error');
            console.log(e.toString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error: Test error\n", output);
    }

    [Theory, ModeData]
    public void Error_ToString_NoMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new Error();
            console.log(e.toString());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Error\n", output);
    }

    [Theory, ModeData]
    public void Error_StringCoercion_InvokesToString(ExecutionMode mode)
    {
        // String(e), `${e}` and "" + e must all go through Error.prototype.toString
        // ("Name: message"), matching Node — not "TypeError instance" / "[object TypeError]".
        var source = @"
            let e = new TypeError('boom');
            console.log(String(e));
            console.log(`${e}`);
            console.log('' + e);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError: boom\nTypeError: boom\nTypeError: boom\n", output);
    }

    [Theory, ModeData]
    public void Error_StringCoercion_NoMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new RangeError();
            console.log(String(e));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("RangeError\n", output);
    }

    [Theory, ModeData]
    public void Error_StringCoercion_FromCatch(ExecutionMode mode)
    {
        var source = @"
            try {
                throw new TypeError('caught');
            } catch (err: any) {
                console.log(String(err));
                console.log(`${err}`);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError: caught\nTypeError: caught\n", output);
    }

    [Theory, ModeData]
    public void Instance_StringCoercion_InvokesUserToString(ExecutionMode mode)
    {
        // #922 generalization: a user class with its own toString() is dispatched
        // in every string-coercion form, matching Node. Compiled mode now dispatches
        // it too (#931/#933) — pins both.
        var source = @"
            class Pt { x = 1; y = 2; toString() { return `(${this.x},${this.y})`; } }
            const p = new Pt();
            console.log(String(p));
            console.log('' + p);
            console.log(`${p}`);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("(1,2)\n(1,2)\n(1,2)\n", output);
    }

    [Theory, ModeData]
    public void Instance_StringCoercion_NoToString_KeepsDefault(ExecutionMode mode)
    {
        // A plain instance with no toString() brands as Node's ordinary-object
        // "[object Object]" (Object.prototype.toString), not the class name — in
        // both modes (#931). Was "[object Plain]" before compiled/interp parity.
        var source = @"
            class Plain { x = 1; }
            console.log('' + new Plain());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[object Object]\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    #region Error.cause Tests (ES2022)

    [Theory, ModeData]
    public void Error_WithCause_SetsCauseProperty(ExecutionMode mode)
    {
        var source = @"
            const cause = new Error('original');
            const e = new Error('wrapped', { cause: cause });
            console.log(e.message);
            console.log(e.cause.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("wrapped\noriginal\n", output);
    }

    [Theory, ModeData]
    public void Error_WithStringCause_SetsCauseProperty(ExecutionMode mode)
    {
        var source = @"
            const e = new Error('failed', { cause: 'network timeout' });
            console.log(e.message);
            console.log(e.cause);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("failed\nnetwork timeout\n", output);
    }

    [Theory, ModeData]
    public void Error_WithNumberCause_SetsCauseProperty(ExecutionMode mode)
    {
        var source = @"
            const e = new Error('failed', { cause: 42 });
            console.log(e.cause);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Error_WithoutCause_CauseIsUndefined(ExecutionMode mode)
    {
        var source = @"
            const e = new Error('no cause');
            console.log(e.cause);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory, ModeData]
    public void TypeError_WithCause_SetsCauseProperty(ExecutionMode mode)
    {
        var source = @"
            const original = new Error('root cause');
            const e = new TypeError('type mismatch', { cause: original });
            console.log(e.name);
            console.log(e.message);
            console.log(e.cause.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError\ntype mismatch\nroot cause\n", output);
    }

    [Theory, ModeData]
    public void Error_CauseChaining_ThreeLevels(ExecutionMode mode)
    {
        var source = @"
            const root = new Error('root');
            const mid = new Error('middle', { cause: root });
            const top = new Error('top', { cause: mid });
            console.log(top.message);
            console.log(top.cause.message);
            console.log(top.cause.cause.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("top\nmiddle\nroot\n", output);
    }

    [Theory, ModeData]
    public void Error_CauseInTryCatch_PreservedAfterThrow(ExecutionMode mode)
    {
        var source = @"
            try {
                const inner = new RangeError('out of range');
                throw new Error('wrapper', { cause: inner });
            } catch (e) {
                console.log(e.message);
                console.log(e.cause.name);
                console.log(e.cause.message);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("wrapper\nRangeError\nout of range\n", output);
    }

    [Theory, ModeData]
    public void Error_CauseIsMutable(ExecutionMode mode)
    {
        var source = @"
            const e = new Error('test');
            e.cause = 'manually set';
            console.log(e.cause);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("manually set\n", output);
    }

    [Theory, ModeData]
    public void Error_CalledWithoutNew_WithCause(ExecutionMode mode)
    {
        var source = @"
            const e = Error('without new', { cause: 'some cause' });
            console.log(e.message);
            console.log(e.cause);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("without new\nsome cause\n", output);
    }

    #endregion

    #region Error as Global Variable (Issue #24)

    [Theory, ModeData]
    public void TypeofError_ReturnsFunction(ExecutionMode mode)
    {
        var source = @"
            console.log(typeof Error);
            console.log(typeof TypeError);
            console.log(typeof RangeError);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfunction\nfunction\n", output);
    }

    [Theory, ModeData]
    public void ClassExtendsError_Basic(ExecutionMode mode)
    {
        var source = @"
            class MyError extends Error {
                constructor(msg) {
                    super(msg);
                    this.name = 'MyError';
                }
            }
            const e = new MyError('test');
            console.log(e.name);
            console.log(e.message);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("MyError\ntest\n", output);
    }

    [Theory, ModeData]
    public void ClassExtendsError_InstanceofMyError(ExecutionMode mode)
    {
        var source = @"
            class MyError extends Error {
                constructor(msg) {
                    super(msg);
                    this.name = 'MyError';
                }
            }
            const e = new MyError('test');
            console.log(e instanceof MyError);
            console.log(e instanceof Error);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ClassExtendsTypeError(ExecutionMode mode)
    {
        var source = @"
            class CustomTypeError extends TypeError {
                constructor(msg) {
                    super(msg);
                    this.name = 'CustomTypeError';
                }
            }
            const e = new CustomTypeError('bad type');
            console.log(e.name);
            console.log(e.message);
            console.log(e instanceof CustomTypeError);
            console.log(e instanceof TypeError);
            console.log(e instanceof Error);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("CustomTypeError\nbad type\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ClassExtendsError_MultiLevel(ExecutionMode mode)
    {
        var source = @"
            class AppError extends Error {
                code: number;
                constructor(msg, code) {
                    super(msg);
                    this.name = 'AppError';
                    this.code = code;
                }
            }
            class HttpError extends AppError {
                constructor(msg) {
                    super(msg, 500);
                }
            }
            const e = new HttpError('server error');
            console.log(e.name);
            console.log(e.message);
            console.log(e.code);
            console.log(e instanceof HttpError);
            console.log(e instanceof AppError);
            console.log(e instanceof Error);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("AppError\nserver error\n500\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ClassExtendsError_NoExplicitConstructor(ExecutionMode mode)
    {
        var source = @"
            class SimpleError extends Error {}
            const e = new SimpleError('hello');
            console.log(e.message);
            console.log(e instanceof SimpleError);
            console.log(e instanceof Error);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\ntrue\ntrue\n", output);
    }

    #endregion

    #region Built-in error parity (#694)

    /// <summary>
    /// Regression for #694: built-ins signal JS errors with
    /// <c>throw new Exception("RangeError: ...")</c>. The interpreter previously bound the
    /// bare message string as the caught value (<c>typeof e === "string"</c>,
    /// <c>e instanceof RangeError === false</c>, <c>e.name === undefined</c>); it now
    /// synthesizes a real <c>RangeError</c>, matching compiled mode which throws a real
    /// <c>$RangeError</c>. Asserted identical in both modes.
    /// </summary>
    [Theory, ModeData]
    public void BuiltInRangeError_CaughtAsRangeErrorInstance(ExecutionMode mode)
    {
        var source = @"
            try {
                String.fromCodePoint(-1);
            } catch (e: any) {
                console.log(typeof e);
                console.log(e instanceof RangeError);
                console.log(e instanceof Error);
                console.log(e.name);
                console.log(typeof e.message === 'string' && e.message.length > 0);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\ntrue\nRangeError\ntrue\n", output);
    }

    /// <summary>
    /// Regression for #694 covering a second error type: an empty-array <c>reduce</c> with
    /// no initial value throws <c>"TypeError: ..."</c> from the built-in. The caught value
    /// must be a real <c>TypeError</c> in both modes.
    /// </summary>
    [Theory, ModeData]
    public void BuiltInTypeError_CaughtAsTypeErrorInstance(ExecutionMode mode)
    {
        var source = @"
            try {
                ([] as number[]).reduce((a, b) => a + b);
            } catch (e: any) {
                console.log(typeof e);
                console.log(e instanceof TypeError);
                console.log(e instanceof Error);
                console.log(e.name);
                console.log(typeof e.message === 'string' && e.message.length > 0);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\ntrue\nTypeError\ntrue\n", output);
    }

    /// <summary>
    /// Regression for #694: a built-in error thrown inside a CALLED function — so it crosses
    /// a function boundary where <c>ThrowException.FromResult</c> stringifies it — is still
    /// caught as the typed Error by an outer <c>try</c>. Guards the catch-binding coercion
    /// against the host-error string round-trip across the boundary.
    /// </summary>
    [Theory, ModeData]
    public void BuiltInError_AcrossFunctionBoundary_CaughtAsTypedError(ExecutionMode mode)
    {
        var source = @"
            function boom() { String.fromCodePoint(-1); }
            try {
                boom();
            } catch (e: any) {
                console.log(e instanceof RangeError);
                console.log(e.name);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nRangeError\n", output);
    }

    /// <summary>
    /// Regression guard for #694: a guest <c>throw</c> of a real error object keeps its exact
    /// identity when caught (the catch-binding coercion only touches prefixed strings), and a
    /// guest <c>throw</c> of a plain string stays a string.
    /// </summary>
    [Theory, ModeData]
    public void GuestThrows_PreserveValueIdentity(ExecutionMode mode)
    {
        var source = @"
            const original = new RangeError('mine');
            try { throw original; } catch (e: any) { console.log(e === original, e instanceof RangeError); }
            try { throw 'plain string'; } catch (e: any) { console.log(typeof e, e); }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true true\nstring plain string\n", output);
    }

    #endregion
}
