using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ClassIntersectionUtilityProjectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool modules in new[] { false, true })
        {
            yield return ["""
                class Box { value = 1; }
                declare let copy: Pick<Box & { other: string }, 'value'>;
                const value: number = copy.value;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { value = 1; }
                declare let copy: Omit<Box & { other: string }, 'other'>;
                const value: number = copy.value;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { value = 1; }
                declare let copy: Required<Box & { value: string }>;
                const value: never = copy.value;
                copy.value = 1;
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { value = 1; }
                declare let copy: Readonly<Box & { value: string }>;
                const value: never = copy.value;
                copy.value = 1;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                class Base<T> { readonly value!: T; mutable!: T; }
                class Box extends Base<number> {}
                declare let copy: Required<Box & { other: string }>;
                const value: number = copy.value;
                const other: string = copy.other;
                copy.mutable = 2;
                copy.value = 2;
                copy.mutable = 'wrong';
                """, new[] { "TS2540", "TS2322" }, modules];
            yield return ["""
                class Box { readonly value: number = 1; }
                declare let copy: Required<Box & { value: number }>;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { readonly value: number = 1; }
                declare let copy: Pick<Box & { value: number }, 'value'>;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { readonly value: number = 1; }
                declare let copy: Omit<Box & { value: number; other: string }, 'other'>;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { value?: number; }
                const empty: Pick<Box & { other: string }, 'value'> = {};
                declare let copy: Pick<Box & { other: string }, 'value'>;
                const value: number | undefined = copy.value;
                const invalid: number = copy.value;
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { value?: number; }
                declare let copy: Pick<Box & { value: number }, 'value'>;
                const value: number = copy.value;
                const empty: Pick<Box & { value: number }, 'value'> = {};
                """, new[] { "TS2741" }, modules];
            yield return ["""
                class Box { value = 1; }
                declare let copy: Partial<Box & { value: string }>;
                const value: undefined = copy.value;
                copy.value = 1;
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Box { private hidden = 1; value = 2; }
                declare let copy: Omit<Box & { other: string }, 'other'>;
                const value: number = copy.value;
                copy.hidden;
                """, new[] { "TS2339" }, modules];
            yield return ["""
                class Base { private hidden = 1; protected secret = 2; value = 3; }
                interface Named extends Base {}
                declare let copy: Omit<Named & { other: string }, 'other'>;
                const value: number = copy.value;
                copy.hidden;
                copy.secret;
                """, new[] { "TS2339", "TS2339" }, modules];
            yield return ["""
                class Animal { base = 1; }
                class Dog extends Animal { dog = 1; }
                class Box { handle(value: Animal): void {} }
                declare let copy: Pick<Box & { other: number }, 'handle'>;
                const narrow = { handle(value: Dog): void {} };
                const admitted: typeof copy = narrow;
                const invalid: typeof copy = { handle(value: string): void {} };
                """, new[] { "TS2322" }, modules];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void UtilityProjectionRetainsClassIntersectionTypesAndEffectiveModifiers(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "class-intersection-utility-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }
}
