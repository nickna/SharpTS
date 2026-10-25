using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class RequiredInheritedClassMemberTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool modules in new[] { false, true })
        {
            yield return ["""
                class Base { readonly value: number = 1; }
                class Child extends Base { mutable: number = 2; }
                declare let copy: Required<Child>;
                const value: number = copy.value;
                copy.mutable = 3;
                copy.value = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                class Base { value: number = 1; }
                class Child extends Base {}
                declare let copy: Required<Child>;
                const value: number = copy.value;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Base { readonly value: number = 1; }
                class Middle extends Base { value: number = 2; }
                class Child extends Middle {}
                declare let copy: Required<Child>;
                copy.value = 3;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Base { get value(): number { return 1; } }
                class Child extends Base {}
                declare let copy: Required<Child>;
                const value: number = copy.value;
                copy.value = 2;
                """, new[] { "TS2540" }, modules];
            yield return ["""
                class Base {
                    get value(): number { return 1; }
                    set value(next: number) {}
                }
                class Child extends Base {}
                declare let copy: Required<Child>;
                copy.value = 2;
                copy.value = 'wrong';
                """, new[] { "TS2322" }, modules];
            yield return ["""
                class Base<T> {
                    readonly value!: T;
                    mutable!: T;
                    read(input: T): T { return input; }
                }
                class Middle<U> extends Base<U> {}
                class Child extends Middle<number> {}
                declare let copy: Required<Child>;
                const value: number = copy.value;
                const result: number = copy.read(1);
                copy.mutable = 2;
                copy.value = 2;
                copy.mutable = 'wrong';
                """, new[] { "TS2540", "TS2322" }, modules];
            yield return ["""
                class Base<T> { readonly value!: T; }
                class Child<T> extends Base<T> { own!: T; }
                declare let copy: Required<Child<number>>;
                const value: number = copy.value;
                copy.own = 2;
                copy.value = 2;
                copy.own = 'wrong';
                """, new[] { "TS2540", "TS2322" }, modules];
            yield return ["""
                class Base { value?: number; }
                class Child extends Base {}
                declare let copy: Required<Child>;
                const value: number = copy.value;
                const missing: Required<Child> = {};
                """, new[] { "TS2741" }, modules];
            yield return ["""
                class Base {
                    private secret = 1;
                    protected hidden = 2;
                    private get privateValue(): number { return 1; }
                    protected get protectedValue(): number { return 2; }
                    public visible = 3;
                }
                class Child extends Base { own = 4; }
                declare let copy: Required<Child>;
                const value: number = copy.visible;
                copy.secret;
                copy.hidden;
                copy.privateValue;
                copy.protectedValue;
                """, new[] { "TS2339", "TS2339", "TS2339", "TS2339" }, modules];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void InheritedClassProjectionRetainsPublicTypesAndModifiers(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "required-inherited-class-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }
}
