using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UninitializedStaticFieldTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["declaration-own-value", """
            class Owner { static value: undefined; }
            const owner: any = Owner, descriptor: any = Object.getOwnPropertyDescriptor(owner, 'value');
            console.log(owner.value === undefined, owner.value === null,
                Reflect.get(owner, 'value') === undefined, descriptor.value === undefined,
                Object.hasOwn(owner, 'value'), descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "true false true true true true true true\n"];
        yield return ["expression-own-value", """
            const Owner = class { static value: undefined; };
            const owner: any = Owner, descriptor: any = Object.getOwnPropertyDescriptor(owner, 'value');
            console.log(owner.value === undefined, owner.value === null,
                Reflect.get(owner, 'value') === undefined, descriptor.value === undefined,
                Object.hasOwn(owner, 'value'), descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "true false true true true true true true\n"];
        yield return ["generic-own-value", """
            class Owner<T> { static value: undefined; }
            const owner: any = Owner, descriptor: any = Object.getOwnPropertyDescriptor(owner, 'value');
            console.log(owner.value === undefined, owner.value === null,
                Reflect.get(owner, 'value') === undefined, descriptor.value === undefined,
                Object.hasOwn(owner, 'value'), descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "true false true true true true true true\n"];
        yield return ["declaration-explicit-null-undefined-and-computed-controls", """
            class Owner {
                static omitted: any;
                static explicitUndefined: any = undefined;
                static explicitNull: any = null;
                static ['computed']: any;
                static [Symbol.toStringTag]: any;
            }
            const owner: any = Owner;
            console.log(owner.omitted === undefined, owner.explicitUndefined === undefined,
                owner.explicitNull === null, owner.explicitNull === undefined,
                owner.computed === undefined, owner[Symbol.toStringTag] === undefined);
            console.log(Object.hasOwn(owner, 'computed'), Object.hasOwn(owner, Symbol.toStringTag));
            owner.omitted = 9;
            console.log(owner.omitted, Object.getOwnPropertyDescriptor(owner, 'omitted')!.value);
            """, "true true true false true true\ntrue true\n9 9\n"];
        yield return ["expression-explicit-null-undefined-and-computed-controls", """
            const Owner = class {
                static omitted: any;
                static explicitUndefined: any = undefined;
                static explicitNull: any = null;
                static ['computed']: any;
                static [Symbol.toStringTag]: any;
            };
            const owner: any = Owner;
            console.log(owner.omitted === undefined, owner.explicitUndefined === undefined,
                owner.explicitNull === null, owner.explicitNull === undefined,
                owner.computed === undefined, owner[Symbol.toStringTag] === undefined);
            console.log(Object.hasOwn(owner, 'computed'), Object.hasOwn(owner, Symbol.toStringTag));
            owner.omitted = 9;
            console.log(owner.omitted, Object.getOwnPropertyDescriptor(owner, 'omitted')!.value);
            """, "true true true false true true\ntrue true\n9 9\n"];
        yield return ["declaration-later-initializers-and-static-blocks", """
            class Owner {
                static omitted: any;
                static { console.log(this.omitted === undefined,
                    Object.getOwnPropertyDescriptor(this, 'omitted')!.value === undefined,
                    Object.hasOwn(this, 'omitted')); }
                static copy = this.omitted;
            }
            console.log(Owner.copy === undefined);
            """, "true true true\ntrue\n"];
        yield return ["expression-later-initializers-and-static-blocks", """
            const Owner = class {
                static omitted: any;
                static { console.log(this.omitted === undefined,
                    Object.getOwnPropertyDescriptor(this, 'omitted')!.value === undefined,
                    Object.hasOwn(this, 'omitted')); }
                static copy = this.omitted;
            };
            console.log(Owner.copy === undefined);
            """, "true true true\ntrue\n"];
        yield return ["declaration-inherited-field-is-shadowed", """
            class Parent { static value: any = 7; }
            class Owner extends Parent { static value: any; }
            const owner: any = Owner;
            console.log(owner.value === undefined, Reflect.get(owner, 'value') === undefined,
                Object.hasOwn(owner, 'value'), Object.getOwnPropertyDescriptor(owner, 'value')!.value === undefined,
                Parent.value);
            """, "true true true true 7\n"];
        yield return ["expression-inherited-field-is-shadowed", """
            class Parent { static value: any = 7; }
            const Owner = class extends Parent { static value: any; };
            const owner: any = Owner;
            console.log(owner.value === undefined, Reflect.get(owner, 'value') === undefined,
                Object.hasOwn(owner, 'value'), Object.getOwnPropertyDescriptor(owner, 'value')!.value === undefined,
                Parent.value);
            """, "true true true true 7\n"];
        yield return ["declaration-omitted-private-field-and-explicit-null", """
            class Owner {
                static #omitted: any;
                static #explicitNull: any = null;
                static read() { console.log(this.#omitted === undefined, this.#omitted === null,
                    this.#explicitNull === null); }
            }
            Owner.read();
            """, "true false true\n"];
        yield return ["expression-omitted-private-field-and-explicit-null", """
            const Owner = class {
                static #omitted: any;
                static #explicitNull: any = null;
                static read() { console.log(this.#omitted === undefined, this.#omitted === null,
                    this.#explicitNull === null); }
            };
            Owner.read();
            """, "true false true\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];

            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void OmittedStaticInitializersInstallUndefined(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
