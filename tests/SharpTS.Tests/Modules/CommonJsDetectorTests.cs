using SharpTS.Modules;
using Xunit;

namespace SharpTS.Tests.Modules;

/// <summary>
/// Unit tests for <see cref="CommonJsDetector"/>. Each test creates a small temp directory
/// to exercise the extension/package.json/heuristic resolution rules.
/// </summary>
public class CommonJsDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public CommonJsDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sharpts-cjs-detect-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string MakeFile(string relativePath, string content = "")
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Fact]
    public void CjsExtension_AlwaysCommonJs()
    {
        var path = MakeFile("foo.cjs", "module.exports = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.CommonJs, CommonJsDetector.Detect(path));
    }

    [Fact]
    public void MjsExtension_AlwaysEsm()
    {
        var path = MakeFile("foo.mjs", "export const x = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.EsModule, CommonJsDetector.Detect(path));
    }

    [Fact]
    public void TsExtension_AlwaysEsm()
    {
        var path = MakeFile("foo.ts", "export const x = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.EsModule, CommonJsDetector.Detect(path));
    }

    [Fact]
    public void JsExtension_PackageJsonTypeModule_Esm()
    {
        MakeFile("package.json", """{ "type": "module" }""");
        var path = MakeFile("foo.js", "export const x = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.EsModule, CommonJsDetector.Detect(path));
    }

    [Fact]
    public void JsExtension_PackageJsonTypeCommonJs_Cjs()
    {
        MakeFile("package.json", """{ "type": "commonjs" }""");
        var path = MakeFile("foo.js", "module.exports = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.CommonJs, CommonJsDetector.Detect(path));
    }

    [Fact]
    public void JsExtension_PackageJsonNoTypeField_Cjs()
    {
        // Node default: omitted "type" field means CommonJS
        MakeFile("package.json", """{ "name": "thing" }""");
        var path = MakeFile("foo.js", "module.exports = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.CommonJs, CommonJsDetector.Detect(path));
    }

    [Fact]
    public void JsExtension_NoPackageJsonReachable_HeuristicFromContent_Cjs()
    {
        // No package.json anywhere — fall back to content heuristic.
        // Use a fresh isolated dir so even system roots don't interfere with discovery.
        var isolatedDir = Path.Combine(_tempDir, "no-pkg", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDir);
        var path = Path.Combine(isolatedDir, "foo.js");
        File.WriteAllText(path, "module.exports = function () { return 1; };");

        // The detector walks up the tree; we can't truly guarantee no parent has a package.json on
        // every dev machine. So this test asserts the broader contract: if any signal at all
        // says CJS, the result is CJS.
        var kind = CommonJsDetector.Detect(path);
        Assert.Equal(CommonJsDetector.ModuleKind.CommonJs, kind);
    }

    [Fact]
    public void NestedFile_WalksToNearestPackageJson()
    {
        // Outer pkg says module, inner pkg says commonjs — inner wins for files inside it.
        MakeFile("package.json", """{ "type": "module" }""");
        MakeFile("inner/package.json", """{ "type": "commonjs" }""");
        var innerJs = MakeFile("inner/file.js", "module.exports = {};");
        Assert.Equal(CommonJsDetector.ModuleKind.CommonJs, CommonJsDetector.Detect(innerJs));

        var outerJs = MakeFile("outer.js", "export const x = 1;");
        Assert.Equal(CommonJsDetector.ModuleKind.EsModule, CommonJsDetector.Detect(outerJs));
    }
}
