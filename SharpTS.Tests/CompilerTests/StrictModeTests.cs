using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class StrictModeTests
{
    // Basic directive parsing
    [Fact]
    public void UseStrict_DirectiveIsParsed()
    {
        var source = """
            "use strict";
            console.log("parsed");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("parsed\n", output);
    }

    // Frozen object tests
    [Fact]
    public void UseStrict_FrozenObject_ThrowsTypeError()
    {
        var source = """
            "use strict";
            const obj = Object.freeze({ x: 1 });
            obj.x = 2;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("TypeError", ex.Message);
    }

    [Fact]
    public void NoUseStrict_FrozenObject_SilentlyFails()
    {
        var source = """
            const obj = Object.freeze({ x: 1 });
            obj.x = 2;
            console.log(obj.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output); // Value should remain 1 (modification silently ignored)
    }

    // Sealed object tests
    [Fact]
    public void UseStrict_SealedObject_AddProperty_ThrowsTypeError()
    {
        var source = """
            "use strict";
            const obj: { x: number, y?: number } = Object.seal({ x: 1 });
            obj.y = 2;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("TypeError", ex.Message);
    }

    [Fact]
    public void UseStrict_SealedObject_ModifyExisting_Works()
    {
        var source = """
            "use strict";
            const obj = Object.seal({ x: 1 });
            obj.x = 2;
            console.log(obj.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n", output); // Sealed objects allow modifying existing properties
    }

    [Fact]
    public void NoUseStrict_SealedObject_AddProperty_SilentlyFails()
    {
        var source = """
            const obj: { x: number, y?: number } = Object.seal({ x: 1 });
            obj.y = 2;
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\nnull\n", output); // Property addition silently ignored (null used for missing properties)
    }

    // Frozen array tests
    [Fact]
    public void UseStrict_FrozenArray_SetElement_ThrowsTypeError()
    {
        var source = """
            "use strict";
            const arr = Object.freeze([1, 2, 3]);
            arr[0] = 10;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("TypeError", ex.Message);
    }

    [Fact]
    public void NoUseStrict_FrozenArray_SetElement_SilentlyFails()
    {
        var source = """
            const arr = Object.freeze([1, 2, 3]);
            arr[0] = 10;
            console.log(arr[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output); // Element modification silently ignored
    }

    // Function-level strict mode
    [Fact]
    public void UseStrict_FunctionLevel_AffectsOnlyFunction()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output); // Global modification silently ignored
    }

    [Fact]
    public void UseStrict_FunctionLevel_ThrowsInFunction()
    {
        var source = """
            function strictFunc() {
                "use strict";
                const obj = Object.freeze({ x: 1 });
                obj.x = 2;
            }
            strictFunc();
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("TypeError", ex.Message);
    }

    // Strict mode inheritance to nested functions (using arrow functions which are supported by compiler)
    [Fact]
    public void UseStrict_InheritsToNestedFunctions()
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

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("TypeError", ex.Message);
    }

    // Multiple directives
    [Fact]
    public void UseStrict_OtherDirectivesAllowed()
    {
        var source = """
            "use strict";
            "other directive";
            console.log("works");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("works\n", output);
    }

    // Directive must be at the start
    [Fact]
    public void UseStrict_AfterStatements_NotDirective()
    {
        var source = """
            console.log("first");
            "use strict";
            const obj = Object.freeze({ x: 1 });
            obj.x = 2; // Should NOT throw because "use strict" after statement is just a string
            console.log(obj.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\n1\n", output);
    }

    // Class instance frozen
    [Fact]
    public void UseStrict_FrozenClassInstance_ThrowsTypeError()
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

        var ex = Assert.Throws<Exception>(() => TestHarness.RunCompiled(source));
        Assert.Contains("TypeError", ex.Message);
    }
}
