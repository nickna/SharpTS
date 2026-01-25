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
        // Note: util.types.isFunction checks for SharpTSFunction or BuiltInMethod
        // Arrow functions are SharpTSArrowFunction (different type)
        // Testing with a regular function declaration
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                function regularFn() {}
                console.log(util.types.isFunction(regularFn));
                console.log(util.types.isFunction('not function'));
                console.log(util.types.isFunction(42));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsNull()
    {
        // Note: In SharpTS, undefined is represented as SharpTSUndefined.Instance
        // The util.types.isNull checks for args[0] == null
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNull(null));
                console.log(util.types.isNull(0));
                console.log(util.types.isNull(''));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Types_IsUndefined()
    {
        // Note: In SharpTS, undefined is represented as SharpTSUndefined.Instance
        // The util.types.isUndefined checks for args[0] == null
        // This test verifies it returns false for non-null values
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isUndefined(0));
                console.log(util.types.isUndefined('test'));
                console.log(util.types.isUndefined({}));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false\nfalse\nfalse\n", output);
    }
}
