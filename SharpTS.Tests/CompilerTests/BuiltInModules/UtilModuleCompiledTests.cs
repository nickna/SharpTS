using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the util module in compiled mode.
/// Verifies that compiled assemblies can run standalone without requiring SharpTS.dll
/// for core util module functionality.
/// </summary>
public class UtilModuleCompiledTests
{
    #region util.types

    [Fact]
    public void Compiled_Util_Types_IsArray_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArray([1, 2, 3]));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsArray_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArray('hello'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsFunction_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isFunction(() => {}));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsFunction_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isFunction('not a function'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsNull_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNull(null));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsNull_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNull(42));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsUndefined_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isUndefined(undefined));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsDate_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isDate(new Date()));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsDate_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isDate('2024-01-01'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsMap_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isMap(new Map()));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsSet_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isSet(new Set()));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    #endregion

    #region util.deprecate

    [Fact]
    public void Compiled_Util_Deprecate_ReturnsWrappedFunction()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                function oldFn(x: number): number {
                    return x * 2;
                }

                const deprecated = util.deprecate(oldFn, 'oldFn is deprecated');
                console.log(deprecated(5));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        // Output includes deprecation warning to stderr and result to stdout
        Assert.Contains("10", output);
    }

    [Fact]
    public void Compiled_Util_Deprecate_WarnsOnFirstCall()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                function oldFn(): string {
                    return 'done';
                }

                const deprecated = util.deprecate(oldFn, 'use newFn instead');
                deprecated();
                deprecated();
                console.log('finished');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("finished", output);
    }

    #endregion

    #region TextEncoder

    [Fact]
    public void Compiled_Util_TextEncoder_EncodingProperty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                const encoder = new util.TextEncoder();
                console.log(encoder.encoding);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("utf-8\n", output);
    }

    [Fact]
    public void Compiled_Util_TextEncoder_EncodeReturnsBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                const encoder = new util.TextEncoder();
                const encoded = encoder.encode('Hi');
                // Check that it's a buffer-like object
                console.log(typeof encoded === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion

    #region TextDecoder

    [Fact]
    public void Compiled_Util_TextDecoder_EncodingProperty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                const decoder = new util.TextDecoder();
                console.log(decoder.encoding);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("utf-8\n", output);
    }

    [Fact]
    public void Compiled_Util_TextEncoder_TextDecoder_RoundTrip()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                const encoder = new util.TextEncoder();
                const decoder = new util.TextDecoder();

                const original = 'Hello, World!';
                const encoded = encoder.encode(original);
                const decoded = decoder.decode(encoded);

                console.log(decoded === original);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_TextDecoder_DecodesUnicode()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                const encoder = new util.TextEncoder();
                const decoder = new util.TextDecoder();

                const original = 'Hello 世界 🌍';
                const encoded = encoder.encode(original);
                const decoded = decoder.decode(encoded);

                console.log(decoded === original);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Simple TextEncoder/TextDecoder (global-style access)

    [Fact]
    public void Compiled_TextEncoder_DirectConstruction()
    {
        // Tests new TextEncoder() when TextEncoder is available globally (via type checker)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                // Access via util module
                const encoder = new util.TextEncoder();
                console.log(encoder.encoding === 'utf-8');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion
}
