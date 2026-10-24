using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AnyStaticPrivateReceiverTests
{
    public const string IssueSource = """
        class C {
            static #m = 42;
            static read(value: any) { return value.#m; }
        }
        console.log(C.read(C));
        """;

    public const string AnonymousSource = """
        const C = class {
            static #m = 42;
            static read(value: typeof C) { return value.#m; }
        };
        console.log(C.read(C));
        """;

    public const string TypedMembersSource = """
        const events = { receivers: 0 };
        function receiver(value: any) { events.receivers++; return value; }
        class C {
            static #value: number = 1;
            static #callable: (value: number) => number = (value: number) => value + 1;
            static #method(value: number): number { return value + 2; }
            static read(value: any): number { return receiver(value).#value; }
            static write(value: any, next: number) { receiver(value).#value = next; }
            static call(value: any): number {
                return receiver(value).#callable(3) + receiver(value).#method(4);
            }
        }
        const alias: any = C;
        console.log(C.read(alias));
        C.write(alias, 7);
        console.log(C.read(alias), C.call(alias), events.receivers);
        """;

    public const string BrandsSource = """
        class C {
            static #value: number = 1;
            static #callable: (value: number) => number = (value: number) => value + 1;
            static #method(value: number): number { return value + 2; }
            static read(value: any) { return value.#value; }
            static write(value: any) { value.#value = 9; }
            static callField(value: any) { return value.#callable(3); }
            static callMethod(value: any) { return value.#method(4); }
        }
        class Child extends C {}
        class Other {}
        const invalid: any[] = [Child, Other, new C(), {}, null, undefined,
            1, "value", false, Symbol("value"), 1n];
        let rejected = 0, returned = 0;
        for (const value of invalid) {
            try { C.read(value); returned++; }
            catch (error) { if (error instanceof TypeError) rejected++; }
            try { C.write(value); returned++; }
            catch (error) { if (error instanceof TypeError) rejected++; }
            try { C.callField(value); returned++; }
            catch (error) { if (error instanceof TypeError) rejected++; }
            try { C.callMethod(value); returned++; }
            catch (error) { if (error instanceof TypeError) rejected++; }
        }
        console.log(rejected, returned, C.read(C), C.callField(C), C.callMethod(C));
        """;

    public const string LexicalOwnersSource = """
        class Outer {
            static #value: number = 11;
            static reader() { return class {
                static read(value: any) { return value.#value; }
            }; }
            static shadow() { return class {
                static #value: number = 22;
                static read(value: any) { return value.#value; }
            }; }
        }
        const Reader: any = Outer.reader(), Inner: any = Outer.shadow();
        console.log(Reader.read(Outer), Inner.read(Inner));
        try { Reader.read(Inner); } catch (error) { console.log(error instanceof TypeError); }
        try { Inner.read(Outer); } catch (error) { console.log(error instanceof TypeError); }
        """;

    public const string InitializationSource = """
        class C {
            static first = C.read(C);
            static #m: number = 42;
            static read(value: any): any {
                try { return value.#m; }
                catch (error) { return error instanceof TypeError; }
            }
        }
        console.log(C.first, C.read(C));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", IssueSource, "42\n"];
        yield return ["anonymous", AnonymousSource, "42\n"];
        yield return ["typed-members", TypedMembersSource, "1\n7 10 5\n"];
        yield return ["brands", BrandsSource, "44 0 1 4 6\n"];
        yield return ["lexical-owners", LexicalOwnersSource, "11 22\ntrue\ntrue\n"];
        yield return ["initialization", InitializationSource, "true 42\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DynamicReceiversUseStaticPrivateRuntimeBrands(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    public static IEnumerable<object[]> AcceptedAnyCases()
    {
        yield return ["""
            class C { static #value: number = 1; static write(value: any) { value.#value = "bad"; } }
            """];
        yield return ["""
            class C { static #value: number = 1; static read(value: any): string { return value.#value; } }
            """];
        yield return ["""
            class C { static #value: number = 1; static call(value: any) { return value.#value(); } }
            """];
        yield return ["""
            class C { static #callable: (value: number) => number = (value: number) => value;
                static call(value: any) { return value.#callable("bad"); } }
            """];
        yield return ["""
            class C { static #callable: (value: number) => number = (value: number) => value;
                static call(value: any) { return value.#callable(); } }
            """];
        yield return ["""
            class C { static #method(value: number): number { return value; }
                static call(value: any) { return value.#method("bad"); } }
            """];
        yield return ["""
            class C { static #method(value: number): number { return value; }
                static call(value: any) { return value.#method(); } }
            """];
    }

    [Theory, MemberData(nameof(AcceptedAnyCases))]
    public void StaticPrivateAccessThroughAnyHasAnyType(string source)
    {
        new TypeChecker().Check(TestHarness.ParseOrThrow(source));
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["""
            class C { static #value: number = 1; static write(value: typeof C) { value.#value = "bad"; } }
            """, "TS2322"];
        yield return ["""
            class C { static #value: number = 1; static read(value: typeof C): string { return value.#value; } }
            """, "TS2322"];
        yield return ["""
            class C { static #value: number = 1; static call(value: typeof C) { return value.#value(); } }
            """, "TS2349"];
        yield return ["""
            class C { static #callable: (value: number) => number = (value: number) => value;
                static call(value: typeof C) { return value.#callable("bad"); } }
            """, "TS2345"];
        yield return ["""
            class C { static #callable: (value: number) => number = (value: number) => value;
                static call(value: typeof C) { return value.#callable(); } }
            """, "TS2554"];
        yield return ["""
            class C { static #method(value: number): number { return value; }
                static call(value: typeof C) { return value.#method("bad"); } }
            """, "TS2345"];
        yield return ["""
            class C { static #method(value: number): number { return value; }
                static call(value: typeof C) { return value.#method(); } }
            """, "TS2554"];
        yield return ["""
            class C { static #value: number = 1; static read(value: unknown) { return value.#value; } }
            """, "TS18046"];
        yield return ["""
            class C { static #value: number = 1; static read(value: {}) { return value.#value; } }
            """, "TS2339"];
        yield return ["""
            class Owner { static #value: number = 1; }
            class Other { static read(value: any) { return value.#value; } }
            """, "TS2339"];
        yield return ["""
            class C { static #value: number = 1; } const value: any = C; console.log(value.#value);
            """, "TS18013"];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void TypedReceiversAndLexicalVisibilityRetainTheirChecks(string source, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
