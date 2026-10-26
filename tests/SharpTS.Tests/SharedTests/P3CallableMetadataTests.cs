using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3CallableMetadataTests
{
    [Theory, ModeData]
    public void VariableAssignedAsyncArrowsHaveInferredOwnNames(ExecutionMode mode)
    {
        const string source = """
            const arrow: any = async (value: any) => value;
            const grouped: any = (async () => 1);
            const unnamed: any[] = [async () => 2];
            console.log(JSON.stringify(arrow.name), JSON.stringify(arrow.bind(null).name));
            console.log(grouped.name, Object.hasOwn(arrow, "name"));
            console.log(JSON.stringify(unnamed[0].name), JSON.stringify(unnamed[0].bind(null).name));
            function make() { const nested: any = async () => 3; return nested; }
            console.log(make().name);
            """;
        Assert.Equal("\"arrow\" \"bound arrow\"\ngrouped true\n\"\" \"bound \"\nnested\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FrozenFunctionMetadataDescriptorsAreNotConfigurable(ExecutionMode mode)
    {
        const string source = """
            function declaration(first: any) {}
            const functions: any[] = [declaration, function named(first: any) {}, (first: any) => first];
            for (const target of functions) {
                Object.freeze(target);
                console.log(Object.isFrozen(target));
                for (const key of ["name", "length"]) {
                    const descriptor: any = Object.getOwnPropertyDescriptor(target, key);
                    console.log(Object.hasOwn(target, key), descriptor.writable,
                        descriptor.enumerable, descriptor.configurable,
                        Reflect.deleteProperty(target, key));
                }
            }
            const sealed: any = (value: any) => value;
            Object.seal(sealed);
            console.log(Object.getOwnPropertyDescriptor(sealed, "name")!.configurable);
            const prevented: any = (value: any) => value;
            Object.preventExtensions(prevented);
            console.log(Object.getOwnPropertyDescriptor(prevented, "name")!.configurable);
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\ntrue false false false false\ntrue false false false false\n", 3))
            + "false\ntrue\n", TestHarness.Run(source, mode));
    }

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
