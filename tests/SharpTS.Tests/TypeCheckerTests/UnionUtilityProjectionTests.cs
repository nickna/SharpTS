using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class UnionUtilityProjectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool modules in new[] { false, true })
        {
            yield return ["""
                type Box = { value: number; other: string } | { value: number; other: boolean };
                declare let copy: Pick<Box, 'value'>;
                const value: number = copy.value;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                type Box = { value: number; other: string } | { value: number; other: boolean };
                declare let copy: Omit<Box, 'other'>;
                const value: number = copy.value;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                type Box = { value: number; left: number } | { value: string; right: boolean };
                declare let copy: Omit<Box, never>;
                const value: number | string = copy.value;
                copy.value = 2;
                copy.value = 'ok';
                copy.left;
                copy.right;
                copy.value = true;
                """, new[] { "TS2339", "TS2339", "TS2322" }, modules];
            yield return ["""
                type Box = { value: number; other: string } | { value: string; other: boolean };
                declare let copy: Pick<Box, 'value' | 'other'>;
                const value: number | string = copy.value;
                const other: string | boolean = copy.other;
                copy.value = 'ok';
                copy.other = false;
                copy.value = true;
                copy.other = 1;
                """, new[] { "TS2322", "TS2322" }, modules];
            yield return ["""
                type Box = { value?: number; other: string } | { value: number; other: boolean };
                const empty: Pick<Box, 'value'> = {};
                declare let copy: Omit<Box, 'other'>;
                const value: number | undefined = copy.value;
                const invalid: number = copy.value;
                copy.value = 2;
                """, new[] { "TS2322" }, modules];
            yield return ["""
                type Box = { readonly value: number; mutable: number; other: string }
                    | { value: number; mutable: number; other: boolean };
                declare let copy: Pick<Box, 'value' | 'mutable'>;
                const value: number = copy.value;
                copy.mutable = 2;
                copy.value = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                type Box = { value: number; mutable: number; other: string }
                    | { readonly value: number; mutable: number; other: boolean };
                declare let copy: Omit<Box, 'other'>;
                copy.mutable = 2;
                copy.value = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                type Box<T> = { readonly value: T; mutable: T; other: string }
                    | { value: T; mutable: T; other: boolean };
                type Copy<T> = Pick<Box<T>, 'value' | 'mutable'>;
                declare let copy: Copy<number>;
                const value: number = copy.value;
                copy.mutable = 2;
                copy.value = 2;
                copy.mutable = 'wrong';
                """, new[] { "TS2540", "TS2322" }, modules];
            yield return ["""
                interface Base { readonly value: number; mutable: number; }
                interface Left extends Base { left: string; }
                interface Right extends Base { right: boolean; }
                declare let copy: Omit<Left | Right, never>;
                const value: number = copy.value;
                copy.mutable = 2;
                copy.value = 2;
                copy.left;
                """, new[] { "TS2540", "TS2339" }, modules];
            yield return ["""
                type Box = { value: number; other: string } | { value: number; other: boolean };
                const missing: Pick<Box, 'value'> = {};
                """, new[] { "TS2741" }, modules];
            yield return ["""
                type Box = { [key: string]: number } | { value: number; other: number };
                declare let copy: Omit<Box, never>;
                const value: number = copy.value;
                copy.other = 2;
                copy.other = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                type Box = Readonly<Record<string, number>> | { value: number };
                declare let copy: Pick<Box, 'value'>;
                const value: number = copy.value;
                copy.value = 2;
                """, new[] { "TS2540" }, modules];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void PickAndOmitRetainOnlyCommonUnionPropertiesAndTheirModifiers(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "union-utility-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }
}
