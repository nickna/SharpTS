using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassWritableNameMetadataTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["class-state-and-constructor-identity-survive-renaming", """
            class Declared {
                static count = 2;
                value = 5;
                static bump() { this.count++; return this.count; }
                static self() { return Declared; }
            }
            const Expression = class Internal {
                static count = 7;
                value = 11;
                static bump() { this.count++; return this.count; }
                static self() { return Internal; }
            };
            const declaration: any = Declared;
            const expression: any = Expression;
            Object.defineProperty(declaration, 'name', { value: 'first', writable: true, configurable: true });
            Object.defineProperty(expression, 'name', { value: 'second', writable: true, configurable: true });
            declaration.name = 'declared-renamed';
            expression['name'] = 'expression-renamed';
            console.log(declaration.name, Declared.bump(), new Declared().value, Declared.self() === Declared);
            console.log(expression.name, Expression.bump(), new Expression().value, Expression.self() === Expression);
            console.log(Declared.prototype.constructor === Declared, Expression.prototype.constructor === Expression,
                new Declared() instanceof Declared, new Expression() instanceof Expression);
            """, "declared-renamed 3 5 true\nexpression-renamed 8 11 true\ntrue true true true\n"];
        yield return ["generic-and-native-array-constructor-name-writes", """
            class Generic<T> {
                static count = 1;
                value: T;
                constructor(value: T) { this.value = value; }
            }
            class Values extends Array<number> {}
            const generic: any = Generic;
            const native: any = Values;
            Object.defineProperty(generic, 'name', { value: 'generic-start', writable: true, configurable: true });
            Object.defineProperty(native, 'name', { value: 'native-start', writable: true, configurable: true });
            generic.name = 'generic-written';
            native['name'] = 'native-written';
            console.log(generic.name, Reflect.get(generic, 'name'), Object.getOwnPropertyDescriptor(generic, 'name')!.value);
            console.log(native.name, Reflect.get(native, 'name'), Object.getOwnPropertyDescriptor(native, 'name')!.value);
            console.log(new Generic<number>(7).value, new Generic<string>('ok').value, Generic.count);
            const values = new Values(3, 5);
            console.log(values.length, values[0], values[1], values instanceof Values,
                Object.getOwnPropertyDescriptor(native, 'name')!.writable);
            """, "generic-written generic-written generic-written\nnative-written native-written native-written\n7 ok 1\n2 3 5 true true\n"];
        yield return ["readonly-name-refusal-and-reenabled-writes", """
            function strictWrite(target: any, key: string, value: string) {
                'use strict';
                target[key] = value;
            }
            class Declared {}
            const Expression = class Internal {};
            const ordinary: any = function original() {};
            for (const target of [Declared, Expression, ordinary]) {
                const original = target.name;
                target.name = 'ignored';
                let pristineThrew = false;
                try { strictWrite(target, 'name', 'strict-ignored'); }
                catch (error) { pristineThrew = error instanceof TypeError; }
                console.log(target.name === original, pristineThrew, Object.getOwnPropertyDescriptor(target, 'name')!.writable);
                Object.defineProperty(target, 'name', { value: 'writable', writable: true, configurable: true });
                strictWrite(target, 'name', 'written');
                console.log(target.name, Reflect.get(target, 'name'));
                Object.defineProperty(target, 'name', { writable: false });
                target.name = 'ignored-again';
                let relockedThrew = false;
                try { strictWrite(target, 'name', 'strict-ignored-again'); }
                catch (error) { relockedThrew = error instanceof TypeError; }
                console.log(target.name, relockedThrew, Object.getOwnPropertyDescriptor(target, 'name')!.value);
            }
            """, "true true false\nwritten written\nwritten true written\ntrue true false\nwritten written\nwritten true written\ntrue true false\nwritten written\nwritten true written\n"];
        yield return ["restored-own-name-redefinition-and-partial-value-updates", """
            class Original {}
            const owner: any = Original;
            Object.defineProperty(owner, "name", { value: "renamed", writable: true, configurable: true });
            owner.name = "written";
            console.log(JSON.stringify([owner.name, Reflect.get(owner, "name"), Object.getOwnPropertyDescriptor(owner, "name")!.value]));
            console.log(Reflect.defineProperty(owner, "name", { value: "reflected" }));
            const descriptor = Object.getOwnPropertyDescriptor(owner, "name")!;
            console.log(JSON.stringify([owner.name, Reflect.get(owner, "name"), descriptor.value]));
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "[\"written\",\"written\",\"written\"]\ntrue\n[\"reflected\",\"reflected\",\"reflected\"]\ntrue false true\n"];
        yield return ["typed-and-dynamic-dot-computed-name-writes", """
            class Declared {}
            const Expression = class Internal {};
            const typed: { name: string } = Declared as any;
            const dynamic: any = Expression;
            Object.defineProperty(Declared, 'name', { value: 'declared-start', writable: true, configurable: true });
            Object.defineProperty(Expression, 'name', { value: 'expression-start', writable: true, configurable: true });
            typed.name = 'typed-dot';
            const key: string = 'name';
            dynamic[key] = 'dynamic-index';
            console.log(typed.name, Reflect.get(Declared, 'name'), Object.getOwnPropertyDescriptor(Declared, 'name')!.value);
            console.log(dynamic.name, Reflect.get(Expression, 'name'), Object.getOwnPropertyDescriptor(Expression, 'name')!.value);
            typed['name'] = 'typed-index';
            dynamic.name = 'dynamic-dot';
            console.log(typed.name, dynamic[key]);
            Object.defineProperty(Declared, 'name', { value: 'partial' });
            typed.name = 'after-partial';
            console.log(typed.name, Object.getOwnPropertyDescriptor(Declared, 'name')!.writable,
                Object.getOwnPropertyDescriptor(Expression, 'name')!.writable, Object.hasOwn(Expression, 'name'));
            """, "typed-dot typed-dot typed-dot\ndynamic-index dynamic-index dynamic-index\ntyped-index dynamic-dot\nafter-partial true true true\n"];
        yield return ["writable-class-name-issue", """
            class C {}
            const target: any = C;
            Object.defineProperty(target, "name", { value: "renamed", writable: true, configurable: true });
            target.name = "written";
            console.log(target.name, Reflect.get(target, "name"), Object.getOwnPropertyDescriptor(target, "name")!.value);
            console.log(Object.getOwnPropertyDescriptor(target, "name")!.writable);
            const ordinary: any = function original() {};
            Object.defineProperty(ordinary, "name", { value: "renamed", writable: true, configurable: true });
            ordinary.name = "written";
            console.log(ordinary.name, Reflect.get(ordinary, "name"), Object.getOwnPropertyDescriptor(ordinary, "name")!.value);
            """, "written written written\ntrue\nwritten written written\n"];
        yield return ["exact-strict-indexed-writable-class-name-and-length", """
            function write(target: any, key: string) { "use strict"; target[key] = 8; }
            class C {}
            for (const key of ["name", "length"]) {
                Object.defineProperty(C, key, { value: 7, writable: true, configurable: true });
                try { write(C, key); console.log("accepted"); }
                catch (error) { console.log(error instanceof TypeError); }
                const descriptor = Object.getOwnPropertyDescriptor(C, key)!;
                console.log(descriptor.value, descriptor.writable);
            }
            """, "accepted\n8 true\naccepted\n8 true\n"];
        yield return ["nearest-metadata-descriptors-and-prototype-invariant-preservation", """
            function sloppyWrite(target: any, key: string, value: any) { return target[key] = value; }
            function strictWrite(target: any, key: string, value: any) { 'use strict'; return target[key] = value; }
            class Base {}
            class Child extends Base {}
            const base: any = Base, child: any = Child;
            const state = { gets: 0, sets: 0, stored: 0, same: false };
            delete child.name;
            delete child.length;
            Object.defineProperty(base, 'name', { value: 4, writable: false, configurable: true });
            Object.defineProperty(base, 'length', { get() { state.gets++; return 5; }, configurable: true });
            console.log(sloppyWrite(child, 'name', 7), sloppyWrite(child, 'length', 7), state.gets,
                Object.hasOwn(child, 'name'), Object.hasOwn(child, 'length'));
            for (const key of ['name', 'length']) {
                try { strictWrite(child, key, 9); console.log(false); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            console.log(state.gets, child.name, child.length, state.gets,
                Object.hasOwn(child, 'name'), Object.hasOwn(child, 'length'));
            Object.defineProperty(base, 'name', {
                set(next: number) { state.sets++; state.stored = next; state.same = this === child; }, configurable: true
            });
            console.log(strictWrite(child, 'name', 13), state.sets, state.stored, state.same, Object.hasOwn(child, 'name'));
            const originalPrototype = child.prototype;
            const replacement = {};
            console.log(sloppyWrite(child, 'prototype', replacement) === replacement, child.prototype === originalPrototype);
            try { strictWrite(child, 'prototype', replacement); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            const descriptor = Object.getOwnPropertyDescriptor(child, 'prototype')!;
            console.log(child.prototype === originalPrototype, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "7 7 0 false false\ntrue\ntrue\n0 4 5 1 false false\n13 1 13 true false\ntrue true\ntrue\ntrue false false false\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void WritableClassNamesRemainOrdinaryOwnDataProperties(
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
