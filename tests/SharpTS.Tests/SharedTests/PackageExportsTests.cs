using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Integration tests for package.json "exports" field support.
/// Uses TestHarness.RunModules() to test end-to-end module resolution through
/// the interpreter and compiler.
/// </summary>
public class PackageExportsTests
{
    [Theory, ModeData]
    public void SimpleStringExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","exports":"./index.ts"}""",
            ["./node_modules/mypkg/index.ts"] = """
                export const value: number = 42;
                """,
            ["./main.ts"] = """
                import { value } from 'mypkg';
                console.log(value);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void ConditionalExports_Import(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","exports":{"import":"./esm.ts","require":"./cjs.ts"}}""",
            ["./node_modules/mypkg/esm.ts"] = """
                export const src: string = "esm";
                """,
            ["./node_modules/mypkg/cjs.ts"] = """
                export const src: string = "cjs";
                """,
            ["./main.ts"] = """
                import { src } from 'mypkg';
                console.log(src);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("esm\n", output);
    }

    [Theory, ModeData]
    public void SubpathExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","exports":{".":"./index.ts","./utils":"./src/utils.ts"}}""",
            ["./node_modules/mypkg/index.ts"] = """
                export const main: string = "main";
                """,
            ["./node_modules/mypkg/src/utils.ts"] = """
                export function helper(): string { return "helped"; }
                """,
            ["./main.ts"] = """
                import { main } from 'mypkg';
                import { helper } from 'mypkg/utils';
                console.log(main);
                console.log(helper());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("main\nhelped\n", output);
    }

    [Theory, ModeData]
    public void WildcardExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","exports":{"./*":"./src/*.ts"}}""",
            ["./node_modules/mypkg/src/foo.ts"] = """
                export const foo: string = "foo-value";
                """,
            ["./main.ts"] = """
                import { foo } from 'mypkg/foo';
                console.log(foo);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("foo-value\n", output);
    }

    [Theory, ModeData]
    public void NullRestriction_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","exports":{".":"./index.ts","./internal":null}}""",
            ["./node_modules/mypkg/index.ts"] = """
                export const pub: string = "public";
                """,
            ["./node_modules/mypkg/internal.ts"] = """
                export const priv: string = "private";
                """,
            ["./main.ts"] = """
                import { priv } from 'mypkg/internal';
                console.log(priv);
                """
        };

        Assert.ThrowsAny<Exception>(() => TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void MainFieldFallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","main":"./lib/index.ts"}""",
            ["./node_modules/mypkg/lib/index.ts"] = """
                export const val: number = 99;
                """,
            ["./main.ts"] = """
                import { val } from 'mypkg';
                console.log(val);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void ExtensionMapping_JsToTs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """{"name":"mypkg","exports":"./dist/index.js"}""",
            ["./node_modules/mypkg/dist/index.ts"] = """
                export const mapped: string = "mapped-from-ts";
                """,
            ["./main.ts"] = """
                import { mapped } from 'mypkg';
                console.log(mapped);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("mapped-from-ts\n", output);
    }

    [Theory, ModeData]
    public void ScopedPackage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/@scope/pkg/package.json"] = """{"name":"@scope/pkg","exports":"./index.ts"}""",
            ["./node_modules/@scope/pkg/index.ts"] = """
                export const scoped: string = "scoped-value";
                """,
            ["./main.ts"] = """
                import { scoped } from '@scope/pkg';
                console.log(scoped);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("scoped-value\n", output);
    }

    [Theory, ModeData]
    public void ScopedPackageSubpath(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/@scope/pkg/package.json"] = """{"name":"@scope/pkg","exports":{".":"./index.ts","./utils":"./src/utils.ts"}}""",
            ["./node_modules/@scope/pkg/index.ts"] = """
                export const main: string = "main";
                """,
            ["./node_modules/@scope/pkg/src/utils.ts"] = """
                export const util: string = "util-value";
                """,
            ["./main.ts"] = """
                import { util } from '@scope/pkg/utils';
                console.log(util);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("util-value\n", output);
    }

    [Theory, ModeData]
    public void NoPackageJson_LegacyIndexTs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/legacy/index.ts"] = """
                export const legacy: string = "legacy-value";
                """,
            ["./main.ts"] = """
                import { legacy } from 'legacy';
                console.log(legacy);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("legacy-value\n", output);
    }

    [Theory, ModeData]
    public void SelfReferencing(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./package.json"] = """{"name":"myapp","exports":{".":"./index.ts","./utils":"./src/utils.ts"}}""",
            ["./src/utils.ts"] = """
                export function greet(): string { return "hello from utils"; }
                """,
            ["./index.ts"] = """
                export const root: string = "root";
                """,
            ["./main.ts"] = """
                import { greet } from 'myapp/utils';
                console.log(greet());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("hello from utils\n", output);
    }

    [Theory, ModeData]
    public void SubpathImports_HashPrefix(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./package.json"] = """{"name":"myapp","imports":{"#utils":"./src/utils.ts"}}""",
            ["./src/utils.ts"] = """
                export function helper(): string { return "import-mapped"; }
                """,
            ["./main.ts"] = """
                import { helper } from '#utils';
                console.log(helper());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("import-mapped\n", output);
    }

    [Theory, ModeData]
    public void NestedConditionalWithSubpath(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./node_modules/mypkg/package.json"] = """
                {
                    "name": "mypkg",
                    "exports": {
                        ".": {
                            "import": {
                                "types": "./types/index.ts",
                                "default": "./esm/index.ts"
                            },
                            "default": "./cjs/index.ts"
                        },
                        "./utils": {
                            "import": "./esm/utils.ts",
                            "default": "./cjs/utils.ts"
                        }
                    }
                }
                """,
            ["./node_modules/mypkg/types/index.ts"] = """
                export const src: string = "types";
                """,
            ["./node_modules/mypkg/esm/index.ts"] = """
                export const src: string = "esm";
                """,
            ["./node_modules/mypkg/esm/utils.ts"] = """
                export function util(): string { return "esm-util"; }
                """,
            ["./main.ts"] = """
                import { src } from 'mypkg';
                import { util } from 'mypkg/utils';
                console.log(src);
                console.log(util());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        // "types" condition is not used for runtime — "import" matches first, resolving to esm/index.ts
        Assert.Equal("esm\nesm-util\n", output);
    }
}
