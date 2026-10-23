using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BorrowedReplaceAllFlagsRegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-observable-regexp-flags", """
            const search: any = /a/g;
            Object.defineProperty(search, "flags", { value: "" });
            const method: any = (String.prototype as any).replaceAll;
            try { console.log("completed", method.call("aba", search, "x")); }
            catch (error) { console.log("threw", error instanceof TypeError); }
            """, "threw true\n"];
        yield return ["flags-coercion-before-replacement-hook", """
            const method: any = (String.prototype as any).replaceAll;
            function run(name: string) {
                const trace: string[] = [];
                const token = {};
                const search: any = name === 'ok' ? /a/ : /a/g;
                const replacement = { toString() { trace.push('replacement'); return 'x'; } };
                Object.defineProperty(search, 'flags', {
                    get() {
                        trace.push('flags');
                        if (name === 'getter-throw') throw token;
                        if (name === 'undefined') return undefined;
                        return {
                            [Symbol.toPrimitive](hint: string) {
                                trace.push('coerce:' + hint);
                                if (name === 'coercion-throw') throw token;
                                return name === 'reject' ? 'i' : 'g';
                            }
                        };
                    }
                });
                Object.defineProperty(search, Symbol.replace, {
                    get() {
                        trace.push('hook');
                        if (name === 'hook-throw') throw token;
                        return function(this: any, input: any, value: any) {
                            trace.push('call');
                            console.log(name, 'hook', this === search, input === 42, value === replacement);
                            return 'done';
                        };
                    }
                });
                try { console.log(name, 'result', method.call(42, search, replacement)); }
                catch (error) { console.log(name, 'error', error === token, error instanceof TypeError); }
                console.log(name, 'trace', trace.join(','));
            }
            run('ok');
            run('reject');
            run('getter-throw');
            run('coercion-throw');
            run('undefined');
            run('hook-throw');
            """ + "\n", "ok hook true true true\nok result done\nok trace flags,coerce:string,hook,call\nreject error false true\nreject trace flags,coerce:string\ngetter-throw error true false\ngetter-throw trace flags\ncoercion-throw error true false\ncoercion-throw trace flags,coerce:string\nundefined error false true\nundefined trace flags\nhook-throw error true false\nhook-throw trace flags,coerce:string,hook\n"];
    }

    public static IEnumerable<object[]> SavedCases() =>
        BorrowedReplaceAllObservableFlagsTests.Cases().Concat(Cases());

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ObservableRegExpFlagsPrecedeReplacementProtocol(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
