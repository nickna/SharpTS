using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Test262;

/// <summary>
/// Milestone 1 acceptance: one Test262 file runs end-to-end in both execution
/// modes with correct outcome classification. We do not assert <c>Pass</c> —
/// SharpTS is not 100% spec-compliant and the point of this suite is to
/// surface exactly where it isn't. The bar is that plumbing works:
/// neither mode lands in <see cref="Test262Outcome.HarnessError"/>.
/// </summary>
public class SmokeTest
{
    private readonly ITestOutputHelper _output;

    public SmokeTest(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Issue #79 smoke: one async-flagged test must execute end-to-end in
    /// both modes, hitting <c>$DONE</c> and bucketing as <c>Pass</c>. The
    /// chosen file (<c>Promise/resolve/arg-non-thenable</c>) follows the
    /// canonical <c>.then($DONE, $DONE)</c> shape and exercises both the
    /// host-callable interpreter path and the JS-shim compiled path.
    /// </summary>
    [Theory]
    [InlineData(Test262ExecutionMode.Interpreted)]
    [InlineData(Test262ExecutionMode.Compiled)]
    public void Promise_resolve_argNonThenable_AsyncDone(Test262ExecutionMode mode)
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/test262 not initialized");
            return;
        }
        var testPath = Path.Combine(
            Test262Paths.TestDir(root),
            "built-ins", "Promise", "resolve", "arg-non-thenable.js");
        Assert.True(File.Exists(testPath));

        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        var result = runner.RunOne(testPath, mode);
        _output.WriteLine($"{mode} → {result.Outcome}");
        if (result.Message is not null) _output.WriteLine($"  message: {result.Message}");
        if (result.SkipReason is not null) _output.WriteLine($"  skip: {result.SkipReason}");

        Assert.NotEqual(Test262Outcome.Skipped, result.Outcome);
        Assert.NotEqual(Test262Outcome.HarnessError, result.Outcome);
    }

    /// <summary>
    /// Pinpoints the exact line in species-ctor.js that throws after prop-desc.js,
    /// by progressively shrinking the source.
    /// </summary>
    [Fact(Skip = "diagnostic only — kept for repro of the issue#101 cross-test prototype leak")]
    public void Diagnostic_SpeciesCtorAfterPropDesc()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var testDir = Path.Combine(
            Test262Paths.TestDir(root),
            "built-ins", "RegExp", "prototype", "Symbol.split");
        var prior = Path.Combine(testDir, "prop-desc.js");

        // Various inline scripts to try AFTER running prop-desc.js.
        var snippets = new (string label, string src)[]
        {
            ("just regex literal", "var re = /x/iy;"),
            ("set re.constructor", "var re = /x/iy; re.constructor = function() {};"),
            ("read RegExp.prototype", "var p = RegExp.prototype; console.log(typeof p);"),
            ("read [Symbol.split]", "var p = RegExp.prototype[Symbol.split]; console.log(typeof p);"),
            ("regex then read [Symbol.split]", "var re = /x/iy; var p = RegExp.prototype[Symbol.split]; console.log(typeof p);"),
            ("regex then RegExp.prototype", "var re = /x/iy; var p = RegExp.prototype; console.log(typeof p, p);"),
            ("regex; m=...; typeof m STRICT", "var re = /x/iy; var m = RegExp.prototype[Symbol.split]; if (typeof m !== 'function') throw new Error('m typeof: ' + typeof m); console.log('ok');"),
            ("regex; m=...; m.call typeof", "var re = /x/iy; var m = RegExp.prototype[Symbol.split]; var c = m.call; console.log(typeof c);"),
            ("after prop-desc, m.call call", "var re = /x/iy; var r = RegExp.prototype[Symbol.split].call(re, 'abcde'); console.log(r);"),
            ("regex; nothing else", "var re = /x/iy;"),
            ("just save+typeof split", "var m = RegExp.prototype[Symbol.split]; var c = m.call; console.log(typeof c);"),
            ("verify m+then call", "var m = RegExp.prototype[Symbol.split]; if (typeof m !== 'function') throw new Error('m is ' + typeof m); var c = m.call; if (typeof c !== 'function') throw new Error('c is ' + typeof c); console.log('ok');"),
            ("just verify m alone", "var m = RegExp.prototype[Symbol.split]; if (typeof m !== 'function') throw new Error('m is ' + typeof m); console.log('ok');"),
            ("c=m.call; log done", "var m = RegExp.prototype[Symbol.split]; var c = m.call; console.log('done');"),
            ("c=m.call no log", "var m = RegExp.prototype[Symbol.split]; var c = m.call;"),
            ("expr m.call no var", "var m = RegExp.prototype[Symbol.split]; m.call;"),
            ("var c = m.call; typeof c", "var m = RegExp.prototype[Symbol.split]; var c = m.call; typeof c;"),
            ("typeof_c_alone", "var m = RegExp.prototype[Symbol.split]; var c = m.call; var t = typeof c;"),
            ("let m c", "let m = RegExp.prototype[Symbol.split]; let c = m.call; console.log(typeof c);"),
            ("inline m.call", "console.log(typeof RegExp.prototype[Symbol.split].call);"),
            ("inline +var m", "var m = RegExp.prototype[Symbol.split]; console.log(typeof m.call);"),
            ("inline2 + 2 var m", "var m = RegExp.prototype[Symbol.split]; var n = m; console.log(typeof n.call);"),
            ("ifelse with m alone", "var m = RegExp.prototype[Symbol.split]; if (m) { console.log('ok'); }"),
            ("ifelse with m.call", "var m = RegExp.prototype[Symbol.split]; if (m.call) { console.log('ok'); }"),
            ("var c = (m.call)", "var m = RegExp.prototype[Symbol.split]; var c = (m.call); console.log(typeof c);"),
            ("c = m['call']", "var m = RegExp.prototype[Symbol.split]; var c = m['call']; console.log(typeof c);"),
            ("var c only m", "var m = RegExp.prototype[Symbol.split]; var c = m; console.log(typeof c);"),
            ("var c=m, then m.call", "var m = RegExp.prototype[Symbol.split]; var c = m; if (typeof c !== 'function') throw new Error('c is ' + typeof c); console.log('ok');"),
            ("invoke Symbol.split direct", "var re = /x/iy; var r = re[Symbol.split]('abcde'); console.log(r);"),
            ("Array.push.call", "var arr = []; Array.prototype.push.call(arr, 1); console.log(arr.length);"),
            ("hasOwnProperty.call", "var o = {a:1}; var r = Object.prototype.hasOwnProperty.call(o, 'a'); console.log(r);"),
            ("Function.prototype.call exists", "console.log(typeof Function.prototype.call);"),
            ("Function.prototype.call.bind", "var f = Function.prototype.call.bind(Object.prototype.hasOwnProperty); console.log(typeof f);"),
            ("save method then call", "var re = /x/iy; var m = RegExp.prototype[Symbol.split]; var r = m.call(re, 'abcde'); console.log(r);"),
            ("save method then check typeof", "var re = /x/iy; var m = RegExp.prototype[Symbol.split]; console.log(typeof m, typeof m.call);"),
            ("invoke Symbol.split", "var re = /x/iy; var r = RegExp.prototype[Symbol.split].call(re, 'abcde'); console.log(r);"),
            ("species-ctor body", "var re = /x/iy; re.constructor = function() {}; re.constructor[Symbol.species] = function() { return /[db]/y; }; var r = RegExp.prototype[Symbol.split].call(re, 'abcde'); console.log(r);"),
        };

        foreach (var (label, src) in snippets)
        {
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            runner.RunOne(prior, Test262ExecutionMode.Interpreted);
            var tmp = Path.GetTempFileName() + ".js";
            File.WriteAllText(tmp, src);
            try
            {
                var result = runner.RunOne(tmp, Test262ExecutionMode.Interpreted);
                _output.WriteLine($"[{label}] → {result.Outcome}: {result.Message}");
            }
            finally { File.Delete(tmp); }
        }
    }

    /// <summary>
    /// Minimal repro: writes the species-ctor pattern inline so we don't need
    /// the harness. Confirms the bug is in the JS pattern itself, not in
    /// harness loading or shared state.
    /// </summary>
    [Fact(Skip = "diagnostic only — kept for repro of the issue#101 cross-test prototype leak")]
    public void Diagnostic_SpeciesCtorMinimal()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        // Write a temp .js with the species-ctor pattern.
        var src = "var re = /x/iy; re.constructor = function() {}; re.constructor[Symbol.species] = function() { return /[db]/y; }; var r = RegExp.prototype[Symbol.split].call(re, 'abcde'); console.log(r);";
        var tmp = Path.GetTempFileName() + ".js";
        File.WriteAllText(tmp, src);
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        var result = runner.RunOne(tmp, Test262ExecutionMode.Interpreted);
        _output.WriteLine($"outcome: {result.Outcome}");
        if (result.Message is not null) _output.WriteLine($"message: {result.Message}");
        File.Delete(tmp);
    }

    /// <summary>
    /// Diagnostic for #101: species-ctor.js passes manually but reports
    /// RuntimeError in the regen baseline. Runs the test through Test262Runner
    /// directly to capture the exact outcome + message and confirm whether
    /// it's a runner-state issue or a code-path bug.
    /// </summary>
    [Fact]
    public void Diagnostic_SpeciesGetErr()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(Test262Paths.TestDir(root),
            "built-ins", "RegExp", "prototype", "Symbol.split", "species-ctor-species-get-err.js");
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        var result = runner.RunOne(path, Test262ExecutionMode.Interpreted);
        _output.WriteLine($"{result.Outcome}: {result.Message}");
    }

    [Fact]
    public void Diagnostic_RegExpRegressions()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var paths = new[]
        {
            Path.Combine(Test262Paths.TestDir(root), "built-ins", "RegExp", "S15.10.4.1_A6_T1.js"),
            Path.Combine(Test262Paths.TestDir(root), "built-ins", "RegExp", "prototype", "Symbol.match", "g-coerce-result-err.js"),
            Path.Combine(Test262Paths.TestDir(root), "built-ins", "RegExp", "prototype", "Symbol.matchAll", "this-get-flags.js"),
        };
        foreach (var path in paths)
        {
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            var result = runner.RunOne(path, Test262ExecutionMode.Interpreted);
            _output.WriteLine($"{Path.GetFileName(path)} → {result.Outcome}: {result.Message}");
        }
    }

    [Fact]
    public void Diagnostic_CompiledThisValRegression()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var dir = Test262Paths.TestDir(root);
        var paths = new[]
        {
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.match", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.replace", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.split", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.search", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "Array", "from", "iter-map-fn-this-non-strict.js"),
        };
        foreach (var p in paths)
        {
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            var result = runner.RunOne(p, Test262ExecutionMode.Compiled);
            _output.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(p))}/{Path.GetFileName(p)} → {result.Outcome}: {result.Message}");
        }
    }

    [Fact]
    public void Diagnostic_CompiledNotAConstructor()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var dir = Test262Paths.TestDir(root);
        var paths = new[]
        {
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.split", "not-a-constructor.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.match", "not-a-constructor.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.search", "not-a-constructor.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.replace", "not-a-constructor.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.matchAll", "not-a-constructor.js"),
        };
        foreach (var p in paths)
        {
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            var result = runner.RunOne(p, Test262ExecutionMode.Compiled);
            _output.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(p))}/{Path.GetFileName(p)} → {result.Outcome}: {result.Message}");
        }
    }

    [Fact]
    public void Diagnostic_CompiledThisValNonObj()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var dir = Test262Paths.TestDir(root);
        var paths = new[]
        {
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.split", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.match", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.search", "this-val-non-obj.js"),
            Path.Combine(dir, "built-ins", "RegExp", "prototype", "Symbol.replace", "this-val-non-obj.js"),
        };
        foreach (var p in paths)
        {
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            var result = runner.RunOne(p, Test262ExecutionMode.Compiled);
            _output.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(p))}/{Path.GetFileName(p)} → {result.Outcome}: {result.Message}");
        }
    }

    [Fact]
    public void Diagnostic_CoerceGlobal()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var path = Path.Combine(Test262Paths.TestDir(root),
            "built-ins", "RegExp", "prototype", "Symbol.replace", "coerce-global.js");
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        var result = runner.RunOne(path, Test262ExecutionMode.Interpreted);
        _output.WriteLine($"{result.Outcome}: {result.Message}");
    }

    [Fact]
    public void Diagnostic_RegExpExecRegression()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var tests = new[] { "S15.10.6.2_A6.js", "S15.10.6.2_A8.js", "S15.10.6.2_A9.js", "S15.10.6.2_A10.js", "S15.10.6.2_A11.js" };
        foreach (var t in tests)
        {
            var path = Path.Combine(Test262Paths.TestDir(root), "built-ins", "RegExp", "prototype", "exec", t);
            var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
            var result = runner.RunOne(path, Test262ExecutionMode.Interpreted);
            _output.WriteLine($"{t} → {result.Outcome}: {result.Message}");
        }
    }

    /// <summary>
    /// Diagnostic: verify the BoundArrayMethod thisArg / Promise non-iterable
    /// reject-with-TypeError / Promise.then WrapException / Function.prototype
    /// proto-walk / RangeError-for-huge-length cluster of #101 fixes by running
    /// a curated set of previously-failing tests in-process and reporting outcomes.
    /// </summary>
    [Fact]
    public void Diagnostic_ClusterFixes()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var tests = new[] {
            // BoundArrayMethod thisArg fix (compiled-mode dynamic dispatch)
            "test/built-ins/Array/prototype/map/15.4.4.19-5-10.js",
            "test/built-ins/Array/prototype/map/15.4.4.19-5-11.js",
            "test/built-ins/Array/prototype/every/15.4.4.16-5-10.js",
            "test/built-ins/Array/prototype/every/15.4.4.16-5-11.js",
            "test/built-ins/Array/prototype/forEach/15.4.4.18-5-10.js",
            "test/built-ins/Array/prototype/some/15.4.4.17-5-10.js",
            "test/built-ins/Array/prototype/filter/15.4.4.20-5-10.js",
            // RangeError for huge length
            "test/built-ins/Array/prototype/map/15.4.4.19-3-14.js",
            "test/built-ins/Array/prototype/map/15.4.4.19-3-28.js",
            "test/built-ins/Array/prototype/map/15.4.4.19-3-29.js",
            // BigInt/Symbol descriptor rejection
            "test/built-ins/Object/defineProperty/property-description-must-be-an-object-not-bigint.js",
            "test/built-ins/Object/defineProperty/property-description-must-be-an-object-not-symbol.js",
            // Function.prototype walk
            "test/built-ins/Object/defineProperty/15.2.3.6-3-139-1.js",
            // Promise non-iterable → TypeError
            "test/built-ins/Promise/race/S25.4.4.3_A2.2_T1.js",
            "test/built-ins/Promise/all/S25.4.4.1_A3.1_T1.js",
            // Promise unwrap: rejection with TypeError instance preserved
            "test/built-ins/Promise/prototype/catch/S25.4.5.1_A3.1_T2.js",
            // Sparse-array iteration (cached length fix)
            "test/built-ins/Array/prototype/every/15.4.4.16-7-5.js",
            // Object.getOwnPropertyDescriptor identity fix (Object["X"] →
            // same $TSFunction as Object.X via LookupBuiltInStaticMember)
            "test/built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-14.js",
            "test/built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-15.js",
            "test/built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-17.js",
            "test/built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-19.js",
            "test/built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-22.js",
            "test/built-ins/Object/getOwnPropertyDescriptor/15.2.3.3-4-25.js",
            // Sticky RegExp lastIndex reset on test() failure
            "test/built-ins/RegExp/prototype/test/y-fail-lastindex.js",
            "test/built-ins/RegExp/prototype/test/y-fail-return.js",
            "test/built-ins/RegExp/prototype/test/y-init-lastindex.js",
            // Promise.prototype.{then,catch,finally}.call(null|undefined) → TypeError
            "test/built-ins/Promise/prototype/catch/this-value-non-object.js",
            // catch invokes user-installed then per spec
            "test/built-ins/Promise/prototype/catch/invokes-then.js",
            "test/built-ins/Promise/prototype/catch/this-value-then-not-callable.js",
            "test/built-ins/Promise/prototype/catch/this-value-then-throws.js",
            "test/built-ins/Promise/prototype/catch/this-value-then-poisoned.js",
            "test/built-ins/Promise/prototype/finally/this-value-non-object.js",
            // then requires IsPromise(this)
            "test/built-ins/Promise/prototype/then/context-check-on-entry.js",
            "test/built-ins/Promise/prototype/then/S25.4.5.3_A2.1_T1.js",
            // Promise.resolve/reject value-form requires this to be Object
            "test/built-ins/Promise/resolve/ctx-non-object.js",
            "test/built-ins/Promise/reject/ctx-non-object.js",
            // Promise.all/race/allSettled/any value-form: same check
            "test/built-ins/Promise/all/ctx-non-object.js",
            "test/built-ins/Promise/race/ctx-non-object.js",
            "test/built-ins/Promise/allSettled/ctx-non-object.js",
            "test/built-ins/Promise/any/ctx-non-object.js",
        };
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        int pass = 0, fail = 0;
        foreach (var t in tests)
        {
            var path = Path.Combine(Test262Paths.TestDir(root), t.Substring("test/".Length));
            if (!File.Exists(path)) { _output.WriteLine($"  MISSING {t}"); continue; }
            var r = runner.RunOne(path, Test262ExecutionMode.Compiled);
            var status = r.Outcome == Test262Outcome.Pass ? "PASS" : $"{r.Outcome}";
            if (r.Outcome == Test262Outcome.Pass) pass++; else fail++;
            _output.WriteLine($"  {status}: {t}");
            if (r.Outcome != Test262Outcome.Pass && r.Message != null)
                _output.WriteLine($"    msg: {r.Message.Substring(0, Math.Min(120, r.Message.Length))}");
        }
        _output.WriteLine($"\nSummary: {pass} pass / {fail} fail out of {tests.Length}");
    }

    /// <summary>
    /// Diagnostic: scan ALL previously-failing Object.getOwnPropertyDescriptor
    /// tests to count impact of the LookupBuiltInStaticMember fix.
    /// </summary>
    [Fact]
    public void Diagnostic_GopdCluster()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var baselineFile = Path.Combine(Path.GetDirectoryName(typeof(SmokeTest).Assembly.Location)!,
            "..", "..", "..", "baselines", "compiled.txt");
        baselineFile = Path.GetFullPath(baselineFile);
        if (!File.Exists(baselineFile)) return;
        var allFails = File.ReadAllLines(baselineFile)
            .Where(l => l.EndsWith(" Fail"))
            .Select(l => l.Substring(0, l.Length - 5))
            .Where(p => p.Contains("Object/getOwnPropertyDescriptor/"))
            .ToList();
        _output.WriteLine($"checking {allFails.Count} gOPD failing tests");
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        int pass = 0, stillFail = 0;
        foreach (var rel in allFails)
        {
            var abs = Path.Combine(Test262Paths.TestDir(root), rel.Substring("test/".Length));
            if (!File.Exists(abs)) continue;
            var r = runner.RunOne(abs, Test262ExecutionMode.Compiled);
            if (r.Outcome == Test262Outcome.Pass) pass++; else stillFail++;
        }
        _output.WriteLine($"Result: {pass} now pass, {stillFail} still fail");
    }

    /// <summary>
    /// Diagnostic: scan a sample of currently-Passing tests to verify the
    /// cluster fixes don't regress them.
    /// </summary>
    [Fact]
    public void Diagnostic_NoRegressions()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var baselineFile = Path.Combine(Path.GetDirectoryName(typeof(SmokeTest).Assembly.Location)!,
            "..", "..", "..", "baselines", "compiled.txt");
        baselineFile = Path.GetFullPath(baselineFile);
        if (!File.Exists(baselineFile))
        {
            _output.WriteLine($"baseline not found at {baselineFile}");
            return;
        }
        // Sample: pick every Nth Pass test from the categories my fixes touched.
        // Goal: ensure none regress to Fail/RuntimeError.
        var allPasses = File.ReadAllLines(baselineFile)
            .Where(l => l.EndsWith(" Pass"))
            .Select(l => l.Substring(0, l.Length - 5))
            .Where(p => p.Contains("Array/prototype/map/")
                     || p.Contains("Array/prototype/filter/")
                     || p.Contains("Array/prototype/forEach/")
                     || p.Contains("Array/prototype/every/")
                     || p.Contains("Array/prototype/some/")
                     || p.Contains("Array/prototype/find/")
                     || p.Contains("Array/prototype/flatMap/")
                     || p.Contains("Object/defineProperty/")
                     || p.Contains("Promise/race/")
                     || p.Contains("Promise/all/")
                     || p.Contains("Promise/any/")
                     || p.Contains("Promise/prototype/"))
            .ToList();
        // Sample every 20th to keep this fast (~50-100 tests, ~30s).
        var sample = allPasses.Where((_, i) => i % 20 == 0).ToList();
        _output.WriteLine($"sampling {sample.Count} of {allPasses.Count} previously-passing tests");
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        int pass = 0, fail = 0;
        var regressions = new List<string>();
        foreach (var rel in sample)
        {
            var abs = Path.Combine(Test262Paths.TestDir(root), rel.Substring("test/".Length));
            if (!File.Exists(abs)) continue;
            var r = runner.RunOne(abs, Test262ExecutionMode.Compiled);
            if (r.Outcome == Test262Outcome.Pass) pass++;
            else { fail++; regressions.Add($"{r.Outcome}: {rel}"); }
        }
        _output.WriteLine($"Result: {pass} still pass, {fail} regressed");
        foreach (var r in regressions.Take(20)) _output.WriteLine($"  {r}");
        Assert.Equal(0, fail);
    }

    /// <summary>
    /// Diagnostic: re-run ALL currently-baseline-failing tests in clusters my
    /// recent fixes touched. Reports how many now pass vs still fail. Used in
    /// lieu of full regen when xunit baseline write is acting up.
    /// </summary>
    [Fact]
    public void Diagnostic_PostFixAudit()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var baselineFile = Path.Combine(Path.GetDirectoryName(typeof(SmokeTest).Assembly.Location)!,
            "..", "..", "..", "baselines", "compiled.txt");
        baselineFile = Path.GetFullPath(baselineFile);
        if (!File.Exists(baselineFile)) return;

        // Clusters touched by recent fixes (Array iterator thisArg, Promise
        // statics/proto, RegExp sticky, Object defineProperty/gOPD).
        bool InCluster(string p) =>
            p.Contains("Array/prototype/map/") ||
            p.Contains("Array/prototype/filter/") ||
            p.Contains("Array/prototype/forEach/") ||
            p.Contains("Array/prototype/every/") ||
            p.Contains("Array/prototype/some/") ||
            p.Contains("Array/prototype/find/") ||
            p.Contains("Array/prototype/findIndex/") ||
            p.Contains("Array/prototype/findLast/") ||
            p.Contains("Array/prototype/findLastIndex/") ||
            p.Contains("Array/prototype/flatMap/") ||
            p.Contains("Promise/prototype/then/") ||
            p.Contains("Promise/prototype/catch/") ||
            p.Contains("Promise/prototype/finally/") ||
            p.Contains("Promise/resolve/") ||
            p.Contains("Promise/reject/") ||
            p.Contains("Promise/all/") ||
            p.Contains("Promise/race/") ||
            p.Contains("Promise/allSettled/") ||
            p.Contains("Promise/any/") ||
            p.Contains("RegExp/prototype/test/") ||
            p.Contains("Object/defineProperty/") ||
            p.Contains("Object/getOwnPropertyDescriptor/") ||
            p.Contains("Number/prototype/toExponential/");

        var lines = File.ReadAllLines(baselineFile);
        var failing = lines
            .Where(l => l.EndsWith(" Fail") || l.EndsWith(" RuntimeError"))
            .Select(l => l.Substring(0, l.LastIndexOf(' ')))
            .Where(InCluster)
            .ToList();
        _output.WriteLine($"checking {failing.Count} baseline-failing tests across touched clusters");

        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        int nowPass = 0, stillFail = 0;
        var newPassesByCluster = new Dictionary<string, int>();
        foreach (var rel in failing)
        {
            var abs = Path.Combine(Test262Paths.TestDir(root), rel.Substring("test/".Length));
            if (!File.Exists(abs)) continue;
            var r = runner.RunOne(abs, Test262ExecutionMode.Compiled);
            if (r.Outcome == Test262Outcome.Pass)
            {
                nowPass++;
                var cluster = GetClusterName(rel);
                newPassesByCluster.TryGetValue(cluster, out var c);
                newPassesByCluster[cluster] = c + 1;
            }
            else stillFail++;
        }
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"Result: {nowPass} now pass, {stillFail} still fail");
        foreach (var kv in newPassesByCluster.OrderByDescending(kv => kv.Value))
            summary.AppendLine($"  +{kv.Value,3} {kv.Key}");
        _output.WriteLine(summary.ToString());
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "post_fix_audit.txt"), summary.ToString());
    }

    private static string GetClusterName(string testPath)
    {
        // Extract the method-level cluster (e.g., "Array/prototype/map")
        var parts = testPath.Split('/');
        if (parts.Length >= 4 && parts[1] == "built-ins")
        {
            if (parts.Length >= 6 && parts[3] == "prototype")
                return $"{parts[2]}/{parts[3]}/{parts[4]}";
            return $"{parts[2]}/{parts[3]}";
        }
        return testPath;
    }

    /// <summary>
    /// Diagnostic: focused subset audit. Re-runs only error-related baseline
    /// failures in a single cluster (Promise/* / Error/* / Object/get*proto*)
    /// to measure the impact of the native-error subclass prototype work
    /// without paying the full ~12-min audit cost (which is currently
    /// crashing testhost on memory pressure when the assembly load count
    /// gets too high).
    /// </summary>
    [Fact]
    public void Diagnostic_NativeErrorSubclassAudit()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var baselineFile = Path.Combine(Path.GetDirectoryName(typeof(SmokeTest).Assembly.Location)!,
            "..", "..", "..", "baselines", "compiled.txt");
        baselineFile = Path.GetFullPath(baselineFile);
        if (!File.Exists(baselineFile)) return;

        // Tests where Object.getPrototypeOf(error) === SubclassError.prototype
        // checks or SubclassError.prototype property reads are the failure
        // mode — Promise iter-* rejects and Error subclass usage.
        bool InCluster(string p) =>
            p.Contains("Promise/any/iter-") ||
            p.Contains("Promise/all/iter-") ||
            p.Contains("Promise/race/iter-") ||
            p.Contains("Promise/allSettled/iter-") ||
            p.Contains("Error/isError") ||
            p.Contains("Error/proto") ||
            p.Contains("String/prototype/indexOf/") ||
            p.Contains("String/prototype/lastIndexOf/") ||
            p.Contains("String/prototype/includes/") ||
            p.Contains("String/prototype/split/") ||
            p.Contains("TypeError") ||
            p.Contains("RangeError") ||
            p.Contains("ReferenceError") ||
            p.Contains("SyntaxError") ||
            p.Contains("URIError") ||
            p.Contains("EvalError") ||
            p.Contains("AggregateError");

        var lines = File.ReadAllLines(baselineFile);
        var failing = lines
            .Where(l => l.EndsWith(" Fail") || l.EndsWith(" RuntimeError"))
            .Select(l => l.Substring(0, l.LastIndexOf(' ')))
            .Where(InCluster)
            .ToList();
        _output.WriteLine($"checking {failing.Count} baseline-failing tests");
        if (failing.Count == 0) return;

        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        int nowPass = 0, stillFail = 0;
        var byCluster = new Dictionary<string, int>();
        foreach (var rel in failing)
        {
            var abs = Path.Combine(Test262Paths.TestDir(root), rel.Substring("test/".Length));
            if (!File.Exists(abs)) continue;
            var r = runner.RunOne(abs, Test262ExecutionMode.Compiled);
            if (r.Outcome == Test262Outcome.Pass)
            {
                nowPass++;
                var cluster = GetClusterName(rel);
                byCluster.TryGetValue(cluster, out var c);
                byCluster[cluster] = c + 1;
            }
            else stillFail++;
        }
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"Result: {nowPass} now pass, {stillFail} still fail");
        foreach (var kv in byCluster.OrderByDescending(kv => kv.Value))
            summary.AppendLine($"  +{kv.Value,3} {kv.Key}");
        _output.WriteLine(summary.ToString());
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "native_error_audit.txt"), summary.ToString());
    }

    /// <summary>
    /// Diagnostic: probe a small set of failing tests for their actual error
    /// messages. Lets me see why iter-* Promise tests are failing without
    /// running a full regen. Writes to /tmp/probe.txt.
    /// </summary>
    [Fact]
    public void Diagnostic_ProbeFailingTests()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var probes = new[] {
            "test/built-ins/Promise/any/iter-arg-is-undefined-reject.js",
            "test/built-ins/Promise/any/iter-arg-is-null-reject.js",
            "test/built-ins/Promise/any/iter-arg-is-false-reject.js",
            "test/built-ins/Promise/any/iter-arg-is-string-resolve.js",
            "test/built-ins/Promise/allSettled/iter-arg-is-undefined-reject.js",
            "test/built-ins/String/prototype/split/separator-null.js",
        };
        var sb = new System.Text.StringBuilder();
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        foreach (var rel in probes)
        {
            var abs = Path.Combine(Test262Paths.TestDir(root), rel.Substring("test/".Length));
            if (!File.Exists(abs)) { sb.AppendLine($"{rel} -- MISSING"); continue; }
            var r = runner.RunOne(abs, Test262ExecutionMode.Compiled);
            sb.AppendLine($"{rel}: {r.Outcome}");
            if (!string.IsNullOrEmpty(r.Message)) sb.AppendLine($"  msg: {r.Message}");
        }
        _output.WriteLine(sb.ToString());
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "probe.txt"), sb.ToString());
    }

    /// <summary>
    /// Diagnostic: invoke BatchedSubprocessRunner directly with a 600-test
    /// sample. Compares wall-clock against the direct-shell experiment
    /// (92s for 600 tests, 391 tests/min) to isolate whether the xunit
    /// orchestrator path adds significant overhead vs raw subprocess
    /// pipe IPC.
    /// </summary>
    [Fact]
    public void Diagnostic_BatchedRunnerThroughput()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var testDir = Test262Paths.TestDir(root);
        // Throughput experiment: take a 1500-test mix matching the proportions
        // of the full subset.json — half Object (~3411/11K), then Array, Promise,
        // RegExp, String, Math, others. This isolates whether the regen's
        // never-completes behavior is just N * per-test or worse-than-linear.
        var paths = new List<string>();
        var sample = new (string dir, int count)[]
        {
            ("Array", 400),
            ("Object", 450),
            ("RegExp", 250),
            ("Promise", 90),
            ("String", 160),
            ("Math", 50),
            ("Number", 50),
            ("Error", 25),
            ("JSON", 20),
            ("Boolean", 5),
        };
        foreach (var (dir, count) in sample)
        {
            paths.AddRange(Directory.EnumerateFiles(Path.Combine(testDir, "built-ins", dir), "*.js", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("FIXTURE.js", StringComparison.Ordinal))
                .Take(count));
        }
        _output.WriteLine($"Running {paths.Count} tests through BatchedSubprocessRunner...");

        var workerExe = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(SmokeTest).Assembly.Location)!,
            "..", "..", "..", "..", "SharpTS.Test262.Worker", "bin", "Debug", "net10.0",
            "SharpTS.Test262.Worker.dll"));
        if (!File.Exists(workerExe))
        {
            _output.WriteLine($"Worker DLL not found: {workerExe}");
            return;
        }

        var skipFeatures = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(SmokeTest).Assembly.Location)!,
            "..", "..", "..", "config", "skip-features.txt"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var runner = new BatchedSubprocessRunner(
            root,
            Test262ExecutionMode.Compiled,
            TimeSpan.FromSeconds(15),
            File.Exists(skipFeatures) ? skipFeatures : null,
            workerExe);
        var results = runner.RunAll(paths);
        sw.Stop();

        var pass = results.Values.Count(b => b == "Pass");
        var fail = results.Values.Count(b => b != "Pass" && !b.StartsWith("Skipped"));
        var skip = results.Values.Count(b => b.StartsWith("Skipped"));
        _output.WriteLine($"Elapsed: {sw.Elapsed.TotalSeconds:F1}s for {paths.Count} tests");
        _output.WriteLine($"Throughput: {paths.Count / sw.Elapsed.TotalMinutes:F0} tests/min");
        _output.WriteLine($"Pass: {pass}, Fail: {fail}, Skip: {skip}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "batched_throughput.txt"),
            $"{sw.Elapsed.TotalSeconds:F1}s for {paths.Count} tests = {paths.Count / sw.Elapsed.TotalMinutes:F0} tests/min\n" +
            $"Pass: {pass}, Fail: {fail}, Skip: {skip}\n");
    }

    [Fact(Skip = "diagnostic only — kept for repro of the issue#101 cross-test prototype leak")]
    public void Diagnostic_SpeciesCtor()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var testDir = Path.Combine(
            Test262Paths.TestDir(root),
            "built-ins", "RegExp", "prototype", "Symbol.split");
        // Run all Symbol.split tests in alphabetical order using a single
        // Test262Runner (matching the baseline path). If species-ctor.js
        // passes standalone but fails in this batch, some earlier test is
        // polluting state.
        var files = Directory.EnumerateFiles(testDir, "*.js")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: true);
        var failures = new List<string>();
        foreach (var f in files)
        {
            var result = runner.RunOne(f, Test262ExecutionMode.Interpreted);
            var name = Path.GetFileName(f);
            if (result.Outcome == Test262Outcome.RuntimeError)
                failures.Add($"{name}: {result.Message}");
            if (name.Contains("species-ctor.js") || name == "species-ctor-y.js")
                _output.WriteLine($"{name} → {result.Outcome}: {result.Message}");
        }
        _output.WriteLine($"runtime errors: {failures.Count}");
        foreach (var f in failures.Take(20)) _output.WriteLine($"  {f}");
    }

    /// <summary>
    /// Diagnostic only — runs a fixed ~500-test subset through
    /// <see cref="BatchedSubprocessRunner"/> with worker counts 1, 2, 4, 8 and
    /// reports wall-clock per run. Lets us see whether perf scales linearly
    /// with worker count, plateaus, or degrades — the prior 4-worker compile
    /// regen only got 1.32× over serial, suggesting either disk/JIT
    /// contention, straggler batches, or orchestrator overhead.
    /// </summary>
    [Fact]
    public void Diagnostic_ParallelScaling()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var workerDll = Test262Paths.TryFindWorkerDll();
        if (workerDll is null)
        {
            _output.WriteLine("worker DLL not found — build SharpTS.Test262.Worker first");
            return;
        }

        // Math folder: ~1949 tests, mostly fast (no async timeouts), good signal
        // for compile + JIT + load throughput. Take 500 to keep total runtime
        // manageable across four passes.
        var testDir = Path.Combine(Test262Paths.TestDir(root), "built-ins", "Math");
        var paths = Directory.EnumerateFiles(testDir, "*.js", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_FIXTURE.js"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Take(500)
            .ToList();
        _output.WriteLine($"profiling subset: {paths.Count} tests");

        // Warm up: spawn one worker to JIT the worker assembly + load the
        // Test262 harness on disk. Without this, run #1 pays cold-start cost
        // that runs #2-4 don't.
        var warmup = new BatchedSubprocessRunner(
            root, Test262ExecutionMode.Compiled, TimeSpan.FromSeconds(5), null, workerDll)
        { WorkerCount = 1 };
        warmup.RunAll(paths.Take(25).ToList());

        foreach (var n in new[] { 1, 2, 4, 8 })
        {
            var runner = new BatchedSubprocessRunner(
                root, Test262ExecutionMode.Compiled, TimeSpan.FromSeconds(5), null, workerDll)
            { WorkerCount = n };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = runner.RunAll(paths);
            sw.Stop();

            var msPerTest = sw.Elapsed.TotalMilliseconds / paths.Count;
            _output.WriteLine($"N={n}: {sw.Elapsed.TotalSeconds:F1}s total, {msPerTest:F1} ms/test, {results.Count} results");
        }
    }

    /// <summary>
    /// Diagnostic only — measures per-test latency for compiled vs interpreted
    /// modes on a trivial Math test, instrumenting the major phases (parse,
    /// type-check, IL emit, save, ALC load, invoke). Lets us pinpoint where
    /// the per-test cost concentrates so we know what's worth optimizing.
    /// </summary>
    [Fact]
    public void Diagnostic_PerTestTimingBreakdown()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) return;
        var testPath = Path.Combine(Test262Paths.TestDir(root),
            "built-ins", "Math", "abs", "S15.8.2.1_A1.js");
        if (!File.Exists(testPath)) return;

        var skip = new HashSet<string>(StringComparer.Ordinal);
        var runner = new Test262Runner(root, TimeSpan.FromSeconds(5), skip, useNonCollectibleLoad: true);

        // Warmup so JIT + filesystem caches are hot.
        for (int i = 0; i < 3; i++)
        {
            runner.RunOne(testPath, Test262ExecutionMode.Compiled);
            runner.RunOne(testPath, Test262ExecutionMode.Interpreted);
        }

        const int iterations = 30;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
            runner.RunOne(testPath, Test262ExecutionMode.Compiled);
        sw.Stop();
        var compiledMs = sw.Elapsed.TotalMilliseconds / iterations;
        _output.WriteLine($"Compiled: {compiledMs:F1} ms/test (over {iterations} iterations)");

        sw.Restart();
        for (int i = 0; i < iterations; i++)
            runner.RunOne(testPath, Test262ExecutionMode.Interpreted);
        sw.Stop();
        var interpretedMs = sw.Elapsed.TotalMilliseconds / iterations;
        _output.WriteLine($"Interpreted: {interpretedMs:F1} ms/test (over {iterations} iterations)");

        _output.WriteLine($"Compiled-mode overhead vs interpreted: {compiledMs - interpretedMs:F1} ms ({compiledMs / interpretedMs:F1}x)");
    }

    /// <summary>
    /// Diagnostic only — instruments per-phase compiled-mode time over a fixed
    /// 200-test prefix of <c>built-ins/Math</c>. Math is chosen because it has
    /// very few timeouts and pathological allocations, so the timings reflect
    /// the steady-state compile + load + invoke path rather than outliers.
    /// Sequential single-threaded so phase ratios are clean (no contention).
    /// </summary>
    // Diagnostic/profiling only. The "collectible ALC" arm intentionally drives
    // the in-process collectible-ALC load → JIT → Unload churn that crashes the
    // test host with a fatal CLR error (0x80131506) at volume — see issue #964.
    // It is therefore skipped in normal runs; un-skip to profile the two load
    // modes against each other in isolation.
    [Fact(Skip = "diagnostic/profiling only — collectible-ALC arm crashes the host at volume (issue #964); un-skip to profile load modes")]
    public void Diagnostic_CompiledPhaseBreakdown()
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null) { _output.WriteLine("external/test262 not initialized"); return; }

        var mathDir = Path.Combine(Test262Paths.TestDir(root), "built-ins", "Math");
        if (!Directory.Exists(mathDir)) { _output.WriteLine($"missing {mathDir}"); return; }

        var paths = Directory.EnumerateFiles(mathDir, "*.js", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("_FIXTURE.js"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Take(200)
            .ToList();
        _output.WriteLine($"profiling {paths.Count} tests from Math/");

        DumpRun("collectible ALC (current)", useNonCollectibleLoad: false);
        DumpRun("non-collectible Assembly.Load (diagnostic)", useNonCollectibleLoad: true);

        void DumpRun(string label, bool useNonCollectibleLoad)
        {
            // The load mode is immutable per Test262Runner instance, so each arm
            // builds its own runner rather than toggling a shared flag.
            var runner = new Test262Runner(
                root, TimeSpan.FromSeconds(15), useNonCollectibleLoad: useNonCollectibleLoad);

            // Warm up (JIT, file caches, harness cache) so the measured pass
            // isn't full of cold-start cost.
            for (int i = 0; i < 3; i++)
                runner.RunOne(paths[0], Test262ExecutionMode.Compiled);

            CompiledPhaseStats.Reset();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var p in paths)
                runner.RunOne(p, Test262ExecutionMode.Compiled);
            sw.Stop();

            long total = CompiledPhaseStats.LexParseTicks
                       + CompiledPhaseStats.TypeCheckTicks
                       + CompiledPhaseStats.DeadCodeTicks
                       + CompiledPhaseStats.ILCompileTicks
                       + CompiledPhaseStats.SaveBytesTicks
                       + CompiledPhaseStats.AlcLoadTicks
                       + CompiledPhaseStats.InvokeTicks
                       + CompiledPhaseStats.UnloadTicks
                       + CompiledPhaseStats.PeriodicGcTicks;
            long count = CompiledPhaseStats.Count;
            if (count == 0) { _output.WriteLine($"--- {label}: no tests measured"); return; }

            _output.WriteLine($"--- {label} ---");
            _output.WriteLine($"wall: {sw.Elapsed.TotalSeconds:F2} s   tests: {count}   wall/test: {sw.Elapsed.TotalMilliseconds / count:F2} ms");
            _output.WriteLine($"  (total measured phase ticks: {CompiledPhaseStats.Ms(total):F1} ms)");
            Row("LexParse",        CompiledPhaseStats.LexParseTicks, count, total);
            Row("TypeCheck",       CompiledPhaseStats.TypeCheckTicks, count, total);
            Row("DeadCode",        CompiledPhaseStats.DeadCodeTicks, count, total);
            Row("ILCompile",       CompiledPhaseStats.ILCompileTicks, count, total);
            Row("SaveBytes",       CompiledPhaseStats.SaveBytesTicks, count, total);
            Row("AlcLoad",         CompiledPhaseStats.AlcLoadTicks, count, total);
            Row("  AlcCtor",       CompiledPhaseStats.AlcCtorTicks, count, total);
            Row("  AlcLoadStream", CompiledPhaseStats.AlcLoadFromStreamTicks, count, total);
            Row("  AlcReflect",    CompiledPhaseStats.AlcReflectionTicks, count, total);
            Row("Invoke",          CompiledPhaseStats.InvokeTicks, count, total);
            Row("Unload",          CompiledPhaseStats.UnloadTicks, count, total);
            Row("PeriodicGc",      CompiledPhaseStats.PeriodicGcTicks, count, total);
        }

        void Row(string name, long ticks, long count, long total)
        {
            var ms = CompiledPhaseStats.Ms(ticks);
            var perTest = ms / count;
            var pct = total == 0 ? 0 : 100.0 * ticks / total;
            _output.WriteLine($"  {name,-16} {ms,9:F1} ms total    {perTest,7:F2} ms/test    {pct,5:F1}%");
        }
    }

    [Theory]
    [InlineData(Test262ExecutionMode.Interpreted)]
    [InlineData(Test262ExecutionMode.Compiled)]
    public void ArrayIsArray_existsAsFunction(Test262ExecutionMode mode)
    {
        var root = Test262Paths.TryFindRoot();
        if (root is null)
        {
            _output.WriteLine("external/test262 not initialized — run `git submodule update --init external/test262`");
            return; // Soft-skip so local builds without the submodule still pass.
        }

        var testPath = Path.Combine(
            Test262Paths.TestDir(root),
            "built-ins", "Array", "isArray", "15.4.3.2-0-1.js");

        Assert.True(File.Exists(testPath), $"Expected Test262 file at {testPath}");

        var runner = new Test262Runner(root, useNonCollectibleLoad: true);
        var result = runner.RunOne(testPath, mode);

        _output.WriteLine($"{mode} → {result.Outcome}");
        if (result.Message is not null) _output.WriteLine($"  message: {result.Message}");
        if (result.SkipReason is not null) _output.WriteLine($"  skip: {result.SkipReason}");

        Assert.NotEqual(Test262Outcome.HarnessError, result.Outcome);
    }
}
