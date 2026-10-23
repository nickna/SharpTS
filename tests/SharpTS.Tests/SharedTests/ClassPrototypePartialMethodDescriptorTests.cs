using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassPrototypePartialMethodDescriptorTests
{
    public const string IssueSource = """
        class Owner { method() { return 7; } }
        const source: any = Owner.prototype;
        const before = Object.getOwnPropertyDescriptor(source, 'method')!;
        Object.defineProperty(source, 'method', { enumerable: true });
        const after = Object.getOwnPropertyDescriptor(source, 'method')!;
        console.log(before.value === after.value, after.writable,
            after.enumerable, after.configurable, typeof source.method);
        const symbol = Symbol('method');
        class SymbolOwner { [symbol]() { return 8; } }
        const symbolic: any = SymbolOwner.prototype;
        const symbolBefore = Object.getOwnPropertyDescriptor(symbolic, symbol)!;
        Object.defineProperty(symbolic, symbol, { enumerable: true });
        const symbolAfter = Object.getOwnPropertyDescriptor(symbolic, symbol)!;
        console.log(symbolBefore.value === symbolAfter.value, symbolAfter.writable,
            symbolAfter.enumerable, symbolAfter.configurable, typeof symbolic[symbol]);
        const constructorBefore = Object.getOwnPropertyDescriptor(source, 'constructor')!;
        Object.defineProperty(source, 'constructor', { enumerable: true });
        const constructorAfter = Object.getOwnPropertyDescriptor(source, 'constructor')!;
        console.log(constructorBefore.value === constructorAfter.value, constructorAfter.value === Owner,
            constructorAfter.writable, constructorAfter.enumerable, constructorAfter.configurable);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-enumerable-update-symbol-and-constructor-controls", IssueSource,
            "true true true true function\ntrue true true true function\ntrue true true true true\n"];
        yield return ["writable-only-and-configurable-only-updates", """
            class Owner { first() { return 7; } second() { return 8; } }
            const source: any = Owner.prototype;
            const firstBefore = Object.getOwnPropertyDescriptor(source, 'first')!;
            const secondBefore = Object.getOwnPropertyDescriptor(source, 'second')!;
            Object.defineProperty(source, 'first', { writable: false });
            Object.defineProperty(source, 'second', { configurable: false });
            const first = Object.getOwnPropertyDescriptor(source, 'first')!;
            const second = Object.getOwnPropertyDescriptor(source, 'second')!;
            console.log(first.value === firstBefore.value, first.writable, first.enumerable,
                first.configurable, source.first === firstBefore.value, first.value.call(source));
            console.log(second.value === secondBefore.value, second.writable, second.enumerable,
                second.configurable, source.second === secondBefore.value, second.value.call(source));
            """, "true false false true true 7\ntrue true false false true 8\n"];
        yield return ["nonconfigurable-readonly-transitions-preserve-rejected-state", """
            class Owner { method() { return 7; } }
            const source: any = Owner.prototype;
            const original = Object.getOwnPropertyDescriptor(source, 'method')!.value;
            Object.defineProperty(source, 'method', { configurable: false });
            Object.defineProperty(source, 'method', { writable: false });
            const replacement = () => 8;
            function refused(descriptor: any): boolean {
                try { Object.defineProperty(source, 'method', descriptor); return false; }
                catch (error) { return error instanceof TypeError; }
            }
            console.log(Object.defineProperty(source, 'method', { value: original }) === source,
                refused({ writable: true }), refused({ enumerable: true }), refused({ value: replacement }));
            const after = Object.getOwnPropertyDescriptor(source, 'method')!;
            console.log(after.value === original, after.writable, after.enumerable, after.configurable,
                source.method === original, after.value.call(source));
            """, "true true true true\ntrue false false false true 7\n"];
        yield return ["fresh-deleted-and-inherited-properties-keep-new-defaults", """
            function show(source: any, key: string) {
                const descriptor = Object.getOwnPropertyDescriptor(source, key)!;
                console.log(descriptor.value === undefined, descriptor.writable, descriptor.enumerable,
                    descriptor.configurable, typeof source[key]);
            }
            class Fresh {}
            Object.defineProperty(Fresh.prototype, 'extra', { enumerable: true });
            show(Fresh.prototype, 'extra');
            class Deleted { method() { return 7; } }
            const deleted: any = Deleted.prototype;
            console.log(delete deleted.method, Object.hasOwn(deleted, 'method'));
            Object.defineProperty(deleted, 'method', { enumerable: true });
            show(deleted, 'method');
            class Parent { method() { return 9; } }
            class Child extends Parent {}
            const inherited = Object.getOwnPropertyDescriptor(Parent.prototype, 'method')!.value;
            Object.defineProperty(Child.prototype, 'method', { enumerable: true });
            show(Child.prototype, 'method');
            console.log(Object.getOwnPropertyDescriptor(Parent.prototype, 'method')!.value === inherited);
            class ExplicitUndefined { method() { return 11; } }
            Object.defineProperty(ExplicitUndefined.prototype, 'method', { value: undefined });
            show(ExplicitUndefined.prototype, 'method');
            """, "true false true false undefined\ntrue false\ntrue false true false undefined\ntrue false true false undefined\ntrue\ntrue true false true undefined\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PartialMethodUpdatesPreserveOmittedOwnDescriptorFields(
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
