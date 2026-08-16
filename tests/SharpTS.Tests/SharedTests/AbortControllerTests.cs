using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for AbortController/AbortSignal functionality. Runs against both interpreter and compiler.
/// </summary>
public class AbortControllerTests
{
    [Theory, ModeData]
    public void AbortController_CreateAndAccessSignal(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            console.log(controller.signal.aborted);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void AbortController_AbortSetsAborted(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            controller.abort();
            console.log(controller.signal.aborted);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AbortController_AbortWithReason(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            controller.abort(""custom reason"");
            console.log(controller.signal.reason);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("custom reason\n", output);
    }

    [Theory, ModeData]
    public void AbortController_AbortWithDefaultReason(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            controller.abort();
            const reason: string = controller.signal.reason as string;
            console.log(reason.includes(""AbortError""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_AddEventListener(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            let called = false;
            controller.signal.addEventListener(""abort"", () => {
                called = true;
            });
            controller.abort();
            console.log(called);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_MultipleListeners(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            let count = 0;
            controller.signal.addEventListener(""abort"", () => { count++; });
            controller.signal.addEventListener(""abort"", () => { count++; });
            controller.signal.addEventListener(""abort"", () => { count++; });
            controller.abort();
            console.log(count);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_RemoveEventListener(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            let called = false;
            const listener = () => { called = true; };
            controller.signal.addEventListener(""abort"", listener);
            controller.signal.removeEventListener(""abort"", listener);
            controller.abort();
            console.log(called);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_OnAbortProperty(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            let called = false;
            controller.signal.onabort = () => { called = true; };
            controller.abort();
            console.log(called);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_ThrowIfAborted_NotAborted(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            try {
                controller.signal.throwIfAborted();
                console.log(""no throw"");
            } catch (e) {
                console.log(""threw"");
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("no throw\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_ThrowIfAborted_Aborted(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            controller.abort();
            try {
                controller.signal.throwIfAborted();
                console.log(""no throw"");
            } catch (e) {
                console.log(""threw"");
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_StaticAbort(ExecutionMode mode)
    {
        var source = @"
            const signal = AbortSignal.abort();
            console.log(signal.aborted);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_StaticAbortWithReason(ExecutionMode mode)
    {
        var source = @"
            const signal = AbortSignal.abort(""my reason"");
            console.log(signal.aborted);
            console.log(signal.reason);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nmy reason\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_StaticTimeout(ExecutionMode mode)
    {
        var source = @"
            const signal = AbortSignal.timeout(10000);
            console.log(signal.aborted);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_Any_AbortsWhenAnyAborts(ExecutionMode mode)
    {
        var source = @"
            const c1 = new AbortController();
            const c2 = new AbortController();
            const combined = AbortSignal.any([c1.signal, c2.signal]);
            console.log(combined.aborted);
            c1.abort(""first aborted"");
            console.log(combined.aborted);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_Any_AlreadyAborted(ExecutionMode mode)
    {
        var source = @"
            const c1 = new AbortController();
            c1.abort(""already done"");
            const combined = AbortSignal.any([c1.signal]);
            console.log(combined.aborted);
            console.log(combined.reason);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nalready done\n", output);
    }

    [Theory, ModeData]
    public void AbortController_AbortIdempotent(ExecutionMode mode)
    {
        var source = @"
            const controller = new AbortController();
            let count = 0;
            controller.signal.addEventListener(""abort"", () => { count++; });
            controller.abort();
            controller.abort();
            console.log(count);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void AbortSignal_InstanceOf_BrandChecks(ExecutionMode mode)
    {
        // #246: interpreter returned false for real signals (no brand linkage
        // on the AbortSignal sentinel); compiled returned true for ANY plain
        // object (Dictionary IsAssignableFrom fallback).
        var source = @"
            const s: any = AbortSignal.abort('x');
            console.log(s instanceof (AbortSignal as any));
            console.log(({} as any) instanceof (AbortSignal as any));
            const c = new AbortController();
            console.log(c.signal instanceof (AbortSignal as any));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    // #985: the typed strategy (AbortSignalEmitter) only fires when the receiver's
    // static type is AbortSignal. When a signal flows through an `any` parameter — the
    // common case for stdlib facades and user helpers — the dynamic dispatch path must
    // also route the listener API and the onabort setter to the signal helpers. Before
    // the fix, compiled mode wrote a plain "onabort" dict key (never fired) and threw
    // "undefined is not a function" for addEventListener.

    [Theory, ModeData]
    public void AbortSignal_OnAbort_DynamicReceiver_Fires(ExecutionMode mode)
    {
        var source = @"
            function reg(signal: any, cb: any): void { signal.onabort = cb; }
            const ac = new AbortController();
            let fired = 'no';
            reg(ac.signal, () => { fired = 'yes'; });
            ac.abort();
            console.log(fired);
        ";
        Assert.Equal("yes\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbortSignal_AddEventListener_DynamicReceiver_Fires(ExecutionMode mode)
    {
        var source = @"
            function reg(signal: any, cb: any): void { signal.addEventListener('abort', cb); }
            const ac = new AbortController();
            let fired = 'no';
            reg(ac.signal, () => { fired = 'yes'; });
            ac.abort();
            console.log(fired);
        ";
        Assert.Equal("yes\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbortSignal_RemoveEventListener_DynamicReceiver(ExecutionMode mode)
    {
        var source = @"
            function addL(s: any, cb: any): void { s.addEventListener('abort', cb); }
            function rmL(s: any, cb: any): void { s.removeEventListener('abort', cb); }
            const ac = new AbortController();
            let count = 0;
            const cb = () => { count++; };
            addL(ac.signal, cb);
            rmL(ac.signal, cb);
            ac.abort();
            console.log(count);
        ";
        Assert.Equal("0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbortSignal_ThrowIfAborted_DynamicReceiver(ExecutionMode mode)
    {
        var source = @"
            function tryThrow(s: any): string {
                try { s.throwIfAborted(); return 'noThrow'; } catch (e: any) { return 'threw'; }
            }
            const ac = new AbortController();
            console.log(tryThrow(ac.signal));
            ac.abort();
            console.log(tryThrow(ac.signal));
        ";
        Assert.Equal("noThrow\nthrew\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbortSignal_AbortedAndReason_DynamicReceiver(ExecutionMode mode)
    {
        // The property reads on an `any` receiver (#224) must keep working alongside
        // the new method/onabort routing.
        var source = @"
            function isAborted(s: any): boolean { return s.aborted; }
            function reasonOf(s: any): any { return s.reason; }
            const ac = new AbortController();
            console.log(isAborted(ac.signal));
            ac.abort('boom');
            console.log(isAborted(ac.signal));
            console.log(reasonOf(ac.signal));
        ";
        Assert.Equal("false\ntrue\nboom\n", TestHarness.Run(source, mode));
    }
}
