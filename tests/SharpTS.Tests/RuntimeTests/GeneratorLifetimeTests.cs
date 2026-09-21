using System.Reflection;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

public class GeneratorLifetimeTests
{
    [Fact]
    public void GeneratorDisposalStopsSuspendedWorkerAndIsIdempotent()
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        var generator = Create(interpreter, "function* g(){ try { yield 1; console.log('resumed'); } finally { console.log('finally'); } } g();");
        Assert.Equal(1d, generator.Next().Value);
        var worker = Worker(generator);

        generator.Dispose();
        generator.Dispose();

        Assert.True(worker.Join(TimeSpan.FromSeconds(2)));
        Assert.Empty(output.ToString());
        Assert.True(generator.Next().Done);
        Assert.Equal(42d, generator.Return(42d).Value);
        interpreter.Dispose();
    }

    [Fact]
    public void DisposalBeforeFirstResumeDoesNotStartWorker()
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        var generator = Create(interpreter, "function* g(){ console.log('started'); yield 1; } g();");
        generator.Dispose();
        generator.Dispose();
        Assert.True(generator.Next().Done);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void CompletedGeneratorCanBeDisposedBeforeItsInterpreter()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        var generator = Create(interpreter, "function* g(){ yield 1; } g();");
        generator.Next();
        Assert.True(generator.Next().Done);
        generator.Dispose();
        interpreter.Dispose();
        Assert.True(Worker(generator).Join(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void InterpreterDisposalReleasesBothWorkersInYieldStarDelegation()
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        var inner = Create(interpreter, "function* child(){ yield 1; console.log('child resumed'); } const inner = child(); inner;");
        var outer = Create(interpreter, "function* parent(){ yield* inner; console.log('parent resumed'); } parent();");
        Assert.Equal(1d, outer.Next().Value);
        var innerWorker = Worker(inner);
        var outerWorker = Worker(outer);

        interpreter.Dispose();

        Assert.True(innerWorker.Join(TimeSpan.FromSeconds(2)));
        Assert.True(outerWorker.Join(TimeSpan.FromSeconds(2)));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void GeneratorCreatedBeforeInterpreterDisposalNeverStartsAfterShutdown()
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        var generator = Create(interpreter, "function* g(){ console.log('started'); yield 1; } g();");
        interpreter.Dispose();
        Assert.True(generator.Next().Done);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void InterpreterDisposalStopsItsSuspendedGeneratorWithoutResumingGuestCode()
    {
        using var output = new StringWriter();
        using var interpreter = new Interpreter(output, TextWriter.Null);
        var generator = Create(interpreter, "function* g(){ try { yield 1; console.log('resumed'); } finally { console.log('finally'); } } g();");
        Assert.Equal(1d, generator.Next().Value);
        var worker = Worker(generator);

        interpreter.Dispose();

        Assert.True(worker.Join(TimeSpan.FromSeconds(2)), "Generator worker survived interpreter disposal.");
        Assert.Empty(output.ToString());
        Assert.True(generator.Next().Done);
        interpreter.Dispose();
    }

    [Fact]
    public void DisposingOneInterpreterDoesNotStopAnotherInterpretersGenerator()
    {
        using var first = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var second = new Interpreter(TextWriter.Null, TextWriter.Null);
        var firstGenerator = Create(first, "function* g(){ yield 1; yield 2; } g();");
        var secondGenerator = Create(second, "function* g(){ yield 3; yield 4; } g();");
        firstGenerator.Next();
        secondGenerator.Next();
        var worker = Worker(firstGenerator);

        first.Dispose();

        Assert.True(worker.Join(TimeSpan.FromSeconds(2)));
        Assert.Equal(4d, secondGenerator.Next().Value);
        Assert.True(secondGenerator.Next().Done);
    }

    private static SharpTSGenerator Create(Interpreter interpreter, string source) =>
        Assert.IsType<SharpTSGenerator>(interpreter.InterpretRepl(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));

    private static Thread Worker(SharpTSGenerator generator) =>
        Assert.IsType<Thread>(typeof(SharpTSGenerator).GetField("_workerThread", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(generator));
}
