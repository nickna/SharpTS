using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassDeclarationDeclareFieldErasureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["class-declaration-declare-field-erasure-issue", """
            class Model {
                declare id: number;
                declare readonly label: string;
                declare private hidden: number;
                actual: number = 11;
                readHidden(): any { return this.hidden; }
            }
            const m: any = new Model();
            console.log(Object.hasOwn(m, "id"), Object.hasOwn(m, "label"), Object.hasOwn(m, "hidden"), Object.hasOwn(m, "actual"));
            console.log(m.id === undefined, m.label === undefined, m.readHidden() === undefined, m.actual);
            m.id = 123;
            console.log(Object.hasOwn(m, "id"), m.id, Object.keys(m).join(","));

            const Expression = class {
                declare id: number;
                actual: number = 11;
            };
            const expression: any = new Expression();
            console.log(Object.hasOwn(expression, "id"), expression.id === undefined, expression.actual);
            """, "false false false true\ntrue true true 11\ntrue 123 actual,id\nfalse true 11\n"];
        yield return ["class-declaration-static-declare-field-erasure-issue", """
            class Model {
                declare static version: string;
                static actual: number = 12;
            }
            console.log(Object.hasOwn(Model, "version"), Model.version === undefined, Model.actual);
            console.log(Object.getOwnPropertyDescriptor(Model, "version") === undefined);
            """, "false true 12\ntrue\n"];
        yield return ["later-typed-and-dynamic-writes-create-properties-with-live-descriptors", """
            class Model {
                declare id: number;
                declare static version: string;
                actual = 3;
            }
            const value = new Model();
            value.id = 17;
            Model.version = 'first';
            const instance: any = value, constructor: any = Model;
            for (const target of [instance, constructor]) {
                const key = target === instance ? 'id' : 'version';
                const descriptor = Object.getOwnPropertyDescriptor(target, key)!;
                console.log(target[key], descriptor.writable, descriptor.enumerable, descriptor.configurable);
                Object.defineProperty(target, key, { value: 19, writable: true, enumerable: true, configurable: true });
                console.log(delete target[key], Object.hasOwn(target, key), Object.getOwnPropertyDescriptor(target, key) === undefined);
                Object.defineProperty(target, key, { value: 23, writable: true, enumerable: true, configurable: true });
                const recreated = Object.getOwnPropertyDescriptor(target, key)!;
                console.log(target[key], recreated.writable, recreated.enumerable, recreated.configurable);
            }
            console.log(instance.actual, Object.keys(instance).join(','));
            """, "17 true true true\ntrue false true\n23 true true true\nfirst true true true\ntrue false true\n23 true true true\n3 actual,id\n"];
        yield return ["real-fields-retain-presence-and-initialization-around-erased-declarations", """
            const events: string[] = [];
            class Model {
                declare erased: number;
                ordinary: any;
                first = (events.push('first'), 1);
                declare readonly label: string;
                last = (events.push('last'), 2);
                declare static version: string;
                static ordinary: any;
                static actual = 5;
            }
            const first = new Model(), second = new Model();
            console.log(Object.hasOwn(first, 'erased'), Object.hasOwn(first, 'ordinary'), Object.hasOwn(first, 'first'), Object.hasOwn(first, 'label'), Object.hasOwn(first, 'last'));
            console.log(Object.hasOwn(Model, 'version'), Object.hasOwn(Model, 'ordinary'), Object.hasOwn(Model, 'actual'), Model.actual);
            const descriptor = Object.getOwnPropertyDescriptor(first, 'ordinary')!;
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable, events.join(','));
            first.ordinary = 17;
            console.log(first.ordinary, Object.hasOwn(second, 'ordinary'), Object.keys(first).join(','));
            """, "false true true false true\nfalse true true 5\ntrue true true first,last,first,last\n17 true ordinary,first,last\n"];
        yield return ["generic-and-native-array-declarations-preserve-real-state-and-expression-controls", """
            class Model<T> {
                declare id: T;
                actual: T;
                constructor(value: T) { this.actual = value; }
            }
            const number = new Model<number>(7), text = new Model<string>('x');
            console.log(Object.hasOwn(number, 'id'), Object.hasOwn(text, 'id'), number.actual, text.actual, Object.keys(number).join(','));
            class Values extends Array<number> {
                declare tag: string;
                actual = 5;
                constructor() { super(1, 2); }
            }
            const values = new Values();
            console.log(Array.isArray(values), values.length, values[0], values[1], values.actual, Object.hasOwn(values, 'tag'));
            const Expression = class { declare id: number; actual = 13; };
            const expression = new Expression();
            console.log(Object.hasOwn(expression, 'id'), expression.id === undefined, expression.actual, Object.keys(expression).join(','));
            """, "false false 7 x actual\ntrue 2 1 2 5 false\nfalse true 13 actual\n"];
        yield return ["later-typed-and-dynamic-writes-retain-static-deletion-and-descriptors", """
            class Model {
                declare id: number;
                declare static version: string;
                actual = 3;
            }
            const value = new Model();
            value.id = 17;
            Model.version = 'first';
            const instance: any = value, constructor: any = Model;
            for (const target of [instance, constructor]) {
                const key = target === instance ? 'id' : 'version';
                const descriptor = Object.getOwnPropertyDescriptor(target, key)!;
                console.log(target[key], descriptor.writable, descriptor.enumerable, descriptor.configurable);
                Object.defineProperty(target, key, { value: 19, writable: true, enumerable: true, configurable: true });
                if (target === constructor) console.log(delete target[key], Object.hasOwn(target, key), Object.getOwnPropertyDescriptor(target, key) === undefined);
                Object.defineProperty(target, key, { value: 23, writable: true, enumerable: true, configurable: true });
                const recreated = Object.getOwnPropertyDescriptor(target, key)!;
                console.log(target[key], recreated.writable, recreated.enumerable, recreated.configurable);
            }
            console.log(instance.actual, Object.keys(instance).join(','));
            """, "17 true true true\n23 true true true\nfirst true true true\ntrue false true\n23 true true true\n3 actual,id\n"];
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
    public void DeclaredFieldsAreErasedWhileRealFieldsAndLaterWritesRemainObservable(
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
