using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Covers issue #55: ESM <c>import { name } from './cjs-module'</c> must invoke accessor
/// properties defined on the CJS <c>exports</c> object via <c>Object.defineProperty</c>.
/// Babel-transpiled CJS (uuid, semver, etc.) emits getters for every named export, so a
/// direct <c>_fields</c> read in the interpreter's binding step bound the name to
/// undefined. The fix routes through the full property-access path so the getter runs.
/// </summary>
public class CjsAccessorImportTests
{
    [Theory, ModeData]
    public void NamedImport_Reads_GetterDefinedExport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            // Hand-rolled Babel-shaped CJS: exports are accessor properties, not fields.
            ["./lib.cjs"] = """
                "use strict";
                Object.defineProperty(exports, "__esModule", { value: true });
                Object.defineProperty(exports, "greet", {
                    enumerable: true,
                    get: function () { return _inner; }
                });
                var _inner = function (name) { return "hi " + name; };
                """,
            ["./main.ts"] = """
                import { greet } from './lib.cjs';
                console.log(greet('world'));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("hi world\n", output);
    }

    [Theory, ModeData]
    public void NamedImport_GetterClosesOverLaterBinding(ExecutionMode mode)
    {
        // Mirrors uuid's layout: Object.defineProperty calls come before the var that the
        // getter closes over is assigned. Validates that var hoisting + getter late-binding
        // work through the ESM import path.
        var files = new Dictionary<string, string>
        {
            ["./lib.cjs"] = """
                "use strict";
                Object.defineProperty(exports, "value", {
                    enumerable: true,
                    get: function () { return _v; }
                });
                var _v = 42;
                """,
            ["./main.ts"] = """
                import { value } from './lib.cjs';
                console.log(value);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    // Re-export from a CJS source is interpreter-only for now — the compiled pipeline
    // resolves cross-module exports via pre-computed static fields, which don't exist
    // for dynamically-defined accessor properties on the CJS exports object. Tracked
    // separately from issue #55.
    [Fact]
    public void NamedReexport_From_CjsWithAccessors_Interpreted()
    {
        var files = new Dictionary<string, string>
        {
            ["./lib.cjs"] = """
                "use strict";
                Object.defineProperty(exports, "answer", {
                    enumerable: true,
                    get: function () { return 42; }
                });
                """,
            ["./reexport.ts"] = """
                export { answer } from './lib.cjs';
                """,
            ["./main.ts"] = """
                import { answer } from './reexport';
                console.log(answer);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted);
        Assert.Equal("42\n", output);
    }
}
