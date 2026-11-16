using SharpTS.Tests.Infrastructure;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed partial class WithStatementTests
{
    public const string StandaloneSource = """
        let read;
        const object = { value: 2, hidden: 3 };
        const hidden = 9;
        object[Symbol.unscopables] = { hidden: true };
        with (object) { value += hidden; read = () => value; }
        function* values() {
            const value = 1;
            yield value;
            with (object) yield value;
            yield value;
        }
        console.log(read(), [...values()].join(','));
        try { with (null) {} } catch (error) { console.log(error instanceof TypeError); }
        """;
    public const string StandaloneExpected = "11 1,11,1\ntrue\n";

    [Theory, ModeData]
    public void With_StandaloneScenario(ExecutionMode mode) =>
        Assert.Equal(StandaloneExpected, TestHarness.RunUnchecked(StandaloneSource, mode));

    [Theory, ModeData]
    public void With_ResolvesLivePropertiesAndPreservesOuterBindings(ExecutionMode mode)
    {
        const string source = """
            let value = 1;
            const object = { value: 2 };
            with (object) {
                console.log(value);
                value += 3;
                console.log(value++, ++value);
                value &&= 8;
                console.log(value);
                console.log(delete value, value);
                value = 9;
                console.log(typeof missing);
            }
            console.log(value, object.value);
            """;
        Assert.Equal("2\n5 7\n8\ntrue 1\nundefined\n9 undefined\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_UsesPrototypesUnscopablesAndMethodReceivers(ExecutionMode mode)
    {
        const string source = """
            const hidden = 'outer';
            const proto = { inherited: 4 };
            const object = Object.create(proto);
            object.hidden = 'inner';
            object[Symbol.unscopables] = { hidden: true };
            object.method = function (n) { return this === object ? n : -1; };
            with (object) {
                console.log(inherited, hidden, method(7), (method)(8));
                console.log(method?.(9));
            }
            object[Symbol.unscopables] = true;
            with (object) console.log(hidden);
            """;
        Assert.Equal("4 outer 7 8\n9\ninner\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_KeepsLexicalShadowingAndCapturedObjectLifetimes(ExecutionMode mode)
    {
        const string source = """
            let read;
            let write;
            let outer = 1;
            const object = { outer: 2, parameter: 99, local: 99 };
            with (object) {
                read = () => outer;
                write = function (parameter) { var local = 3; outer = parameter + local; };
                { const outer = 8; console.log(outer); }
                try { throw 7; } catch (outer) { console.log(outer); }
                with ({ outer: 4 }) console.log(outer);
                console.log(outer);
            }
            write(5);
            console.log(read(), object.outer, outer);
            """;
        Assert.Equal("8\n7\n4\n2\n8 8 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_FunctionDefaultsAndMethodNamesUseOuterObjectEnvironment(ExecutionMode mode)
    {
        const string source = """
            let read;
            with ({ value: 42, method: 8 }) {
                read = function (argument = value) { var value = 7; return argument; };
                class C { method() { return method; } }
                console.log(new C().method());
            }
            console.log(read());
            """;
        Assert.Equal("8\n42\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_ClassStaticBlockVarBindingsRemainLocal(ExecutionMode mode)
    {
        const string source = """
            const object = { value: 42 };
            with (object) {
                class C { static { { var value = 1; } console.log(value); } }
            }
            console.log(object.value);
            """;
        Assert.Equal("1\n42\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_PreservesGeneratorAndAsyncSuspension(ExecutionMode mode)
    {
        const string source = """
            function* values() {
                var value = 1;
                yield value;
                with ({ value: 2 }) { yield value; yield value + 1; }
                yield value;
            }
            console.log([...values()].join(','));
            function* resumed() {
                with ({ value: 2 }) { try { value += yield 5; return value; } catch (e) { return 'bad'; } }
            }
            const generator = resumed();
            console.log(generator.next().value, generator.next(3).value);
            function* logical() { with ({ value: 0 }) { value ||= yield 8; return value; } }
            const logicalGenerator = logical();
            console.log(logicalGenerator.next().value, logicalGenerator.next(9).value);
            async function task() {
                let value = 1;
                with ({ value: 4 }) {
                    try { value += await Promise.resolve(2); console.log(value); } catch (e) { console.log('bad'); }
                }
                console.log(value);
            }
            task();
            """;
        Assert.Equal("1,2,3,1\n5 5\n8 9\n6\n1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AwaitedDestructuringInsideTry_TracksSuspensionInLoweredStatements(ExecutionMode mode)
    {
        // The same lowered-expression traversal used by with assignment temporaries
        // must retain suspension and variable liveness for ordinary destructuring (#3700).
        const string source = """
            const run = async () => {
                let value;
                try { [value] = await Promise.resolve([7]); console.log(value); }
                catch (error) { console.log('bad'); }
            };
            run();
            """;
        Assert.Equal("7\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_AsyncArrowAssignmentPreservesSuspensionAndFallbackStorage(ExecutionMode mode)
    {
        const string source = """
            const run = async () => {
                with ({ value: 1 }) {
                    try { value += await Promise.resolve(2); console.log(value); }
                    catch (error) { console.log('bad'); }
                }
            };
            run();
            """;
        Assert.Equal("3\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_ThrowingSetterAfterYieldReachesEnclosingCatch(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            const object = { get value() { return 1; }, set value(v) { throw marker; } };
            function* run() {
                try { with (object) { value += yield 5; } }
                catch (error) { return error === marker; }
            }
            const generator = run();
            console.log(generator.next().value);
            console.log(generator.next(2).value);
            """;
        Assert.Equal("5\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_SimpleAndLogicalSuspendedAssignmentsRetainSelectedEnvironment(ExecutionMode mode)
    {
        const string source = """
            const object = { value: 1 };
            function* run() {
                with (object) {
                    value = yield 5;
                    value &&= yield 6;
                    return value;
                }
            }
            const generator = run();
            console.log(generator.next().value, generator.next(2).value, generator.next(3).value, object.value);
            const marker = {};
            const throwing = { get value() { return 1; }, set value(v) { throw marker; } };
            async function task() {
                with (object) { value = await Promise.resolve(4); console.log(value); }
                try { with (throwing) { value = await Promise.resolve(7); } }
                catch (error) { console.log(error === marker); }
                try { with (throwing) { value &&= await Promise.resolve(8); } }
                catch (error) { console.log(error === marker); }
            }
            task();
            """;
        Assert.Equal("5 6 3 3\n4\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_ResolvesVarInitializersAndLoopAssignmentHeads(ExecutionMode mode)
    {
        const string source = """
            function check() {
                var value = 1;
                const object = { value: 2 };
                with (object) {
                    var value = 3;
                    for (value of [4, 5]) console.log(value);
                    for (var value in { a: 1 }) console.log(value);
                    for (let value of [6]) console.log(value);
                }
                console.log(value, object.value);
            }
            check();
            """;
        Assert.Equal("4\n5\na\n6\n1 a\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_UnbracedVarStatementRetainsClosureScope(ExecutionMode mode)
    {
        const string source = """
            var probe;
            with ({ value: 0 }) var value = 1, ignored = probe = function () { return value; };
            var value = 2;
            console.log(probe(), value);
            """;
        Assert.Equal("1 2\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_ObjectExpressionRunsBeforeEnteringObjectEnvironment(ExecutionMode mode)
    {
        const string source = """
            var value = 0;
            const read = function () { return value; };
            with (eval('var value = 1;'), { value: 2 }) console.log(value);
            console.log(read(), value);
            """;
        Assert.Equal("2\n1 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_NestedUpdateReadsUnscopablesOnlyOnce(ExecutionMode mode)
    {
        const string source = """
            var calls = 0;
            var a, b, flag = true;
            with (a = { x: 7 }) {
                with (b = { x: 4, get [Symbol.unscopables]() { calls++; return { x: flag = !flag }; } }) {
                    x++;
                }
            }
            console.log(calls, a.x, b.x);
            """;
        Assert.Equal("1 7 5\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_UsingBindingsShadowTheObjectAndDisposeNormally(ExecutionMode mode)
    {
        const string source = """
            const object = { resource: { [Symbol.dispose]() { console.log('disposed'); } }, local: 99 };
            with (object) { using local = resource; console.log(local === object.resource); }
            console.log(object.local);
            """;
        Assert.Equal("true\ndisposed\n99\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_BoxesPrimitivesAndRejectsNullishObjects(ExecutionMode mode)
    {
        const string source = """
            with ('abc') console.log(length, valueOf());
            with (7) console.log(valueOf());
            with (true) console.log(valueOf());
            try { with (null) console.log('bad'); } catch (e) { console.log(e instanceof TypeError); }
            try { with (undefined) console.log('bad'); } catch (e) { console.log(e instanceof TypeError); }
            """;
        Assert.Equal("3 abc\n7\ntrue\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_ResolvesReferenceBeforeRightHandSideAndArguments(ExecutionMode mode)
    {
        const string source = """
            let value = 1;
            const object = { value: 2, method: function (n) { return this === object ? n : -1; } };
            function change() { delete object.value; return 7; }
            with (object) value = change();
            console.log(value, object.value);
            let calls = 0;
            const proxy = new Proxy(object, {
                has(target, key) { if (key === 'value') { calls++; } return key in target; }
            });
            with (proxy) value += 1;
            console.log(calls, object.value);
            """;
        // ECMA-262 keeps the resolved reference across RHS evaluation. GetBindingValue
        // and SetMutableBinding each perform another HasProperty without @@unscopables.
        // V8 currently differs here; Test262 assignment/S11.13.1_A5_T1.js pins this rule.
        Assert.Equal("1 7\n3 8\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void With_StrictClosuresRejectBindingsDeletedAfterResolution(ExecutionMode mode)
    {
        const string source = """
            const object = { value: 2 };
            let read;
            let write;
            with (object) {
                read = function () { 'use strict'; return value; };
                write = function () { 'use strict'; value = (delete object.value, 7); };
            }
            try { write(); } catch (e) { console.log(e instanceof ReferenceError); }
            object.value = 3;
            Object.defineProperty(object, Symbol.unscopables, { get() { delete this.value; return {}; } });
            try { read(); } catch (e) { console.log(e instanceof ReferenceError); }
            """;
        Assert.Equal("true\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData("'use strict'; with ({}) {}")]
    [InlineData("'use strict'\nwith ({}) {}")]
    [InlineData("function f() { 'use strict'\nwith ({}) {} }")]
    [InlineData("class C { static { with ({}) {} } }")]
    [InlineData("const C = class { static { with ({}) {} } };")]
    [InlineData("function f() { 'use strict'; with ({}) {} }")]
    [InlineData("class C { method() { with ({}) {} } }")]
    [InlineData("const C = class { method() { with ({}) {} } };")]
    [InlineData("with ({}) {} export {};")]
    [InlineData("export {}; function f() { with ({}) {} }")]
    public void With_IsRejectedInStrictContexts(string source)
    {
        Assert.ThrowsAny<Exception>(() => TestHarness.ParseOrThrow(source));
    }

    [Theory]
    [InlineData("class C { [(function () { with ({}) {} return 'm'; })()]() {} }")]
    [InlineData("class C { static [(function () { with ({}) {} return 'm'; })()]() {} }")]
    [InlineData("const C = class { [(function () { with ({}) {} return 'm'; })()]() {} };")]
    [InlineData("const C = class { static [(function () { with ({}) {} return 'm'; })()]() {} };")]
    [InlineData("class C { [(function () { with ({}) {} return 'x'; })()] = 1; }")]
    [InlineData("class C { static [(function () { with ({}) {} return 'x'; })()] = 1; }")]
    [InlineData("const C = class { [(function () { with ({}) {} return 'x'; })()] = 1; };")]
    [InlineData("const C = class { static [(function () { with ({}) {} return 'x'; })()] = 1; };")]
    [InlineData("class C extends (function () { with ({}) {} return Object; })() {}")]
    [InlineData("const C = class extends (function () { with ({}) {} return Object; })() {};")]
    [InlineData("class C { accessor x = (function () { with ({}) {} return 1; })(); }")]
    [InlineData("class C { static accessor x = (function () { with ({}) {} return 1; })(); }")]
    [InlineData("const C = class { accessor x = (function () { with ({}) {} return 1; })(); };")]
    [InlineData("const C = class { static accessor x = (function () { with ({}) {} return 1; })(); };")]
    public void With_IsRejectedInClassDefinitionExpressions(string source)
    {
        var result = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS1101");
    }

    [Fact]
    public void With_RemainsAllowedInSloppyObjectMethodKeys()
    {
        const string source = "const value = { [(function () { with ({}) {} return 'm'; })()]() {} };";
        Assert.True(new Parser(new Lexer(source).ScanTokens()).Parse().IsSuccess);
    }

    [Theory]
    [InlineData("main.mjs")]
    [InlineData("main.mts")]
    public void With_IsRejectedByExplicitModuleFileGoal(string path)
    {
        var result = new Parser(new Lexer("with ({}) {}").ScanTokens()).WithFilePath(path).Parse();
        Assert.False(result.IsSuccess);
    }
}
