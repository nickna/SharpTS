using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NonExtensibleClassConstructorAssignmentTests
{
    public const string IssueSource = """
        class C {}
        const owner: any = C;
        function strictWrite(target: any) { 'use strict'; target.value = 3; }
        Object.preventExtensions(owner);
        console.log(Object.isExtensible(owner), Object.hasOwn(owner, 'value'));
        try { strictWrite(owner); } catch (e) { console.log(e instanceof TypeError); }
        console.log(Object.hasOwn(owner, 'value'), owner.value === undefined,
            Object.getOwnPropertyDescriptor(owner, 'value') === undefined);
        """;

    public const string SloppySource = """
        class C {}
        const owner: any = C;
        function strictWrite(target: any) { target.value = 3; }
        Object.preventExtensions(owner);
        console.log(Object.isExtensible(owner), Object.hasOwn(owner, 'value'));
        try { strictWrite(owner); } catch (e) { console.log(e instanceof TypeError); }
        console.log(Object.hasOwn(owner, 'value'), owner.value === undefined,
            Object.getOwnPropertyDescriptor(owner, 'value') === undefined);
        """;

    public const string IndexedOrderSource = """
        class Declared {}
        const Expression = class {};
        class Generic<T> {}
        const effects = { order: "" };
        let owner: any;
        function receiver(): any { effects.order += "r"; return owner; }
        function key() { effects.order += "k"; return "fresh"; }
        function rhs() { effects.order += "v"; return 7; }
        function strictWrite() { "use strict"; return receiver()[key()] = rhs(); }
        for (const target of [Declared, Expression, Generic] as any[]) {
            owner = target;
            Object.preventExtensions(owner);
            effects.order = "";
            const result = receiver()[key()] = rhs();
            console.log(result, effects.order, Object.hasOwn(owner, "fresh"));
            effects.order = "";
            try { strictWrite(); console.log(false); }
            catch (error) { console.log(error instanceof TypeError, effects.order); }
            console.log(Object.hasOwn(owner, "fresh"), owner.fresh === undefined, Object.getOwnPropertyDescriptor(owner, "fresh") === undefined);
        }
        """;

    public const string WritableMembersSource = """
        class C { static field = 1; static method() { return 2; } static ["computed"]() { return 3; } }
        const Expression = class { static field = 1; static method() { return 2; } static ["computed"]() { return 3; } };
        function strictWrite(target: any) {
            "use strict";
            target.field = 9; target["extra"] = 10;
            target.method = function() { return 11; };
            target["computed"] = function() { return 12; };
        }
        for (const owner of [C, Expression] as any[]) {
            owner.extra = 4;
            const original = owner.method, computed = owner.computed;
            Object.preventExtensions(owner);
            owner.field = 5; owner.extra = 6;
            owner.method = function() { return 7; };
            owner["computed"] = function() { return 8; };
            console.log(owner.field, owner.extra, owner.method(), owner.computed(), owner.method !== original,
                owner.computed !== computed, Object.isExtensible(owner));
            strictWrite(owner);
            console.log(owner.field, owner.extra, owner.method(), owner.computed(), Object.hasOwn(owner, "method"), Object.hasOwn(owner, "computed"));
        }
        """;

    public const string SetterSource = """
        const state: any = { calls: 0, ownCalls: 0, same: true, expected: null };
        const marker = {};
        class Base {
            static set value(next: number) {
                state.calls++; state.same = state.same && this === state.expected;
                (this as any).stored = next;
                if (next === 99) throw marker;
            }
        }
        class C extends Base { static stored = 1; }
        const owner: any = C;
        Object.defineProperty(owner, "own", {
            set: function(this: any, next: number) { state.ownCalls++; state.same = state.same && this === owner; this.stored = next; },
            configurable: true
        });
        state.expected = owner;
        Object.preventExtensions(owner);
        function strictDot(target: any, value: number) { "use strict"; return target.value = value; }
        function strictIndex(target: any, key: string, value: number) { "use strict"; return target[key] = value; }
        console.log(strictDot(owner, 7), strictIndex(owner, "own", 8), owner.stored, state.calls, state.ownCalls, state.same);
        try { strictIndex(owner, "value", 99); console.log(false); }
        catch (error) { console.log(error === marker); }
        console.log(owner.stored, state.calls, state.same, Object.hasOwn(owner, "value"));
        """;

    public const string InheritedDataSource = """
        class Base { static value = 1; }
        class C extends Base {}
        const owner: any = C;
        Object.preventExtensions(owner);
        function strictWrite(target: any) { "use strict"; target.value = 3; }
        try { strictWrite(owner); } catch (error) { console.log(error instanceof TypeError); }
        console.log(Object.hasOwn(owner, "value"), owner.value, Base.value);
        """;

    public const string InitializationSource = """
        const effects: string[] = [];
        let owner: any;
        try {
            class Declared {
                static { owner = this; Object.preventExtensions(this); }
                static value = (effects.push("rhs"), 1);
                static later = (effects.push("later"), 2);
                static { effects.push("block"); }
            }
            console.log("no error");
        } catch (error) { console.log(error instanceof TypeError); }
        console.log(effects.join(","), Object.hasOwn(owner, "value"), Object.hasOwn(owner, "later"));
        effects.length = 0;
        try {
            const Expression = class {
                static { owner = this; Object.preventExtensions(this); }
                static value = (effects.push("rhs"), 1);
                static later = (effects.push("later"), 2);
                static { effects.push("block"); }
            };
            console.log("no error");
        } catch (error) { console.log(error instanceof TypeError); }
        console.log(effects.join(","), Object.hasOwn(owner, "value"), Object.hasOwn(owner, "later"));
        """;

    public const string SymbolAssignmentSource = """
        const looseKey = Symbol('loose'), strictKey = Symbol('strict');
        class FreshConstructor {}
        const freshOwner: any = FreshConstructor;
        const freshEffects = { order: '', calls: 0 };
        function freshReceiver(): any { freshEffects.order += 'r'; return freshOwner; }
        function freshKey(selected: symbol): symbol { freshEffects.order += 'k'; return selected; }
        function freshRhs(): number { freshEffects.order += 'v'; freshEffects.calls++; return 7; }
        function freshSloppyWrite(selected: symbol) { return freshReceiver()[freshKey(selected)] = freshRhs(); }
        function freshStrictWrite(selected: symbol) { 'use strict'; return freshReceiver()[freshKey(selected)] = freshRhs(); }
        Object.preventExtensions(freshOwner);
        console.log(freshSloppyWrite(looseKey), freshEffects.order, Object.hasOwn(freshOwner, looseKey), freshOwner[looseKey] === undefined);
        freshEffects.order = '';
        try { freshStrictWrite(strictKey); console.log(false); }
        catch (error) { console.log(error instanceof TypeError, freshEffects.order); }
        console.log(freshEffects.calls, Object.hasOwn(freshOwner, strictKey), freshOwner[strictKey] === undefined, Object.isExtensible(freshOwner));
        const fieldKey = Symbol('field');
        const methodKey = Symbol('method');
        const expandoKey = Symbol('expando');
        class OwnSlots { static [fieldKey] = 1; static [methodKey]() { return 2; } }
        const ownOwner: any = OwnSlots;
        ownOwner[expandoKey] = 3;
        function ownSloppyWrite(target: any, key: symbol, value: any) { return target[key] = value; }
        function ownStrictWrite(target: any, key: symbol, value: any) { 'use strict'; return target[key] = value; }
        Object.preventExtensions(ownOwner);
        ownSloppyWrite(ownOwner, fieldKey, 4);
        ownSloppyWrite(ownOwner, methodKey, () => 5);
        ownSloppyWrite(ownOwner, expandoKey, 6);
        console.log(ownOwner[fieldKey], ownOwner[methodKey](), ownOwner[expandoKey],
            Object.hasOwn(ownOwner, fieldKey), Object.hasOwn(ownOwner, methodKey), Object.hasOwn(ownOwner, expandoKey));
        ownStrictWrite(ownOwner, fieldKey, 7);
        ownStrictWrite(ownOwner, methodKey, () => 8);
        ownStrictWrite(ownOwner, expandoKey, 9);
        console.log(ownOwner[fieldKey], ownOwner[methodKey](), ownOwner[expandoKey],
            Object.hasOwn(ownOwner, fieldKey), Object.hasOwn(ownOwner, methodKey), Object.hasOwn(ownOwner, expandoKey));
        const dataKey = Symbol('data');
        class SymbolBase {}
        const base: any = SymbolBase;
        base[dataKey] = 1;
        class SymbolChild extends SymbolBase {}
        const child: any = SymbolChild;
        function inheritedSloppyWrite(target: any, key: symbol, value: number) { return target[key] = value; }
        function inheritedStrictWrite(target: any, key: symbol, value: number) { 'use strict'; return target[key] = value; }
        Object.preventExtensions(child);
        console.log(inheritedSloppyWrite(child, dataKey, 4), Object.hasOwn(child, dataKey), base[dataKey]);
        try { inheritedStrictWrite(child, dataKey, 5); console.log(false); }
        catch (error) { console.log(error instanceof TypeError); }
        console.log(child[dataKey], base[dataKey], Object.hasOwn(child, dataKey));
        """;

    public const string SymbolSetterSource = """
        const setterKey = Symbol('setter'), state = { calls: 0, same: true }, marker = {};
        class Base { static stored = 0; }
        class Child extends Base { static stored = 0; }
        const child: any = Child;
        Object.defineProperty(Base, setterKey, {
            set: function(this: any, value: number) {
                state.calls++; state.same = state.same && this === child; this.stored = value;
                if (value === 99) throw marker;
            }, configurable: true
        });
        function sloppyWrite(target: any, key: symbol, value: number) { return target[key] = value; }
        function strictWrite(target: any, key: symbol, value: number) { 'use strict'; return target[key] = value; }
        Object.preventExtensions(child);
        console.log(sloppyWrite(child, setterKey, 3), strictWrite(child, setterKey, 7),
            state.calls, state.same, child.stored, Object.hasOwn(child, setterKey));
        try { strictWrite(child, setterKey, 99); console.log(false); }
        catch (error) { console.log(error === marker); }
        console.log(state.calls, child.stored, state.same, Object.hasOwn(child, setterKey));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", IssueSource, "false false\ntrue\nfalse true true\n"];
        yield return ["sloppy", SloppySource, "false false\nfalse true true\n"];
        yield return ["sloppy-strict-rhs-and-generic-owner", IndexedOrderSource, "7 rkv false\ntrue rkv\nfalse true true\n7 rkv false\ntrue rkv\nfalse true true\n7 rkv false\ntrue rkv\nfalse true true\n"];
        yield return ["existing-field-expando-ordinary-computed-method", WritableMembersSource, "5 6 7 8 true true false\n9 10 11 12 true true\n5 6 7 8 true true false\n9 10 11 12 true true\n"];
        yield return ["inherited-and-own-setter-receiver-abrupt", SetterSource, "7 8 8 1 1 true\ntrue\n99 2 true false\n"];
        yield return ["inherited-data", InheritedDataSource, "true\nfalse 1 1\n"];
        yield return ["symbol-creation-and-existing-slots", SymbolAssignmentSource, "7 rkv false true\ntrue rkv\n2 false true false\n4 5 6 true true true\n7 8 9 true true true\n4 false 1\ntrue\n1 1 false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NonExtensibleConstructorsRejectNewSlotsAndPreserveExistingWrites(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public void InterpretedStaticFieldDefinitionThrowsAfterItsInitializerAndStopsLaterEffects()
    {
        // #2445 retains the separate compiled static-field definition failure.
        Assert.Equal("true\nrhs false false\ntrue\nrhs false false\n",
            TestHarness.RunInterpreted(InitializationSource));
    }

    [Fact]
    public void InterpretedInheritedSymbolSetterRetainsReceiverAndAbruptCompletion()
    {
        // #2447 retains the separate compiled strict inherited Symbol setter failure.
        Assert.Equal("3 7 2 true 7 false\ntrue\n3 99 true false\n",
            TestHarness.RunInterpreted(SymbolSetterSource));
    }
}
