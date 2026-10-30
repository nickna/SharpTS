using System.Reflection;
using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("TimerTests")]
public sealed class PrimitiveFixedArgumentTransportTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "import * as fs from 'fs';function pass(value:number):number{return value;}let value:any=1n;const result=pass(value++);console.log(String(result),typeof result,typeof fs.watch);", "1 bigint function\n"];
        yield return ["node-alias", "import * as fs from 'node:fs';function pass(value:number):number{return value;}let value:any=2n;const result=pass(value++);console.log(String(result),typeof result,typeof fs.watch);", "2 bigint function\n"];
        yield return ["promises", "import * as fs from 'fs/promises';function pass(value:number):number{return value;}let value:any=3n;const result=pass(value++);console.log(String(result),typeof result,typeof fs.watch);", "3 bigint function\n"];
        yield return ["member-target", "import * as fs from 'fs';const holder={pass:(value:number)=>value};let value:any=4n;const result=holder.pass(++value);console.log(String(result),typeof result,typeof fs.watch);", "5 bigint function\n"];
        yield return ["mutable-target", "import * as fs from 'fs';function pass(value:number):number{return value;}let selected:(value:number)=>number=pass;selected=(value:number)=>value;let value:any=6n;const result=selected(value++);console.log(String(result),typeof result,typeof fs.watch);", "6 bigint function\n"];
        yield return ["unary-producer", "import * as fs from 'fs';function pass(value:number):number{return value;}const result=pass(-(7n as any));console.log(String(result),typeof result,typeof fs.watch);", "-7 bigint function\n"];
        yield return ["ordinary-control", "import * as fs from 'fs';function pass(value:number):number{return value;}const result=pass(8);console.log(String(result),typeof result,typeof fs.watch);", "8 number function\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void RawSourceValuesCrossVerifiedPrimitiveBoundaries(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        byte[] bytes = CompileAndVerify(source);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAndPrimitiveAbisRemainDistinct(bool hosted)
    {
        byte[] bytes = CompileAndVerify((string)Cases().First()[1], hosted);
        var assembly = Assembly.Load(bytes);
        var program = assembly.GetType("$Program")!;
        Assert.Same(typeof(object), program.GetMethod("$M_fs_watch", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetParameters()[0].ParameterType);
        var runtime = assembly.GetType("$Runtime")!;
        foreach (string name in new[] { "FsWatch", "FsWatchFile", "FsUnwatchFile" })
            Assert.Same(typeof(string), runtime.GetMethod(name)!.GetParameters()[0].ParameterType);
    }

    [Theory, ModeData]
    public void LiveWatchFactoriesRetainPathOrderAndRawValues(ExecutionMode mode)
    {
        string path = Path.Combine(Path.GetTempPath(), "sharpts_fixed_abi_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "owned watcher input");
        try
        {
            string source = "import * as fs from 'fs';const path=" + JsonSerializer.Serialize(path) + ";"
                + "function pass(value:number):number{return value;}let value:any=9n;const result=pass(value++);console.log(String(result),typeof result);"
                + "const trace:string[]=[];function filename(){trace.push('path');return path;}function options(){trace.push('options');return {};}function listener(){trace.push('listener');return()=>{};}"
                + "const watcher=fs.watch(filename(),options(),listener());console.log(typeof watcher.close);watcher.close();console.log(trace.join(','));"
                + "fs.watchFile(path,{interval:100000},()=>{});fs.unwatchFile(path);fs.watchFile(path,()=>{});fs.unwatchFile(path);"
                + "async function finish(){const iterator=fs.promises.watch(path);const end=await iterator.return();console.log(end.done);}finish();";
            string actual = mode == ExecutionMode.Interpreted
                ? TestHarness.RunModules(new() { ["main.ts"] = source }, "main.ts", mode, TimeSpan.FromSeconds(30))
                : TestHarness.RunCompiledWithTimeout(() => Assembly.Load(CompileAndVerify(source)), TimeSpan.FromSeconds(30));
            Assert.Equal("9 bigint\nfunction\npath,options,listener\ntrue\n", actual);
        }
        finally { File.Delete(path); }
    }

    private static byte[] CompileAndVerify(string source, bool hosted = false)
    {
        const string entry = "/primitive-fixed-abi/main.ts";
        var resolver = new ModuleResolver(entry, new Dictionary<string, string> { [entry] = source });
        var module = resolver.LoadModule(entry); var modules = resolver.GetModulesInOrder(module);
        var map = new TypeChecker().CheckModules(modules, resolver);
        var compiler = new ILCompiler("fixed_primitive_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (hosted) compiler.EnableHostedOutput();
        compiler.CompileModules(modules, resolver, map);
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return bytes;
    }
}