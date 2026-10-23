using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassConstructorSelectedSuperTests
{
    public const string IssueSource = """
        class First { constructor() { console.log("first"); } }
        class Second { constructor() { console.log("second"); } }
        class Explicit extends First { constructor() { super(); } }
        class Implicit extends First {}
        Object.setPrototypeOf(Explicit, Second);
        Object.setPrototypeOf(Implicit, Second);
        new Explicit();
        new Implicit();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-explicit-and-implicit", IssueSource, "second\nsecond\n"];
        yield return ["active-constructor-and-descendant-new-target", """
            const trace: string[] = [];
            function mark(name: string, value: number): number { trace.push(name); return value; }
            class First {
                first = mark("first-field", 1);
                constructor() { trace.push("first-body"); }
            }
            class Selected {
                selected = mark("selected-field", 2);
                constructor() { trace.push((new.target as any) === Descendant ? "selected-descendant" : "wrong-target"); }
            }
            class Derived extends First {
                child = mark("derived-field", 3);
                constructor() {
                    super();
                    trace.push((new.target as any) === Descendant ? "derived-descendant" : "wrong-target");
                }
            }
            class Descendant extends Derived { leaf = mark("descendant-field", 4); }
            Object.setPrototypeOf(Derived, Selected);
            const value: any = new Descendant();
            console.log(trace.join(","));
            console.log(value.first === undefined, value.selected, value.child, value.leaf);
            console.log(Object.getPrototypeOf(value) === Descendant.prototype,
                Object.getPrototypeOf(Derived.prototype) === First.prototype,
                value instanceof First, value instanceof Selected);
            """, "selected-field,selected-descendant,derived-field,derived-descendant,descendant-field\ntrue 2 3 4\ntrue true true false\n"];
        yield return ["selected-parent-replacement-and-child-once", """
            const trace: string[] = [];
            const state = { children: 0 };
            const replacement: any = { from: "selected" };
            class First {}
            class Selected {
                parent = (trace.push("selected-field"), 5);
                constructor() { trace.push("selected-body"); return replacement; }
            }
            class Derived extends First {
                child = (trace.push("child-field"), ++state.children);
                constructor() { super(); trace.push("child-body"); }
            }
            Object.setPrototypeOf(Derived, Selected);
            const value: any = new Derived();
            console.log(value === replacement, value.from, value.parent === undefined,
                value.child, state.children, Object.getPrototypeOf(value) === Object.prototype);
            console.log(trace.join(","));
            """, "true selected true 1 1 true\nselected-field,selected-body,child-field,child-body\n"];
        yield return ["captured-default-parameter-cell-preserved", """
            class Base {}
            class Derived extends Base {
                constructor(value: number = 7) {
                    super();
                    const read = () => value;
                    console.log(read());
                    value = 8;
                    (this as any).read = read;
                }
            }
            Object.setPrototypeOf(Derived, Base);
            const defaulted: any = new Derived();
            console.log(defaulted.read());
            const supplied: any = new Derived(9);
            console.log(supplied.read());
            """, "7\n8\n9\n8\n"];
    }

    public static IEnumerable<object[]> StandaloneCases() => Cases().Concat(
        ClassConstructorLiveSuperTests.Cases().Where(row => (string)row[0] is
            "explicit-live-parent-and-restoration" or "implicit-expression-live-parent"
            or "derived-nonconstructor-parent-and-base-kind" or "array-leaf-to-ordinary-parent"
            or "ordinary-leaf-to-array-parent"));

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SuperConstructionUsesTheActiveConstructorsSelectedParent(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
