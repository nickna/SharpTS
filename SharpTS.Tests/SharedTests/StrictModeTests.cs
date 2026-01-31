using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class StrictModeTests
{
    // Basic directive parsing
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_DirectiveIsParsed(ExecutionMode mode)
    {
        var source = """
            "use strict";
            console.log("parsed");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    // Frozen object tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_FrozenObject_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const obj = Object.freeze({ x: 1 });
            obj.x = 2;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
        Assert.Contains("Cannot assign to read only property", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NoUseStrict_FrozenObject_SilentlyFails(ExecutionMode mode)
    {
        var source = """
            const obj = Object.freeze({ x: 1 });
            obj.x = 2;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output); // Value should remain 1 (modification silently ignored)
    }

    // Sealed object tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_SealedObject_AddProperty_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const obj: { x: number, y?: number } = Object.seal({ x: 1 });
            obj.y = 2;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
        Assert.Contains("Cannot add property", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_SealedObject_ModifyExisting_Works(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const obj = Object.seal({ x: 1 });
            obj.x = 2;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output); // Sealed objects allow modifying existing properties
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NoUseStrict_SealedObject_AddProperty_SilentlyFails(ExecutionMode mode)
    {
        var source = """
            const obj: { x: number, y?: number } = Object.seal({ x: 1 });
            obj.y = 2;
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nnull\n", output); // Property addition silently ignored (null used for missing properties)
    }

    // Frozen array tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_FrozenArray_SetElement_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const arr = Object.freeze([1, 2, 3]);
            arr[0] = 10;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NoUseStrict_FrozenArray_SetElement_SilentlyFails(ExecutionMode mode)
    {
        var source = """
            const arr = Object.freeze([1, 2, 3]);
            arr[0] = 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output); // Element modification silently ignored
    }

    // Function-level strict mode
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_FunctionLevel_AffectsOnlyFunction(ExecutionMode mode)
    {
        var source = """
            const globalObj = Object.freeze({ x: 1 });

            function strictFunc() {
                "use strict";
                const localObj = Object.freeze({ y: 2 });
                localObj.y = 10; // Should throw in strict function
            }

            globalObj.x = 10; // Non-strict, should silently fail
            console.log(globalObj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output); // Global modification silently ignored
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_FunctionLevel_ThrowsInFunction(ExecutionMode mode)
    {
        var source = """
            function strictFunc() {
                "use strict";
                const obj = Object.freeze({ x: 1 });
                obj.x = 2;
            }
            strictFunc();
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
    }

    // Strict mode inheritance to nested functions (uses arrow functions for compiler compatibility)
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_InheritsToNestedFunctions(ExecutionMode mode)
    {
        var source = """
            "use strict";

            function outer() {
                const inner = () => {
                    const obj = Object.freeze({ x: 1 });
                    obj.x = 2; // Should throw because strict mode inherited
                };
                inner();
            }
            outer();
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
    }

    // Multiple directives
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_OtherDirectivesAllowed(ExecutionMode mode)
    {
        var source = """
            "use strict";
            "other directive";
            console.log("works");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("works\n", output);
    }

    // Directive must be at the start
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_AfterStatements_NotDirective(ExecutionMode mode)
    {
        var source = """
            console.log("first");
            "use strict";
            const obj = Object.freeze({ x: 1 });
            obj.x = 2; // Should NOT throw because "use strict" after statement is just a string
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n1\n", output);
    }

    // Class instance frozen
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_FrozenClassInstance_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            const p = Object.freeze(new Point(1, 2));
            p.x = 10;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
    }
}
