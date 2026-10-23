using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BuiltInPrototypeConstructorAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["live-constructor-assignment-retains-own-data-descriptor", """
            const prototypes: any[] = [String.prototype, Number.prototype, Boolean.prototype,
                Array.prototype, Object.prototype, Function.prototype, Error.prototype,
                Promise.prototype, BigInt.prototype, Symbol.prototype];
            for (let i = 0; i < prototypes.length; i++) {
                const prototype: any = prototypes[i];
                const original = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                const marker = { index: i };
                try {
                    prototype.constructor = marker;
                    const descriptor = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                    console.log(i, descriptor.value === marker, descriptor.writable, descriptor.enumerable, descriptor.configurable);
                } finally {
                    Object.defineProperty(prototype, 'constructor', original);
                }
            }
            """, "0 true true false true\n1 true true false true\n2 true true false true\n3 true true false true\n4 true true false true\n5 true true false true\n6 true true false true\n7 true true false true\n8 true true false true\n9 true true false true\n"];
        yield return ["explicit-constructor-descriptors-retain-flags-and-accessor-identities", """
            const prototypes: any[] = [String.prototype, Number.prototype, Boolean.prototype,
                Array.prototype, Object.prototype, Function.prototype, Error.prototype,
                Promise.prototype, BigInt.prototype, Symbol.prototype];
            for (let i = 0; i < prototypes.length; i++) {
                const prototype: any = prototypes[i];
                const original = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                const state = { reads: 0, writes: 0 };
                const getter = function(): number { state.reads++; return 7; };
                const setter = function(value: any): void { state.writes++; };
                try {
                    Object.defineProperty(prototype, 'constructor', { value: 5, writable: true, enumerable: true, configurable: true });
                    prototype.constructor = 6;
                    const data = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                    console.log(i, data.value, data.writable, data.enumerable, data.configurable);
                    Object.defineProperty(prototype, 'constructor', { get: getter, set: setter, enumerable: true, configurable: true });
                    Object.defineProperty(prototype, 'constructor', { enumerable: false });
                    const accessor = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                    console.log(accessor.get === getter, accessor.set === setter, accessor.enumerable, accessor.configurable,
                        accessor.value === undefined, accessor.writable === undefined, state.reads, state.writes);
                } finally {
                    Object.defineProperty(prototype, 'constructor', original);
                }
            }
            """, "0 6 true true true\ntrue true false true true true 0 0\n1 6 true true true\ntrue true false true true true 0 0\n2 6 true true true\ntrue true false true true true 0 0\n3 6 true true true\ntrue true false true true true 0 0\n4 6 true true true\ntrue true false true true true 0 0\n5 6 true true true\ntrue true false true true true 0 0\n6 6 true true true\ntrue true false true true true 0 0\n7 6 true true true\ntrue true false true true true 0 0\n8 6 true true true\ntrue true false true true true 0 0\n9 6 true true true\ntrue true false true true true 0 0\n"];
        yield return ["deleted-constructor-and-fresh-expando-defaults", """
            const prototypes: any[] = [String.prototype, Number.prototype, Boolean.prototype,
                Array.prototype, Object.prototype, Function.prototype, Error.prototype,
                Promise.prototype, BigInt.prototype, Symbol.prototype];
            for (let index = 0; index < prototypes.length; index++) {
                const prototype = prototypes[index];
                const original = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                try {
                    const deleted = delete prototype.constructor;
                    prototype.constructor = 2;
                    const constructor = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                    console.log(index, deleted, constructor.value, constructor.writable,
                        constructor.enumerable, constructor.configurable);
                    prototype.__constructorLifecycleExtra = 1;
                    const extra = Object.getOwnPropertyDescriptor(prototype, '__constructorLifecycleExtra')!;
                    console.log(extra.value, extra.writable, extra.enumerable, extra.configurable);
                } finally {
                    delete prototype.__constructorLifecycleExtra;
                    Object.defineProperty(prototype, 'constructor', original);
                }
            }
            """, "0 true 2 true true true\n1 true true true\n1 true 2 true true true\n1 true true true\n2 true 2 true true true\n1 true true true\n3 true 2 true true true\n1 true true true\n4 true 2 true true true\n1 true true true\n5 true 2 true true true\n1 true true true\n6 true 2 true true true\n1 true true true\n7 true 2 true true true\n1 true true true\n8 true 2 true true true\n1 true true true\n9 true 2 true true true\n1 true true true\n"];
        yield return ["deleted-constructor-recreation-and-fresh-expando-defaults-order", """
            const prototypes: any[] = [String.prototype, Number.prototype, Boolean.prototype,
                Array.prototype, Object.prototype, Function.prototype, Error.prototype,
                Promise.prototype, BigInt.prototype, Symbol.prototype];
            for (let i = 0; i < prototypes.length; i++) {
                const prototype: any = prototypes[i];
                const original = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                try {
                    prototype.overlayFirst = 1;
                    const deleted = delete prototype.constructor;
                    const absent = !Object.hasOwn(prototype, 'constructor');
                    prototype.constructor = 2;
                    prototype.overlayLast = 3;
                    const descriptor = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                    const extra = Object.getOwnPropertyDescriptor(prototype, 'overlayFirst')!;
                    console.log(i, deleted, absent, descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
                    let order = '';
                    for (const key of Object.keys(prototype)) {
                        if (key === 'overlayFirst' || key === 'constructor' || key === 'overlayLast') order += key + ',';
                    }
                    console.log(order, extra.value, extra.writable, extra.enumerable, extra.configurable);
                } finally {
                    delete prototype.overlayFirst;
                    delete prototype.overlayLast;
                    Object.defineProperty(prototype, 'constructor', original);
                }
            }
            """.ReplaceLineEndings("\n") + "\n", "0 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n1 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n2 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n3 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n4 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n5 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n6 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n7 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n8 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n9 true true 2 true true true\noverlayFirst,constructor,overlayLast, 1 true true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ConstructorWritesRetainLiveDescriptorsAndCreateOrdinaryPropertiesAfterDeletion(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
