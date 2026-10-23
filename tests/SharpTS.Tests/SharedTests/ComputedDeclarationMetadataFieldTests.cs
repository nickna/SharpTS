using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ComputedDeclarationMetadataFieldTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["field-descriptors-own-key-order-and-partial-redefinition", """
            const nameKey = 'name', lengthKey = 'length', events: string[] = [];
            class Fields {
                static first = (events.push('first'), 1);
                static [nameKey] = (events.push('name'), 'field-name');
                static [lengthKey] = (events.push('length'), 7);
                static last = (events.push('last'), 2);
            }
            const owner: any = Fields;
            const nameDescriptor = Object.getOwnPropertyDescriptor(owner, 'name')!;
            const lengthDescriptor = Object.getOwnPropertyDescriptor(owner, 'length')!;
            console.log(events.join(','), Object.keys(owner).join(','));
            console.log(Object.getOwnPropertyNames(owner).join(','));
            console.log(owner.name, Reflect.get(owner, 'name'), nameDescriptor.value, nameDescriptor.writable, nameDescriptor.enumerable, nameDescriptor.configurable);
            console.log(owner.length, Reflect.get(owner, 'length'), lengthDescriptor.value, lengthDescriptor.writable, lengthDescriptor.enumerable, lengthDescriptor.configurable);
            Object.defineProperty(owner, 'name', { value: 'redefined' });
            Object.defineProperty(owner, 'length', { writable: false });
            const renamed = Object.getOwnPropertyDescriptor(owner, 'name')!;
            const relocked = Object.getOwnPropertyDescriptor(owner, 'length')!;
            console.log(owner.name, renamed.value, renamed.writable, renamed.enumerable, renamed.configurable);
            console.log(owner.length, relocked.value, relocked.writable, relocked.enumerable, relocked.configurable);
            console.log(Object.keys(owner).join(','), Object.hasOwn(owner, 'name'), Object.hasOwn(owner, 'length'));
            """, "first,name,length,last length,name,first,last\nlength,name,prototype,first,last\nfield-name field-name field-name true true true\n7 7 7 true true true\nredefined redefined true true true\n7 7 false true true\nlength,name,first,last true true\n"];
        yield return ["generic-and-derived-declarations-retain-own-metadata-fields", """
            const nameKey = 'name', lengthKey = 'length';
            class Generic<T> {
                static [nameKey] = 'generic';
                static [lengthKey] = 3;
                static regular = 5;
                value: T;
                constructor(value: T) { this.value = value; }
            }
            class Derived extends Generic<number> {
                static [nameKey] = 'derived';
                static [lengthKey] = 8;
            }
            function report(target: any) {
                const name = Object.getOwnPropertyDescriptor(target, 'name')!;
                const length = Object.getOwnPropertyDescriptor(target, 'length')!;
                console.log(target.name, Reflect.get(target, 'name'), name.value, name.writable, name.enumerable, name.configurable);
                console.log(target.length, Reflect.get(target, 'length'), length.value, length.writable, length.enumerable, length.configurable);
                console.log(Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
            }
            report(Generic);
            report(Derived);
            console.log(new Generic<number>(7).value, new Generic<string>('ok').value, new Derived(11).value);
            console.log(Generic.regular, Derived.regular, Object.hasOwn(Generic, 'regular'), Object.hasOwn(Derived, 'regular'));
            """, "generic generic generic true true true\n3 3 3 true true true\ntrue true\nderived derived derived true true true\n8 8 8 true true true\ntrue true\n7 ok 11\n5 5 true false\n"];
        yield return ["computed-definitions-bypass-inherited-setter-readonly-and-keep-symbol", """
            const slotKey = 'slot', blockedKey = 'blocked', symbolKey: unique symbol = Symbol('data');
            const events: string[] = [];
            let setterCalls = 0;
            class Parent {}
            Object.defineProperty(Parent, slotKey, { set(value: any) { setterCalls++; }, configurable: true });
            Object.defineProperty(Parent, blockedKey, { value: 1, writable: false, configurable: true });
            class Child extends Parent {
                static [slotKey] = (events.push('slot'), 7);
                static [blockedKey] = (events.push('blocked'), 9);
                static [symbolKey] = (events.push('symbol'), 11);
            }
            const child: any = Child;
            const slot = Object.getOwnPropertyDescriptor(child, slotKey)!;
            const blocked = Object.getOwnPropertyDescriptor(child, blockedKey)!;
            const symbol = Object.getOwnPropertyDescriptor(child, symbolKey)!;
            console.log(events.join(','), setterCalls, child.slot, child.blocked, child[symbolKey]);
            console.log(slot.value, slot.writable, slot.enumerable, slot.configurable,
                blocked.value, blocked.writable, blocked.enumerable, blocked.configurable);
            console.log(symbol.value, symbol.writable, symbol.enumerable, symbol.configurable,
                Object.getOwnPropertySymbols(child)[0] === symbolKey);
            console.log(Object.getOwnPropertyDescriptor(Parent, blockedKey)!.value,
                typeof Object.getOwnPropertyDescriptor(Parent, slotKey)!.set,
                Object.hasOwn(child, slotKey), Object.hasOwn(child, blockedKey));
            """, "slot,blocked,symbol 0 7 9 11\n7 true true true 9 true true true\n11 true true true true\n1 function true true\n"];
        yield return ["field-descriptors-and-partial-redefinition-without-own-key-inventory", """
            const nameKey = 'name', lengthKey = 'length', events: string[] = [];
            class Fields {
                static first = (events.push('first'), 1);
                static [nameKey] = (events.push('name'), 'field-name');
                static [lengthKey] = (events.push('length'), 7);
                static last = (events.push('last'), 2);
            }
            const owner: any = Fields;
            const nameDescriptor = Object.getOwnPropertyDescriptor(owner, 'name')!;
            const lengthDescriptor = Object.getOwnPropertyDescriptor(owner, 'length')!;
            console.log(events.join(','));
            console.log(owner.name, Reflect.get(owner, 'name'), nameDescriptor.value, nameDescriptor.writable, nameDescriptor.enumerable, nameDescriptor.configurable);
            console.log(owner.length, Reflect.get(owner, 'length'), lengthDescriptor.value, lengthDescriptor.writable, lengthDescriptor.enumerable, lengthDescriptor.configurable);
            Object.defineProperty(owner, 'name', { value: 'redefined' });
            Object.defineProperty(owner, 'length', { writable: false });
            const renamed = Object.getOwnPropertyDescriptor(owner, 'name')!;
            const relocked = Object.getOwnPropertyDescriptor(owner, 'length')!;
            console.log(owner.name, renamed.value, renamed.writable, renamed.enumerable, renamed.configurable);
            console.log(owner.length, relocked.value, relocked.writable, relocked.enumerable, relocked.configurable);
            console.log(Object.hasOwn(owner, 'name'), Object.hasOwn(owner, 'length'));
            """, "first,name,length,last\nfield-name field-name field-name true true true\n7 7 7 true true true\nredefined redefined true true true\n7 7 false true true\ntrue true\n"];
        yield return ["generic-and-derived-metadata-without-inherited-ordinary-static-read", """
            const nameKey = 'name', lengthKey = 'length';
            class Generic<T> {
                static [nameKey] = 'generic';
                static [lengthKey] = 3;
                static regular = 5;
                value: T;
                constructor(value: T) { this.value = value; }
            }
            class Derived extends Generic<number> {
                static [nameKey] = 'derived';
                static [lengthKey] = 8;
            }
            function report(target: any) {
                const name = Object.getOwnPropertyDescriptor(target, 'name')!;
                const length = Object.getOwnPropertyDescriptor(target, 'length')!;
                console.log(target.name, Reflect.get(target, 'name'), name.value, name.writable, name.enumerable, name.configurable);
                console.log(target.length, Reflect.get(target, 'length'), length.value, length.writable, length.enumerable, length.configurable);
                console.log(Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
            }
            report(Generic);
            report(Derived);
            console.log(new Generic<number>(7).value, new Generic<string>('ok').value, new Derived(11).value);
            console.log(Generic.regular, Object.hasOwn(Generic, 'regular'), Object.hasOwn(Derived, 'regular'));
            """, "generic generic generic true true true\n3 3 3 true true true\ntrue true\nderived derived derived true true true\n8 8 8 true true true\ntrue true\n7 ok 11\n5 true false\n"];
    }

    private static IEnumerable<object[]> CompiledCases() => Cases();

    public static IEnumerable<object[]> SavedCases() => CompiledCases().Concat(
        ClassNameReflectionTests.Cases().Where(row => (string)row[0] is
            "computed-declaration-metadata-fields" or "computed-expression-fields-and-method-accessor-metadata"));

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
        foreach (var row in CompiledCases())
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedStaticFieldsDefineOwnDataProperties(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (name is "field-descriptors-own-key-order-and-partial-redefinition"
            or "generic-and-derived-declarations-retain-own-metadata-fields")
        {
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
