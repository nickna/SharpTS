using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class InheritedScriptStrictnessTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "'use strict';function remove(value:any){try{console.log('result',delete value.length);}catch(error){console.log('caught',error instanceof TypeError);}}remove('x');", "caught true\n"];
        yield return ["async-awaited-key", "'use strict';const target:any={};Object.defineProperty(target,'x',{value:7,configurable:false});async function run(){try{delete target[await Promise.resolve('x')];}catch(error){console.log(error instanceof TypeError,target.x);}}run();", "true 7\n"];
        yield return ["generator", "'use strict';function* remove(value:any){try{yield delete value.length;}catch(error){console.log(error instanceof TypeError);}}remove('x').next();", "true\n"];
        yield return ["suspension-free-async", "'use strict';async function remove(value:any):Promise<number>{try{delete value.length;}catch(error){console.log(error instanceof TypeError);}return 1;}remove('x');", "true\n"];
        yield return ["sloppy-control", "function remove(value:any){console.log(delete value.length);}remove('x');", "false\n"];
        yield return ["local-directive", "function remove(value:any){'use strict';try{delete value.length;}catch(error){console.log(error instanceof TypeError);}}remove('x');", "true\n"];
        yield return ["nested-function", "'use strict';function outer(){function remove(value:any){try{delete value.length;}catch(error){console.log(error instanceof TypeError);}}remove('x');}outer();", "true\n"];
        yield return ["default-parameter", "'use strict';function remove(value:any=delete('x' as any).length){console.log('entered');}try{remove();}catch(error){console.log(error instanceof TypeError);}", "true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { false, true }.Select(native => new object[] { row[0], row[1], row[2], native }));

    [Theory, MemberData(nameof(Modes))]
    public void ModuleCompilationPreservesLexicalFunctionStrictness(string name, string source, string expected, bool native)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, RunModules([source], native));
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterAgreesWithTheSameSources(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StrictAndSloppyScriptOwnersRemainIndependent(bool reverse, bool native)
    {
        const string strict = "'use strict';function strictRemove(value:any){try{delete value.length;}catch(error){console.log(error instanceof TypeError);}}";
        const string sloppy = "function sloppyRemove(value:any){console.log(delete value.length);}";
        string[] sources = reverse ? [sloppy, strict] : [strict, sloppy];
        Assert.Equal("true\nfalse\n", RunModules([.. sources, "strictRemove('x');sloppyRemove('x');"], native));
    }

    private static string RunModules(string[] sources, bool native)
    {
        string directory = Path.Combine(Path.GetTempPath(), "strict_modules_" + Guid.NewGuid().ToString("N"));
        var files = sources.Select((source, index) => (path: Path.Combine(directory, $"script{index}.ts"), source))
            .ToDictionary(row => row.path, row => row.source, StringComparer.OrdinalIgnoreCase);
        var resolver = new ModuleResolver(files.Keys.Last(), files);
        var modules = files.Keys.Select(path => resolver.LoadModule(path)).ToList();
        var types = TestHarness.CheckModulesOrThrow(new TypeChecker(), modules, resolver);
        var compiler = new ILCompiler("strict_modules_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20));
    }
}
