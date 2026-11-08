using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ModuleTopLevelReceiverTests
{
    [Theory, ModeData]
    public void ModuleAndItsArrowReceiveUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["classic.ts"] = """
                "use strict";
                console.log("classic", this === globalThis);
                """,
            ["main.mjs"] = """
                /// <reference path="./classic.ts" />
                export const marker = 1;
                console.log("module", this === undefined, this === globalThis);
                const arrow = () => this;
                console.log("arrow", arrow.call({}) === undefined);
                """
        };

        Assert.Equal("classic true\nmodule true false\narrow true\n",
            TestHarness.RunModules(files, "main.mjs", mode, allowTypeErrors: true));
    }
}
