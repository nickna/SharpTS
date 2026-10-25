using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DerivedUtilityReadonlyMemberTests
{
    public static IEnumerable<object[]> Projections()
    {
        foreach (string projection in new[] { "Required<Box>", "Pick<Box, 'value' | 'mutable'>", "Omit<Box, 'other'>", "Box" })
            foreach (bool modules in new[] { false, true })
                yield return [projection, modules];
    }

    [Theory, MemberData(nameof(Projections))]
    public void DerivedInterfacesEnforceReadonlyAndRetainMutableMemberTypes(string projection, bool modules)
    {
        string source = $$"""
            interface Box { readonly value: number; mutable: number; other: string; }
            interface Derived extends {{projection}} {}
            declare let box: Derived;
            const value: number = box.value;
            box.mutable = 2;
            box.value = 2;
            """;
        var diagnostic = Assert.Single(Check(source, modules));
        Assert.Equal("TS2540", diagnostic.TsCode);
        Assert.Equal(6, diagnostic.Line);
    }

    [Theory]
    [InlineData("Required<Box>", false)]
    [InlineData("Required<Box>", true)]
    [InlineData("Pick<Box, 'value' | 'mutable'>", false)]
    [InlineData("Pick<Box, 'value' | 'mutable'>", true)]
    [InlineData("Omit<Box, 'other'>", false)]
    [InlineData("Omit<Box, 'other'>", true)]
    public void GetterOnlyMembersRemainReadonlyThroughProjectedBases(string projection, bool modules)
    {
        string source = $$"""
            class Box { get value(): number { return 1; } mutable = 1; other = 's'; }
            interface Derived extends {{projection}} {}
            declare let box: Derived;
            const value: number = box.value;
            box.mutable = 2;
            box.value = 2;
            """;
        var diagnostic = Assert.Single(Check(source, modules));
        Assert.Equal("TS2540", diagnostic.TsCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalAndWholeRecordReadonlyMetadataSurviveInheritance(bool modules)
    {
        var diagnostics = Check("""
            interface Optional { readonly value?: number; }
            interface OptionalDerived extends Optional {}
            declare let optional: OptionalDerived;
            const value: number | undefined = optional.value;
            optional.value = 2;
            type AllReadonly = Readonly<{ value: number }>;
            interface ReadonlyDerived extends AllReadonly {}
            declare let allReadonly: ReadonlyDerived;
            allReadonly.value = 2;
            """, modules);
        Assert.Equal(new[] { "TS2540", "TS2540" }, diagnostics.Select(diagnostic => diagnostic.TsCode));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MutableOverridesAtMiddleOrLeafShadowReadonlyAncestors(bool atLeaf, bool modules)
    {
        string source = $$"""
            interface Box { readonly value: number; }
            interface Middle extends Required<Box> { {{(atLeaf ? "" : "value: number;")}} }
            interface Derived extends Middle { {{(atLeaf ? "value: number;" : "")}} }
            declare let box: Derived;
            box.value = 2;
            const value: number = box.value;
            box.value = 'wrong';
            """;
        var diagnostic = Assert.Single(Check(source, modules));
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(7, diagnostic.Line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericProjectionBasesRetainReadonlyAndNumericSubstitution(bool modules)
    {
        var diagnostics = Check("""
            interface Box<T> { readonly value: T; mutable: T; }
            interface Derived<T> extends Pick<Box<T>, 'value' | 'mutable'> {}
            declare let box: Derived<number>;
            const value: number = box.value;
            box.mutable = 2;
            box.value = 2;
            box.mutable = 'wrong';
            """, modules);
        Assert.Equal(new[] { "TS2540", "TS2322" }, diagnostics.Select(diagnostic => diagnostic.TsCode));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GenericMutableOverridesKeepTheirSubstitutedNumericType(bool atLeaf, bool modules)
    {
        string source = $$"""
            interface Box<T> { readonly value: T; }
            interface Middle<T> extends Required<Box<T>> { {{(atLeaf ? "" : "value: T;")}} }
            interface Derived<T> extends Middle<T> { {{(atLeaf ? "value: T;" : "")}} }
            declare let box: Derived<number>;
            box.value = 2;
            const value: number = box.value;
            box.value = 'wrong';
            """;
        var diagnostic = Assert.Single(Check(source, modules));
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(7, diagnostic.Line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnReadonlyDeclarationsStillOverrideMutableBases(bool modules)
    {
        var diagnostic = Assert.Single(Check("""
            interface Base { value: number; }
            interface Derived extends Base { readonly value: number; }
            declare let box: Derived;
            box.value = 2;
            """, modules));
        Assert.Equal("TS2540", diagnostic.TsCode);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "derived-utility-readonly-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
