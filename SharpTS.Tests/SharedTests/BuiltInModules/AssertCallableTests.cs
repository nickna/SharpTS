using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the assert module additions (issue #1102): the callable
/// assert(value) form, rejects/doesNotReject, match/doesNotMatch, ifError, and
/// loose deepEqual/notDeepEqual. Pure-TS facade, so interpreter == compiled.
/// </summary>
public class AssertCallableTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_Callable_PassesAndThrows(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                assert(true);
                assert(1, 'should pass');
                let threw = false;
                try { assert(false); } catch (e: any) { threw = true; }
                console.log('threw:' + threw);
                console.log('ok is fn:' + (typeof assert.ok === 'function'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("threw:true\nok is fn:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_Rejects_ResolvesWhenPromiseRejects(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                async function main(): Promise<void> {
                    await assert.rejects(async () => { throw new Error('boom'); });
                    console.log('rejects-ok');
                    let threw = false;
                    try {
                        await assert.rejects(async () => 5);
                    } catch (e: any) {
                        threw = true;
                    }
                    console.log('no-reject-throws:' + threw);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("rejects-ok\nno-reject-throws:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_DoesNotReject_PassesWhenNoRejection(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                async function main(): Promise<void> {
                    await assert.doesNotReject(async () => 42);
                    console.log('does-not-reject-ok');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("does-not-reject-ok\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_Match_And_DoesNotMatch(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                assert.match('abc', /b/);
                assert.doesNotMatch('abc', /z/);
                let m = false;
                try { assert.match('abc', /z/); } catch (e: any) { m = true; }
                let d = false;
                try { assert.doesNotMatch('abc', /b/); } catch (e: any) { d = true; }
                console.log('match-throws:' + m + ',dnm-throws:' + d);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("match-throws:true,dnm-throws:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_IfError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                assert.ifError(null);
                assert.ifError(undefined);
                let threw = false;
                try { assert.ifError(new Error('boom')); } catch (e: any) { threw = true; }
                console.log('ifError-throws:' + threw);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("ifError-throws:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_LooseDeepEqual_Coerces(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                // Loose: 1 == '1', so these are loosely deep-equal.
                assert.deepEqual({ a: 1, b: [2] }, { a: '1', b: ['2'] });
                console.log('loose-deep-ok');
                assert.notDeepEqual({ a: 1 }, { a: 2 });
                console.log('not-deep-ok');
                // strict would reject the coerced form
                let strictThrew = false;
                try { assert.deepStrictEqual({ a: 1 }, { a: '1' }); } catch (e: any) { strictThrew = true; }
                console.log('strict-throws:' + strictThrew);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("loose-deep-ok\nnot-deep-ok\nstrict-throws:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Assert_NamedImports_OfNewFunctions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { match, ifError, deepEqual, notDeepEqual } from 'assert';
                match('hello', /ell/);
                ifError(null);
                deepEqual([1], ['1']);
                notDeepEqual([1], [2]);
                console.log('named-ok');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("named-ok\n", output);
    }
}
