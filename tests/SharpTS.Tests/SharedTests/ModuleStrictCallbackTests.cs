using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ModuleStrictCallbackTests
{
    [Theory, ModeData]
    public void EsModuleCodeAndNestedCallablesAreImplicitlyStrict(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["classic.ts"] = """
                function removeClassic(value: any) { console.log("classic", delete value.length); }
                removeClassic("x");
                """,
            ["main.mjs"] = """
                /// <reference path="./classic.ts" />
                export const marker = 1;
                const value = "x";
                try { console.log("module", delete value.length); }
                catch (error) { console.log("module", error instanceof TypeError); }
                function remove(value) {
                    try { console.log("function", delete value.length); }
                    catch (error) { console.log("function", error instanceof TypeError); }
                }
                const arrow = value => {
                    try { console.log("arrow", delete value.length); }
                    catch (error) { console.log("arrow", error instanceof TypeError); }
                };
                remove(value);
                arrow(value);
                [1].find(function() { console.log("callback", this === undefined); return false; });
                async function* removeAsync(value) { yield delete value.length; }
                removeAsync(value).next().then(
                    result => console.log("async generator", result.value),
                    error => console.log("async generator", error instanceof TypeError));
                """
        };

        Assert.Equal("classic false\nmodule true\nfunction true\narrow true\ncallback true\nasync generator true\n",
            TestHarness.RunModules(files, "main.mjs", mode, allowTypeErrors: true));
    }

    [Theory, ModeData]
    public void StrictScriptCallbacksInheritTheOwningScriptsDirective(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                "use strict";
                const values = [1];
                const context = {tag: "context"};
                values.find(function() { console.log(this === undefined); return false; });
                values.findIndex(function() { console.log(this === undefined); return false; });
                values.findLast(function() { console.log(this === undefined); return false; });
                values.findLastIndex(function() { console.log(this === undefined); return false; });
                values.find(function() { console.log(this === null); return false; }, null);
                values.findIndex(function() { console.log(this === context); return false; }, context);
                const invoke = function() {
                    values.find(function() { console.log(this === undefined); return false; });
                };
                invoke();
                """
        };

        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n",
            TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void StrictScriptCollectionDoesNotMakeFollowingScriptsStrict(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["strict.ts"] = """
                "use strict";
                [1].find(function() { console.log("strict", this === undefined); return false; });
                [1].find(function() { console.log("strict null", this === null); return false; }, null);
                """,
            ["sloppy.ts"] = """
                [1].find(function() { console.log("sloppy", this === globalThis); return false; });
                [1].find(function() { console.log("sloppy null", this === globalThis); return false; }, null);
                """,
            ["main.ts"] = """
                /// <reference path="./strict.ts" />
                /// <reference path="./sloppy.ts" />
                [1].find(function() { console.log("main", this === globalThis); return false; });
                """
        };

        Assert.Equal("strict true\nstrict null true\nsloppy true\nsloppy null true\nmain true\n",
            TestHarness.RunModules(files, "main.ts", mode));
    }
}
