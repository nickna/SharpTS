using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PrimitiveStringAccessorReceiverTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["primitive-string-direct-and-indexed-accessor-receiver-issue", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
            let reads = 0;
            try {
                Object.defineProperty(prototype, "indexOf", {
                    configurable: true,
                    get() { "use strict"; reads++; return this; }
                });
                const input: any = "abc", direct: any = input.indexOf, indexed: any = input["indexOf"];
                console.log(direct === input, indexed === input, typeof direct, typeof indexed, reads);
            } finally { Object.defineProperty(prototype, "indexOf", original); }
            """, "true true string string 2\n"];
        yield return ["primitive-and-boxed-string-getters-keep-their-distinct-receivers", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, 'indexOf')!;
            const primitive: any = 'abc', boxed: any = new String('abc');
            let reads = 0;
            try {
                Object.defineProperty(prototype, 'indexOf', {
                    configurable: true, get() { 'use strict'; reads++; return this; }
                });
                const direct: any = primitive.indexOf, indexed: any = primitive['indexOf'];
                const boxedDirect: any = boxed.indexOf, boxedIndexed: any = boxed['indexOf'];
                console.log(direct === primitive, indexed === primitive, typeof direct);
                console.log(boxedDirect === boxed, boxedIndexed === boxed, typeof boxedDirect, reads);
            } finally { Object.defineProperty(prototype, 'indexOf', original); }
            console.log('abc'.indexOf('b'));
            """, "true true string\ntrue true object 4\n1\n"];
        yield return ["data-getterless-and-throwing-string-prototype-reads", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, 'indexOf')!;
            const input: any = 'abc', token = {};
            let reads = 0, receiver = true;
            try {
                Object.defineProperty(prototype, 'indexOf', { value: 23, writable: true, configurable: true });
                console.log(input.indexOf === 23, input['indexOf'] === 23);
                Object.defineProperty(prototype, 'indexOf', { get: undefined, set(value: any) {}, configurable: true });
                console.log(input.indexOf === undefined, input['indexOf'] === undefined, reads);
                Object.defineProperty(prototype, 'indexOf', {
                    configurable: true,
                    get() { 'use strict'; reads++; receiver = receiver && this === input; throw token; }
                });
                try { const value = input.indexOf; console.log('unexpected', value); }
                catch (error) { console.log(error === token); }
                try { const value = input['indexOf']; console.log('unexpected', value); }
                catch (error) { console.log(error === token); }
                console.log(reads, receiver);
            } finally { Object.defineProperty(prototype, 'indexOf', original); }
            console.log(input.indexOf('b'));
            """, "true true\ntrue true 0\ntrue\ntrue\n2 true\n1\n"];
        // Strict TypeScript needs this annotation; SavedCases retains the unchanged original too.
        yield return ["primitive-string-call-order-explicit-this-strict-companion", """
            const prototype: any = String.prototype;
            const original = Object.getOwnPropertyDescriptor(prototype, "indexOf")!;
            const input = "abc", events: string[] = [];
            let result: any;
            try {
                Object.defineProperty(prototype, "indexOf", {
                    configurable: true,
                    get() {
                        "use strict"; events.push("get:" + (this === input));
                        return function(this: any, _needle: string, _position?: number): any {
                            "use strict"; events.push("call:" + (this === input)); return 17;
                        };
                    }
                });
                function receiver() { events.push("receiver"); return input; }
                function first() {
                    events.push("first");
                    Object.defineProperty(prototype, "indexOf", { value: () => 99, writable: true, configurable: true });
                    return "b";
                }
                function second() { events.push("second"); return 0; }
                result = receiver().indexOf(first(), second());
            } finally { Object.defineProperty(prototype, "indexOf", original); }
            console.log(result, events.join(","));
            """, "17 receiver,get:true,first,second,call:true\n"];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        foreach (var row in Cases())
            yield return row;
        yield return PrimitiveStringLiveMethodTests.Cases().Single(row =>
            (string)row[0] == "getter-receiver-and-lookup-before-arguments");
        yield return PrimitiveStringLiveMethodTests.Cases().Single(row =>
            (string)row[0] == "mask-delete-and-inherited-fallback");
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void PrimitiveStringAccessorReadsPreserveTheOriginalReceiver(
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
