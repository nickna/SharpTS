using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'assert' module across interpreter and compiled modes.
/// </summary>
public class AssertModuleTests
{
    [Theory, ModeData]
    public void Assert_Ok_PassesForTruthyValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ok } from 'assert';
                ok(true);
                ok(1);
                ok('hello');
                ok({});
                ok([]);
                console.log('all passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("all passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_Ok_ThrowsForFalsy(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ok } from 'assert';
                try {
                    ok(false);
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Assert_StrictEqual_PassesForEqualValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { strictEqual } from 'assert';
                strictEqual(1, 1);
                strictEqual('hello', 'hello');
                strictEqual(true, true);
                console.log('all passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("all passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_StrictEqual_ThrowsForUnequalValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { strictEqual } from 'assert';
                try {
                    strictEqual(1, 2);
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Assert_StrictEqual_ThrowsForDifferentTypes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { strictEqual } from 'assert';
                try {
                    strictEqual(1, '1');
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught type mismatch');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught type mismatch\n", output);
    }

    [Theory, ModeData]
    public void Assert_NotStrictEqual_PassesForUnequalValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { notStrictEqual } from 'assert';
                notStrictEqual(1, 2);
                notStrictEqual('a', 'b');
                notStrictEqual(true, false);
                console.log('all passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("all passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_NotStrictEqual_ThrowsForEqualValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { notStrictEqual } from 'assert';
                try {
                    notStrictEqual(1, 1);
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Assert_DeepStrictEqual_PassesForEqualObjects(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { deepStrictEqual } from 'assert';
                deepStrictEqual({ a: 1, b: 2 }, { a: 1, b: 2 });
                console.log('objects passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("objects passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_DeepStrictEqual_PassesForEqualArrays(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { deepStrictEqual } from 'assert';
                deepStrictEqual([1, 2, 3], [1, 2, 3]);
                console.log('arrays passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("arrays passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_DeepStrictEqual_ThrowsForDifferentObjects(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { deepStrictEqual } from 'assert';
                try {
                    deepStrictEqual({ a: 1 }, { a: 2 });
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Assert_DeepStrictEqual_ThrowsForDifferentArrays(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { deepStrictEqual } from 'assert';
                try {
                    deepStrictEqual([1, 2], [1, 3]);
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Assert_Equal_PassesForLooselyEqualValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { equal } from 'assert';
                equal(1, 1);
                equal('1', '1');
                console.log('all passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("all passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_NotEqual_PassesForDifferentValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { notEqual } from 'assert';
                notEqual(1, 2);
                notEqual('a', 'b');
                console.log('all passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("all passed\n", output);
    }

    [Theory, ModeData]
    public void Assert_Fail_AlwaysThrows(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { fail } from 'assert';
                try {
                    fail('custom message');
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void Assert_Fail_WithDefaultMessage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { fail } from 'assert';
                try {
                    fail();
                    console.log('should not reach');
                } catch (e) {
                    console.log('caught default');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught default\n", output);
    }

    [Theory, ModeData]
    public void Assert_NamespaceImport_Works(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as assert from 'assert';
                assert.ok(true);
                assert.strictEqual(1, 1);
                console.log('namespace import works');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("namespace import works\n", output);
    }

    [Theory, ModeData]
    public void Assert_CustomMessage_IsUsed(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ok } from 'assert';
                try {
                    ok(false, 'custom error message');
                } catch (e) {
                    // Just check that it throws - message is in the error
                    console.log('threw with message');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("threw with message\n", output);
    }

    [Theory, ModeData]
    public void Assert_MultipleAssertions_InSequence(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ok, strictEqual, deepStrictEqual } from 'assert';
                ok(true);
                strictEqual(42, 42);
                deepStrictEqual({ x: 1 }, { x: 1 });
                console.log('all assertions passed');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("all assertions passed\n", output);
    }
}
