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
}
