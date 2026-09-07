using System.Reflection;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ObjectSpreadAllocationTests
{
    [Fact]
    public void PlainSpreadAndSymbolReads_DoNotAttachStorageOrAllocateCopyTemporaries()
    {
        const string source = """
            function copy(value: any): any { return { ...value, d: 4 }; }
            function symbols(value: any): any { return Object.getOwnPropertySymbols(value); }
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"spread_allocation_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, new DeadCodeAnalyzer(typeMap).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var runtime = assembly.GetType("$Runtime")!;
        var merge = runtime.GetMethod("MergeIntoObject")!
            .CreateDelegate<Action<Dictionary<string, object?>, object?>>();
        var getSymbols = runtime.GetMethod("GetOwnPropertySymbols")!
            .CreateDelegate<Func<object, object>>();
        var storage = (ConditionalWeakTable<object, Dictionary<object, object?>>)runtime
            .GetField("_symbolStorage", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var input = new Dictionary<string, object?> { ["a"] = 1d, ["b"] = 2d, ["c"] = 3d };
        var target = new Dictionary<string, object?>(4);

        Assert.NotSame(getSymbols(input), getSymbols(input));
        Assert.False(storage.TryGetValue(input, out _));
        // Reuse pre-sized storage to isolate copy overhead from the required result allocation.
        for (int i = 0; i < 100; i++) merge(target, input);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) merge(target, input);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(3d, target["c"]);
        Assert.False(storage.TryGetValue(input, out _));
    }
}
