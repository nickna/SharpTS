using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorPrototypeMutationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["base-declaration-expression-custom-null-restored", """
            function declared(): any {
                class Owner { static own = 3; static read() { return this.own; } value = 5; }
                return Owner;
            }
            function expression(): any {
                return class Owner { static own = 3; static read() { return this.own; } value = 5; };
            }
            for (const factory of [declared, expression]) {
                const owner: any = factory(), initial = Object.getPrototypeOf(owner), prototype = owner.prototype;
                for (const selected of [{ marker: 7 }, null, initial]) {
                    console.log(Object.setPrototypeOf(owner, selected) === owner,
                        Object.getPrototypeOf(owner) === selected, Reflect.getPrototypeOf(owner) === selected);
                    const instance: any = new owner();
                    console.log(owner.own, owner.read(), owner.prototype === prototype,
                        instance.value, instance instanceof owner);
                }
            }
            """, string.Concat(Enumerable.Repeat("true true true\n3 3 true 5 true\n", 6))];

        yield return ["derived-descendant-live-constructor-chain", """
            const state = { oldGets: 0 };
            class Parent {
                static inherited = 11;
                static get observed() { state.oldGets++; return 'old'; }
            }
            class Child extends Parent { static own = 3; }
            class Descendant extends Child {}
            class Alternate {
                static inherited = 33;
                static get observed(): any { return (this as any).own + 100; }
            }
            const child: any = Child, descendant: any = Descendant, initial = Object.getPrototypeOf(child);
            const custom: any = { inherited: 22, observed: 'custom' };
            Object.setPrototypeOf(child, custom);
            console.log(child.inherited, descendant.inherited, child.observed, descendant.observed,
                state.oldGets, descendant.own);
            Object.setPrototypeOf(child, Alternate);
            console.log(child.inherited, descendant.inherited, child.observed, descendant.observed, state.oldGets);
            Object.setPrototypeOf(child, null);
            console.log(child.inherited === undefined, descendant.inherited === undefined,
                Reflect.get(descendant, 'inherited') === undefined,
                descendant.observed === undefined, descendant.bind === undefined, descendant.own, state.oldGets);
            Object.setPrototypeOf(child, initial);
            const instance: any = new Descendant();
            console.log(child.inherited, descendant.inherited, descendant.observed, state.oldGets,
                instance instanceof Child, instance instanceof Parent, instance instanceof Descendant);
            """, "22 22 custom custom 0 3\n33 33 103 103 0\ntrue true true true true 3 0\n11 11 old 1 true true true\n"];

        yield return ["string-symbol-getters-and-own-masking", """
            class Owner { static marker = 7; }
            const owner: any = Owner, key = Symbol('read'), method = function(this: any) { return this; };
            const custom: any = Object.create(Function.prototype), receiver: any = { marker: 20 };
            const state = { gets: 0, ownerReads: 0, otherReads: 0 };
            function observe(value: any): any {
                state.gets++;
                if (value === owner) state.ownerReads++; else if (value === receiver) state.otherReads++;
                return value.marker;
            }
            Object.defineProperty(custom, 'read', { get: function(this: any) { return observe(this); }, configurable: true });
            Object.defineProperty(custom, key, { get: function(this: any) { return observe(this) + 1; }, configurable: true });
            custom.method = method;
            Object.setPrototypeOf(owner, custom);
            console.log(owner.read, owner[key], Reflect.get(owner, 'read', receiver), Reflect.get(owner, key, receiver),
                state.gets, state.ownerReads, state.otherReads);
            console.log(owner.method === method, owner['method'] === method,
                Reflect.get(owner, 'method') === method, owner.method() === owner);
            Object.defineProperty(owner, 'read', { value: undefined, configurable: true });
            Object.defineProperty(owner, key, { get: undefined, configurable: true });
            console.log(owner.read === undefined, owner['read'] === undefined, owner[key] === undefined, state.gets);
            """, "7 8 20 21 4 2 2\ntrue true true true\ntrue true true 4\n"];

        yield return ["writes-follow-selected-descriptors", """
            const masked = Symbol('masked'), setter = Symbol('setter'), getterOnly = Symbol('getter-only');
            const state = { oldSets: 0, sets: 0, receiver: true };
            class Parent { static set oldSetter(value: number) { state.oldSets++; } }
            Object.defineProperty(Parent, 'masked', { value: 1, writable: false, configurable: true });
            Object.defineProperty(Parent, masked, { value: 2, writable: false, configurable: true });
            class Owner extends Parent { static marker = 0; }
            const owner: any = Owner, receiver: any = { marker: 0 }, custom: any = {};
            Object.defineProperty(custom, 'masked', { value: 3, writable: true, configurable: true });
            Object.defineProperty(custom, masked, { value: 4, writable: true, configurable: true });
            Object.defineProperty(custom, 'value', { set: function(this: any, value: number) {
                state.sets++; state.receiver = state.receiver && (this === owner || this === receiver); this.marker = value;
            }, configurable: true });
            Object.defineProperty(custom, setter, { set: function(this: any, value: number) {
                state.sets++; state.receiver = state.receiver && this === owner; this.marker = value;
            }, configurable: true });
            Object.defineProperty(custom, 'locked', { value: 5, writable: false, configurable: true });
            Object.defineProperty(custom, getterOnly, { get() { return 6; }, configurable: true });
            function write(target: any, key: any, value: number) { 'use strict'; target[key] = value; }
            Object.setPrototypeOf(owner, custom);
            write(owner, 'masked', 9); write(owner, masked, 10);
            owner.value = 11; owner[setter] = 12;
            console.log(Reflect.set(owner, 'value', 13, receiver));
            write(owner, 'oldSetter', 14);
            console.log(owner.masked, owner[masked], owner.marker, receiver.marker,
                state.sets, state.receiver, state.oldSets, owner.oldSetter,
                Object.hasOwn(owner, 'masked'), Object.hasOwn(owner, masked));
            try { write(owner, 'locked', 20); } catch (error) { console.log(error instanceof TypeError); }
            try { write(owner, getterOnly, 20); } catch (error) { console.log(error instanceof TypeError); }
            console.log(owner.locked, owner[getterOnly], Object.hasOwn(owner, 'locked'), Object.hasOwn(owner, getterOnly));
            """, "true\n9 10 12 13 3 true 0 14 true true\ntrue\ntrue\n5 6 false false\n"];

        yield return ["cycles-nonextensible-and-same-prototype", """
            class Self {}
            const selfInitial = Object.getPrototypeOf(Self);
            console.log(Reflect.setPrototypeOf(Self, Self), Object.getPrototypeOf(Self) === selfInitial);
            try { Object.setPrototypeOf(Self, Self); }
            catch (error) { console.log(error instanceof TypeError, Object.getPrototypeOf(Self) === selfInitial); }
            class Parent {}
            class Child extends Parent {}
            const parentInitial = Object.getPrototypeOf(Parent);
            console.log(Reflect.setPrototypeOf(Parent, Child), Object.getPrototypeOf(Parent) === parentInitial,
                Object.getPrototypeOf(Child) === Parent);
            try { Object.setPrototypeOf(Parent, Child); }
            catch (error) { console.log(error instanceof TypeError, Object.getPrototypeOf(Parent) === parentInitial,
                Object.getPrototypeOf(Child) === Parent); }
            class First {}
            class Second {}
            Object.setPrototypeOf(First, Second);
            console.log(Reflect.setPrototypeOf(Second, First), Object.getPrototypeOf(Second) === Function.prototype);
            try { Object.setPrototypeOf(Second, First); } catch (error) { console.log(error instanceof TypeError); }
            const bridge: any = {};
            Object.setPrototypeOf(bridge, First);
            console.log(Reflect.setPrototypeOf(First, bridge));
            Object.preventExtensions(First);
            console.log(Object.setPrototypeOf(First, Second) === First, Reflect.setPrototypeOf(First, Second),
                Reflect.setPrototypeOf(First, null));
            try { Object.setPrototypeOf(First, null); } catch (error) { console.log(error instanceof TypeError); }
            class Sealed {}
            const initial = Object.getPrototypeOf(Sealed);
            Object.seal(Sealed);
            console.log(Object.setPrototypeOf(Sealed, initial) === Sealed, Reflect.setPrototypeOf(Sealed, {}),
                Object.getPrototypeOf(Sealed) === initial);
            """, "false true\ntrue true\nfalse true true\ntrue true true\nfalse true\ntrue\nfalse\ntrue true false\ntrue\ntrue false true\n"];

        yield return ["proxy-prototype-forwarding-and-no-eager-traps", """
            class Owner { static marker = 7; }
            const owner: any = Owner, state = { prototypes: 0, gets: 0, sets: 0, receiver: true };
            const target: any = { get read() { return (this as any).marker; } };
            const handler: any = {
                getPrototypeOf() { state.prototypes++; return owner; },
                get(target: any, key: any, receiver: any) {
                    state.gets++; state.receiver = state.receiver && receiver === owner;
                    return Reflect.get(target, key, receiver);
                },
                set(target: any, key: any, value: any, receiver: any) {
                    state.sets++; state.receiver = state.receiver && receiver === owner;
                    return Reflect.set(target, key, value, receiver);
                }
            };
            const proxy: any = new Proxy(target, handler);
            Object.setPrototypeOf(owner, proxy);
            console.log(Object.getPrototypeOf(owner) === proxy, state.prototypes, state.gets, state.sets);
            console.log(owner.read, state.gets, state.receiver);
            owner.created = 11;
            console.log(owner.created, Object.hasOwn(owner, 'created'), Object.hasOwn(target, 'created'),
                state.sets, state.prototypes, state.receiver);
            const symbol = Symbol('created');
            owner[symbol] = 13;
            console.log(owner[symbol], Object.hasOwn(owner, symbol), Object.hasOwn(target, symbol),
                state.sets, state.gets, state.prototypes, state.receiver);
            """, "true 0 0 0\n7 1 true\n11 true false 1 0 true\n13 true false 2 1 0 true\n"];

        yield return ["async-locals-and-evaluated-references", """
            async function run() {
                class Owner { static marker = 7; }
                const owner: any = Owner, initial = Object.getPrototypeOf(owner), state = { order: '' };
                const custom: any = { get value() { state.order += 'g'; return (this as any).marker; } };
                Object.setPrototypeOf(owner, custom);
                async function receiver(): Promise<any> { state.order += 'r'; await Promise.resolve(); return owner; }
                async function key(): Promise<any> { state.order += 'k'; await Promise.resolve(); return 'value'; }
                const value = (await receiver())[await key()];
                console.log(value, state.order, owner.marker);
                Object.setPrototypeOf(owner, null);
                await Promise.resolve();
                console.log(owner.value === undefined, owner.marker, Object.getPrototypeOf(owner) === null);
                Object.setPrototypeOf(owner, initial);
                const instance = new owner();
                console.log(Object.getPrototypeOf(owner) === initial, instance instanceof owner);
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "7 rkg 7\ntrue 7 true\ntrue true\n"];

        yield return ["selected-accessor-abrupt-and-getterless-masks", """
            const key = Symbol('masked'), throwing = Symbol('throw'), marker = {}, state = { old: 0, selected: 0 };
            class Parent { static get masked() { state.old++; return 1; } }
            Object.defineProperty(Parent, key, { get() { state.old++; return 2; }, configurable: true });
            class Owner extends Parent {}
            const owner: any = Owner, custom: any = {};
            Object.defineProperty(custom, 'masked', { get: undefined, configurable: true });
            Object.defineProperty(custom, key, { get: undefined, configurable: true });
            Object.defineProperty(custom, 'throwing', { get() { state.selected++; throw marker; }, configurable: true });
            Object.defineProperty(custom, throwing, { get() { state.selected++; throw marker; }, configurable: true });
            Object.setPrototypeOf(owner, custom);
            console.log(owner.masked === undefined, owner[key] === undefined, state.old);
            try { owner.throwing; } catch (error) { console.log(error === marker, state.selected); }
            try { Reflect.get(owner, throwing); } catch (error) { console.log(error === marker, state.selected); }
            console.log(state.old);
            """, "true true 0\ntrue 1\ntrue 2\n0\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];

            // #2563: the derived null-prototype Reflect.get control remains unsupported.
            if ((string)row[0] == "derived-descendant-live-constructor-chain")
                continue;

            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorPrototypesFollowTheirSelectedLiveChain(
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
