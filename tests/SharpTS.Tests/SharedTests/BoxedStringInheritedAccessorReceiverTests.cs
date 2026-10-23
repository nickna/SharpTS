using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BoxedStringInheritedAccessorReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["boxed-own-getter-control", """
            const input: any = new String('abc');
            let reads = 0;
            Object.defineProperty(input, 'probe', {
                configurable: true,
                get() { 'use strict'; reads++; return this; }
            });
            const direct: any = input.probe;
            const indexed: any = input['probe'];
            console.log(direct === input, indexed === input, typeof direct, reads);
            """ + "\n", "true true object 2\n"];
        yield return ["boxed-selected-prototype-receiver-and-abrupt", """
            const first: any = new String('abc'), second: any = new String('xyz');
            const ancestor: any = Object.create(String.prototype);
            let reads = 0, receiver = true;
            let expected: any;
            const marker = {}, token = {};
            Object.defineProperty(ancestor, 'probe', {
                configurable: true, get() { 'use strict'; reads++; return this; }
            });
            Object.setPrototypeOf(first, Object.create(ancestor));
            Object.setPrototypeOf(second, ancestor);
            console.log('inherited', first.probe === first, second['probe'] === second, reads, Object.hasOwn(first, 'probe'));
            Object.defineProperty(ancestor, 'boom', {
                configurable: true,
                get() { 'use strict'; reads++; receiver = receiver && this === expected; throw token; }
            });
            expected = first;
            try { first.boom; } catch (error) { console.log('dot', error === token, receiver, reads); }
            expected = second;
            try { second['boom']; } catch (error) { console.log('index', error === token, receiver, reads); }
            Object.defineProperty(ancestor, 'probe', { configurable: true, get: undefined, set(value: any) {} });
            console.log('getterless', first.probe === undefined, reads);
            Object.defineProperty(first, 'probe', { configurable: true, value: marker });
            console.log('own', first.probe === marker, Object.hasOwn(first, 'probe'), reads);
            delete first.probe;
            Object.defineProperty(ancestor, 'probe', { configurable: true, value: 17 });
            console.log('data', first.probe, Object.hasOwn(first, 'probe'), reads);
            Object.setPrototypeOf(first, null);
            console.log('null', first.probe === undefined, Object.getPrototypeOf(first) === null, reads);
            """ + "\n", "inherited true true 2 false\ndot true true 3\nindex true true 4\ngetterless true 4\nown true true 4\ndata 17 false 4\nnull true true 4\n"];
        yield return ["ordinary-derived-control", """
            const proto: any = { base: 3, get value() { return (this as any).own + this.base; } };
            const value: any = Object.create(proto);
            value.own = 4;
            console.log(value.value, 'base' in value, Object.hasOwn(value, 'base'), Object.getPrototypeOf(value) === proto);
            """ + "\n", "7 true false true\n"];
        var original = PrimitiveStringAccessorReceiverTests.Cases().Single(row =>
            (string)row[0] == "primitive-and-boxed-string-getters-keep-their-distinct-receivers");
        // The historical whole program adds a final CRLF to the existing payload.
        yield return ["primitive-and-boxed-string-getters-keep-their-distinct-receivers", (string)original[1] + "\r\n", original[2]];
        yield return ["public-boxed-string-inherited-getter", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, 'indexOf')!;
            const input: any = new String('abc');
            let reads = 0;
            try {
                Object.defineProperty(prototype, 'indexOf', {
                    configurable: true,
                    get() { 'use strict'; reads++; return this; }
                });
                const direct: any = input.indexOf;
                const indexed: any = input['indexOf'];
                console.log(direct === input, indexed === input, typeof direct, reads);
            } finally { Object.defineProperty(prototype, 'indexOf', original); }
            """ + "\n", "true true object 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void InheritedAccessorsRetainOriginalWrapperReceiver(
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
