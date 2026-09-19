using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Compilation.Emitters;
using SharpTS.Compilation.Emitters.Modules;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class BuiltInModuleEmitterRegistryTests
{
    public static TheoryData<string, Type> DefaultModules => new()
    {
        { "primitive:os", typeof(OsModuleEmitter) },
        { "primitive:fs", typeof(FsModuleEmitter) },
        { "primitive:process", typeof(ProcessModuleEmitter) },
        { "crypto", typeof(CryptoModuleEmitter) },
        { "primitive:readline", typeof(ReadlinePrimitiveEmitter) },
        { "primitive:module", typeof(ModulePrimitiveEmitter) },
        { "primitive:stream/consumers", typeof(StreamConsumersPrimitiveEmitter) },
        { "child_process", typeof(ChildProcessModuleEmitter) },
        { "buffer", typeof(BufferModuleEmitter) },
        { "primitive:zlib", typeof(ZlibModuleEmitter) },
        { "primitive:timers", typeof(TimersPrimitiveEmitter) },
        { "primitive:timers/promises", typeof(TimersPromisesPrimitiveEmitter) },
        { "primitive:perf", typeof(PerfPrimitiveEmitter) },
        { "stream", typeof(StreamModuleEmitter) },
        { "stream/promises", typeof(StreamPromisesModuleEmitter) },
        { "stream/web", typeof(StreamWebModuleEmitter) },
        { "http", typeof(HttpModuleEmitter) },
        { "worker_threads", typeof(WorkerThreadsModuleEmitter) },
        { "primitive:dns", typeof(DnsModuleEmitter) },
        { "primitive:dns/promises", typeof(DnsPromisesModuleEmitter) },
        { "primitive:fs/promises", typeof(FsPromisesModuleEmitter) },
        { "primitive:net", typeof(NetModuleEmitter) },
        { "tls", typeof(TlsModuleEmitter) },
        { "dgram", typeof(DgramModuleEmitter) },
        { "cluster", typeof(ClusterModuleEmitter) },
        { "vm", typeof(VmModuleEmitter) },
        { "sharpts:execution", typeof(SourceExecutionModuleEmitter) },
        { "primitive:async_hooks", typeof(AsyncHooksPrimitiveEmitter) },
        { "primitive:tty", typeof(TtyPrimitiveEmitter) },
        { "https", typeof(HttpsModuleEmitterProxy) },
    };

    [Theory]
    [MemberData(nameof(DefaultModules))]
    public void CompilerOwnsCompleteDistinctStrategyInstances(string key, Type expectedType)
    {
        var first = Registry(new ILCompiler("first"));
        var second = Registry(new ILCompiler("second"));
        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
        var strategy = first.GetEmitter(key);
        Assert.IsType(expectedType, strategy);
        Assert.NotSame(strategy, second.GetEmitter(key));
        Assert.Same(strategy, first.GetEmitter(key));
        Assert.Throws<InvalidOperationException>(() => first.RegisterAlias("late", strategy!));
        Assert.Null(first.GetEmitter("late"));
        Assert.Null(second.GetEmitter("late"));
    }

    [Fact]
    public void RegisteredStrategiesAreAvailableBeforeAndAfterCompletion()
    {
        var registry = new BuiltInModuleEmitterRegistry();
        var strategy = new StubEmitter("example");
        Assert.False(registry.IsComplete);
        Assert.Null(registry.GetEmitter("example"));
        registry.Register(strategy);
        Assert.Same(strategy, registry.GetEmitter("example"));
        registry.CompleteRegistration();
        Assert.True(registry.IsComplete);
        Assert.Same(strategy, registry.GetEmitter("example"));
        Assert.Null(registry.GetEmitter("missing"));
        Assert.Throws<InvalidOperationException>(registry.CompleteRegistration);
        Assert.Throws<InvalidOperationException>(() => registry.Register(new StubEmitter("late")));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterAlias("late", strategy));
        Assert.Null(registry.GetEmitter("late"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DuplicateKeysCannotReplaceTheOriginal(bool originalAlias, bool replacementAlias)
    {
        var registry = new BuiltInModuleEmitterRegistry();
        var original = new StubEmitter("example");
        var replacement = new StubEmitter("example");
        if (originalAlias) registry.RegisterAlias("example", original);
        else registry.Register(original);
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (replacementAlias) registry.RegisterAlias("example", replacement);
            else registry.Register(replacement);
        });
        Assert.Throws<InvalidOperationException>(() => registry.Register(original));
        Assert.Same(original, registry.GetEmitter("example"));
        registry.CompleteRegistration();
        Assert.Same(original, registry.GetEmitter("example"));
    }

    [Fact]
    public void AliasesPreserveIdentityWithoutRequiringCanonicalRegistration()
    {
        var registry = new BuiltInModuleEmitterRegistry();
        var strategy = new StubEmitter("canonical");
        registry.RegisterAlias("first", strategy);
        registry.RegisterAlias("second", strategy);
        registry.CompleteRegistration();
        Assert.Same(strategy, registry.GetEmitter("first"));
        Assert.Same(strategy, registry.GetEmitter("second"));
        Assert.Null(registry.GetEmitter("canonical"));
        Assert.Null(registry.GetEmitter("FIRST"));
        var defaults = BuiltInModuleEmitterRegistry.CreateDefault();
        Assert.IsType<ProcessModuleEmitter>(defaults.GetEmitter("primitive:process"));
        Assert.Null(defaults.GetEmitter("process"));
        Assert.Null(defaults.GetEmitter("path"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void InvalidKeysDoNotPoisonRegistration(string? key)
    {
        var registry = new BuiltInModuleEmitterRegistry();
        Assert.ThrowsAny<ArgumentException>(() => registry.Register(new StubEmitter(key!)));
        Assert.ThrowsAny<ArgumentException>(() => registry.RegisterAlias(key!, new StubEmitter("valid")));
        registry.Register(new StubEmitter("valid"));
        registry.CompleteRegistration();
        Assert.NotNull(registry.GetEmitter("valid"));
    }

    [Fact]
    public void NullStrategiesDoNotReserveKeys()
    {
        var registry = new BuiltInModuleEmitterRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        Assert.Throws<ArgumentNullException>(() => registry.RegisterAlias("example", null!));
        var strategy = new StubEmitter("example");
        registry.Register(strategy);
        registry.CompleteRegistration();
        Assert.Same(strategy, registry.GetEmitter("example"));
    }

    private static BuiltInModuleEmitterRegistry Registry(ILCompiler compiler) =>
        (BuiltInModuleEmitterRegistry)typeof(ILCompiler)
            .GetField("_builtInModuleEmitterRegistry", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(compiler)!;

    private sealed class StubEmitter(string name) : IBuiltInModuleEmitter
    {
        public string ModuleName => name;
        public IReadOnlyList<string> GetExportedMembers() => [];
        public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments) => false;
        public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName) => false;
    }
}
