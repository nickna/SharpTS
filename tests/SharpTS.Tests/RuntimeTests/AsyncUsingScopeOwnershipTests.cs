using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

// These controls inspect actual Core resource/exception identities and CLR
// interface completion. Shared JavaScript behavior uses the Node-pinned corpus.
public sealed class AsyncUsingScopeOwnershipTests
{
    [Fact]
    public void AsyncClrFallbackCompletesBeforeTheHandlerReturns()
    {
        var resource = new AsyncResource();
        using var interpreter = Run("declare const input:any;async function run(){try{await using held=input;await 0;}finally{}}run();", resource);
        Assert.Equal(1, resource.Calls);
        Assert.True(resource.Completed);
    }

    [Fact]
    public void SyncClrFallbackIsInvokedOnceInAnAsyncHandler()
    {
        var resource = new SyncResource();
        using var interpreter = Run("declare const input:any;async function run(){try{using held=input;await 0;}finally{}}run();", resource);
        Assert.Equal(1, resource.Calls);
    }

    [Fact]
    public void AsyncSuppressionProducerRetainsBothActualGuestValues()
    {
        const string source = "const body={},cleanup={};let observed:any=null;async function run(){try{await using held={async[Symbol.asyncDispose](){await 0;throw cleanup}};await 0;throw body}catch(error){observed=error}}run();";
        using var interpreter = Run(source);
        var error = Assert.IsType<SharpTSSuppressedError>(interpreter.Environment.Get("observed").ToObject());
        Assert.Same(interpreter.Environment.Get("cleanup").ToObject(), error.Error);
        Assert.Same(interpreter.Environment.Get("body").ToObject(), error.Suppressed);
    }

    private static Interpreter Run(string source, object? input = null)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var interpreter = new Interpreter(stdout: TextWriter.Null, stderr: TextWriter.Null);
        interpreter.Environment.Define("input", input);
        interpreter.Interpret(statements, new TypeChecker().Check(statements));
        Assert.Null(interpreter.LastUncaughtError);
        return interpreter;
    }

    private sealed class SyncResource : IDisposable
    {
        public int Calls { get; private set; }
        public void Dispose() => Calls++;
    }

    private sealed class AsyncResource : IAsyncDisposable
    {
        public int Calls { get; private set; }
        public bool Completed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Calls++;
            return new ValueTask(Task.Run(async () =>
            {
                await Task.Delay(1).ConfigureAwait(false);
                Completed = true;
            }));
        }
    }
}
