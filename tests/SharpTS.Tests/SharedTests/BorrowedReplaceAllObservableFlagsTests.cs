using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BorrowedReplaceAllObservableFlagsTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["primitive-borrowed-replace-all-observable-flags-issue", """
            const search: any = /a/g;
            Object.defineProperty(search, "flags", { value: "" });
            const method: any = (String.prototype as any).replaceAll;
            try { console.log("completed", method.call("aba", search, "x")); }
            catch (error) { console.log("threw", error instanceof TypeError); }
            """, "threw true\n"];
        yield return ["generic-borrowed-rejects-flags-before-receiver-conversion", """
            const events: string[] = [];
            const receiver: any = { toString() { events.push("receiver"); return "aba"; } };
            const search: any = /a/g;
            Object.defineProperty(search, "flags", { value: "" });
            const method: any = (String.prototype as any).replaceAll;
            try { console.log("completed", method.call(receiver, search, "x")); }
            catch (error) { console.log("threw", error instanceof TypeError); }
            console.log(events.join(","));
            """, "threw true\n\n"];
        yield return ["observable-flags-coercion-precedes-replace-hook-with-original-values", """
            const events: string[] = [];
            const receiver: any = { toString() { events.push('unexpected-receiver'); return 'aba'; } };
            const replacement: any = { toString() { events.push('unexpected-replacement'); return 'x'; } };
            const flags: any = { toString() { events.push('flags-string'); return 'g'; } };
            const search: any = /a/, returned = {};
            Object.defineProperty(search, 'flags', {
                configurable: true, get() { events.push('flags-get'); return flags; }
            });
            Object.defineProperty(search, Symbol.replace, {
                configurable: true,
                get() {
                    events.push('replace-get');
                    return function(this: any, value: any, substitute: any) {
                        events.push('call');
                        console.log(this === search, value === receiver, substitute === replacement);
                        return returned;
                    };
                }
            });
            const method: any = String.prototype.replaceAll;
            const result = method.call(receiver, search, replacement);
            console.log(result === returned, events.join(','));
            """, "true true true\ntrue flags-get,flags-string,replace-get,call\n"];
        yield return ["abrupt-flags-getter-and-coercion-preserve-token-and-suppress-later-reads", """
            const events: string[] = [], token = {};
            const receiver: any = { toString() { events.push('receiver'); return 'aba'; } };
            const flags: any = { toString() { events.push('flags-string'); throw token; } };
            const search: any = /a/g;
            let failAtGet = true;
            Object.defineProperty(search, 'flags', {
                configurable: true,
                get() { events.push('flags-get'); if (failAtGet) throw token; return flags; }
            });
            Object.defineProperty(search, Symbol.replace, {
                configurable: true,
                get() { events.push('replace-get'); return () => { events.push('call'); return 'unexpected'; }; }
            });
            const method: any = String.prototype.replaceAll;
            try { console.log('unexpected', method.call(receiver, search, 'x')); }
            catch (error) { console.log(error === token, events.join(',')); }
            events.length = 0; failAtGet = false;
            try { console.log('unexpected', method.call(receiver, search, 'x')); }
            catch (error) { console.log(error === token, events.join(',')); }
            """, "true flags-get\ntrue flags-get,flags-string\n"];
        yield return ["nullish-replace-hook-fallback-validates-once-before-string-and-functional-replacement", """
            const events: string[] = [];
            const receiver: any = { toString() { events.push('receiver-string'); return 'aba'; } };
            const replacement: any = { toString() { events.push('replacement-string'); return 'x'; } };
            const flags: any = { toString() { events.push('flags-string'); return 'g'; } };
            const search: any = /a/g;
            search.toString = function() { events.push('search-string'); return 'a'; };
            Object.defineProperty(search, 'flags', {
                configurable: true, get() { events.push('flags-get'); return flags; }
            });
            const method: any = String.prototype.replaceAll;
            const hooks: any[] = [null, undefined];
            for (const hook of hooks) {
                Object.defineProperty(search, Symbol.replace, {
                    configurable: true, get() { events.push('replace-get'); return hook; }
                });
                console.log(method.call(receiver, search, replacement), events.join(','));
                events.length = 0;
                const callback = (match: string, index: number, text: string) => {
                    events.push('callback:' + index + ':' + match + ':' + text); return 'x';
                };
                console.log(method.call(receiver, search, callback), events.join(','));
                events.length = 0;
            }
            """, "xbx flags-get,flags-string,replace-get,receiver-string,search-string,replacement-string\nxbx flags-get,flags-string,replace-get,receiver-string,search-string,callback:0:a:aba,callback:2:a:aba\nxbx flags-get,flags-string,replace-get,receiver-string,search-string,replacement-string\nxbx flags-get,flags-string,replace-get,receiver-string,search-string,callback:0:a:aba,callback:2:a:aba\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void BorrowedReplaceAllValidatesObservableFlagsBeforeProtocolOrReceiverConversion(
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
