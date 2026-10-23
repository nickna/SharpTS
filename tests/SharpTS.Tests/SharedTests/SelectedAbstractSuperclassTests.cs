using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class SelectedAbstractSuperclassTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["changed-abstract-implicit", """
            class Original { original = 1; }
            abstract class Selected { selected = 2; }
            class Derived extends Original { own = 3; }
            Object.setPrototypeOf(Derived, Selected);
            const value: any = new Derived();
            console.log(value.original === undefined, value.selected, value.own,
                Object.getPrototypeOf(value) === Derived.prototype);
            """, "true 2 3 true\n"];
        yield return ["changed-abstract-explicit", """
            class Original { original = 1; }
            abstract class Selected { selected = 2; }
            class Derived extends Original { own = 3; constructor() { super(); } }
            Object.setPrototypeOf(Derived, Selected);
            const value: any = new Derived();
            console.log(value.original === undefined, value.selected, value.own,
                Object.getPrototypeOf(value) === Derived.prototype);
            """, "true 2 3 true\n"];
        yield return ["unchanged-declared-abstract-control", """
            class Original { original = 1; }
            abstract class Selected { selected = 2; }
            class Derived extends Selected { own = 3; }
            Object.setPrototypeOf(Derived, Selected);
            const value: any = new Derived();
            console.log(value.original === undefined, value.selected, value.own,
                Object.getPrototypeOf(value) === Derived.prototype);
            """, "true 2 3 true\n"];
        yield return ["changed-concrete-control", """
            class Original { original = 1; }
            class Selected { selected = 2; }
            class Derived extends Original { own = 3; }
            Object.setPrototypeOf(Derived, Selected);
            const value: any = new Derived();
            console.log(value.original === undefined, value.selected, value.own,
                Object.getPrototypeOf(value) === Derived.prototype);
            """, "true 2 3 true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SelectedAbstractSuperclassesInitializeConcreteDerivedInstances(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
