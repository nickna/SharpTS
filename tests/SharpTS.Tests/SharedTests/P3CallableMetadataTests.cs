using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3CallableMetadataTests
{
    [Theory, ModeData]
    public void RepeatedClassExpressionAccessorMetadataIsIndependent(ExecutionMode mode)
    {
        const string source = """
            function make(value: number) {
                return class {
                    get result() { return value; }
                    set result(next: number) { value = next; }
                    static get result() { return value; }
                };
            }
            const A = make(41), B = make(42);
            const first: any = Object.getOwnPropertyDescriptor(A.prototype, "result")!.get;
            const second: any = Object.getOwnPropertyDescriptor(B.prototype, "result")!.get;
            Object.defineProperty(first, "name", { value: "changed" });
            console.log(first === second, first.name, second.name);
            console.log(Object.getOwnPropertyDescriptor(A.prototype, "result")!.get === first);
            console.log(new A().result, new B().result);
            const staticFirst: any = Object.getOwnPropertyDescriptor(A, "result")!.get;
            const staticSecond: any = Object.getOwnPropertyDescriptor(B, "result")!.get;
            Object.defineProperty(staticFirst, "length", { value: 7 });
            console.log(staticFirst === staticSecond, staticFirst.length, staticSecond.length);
            const setterFirst: any = Object.getOwnPropertyDescriptor(A.prototype, "result")!.set;
            const setterSecond: any = Object.getOwnPropertyDescriptor(B.prototype, "result")!.set;
            Object.defineProperty(setterFirst, "name", { value: "setter" });
            console.log(setterFirst.name, setterSecond.name);
            """;
        Assert.Equal("false changed get result\ntrue\n41 42\nfalse 7 0\nsetter set result\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumberConstructorAliasHasItsOwnInitialLength(ExecutionMode mode)
    {
        const string source = """
            const constructor: any = Number;
            const descriptor: any = Object.getOwnPropertyDescriptor(constructor, "length");
            console.log(constructor.length, Reflect.get(constructor, "length"), descriptor.value);
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
            Object.defineProperty(constructor, "length", { value: 5, configurable: true });
            console.log(constructor.length, Reflect.get(constructor, "length"));
            console.log(delete constructor.length, Object.hasOwn(constructor, "length"));
            """;
        Assert.Equal("1 1 1\nfalse false true\n5 5\ntrue false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DeclaredFunctionLengthReadsObserveDescriptorChanges(ExecutionMode mode)
    {
        const string source = """
            function fn(first: any) { return first; }
            Object.defineProperty(fn, "length", { value: 4, configurable: true });
            console.log(fn.length, fn(17));
            Object.defineProperty(fn, "length", { get() { return 6; }, configurable: true });
            console.log(fn.length);
            console.log(delete fn.length, fn.length);
            """;
        Assert.Equal("4 17\n6\ntrue 0\n", TestHarness.Run(source, mode));
    }
}
