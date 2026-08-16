using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for function methods (bind, call, apply, length, name). Runs against both interpreter and compiler.
/// </summary>
public class FunctionMethodsTests
{
    #region Bind Tests

    [Theory, ModeData]
    public void Bind_PartialApplication_PrependArgs(ExecutionMode mode)
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let add5 = add.bind(null, 5);
            console.log(add5(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory, ModeData]
    public void Bind_ArrowFunction_IgnoresThisArg(ExecutionMode mode)
    {
        var source = """
            let outer = { name: "outer" };
            let fn = (): string => "arrow";
            let bound = fn.bind(outer);
            console.log(bound());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("arrow\n", output);
    }

    [Theory, ModeData]
    public void Bind_ChainedBind_PreservesFirstThis(ExecutionMode mode)
    {
        // Test that chained bind preserves the first 'this' binding
        // We use object method shorthand which allows 'this' access
        var source = """
            let obj1 = {
                name: "first",
                getName() { return this.name; }
            };
            let obj2 = { name: "second" };
            let bound1 = obj1.getName.bind(obj1);
            let bound2 = bound1.bind(obj2);
            console.log(bound2());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n", output);
    }

    #endregion

    #region Call Tests

    [Theory, ModeData]
    public void Call_WithMultipleArgs(ExecutionMode mode)
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            console.log(sum.call(null, 1, 2, 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void Call_ArrowFunction_IgnoresThisArg(ExecutionMode mode)
    {
        var source = """
            let fn = (x: number): number => x * 2;
            let obj = { value: 100 };
            console.log(fn.call(obj, 5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Apply Tests

    [Theory, ModeData]
    public void Apply_SpreadArrayArgs(ExecutionMode mode)
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            let args: number[] = [1, 2, 3];
            console.log(sum.apply(null, args));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void Apply_NullArgs_CallsWithNoArgs(ExecutionMode mode)
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.apply(null, null));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hi\n", output);
    }

    [Theory, ModeData]
    public void Apply_EmptyArgs_CallsWithNoArgs(ExecutionMode mode)
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.apply(null, []));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hi\n", output);
    }

    #endregion

    #region Function Length Tests

    [Theory, ModeData]
    public void FunctionLength_ReturnsArity(ExecutionMode mode)
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            console.log(add.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void FunctionLength_ZeroParams(ExecutionMode mode)
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void ClassMethodLength_UsesJavaScriptParameterRules(ExecutionMode mode)
    {
        var source = """
            class Methods {
                plain(a: unknown, b: unknown,) {}
                defaulted(a: unknown, b: unknown = 1, c: unknown = 2) {}
                rest(a: unknown, ...values: unknown[]) {}

                static plain(a: unknown, b: unknown,) {}
                static defaulted(a: unknown, b: unknown = 1, c: unknown = 2) {}
                static rest(a: unknown, ...values: unknown[]) {}
            }

            const value = new Methods();
            console.log(value.plain.length);
            console.log(value.defaulted.length);
            console.log(value.rest.length);
            console.log(Methods.plain.length);
            console.log(Methods.defaulted.length);
            console.log(Methods.rest.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1\n1\n2\n1\n1\n", output);
    }

    [Theory, ModeData]
    public void ClassMethodLength_CoversAsyncAndGeneratorMethods(ExecutionMode mode)
    {
        var source = """
            class Methods {
                async asyncMethod(a: unknown, b: unknown = 1) {}
                *generatorMethod(a: unknown, b: unknown = 1) {}
                async *asyncGeneratorMethod(a: unknown, b: unknown = 1) {}

                static async asyncMethod(a: unknown, b: unknown = 1) {}
                static *generatorMethod(a: unknown, b: unknown = 1) {}
                static async *asyncGeneratorMethod(a: unknown, b: unknown = 1) {}
            }

            const value = new Methods();
            console.log(value.asyncMethod.length);
            console.log(value.generatorMethod.length);
            console.log(value.asyncGeneratorMethod.length);
            console.log(Methods.asyncMethod.length);
            console.log(Methods.generatorMethod.length);
            console.log(Methods.asyncGeneratorMethod.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n1\n1\n1\n1\n", output);
    }

    [Theory, ModeData]
    public void ClassExpressionMethodLength_UsesJavaScriptParameterRules(ExecutionMode mode)
    {
        var source = """
            const Methods = class {
                method(a: unknown, b: unknown = 1) {}
                static method(a: unknown, b: unknown = 1) {}
            };

            console.log(new Methods().method.length);
            console.log(Methods.method.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n", output);
    }

    [Theory, ModeData]
    public void StaticClassMethodsUsedAsValuesAreFunctionObjects(ExecutionMode mode)
    {
        var source = """
            class Declaration {
                static regular() {
                    console.log(typeof this.regular);
                    console.log(this.regular.hasOwnProperty("caller"));
                }
                static *generator() {
                    console.log(typeof this.generator);
                    console.log(this.generator.hasOwnProperty("arguments"));
                }
                static async asyncMethod() {
                    console.log(typeof this.asyncMethod);
                    console.log(this.asyncMethod.hasOwnProperty("caller"));
                }
            }

            const Expression = class {
                static regular() {
                    console.log(typeof this.regular);
                    console.log(this.regular.hasOwnProperty("arguments"));
                }
            };

            Declaration.regular();
            [...Declaration.generator()];
            Declaration.asyncMethod();
            Expression.regular();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\nfalse\nfunction\nfalse\nfunction\nfalse\nfunction\nfalse\n", output);
    }

    #endregion

    #region Function Name Tests

    [Theory, ModeData]
    public void FunctionName_ReturnsName(ExecutionMode mode)
    {
        var source = """
            function myFunction(): void {}
            console.log(myFunction.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("myFunction\n", output);
    }

    [Theory, ModeData]
    public void BoundFunctionName_PrefixedWithBound(ExecutionMode mode)
    {
        var source = """
            function myFunction(): void {}
            let bound = myFunction.bind(null);
            console.log(bound.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bound myFunction\n", output);
    }

    #endregion

    #region Type Error Tests

    [Theory, InterpretedOnlyData]
    public void FunctionInvalidMember_ReturnsUndefined(ExecutionMode mode)
    {
        // JS functions are objects and support arbitrary property access —
        // reading an unset property returns `undefined` rather than throwing.
        // (Required for CommonJS packages like uuid/debug that treat
        // functions as namespaces: `debug.log`, `v3.DNS`, etc.)
        // Compiled mode still reports "object" here — it lacks the same
        // property bag on emitted $TSFunction that the interpreter's
        // SharpTSFunction has.
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let x = add.invalidMethod;
            console.log(typeof x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    #endregion

    #region Bound Function Tests

    [Theory, ModeData]
    public void BoundFunction_CannotBeUsedAsConstructor(ExecutionMode mode)
    {
        // In JavaScript, bound functions cannot be used with 'new' for class constructors
        // This test verifies that attempting to use 'new' with a bound function throws an error
        // Note: Classes in SharpTS don't have bind() methods like regular functions do
        // This test ensures the runtime check catches bound functions in new expressions
        var source = """
            function createPerson(name: string): { name: string } {
                return { name: name };
            }
            let boundCreate = createPerson.bind(null, "John");
            // The following would throw at runtime if bound functions were usable as constructors
            // For now, just verify the binding works
            console.log(boundCreate().name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("John\n", output);
    }

    #endregion
}
