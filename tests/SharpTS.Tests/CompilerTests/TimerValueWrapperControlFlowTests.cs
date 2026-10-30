using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class TimerValueWrapperControlFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetTimeoutWrapperReturnsAndInitializesItsActualArguments(bool explicitDelay)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName($"timer_wrapper_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var statements = new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow();
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(builder.DefineDynamicModule("main"),
            new RuntimeFeatureDetector().Detect(statements));
        using var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        var assembly = Assembly.Load(stream.ToArray());
        var owner = assembly.GetType("$Runtime")!;
        var marker = new object();
        object?[] arguments = explicitDelay ? [marker, 0d, 7d, "ok"] : [marker];
        try
        {
            Assert.NotNull(owner.GetMethod("SetTimeoutWrapper")!.Invoke(null, [arguments]));
            var queue = Assert.IsAssignableFrom<System.Collections.IList>(owner.GetField("_timerQueue",
                BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
            var timer = Assert.Single(queue.Cast<object>());
            Assert.Same(marker, timer.GetType().GetField("Callback")!.GetValue(timer));
            var actualArguments = Assert.IsType<object[]>(timer.GetType().GetField("Args")!.GetValue(timer));
            Assert.Equal(explicitDelay ? new object[] { 7d, "ok" } : [], actualArguments);
        }
        finally
        {
            owner.GetMethod(runtime.Timers.CancelAllTimers.Name)!.Invoke(null, null);
        }
    }
}