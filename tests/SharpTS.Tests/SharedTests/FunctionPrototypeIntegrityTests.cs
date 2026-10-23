using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FunctionPrototypeIntegrityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-freeze-function-prototype-constructor", """
            const prototype: any = Function.prototype;
            const original = prototype.constructor;
            Object.freeze(prototype);
            const descriptor = Object.getOwnPropertyDescriptor(prototype, "constructor")!;
            console.log(Object.isFrozen(prototype), descriptor.writable,
                descriptor.enumerable, descriptor.configurable);
            prototype.constructor = 7;
            console.log(prototype.constructor === original, prototype.constructor === 7);
            """, "true false false false\ntrue false\n"];
        yield return ["exact-seal-function-prototype-constructor", """
            const prototype: any = Function.prototype;
            Object.seal(prototype);
            const descriptor = Object.getOwnPropertyDescriptor(prototype, "constructor")!;
            console.log(Object.isSealed(prototype), descriptor.writable,
                descriptor.enumerable, descriptor.configurable);
            prototype.constructor = 7;
            console.log(prototype.constructor === 7);
            """, "true true false false\ntrue\n"];
        yield return ["seal-then-freeze-intrinsic-accessor-and-symbol-descriptor-snapshots", """
            const prototype: any = Function.prototype;
            const names = ['constructor', 'name', 'length', 'call', 'apply', 'bind', 'toString'];
            const originalValues: any = {};
            for (const name of names) originalValues[name] = Object.getOwnPropertyDescriptor(prototype, name)!.value;
            const state = { gets: 0, sets: 0 };
            const getter = function(): number { state.gets++; return 11; };
            const setter = function(value: any): void { state.sets++; };
            const key = Symbol('integrity');
            Object.defineProperty(prototype, 'integrityAccessor', { get: getter, set: setter, enumerable: true, configurable: true });
            Object.defineProperty(prototype, key, { value: 9, writable: true, enumerable: true, configurable: true });
            for (const frozen of [false, true]) {
                if (frozen) Object.freeze(prototype);
                else Object.seal(prototype);
                console.log(frozen, Object.isSealed(prototype), Object.isFrozen(prototype), Object.isExtensible(prototype));
                for (const name of names) {
                    const descriptor = Object.getOwnPropertyDescriptor(prototype, name)!;
                    console.log(name, descriptor.value === originalValues[name], descriptor.writable, descriptor.enumerable, descriptor.configurable);
                }
                const accessor = Object.getOwnPropertyDescriptor(prototype, 'integrityAccessor')!;
                const symbol = Object.getOwnPropertyDescriptor(prototype, key)!;
                console.log(accessor.get === getter, accessor.set === setter, accessor.enumerable, accessor.configurable,
                    accessor.value === undefined, accessor.writable === undefined, state.gets, state.sets);
                console.log(symbol.value, symbol.writable, symbol.enumerable, symbol.configurable, Object.hasOwn(prototype, key));
            }
            """, "false true false false\nconstructor true true false false\nname true false false false\nlength true false false false\ncall true true false false\napply true true false false\nbind true true false false\ntoString true true false false\ntrue true true false true true 0 0\n9 true true false true\ntrue true true false\nconstructor true false false false\nname true false false false\nlength true false false false\ncall true false false false\napply true false false false\nbind true false false false\ntoString true false false false\ntrue true true false true true 0 0\n9 false true false true\n"];
        yield return ["deleted-intrinsic-stays-absent-through-nonextensible-seal-freeze", """
            const prototype: any = Function.prototype;
            const originalConstructor = prototype.constructor;
            console.log(delete prototype.apply);
            Object.preventExtensions(prototype);
            Object.seal(prototype);
            const sealed = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
            console.log(Object.isSealed(prototype), Object.isExtensible(prototype), Object.hasOwn(prototype, 'apply'),
                Object.getOwnPropertyDescriptor(prototype, 'apply') === undefined, prototype.apply === undefined);
            console.log(sealed.value === originalConstructor, sealed.writable, sealed.enumerable, sealed.configurable);
            Object.freeze(prototype);
            const frozen = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
            console.log(Object.isFrozen(prototype), Object.isExtensible(prototype), Object.hasOwn(prototype, 'apply'),
                Object.getOwnPropertyDescriptor(prototype, 'apply') === undefined, prototype.apply === undefined);
            console.log(frozen.value === originalConstructor, frozen.writable, frozen.enumerable, frozen.configurable);
            """, "true\ntrue false false true true\ntrue true false false\ntrue false false true true\ntrue false false false\n"];
        yield return ["preventextensions-preserves-live-flags-and-rejects-new-property", """
            const prototype: any = Function.prototype;
            const originalConstructor = prototype.constructor;
            const originalCall = prototype.call;
            Object.preventExtensions(prototype);
            const before = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
            console.log(Object.isExtensible(prototype), Object.isSealed(prototype), Object.isFrozen(prototype),
                before.writable, before.enumerable, before.configurable);
            prototype.constructor = 7;
            prototype.__integrityNew = 9;
            const after = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
            console.log(prototype.constructor === 7, before.value === originalConstructor, prototype.call === originalCall);
            console.log(after.value === 7, after.writable, after.enumerable, after.configurable);
            console.log(Object.hasOwn(prototype, '__integrityNew'), prototype.__integrityNew === undefined);
            """.ReplaceLineEndings("\n") + "\n", "false false false true false true\ntrue true true\ntrue true false true\nfalse true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void SealAndFreezeUseTheAuthoritativeOwnDescriptors(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
