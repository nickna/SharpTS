using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ObjectStringCompoundAssignmentTests
{
    [Theory, ModeData]
    public void OrdinaryStringAndDynamicKeysReadAndWriteTheObject(ExecutionMode mode)
    {
        const string source = """
            const object: any = { value: 1 };
            object["value"] += 2;
            const key: any = "value";
            object[key] *= 4;
            console.log(object.value);
            const array = [1];
            array[0] += 2;
            console.log(array[0]);
            class C { [key: string]: number; value: number = 1; }
            const instance = new C();
            instance["value"] += 2;
            console.log(instance.value);
            """;
        Assert.Equal("12\n3\n3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReceiverKeyAndReadAreRetainedBeforeTheRightOperand(ExecutionMode mode)
    {
        const string source = """
            const events: string[] = [];
            const original: any = { value: 1 }, other: any = { value: 20 };
            let selected: any = original;
            function receiver(): any { events.push("receiver"); return selected; }
            function key(): string { events.push("key"); return "value"; }
            function right(): number {
                events.push("rhs"); original.value = 100; selected = other; return 2;
            }
            console.log(receiver()[key()] += right(), original.value, other.value, events.join(","));
            """;
        Assert.Equal("3 3 20 receiver,key,rhs\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ComputedPropertyConversionAndAccessorsUseOneReference(ExecutionMode mode)
    {
        const string source = """
            const events: string[] = [];
            let stored = 1;
            const object: any = {};
            Object.defineProperty(object, "value", {
                get() { events.push("get"); return stored; },
                set(value: number) { events.push("set:" + value); stored = value; },
                configurable: true
            });
            const key: any = { [Symbol.toPrimitive](hint: string) { events.push("key:" + hint); return "value"; } };
            function right(): number { events.push("rhs"); stored = 100; return 2; }
            console.log(object[key] += right(), stored, events.join(","));
            """;
        Assert.Equal("3 3 key:string,get,rhs,key:string,set:3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedRightOperandRetainsTheConvertedKeyAndOldValue(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                const events: string[] = [];
                const object: any = { value: 1 };
                const key: any = { [Symbol.toPrimitive](hint: string) { events.push(hint); return "value"; } };
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                async function change() { return object[key] += await pending; }
                const task = change();
                object.value = 100;
                release(2);
                console.log(await task, object.value, events.join(","));
            }
            run().catch((error: any) => console.log("unexpected", error.message));
            """;
        Assert.Equal("3 3 string,string\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullishTargetThrowsBeforeKeyConversionOrRightOperand(ExecutionMode mode)
    {
        const string source = """
            const events: string[] = [];
            const object: any = null;
            const key: any = { [Symbol.toPrimitive](hint: string) { events.push("key"); return "value"; } };
            function right(): number { events.push("rhs"); return 2; }
            try { object[key] += right(); }
            catch (error: any) { console.log(error.name, events.length); }
            """;
        Assert.Equal("TypeError 0\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("const object: number = 1; object[\"value\"] += 1;")]
    [InlineData("const object: any = { value: 1 }; object[true] += 1;")]
    public void NonPropertyTargetsAndKeysStillFailChecking(string source)
        => Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(
            TestHarness.ParseOrThrow(source)));
}
