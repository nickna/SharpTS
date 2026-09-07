using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ObjectConsumerAllocationTests
{
    [Fact]
    public void ImportedWorkload_UsesModuleLocalSummaryDespiteUnrelatedThisUsage()
    {
        string virtualBase = Path.Combine(Path.GetTempPath(), $"consumer_modules_{Guid.NewGuid():N}");
        string main = Path.Combine(virtualBase, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(virtualBase, "support.ts")] = "export class Example { value(): number { return this.n; } n: number = 1; }",
            [main] = """
                import { Example } from './support';
                function consume(value: any): number { value.a = value.a + 1; return value.a; }
                export function work(n: number): number {
                    let sum: number = 0;
                    for (let i: number = 0; i < n; i++) {
                        const original = { a: i };
                        const result = { ...original };
                        sum = sum + consume(result);
                    }
                    return sum;
                }
                """
        };
        var resolver = new ModuleResolver(main, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(main));
        var map = TestHarness.CheckModulesOrThrow(new TypeChecker(), modules, resolver);
        var compiler = new ILCompiler($"consumer_modules_{Guid.NewGuid():N}");
        compiler.CompileModules(modules, resolver, map,
            new DeadCodeAnalyzer(map).Analyze(modules.SelectMany(module => module.Statements).ToList()));
        var work = FindFunction(Assembly.Load(compiler.SaveToBytes()), "work").CreateDelegate<Func<double, double>>();
        Assert.Equal(50005000, work(10000));
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = work(10000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(50005000, result);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReassignedDirectConsumer_DoesNotReceiveASummary()
    {
        const string source = """
            function consume(value: any): number { value.a = value.a + 1; return value.a; }
            function work(): number { const result = { a: 1 }; return consume(result); }
            consume = (value: any): number => 100;
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        Assert.Empty(StableObjectConsumerAnalyzer.Analyze(statements));
    }
    [Fact]
    public void NumericConsumer_PreservesPromotionAcrossCallBoundary()
    {
        const string source = """
            function consume(value: any): number {
                value.d = value.d + 1;
                return value.a + value.d;
            }
            function work(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const original = { a: i, b: i + 1, c: i + 2 };
                    const result = { ...original, d: i + 3 };
                    result.b = result.b + 1;
                    total = total + consume(result);
                }
                return total;
            }
            """;
        var work = FindFunction(Compile(source), "work").CreateDelegate<Func<double, double>>();
        Assert.Equal(100030000, work(10000));
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = work(10000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(100030000, result);
        Assert.Equal(0, allocated);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void EscapingResult_KeepsSourceInTypedStorage()
    {
        const string source = """
            function make(n: number, flag: boolean, text: string): any {
                const original = { a: n, b: flag, c: text };
                const result = { ...original, d: n + 3 };
                return result;
            }
            """;
        var method = FindFunction(Compile(source), "make");
        Assert.Contains(method.GetMethodBody()!.LocalVariables,
            local => local.LocalType.Name.StartsWith("$Shape_", StringComparison.Ordinal));
        var make = method.CreateDelegate<Func<double, bool, string, object>>();
        var first = Assert.IsType<Dictionary<string, object>>(make(1, true, "three"));
        var second = Assert.IsType<Dictionary<string, object>>(make(1, true, "three"));
        Assert.NotSame(first, second);
        first["a"] = 10d;
        Assert.Equal(1d, second["a"]);
        Assert.Equal(true, second["b"]);
        Assert.Equal("three", second["c"]);
        Assert.Equal(4d, second["d"]);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"object_consumer_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, new DeadCodeAnalyzer(typeMap).Analyze(statements));
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) => assembly.GetType("$Program")!
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));
}
