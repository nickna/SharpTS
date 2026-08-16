using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// #600: inside compiled state machines (async, generator, async-arrow, async-generator) the
/// <c>undefined</c> literal collapsed to CLR null and strict equality (<c>===</c>/<c>!==</c>) was
/// emitted with loose semantics, so <c>null === undefined</c> was wrongly true and <c>typeof
/// undefined</c> was "object". These tests pin the corrected behavior across both execution modes.
/// </summary>
public class StateMachineUndefinedEqualityTests
{
    [Theory, ModeData]
    public void Async_AwaitNullResolvedPromise_IsNotUndefined(ExecutionMode mode)
    {
        // The exact repro from #600: awaiting a promise that resolves null must yield a value that is
        // `=== null` but NOT `=== undefined`.
        var source = """
            async function f() { return null; }
            async function viaAwait() {
                const v = await f();
                console.log(typeof v);
                console.log(v === null);
                console.log(v === undefined);
            }
            viaAwait();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Async_UndefinedLiteralAndStrictEquality(ExecutionMode mode)
    {
        var source = """
            async function main() {
                console.log(typeof undefined);
                console.log(String(undefined));
                console.log(null === undefined);
                console.log(null !== undefined);
                console.log(undefined === undefined);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\nfalse\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Async_ReturnNull_ResolvesNullNotUndefined(ExecutionMode mode)
    {
        // `return null` in an async function still resolves with null (distinct from undefined).
        var source = """
            async function f() { return null; }
            async function main() {
                const v = await f();
                console.log(typeof v, String(v), v === null, v === undefined);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object null true false\n", output);
    }

    [Theory, ModeData]
    public void Async_ThrowUndefined_StringifiesAsUndefined(ExecutionMode mode)
    {
        // #629: the undefined literal was Ldnull in state machines, so a caught `throw undefined`
        // stringified as "null". The sentinel is now emitted, so it stringifies as "undefined".
        var source = """
            async function f() {
                try { throw undefined; } catch (e) { console.log("u=" + e + " isUndef=" + (e === undefined)); }
                try { throw null; }      catch (e) { console.log("n=" + e + " isNull=" + (e === null)); }
            }
            f();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("u=undefined isUndef=true\nn=null isNull=true\n", output);
    }

    [Theory, ModeData]
    public void Generator_UndefinedLiteralAndStrictEquality(ExecutionMode mode)
    {
        var source = """
            function* g() {
                yield typeof undefined;
                yield (null === undefined);
                yield (undefined === undefined);
            }
            for (const x of g()) console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_UndefinedLiteralAndStrictEquality(ExecutionMode mode)
    {
        var source = """
            const r = async () => {
                console.log(typeof undefined);
                console.log(null === undefined);
            };
            r();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nfalse\n", output);
    }

    [Theory, ModeData]
    public void AsyncGenerator_UndefinedLiteralAndStrictEquality(ExecutionMode mode)
    {
        var source = """
            async function* g() {
                yield typeof undefined;
                yield (null === undefined);
                yield (undefined === undefined);
            }
            async function main() {
                for await (const x of g()) console.log(x);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nfalse\ntrue\n", output);
    }
}
