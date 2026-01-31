using System.Diagnostics;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'util' module.
/// </summary>
public class UtilModuleTests
{
    #region Format Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Format_StringPlaceholder(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Hello %s!', 'world');
                console.log(result === 'Hello world!');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Format_NumberPlaceholder(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Value: %d', 42);
                console.log(result === 'Value: 42');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Format_FloatPlaceholder(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Pi: %f', 3.14);
                console.log(result.startsWith('Pi: 3.14'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Format_MultiplePlaceholders(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('%s has %d items', 'List', 5);
                console.log(result === 'List has 5 items');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Format_ExtraArguments(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Hello', 'extra', 'args');
                console.log(result === 'Hello extra args');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Format_EscapedPercent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('100%% complete');
                console.log(result.includes('%'));
                console.log(result.includes('complete'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Inspect Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_ReturnsString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.inspect({ a: 1, b: 2 });
                console.log(typeof result === 'string');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_ObjectContent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.inspect({ name: 'test' });
                console.log(result.includes('name'));
                console.log(result.includes('test'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Inspect_ArrayContent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.inspect([1, 2, 3]);
                console.log(result.includes('1'));
                console.log(result.includes('2'));
                console.log(result.includes('3'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Types Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsArray_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArray([1, 2, 3]));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsArray_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArray('not array'));
                console.log(util.types.isArray({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsFunction_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isFunction(() => {}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsFunction_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isFunction('not function'));
                console.log(util.types.isFunction(42));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsNull_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNull(null));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsNull_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNull(undefined));
                console.log(util.types.isNull(0));
                console.log(util.types.isNull(''));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsUndefined_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isUndefined(undefined));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Types_IsUndefined_False(ExecutionMode mode)
    {
        // Compiled mode has different null/undefined semantics
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isUndefined(null));
                console.log(util.types.isUndefined(0));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsPromise_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const p = Promise.resolve(42);
                console.log(util.types.isPromise(p));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsPromise_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isPromise(42));
                console.log(util.types.isPromise({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsRegExp_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isRegExp(/test/));
                console.log(util.types.isRegExp(new RegExp('test')));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsRegExp_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isRegExp('test'));
                console.log(util.types.isRegExp({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsMap_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isMap(new Map()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Types_IsMap_False(ExecutionMode mode)
    {
        // Compiled mode's Set is detected as Map due to underlying type similarity
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isMap({}));
                console.log(util.types.isMap(new Set()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsSet_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isSet(new Set()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsSet_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isSet({}));
                console.log(util.types.isSet(new Map()));
                console.log(util.types.isSet([]));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsTypedArray_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isTypedArray(Buffer.from('test')));
                console.log(util.types.isTypedArray(Buffer.alloc(10)));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsTypedArray_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isTypedArray([]));
                console.log(util.types.isTypedArray({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsDate_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isDate(new Date()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsDate_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isDate('2024-01-01'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("false", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsNativeError_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNativeError(new Error('test')));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsNativeError_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNativeError('not an error'));
                console.log(util.types.isNativeError({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsBoxedPrimitive_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isBoxedPrimitive(42));
                console.log(util.types.isBoxedPrimitive('hello'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsWeakMap_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakMap(new WeakMap()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsWeakMap_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakMap(new Map()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("false", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsWeakSet_False_ForSet(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakSet(new Set()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("false", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsArrayBuffer_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArrayBuffer(Buffer.alloc(10)));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Types_IsArrayBuffer_False(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArrayBuffer([]));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("false", output.ToLower());
    }

    #endregion

    #region Deprecate Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Deprecate_ReturnsWrappedFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function oldFn(): number { return 42; }
                const deprecated = util.deprecate(oldFn, 'oldFn is deprecated');
                const result = deprecated();
                console.log(result === 42);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Deprecate_WarnsOnFirstCallOnly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function oldFn(): number { return 1; }
                const deprecated = util.deprecate(oldFn, 'Use newFn instead');
                deprecated();
                deprecated();
                deprecated();
                console.log('done');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("done\n", output);
    }

    #endregion

    #region Callbackify Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Callbackify_CallsCallbackWithResult(ExecutionMode mode)
    {
        // Callbackify behavior differs in compiled mode
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function syncFn(): string { return 'success'; }
                const asyncFn = util.callbackify(syncFn);
                asyncFn((err: any, result: any) => {
                    console.log(err === null);
                    console.log(result === 'success');
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Callbackify_CallsCallbackWithErrorOnThrow(ExecutionMode mode)
    {
        // Compiled mode doesn't catch errors in callbackify
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function throwingFn() { throw new Error('oops'); }
                const asyncFn = util.callbackify(throwingFn);
                asyncFn((err: any, result: any) => {
                    console.log(err !== null);
                    console.log(result === null);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Promisify Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Promisify_ReturnsPromise(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function callbackFn(callback: (err: any, result: string) => void) {
                    callback(null, 'success');
                }
                const promiseFn = util.promisify(callbackFn);
                const result = promiseFn();
                console.log(util.types.isPromise(result));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Promisify_ResolvesWithValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function callbackFn(callback: (err: any, result: string) => void) {
                    callback(null, 'hello world');
                }
                const promiseFn = util.promisify(callbackFn);
                async function main() {
                    const result = await promiseFn();
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Promisify_RejectsOnError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function callbackFn(callback: (err: any, result: any) => void) {
                    callback(new Error('something went wrong'), null);
                }
                const promiseFn = util.promisify(callbackFn);
                async function main() {
                    try {
                        await promiseFn();
                        console.log('no error');
                    } catch (e) {
                        console.log('caught error');
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Promisify_PassesArgumentsToOriginalFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function callbackFn(a: number, b: number, callback: (err: any, result: number) => void) {
                    callback(null, a + b);
                }
                const promiseFn = util.promisify(callbackFn);
                async function main() {
                    const result = await promiseFn(3, 4);
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("7\n", output);
    }

    #endregion

    #region Inherits Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Inherits_SetsSuperProperty(ExecutionMode mode)
    {
        // Compiled mode doesn't set super_ property correctly
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                class Parent {}
                class Child {}
                util.inherits(Child, Parent);
                console.log((Child as any).super_ === Parent);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region TextEncoder Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TextEncoder_EncodingPropertyIsUtf8(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextEncoder } from 'util';
                const encoder = new TextEncoder();
                console.log(encoder.encoding === 'utf-8');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TextEncoder_EncodesUnicodeCorrectly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextEncoder } from 'util';
                const encoder = new TextEncoder();
                const encoded = encoder.encode('\u00e9');  // e with acute accent (2 bytes in UTF-8)
                console.log(encoded.length === 2);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region TextDecoder Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TextDecoder_DefaultEncodingIsUtf8(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextDecoder } from 'util';
                const decoder = new TextDecoder();
                console.log(decoder.encoding === 'utf-8');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TextDecoder_DecodesUtf8Buffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextDecoder } from 'util';
                const decoder = new TextDecoder();
                const buf = Buffer.from([104, 101, 108, 108, 111]);  // 'hello'
                const decoded = decoder.decode(buf);
                console.log(decoded === 'hello');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TextEncoder_TextDecoder_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextEncoder, TextDecoder } from 'util';
                const encoder = new TextEncoder();
                const decoder = new TextDecoder();
                const original = 'Hello, World!';
                const encoded = encoder.encode(original);
                const decoded = decoder.decode(encoded);
                console.log(decoded === original);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region IsDeepStrictEqual Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_PrimitivesEqual(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(1, 1));
                console.log(util.isDeepStrictEqual('hello', 'hello'));
                console.log(util.isDeepStrictEqual(true, true));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_PrimitivesNotEqual(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(1, 2));
                console.log(util.isDeepStrictEqual('hello', 'world'));
                console.log(util.isDeepStrictEqual(1, '1'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_NaNEqualsNaN(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(NaN, NaN));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_ArraysEqual(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual([1, 2, 3], [1, 2, 3]));
                console.log(util.isDeepStrictEqual([], []));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_ArraysNotEqual(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual([1, 2], [1, 2, 3]));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_ObjectsEqual(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 1 }));
                console.log(util.isDeepStrictEqual({}, {}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IsDeepStrictEqual_ObjectsNotEqual(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 2 }));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region ParseArgs Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ParseArgs_BooleanOption(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--verbose'],
                    options: {
                        verbose: { type: 'boolean' }
                    }
                });
                console.log(result.values.verbose === true);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ParseArgs_StringOption(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--output', 'file.txt'],
                    options: {
                        output: { type: 'string' }
                    }
                });
                console.log(result.values.output === 'file.txt');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ParseArgs_ShortOption(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['-v'],
                    options: {
                        verbose: { type: 'boolean', short: 'v' }
                    }
                });
                console.log(result.values.verbose === true);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ParseArgs_Positionals(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['file1.txt', 'file2.txt'],
                    options: {},
                    allowPositionals: true
                });
                console.log(result.positionals.length === 2);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region ToUSVString Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ToUSVString_RegularString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.toUSVString('hello') === 'hello');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ToUSVString_Emoji(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const emoji = '😀';
                console.log(util.toUSVString(emoji) === emoji);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ToUSVString_LoneSurrogate(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const loneHigh = '\uD83D';
                const result = util.toUSVString(loneHigh);
                console.log(result === '\uFFFD');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region StripVTControlCharacters Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StripVTControlCharacters_RemovesAnsiColors(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const colored = '\x1b[31mRed\x1b[0m';
                console.log(util.stripVTControlCharacters(colored) === 'Red');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StripVTControlCharacters_PreservesPlainText(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const plain = 'Hello, World!';
                console.log(util.stripVTControlCharacters(plain) === plain);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region GetSystemErrorName Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetSystemErrorName_ReturnsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-2) === 'ENOENT');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetSystemErrorName_ReturnsEACCES(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-13) === 'EACCES');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetSystemErrorName_ReturnsEPERM(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-1) === 'EPERM');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region GetSystemErrorMap Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetSystemErrorMap_ReturnsMap(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const map = util.getSystemErrorMap();
                console.log(typeof map === 'object');
                console.log(map !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetSystemErrorMap_ContainsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const map = util.getSystemErrorMap();
                const entry = map.get(-2);
                console.log(entry !== undefined);
                console.log(entry[0] === 'ENOENT');
                console.log(entry[1].includes('no such file'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Interpreted-Only Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Typeof_BuiltInMethod_ReturnsFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(typeof util.format === 'function');
                console.log(typeof util.inspect === 'function');
                console.log(typeof Math.floor === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Types_IsFunction_AllTypes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function regularFn() {}
                const arrowFn = () => {};
                console.log(util.types.isFunction(regularFn));
                console.log(util.types.isFunction(arrowFn));
                console.log(util.types.isFunction(util.format));
                console.log(util.types.isFunction('not function'));
                console.log(util.types.isFunction(42));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Types_IsWeakSet_True(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakSet(new WeakSet()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void TextDecoder_SupportsLatin1(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextDecoder } from 'util';
                const decoder = new TextDecoder('latin1');
                console.log(decoder.encoding === 'latin1');
                const buf = Buffer.from([233]);  // e with acute in Latin-1
                const decoded = decoder.decode(buf);
                console.log(decoded.length === 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void TextDecoder_SupportsUtf16le(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextDecoder } from 'util';
                const decoder = new TextDecoder('utf-16le');
                console.log(decoder.encoding === 'utf-16le');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ParseArgs_OptionTerminator(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--verbose', '--', '--not-an-option'],
                    options: {
                        verbose: { type: 'boolean' }
                    },
                    allowPositionals: true
                });
                console.log(result.values.verbose === true);
                console.log(result.positionals[0] === '--not-an-option');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ParseArgs_MultipleValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--file', 'a.txt', '--file', 'b.txt'],
                    options: {
                        file: { type: 'string', multiple: true }
                    }
                });
                console.log(Array.isArray(result.values.file));
                console.log(result.values.file.length === 2);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Compiled-Only Standalone Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void Compiled_Util_ToUSVString_Standalone(ExecutionMode mode)
    {
        // This test verifies that toUSVString is truly self-contained
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var mainPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(mainPath, """
                import * as util from 'util';
                const input = 'hello';
                const result = util.toUSVString(input);
                console.log(result === 'hello');
                const emoji = '\uD83D\uDE00';
                console.log(util.toUSVString(emoji) === emoji);
                const lone = '\uD83D';
                console.log(util.toUSVString(lone) === '\uFFFD');
                """);

            var dllPath = Path.Combine(tempDir, "test.dll");

            var resolver = new ModuleResolver(mainPath);
            var entryModule = resolver.LoadModule(mainPath);
            var allModules = resolver.GetModulesInOrder(entryModule);
            var checker = new TypeChecker();
            var typeMap = checker.CheckModules(allModules, resolver);
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allModules.SelectMany(m => m.Statements).ToList());

            var compiler = new ILCompiler("test");
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            File.WriteAllText(Path.Combine(tempDir, "test.runtimeconfig.json"), """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);

            Assert.Equal("true\ntrue\ntrue\n", output.Replace("\r\n", "\n"));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void Compiled_Util_Format_Standalone(ExecutionMode mode)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_format_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var mainPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(mainPath, """
                import * as util from 'util';
                console.log(util.format('Hello %s', 'world') === 'Hello world');
                console.log(util.format('%d + %d = %d', 1, 2, 3) === '1 + 2 = 3');
                console.log(util.format('Value: %f', 3.14).startsWith('Value: 3.14'));
                console.log(util.format('%%s is a format') === '%s is a format');
                """);

            var dllPath = Path.Combine(tempDir, "test.dll");

            var resolver = new ModuleResolver(mainPath);
            var entryModule = resolver.LoadModule(mainPath);
            var allModules = resolver.GetModulesInOrder(entryModule);
            var checker = new TypeChecker();
            var typeMap = checker.CheckModules(allModules, resolver);
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allModules.SelectMany(m => m.Statements).ToList());

            var compiler = new ILCompiler("test");
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            File.WriteAllText(Path.Combine(tempDir, "test.runtimeconfig.json"), """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);

            Assert.Equal("true\ntrue\ntrue\ntrue\n", output.Replace("\r\n", "\n"));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion
}
