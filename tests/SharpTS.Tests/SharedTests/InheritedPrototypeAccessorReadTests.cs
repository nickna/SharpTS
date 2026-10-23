using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InheritedPrototypeAccessorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inherited-ancestor-descriptor-issue", """
            let gets = 0, sets = 0, stored = 7;
            let current: any;
            class Parent {}
            Object.defineProperty(Parent.prototype, "value", {
                get() { gets++; return stored; },
                set(value: number) { sets++; stored = value; },
                configurable: true
            });
            const Child = class extends Parent {};
            current = new Child();
            console.log(current.value, gets, Object.hasOwn(current, "value"));
            current.value = 9;
            console.log(current.value, gets, sets, Object.hasOwn(current, "value"));
            """, "7 1 false\n9 2 1 false\n"];
        yield return ["declared-accessors-across-declaration-and-expression-parents", """
            let gets = 0, sets = 0;
            class DeclaredParent {
                stored = 7;
                get value() { gets++; return this.stored; }
                set value(value: number) { sets++; this.stored = value; }
            }
            const ExpressionChild = class extends DeclaredParent {};
            const first: any = new ExpressionChild();
            console.log(first.value, gets, Object.hasOwn(first, 'value'));
            first.value = 9;
            console.log(first.value, gets, sets, first.stored);
            const ExpressionParent = class {
                stored = 11;
                get value() { gets++; return this.stored; }
                set value(value: number) { sets++; this.stored = value; }
            };
            class DeclaredChild extends ExpressionParent {}
            const second: any = new DeclaredChild();
            console.log(second.value, gets, Object.hasOwn(second, 'value'));
            second.value = 13;
            console.log(second.value, gets, sets, second.stored);
            """, "7 1 false\n9 2 1 9\n11 3 false\n13 4 2 13\n"];
        yield return ["own-and-nearer-prototype-descriptors-mask-ancestor-accessors", """
            let ancestorGets = 0, childGets = 0, setterCalls = 0;
            class Parent {}
            Object.defineProperty(Parent.prototype, 'value', { get() { ancestorGets++; return 7; }, configurable: true });
            const OwnChild = class extends Parent {};
            const own: any = new OwnChild();
            Object.defineProperty(own, 'value', { value: 31, configurable: true });
            console.log('own', own.value, ancestorGets, Object.hasOwn(own, 'value'));
            const DataChild = class extends Parent {};
            Object.defineProperty(DataChild.prototype, 'value', { value: 41, configurable: true });
            const data: any = new DataChild();
            console.log('data', data.value, ancestorGets, Object.hasOwn(data, 'value'));
            const AccessorChild = class extends Parent {};
            Object.defineProperty(AccessorChild.prototype, 'value', { get() { childGets++; return 43; }, configurable: true });
            const accessor: any = new AccessorChild();
            console.log('accessor', accessor.value, ancestorGets, childGets, Object.hasOwn(accessor, 'value'));
            const GetterlessChild = class extends Parent {};
            Object.defineProperty(GetterlessChild.prototype, 'value', { set(value: number) { setterCalls++; }, configurable: true });
            const getterless: any = new GetterlessChild();
            console.log('getterless', getterless.value === undefined, ancestorGets, childGets, setterCalls);
            """, "own 31 0 true\ndata 41 0 false\naccessor 43 0 1 false\ngetterless true 0 1 0\n"];
        yield return ["inherited-getter-receiver-value-identity-and-abrupt-completion", """
            const marker = {}, token = {};
            const state = { gets: 0, throws: 0, receiver: true };
            let expected: any;
            class Parent { slot: any; }
            Object.defineProperty(Parent.prototype, 'value', {
                get() { state.gets++; state.receiver = state.receiver && this === expected; return this.slot; }, configurable: true
            });
            Object.defineProperty(Parent.prototype, 'boom', {
                get() { state.throws++; state.receiver = state.receiver && this === expected; throw token; }, configurable: true
            });
            const Child = class extends Parent {};
            const first: any = new Child(), second: any = new Child();
            first.slot = marker; expected = first;
            console.log(first.value === marker, state.gets, state.receiver, Object.hasOwn(first, 'value'));
            expected = second;
            console.log(second.value === undefined, state.gets, state.receiver, Object.hasOwn(second, 'value'));
            try { second.boom; } catch (error) { console.log(error === token, state.throws, state.receiver); }
            expected = first;
            console.log(first.value === marker, state.gets, state.throws, state.receiver);
            """, "true 1 true false\ntrue 2 true false\ntrue 1 true\ntrue 3 1 true\n"];
        yield return ["mixed-multilevel-hierarchies-select-the-nearest-live-descriptor", """
            let rootGets = 0, middleGets = 0;
            class Root { tag = 7; }
            Object.defineProperty(Root.prototype, 'value', { get() { rootGets++; return this.tag; }, configurable: true });
            const Middle = class extends Root {};
            class Leaf extends Middle {}
            const End = class extends Leaf {};
            const first: any = new End();
            console.log(first.value, rootGets, middleGets, Object.hasOwn(first, 'value'));
            Object.defineProperty(Middle.prototype, 'value', { get() { middleGets++; return this.tag + 10; }, configurable: true });
            console.log(first.value, rootGets, middleGets, Object.hasOwn(first, 'value'));
            const OtherRoot = class { tag = 19; };
            Object.defineProperty(OtherRoot.prototype, 'value', { get() { rootGets++; return this.tag; }, configurable: true });
            class OtherMiddle extends OtherRoot {}
            const OtherLeaf = class extends OtherMiddle {};
            class OtherEnd extends OtherLeaf {}
            const second: any = new OtherEnd();
            console.log(second.value, rootGets, middleGets, Object.hasOwn(second, 'value'));
            """, "7 1 0 false\n17 1 1 false\n19 2 1 false\n"];
        yield return ["inherited-getter-receiver-value-identity-and-abrupt-with-explicit-undefined-field", """
            const marker = {}, token = {};
            const state = { gets: 0, throws: 0, receiver: true };
            let expected: any;
            class Parent { slot: any = undefined; }
            Object.defineProperty(Parent.prototype, 'value', {
                get() { state.gets++; state.receiver = state.receiver && this === expected; return this.slot; }, configurable: true
            });
            Object.defineProperty(Parent.prototype, 'boom', {
                get() { state.throws++; state.receiver = state.receiver && this === expected; throw token; }, configurable: true
            });
            const Child = class extends Parent {};
            const first: any = new Child(), second: any = new Child();
            first.slot = marker; expected = first;
            console.log(first.value === marker, state.gets, state.receiver, Object.hasOwn(first, 'value'));
            expected = second;
            console.log(second.value === undefined, state.gets, state.receiver, Object.hasOwn(second, 'value'));
            try { second.boom; } catch (error) { console.log(error === token, state.throws, state.receiver); }
            expected = first;
            console.log(first.value === marker, state.gets, state.throws, state.receiver);
            """, "true 1 true false\ntrue 2 true false\ntrue 1 true\ntrue 3 1 true\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    // #2599: an uninitialized named instance field holds null in the interpreter.
    // Retain the full original source compiled/saved; its explicit-undefined companion runs both modes.
    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        ((string)row[0] == "inherited-getter-receiver-value-identity-and-abrupt-completion"
            ? new[] { ExecutionMode.Compiled }
            : new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void InheritedPrototypeAccessorsRetainValuesAndOriginalReceivers(
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
