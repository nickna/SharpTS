using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Cross-mode regressions for issue #1382 Phase 2 language-environment parity.
/// </summary>
public sealed class Issue1382Phase2ParityTests
{
    [Theory, ModeData]
    public void Strict_functions_and_direct_eval_bind_this_correctly(ExecutionMode mode)
    {
        const string source = """
            var globalObject: any = this;
            function sloppyThis(): any { return this; }
            function strictThis(): any {
              "use strict"
              return this;
            }
            function strictEvalInSloppyCaller(): boolean {
              return eval('"use strict"; this;') === this;
            }
            console.log(sloppyThis() === globalObject);
            console.log(strictThis() === undefined);
            console.log(strictEvalInSloppyCaller());
            """;

        Assert.Equal("true\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Eval_preserves_lexical_environment_declarations_and_completion(ExecutionMode mode)
    {
        const string source = """
            function localEval(): void {
              var value: any = "before";
              var initial: any;
              console.log(eval("value"));
              eval("var value = 'after'");
              console.log(value);
              eval("initial = local; function local() { return 33; }");
              console.log(initial());
            }
            localEval();
            var globalValue: any = 0;
            console.log((0, eval)("globalValue = 2"));
            console.log(globalValue);
            console.log((0, eval)("{}") === undefined);
            """;

        Assert.Equal("before\nafter\n33\n2\n2\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Async_bodies_materialize_nested_function_declarations(ExecutionMode mode)
    {
        const string source = """
            async function outer(): Promise<void> {
              function inner(): number { return 42; }
              console.log(inner());
            }
            outer();
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Block_and_catch_bindings_shadow_function_parameters(ExecutionMode mode)
    {
        const string source = """
            function shadow(value: any): void {
              {
                let value: any = 2;
                console.log(value);
              }
              try { throw "caught"; }
              catch (value) {
                console.log(value);
                value = 3;
                console.log(value);
              }
              console.log(value);
            }
            shadow(1);
            """;

        Assert.Equal("2\ncaught\n3\n1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Strict_delete_rejects_nonconfigurable_arguments_index(ExecutionMode mode)
    {
        const string source = """
            function inspect(value: any): void {
              Object.defineProperty(arguments, "0", { configurable: false });
              var args: any = arguments;
              try {
                (function(): void { "use strict"; delete args[0]; })();
              } catch (error) {
                console.log(error instanceof TypeError);
              }
              console.log(value, arguments[0]);
            }
            inspect(1);
            """;

        Assert.Equal("true\n1 1\n", TestHarness.Run(source, mode));
    }
}
