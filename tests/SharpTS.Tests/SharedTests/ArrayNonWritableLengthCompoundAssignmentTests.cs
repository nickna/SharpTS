using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayNonWritableLengthCompoundAssignmentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["raw-length-compound", """
            const values: any[] = [1];
            Object.defineProperty(values, "length", { writable: false });
            let rights = 0;
            const result = values[1] += (() => { rights++; return 2; })();
            console.log("sloppy", Number.isNaN(result), values.length, Object.hasOwn(values, "1"), rights);
            function strictCompound() {
                "use strict";
                const values: any[] = [1];
                Object.defineProperty(values, "length", { writable: false });
                let rights = 0;
                try { values[1] += (() => { rights++; return 2; })(); console.log("strict", "completed", values.length, Object.hasOwn(values, "1"), rights); }
                catch (error) { console.log("strict", error instanceof TypeError, values.length, Object.hasOwn(values, "1"), rights); }
            }
            strictCompound();
            """, "sloppy true 1 false 1\nstrict true 1 false 1\n"];
        yield return ["nonwritable-length", """
            const values: any[] = [1];
            Object.defineProperty(values, "length", { writable: false });
            const result = values[1] += 2;
            console.log(Number.isNaN(result), values.length, Object.hasOwn(values, "1"));
            function strictCase() {
                "use strict";
                const values: any[] = [1];
                Object.defineProperty(values, "length", { writable: false });
                try { values[1] += 2; console.log("completed"); }
                catch (error) { console.log(error instanceof TypeError, values.length, Object.hasOwn(values, "1")); }
            }
            strictCase();
            """, "true 1 false\ntrue 1 false\n"];
        yield return ["length-protocol-preservation", """
            function strictCompound() {
                "use strict";
                const values: any[] = [1];
                Object.defineProperty(values, "length", { writable: false });
                try { values[1] += 2; console.log("strict", "completed", values.length, Object.hasOwn(values, "1")); }
                catch (error) { console.log("strict", error instanceof TypeError, values.length, Object.hasOwn(values, "1")); }
            }
            strictCompound();
            const reflected: any[] = [1];
            Object.defineProperty(reflected, "length", { writable: false });
            console.log("reflect", Reflect.set(reflected, "1", 2), reflected.length, Object.hasOwn(reflected, "1"));
            function inheritedSetter() {
                "use strict";
                const values: any[] = [1], state = { value: 4, gets: 0, sets: 0, same: true };
                const prototype = Object.create(Array.prototype);
                Object.defineProperty(prototype, "1", {
                    get() { state.gets++; state.same = state.same && this === values; return state.value; },
                    set(next: number) { state.sets++; state.same = state.same && this === values; state.value = next; },
                    configurable: true
                });
                Object.setPrototypeOf(values, prototype);
                Object.defineProperty(values, "length", { writable: false });
                const result = values[1] += 2;
                console.log("inherited", result, state.value, state.gets, state.sets, state.same, values.length, Object.hasOwn(values, "1"));
            }
            inheritedSetter();
            """, "strict true 1 false\nreflect false 1 false\ninherited 6 6 1 1 true 1 false\n"];
        yield return ["arithmetic-key-rhs-supported", """
            function sloppy(operation: number) {
                const values: any[] = [1];
                Object.defineProperty(values, "length", { writable: false });
                let keys = 0, rights = 0, order = "";
                function key(): number { keys++; order += "key,"; return 1; }
                function right(): number { rights++; order += "right"; return 2; }
                let result: any;
                if (operation === 0) result = values[key()] += right();
                else if (operation === 1) result = values[key()] -= right();
                else if (operation === 2) result = values[key()] *= right();
                else if (operation === 3) result = values[key()] /= right();
                else if (operation === 4) result = values[key()] %= right();
                console.log("sloppy", operation, Number.isNaN(result), values.length,
                    Object.hasOwn(values, "1"), keys, rights, order);
            }
            function strict(operation: number) {
                "use strict";
                const values: any[] = [1];
                Object.defineProperty(values, "length", { writable: false });
                let keys = 0, rights = 0, order = "";
                function key(): number { keys++; order += "key,"; return 1; }
                function right(): number { rights++; order += "right"; return 2; }
                try {
                    if (operation === 0) values[key()] += right();
                    else if (operation === 1) values[key()] -= right();
                    else if (operation === 2) values[key()] *= right();
                    else if (operation === 3) values[key()] /= right();
                    else if (operation === 4) values[key()] %= right();
                    console.log("strict", operation, "completed", values.length,
                        Object.hasOwn(values, "1"), keys, rights, order);
                } catch (error) {
                    console.log("strict", operation, error instanceof TypeError, values.length,
                        Object.hasOwn(values, "1"), keys, rights, order);
                }
            }
            for (let operation = 0; operation < 5; operation++) {
                sloppy(operation);
                strict(operation);
            }
            function permittedWrites() {
                "use strict";
                const values: any[] = [2];
                values.length = 3;
                Object.defineProperty(values, "length", { writable: false });
                const existing = values[0] *= 3;
                const hole = values[1] += 2;
                console.log("permitted", existing, values[0], Number.isNaN(hole),
                    Object.hasOwn(values, "1"), values.length, Object.hasOwn(values, "3"));
            }
            permittedWrites();
            """ + "\n", "sloppy 0 true 1 false 1 1 key,right\nstrict 0 true 1 false 1 1 key,right\nsloppy 1 true 1 false 1 1 key,right\nstrict 1 true 1 false 1 1 key,right\nsloppy 2 true 1 false 1 1 key,right\nstrict 2 true 1 false 1 1 key,right\nsloppy 3 true 1 false 1 1 key,right\nstrict 3 true 1 false 1 1 key,right\nsloppy 4 true 1 false 1 1 key,right\nstrict 4 true 1 false 1 1 key,right\npermitted 6 6 true true 3 false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NumericArrayCompoundsRespectIndependentLengthWritability(
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
