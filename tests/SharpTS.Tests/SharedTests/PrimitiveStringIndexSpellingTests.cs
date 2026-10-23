using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PrimitiveStringIndexSpellingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["canonical-and-noncanonical-spelling-controls", """
            const input: any = "abc";
            console.log(input["0"], input[0], input[-0], input["1"], input[1], input["2"]);
            console.log(input["-0"] === undefined, input["01"] === undefined, input["1.0"] === undefined,
                input["+1"] === undefined, input["1e0"] === undefined, input["1.5"] === undefined, input[1.5] === undefined);
            console.log(input["3"] === undefined, input[3] === undefined,
                input["4294967294"] === undefined, input["4294967295"] === undefined);
            """ + "\n", "a a a b b c\ntrue true true true true true true\ntrue true true true\n"];
        yield return ["noncanonical-names-retain-data-getters-and-abrupt-completion", """
            const prototype: any = String.prototype;
            const oldMinus = Object.getOwnPropertyDescriptor(prototype, '-0');
            const oldLeading = Object.getOwnPropertyDescriptor(prototype, '01');
            const oldPlus = Object.getOwnPropertyDescriptor(prototype, '+1');
            const oldSpace = Object.getOwnPropertyDescriptor(prototype, ' 1 ');
            const input: any = 'abc';
            const token = {};
            let reads = 0, receiver = true;
            try {
                Object.defineProperty(prototype, '-0', { configurable: true, value: 'minus' });
                Object.defineProperty(prototype, '01', {
                    configurable: true,
                    get() { 'use strict'; reads++; receiver = receiver && this === 'abc'; return 'leading'; }
                });
                Object.defineProperty(prototype, '+1', {
                    configurable: true,
                    get() { 'use strict'; reads++; receiver = receiver && this === 'abc'; throw token; }
                });
                Object.defineProperty(prototype, ' 1 ', { configurable: true, value: 'space' });
                console.log('native', input[0], input[-0], input['0'], input[1], input['1']);
                console.log('named', input['-0'], input['01'], input[' 1 '], reads, receiver);
                try { input['+1']; }
                catch (error) { console.log('throw', error === token, reads, receiver); }
                delete prototype['01'];
                console.log('deleted', input['01'] === undefined, input[1], reads);
            } finally {
                if (oldMinus) Object.defineProperty(prototype, '-0', oldMinus); else delete prototype['-0'];
                if (oldLeading) Object.defineProperty(prototype, '01', oldLeading); else delete prototype['01'];
                if (oldPlus) Object.defineProperty(prototype, '+1', oldPlus); else delete prototype['+1'];
                if (oldSpace) Object.defineProperty(prototype, ' 1 ', oldSpace); else delete prototype[' 1 '];
            }
            console.log('restored', input['-0'] === undefined, input['01'] === undefined,
                input['+1'] === undefined, input[' 1 '] === undefined);
            """ + "\n", "native a a a b b\nnamed minus leading space 1 true\nthrow true 2 true\ndeleted true b 2\nrestored true true true true\n"];
        yield return ["public-noncanonical-string-indices", """
            const input: any = "abc";
            console.log(input["-0"] === undefined, input["01"] === undefined, input["+1"] === undefined);
            console.log(input["0"], input["1"]);
            """ + "\n", "true true true\na b\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StringIndexSpellingsPreserveNamedProperties(
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
