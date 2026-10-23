using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorMetadataIntegrityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["expression-and-derived-constructor-metadata-integrity", """
            const Expression = class Internal {};
            class Parent {}
            class Child extends Parent {}
            const parentConstructor: any = Parent;
            Object.defineProperty(parentConstructor, 'name', { value: 'parent-name', writable: true, configurable: true });
            const targets: any[] = [Expression, Child];
            for (const target of targets) {
                const originalName = target.name, originalLength = target.length;
                Object.seal(target);
                const sealedName = Object.getOwnPropertyDescriptor(target, 'name')!;
                const sealedLength = Object.getOwnPropertyDescriptor(target, 'length')!;
                console.log(target.name, Reflect.get(target, 'name'), sealedName.value,
                    sealedName.writable, sealedName.enumerable, sealedName.configurable,
                    sealedLength.value === originalLength, sealedLength.configurable);
                console.log(Reflect.deleteProperty(target, 'name'), Reflect.deleteProperty(target, 'length'),
                    Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
                Object.freeze(target);
                console.log(target.name === originalName, target.length === originalLength,
                    Object.isSealed(target), Object.isFrozen(target),
                    Object.getOwnPropertyDescriptor(target, 'name')!.configurable,
                    Object.getOwnPropertyDescriptor(target, 'length')!.configurable);
            }
            console.log(parentConstructor.name, Object.getOwnPropertyDescriptor(parentConstructor, 'name')!.writable,
                Object.getOwnPropertyDescriptor(parentConstructor, 'name')!.configurable, Object.isExtensible(parentConstructor));
            """, "Internal Internal Internal false false false true false\nfalse false true true\ntrue true true true false false\nChild Child Child false false false true false\nfalse false true true\ntrue true true true false false\nparent-name true true true\n"];
        yield return ["writable-own-metadata-seal-preserves-writes-freeze-locks-values", """
            class Writable {}
            const target: any = Writable;
            Object.defineProperty(target, 'name', { value: 'original', writable: true, enumerable: true, configurable: true });
            Object.defineProperty(target, 'length', { value: 2, writable: true, enumerable: false, configurable: true });
            Object.seal(target);
            target.name = 'written';
            target.length = 7;
            const sealedName = Object.getOwnPropertyDescriptor(target, 'name')!;
            const sealedLength = Object.getOwnPropertyDescriptor(target, 'length')!;
            console.log(Object.isSealed(target), Object.isFrozen(target), target.name, target.length);
            console.log(sealedName.value, sealedName.writable, sealedName.enumerable, sealedName.configurable,
                sealedLength.value, sealedLength.writable, sealedLength.enumerable, sealedLength.configurable);
            console.log(Reflect.deleteProperty(target, 'name'), Reflect.deleteProperty(target, 'length'));
            console.log(Reflect.defineProperty(target, 'name', { value: 'after-seal' }),
                Reflect.defineProperty(target, 'length', { value: 9 }));
            Object.freeze(target);
            Object.freeze(target);
            Object.seal(target);
            target.name = 'ignored';
            target.length = 99;
            const frozenName = Object.getOwnPropertyDescriptor(target, 'name')!;
            const frozenLength = Object.getOwnPropertyDescriptor(target, 'length')!;
            console.log(Object.isSealed(target), Object.isFrozen(target), target.name, target.length);
            console.log(frozenName.value, frozenName.writable, frozenName.enumerable, frozenName.configurable,
                frozenLength.value, frozenLength.writable, frozenLength.enumerable, frozenLength.configurable);
            console.log(Reflect.set(target, 'name', 'blocked'), Reflect.set(target, 'length', 99),
                Reflect.defineProperty(target, 'name', { value: 'blocked' }),
                Reflect.defineProperty(target, 'length', { value: 99 }));
            console.log(target.name, target.length, Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
            """, "true false written 7\nwritten true true false 7 true false false\nfalse false\ntrue true\ntrue true after-seal 9\nafter-seal false true false 9 false false false\nfalse false false false\nafter-seal 9 true true\n"];
        yield return ["accessor-name-and-deleted-metadata-preserve-integrity", """
            class AccessorOwner {}
            const owner: any = AccessorOwner;
            const state = { reads: 0, writes: 0, value: 'initial', receiver: false };
            const getter = function(this: any) { state.reads++; return state.value; };
            const setter = function(this: any, value: string) {
                state.writes++;
                state.value = value;
                state.receiver = this === owner;
            };
            Object.defineProperty(owner, 'name', { get: getter, set: setter, enumerable: true, configurable: true });
            Object.seal(owner);
            Object.freeze(owner);
            const descriptor = Object.getOwnPropertyDescriptor(owner, 'name')!;
            console.log(state.reads, state.writes, descriptor.get === getter, descriptor.set === setter,
                'value' in descriptor, 'writable' in descriptor, descriptor.enumerable, descriptor.configurable);
            owner.name = 'changed';
            console.log(state.writes, state.receiver, Object.isFrozen(owner));
            console.log(owner.name, state.reads, Reflect.get(owner, 'name'), state.reads);
            class Parent {}
            class Child extends Parent {}
            const child: any = Child;
            console.log(delete child.name, delete child.length);
            Object.freeze(child);
            console.log(Object.getOwnPropertyDescriptor(child, 'name') === undefined,
                Object.getOwnPropertyDescriptor(child, 'length') === undefined,
                Object.hasOwn(child, 'name'), Object.hasOwn(child, 'length'));
            """, "0 0 true true false false true false\n1 true true\nchanged 1 changed 2\ntrue true\ntrue true false false\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(
        ClassNameReflectionTests.Cases().Where(row => (string)row[0] == "metadata-integrity"));

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorMetadataKeepsIntegrityAcrossDescriptorOperations(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
