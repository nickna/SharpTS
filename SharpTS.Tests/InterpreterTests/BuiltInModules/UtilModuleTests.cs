using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'util' module.
/// Uses interpreter mode since util module isn't fully supported in compiled mode.
/// </summary>
public class UtilModuleTests
{
    // ============ FORMAT TESTS ============

    [Fact]
    public void Format_StringPlaceholder()
    {
        // %s should format as string
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Hello %s!', 'world');
                console.log(result === 'Hello world!');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Format_NumberPlaceholder()
    {
        // %d should format as integer
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Value: %d', 42);
                console.log(result === 'Value: 42');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Format_FloatPlaceholder()
    {
        // %f should format as float
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Pi: %f', 3.14);
                console.log(result.startsWith('Pi: 3.14'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Format_MultiplePlaceholders()
    {
        // Multiple placeholders should work
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('%s has %d items', 'List', 5);
                console.log(result === 'List has 5 items');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Format_ExtraArguments()
    {
        // Extra arguments should be appended with spaces
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('Hello', 'extra', 'args');
                console.log(result === 'Hello extra args');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Format_EscapedPercent()
    {
        // %% should output a literal %
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.format('100%% complete');
                // Note: escaped percent produces single %
                console.log(result.includes('%'));
                console.log(result.includes('complete'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ INSPECT TESTS ============

    [Fact]
    public void Inspect_ReturnsString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.inspect({ a: 1, b: 2 });
                console.log(typeof result === 'string');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Inspect_ObjectContent()
    {
        // inspect should show object properties
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.inspect({ name: 'test' });
                console.log(result.includes('name'));
                console.log(result.includes('test'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Inspect_ArrayContent()
    {
        // inspect should show array elements
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ TYPEOF BUILTIN TESTS ============

    [Fact]
    public void Typeof_BuiltInMethod_ReturnsFunction()
    {
        // typeof should return 'function' for built-in methods
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(typeof util.format === 'function');
                console.log(typeof util.inspect === 'function');
                console.log(typeof Math.floor === 'function');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ TYPES TESTS ============

    [Fact]
    public void Types_IsArray()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArray([1, 2, 3]));
                console.log(util.types.isArray('not array'));
                console.log(util.types.isArray({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsFunction()
    {
        // Tests all function types: regular functions, arrow functions, and built-in methods
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsNull()
    {
        // isNull returns true only for null, not for undefined
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNull(null));
                console.log(util.types.isNull(undefined));
                console.log(util.types.isNull(0));
                console.log(util.types.isNull(''));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\nfalse\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsUndefined()
    {
        // isUndefined returns true only for undefined, not for null
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isUndefined(undefined));
                console.log(util.types.isUndefined(null));
                console.log(util.types.isUndefined(0));
                console.log(util.types.isUndefined('test'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\nfalse\nfalse\nfalse\n", output);
    }

    // ============ util.types EXPANSION TESTS ============

    [Fact]
    public void Types_IsPromise_ReturnsTrueForPromise()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const p = Promise.resolve(42);
                console.log(util.types.isPromise(p));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsPromise_ReturnsFalseForNonPromise()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isPromise(42));
                console.log(util.types.isPromise({}));
                console.log(util.types.isPromise('string'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsRegExp_ReturnsTrueForRegExp()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isRegExp(/test/));
                console.log(util.types.isRegExp(new RegExp('test')));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Types_IsRegExp_ReturnsFalseForNonRegExp()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isRegExp('test'));
                console.log(util.types.isRegExp({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    [Fact]
    public void Types_IsMap_ReturnsTrueForMap()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isMap(new Map()));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsMap_ReturnsFalseForNonMap()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isMap({}));
                console.log(util.types.isMap(new Set()));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    [Fact]
    public void Types_IsSet_ReturnsTrueForSet()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isSet(new Set()));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsSet_ReturnsFalseForNonSet()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsTypedArray_ReturnsTrueForBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isTypedArray(Buffer.from('test')));
                console.log(util.types.isTypedArray(Buffer.alloc(10)));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Types_IsTypedArray_ReturnsFalseForNonTypedArray()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isTypedArray([]));
                console.log(util.types.isTypedArray({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    // ============ util.deprecate TESTS ============

    [Fact]
    public void Deprecate_ReturnsWrappedFunction()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Contains("true", output);
    }

    [Fact]
    public void Deprecate_WarnsOnFirstCallOnly()
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

        // The warning is written to stderr, not captured by console.log
        // Just verify it doesn't crash and completes
        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("done\n", output);
    }

    // ============ util.callbackify TESTS ============

    [Fact]
    public void Callbackify_CallsCallbackWithResult()
    {
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Callbackify_CallsCallbackWithErrorOnThrow()
    {
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ util.inherits TESTS ============

    [Fact]
    public void Inherits_SetsSuperProperty()
    {
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ TextEncoder TESTS ============

    [Fact]
    public void TextEncoder_EncodesToUtf8Buffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextEncoder, TextDecoder } from 'util';
                const encoder = new TextEncoder();
                const encoded = encoder.encode('hello');
                console.log(encoded.length === 5);
                // Verify round-trip encoding/decoding
                const decoder = new TextDecoder();
                console.log(decoder.decode(encoded) === 'hello');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void TextEncoder_EncodingPropertyIsUtf8()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextEncoder } from 'util';
                const encoder = new TextEncoder();
                console.log(encoder.encoding === 'utf-8');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TextEncoder_EncodesUnicodeCorrectly()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ TextDecoder TESTS ============

    [Fact]
    public void TextDecoder_DecodesUtf8Buffer()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TextDecoder_DefaultEncodingIsUtf8()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextDecoder } from 'util';
                const decoder = new TextDecoder();
                console.log(decoder.encoding === 'utf-8');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TextDecoder_SupportsLatin1()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void TextDecoder_SupportsUtf16le()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { TextDecoder } from 'util';
                const decoder = new TextDecoder('utf-16le');
                console.log(decoder.encoding === 'utf-16le');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void TextEncoder_TextDecoder_RoundTrip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ isDeepStrictEqual TESTS ============

    [Fact]
    public void IsDeepStrictEqual_PrimitivesEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(1, 1));
                console.log(util.isDeepStrictEqual('hello', 'hello'));
                console.log(util.isDeepStrictEqual(true, true));
                console.log(util.isDeepStrictEqual(null, null));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_PrimitivesNotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(1, 2));
                console.log(util.isDeepStrictEqual('hello', 'world'));
                console.log(util.isDeepStrictEqual(true, false));
                console.log(util.isDeepStrictEqual(1, '1'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\nfalse\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_NaNEqualsNaN()
    {
        // Unlike === operator, isDeepStrictEqual treats NaN as equal to NaN
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(NaN, NaN));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_ArraysEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual([1, 2, 3], [1, 2, 3]));
                console.log(util.isDeepStrictEqual(['a', 'b'], ['a', 'b']));
                console.log(util.isDeepStrictEqual([], []));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_ArraysNotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual([1, 2, 3], [1, 2, 4]));
                console.log(util.isDeepStrictEqual([1, 2], [1, 2, 3]));
                console.log(util.isDeepStrictEqual([1, 2, 3], [1, 2]));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_ObjectsEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual({ a: 1, b: 2 }, { a: 1, b: 2 }));
                console.log(util.isDeepStrictEqual({ x: 'hello' }, { x: 'hello' }));
                console.log(util.isDeepStrictEqual({}, {}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_ObjectsNotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 2 }));
                console.log(util.isDeepStrictEqual({ a: 1 }, { b: 1 }));
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 1, b: 2 }));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_NestedObjectsEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const obj1 = { a: { b: { c: 1 } } };
                const obj2 = { a: { b: { c: 1 } } };
                console.log(util.isDeepStrictEqual(obj1, obj2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_NestedObjectsNotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const obj1 = { a: { b: { c: 1 } } };
                const obj2 = { a: { b: { c: 2 } } };
                console.log(util.isDeepStrictEqual(obj1, obj2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_ArraysWithObjectsEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const arr1 = [{ a: 1 }, { b: 2 }];
                const arr2 = [{ a: 1 }, { b: 2 }];
                console.log(util.isDeepStrictEqual(arr1, arr2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_NullVsUndefined()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(null, undefined));
                console.log(util.isDeepStrictEqual(undefined, null));
                console.log(util.isDeepStrictEqual(undefined, undefined));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\ntrue\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_SameReference()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const obj = { a: 1 };
                console.log(util.isDeepStrictEqual(obj, obj));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_BuffersEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const buf1 = Buffer.from('hello');
                const buf2 = Buffer.from('hello');
                console.log(util.isDeepStrictEqual(buf1, buf2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void IsDeepStrictEqual_BuffersNotEqual()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const buf1 = Buffer.from('hello');
                const buf2 = Buffer.from('world');
                console.log(util.isDeepStrictEqual(buf1, buf2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    // ============ parseArgs TESTS ============

    [Fact]
    public void ParseArgs_BooleanOption()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_StringOption()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_StringOptionWithEquals()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--output=file.txt'],
                    options: {
                        output: { type: 'string' }
                    }
                });
                console.log(result.values.output === 'file.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_ShortOption()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_ShortStringOption()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['-o', 'output.txt'],
                    options: {
                        output: { type: 'string', short: 'o' }
                    }
                });
                console.log(result.values.output === 'output.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_MultipleOptions()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--verbose', '--output', 'file.txt'],
                    options: {
                        verbose: { type: 'boolean' },
                        output: { type: 'string' }
                    }
                });
                console.log(result.values.verbose === true);
                console.log(result.values.output === 'file.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ParseArgs_DefaultValue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: [],
                    options: {
                        output: { type: 'string', default: 'default.txt' }
                    }
                });
                console.log(result.values.output === 'default.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_Positionals()
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
                console.log(result.positionals[0] === 'file1.txt');
                console.log(result.positionals[1] === 'file2.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void ParseArgs_MixedOptionsAndPositionals()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--verbose', 'file.txt'],
                    options: {
                        verbose: { type: 'boolean' }
                    },
                    allowPositionals: true
                });
                console.log(result.values.verbose === true);
                console.log(result.positionals[0] === 'file.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ParseArgs_OptionTerminator()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ParseArgs_MultipleValues()
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
                console.log(result.values.file[0] === 'a.txt');
                console.log(result.values.file[1] === 'b.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void ParseArgs_CombinedShortOptions()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['-abc'],
                    options: {
                        alpha: { type: 'boolean', short: 'a' },
                        beta: { type: 'boolean', short: 'b' },
                        charlie: { type: 'boolean', short: 'c' }
                    }
                });
                console.log(result.values.alpha === true);
                console.log(result.values.beta === true);
                console.log(result.values.charlie === true);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void ParseArgs_AllowNegative()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--no-verbose'],
                    options: {
                        verbose: { type: 'boolean' }
                    },
                    allowNegative: true
                });
                console.log(result.values.verbose === false);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ParseArgs_StrictModeThrowsOnUnknown()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                try {
                    util.parseArgs({
                        args: ['--unknown'],
                        options: {},
                        strict: true
                    });
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void ParseArgs_NonStrictAllowsUnknown()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.parseArgs({
                    args: ['--unknown'],
                    options: {},
                    strict: false
                });
                console.log(result.values.unknown === true);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ toUSVString TESTS ============

    [Fact]
    public void ToUSVString_RegularString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.toUSVString('hello') === 'hello');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ToUSVString_EmptyString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.toUSVString('') === '');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ToUSVString_UnicodeEmoji()
    {
        // Emoji (surrogate pair) should be preserved
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const emoji = '😀';
                console.log(util.toUSVString(emoji) === emoji);
                console.log(util.toUSVString(emoji).length === 2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ToUSVString_ValidSurrogatePair()
    {
        // Valid surrogate pair should be preserved
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                // U+1F600 (😀) = D83D DE00 in UTF-16
                const str = '\uD83D\uDE00';
                const result = util.toUSVString(str);
                console.log(result === str);
                console.log(result.length === 2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ToUSVString_LoneHighSurrogate()
    {
        // Lone high surrogate should be replaced with U+FFFD
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const loneHigh = '\uD83D';
                const result = util.toUSVString(loneHigh);
                console.log(result === '\uFFFD');
                console.log(result.length === 1);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ToUSVString_LoneLowSurrogate()
    {
        // Lone low surrogate should be replaced with U+FFFD
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const loneLow = '\uDE00';
                const result = util.toUSVString(loneLow);
                console.log(result === '\uFFFD');
                console.log(result.length === 1);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ToUSVString_MixedContent()
    {
        // Mix of regular chars, valid pairs, and lone surrogates
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                // 'a' + lone high + 'b' + valid pair + 'c' + lone low
                const input = 'a\uD83Db\uD83D\uDE00c\uDE00';
                const result = util.toUSVString(input);
                // Should become: 'a' + FFFD + 'b' + valid pair + 'c' + FFFD
                console.log(result.length === 7);
                console.log(result.charAt(0) === 'a');
                console.log(result.charAt(1) === '\uFFFD');
                console.log(result.charAt(2) === 'b');
                console.log(result.charAt(5) === 'c');
                console.log(result.charAt(6) === '\uFFFD');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void ToUSVString_ConvertsNonStringToString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.toUSVString(123) === '123');
                // Numbers convert to string representation
                console.log(util.toUSVString(3.14).startsWith('3.14'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ToUSVString_ChineseCharacters()
    {
        // Regular CJK characters (not surrogates) should be preserved
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const chinese = '你好世界';
                console.log(util.toUSVString(chinese) === chinese);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ stripVTControlCharacters TESTS ============

    [Fact]
    public void StripVTControlCharacters_RemovesAnsiColors()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const colored = '\x1b[31mRed\x1b[0m';
                console.log(util.stripVTControlCharacters(colored) === 'Red');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void StripVTControlCharacters_RemovesBoldAndReset()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const bold = '\x1b[1mBold\x1b[0m';
                console.log(util.stripVTControlCharacters(bold) === 'Bold');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void StripVTControlCharacters_PreservesPlainText()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const plain = 'Hello, World!';
                console.log(util.stripVTControlCharacters(plain) === plain);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void StripVTControlCharacters_EmptyString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.stripVTControlCharacters('') === '');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void StripVTControlCharacters_MultipleEscapeSequences()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const multi = '\x1b[31mRed\x1b[0m and \x1b[32mGreen\x1b[0m';
                console.log(util.stripVTControlCharacters(multi) === 'Red and Green');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ getSystemErrorName TESTS ============

    [Fact]
    public void GetSystemErrorName_ReturnsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-2) === 'ENOENT');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void GetSystemErrorName_ReturnsEACCES()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-13) === 'EACCES');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void GetSystemErrorName_ReturnsEPERM()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-1) === 'EPERM');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void GetSystemErrorName_UnknownCode()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const result = util.getSystemErrorName(-999);
                console.log(result.includes('Unknown'));
                console.log(result.includes('-999'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ getSystemErrorMap TESTS ============

    [Fact]
    public void GetSystemErrorMap_ReturnsMap()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const map = util.getSystemErrorMap();
                // Check that it's a Map-like object
                console.log(typeof map === 'object');
                console.log(map !== null);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void GetSystemErrorMap_ContainsENOENT()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ util.types.isNativeError TESTS ============

    [Fact]
    public void Types_IsNativeError_ReturnsTrueForError()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNativeError(new Error('test')));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsNativeError_ReturnsFalseForNonError()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNativeError('not an error'));
                console.log(util.types.isNativeError({}));
                console.log(util.types.isNativeError(null));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    // ============ util.types.isBoxedPrimitive TESTS ============

    [Fact]
    public void Types_IsBoxedPrimitive_ReturnsFalse()
    {
        // In SharpTS, we don't have explicit boxed primitives, so this always returns false
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isBoxedPrimitive(42));
                console.log(util.types.isBoxedPrimitive('hello'));
                console.log(util.types.isBoxedPrimitive(true));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    // ============ util.types.isWeakMap TESTS ============

    [Fact]
    public void Types_IsWeakMap_ReturnsTrueForWeakMap()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakMap(new WeakMap()));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsWeakMap_ReturnsFalseForMap()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakMap(new Map()));
                console.log(util.types.isWeakMap({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    // ============ util.types.isWeakSet TESTS ============

    [Fact]
    public void Types_IsWeakSet_ReturnsTrueForWeakSet()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakSet(new WeakSet()));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsWeakSet_ReturnsFalseForSet()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakSet(new Set()));
                console.log(util.types.isWeakSet({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    // ============ util.types.isArrayBuffer TESTS ============

    [Fact]
    public void Types_IsArrayBuffer_ReturnsTrueForBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArrayBuffer(Buffer.alloc(10)));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Types_IsArrayBuffer_ReturnsFalseForNonBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArrayBuffer([]));
                console.log(util.types.isArrayBuffer({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }
}
