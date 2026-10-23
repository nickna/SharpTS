using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassPrototypeIntegrityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["class-prototype-seal-freeze-own-descriptor-issue", """
            class Sealed {}
            const sealed: any = Sealed.prototype;
            sealed.extra = 1;
            const sealedBefore = Object.getOwnPropertyDescriptor(sealed, "extra")!;
            console.log(sealedBefore.writable, sealedBefore.configurable);
            Object.seal(sealed);
            const sealedDescriptor = Object.getOwnPropertyDescriptor(sealed, "extra")!;
            console.log(Object.isExtensible(sealed), sealedDescriptor.writable, sealedDescriptor.configurable);
            console.log(delete sealed.extra, Object.hasOwn(sealed, "extra"));

            class Frozen {}
            const frozen: any = Frozen.prototype;
            frozen.extra = 2;
            const frozenBefore = Object.getOwnPropertyDescriptor(frozen, "extra")!;
            console.log(frozenBefore.writable, frozenBefore.configurable);
            Object.freeze(frozen);
            const frozenDescriptor = Object.getOwnPropertyDescriptor(frozen, "extra")!;
            console.log(Object.isExtensible(frozen), frozenDescriptor.writable, frozenDescriptor.configurable);
            console.log(delete frozen.extra, Object.hasOwn(frozen, "extra"));
            """, "true true\nfalse true false\nfalse true\ntrue true\nfalse false false\nfalse true\n"];
        yield return ["own-named-data-flags-writes-and-absence", """
            for (const mode of ['seal', 'freeze']) {
                class Owner {}
                const prototype: any = Owner.prototype;
                prototype.slot = 1;
                Object.defineProperty(prototype, 'hidden', { value: 3, writable: true, configurable: true });
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                const descriptor = Object.getOwnPropertyDescriptor(prototype, 'slot')!;
                console.log(mode, Object.isExtensible(prototype), descriptor.writable, descriptor.configurable,
                    descriptor.enumerable, Object.isSealed(prototype), Object.isFrozen(prototype));
                prototype.slot = 9;
                console.log(prototype.slot, delete prototype.slot, Object.hasOwn(prototype, 'slot'), delete prototype.absent);
                const hidden = Object.getOwnPropertyDescriptor(prototype, 'hidden')!;
                console.log(hidden.value, hidden.writable, hidden.enumerable, hidden.configurable);
            }
            for (const mode of ['seal', 'freeze']) {
                class Deleted { method() { return 7; } }
                const prototype: any = Deleted.prototype;
                console.log(delete prototype.method, delete prototype.constructor);
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                prototype.method = () => 9;
                prototype.constructor = () => 11;
                let refused = false;
                try { Object.defineProperty(prototype, 'method', { value: () => 13, configurable: true }); }
                catch (error) { refused = error instanceof TypeError; }
                console.log(mode, Object.hasOwn(prototype, 'method'), Object.hasOwn(prototype, 'constructor'),
                    Object.getOwnPropertyDescriptor(prototype, 'method') === undefined, refused);
            }
            """, "seal false true false true true false\n9 false true true\n3 true false false\nfreeze false false false true true true\n1 false true true\n3 false false false\ntrue true\nseal false false true true\ntrue true\nfreeze false false true true\n"];
        yield return ["declared-method-accessor-and-constructor-integrity", """
            for (const mode of ['seal', 'freeze']) {
                let gets = 0, sets = 0, stored = 7;
                let receiver: any;
                class Owner {
                    method() { return this; }
                    get value() { gets++; return stored; }
                    set value(next: number) { sets++; receiver = this; stored = next; }
                }
                const prototype: any = Owner.prototype;
                const methodBefore = Object.getOwnPropertyDescriptor(prototype, 'method')!;
                const accessorBefore = Object.getOwnPropertyDescriptor(prototype, 'value')!;
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                const method = Object.getOwnPropertyDescriptor(prototype, 'method')!;
                const accessor = Object.getOwnPropertyDescriptor(prototype, 'value')!;
                const constructor = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                console.log(mode, gets, sets, method.value === methodBefore.value, method.writable, method.configurable,
                    method.enumerable, constructor.value === Owner, constructor.writable, constructor.configurable);
                console.log(accessor.get === accessorBefore.get, accessor.set === accessorBefore.set, accessor.configurable);
                prototype.value = 11;
                console.log(prototype.value, gets, sets, receiver === prototype, prototype.method() === prototype);
            }
            """, "seal 0 0 true true false false true true false\ntrue true false\n11 1 1 true true\nfreeze 0 0 true false false false true false false\ntrue true false\n11 1 1 true true\n"];
        yield return ["own-accessor-identities-getterless-and-setter-survive-freeze", """
            let gets = 0, sets = 0, stored = 7;
            let receiver: any;
            class Owner {}
            const prototype: any = Owner.prototype;
            Object.defineProperty(prototype, 'value', {
                get() { gets++; return stored; },
                set(next: number) { sets++; receiver = this; stored = next; },
                enumerable: true, configurable: true
            });
            Object.defineProperty(prototype, 'setterOnly', { set(next: number) { sets++; stored = next; }, configurable: true });
            const before = Object.getOwnPropertyDescriptor(prototype, 'value')!;
            Object.freeze(prototype);
            const after = Object.getOwnPropertyDescriptor(prototype, 'value')!;
            console.log(gets, sets, after.get === before.get, after.set === before.set, after.enumerable, after.configurable);
            prototype.value = 11;
            console.log(prototype.value, gets, sets, receiver === prototype, prototype.setterOnly === undefined);
            prototype.setterOnly = 13;
            console.log(stored, sets, Object.getOwnPropertyDescriptor(prototype, 'setterOnly')!.configurable);
            class Parent { set inherited(next: number) { stored = next; receiver = this; } }
            class Child extends Parent {}
            const child: any = Child.prototype;
            Object.freeze(child);
            child.inherited = 17;
            console.log(stored, receiver === child, Object.hasOwn(child, 'inherited'));
            """, "0 0 true true true false\n11 1 1 true true\n13 2 false\n17 true false\n"];
        yield return ["strict-present-deletion-and-absent-deletion-after-integrity", """
            function strictRemove(target: any, key: string): boolean {
                'use strict';
                return delete target[key];
            }
            for (const mode of ['seal', 'freeze']) {
                class Owner {}
                const prototype: any = Owner.prototype;
                prototype.slot = 7;
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                let threw = false;
                try { strictRemove(prototype, 'slot'); } catch (error) { threw = error instanceof TypeError; }
                console.log(mode, threw, Object.hasOwn(prototype, 'slot'),
                    strictRemove(prototype, 'absent'), Reflect.deleteProperty(prototype, 'absent'));
            }
            """, "seal true true true true\nfreeze true true true true\n"];
        yield return ["symbol-data-declared-method-accessor-and-carrier-boundaries", """
            const dataKey: unique symbol = Symbol('data');
            const methodKey: unique symbol = Symbol('method');
            const accessorKey: unique symbol = Symbol('accessor');
            let reads = 0, writes = 0;
            let receiver: any;
            class Owner<T> {
                [methodKey]() { return this; }
                get [accessorKey]() { reads++; return 3; }
                set [accessorKey](next: number) { writes++; receiver = this; }
            }
            const prototype: any = Owner.prototype;
            Object.defineProperty(prototype, dataKey, { value: 7, writable: true, enumerable: true, configurable: true });
            const methodBefore = Object.getOwnPropertyDescriptor(prototype, methodKey)!;
            const accessorBefore = Object.getOwnPropertyDescriptor(prototype, accessorKey)!;
            Object.freeze(prototype);
            console.log(reads, writes, Object.isFrozen(prototype), Object.isExtensible(Owner));
            const data = Object.getOwnPropertyDescriptor(prototype, dataKey)!;
            const method = Object.getOwnPropertyDescriptor(prototype, methodKey)!;
            const accessor = Object.getOwnPropertyDescriptor(prototype, accessorKey)!;
            console.log(data.value, data.writable, data.enumerable, data.configurable);
            console.log(method.value === methodBefore.value, method.writable, method.enumerable, method.configurable);
            console.log(accessor.get === accessorBefore.get, accessor.set === accessorBefore.set, accessor.configurable, reads);
            prototype[accessorKey] = 11;
            console.log(writes, receiver === prototype, prototype[accessorKey], reads, prototype[methodKey]() === prototype);
            class Values extends Array<number> {}
            const nativePrototype: any = Values.prototype;
            nativePrototype.named = 4;
            Object.freeze(nativePrototype);
            const nativeData = Object.getOwnPropertyDescriptor(nativePrototype, 'named')!;
            console.log(nativeData.writable, nativeData.configurable, Object.isExtensible(Values), Object.isExtensible(nativePrototype));
            """, "0 0 true true\n7 false true false\ntrue false false false\ntrue true false 0\n1 true 3 1 true\nfalse false true false\n"];
        yield return ["own-named-data-flags-without-absent-direct-delete", """
            for (const mode of ['seal', 'freeze']) {
                class Owner {}
                const prototype: any = Owner.prototype;
                prototype.slot = 1;
                Object.defineProperty(prototype, 'hidden', { value: 3, writable: true, configurable: true });
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                const descriptor = Object.getOwnPropertyDescriptor(prototype, 'slot')!;
                console.log(mode, Object.isExtensible(prototype), descriptor.writable, descriptor.configurable,
                    descriptor.enumerable, Object.isSealed(prototype), Object.isFrozen(prototype));
                prototype.slot = 9;
                console.log(prototype.slot, delete prototype.slot, Object.hasOwn(prototype, 'slot'));
                const hidden = Object.getOwnPropertyDescriptor(prototype, 'hidden')!;
                console.log(hidden.value, hidden.writable, hidden.enumerable, hidden.configurable);
            }
            for (const mode of ['seal', 'freeze']) {
                class Deleted { method() { return 7; } }
                const prototype: any = Deleted.prototype;
                console.log(delete prototype.method, delete prototype.constructor);
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                prototype.method = () => 9;
                prototype.constructor = () => 11;
                let refused = false;
                try { Object.defineProperty(prototype, 'method', { value: () => 13, configurable: true }); }
                catch (error) { refused = error instanceof TypeError; }
                console.log(mode, Object.hasOwn(prototype, 'method'), Object.hasOwn(prototype, 'constructor'),
                    Object.getOwnPropertyDescriptor(prototype, 'method') === undefined, refused);
            }
            """, "seal false true false true true false\n9 false true\n3 true false false\nfreeze false false false true true true\n1 false true\n3 false false false\ntrue true\nseal false false true true\ntrue true\nfreeze false false true true\n"];
        yield return ["strict-present-and-reflect-absent-deletion", """
            function strictRemove(target: any, key: string): boolean {
                'use strict';
                return delete target[key];
            }
            for (const mode of ['seal', 'freeze']) {
                class Owner {}
                const prototype: any = Owner.prototype;
                prototype.slot = 7;
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                let threw = false;
                try { strictRemove(prototype, 'slot'); } catch (error) { threw = error instanceof TypeError; }
                console.log(mode, threw, Object.hasOwn(prototype, 'slot'),
                    Reflect.deleteProperty(prototype, 'absent'));
            }
            """, "seal true true true\nfreeze true true true\n"];
        yield return ["symbol-carrier-flags-without-setter-assignment", """
            const dataKey: unique symbol = Symbol('data');
            const methodKey: unique symbol = Symbol('method');
            const accessorKey: unique symbol = Symbol('accessor');
            let reads = 0, writes = 0;
            let receiver: any;
            class Owner<T> {
                [methodKey]() { return this; }
                get [accessorKey]() { reads++; return 3; }
                set [accessorKey](next: number) { writes++; receiver = this; }
            }
            const prototype: any = Owner.prototype;
            Object.defineProperty(prototype, dataKey, { value: 7, writable: true, enumerable: true, configurable: true });
            const methodBefore = Object.getOwnPropertyDescriptor(prototype, methodKey)!;
            const accessorBefore = Object.getOwnPropertyDescriptor(prototype, accessorKey)!;
            Object.freeze(prototype);
            console.log(reads, writes, Object.isFrozen(prototype), Object.isExtensible(Owner));
            const data = Object.getOwnPropertyDescriptor(prototype, dataKey)!;
            const method = Object.getOwnPropertyDescriptor(prototype, methodKey)!;
            const accessor = Object.getOwnPropertyDescriptor(prototype, accessorKey)!;
            console.log(data.value, data.writable, data.enumerable, data.configurable);
            console.log(method.value === methodBefore.value, method.writable, method.enumerable, method.configurable);
            console.log(accessor.get === accessorBefore.get, accessor.set === accessorBefore.set, accessor.configurable, reads);
            console.log(writes, receiver === prototype, prototype[accessorKey], reads, prototype[methodKey]() === prototype);
            class Values extends Array<number> {}
            const nativePrototype: any = Values.prototype;
            nativePrototype.named = 4;
            Object.freeze(nativePrototype);
            const nativeData = Object.getOwnPropertyDescriptor(nativePrototype, 'named')!;
            console.log(nativeData.writable, nativeData.configurable, Object.isExtensible(Values), Object.isExtensible(nativePrototype));
            """, "0 0 true true\n7 false true false\ntrue false false false\ntrue true false 0\n0 false 3 1 true\nfalse false true false\n"];
        yield return ["declared-method-accessor-constructor-descriptor-flags-only", """
            for (const mode of ['seal', 'freeze']) {
                let gets = 0, sets = 0, stored = 7;
                let receiver: any;
                class Owner {
                    method() { return this; }
                    get value() { gets++; return stored; }
                    set value(next: number) { sets++; receiver = this; stored = next; }
                }
                const prototype: any = Owner.prototype;
                const methodBefore = Object.getOwnPropertyDescriptor(prototype, 'method')!;
                const accessorBefore = Object.getOwnPropertyDescriptor(prototype, 'value')!;
                if (mode === 'seal') Object.seal(prototype); else Object.freeze(prototype);
                const method = Object.getOwnPropertyDescriptor(prototype, 'method')!;
                const accessor = Object.getOwnPropertyDescriptor(prototype, 'value')!;
                const constructor = Object.getOwnPropertyDescriptor(prototype, 'constructor')!;
                console.log(mode, gets, sets, method.value === methodBefore.value, method.writable, method.configurable,
                    method.enumerable, constructor.value === Owner, constructor.writable, constructor.configurable);
                console.log(accessor.get === accessorBefore.get, accessor.set === accessorBefore.set, accessor.configurable);
            }
            """, "seal 0 0 true true false false true true false\ntrue true false\nfreeze 0 0 true false false false true false false\ntrue true false\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
        foreach (var row in SavedCases())
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void IntegrityLevelsUpdateCanonicalOwnDescriptorsWithoutInvokingAccessors(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (name is "own-named-data-flags-writes-and-absence"
            or "strict-present-deletion-and-absent-deletion-after-integrity"
            or "declared-method-accessor-and-constructor-integrity"
            or "symbol-data-declared-method-accessor-and-carrier-boundaries")
        {
            // Bridge the unchanged raw Cases value to the exact historical LF/finalLF source.
            source = source.ReplaceLineEndings("\n") + "\n";
            Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        }
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
