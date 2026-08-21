using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Regression coverage for #1429: optimized emitted object carriers must retain
/// ordinary-object semantics when they cross descriptor, Array, spread, JSON,
/// and RegExp protocol boundaries.
/// </summary>
public sealed class Issue1429RuntimeBoundaryTests
{
    [Fact]
    public void Null_getter_and_setter_descriptor_values_are_rejected()
    {
        const string source = """
            var target: any = {};
            var getterThrew = false;
            var setterThrew = false;
            try { Object.defineProperty(target, "getter", { get: null }); }
            catch (error) { getterThrew = error instanceof TypeError; }
            try { Object.defineProperty(target, "setter", { set: null }); }
            catch (error) { setterThrew = error instanceof TypeError; }
            console.log(getterThrew, setterThrew);
            """;

        Assert.Equal("true true\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Generic_array_search_observes_index_added_during_from_index_coercion()
    {
        const string source = """
            var value: any = { length: 30 };
            var target: any = function() {};
            var fromIndex: any = {
              valueOf: function() {
                value[4] = target;
                return 3;
              }
            };
            console.log(Array.prototype.indexOf.call(value, target, fromIndex));
            """;

        Assert.Equal("4\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Generic_array_iteration_uses_live_accessors_and_stops_at_the_specified_point()
    {
        const string source = """
            JSON.stringify({ probe: 1 });
            var touched = false;
            var everyTarget: any = { length: 3 };
            Object.defineProperty(everyTarget, "0", { get: function() { return 11; } });
            Object.defineProperty(everyTarget, "1", { get: function() { return 8; } });
            Object.defineProperty(everyTarget, "2", { get: function() { touched = true; return 8; } });
            var everyResult = Array.prototype.every.call(everyTarget, function(value) {
              return value > 10;
            });

            var setterThrew = false;
            var fillTarget: any = { length: 1 };
            Object.defineProperty(fillTarget, "0", {
              set: function() { throw new Error("setter"); }
            });
            try { Array.prototype.fill.call(fillTarget, 1); }
            catch (error) { setterThrew = error instanceof Error; }
            console.log(everyResult, touched, setterThrew);
            """;

        Assert.Equal("false false true\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Prevent_extensions_still_allows_writes_to_existing_carrier_fields()
    {
        const string source = """
            var target: any = { value: 12 };
            Object.preventExtensions(target);
            target.value = -1;
            target.added = 2;
            console.log(target.value, Object.prototype.hasOwnProperty.call(target, "added"));
            """;

        Assert.Equal("-1 false\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Object_spread_copies_fields_from_emitted_object_carriers()
    {
        const string source = """
            var source: any = { c: 3, d: 4 };
            var copy: any = { ...source };
            console.log(Object.keys(copy).join(","), copy.c, copy.d);
            """;

        Assert.Equal("c,d 3 4\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Json_replacer_receives_the_original_object_as_holder()
    {
        const string source = """
            var child: any = { value: 1 };
            var source: any = { child: child };
            var sameHolder = false;
            JSON.stringify(source, function(key, value) {
              if (key === "child") sameHolder = this === source;
              return value;
            });
            console.log(sameHolder);
            """;

        Assert.Equal("true\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void RegExp_search_restore_throws_for_a_non_writable_last_index()
    {
        const string source = """
            var calls = 0;
            var poisoned: any = {
              get lastIndex() { return (this as any).lastIndex_; },
              set lastIndex(value) {
                if (calls === 1) throw new Error("poisoned");
                (this as any).lastIndex_ = value;
              },
              exec: function() {
                calls += 1;
                return null;
              }
            };
            var receiver: any = {
              exec: function() {
                Object.defineProperty(receiver, "lastIndex", { writable: false });
                calls += 1;
                return null;
              }
            };
            function captures(expected: any, callback: any): boolean {
              try { callback(); }
              catch (error) { return error instanceof expected; }
              return false;
            }
            var poisonedThrew = captures(Error, function() {
              RegExp.prototype[Symbol.search].call(poisoned);
            });
            calls = 0;
            var nonWritableThrew = captures(TypeError, function() {
              RegExp.prototype[Symbol.search].call(receiver);
            });
            console.log(poisonedThrew, nonWritableThrew, calls);
            """;

        Assert.Equal("true true 1\n", TestHarness.RunCompiled(source));
    }
}
