using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Compilation.CallHandlers;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class CallHandlerRegistryTests
{
    [Fact]
    public void SharedDefaultIsCompleteAndStateless()
    {
        var registry = (CallHandlerRegistry)typeof(ExpressionEmitterBase)
            .GetField("_callHandlers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.True(registry.IsComplete);
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Handler(0, () => false)));
        var handlers = Handlers(registry);
        Assert.Equal(new[] { 10, 15, 16, 20, 30, 32, 35, 40, 43, 44, 45, 46, 50, 60, 72, 74, 76, 80 },
            handlers.Select(handler => handler.Priority));
        Assert.Equal(new[] { typeof(SuperConstructorHandler), typeof(ObjectRestHandler),
            typeof(ArrayDestructureHandler), typeof(ConsoleMethodHandler), typeof(StaticTypeHandler),
            typeof(GlobalThisChainHandler), typeof(DateStaticHandler), typeof(BuiltInModuleHandler),
            typeof(ProcessStreamHandler), typeof(CookieJarHandler), typeof(TimerHandler),
            typeof(FetchHandler), typeof(GlobalFunctionHandler), typeof(BuiltInConstructorHandler),
            typeof(ImportedClassStaticHandler), typeof(ClassExprStaticHandler),
            typeof(ThisStaticContextHandler), typeof(AsyncFunctionCallHandler) },
            handlers.Select(handler => handler.GetType()));
        foreach (var handler in handlers)
            for (var type = handler.GetType(); type != null; type = type.BaseType)
                Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void DefaultConstructorAllowsExtensionsBeforeCompletion()
    {
        var registry = new CallHandlerRegistry();
        Assert.False(registry.IsComplete);
        var handler = new Handler(0, () => true);
        registry.Register(handler);
        registry.CompleteRegistration();
        Assert.Same(handler, Handlers(registry)[0]);
        Assert.True(registry.TryHandle(null!, null!));
    }

    [Fact]
    public void IncompleteRegistryCannotDispatch()
    {
        var called = false;
        var registry = new CallHandlerRegistry([new Handler(0, () => called = true)]);
        Assert.Throws<InvalidOperationException>(() => registry.TryHandle(null!, null!));
        Assert.False(called);
    }

    [Fact]
    public void RegistrationRejectsNullAndDuplicateIdentityWithoutChangingChain()
    {
        var handler = new Handler(0, () => false);
        var registry = new CallHandlerRegistry([handler]);
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
        Assert.Throws<InvalidOperationException>(() => registry.Register(handler));
        Assert.Same(handler, Assert.Single(Handlers(registry)));
        Assert.Throws<ArgumentNullException>(() => new CallHandlerRegistry(null!));
        Assert.Throws<ArgumentNullException>(() => new CallHandlerRegistry([null!]));
        Assert.Throws<InvalidOperationException>(() => new CallHandlerRegistry([handler, handler]));
    }

    [Fact]
    public void CompletionRejectsRepeatedCompletionAndLateRegistration()
    {
        var registry = new CallHandlerRegistry([]);
        registry.CompleteRegistration();
        Assert.Throws<InvalidOperationException>(() => registry.CompleteRegistration());
        Assert.Throws<InvalidOperationException>(() => registry.Register(new Handler(0, () => true)));
        Assert.False(registry.TryHandle(null!, null!));
    }

    [Fact]
    public void DispatchUsesPriorityAndStableTiesAndStopsAfterFirstMatch()
    {
        List<int> visited = [];
        var registry = new CallHandlerRegistry([
            new Handler(30, () => { visited.Add(4); return true; }),
            new Handler(20, () => { visited.Add(2); return false; }),
            new Handler(10, () => { visited.Add(1); return false; }),
            new Handler(20, () => { visited.Add(3); return true; })]);
        registry.CompleteRegistration();
        Assert.True(registry.TryHandle(null!, null!));
        Assert.Equal(new[] { 1, 2, 3 }, visited);
    }

    [Fact]
    public void InputSequenceIsSnapshotAndAllMissesReturnFalse()
    {
        int count = 0;
        List<ICallHandler> input = [new Handler(0, () => { count++; return false; })];
        var registry = new CallHandlerRegistry(input);
        input.Clear();
        input.Add(new Handler(-1, () => true));
        registry.CompleteRegistration();
        Assert.False(registry.TryHandle(null!, null!));
        Assert.Equal(1, count);
    }

    [Fact]
    public void CallbackCannotMutateCompletedDispatchChain()
    {
        var registry = new CallHandlerRegistry([]);
        registry.Register(new Handler(0, () =>
        {
            Assert.Throws<InvalidOperationException>(() => registry.Register(new Handler(-1, () => true)));
            return false;
        }));
        registry.Register(new Handler(1, () => true));
        registry.CompleteRegistration();
        Assert.True(registry.TryHandle(null!, null!));
        Assert.True(registry.TryHandle(null!, null!));
        Assert.Equal(2, Handlers(registry).Count);
    }

    [Fact]
    public void DispatchPassesOriginalContextAndCallToEveryHandler()
    {
        var context = DispatchProxy.Create<IEmitterContext, ContextProxy>();
        var call = new Expr.Call(new Expr.Literal(null), null!, null, []);
        int visits = 0;
        var registry = new CallHandlerRegistry([
            new ForwardingHandler((actualContext, actualCall) =>
            {
                Assert.Same(context, actualContext);
                Assert.Same(call, actualCall);
                visits++;
                return false;
            }),
            new ForwardingHandler((actualContext, actualCall) =>
            {
                Assert.Same(context, actualContext);
                Assert.Same(call, actualCall);
                visits++;
                return true;
            })]);
        registry.CompleteRegistration();
        Assert.True(registry.TryHandle(context, call));
        Assert.Equal(2, visits);
    }

    public class ContextProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Registry dispatch must not inspect the context.");
    }

    private sealed class ForwardingHandler(Func<IEmitterContext, Expr.Call, bool> handle) : ICallHandler
    {
        public bool TryHandle(IEmitterContext emitter, Expr.Call call) => handle(emitter, call);
    }

    private static List<ICallHandler> Handlers(CallHandlerRegistry registry) =>
        (List<ICallHandler>)typeof(CallHandlerRegistry)
            .GetField("_handlers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;

    private sealed class Handler(int priority, Func<bool> handle) : ICallHandler
    {
        public int Priority => priority;
        public bool TryHandle(IEmitterContext emitter, Expr.Call call) => handle();
    }
}
