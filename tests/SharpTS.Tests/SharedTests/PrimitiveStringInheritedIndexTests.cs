using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PrimitiveStringInheritedIndexTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["missing-string-indices-data-getterless-abrupt-and-empty", """
            const prototype: any = String.prototype;
            const original3 = Object.getOwnPropertyDescriptor(prototype, '3');
            const original0 = Object.getOwnPropertyDescriptor(prototype, '0');
            const input: any = 'abc', typed: string = 'abc', empty: string = '';
            const token = {};
            let reads = 0, receiver = true, emptyReads = 0;
            try {
                Object.defineProperty(prototype, '3', { configurable: true, value: 17 });
                console.log('data', input[3], input['3'], typed[3], typed['3']);
                Object.defineProperty(prototype, '3', { configurable: true, get: undefined, set(value: any) {} });
                console.log('getterless', input[3] === undefined, input['3'] === undefined, typed[3] === undefined, typed['3'] === undefined);
                Object.defineProperty(prototype, '3', {
                    configurable: true,
                    get() { 'use strict'; reads++; receiver = receiver && this === 'abc'; throw token; }
                });
                try { input[3]; }
                catch (error) { console.log('number', error === token, receiver, reads); }
                try { typed['3']; }
                catch (error) { console.log('quoted', error === token, receiver, reads); }
                delete prototype['3'];
                console.log('deleted', input[3] === undefined, typed['3'] === undefined);
                Object.defineProperty(prototype, '0', {
                    configurable: true,
                    get() { 'use strict'; emptyReads++; return this === '' ? 'empty' : 'wrong'; }
                });
                console.log('own-and-empty', input[0], typed['0'], empty[0], empty['0'], emptyReads);
            } finally {
                if (original3) Object.defineProperty(prototype, '3', original3); else delete prototype['3'];
                if (original0) Object.defineProperty(prototype, '0', original0); else delete prototype['0'];
            }
            console.log('restored', typed[0], typed[3] === undefined, empty[0] === undefined);
            """ + "\n", "data 17 17 17 17\ngetterless true true true true\nnumber true true 1\nquoted true true 2\ndeleted true true\nown-and-empty a a empty empty 2\nrestored a true true\n"];
        yield return ["own-characters-mask-prototype-and-missing-indices-inherit", """
            const prototype: any = String.prototype;
            const original0 = Object.getOwnPropertyDescriptor(prototype, "0");
            const original1 = Object.getOwnPropertyDescriptor(prototype, "1");
            const original3 = Object.getOwnPropertyDescriptor(prototype, "3");
            let ownReads = 0, inheritedReads = 0, receiverOK = true;
            function ownGetter(this: any) { "use strict"; ownReads++; return "wrong"; }
            function inheritedGetter(this: any) { "use strict"; inheritedReads++; receiverOK = receiverOK && this === "abc"; return "outside"; }
            const input: any = "abc";
            try {
                Object.defineProperty(prototype, "0", { get: ownGetter, configurable: true });
                Object.defineProperty(prototype, "1", { get: ownGetter, configurable: true });
                Object.defineProperty(prototype, "3", { get: inheritedGetter, configurable: true });
                console.log(input["0"], input["1"], input[0], input[1], ownReads);
                console.log(input["3"], input[3], inheritedReads, receiverOK);
            } finally {
                if (original0) Object.defineProperty(prototype, "0", original0); else delete prototype["0"];
                if (original1) Object.defineProperty(prototype, "1", original1); else delete prototype["1"];
                if (original3) Object.defineProperty(prototype, "3", original3); else delete prototype["3"];
            }
            """ + "\n", "a b a b 0\noutside outside 2 true\n"];
        yield return ["public-out-of-range-inherited-string-index", """
            const prototype: any = String.prototype;
            const previous = Object.getOwnPropertyDescriptor(prototype, "3");
            try {
                Object.defineProperty(prototype, "3", {
                    configurable: true,
                    get: function(this: any) { "use strict"; return this === "abc" ? "inherited" : "wrong"; }
                });
                const input: any = "abc";
                console.log(input["3"], input[3]);
            } finally {
                if (previous) Object.defineProperty(prototype, "3", previous); else delete prototype["3"];
            }
            """ + "\n", "inherited inherited\n"];
        yield return ["utf16-surrogate-code-units", """
            const input: any = "\uD83D\uDE00X";
            console.log(input.length, input["0"].charCodeAt(0), input[0].charCodeAt(0),
                input["1"].charCodeAt(0), input[1].charCodeAt(0), input["2"], input[2], input["3"] === undefined);
            """ + "\n", "3 55357 55357 56832 56832 X X true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void MissingStringCharactersContinueThroughThePrototype(
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
