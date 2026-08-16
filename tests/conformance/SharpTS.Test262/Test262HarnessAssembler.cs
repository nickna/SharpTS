using System.Collections.Concurrent;
using System.Text;

namespace SharpTS.Test262;

/// <summary>
/// Concatenates the Test262 harness prelude with a test body.
///
/// For non-<c>raw</c> tests: prepend <c>sta.js</c>, <c>assert.js</c>, then each
/// file listed in <c>includes</c>, then the test body. For <c>raw</c> tests:
/// emit the body verbatim (the test carries its own setup).
/// </summary>
public sealed class Test262HarnessAssembler
{
    private readonly string _harnessDir;

    // Harness files (sta.js, assert.js, includes/*) are read fresh from disk per test
    // by AppendHarnessFile. Across a ~10K-test run that's ~50K File.ReadAllText calls
    // for files that never change. Cache by absolute path; a single SharpTS.dll's
    // worth of test runs (parent + N workers) shares this once per process.
    private static readonly ConcurrentDictionary<string, string> _harnessCache = new(StringComparer.Ordinal);

    public Test262HarnessAssembler(string test262Root)
    {
        _harnessDir = Test262Paths.HarnessDir(test262Root);
    }

    /// <summary>
    /// Returns (assembledSource, harnessCharCount) where harnessCharCount is
    /// the number of characters prepended ahead of the test body. Runners use
    /// this boundary to bucket pre-body exceptions as <see cref="Test262Outcome.HarnessError"/>.
    /// </summary>
    public (string Source, int HarnessLength) Assemble(string testBody, Test262Metadata metadata)
    {
        if (metadata.IsRaw)
        {
            return (testBody, 0);
        }

        var sb = new StringBuilder();
        // Test262: an `onlyStrict` test must execute as strict-mode code. SharpTS
        // runs each test once (not the tsc-style sloppy+strict variant pair), so we
        // establish strict mode for the whole assembled program with a leading
        // "use strict" directive prologue. Both execution paths honor a program-level
        // directive — Interpreter.CheckForUseStrict and ILCompiler.CheckForUseStrict —
        // so the test body, sta.js, and assert.js all run strict. `noStrict` and
        // unflagged tests keep the prior sloppy default. The prefix is counted into
        // HarnessLength below (it precedes the body), so pre-body error attribution
        // is unaffected. (Without this, `onlyStrict` tests ran sloppy — e.g. Array
        // find* predicate-call-this-strict expected `this === undefined`.)
        if (metadata.OnlyStrict)
            sb.Append("\"use strict\";\n");
        AppendHarnessFile(sb, "sta.js");
        AppendHarnessFile(sb, "assert.js");
        // SharpTS patch: sta.js's `Test262Error` doesn't set `this.name`. The
        // compiled-mode test runner classifies thrown JS values by reading
        // `name` off the exception's `__tsValue` to distinguish Fail (assert
        // failure) from RuntimeError. Compiled-mode also relies on prototype
        // walks for `instance.constructor`. Rather than rebind Test262Error
        // (which doesn't propagate to subsequent `new Test262Error()` calls
        // in compiled mode because function-decl identity is cached), we
        // inject the metadata onto the prototype directly. Instances inherit
        // `.name` and `.constructor` via the prototype-chain walk that
        // assert.throws and the runner classifier both rely on.
        sb.Append("Test262Error.prototype.name = \"Test262Error\";\n");
        sb.Append("Test262Error.prototype.constructor = Test262Error;\n");
        // SpiderMonkey/D8/JSC harness: tests sometimes alias `print` (an
        // engine-provided global) to a property — coerce-global.js does
        // `Array.print = print;`. The assignment side-effect alone is what
        // matters; map it to console.log so it's defined and still produces
        // sensible output if a downstream test ever invokes it.
        sb.Append("var print = function () { console.log.apply(console, arguments); };\n");
        foreach (var include in metadata.Includes)
        {
            AppendHarnessFile(sb, include);
        }
        var harnessLength = sb.Length;
        sb.Append(testBody);
        return (sb.ToString(), harnessLength);
    }

    private void AppendHarnessFile(StringBuilder sb, string name)
    {
        var path = Path.Combine(_harnessDir, name);
        var content = _harnessCache.GetOrAdd(path, static p =>
        {
            if (!File.Exists(p))
                throw new FileNotFoundException(
                    $"Test262 harness file missing: {Path.GetFileName(p)}", p);
            return File.ReadAllText(p);
        });
        sb.Append(content);
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
    }
}
