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

    [Fact]
    public void Native_error_constructors_inherit_from_error_constructor()
    {
        const string source = """
            console.log(Object.getPrototypeOf(TypeError) === Error);
            console.log(Object.getPrototypeOf(RangeError) === Error);
            console.log(Object.getPrototypeOf(Error) === Function.prototype);
            """;

        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Error_instances_preserve_own_message_and_cause_descriptors()
    {
        const string source = """
            var error: any = new Error("boom", { cause: 42 });
            var message = Object.getOwnPropertyDescriptor(error, "message");
            var cause = Object.getOwnPropertyDescriptor(error, "cause");
            console.log(message.value, message.writable, message.enumerable, message.configurable);
            console.log(cause.value, cause.writable, cause.enumerable, cause.configurable);
            error["message"] = "changed";
            console.log(error.message, Object.getOwnPropertyDescriptor(error, "message").value);
            console.log(delete error["cause"], Object.hasOwn(error, "cause"));
            console.log(Object.hasOwn(new Error(), "message"));
            var nullValue: any = null;
            var nullMessage: any = new Error(nullValue);
            console.log(nullMessage.message, Object.hasOwn(nullMessage, "message"));
            """;

        Assert.Equal(
            "boom true false true\n42 true false true\nchanged changed\ntrue false\nfalse\nnull true\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void String_positions_apply_to_number_before_integer_conversion()
    {
        const string source = """
            var numericString: any = "1";
            var nullPosition: any = null;
            var objectPosition: any = { toString: function() { return 2; } };
            console.log("abcd".charCodeAt(numericString));
            console.log("abcd".charCodeAt(nullPosition));
            console.log("abcd".charCodeAt(objectPosition));
            console.log("abcd".at(numericString));
            """;

        Assert.Equal("98\n97\n99\nb\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Generic_array_mutators_operate_on_the_original_array_like()
    {
        const string source = """
            var value: any = { 0: "b", 1: "c", length: 2 };
            Array.prototype.unshift.call(value, "a");
            Array.prototype.push.call(value, "d");
            Array.prototype.copyWithin.call(value, 1, 2);
            Array.prototype.fill.call(value, "x", 2, 3);
            Array.prototype.reverse.call(value);
            console.log(value.length, value[0], value[1], value[2], value[3]);
            console.log(Array.prototype.shift.call(value));
            console.log(Array.prototype.pop.call(value), value.length);
            """;

        Assert.Equal("4 d x c a\nd\na 2\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Top_level_var_is_initialized_to_undefined_before_its_declaration()
    {
        const string source = """
            console.log(typeof later, String(later), "x".concat(later));
            var later;
            """;

        Assert.Equal("undefined undefined xundefined\n", TestHarness.RunCompiled(source));
    }
}
