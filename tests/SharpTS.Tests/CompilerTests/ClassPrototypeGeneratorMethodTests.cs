using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassPrototypeGeneratorMethodTests
{
    [Fact]
    public void Class_prototype_is_stable_and_exposes_instance_methods()
    {
        const string source = """
            class C {
              method() { return "plain"; }
              async asyncMethod() { return "async"; }
              *generatorMethod() { yield "generator"; }
            }
            const first: any = C.prototype;
            const second: any = C.prototype;
            console.log(first === second);
            console.log(first.method());
            first.asyncMethod().then((value: any) => console.log(value));
            console.log(first.generatorMethod().next().value);
            """;

        Assert.Equal("true\nplain\nasync\ngenerator\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Class_prototype_does_not_run_constructor_and_has_base_prototype()
    {
        const string source = """
            let calls = 0;
            class Base { base() { return "base"; } }
            class Derived extends Base {
              constructor() { super(); calls++; }
              own() { return "own"; }
            }
            const prototype: any = Derived.prototype;
            console.log(calls);
            console.log(prototype.own());
            console.log(prototype.base());
            console.log(Object.getPrototypeOf(prototype) === Base.prototype);
            """;

        Assert.Equal("0\nown\nbase\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Class_prototype_skips_user_field_initializers_through_inheritance()
    {
        const string source = """
            let effects = 0;
            class Base {
              field: number = effects++;
              constructor() { effects += 10; }
              base() { return "base"; }
            }
            class Derived extends Base {
              derivedField: number = effects++;
              constructor() { super(); effects += 100; }
              own() { return "own"; }
            }
            const prototype: any = Derived.prototype;
            console.log(effects);
            console.log(prototype.base(), prototype.own());
            """;

        Assert.Equal("0\nbase own\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Class_static_initializers_can_observe_registered_prototype()
    {
        const string source = """
            let constructorCalls = 0;
            class C {
              static observed: any = C.prototype.method();
              constructor() { constructorCalls++; }
              method() { return "prototype"; }
            }
            console.log(C.observed);
            console.log(constructorCalls);
            """;

        Assert.Equal("prototype\n0\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Class_expression_prototype_uses_constructor_free_path()
    {
        const string source = """
            let constructorCalls = 0;
            const C: any = class {
              value: string = "instance";
              constructor() { constructorCalls++; }
              method() { return "prototype"; }
            };
            console.log(C.prototype.method());
            console.log(constructorCalls);
            """;

        Assert.Equal("prototype\n0\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Async_method_default_preserves_explicit_null()
    {
        const string source = """
            let defaults = 0;
            class C {
              async method(value: any = defaults++) { return value; }
            }
            C.prototype.method(null).then((value: any) => {
              console.log(value === null);
              console.log(defaults);
            });
            """;

        Assert.Equal("true\n0\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Sync_method_default_preserves_explicit_null()
    {
        const string source = """
            let defaults = 0;
            class C {
              method(value: any = defaults++) { return value; }
            }
            const value: any = C.prototype.method(null);
            console.log(value === null);
            console.log(defaults);
            """;

        Assert.Equal("true\n0\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Generator_method_defaults_run_at_call_and_enforce_parameter_tdz()
    {
        const string source = """
            let second: any = "outer";
            class C {
              *abrupt(value: any = (() => { throw new Error("eager"); })()) { yield value; }
              *tdz(first: any = second, second?: any) { yield first; }
            }
            try { C.prototype.abrupt(); } catch (error) { console.log(error.message); }
            try { C.prototype.tdz(); } catch (error) {
              console.log(error.name);
              console.log(error.message);
            }
            """;

        Assert.Equal("eager\nReferenceError\nUndefined variable 'second'.\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Reflected_class_construction_preserves_omitted_argument_as_undefined()
    {
        var files = new Dictionary<string, string>
        {
            ["document.cjs"] = """
                class Document {
                  constructor(value: any, options: any) {
                    console.log(value === null);
                    console.log(options === undefined);
                  }
                }
                module.exports = { Document };
                """,
            ["main.cjs"] = """
                const api = require('./document.cjs');
                new api.Document(null);
                """,
        };

        Assert.Equal(
            "true\ntrue\n",
            TestHarness.RunModules(files, "main.cjs", ExecutionMode.Compiled));
    }
}
