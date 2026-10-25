using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ReadonlyClassProjectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool modules in new[] { false, true })
        {
            yield return ["""
                class Box { value = 1; }
                const original = new Box();
                original.value = 2;
                const copy: Readonly<Box> = original;
                const value: number = copy.value;
                copy.value = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                class Box { value = 1; }
                declare let copy: Readonly<Box>;
                copy['value'] = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                class Box { 0 = 1; }
                declare let copy: Readonly<Box>;
                const value: number = copy[0];
                copy[0] = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                class Base { inherited = 1; optional?: number; }
                class Box extends Base { own = 2; }
                declare let copy: Readonly<Box>;
                const value: number = copy.inherited;
                const optional: number | undefined = copy.optional;
                copy.inherited = 2;
                copy.optional = 2;
                copy.own = 2;
                """, new[] { "TS2540", "TS2540", "TS2540" }, modules];
            yield return ["""
                class Base<T> { value!: T; }
                class Box<T> extends Base<T> { own!: T; }
                declare let copy: Readonly<Box<number>>;
                const value: number = copy.value;
                const invalid: string = copy.own;
                copy.value = 2;
                """, new[] { "TS2322", "TS2540" }, modules];
            yield return ["""
                class Base {
                    get value(): number { return 1; }
                    set value(next: number) {}
                    read(): number { return 1; }
                }
                class Box extends Base {}
                declare let copy: Readonly<Box>;
                const value: number = copy.value;
                const result: number = copy.read();
                copy.value = 2;
                copy.read = () => 2;
                """, new[] { "TS2540", "TS2540" }, modules];
            yield return ["""
                class Base { inherited = 1; }
                class Box extends Base { own = 2; }
                declare let copy: Readonly<Box & { other: number }>;
                const value: number = copy.inherited;
                const own: number = copy.own;
                const other: number = copy.other;
                copy.inherited = 2;
                copy.other = 2;
                """, new[] { "TS2540", "TS2540" }, modules];
            yield return ["""
                declare let copy: Readonly<{ value?: number } & { other: number }>;
                const optional: number | undefined = copy.value;
                copy.value = 2;
                copy.other = 2;
                """, new[] { "TS2540", "TS2540" }, modules];
            yield return ["""
                class Box { [index: number]: number; }
                declare let copy: Readonly<Box>;
                const value: number = copy[0];
                copy[0] = 2;
                """, new[] { "TS2542" }, modules];
            yield return ["""
                class Box { private secret = 1; protected hidden = 2; visible = 3; }
                declare let copy: Readonly<Box>;
                const value: number = copy.visible;
                copy.secret;
                copy.hidden;
                """, new[] { "TS2339", "TS2339" }, modules];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReadonlyClassAndIntersectionProjectionsRetainTypesAndRejectWrites(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "readonly-class-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }
}
