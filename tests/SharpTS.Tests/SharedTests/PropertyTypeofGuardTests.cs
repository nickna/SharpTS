using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PropertyTypeofGuardTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["single-property-guard", """
            function run(holder: { value: string | number }) {
                if (typeof holder.value === 'string') console.log(holder.value.toUpperCase());
            }
            run({ value: 'property' });
            """ + "\r\n", "PROPERTY\n"];
        yield return ["no-loop-with-write", """
            function run(holder: { value: string | number }, other: { value: string | number }) {
                if (typeof holder.value === 'string' && typeof other.value === 'string') {
                    other.value = 42;
                    console.log(holder.value.toUpperCase(), other.value.toFixed(0));
                }
            }
            run({ value: 'property' }, { value: 'before' });
            """ + "\r\n", "PROPERTY 42\n"];
        yield return ["no-loop-no-write", "function run(holder: { value: string | number }, other: { value: string | number }) {\r\n    if (typeof holder.value === 'string' && typeof other.value === 'string') {\r\n        \r\n        console.log(holder.value.toUpperCase(), other.value.toUpperCase());\r\n    }\r\n}\r\nrun({ value: 'property' }, { value: 'before' });\r\n", "PROPERTY BEFORE\n"];
        yield return ["loop-without-write", """
            function run(holder: { value: string | number }, other: { value: string | number }) {
                if (typeof holder.value === 'string' && typeof other.value === 'string') {
                    for (let holder = 0; holder < 1; holder++) {}
                    console.log(holder.value.toUpperCase(), other.value.toUpperCase());
                }
            }
            run({ value: 'property' }, { value: 'before' });
            """ + "\r\n", "PROPERTY BEFORE\n"];
        yield return ["full-existing-null-guard-control", """
            function run(holder: { value: string | null }, other: { value: string | number }) {
                if (holder.value !== null) {
                    for (let holder = 0, unused = (other.value = 42); holder < 1; holder++) {}
                    const text: string = holder.value;
                    const number: number = other.value;
                    console.log(text.toUpperCase(), number.toFixed(0));
                }
            }
            run({ value: 'property' }, { value: 'before' });
            """, "PROPERTY 42\n"];
        yield return ["operator-order-and-branch-matrix", """
            function inspect(holder: { value: string | number }, nested: { inner: { value: string | number } }, tuple: [string | number]) {
                if (typeof holder.value == "string") console.log("eq-left", holder.value.toUpperCase()); else console.log("eq-left", holder.value.toFixed(0));
                if ("string" == typeof holder.value) console.log("eq-right", holder.value.toUpperCase()); else console.log("eq-right", holder.value.toFixed(0));
                if (typeof holder.value === "string") console.log("strict-left", holder.value.toUpperCase()); else console.log("strict-left", holder.value.toFixed(0));
                if ("string" === typeof holder.value) console.log("strict-right", holder.value.toUpperCase()); else console.log("strict-right", holder.value.toFixed(0));
                if (typeof holder.value != "string") console.log("neq-left", holder.value.toFixed(0)); else console.log("neq-left", holder.value.toUpperCase());
                if ("string" != typeof holder.value) console.log("neq-right", holder.value.toFixed(0)); else console.log("neq-right", holder.value.toUpperCase());
                if (typeof holder.value !== "string") console.log("strict-neq-left", holder.value.toFixed(0)); else console.log("strict-neq-left", holder.value.toUpperCase());
                if ("string" !== typeof holder.value) console.log("strict-neq-right", holder.value.toFixed(0)); else console.log("strict-neq-right", holder.value.toUpperCase());
                if (typeof nested.inner.value === "string" && typeof tuple[0] !== "number") console.log(nested.inner.value.toUpperCase(), tuple[0].toUpperCase());
            }
            inspect({ value: "matrix" }, { inner: { value: "nested" } }, ["tuple"]);
            inspect({ value: 7 }, { inner: { value: 2 } }, [3]);
            """ + "\n", "eq-left MATRIX\neq-right MATRIX\nstrict-left MATRIX\nstrict-right MATRIX\nneq-left MATRIX\nneq-right MATRIX\nstrict-neq-left MATRIX\nstrict-neq-right MATRIX\nNESTED TUPLE\neq-left 7\neq-right 7\nstrict-left 7\nstrict-right 7\nneq-left 7\nneq-right 7\nstrict-neq-left 7\nstrict-neq-right 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryPropertyGuardsPreserveBranchTypesAndWrites(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
