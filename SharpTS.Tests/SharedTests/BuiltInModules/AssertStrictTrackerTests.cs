using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the assert module additions (issue #1103): the assert.strict
/// namespace, CallTracker, and partialDeepStrictEqual. Pure-TS facade, so
/// interpreter == compiled.
/// </summary>
public class AssertStrictTrackerTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Strict_LooseFormsAliasStrict(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                assert.strict.equal(1, 1);
                assert.strict.deepEqual({ a: 1 }, { a: 1 });
                let e = false;
                try { assert.strict.equal(1, ('1' as any)); } catch (err: any) { e = true; }
                let d = false;
                try { assert.strict.deepEqual({ a: 1 }, ({ a: '1' } as any)); } catch (err: any) { d = true; }
                console.log('equal-strict-throws:' + e + ',deep-strict-throws:' + d);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("equal-strict-throws:true,deep-strict-throws:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Strict_IsCallableAndSelfReferential(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { strict as assert } from 'assert';
                assert(true);
                let threw = false;
                try { assert(false); } catch (e: any) { threw = true; }
                console.log('callable-throws:' + threw);
                console.log('self:' + (assert.strict === assert));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("callable-throws:true\nself:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PartialDeepStrictEqual_Contains(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                assert.partialDeepStrictEqual({ a: 1, b: 2 }, { a: 1 });
                assert.partialDeepStrictEqual({ a: { x: 1, y: 2 } }, { a: { x: 1 } });
                console.log('pass');
                let threw = false;
                try { assert.partialDeepStrictEqual({ a: 1 }, { a: 1, c: 3 }); } catch (e: any) { threw = true; }
                console.log('missing-throws:' + threw);
                let coerce = false;
                try { assert.partialDeepStrictEqual({ a: 1 }, { a: '1' }); } catch (e: any) { coerce = true; }
                console.log('strict-leaf-throws:' + coerce);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("pass\nmissing-throws:true\nstrict-leaf-throws:true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CallTracker_VerifyPassesWhenCallCountMatches(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                const tracker = new assert.CallTracker();
                const cb = tracker.calls(() => 42, 2);
                console.log(cb());
                cb();
                console.log('report-empty:' + (tracker.report().length === 0));
                tracker.verify();
                console.log('verified');
                console.log('getCalls:' + tracker.getCalls(cb).length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\nreport-empty:true\nverified\ngetCalls:2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CallTracker_VerifyThrowsWhenCountMismatches(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import assert from 'assert';
                const tracker = new assert.CallTracker();
                const cb = tracker.calls(() => {}, 2);
                cb();
                console.log('report-len:' + tracker.report().length);
                let threw = false;
                try { tracker.verify(); } catch (e: any) { threw = true; }
                console.log('verify-throws:' + threw);
                tracker.reset();
                console.log('after-reset-calls:' + tracker.getCalls(cb).length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("report-len:1\nverify-throws:true\nafter-reset-calls:0\n", output);
    }
}
