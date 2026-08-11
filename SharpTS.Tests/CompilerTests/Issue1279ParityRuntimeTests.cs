using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class Issue1279ParityRuntimeTests
{
    [Fact]
    public void Strict_array_index_writes_observe_accessors_and_in_reports_them()
    {
        const string source = """
            var values: any = [, 2];
            Object.defineProperty(values, "0", {
              get: function() { return this.saved; },
              set: function(value) { this.saved = value; }
            });
            Array.prototype.sort.call(values);
            console.log(values.saved);
            console.log(values[0]);
            console.log("0" in values);
            """;

        Assert.Equal("2\n2\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Promise_resolving_functions_have_builtin_function_semantics()
    {
        const string source = """
            new Promise(function(resolve, reject) {
              console.log(typeof resolve, typeof reject);
              console.log(Object.getPrototypeOf(resolve) === Function.prototype);
              console.log(resolve(42) === undefined);
              console.log(reject("ignored") === undefined);
            });
            """;

        Assert.Equal(
            "function function\ntrue\ntrue\ntrue\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Value_form_promise_statics_construct_through_this_value()
    {
        const string source = """
            var calls = 0;
            class SubPromise extends Promise {
              constructor(executor: any) {
                super(executor);
                calls += 1;
              }
            }
            var resolve: any = Promise.resolve;
            var all: any = Promise.all;
            var resolved: any = resolve.call(SubPromise, 42);
            var combined: any = all.call(SubPromise, []);
            console.log(resolved instanceof SubPromise);
            console.log(combined instanceof SubPromise);
            console.log(calls);
            """;

        Assert.Equal("true\ntrue\n2\n", TestHarness.RunCompiled(source));
    }
}
