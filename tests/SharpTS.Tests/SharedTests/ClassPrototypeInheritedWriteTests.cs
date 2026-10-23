using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassPrototypeInheritedWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            class Base {
                set value(next: any) { (this as any).received = next; }
                get readonly() { return 5; }
            }
            class Derived extends Base {}
            const parent: any = Base.prototype, child: any = Derived.prototype;
            Object.defineProperty(parent, "blocked", { value: 4, writable: false, configurable: true });
            Derived.prototype.value = 8;
            child.blocked = 9; child.readonly = 9;
            console.log(child.received, Object.hasOwn(child, "value"));
            console.log(child.blocked, Object.hasOwn(child, "blocked"), child.readonly, Object.hasOwn(child, "readonly"));
            """, "8 false\n4 false 5 false\n"];
        yield return ["nearest-setter-keeps-actual-nonextensible-receiver", """
            const trace: string[] = [];
            let receiver: any, stored: any;
            class Grand {}
            class Middle extends Grand {}
            class Leaf extends Middle {}
            const grand: any = Grand.prototype, middle: any = Middle.prototype, child: any = Leaf.prototype;
            Object.defineProperty(grand, "value", { set(next: any) { trace.push("grand"); }, configurable: true });
            Object.defineProperty(middle, "value", {
                set(next: any) { trace.push("middle:" + next); receiver = this; stored = next; },
                configurable: true
            });
            Object.preventExtensions(child);
            child.value = 7;
            function strictWrite(target: any) { "use strict"; target.value = 8; }
            strictWrite(child);
            console.log(trace.join(","), receiver === child, stored, Object.hasOwn(child, "value"), Object.isExtensible(child));
            """, "middle:7,middle:8 true 8 false false\n"];
        yield return ["readonly-and-getter-only-descriptors-reject-without-get", """
            let reads = 0;
            class Base { get getterOnly() { reads++; return 5; } }
            class Derived extends Base {}
            const parent: any = Base.prototype, child: any = Derived.prototype;
            Object.defineProperty(parent, "readonly", { value: 4, writable: false, configurable: true });
            child.readonly = 9; child.getterOnly = 9;
            console.log(Object.hasOwn(child, "readonly"), Object.hasOwn(child, "getterOnly"), reads);
            function strictWrite(target: any, key: string) {
                "use strict";
                try { target[key] = 11; console.log("missed"); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            strictWrite(child, "readonly"); strictWrite(child, "getterOnly");
            console.log(reads, child.readonly, child.getterOnly, reads);
            """, "false false 0\ntrue\ntrue\n0 4 5 1\n"];
        yield return ["own-writable-data-and-nearest-data-shadowing-preserve-attributes", """
            let setterCalls = 0;
            class Grand {}
            class Middle extends Grand {}
            class Leaf extends Middle {}
            const grand: any = Grand.prototype, middle: any = Middle.prototype, child: any = Leaf.prototype;
            Object.defineProperty(grand, "created", { set(next: any) { setterCalls++; }, configurable: true });
            Object.defineProperty(middle, "created", { value: 3, writable: true, enumerable: false, configurable: false });
            Object.defineProperty(middle, "owned", { set(next: any) { setterCalls++; }, configurable: true });
            Object.defineProperty(child, "owned", { value: 1, writable: true, enumerable: false, configurable: false });
            child.owned = 2; child.created = 4;
            const owned = Object.getOwnPropertyDescriptor(child, "owned")!, created = Object.getOwnPropertyDescriptor(child, "created")!;
            console.log(child.owned, owned.writable, owned.enumerable, owned.configurable, setterCalls);
            console.log(child.created, middle.created, created.writable, created.enumerable, created.configurable, setterCalls);
            """, "2 true false false 0\n4 3 true true true 0\n"];
        yield return ["computed-async-write-retains-reference-and-evaluation-order", """
            const trace: string[] = [];
            let received: any, actualReceiver: any;
            class Base { set value(next: any) { trace.push("set:" + next); received = next; actualReceiver = this; } }
            class First extends Base {}
            class Second extends Base {}
            const first: any = First.prototype, second: any = Second.prototype;
            let current: any = first;
            function target(): any { trace.push("receiver"); return current; }
            function key(): string { trace.push("key"); return "value"; }
            async function rhs() {
                trace.push("rhs-before"); await Promise.resolve(0);
                trace.push("rhs-after"); current = second; return 7;
            }
            async function run() {
                target()[key()] = await rhs();
                console.log(trace.join(","), received, actualReceiver === first, Object.hasOwn(first, "value"), Object.hasOwn(second, "value"));
            }
            run();
            """, "receiver,key,rhs-before,rhs-after,set:7 7 true false false\n"];
        yield return ["throwing-inherited-setter-preserves-abrupt-value-and-rhs-effects", """
            const marker = { token: 1 };
            let rhsCalls = 0, calls = 0, receiver: any;
            class Base {}
            class Derived extends Base {}
            const parent: any = Base.prototype, child: any = Derived.prototype;
            Object.defineProperty(parent, "value", {
                set(next: any) { calls++; receiver = this; throw marker; },
                configurable: true
            });
            function rhs() { rhsCalls++; return 9; }
            try { child.value = rhs(); console.log("missed"); }
            catch (error) { console.log(error === marker); }
            console.log(rhsCalls, calls, receiver === child, Object.hasOwn(child, "value"));
            """, "true\n1 1 true false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AssignmentsHonorNearestPrototypeDescriptorsAndReceiver(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
