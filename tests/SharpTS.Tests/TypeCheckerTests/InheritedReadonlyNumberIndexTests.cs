using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InheritedReadonlyNumberIndexTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool modules in new[] { false, true })
        {
            yield return ["""
                type Base = Readonly<Record<number, number>>;
                interface Derived extends Base {}
                declare let copy: Derived;
                const value: number = copy[0];
                copy[0] = 2;
                copy['1'] = 2;
                declare let key: 2 | '3';
                copy[key] = 2;
                """, new[] { "TS2542", "TS2542", "TS2542" }, modules];
            yield return ["""
                type Base<T> = Readonly<Record<number, T>>;
                interface Middle<T> extends Base<T> { named: T; optional?: T; }
                interface Derived extends Middle<number> {}
                declare let copy: Derived;
                const value: number = copy[0];
                const optional: number | undefined = copy.optional;
                copy.named = 2;
                copy[0] = 2;
                copy.named = 'wrong';
                """, new[] { "TS2542", "TS2322" }, modules];
            yield return ["""
                type Base = Readonly<Record<number, number>>;
                interface Derived extends Base { [index: number]: number; }
                declare let copy: Derived;
                const value: number = copy[0];
                copy[0] = 2;
                copy[0] = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                type Base<T> = Readonly<Record<number, T>>;
                interface Middle<T> extends Base<T> { [index: number]: T; }
                interface Derived extends Middle<number> {}
                declare let copy: Derived;
                const value: number = copy[0];
                copy[0] = 2;
                copy[0] = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                type Base = Readonly<Record<number, number>>;
                interface Derived extends Base { 0: number; }
                declare let copy: Derived;
                copy[0] = 2;
                copy['0'] = 2;
                copy[0]++;
                copy[1] = 2;
                """, new[] { "TS2542" }, modules];
            yield return ["""
                type Base = Readonly<Record<number, number>>;
                interface Derived extends Base { readonly 0: number; }
                declare let copy: Derived;
                copy[0] = 2;
                copy[1] = 2;
                """, new[] { "TS2540", "TS2542" }, modules];
            yield return ["""
                type Base = Readonly<Record<number, number>>;
                interface Derived extends Base {}
                declare let copy: Derived;
                copy[0]++;
                --copy['1'];
                copy[2] += 1;
                copy[3] ||= 1;
                """, new[] { "TS2542", "TS2542", "TS2542", "TS2542" }, modules];
            yield return ["""
                type Base = Readonly<Record<number, number>>;
                interface Derived extends Base {}
                declare let copy: Derived | Record<number, number>;
                copy[0] = 2;
                """, new[] { "TS2542" }, modules];
            yield return ["""
                type Base<T> = Readonly<Record<number, T>>;
                interface Derived<T> extends Base<T> {}
                declare let copy: Derived<number> | Record<number, number>;
                copy[0] = 2;
                """, new[] { "TS2542" }, modules];
            yield return ["""
                type ReadonlyBase = Readonly<Record<number, number>>;
                type MutableBase = Record<number, number>;
                interface Derived extends ReadonlyBase, MutableBase {}
                declare let copy: Derived;
                copy[0] = 2;
                """, new[] { "TS2542" }, modules];
            yield return ["""
                type ReadonlyBase = Readonly<Record<number, number>>;
                type MutableBase = Record<number, number>;
                interface Derived extends MutableBase, ReadonlyBase {}
                declare let copy: Derived;
                copy[0] = 2;
                copy[0] = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                interface Derived extends ReadonlyArray<number> { [index: number]: number; }
                declare let copy: Derived;
                copy[0] = 2;
                copy[0] = 'wrong';
                """, new[] { "TS2322" }, modules];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void NumericIndexInheritancePreservesReadonlyAndNearestMutableOverrides(string source, string[] codes, bool modules) =>
        Assert.Equal(codes, Check(source, modules).Select(d => d.TsCode));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReadonlyGuardDoesNotDependOnNullOrUncheckedIndexOptions(bool strictNullChecks, bool noUncheckedIndexedAccess)
    {
        const string source = """
            type Base = Readonly<Record<number, number>>;
            interface Derived extends Base {}
            declare let copy: Derived;
            const value: number | undefined = copy[0];
            copy[0] = null;
            """;
        var options = TypeCheckerOptions.Strict with
        {
            StrictNullChecks = strictNullChecks,
            NoUncheckedIndexedAccess = noUncheckedIndexedAccess
        };
        Assert.Equal("TS2542", Assert.Single(Check(source, false, options)).TsCode);
    }

    [Fact]
    public void GenericNumericKeysKeepTypeScriptAdmissionBehavior()
    {
        const string source = """
            type Base = Readonly<Record<number, number>>;
            interface Derived extends Base {}
            function set<K extends number>(copy: Derived, key: K) { copy[key] = 1; }
            """;
        Assert.Empty(Check(source, false));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules, TypeCheckerOptions? options = null)
    {
        var checker = new TypeChecker(options ?? TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "inherited-readonly-index-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
