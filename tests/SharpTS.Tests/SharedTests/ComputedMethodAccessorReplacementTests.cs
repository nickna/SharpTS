using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ComputedMethodAccessorReplacementTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["declarations", """
            const key: any = "value";
            let reads = 0;
            class Setter { set value(next: any) {} [key]() { return 5; } }
            class Getter { get value() { reads++; return 1; } [key]() { return 6; } }
            const first: any = new Setter(), second: any = new Getter();
            console.log(typeof first.value, first.value(), second.value(), reads);
            const descriptor: any = Object.getOwnPropertyDescriptor(Setter.prototype, "value");
            console.log(typeof descriptor.value, descriptor.get === undefined, descriptor.set === undefined,
                descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "function 5 6 0\nfunction true true true false true\n"];
        yield return ["expressions", """
            const key: any = "value";
            let reads = 0;
            const Setter = class { set value(next: any) {} [key]() { return 5; } };
            const Getter = class { get value() { reads++; return 1; } [key]() { return 6; } };
            const first: any = new Setter(), second: any = new Getter();
            console.log(typeof first.value, first.value(), second.value(), reads);
            const descriptor: any = Object.getOwnPropertyDescriptor(Getter.prototype, "value");
            console.log(typeof descriptor.value, descriptor.get === undefined, descriptor.set === undefined);
            """, "function 5 6 0\nfunction true true\n"];
        yield return ["statics", """
            const key: any = "value";
            let reads = 0;
            class Setter { static set value(next: any) {} static [key]() { return 5; } }
            const Getter = class { static get value() { reads++; return 1; } static [key]() { return 6; } };
            const first: any = Setter, second: any = Getter;
            console.log(typeof first.value, first.value(), second.value(), reads);
            const descriptor: any = Object.getOwnPropertyDescriptor(first, "value");
            console.log(typeof descriptor.value, descriptor.get === undefined, descriptor.set === undefined);
            """, "function 5 6 0\nfunction true true\n"];
        yield return ["later-accessors", """
            const key: any = "value";
            class Setter { [key]() { return 5; } set value(next: any) {} }
            class Getter { [key]() { return 5; } get value() { return 7; } }
            const Static = class { static [key]() { return 5; } static set value(next: any) {} };
            const first: any = new Setter(), second: any = new Getter(), third: any = Static;
            console.log(first.value === undefined, second.value, third.value === undefined);
            const descriptor: any = Object.getOwnPropertyDescriptor(Setter.prototype, "value");
            console.log(descriptor.value === undefined, descriptor.get === undefined, typeof descriptor.set);
            """, "true 7 true\ntrue true function\n"];
        yield return ["symbol-order", """
            const key = Symbol("value");
            class Method { set [key](next: any) {} [key]() { return 5; } }
            const Setter = class { [key]() { return 5; } set [key](next: any) {} };
            class Static { static get [key]() { return 1; } static [key]() { return 6; } }
            const StaticSetter = class { static [key]() { return 5; } static set [key](next: any) {} };
            const first: any = new Method(), second: any = new Setter(), third: any = Static, fourth: any = StaticSetter;
            console.log(first[key](), second[key] === undefined, third[key](), fourth[key] === undefined);
            const method: any = Object.getOwnPropertyDescriptor(Method.prototype, key);
            const accessor: any = Object.getOwnPropertyDescriptor(Setter.prototype, key);
            console.log(typeof method.value, method.set === undefined, accessor.value === undefined, typeof accessor.set);
            """, "5 true 6 true\nfunction true true function\n"];
        yield return ["literal-computed-accessors", """
            const key: any = "value", numeric: any = "1";
            class Getter { [key]() { return 5; } get ["value"]() { return 7; } }
            const Setter = class { [numeric]() { return 5; } set [1](next: any) {} };
            class Static { static [key]() { return 5; } static get ["value"]() { return 8; } }
            const StaticSetter = class { static [numeric]() { return 5; } static set [1](next: any) {} };
            class Method { get ["value"]() { return 1; } [key]() { return 6; } }
            const first: any = new Getter(), second: any = new Setter(), third: any = Static,
                fourth: any = StaticSetter, method: any = new Method();
            console.log(first.value, second[1] === undefined, third.value, fourth[1] === undefined, method.value());
            const getter: any = Object.getOwnPropertyDescriptor(Getter.prototype, "value");
            const setter: any = Object.getOwnPropertyDescriptor(Setter.prototype, "1");
            console.log(typeof getter.get, getter.value === undefined, typeof setter.set, setter.value === undefined);
            """, "7 true 8 true 6\nfunction true function true\n"];
        yield return ["normalized-literal-and-keyword-names", """
            const key: any = "value", numeric: any = "1", keyword: any = "get";
            class Quoted { [key]() { return 5; } get "value"() { return 7; } }
            const Numeric = class { [numeric]() { return 5; } get 1() { return 8; } };
            class Keyword { get [keyword]() { return 1; } get() { return 6; } }
            const quoted: any = new Quoted(), number: any = new Numeric(), word: any = new Keyword();
            console.log(quoted.value, number[1], word.get());
            """, "7 8 6\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LaterComputedMethodsReplaceAccessorDescriptors(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void AccessorPairsResetAfterAnInterveningMethod(ExecutionMode mode)
    {
        const string source = """
            const key: any = "value";
            class C { get value() { return 1; } [key]() { return 5; } set value(next: any) {} }
            const instance: any = new C();
            const descriptor: any = Object.getOwnPropertyDescriptor(C.prototype, "value");
            console.log(instance.value === undefined, descriptor.get === undefined, typeof descriptor.set);
            """;
        Assert.Equal("true true function\n", TestHarness.Run(source, mode));
    }
}
