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

    // Delete variable tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_DeleteVariable_ThrowsSyntaxError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            let x = 1;
            delete x;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("Delete of unqualified identifier", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SloppyMode_DeleteVariable_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            let x = 1;
            const result = delete x;
            console.log(result);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        // delete returns false for variables in sloppy mode, variable still exists
        Assert.Contains("false", output);
        Assert.Contains("1", output);
    }

    // Duplicate parameter tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_DuplicateParameters_ThrowsSyntaxError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            function f(a: number, a: number) {
                return a;
            }
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("Duplicate parameter name", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_FunctionLevelStrict_DuplicateParameters_ThrowsSyntaxError(ExecutionMode mode)
    {
        // Function with "use strict" directive inside should detect duplicate params
        var source = """
            function f(a: number, a: number) {
                "use strict";
                return a;
            }
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("Duplicate parameter name", ex.Message);
    }

    // eval/arguments assignment tests
    // Note: In strict mode, 'eval' and 'arguments' cannot be used as assignment targets
    // This is checked during parsing when we see `eval = ...` or `arguments = ...`
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_AssignToEval_ThrowsSyntaxError(ExecutionMode mode)
    {
        // Need to have eval as a variable first, then reassign it
        var source = """
            "use strict";
            let x = 0;
            eval = 1;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("eval or arguments", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_AssignToArguments_ThrowsSyntaxError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            let x = 0;
            arguments = 1;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("eval or arguments", ex.Message);
    }

    // Legacy octal literal tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LegacyOctalLiteral_ThrowsSyntaxError(ExecutionMode mode)
    {
        var source = """
            const x = 0777;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("Legacy octal literals are not allowed", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ModernOctalLiteral_Works(ExecutionMode mode)
    {
        var source = """
            const x = 0o777;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("511\n", output); // 0o777 = 511 in decimal
    }

    // Octal escape sequence tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OctalEscapeSequence_ThrowsSyntaxError(ExecutionMode mode)
    {
        // \1 is an octal escape - not allowed
        var source = "const x = \"\\1\";";

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("SyntaxError", ex.Message);
        Assert.Contains("Octal escape sequences are not allowed", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullEscape_WithoutFollowingDigit_Works(ExecutionMode mode)
    {
        // \0 is allowed when not followed by a digit
        var source = """
            const x = "a\0b";
            console.log(x.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output); // "a" + null char + "b" = 3 characters
    }

    // Getter-only property tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_WriteToGetterOnly_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const obj = {
                get x() { return 1; }
            };
            obj.x = 2;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
        Assert.Contains("getter", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SloppyMode_WriteToGetterOnly_SilentlyFails(ExecutionMode mode)
    {
        var source = """
            const obj = {
                get x() { return 1; }
            };
            obj.x = 2;
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output); // Getter still returns 1, write was silently ignored
    }

    // Delete from frozen/sealed object tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_DeleteFromFrozen_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const obj = Object.freeze({ x: 1 });
            delete obj.x;
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
        Assert.Contains("Cannot delete property", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SloppyMode_DeleteFromFrozen_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            const obj = Object.freeze({ x: 1 });
            const result = delete obj.x;
            console.log(result);
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("false", output);
        Assert.Contains("1", output);
    }

    // Strict mode inheritance tests
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UseStrict_ArrowFunction_InheritsStrictMode(ExecutionMode mode)
    {
        var source = """
            "use strict";
            const f = () => {
                const obj = Object.freeze({ x: 1 });
                obj.x = 2;
            };
            f();
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("TypeError", ex.Message);
    }
}
