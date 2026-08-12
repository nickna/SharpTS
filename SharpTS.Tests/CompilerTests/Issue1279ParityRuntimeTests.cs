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
    public void Borrowed_string_substring_coerces_receiver_and_forwards_arguments()
    {
        const string source = """
            var value: any = [1, 2, 3, 4, 5];
            var substring: any = String.prototype.substring;
            var called: any = substring.call(value, true, false);
            value.substring = substring;
            console.log(typeof value.substring, value.substring === substring, value.substring === value);
            var borrowed: any = value.substring("4", "5");
            console.log(typeof called, called, called === value);
            console.log(typeof borrowed, borrowed, borrowed === value);
            """;

        Assert.Equal("function true false\nstring 1 false\nstring 3 false\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Function_prototype_borrowed_substring_forwards_arguments()
    {
        const string source = """
            function value() {}
            var substring: any = String.prototype.substring;
            Function.prototype.substring = substring;
            console.log(typeof value.substring, value.substring === substring);
            var result: any = value.substring(null, Function());
            console.log(typeof result, result, result === value);
            """;

        Assert.Equal("function true\nstring  false\n", TestHarness.RunCompiled(source));
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

    [Fact]
    public void String_well_formed_methods_replace_only_unpaired_surrogates()
    {
        const string source = """
            console.log("\uD800\uDC00".isWellFormed());
            console.log("x\uD800y".isWellFormed(), "x\uDC00y".isWellFormed());
            var repaired = "\uD800A\uDC00".toWellFormed();
            console.log(repaired.charCodeAt(0), repaired.charCodeAt(1), repaired.charCodeAt(2));
            var receiver: any = { toString: function() { return "ok"; } };
            console.log(String.prototype.isWellFormed.call(receiver));
            """;

        Assert.Equal("true\nfalse false\n65533 65 65533\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Property_descriptors_control_assignment_on_dynamic_objects()
    {
        const string source = """
            var value: any = { fixed: 1, observed: 0 };
            Object.defineProperty(value, "fixed", { value: 1, writable: false, configurable: true });
            Object.defineProperty(value, "sink", {
                set: function(next: any) { this.observed = next; }
            });
            value.fixed = 2;
            value.sink = 3;
            console.log(value.fixed, value.observed);
            """;

        Assert.Equal("1 3\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void String_symbol_iterator_yields_unicode_code_points()
    {
        const string source = """
            var text: any = "A\uD83D\uDE00";
            var iteratorMethod: any = text[Symbol.iterator];
            var iterator: any = iteratorMethod.call(text);
            var first: any = iterator.next();
            var second: any = iterator.next();
            var done: any = iterator.next();
            console.log(first.value, first.done);
            console.log(second.value.length, second.done);
            console.log(done.value, done.done);
            """;

        Assert.Equal("A false\n2 false\nundefined true\n", TestHarness.RunCompiled(source));
    }
}
