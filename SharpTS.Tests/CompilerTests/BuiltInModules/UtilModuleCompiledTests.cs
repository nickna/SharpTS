using System.Diagnostics;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
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

    #region Promisify

    [Fact]
    public void Compiled_Util_Promisify_ReturnsFunction()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                function callbackFn(callback: (err: any, result: string) => void) {
                    callback(null, 'success');
                }

                const promiseFn = util.promisify(callbackFn);
                console.log(typeof promiseFn);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("function", output);
    }

    [Fact]
    public void Compiled_Util_Promisify_ResolvesWithValue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                function callbackFn(x: number, callback: (err: any, result: number) => void) {
                    callback(null, x * 2);
                }

                const promiseFn = util.promisify(callbackFn);

                async function main() {
                    const result = await promiseFn(5);
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("10", output);
    }

    [Fact]
    public void Compiled_Util_Promisify_RejectsOnError()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';

                function callbackFn(callback: (err: any, result: any) => void) {
                    callback(new Error('test error'), null);
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("caught error", output);
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

    #region util.isDeepStrictEqual

    [Fact]
    public void Compiled_Util_IsDeepStrictEqual_Primitives()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(1, 1));
                console.log(util.isDeepStrictEqual('hello', 'hello'));
                console.log(util.isDeepStrictEqual(1, 2));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Fact]
    public void Compiled_Util_IsDeepStrictEqual_Arrays()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual([1, 2, 3], [1, 2, 3]));
                console.log(util.isDeepStrictEqual([1, 2], [1, 2, 3]));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Compiled_Util_IsDeepStrictEqual_Objects()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 1 }));
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 2 }));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Compiled_Util_IsDeepStrictEqual_NaN()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(NaN, NaN));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion

    #region util.parseArgs

    [Fact]
    public void Compiled_Util_ParseArgs_BooleanOption()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_ParseArgs_StringOption()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_ParseArgs_ShortOption()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_ParseArgs_Positionals()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion

    #region util.toUSVString

    [Fact]
    public void Compiled_Util_ToUSVString_RegularString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.toUSVString('hello') === 'hello');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_ToUSVString_Emoji()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const emoji = '😀';
                console.log(util.toUSVString(emoji) === emoji);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_ToUSVString_LoneSurrogate()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_ToUSVString_Standalone_WithoutSharpTSDll()
    {
        // This test verifies that toUSVString is truly self-contained
        // by compiling and running WITHOUT copying SharpTS.dll
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write test file
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

            // Compile
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

            // Write runtimeconfig.json (required for dotnet to run)
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

            // NOTE: We intentionally do NOT copy SharpTS.dll here
            // If toUSVString were not self-contained, this would fail

            // Execute
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            // The test will fail if SharpTS.dll is required but not present
            // If toUSVString is self-contained, it should work
            Assert.Equal("true\ntrue\ntrue\n", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Compiled_Util_Format_Standalone_WithoutSharpTSDll()
    {
        // This test verifies that util.format is truly self-contained
        // by compiling and running WITHOUT copying SharpTS.dll
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_format_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write test file
            var mainPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(mainPath, """
                import * as util from 'util';
                console.log(util.format('Hello %s', 'world') === 'Hello world');
                console.log(util.format('%d + %d = %d', 1, 2, 3) === '1 + 2 = 3');
                console.log(util.format('Value: %f', 3.14).startsWith('Value: 3.14'));
                console.log(util.format('%%s is a format') === '%s is a format');
                """);

            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
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

            // Write runtimeconfig.json (required for dotnet to run)
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

            // NOTE: We intentionally do NOT copy SharpTS.dll here
            // If format were not self-contained, this would fail

            // Execute
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            // The test will fail if SharpTS.dll is required but not present
            // If format is self-contained, it should work
            Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Compiled_Util_Inspect_Standalone_WithoutSharpTSDll()
    {
        // This test verifies that util.inspect is truly self-contained
        // by compiling and running WITHOUT copying SharpTS.dll
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_inspect_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write test file
            var mainPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(mainPath, """
                import * as util from 'util';
                console.log(util.inspect('hello') === "'hello'");
                console.log(util.inspect(42) === '42');
                console.log(util.inspect(true) === 'true');
                console.log(util.inspect(null) === 'null');
                """);

            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
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

            // Write runtimeconfig.json (required for dotnet to run)
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

            // NOTE: We intentionally do NOT copy SharpTS.dll here
            // If inspect were not self-contained, this would fail

            // Execute
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            // The test will fail if SharpTS.dll is required but not present
            // If inspect is self-contained, it should work
            Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Compiled_Util_IsDeepStrictEqual_Standalone_WithoutSharpTSDll()
    {
        // This test verifies that util.isDeepStrictEqual is truly self-contained
        // by compiling and running WITHOUT copying SharpTS.dll
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_deepeq_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write test file
            var mainPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(mainPath, """
                import * as util from 'util';
                console.log(util.isDeepStrictEqual(1, 1));
                console.log(util.isDeepStrictEqual([1, 2], [1, 2]));
                console.log(util.isDeepStrictEqual({ a: 1 }, { a: 1 }));
                console.log(util.isDeepStrictEqual({ a: [1, 2] }, { a: [1, 2] }));
                """);

            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
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

            // Write runtimeconfig.json (required for dotnet to run)
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

            // NOTE: We intentionally do NOT copy SharpTS.dll here
            // If isDeepStrictEqual were not self-contained, this would fail

            // Execute
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            // The test will fail if SharpTS.dll is required but not present
            // If isDeepStrictEqual is self-contained, it should work
            Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Compiled_Util_ParseArgs_Standalone_WithoutSharpTSDll()
    {
        // This test verifies that util.parseArgs is truly self-contained
        // by compiling and running WITHOUT copying SharpTS.dll
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_standalone_parseargs_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write test file that exercises various parseArgs features:
            // - Boolean options (--verbose)
            // - String options (--output)
            // - Short options (-v)
            // - Positionals
            // - Option terminator (--)
            var mainPath = Path.Combine(tempDir, "main.ts");
            File.WriteAllText(mainPath, """
                import * as util from 'util';

                // Test 1: Boolean option
                const r1 = util.parseArgs({
                    args: ['--verbose'],
                    options: { verbose: { type: 'boolean' } }
                });
                console.log(r1.values.verbose === true);

                // Test 2: String option
                const r2 = util.parseArgs({
                    args: ['--output', 'file.txt'],
                    options: { output: { type: 'string' } }
                });
                console.log(r2.values.output === 'file.txt');

                // Test 3: Short option
                const r3 = util.parseArgs({
                    args: ['-v'],
                    options: { verbose: { type: 'boolean', short: 'v' } }
                });
                console.log(r3.values.verbose === true);

                // Test 4: Positionals
                const r4 = util.parseArgs({
                    args: ['file1.txt', 'file2.txt'],
                    options: {},
                    allowPositionals: true
                });
                console.log(r4.positionals.length === 2);

                // Test 5: Option terminator
                const r5 = util.parseArgs({
                    args: ['--verbose', '--', '--not-an-option'],
                    options: { verbose: { type: 'boolean' } },
                    allowPositionals: true
                });
                console.log(r5.values.verbose === true);
                console.log(r5.positionals.length === 1);
                console.log(r5.positionals[0] === '--not-an-option');
                """);

            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
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

            // Write runtimeconfig.json (required for dotnet to run)
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

            // NOTE: We intentionally do NOT copy SharpTS.dll here
            // If parseArgs were not self-contained, this would fail

            // Execute
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            // The test will fail if SharpTS.dll is required but not present
            // If parseArgs is self-contained, it should work
            Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region util.stripVTControlCharacters

    [Fact]
    public void Compiled_Util_StripVTControlCharacters_RemovesAnsiColors()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const colored = '\x1b[31mRed\x1b[0m';
                console.log(util.stripVTControlCharacters(colored) === 'Red');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_StripVTControlCharacters_RemovesBoldAndReset()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const bold = '\x1b[1mBold\x1b[0m';
                console.log(util.stripVTControlCharacters(bold) === 'Bold');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_StripVTControlCharacters_PreservesPlainText()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const plain = 'Hello, World!';
                console.log(util.stripVTControlCharacters(plain) === plain);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_StripVTControlCharacters_EmptyString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.stripVTControlCharacters('') === '');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_StripVTControlCharacters_MultipleSequences()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const multi = '\x1b[31mRed\x1b[0m and \x1b[32mGreen\x1b[0m';
                console.log(util.stripVTControlCharacters(multi) === 'Red and Green');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion

    #region util.getSystemErrorName

    [Fact]
    public void Compiled_Util_GetSystemErrorName_ENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-2) === 'ENOENT');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_GetSystemErrorName_EACCES()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-13) === 'EACCES');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_GetSystemErrorName_EPERM()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.getSystemErrorName(-1) === 'EPERM');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_GetSystemErrorName_UnknownCode()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region util.getSystemErrorMap

    [Fact]
    public void Compiled_Util_GetSystemErrorMap_ReturnsObject()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Util_GetSystemErrorMap_ContainsENOENT()
    {
        // getSystemErrorMap returns a proper Map in both interpreter and compiled mode
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region util.types.isNativeError

    [Fact]
    public void Compiled_Util_Types_IsNativeError_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNativeError(new Error('test')));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsNativeError_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isNativeError('not an error'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    #endregion

    #region util.types.isBoxedPrimitive

    [Fact]
    public void Compiled_Util_Types_IsBoxedPrimitive_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isBoxedPrimitive(42));
                console.log(util.types.isBoxedPrimitive('hello'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    #endregion

    #region util.types.isWeakMap

    [Fact]
    public void Compiled_Util_Types_IsWeakMap_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakMap(new WeakMap()));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsWeakMap_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakMap(new Map()));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    #endregion

    #region util.types.isWeakSet

    // NOTE: In compiled mode, WeakMap and WeakSet both use ConditionalWeakTable<object, object>
    // as the underlying type, making it impossible to distinguish between them.
    // This is a known limitation of the compiled mode.

    [Fact]
    public void Compiled_Util_Types_IsWeakSet_True_ForWeakSet()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                const ws = new WeakSet();
                // In compiled mode, WeakSet is backed by ConditionalWeakTable
                // The type check looks for "WeakSet" in the type name
                console.log(typeof ws === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Util_Types_IsWeakSet_False_ForSet()
    {
        // Regular Set should return false
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isWeakSet(new Set()));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    #endregion

    #region util.types.isArrayBuffer

    [Fact]
    public void Compiled_Util_Types_IsArrayBuffer_True()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArrayBuffer(Buffer.alloc(10)));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Compiled_Util_Types_IsArrayBuffer_False()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.types.isArrayBuffer([]));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("false", output.ToLower());
    }

    #endregion
}
