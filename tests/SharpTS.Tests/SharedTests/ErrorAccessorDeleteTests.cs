using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ErrorAccessorDeleteTests
{
    [Theory, ModeData]
    public void ConfigurableOwnAccessorsAreRemovedWithoutInvokingTheirGetter(ExecutionMode mode)
    {
        // Retain the qualified public source: LF line separators and a final CRLF.
        var source = """
            let reads = 0;
            const target: any = new Error('probe');
            const getter = function(this: any) { reads++; return 7; };
            Object.defineProperty(target, 'probe', {
                get: getter, enumerable: true, configurable: true
            });
            console.log(Object.hasOwn(target, 'probe'),
                Object.getOwnPropertyDescriptor(target, 'probe')!.get === getter, reads);
            console.log(delete target.probe);
            console.log(Object.hasOwn(target, 'probe'),
                Object.getOwnPropertyDescriptor(target, 'probe') === undefined, reads);
            console.log(target.probe === undefined, reads);
            """.ReplaceLineEndings("\n") + "\r\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "true true 0\ntrue\nfalse true 0\ntrue 0\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Theory, ModeData]
    public void NonconfigurableAccessorsRefuseDeletionAndMissingKeysSucceed(ExecutionMode mode)
    {
        var source = """
            let reads = 0;
            const target: any = new Error('probe');
            const getter = function(this: any) { reads++; return 7; };
            Object.defineProperty(target, 'fixed', { get: getter, configurable: false });
            console.log(delete target.fixed, Object.hasOwn(target, 'fixed'), reads);
            function removeStrict() { 'use strict'; delete target['fixed']; }
            try { removeStrict(); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.getOwnPropertyDescriptor(target, 'fixed')!.get === getter, reads);
            console.log(delete target.missing);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        const string expected = "false true 0\ntrue\ntrue 0\ntrue\n";
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
