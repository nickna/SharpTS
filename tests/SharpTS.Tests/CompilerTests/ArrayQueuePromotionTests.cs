using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ArrayQueuePromotionTests
{
    private const string Source = """
        function queue(n: number): number {
            const values: number[] = [];
            for (let i: number = 0; i < n; i++) values.push(i);
            let total: number = 0;
            while (values.length > 0) total = total + values.shift();
            return total;
        }
        function indexed(): number {
            const values: number[] = [];
            values.push(1);
            return values[0];
        }
        function holes(): number {
            const values: number[] = [];
            values[3] = 7;
            values.unshift(1);
            return values.length;
        }
        console.log(queue(100), indexed(), holes());
        """;

    [Fact]
    public void QueueStorageIsSelectedWithoutChangingOrdinaryListPromotion()
    {
        var statements = new Parser(new Lexer(Source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"array_queue_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, new DeadCodeAnalyzer(typeMap).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var methods = assembly.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.Contains(methods.Single(m => m.Name.EndsWith("queue")).GetMethodBody()!.LocalVariables,
            local => local.LocalType.Name == "$ArrayQueueDouble");
        Assert.Contains(methods.Single(m => m.Name.EndsWith("indexed")).GetMethodBody()!.LocalVariables,
            local => local.LocalType == typeof(List<double>));
        Assert.Contains(methods.Single(m => m.Name.EndsWith("holes")).GetMethodBody()!.LocalVariables,
            local => local.LocalType.Name == "$ArrayQueueDoubleWithHoles");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
    }

    [Fact]
    public void QueueHelpersPassIlVerification()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(Source));
        Assert.Equal("4950 1 5\n", TestHarness.RunCompiled(Source));
    }
}
