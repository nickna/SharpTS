using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Guards the scheduling contract between Promise combinator result adoption
/// and the allocation-free keyed-combinator result mapper.
/// </summary>
public class PromiseCombinatorSchedulingTests
{
    [Fact]
    public void AdoptPromiseCombinatorResult_AllowsInlineInternalContinuation()
    {
        var lexer = new Lexer("const value = 1;");
        var parser = new Parser(lexer.ScanTokens());
        var statements = parser.ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);

        var compiler = new ILCompiler($"promise_adoption_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var runtimeType = assembly.GetType("$Runtime")
            ?? throw new InvalidOperationException("Compiled assembly has no $Runtime type");
        var adopt = runtimeType.GetMethod(
            "AdoptPromiseCombinatorResult",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("AdoptPromiseCombinatorResult was not emitted");

        var source = new TaskCompletionSource<object?>();
        var adopted = (Task<object?>)(adopt.Invoke(null, [source.Task])
            ?? throw new InvalidOperationException("Adoption returned null"));
        var settlementThread = Environment.CurrentManagedThreadId;
        var continuationThread = -1;
        var continuation = adopted.ContinueWith(
            _ => continuationThread = Environment.CurrentManagedThreadId,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        source.SetResult(new List<object?>());

        Assert.True(
            continuation.IsCompleted,
            "The internal keyed-result mapper was deferred to the thread pool.");
        Assert.Equal(settlementThread, continuationThread);
    }
}
