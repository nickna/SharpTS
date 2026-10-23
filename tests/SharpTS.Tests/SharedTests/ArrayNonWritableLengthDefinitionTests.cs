using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayNonWritableLengthDefinitionTests
{
    public const string IssueSource = """
        const values: any[] = [1];
        Object.defineProperty(values, 'length', { writable: false });
        console.log(Reflect.set(values, '1', 7), values.length, Object.hasOwn(values, '1'));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-set-beyond-nonwritable-length-issue", IssueSource, "false 1 false\n"];
        yield return ["rejected-definitions-preserve-state-and-convert-descriptors-first", """
            const values: any[] = [1];
            Object.defineProperty(values, 'length', { writable: false });
            let reads = 0, conversions = 0, failures = 0;
            function report(key: string) {
                const length = Object.getOwnPropertyDescriptor(values, 'length')!;
                console.log(values.length, length.value, length.writable, Object.hasOwn(values, key),
                    Object.getOwnPropertyDescriptor(values, key) === undefined,
                    Reflect.getOwnPropertyDescriptor(values, key) === undefined);
            }
            try {
                Object.defineProperty(values, '1', { value: 7, writable: true, enumerable: true, configurable: true });
                console.log('unexpected');
            } catch (error) { console.log('data', error instanceof TypeError); }
            report('1');
            try {
                Object.defineProperty(values, '2', { get() { reads++; return 9; }, configurable: true });
                console.log('unexpected');
            } catch (error) { console.log('accessor', error instanceof TypeError, reads); }
            report('2');
            const converted: any = {
                get value() { conversions++; return 7; }, writable: true, enumerable: true, configurable: true
            };
            try { Object.defineProperty(values, '1', converted); console.log('unexpected'); }
            catch (error) { console.log('converted', error instanceof TypeError, conversions); }
            report('1');
            const marker = {};
            const throwing: any = { get value() { failures++; throw marker; } };
            try { Object.defineProperty(values, '1', throwing); console.log('unexpected'); }
            catch (error) { console.log('throwing', error === marker, failures); }
            report('1');
            console.log('reflect', Reflect.defineProperty(values, '1', converted), conversions);
            report('1');
            """, "data true\n1 1 false false true true\naccessor true 0\n1 1 false false true true\nconverted true 1\n1 1 false false true true\nthrowing true 1\n1 1 false false true true\nreflect false 2\n1 1 false false true true\n"];
        yield return ["in-bounds-updates-and-writable-length-extension-remain-valid", """
            const fixed: any[] = [1];
            Object.defineProperty(fixed, 'length', { writable: false });
            console.log(Reflect.set(fixed, '0', 7), fixed.length,
                Object.getOwnPropertyDescriptor(fixed, '0')!.value,
                Object.getOwnPropertyDescriptor(fixed, 'length')!.writable);
            console.log(Object.defineProperty(fixed, '0', { value: 9, writable: true, enumerable: true, configurable: true }) === fixed,
                fixed.length, Object.getOwnPropertyDescriptor(fixed, '0')!.value,
                Object.getOwnPropertyDescriptor(fixed, 'length')!.writable);
            const growing: any[] = [1];
            console.log(Reflect.set(growing, '1', 7), growing.length,
                Object.getOwnPropertyDescriptor(growing, '1')!.value);
            console.log(Object.defineProperty(growing, '2', { value: 9, writable: true, enumerable: true, configurable: true }) === growing,
                growing.length, Object.getOwnPropertyDescriptor(growing, '2')!.value);
            console.log(Reflect.defineProperty(growing, '3', { value: 11, writable: true, enumerable: true, configurable: true }),
                growing.length, Object.getOwnPropertyDescriptor(growing, '3')!.value,
                Object.getOwnPropertyDescriptor(growing, 'length')!.writable);
            """, "true 1 7 false\ntrue 1 9 false\ntrue 2 7\ntrue 3 9\ntrue 4 11 true\n"];
        yield return ["inherited-setter-runs-with-readonly-length-without-own-definition", """
            const values: any[] = [1], state = { calls: 0, reads: 0, value: 0, same: true };
            const selectedPrototype = Object.create(Array.prototype);
            Object.defineProperty(selectedPrototype, '1', {
                get() { state.reads++; return 99; },
                set(next: number) { state.calls++; state.value = next; state.same = state.same && this === values; },
                configurable: true
            });
            Object.setPrototypeOf(values, selectedPrototype);
            Object.defineProperty(values, 'length', { writable: false });
            console.log(Reflect.set(values, '1', 7), state.calls, state.reads, state.value, state.same,
                values.length, Object.hasOwn(values, '1'), Object.getOwnPropertyDescriptor(values, '1') === undefined,
                Object.getOwnPropertyDescriptor(values, 'length')!.writable);
            try {
                Object.defineProperty(values, '1', { value: 9, writable: true, enumerable: true, configurable: true });
                console.log('unexpected');
            } catch (error) {
                console.log(error instanceof TypeError, state.calls, state.reads, state.value, values.length,
                    Object.hasOwn(values, '1'), Object.getOwnPropertyDescriptor(values, '1') === undefined);
            }
            """, "true 1 0 7 true 1 false true false\ntrue 1 0 7 1 false true\n"];
        yield return ["non-array-arguments-and-record-own-creation-ignore-readonly-length", """
            function checkArguments(value: number) {
                const args: any = arguments;
                Object.defineProperty(args, 'length', { writable: false });
                console.log(Reflect.set(args, '1', 7), args.length, Object.hasOwn(args, '1'), args[1]);
            }
            checkArguments(1);
            const ordinary: any = { length: 1 };
            Object.defineProperty(ordinary, 'length', { writable: false });
            console.log(Reflect.set(ordinary, '1', 7), ordinary.length, Object.hasOwn(ordinary, '1'), ordinary[1]);
            """, "true 1 true 7\ntrue 1 true 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CanonicalArrayDefinitionsRespectNonWritableLength(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
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
