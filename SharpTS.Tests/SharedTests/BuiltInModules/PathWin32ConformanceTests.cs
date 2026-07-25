using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Table-driven conformance tests for the <c>path.win32</c> namespace, with
/// expectations generated from real Node.js (v25.5.0; <c>path</c> semantics are
/// unchanged since the v24 target).
/// </summary>
/// <remarks>
/// <para>
/// These assert <em>exact</em> strings, which is possible because
/// <c>path.win32.*</c> applies win32 rules regardless of host OS — the same
/// expectations hold on Windows, Linux, and macOS.
/// </para>
/// <para>
/// <see cref="PathModuleTests"/> covers the platform-default exports and
/// deliberately accepts either separator, so the win32 branch went untested
/// against POSIX-style (<c>/foo</c>) inputs. That gap hid three bugs, all of
/// which are pinned below:
/// </para>
/// <list type="bullet">
/// <item><c>win32.normalize</c> discarded everything after a single leading
/// separator, so <c>path.join('/foo','bar')</c> returned <c>"\"</c> — and on
/// Windows those are the platform defaults.</item>
/// <item>The UNC branch of <c>win32.normalize</c> never marked the path
/// absolute, losing the root separator
/// (<c>join('//srv/sh','y')</c> → <c>\\srv\shy</c>).</item>
/// <item><c>win32.isAbsolute</c> rejected single-leading-separator paths,
/// contradicting <c>resolve</c>/<c>parse</c> in the same module.</item>
/// </list>
/// <para>
/// Cases here avoid spread-call syntax (<c>w.join(...args)</c>) on purpose so a
/// failure localizes to <c>path</c> rather than to argument lowering.
/// </para>
/// </remarks>
public class PathWin32ConformanceTests
{
    private const string Preamble = """
        import * as path from 'path';
        const w = path.win32;
        let bad = 0;
        function eq(label: string, got: string, want: string): void {
            if (got !== want) {
                bad++;
                console.log('BAD ' + label + ' got=' + JSON.stringify(got) + ' want=' + JSON.stringify(want));
            }
        }
        function eqb(label: string, got: boolean, want: boolean): void {
            if (got !== want) {
                bad++;
                console.log('BAD ' + label + ' got=' + got + ' want=' + want);
            }
        }
        function fmtParse(p: any): string {
            return p.root + '|' + p.dir + '|' + p.base + '|' + p.name + '|' + p.ext;
        }

        """;

    private const string Epilogue = """

        console.log(bad === 0 ? 'ALL OK' : 'FAILURES=' + bad);
        """;

    private static void RunTable(string cases, ExecutionMode mode, string what)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = Preamble + cases + Epilogue
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.True(output.Contains("ALL OK"), $"path.win32.{what} diverges from Node:\n{output}");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Win32_Normalize_MatchesNode(ExecutionMode mode)
    {
        // Single-leading-separator cases are the regression guard: before the fix
        // every one of these returned "\", discarding the path.
        RunTable("""
            eq('normalize("/foo/bar")', w.normalize("/foo/bar"), "\\foo\\bar");
            eq('normalize("/foo/../a")', w.normalize("/foo/../a"), "\\a");
            eq('normalize("/a/b/../c")', w.normalize("/a/b/../c"), "\\a\\c");
            eq('normalize("C:/foo/../a")', w.normalize("C:/foo/../a"), "C:\\a");
            eq('normalize("/foo/..")', w.normalize("/foo/.."), "\\");
            eq('normalize("foo/../a")', w.normalize("foo/../a"), "a");
            eq('normalize("//srv/sh/x/../y")', w.normalize("//srv/sh/x/../y"), "\\\\srv\\sh\\y");
            eq('normalize("//srv/sh")', w.normalize("//srv/sh"), "\\\\srv\\sh\\");
            eq('normalize("/")', w.normalize("/"), "\\");
            eq('normalize("\\")', w.normalize("\\"), "\\");
            eq('normalize("/a")', w.normalize("/a"), "\\a");
            eq('normalize("\\a\\b\\..\\c")', w.normalize("\\a\\b\\..\\c"), "\\a\\c");
            eq('normalize("C:")', w.normalize("C:"), "C:.");
            eq('normalize("a")', w.normalize("a"), "a");
            eq('normalize("")', w.normalize(""), ".");
            eq('normalize("/foo//bar")', w.normalize("/foo//bar"), "\\foo\\bar");
            eq('normalize("/foo/bar/")', w.normalize("/foo/bar/"), "\\foo\\bar\\");
            eq('normalize("/../a")', w.normalize("/../a"), "\\a");
            eq('normalize("C:\\")', w.normalize("C:\\"), "C:\\");
            eq('normalize("\\\\srv\\sh\\a\\..\\b")', w.normalize("\\\\srv\\sh\\a\\..\\b"), "\\\\srv\\sh\\b");
            eq('normalize("//./c:/x")', w.normalize("//./c:/x"), "\\\\.\\c:\\x");
            eq('normalize("/a/./b")', w.normalize("/a/./b"), "\\a\\b");
            eq('normalize("C:foo/bar")', w.normalize("C:foo/bar"), "C:foo\\bar");
            """, mode, "normalize");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Win32_IsAbsolute_MatchesNode(ExecutionMode mode)
    {
        RunTable("""
            eqb('isAbsolute("/foo")', w.isAbsolute("/foo"), true);
            eqb('isAbsolute("\\foo")', w.isAbsolute("\\foo"), true);
            eqb('isAbsolute("//srv/a")', w.isAbsolute("//srv/a"), true);
            eqb('isAbsolute("C:/")', w.isAbsolute("C:/"), true);
            eqb('isAbsolute("C:\\")', w.isAbsolute("C:\\"), true);
            eqb('isAbsolute("C:")', w.isAbsolute("C:"), false);
            eqb('isAbsolute("C:foo")', w.isAbsolute("C:foo"), false);
            eqb('isAbsolute("foo")', w.isAbsolute("foo"), false);
            eqb('isAbsolute("")', w.isAbsolute(""), false);
            eqb('isAbsolute(".")', w.isAbsolute("."), false);
            eqb('isAbsolute("/")', w.isAbsolute("/"), true);
            eqb('isAbsolute("\\")', w.isAbsolute("\\"), true);
            eqb('isAbsolute("\\\\srv\\share")', w.isAbsolute("\\\\srv\\share"), true);
            eqb('isAbsolute("c:/x")', w.isAbsolute("c:/x"), true);
            """, mode, "isAbsolute");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Win32_Join_MatchesNode(ExecutionMode mode)
    {
        RunTable("""
            eq('join("/foo", "bar")', w.join("/foo", "bar"), "\\foo\\bar");
            eq('join("/", "foo")', w.join("/", "foo"), "\\foo");
            eq('join("C:/a", "b")', w.join("C:/a", "b"), "C:\\a\\b");
            eq('join("a", "b")', w.join("a", "b"), "a\\b");
            eq('join("//srv/sh", "y")', w.join("//srv/sh", "y"), "\\\\srv\\sh\\y");
            eq('join("/a", "b", "c")', w.join("/a", "b", "c"), "\\a\\b\\c");
            eq('join("", "foo")', w.join("", "foo"), "foo");
            eq('join("/foo", "..")', w.join("/foo", ".."), "\\");
            eq('join("C:", "b")', w.join("C:", "b"), "C:\\b");
            eq('join("/a/", "/b/")', w.join("/a/", "/b/"), "\\a\\b\\");
            eq('join("foo", "", "bar")', w.join("foo", "", "bar"), "foo\\bar");
            eq('join("\\\\s\\h", "x")', w.join("\\\\s\\h", "x"), "\\\\s\\h\\x");
            """, mode, "join");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Win32_Dirname_MatchesNode(ExecutionMode mode)
    {
        // Note: Node's win32 dirname preserves the input separators rather than
        // canonicalizing them to backslash.
        RunTable("""
            eq('dirname("/foo/bar/baz.txt")', w.dirname("/foo/bar/baz.txt"), "/foo/bar");
            eq('dirname("/foo")', w.dirname("/foo"), "/");
            eq('dirname("/")', w.dirname("/"), "/");
            eq('dirname("C:\\a\\b")', w.dirname("C:\\a\\b"), "C:\\a");
            eq('dirname("a")', w.dirname("a"), ".");
            eq('dirname("\\\\s\\h\\f")', w.dirname("\\\\s\\h\\f"), "\\\\s\\h\\");
            eq('dirname("C:\\")', w.dirname("C:\\"), "C:\\");
            """, mode, "dirname");
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Win32_Parse_MatchesNode(ExecutionMode mode)
    {
        RunTable("""
            eq('parse("/foo/bar/baz.txt")', fmtParse(w.parse("/foo/bar/baz.txt")), "/|/foo/bar|baz.txt|baz|.txt");
            eq('parse("C:\\a\\b.js")', fmtParse(w.parse("C:\\a\\b.js")), "C:\\|C:\\a|b.js|b|.js");
            eq('parse("/a")', fmtParse(w.parse("/a")), "/|/|a|a|");
            eq('parse("\\\\s\\h\\f.x")', fmtParse(w.parse("\\\\s\\h\\f.x")), "\\\\s\\h\\|\\\\s\\h\\|f.x|f|.x");
            eq('parse("C:")', fmtParse(w.parse("C:")), "C:|C:|||");
            eq('parse("a.b.c")', fmtParse(w.parse("a.b.c")), "||a.b.c|a.b|.c");
            """, mode, "parse");
    }

    /// <summary>
    /// The platform-default exports on Windows are the win32 variants, so the
    /// normalize bug was reachable through the most common path APIs of all.
    /// Guarded only where the host actually is Windows.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PlatformDefault_OnWindows_RootedPathsSurvive(ExecutionMode mode)
    {
        if (!OperatingSystem.IsWindows()) return;

        RunTable("""
            eq('join("/foo","bar")', path.join("/foo", "bar"), "\\foo\\bar");
            eq('normalize("/foo/bar")', path.normalize("/foo/bar"), "\\foo\\bar");
            eqb('isAbsolute("/foo")', path.isAbsolute("/foo"), true);
            """, mode, "platform defaults");
    }
}
