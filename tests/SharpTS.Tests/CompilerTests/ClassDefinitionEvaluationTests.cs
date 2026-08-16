using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassDefinitionEvaluationTests
{
    [Fact]
    public void Function_local_class_evaluates_static_fields_at_declaration_site()
    {
        const string source = """
            function throwMarker() { throw new Error("marker"); }
            function defineClass() {
              class C {
                static value = throwMarker();
              }
            }
            try {
              defineClass();
            } catch (error) {
              console.log(error.message);
            }
            """;

        Assert.Equal("marker\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Class_declaration_computed_methods_dispatch_by_evaluated_key()
    {
        const string source = """
            class C {
              [1 + 1]() { return "instance"; }
              static [1 + 1]() { return "static"; }
            }
            const instance: any = new C();
            const constructor: any = C;
            console.log(instance[2]());
            console.log(constructor["2"]());
            """;

        Assert.Equal("instance\nstatic\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Function_local_class_binding_can_be_constructed()
    {
        const string source = """
            function makeValue() {
              class C {
                [1 + 1]() { return "local"; }
              }
              const instance: any = new C();
              return instance[2]();
            }
            console.log(makeValue());
            """;

        Assert.Equal("local\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Class_expression_computed_methods_dispatch_by_evaluated_key()
    {
        const string source = """
            const C: any = class {
              [1 + 1]() { return "instance"; }
              static [1 + 1]() { return "static"; }
            };
            const instance: any = new C();
            console.log(instance[2]());
            console.log(C["2"]());
            """;

        Assert.Equal("instance\nstatic\n", TestHarness.RunCompiled(source));
    }
}
