using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Compilation.Emitters;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Tests.CompilerTests;

public class TypeEmitterRegistryTests
{
    public static TheoryData<TI, Type> DefaultInstances => new()
    {
        { new TI.String(), typeof(StringEmitter) },
        { new TI.StringLiteral("value"), typeof(StringEmitter) },
        { new TI.Array(new TI.Any()), typeof(ArrayEmitter) },
        { new TI.Tuple([], 0), typeof(ArrayEmitter) },
        { new TI.Buffer(), typeof(BufferEmitter) },
        { new TI.EventEmitter(), typeof(EventEmitterEmitter) },
        { new TI.Date(), typeof(DateEmitter) },
        { new TI.Map(new TI.Any(), new TI.Any()), typeof(MapEmitter) },
        { new TI.Set(new TI.Any()), typeof(SetEmitter) },
        { new TI.WeakMap(new TI.Any(), new TI.Any()), typeof(WeakMapEmitter) },
        { new TI.WeakSet(new TI.Any()), typeof(WeakSetEmitter) },
        { new TI.WeakRef(new TI.Any()), typeof(WeakRefEmitter) },
        { new TI.FinalizationRegistry(new TI.Any()), typeof(FinalizationRegistryEmitter) },
        { new TI.RegExp(), typeof(RegExpEmitter) },
        { new TI.AsyncGenerator(new TI.Any()), typeof(AsyncGeneratorEmitter) },
        { new TI.Error(), typeof(ErrorEmitter) },
        { new TI.SharedArrayBuffer(), typeof(SharedArrayBufferEmitter) },
        { new TI.ArrayBuffer(), typeof(ArrayBufferEmitter) },
        { new TI.DataView(), typeof(DataViewEmitter) },
        { new TI.AbortController(), typeof(AbortControllerEmitter) },
        { new TI.AbortSignal(), typeof(AbortSignalEmitter) },
        { new TI.Iterator(new TI.Any()), typeof(IteratorEmitter) },
        { new TI.Generator(new TI.Any()), typeof(IteratorEmitter) },
    };

    public static TheoryData<string, Type> DefaultStatics => new()
    {
        { "Math", typeof(MathStaticEmitter) },
        { "JSON", typeof(JSONStaticEmitter) },
        { "Object", typeof(ObjectStaticEmitter) },
        { "Array", typeof(ArrayStaticEmitter) },
        { "Buffer", typeof(BufferStaticEmitter) },
        { "Number", typeof(NumberStaticEmitter) },
        { "Promise", typeof(PromiseStaticEmitter) },
        { "Error", typeof(ErrorStaticEmitter) },
        { "Symbol", typeof(SymbolStaticEmitter) },
        { "Map", typeof(MapStaticEmitter) },
        { "String", typeof(StringStaticEmitter) },
        { "Boolean", typeof(BooleanStaticEmitter) },
        { "process", typeof(ProcessStaticEmitter) },
        { "globalThis", typeof(GlobalThisStaticEmitter) },
        { "Atomics", typeof(AtomicsStaticEmitter) },
        { "ArrayBuffer", typeof(ArrayBufferStaticEmitter) },
        { "Reflect", typeof(ReflectStaticEmitter) },
        { "Proxy", typeof(ProxyStaticEmitter) },
        { "AbortSignal", typeof(AbortSignalStaticEmitter) },
        { "Response", typeof(ResponseStaticEmitter) },
        { "Iterator", typeof(IteratorStaticEmitter) },
        { "RegExp", typeof(RegExpStaticEmitter) },
        { "Date", typeof(DateStaticEmitter) },
        { "ReadableStream", typeof(ReadableStreamStaticEmitter) },
    };

    [Theory]
    [MemberData(nameof(DefaultInstances))]
    public void CompilerOwnsCompleteInstanceStrategies(TI receiver, Type expected)
    {
        var first = Registry(new ILCompiler("first"));
        var second = Registry(new ILCompiler("second"));
        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
        var strategy = first.GetStrategy(receiver);
        Assert.IsType(expected, strategy);
        Assert.Same(strategy, first.GetStrategy(receiver));
        Assert.NotSame(strategy, second.GetStrategy(receiver));
        Assert.Throws<InvalidOperationException>(() => first.Register<TI.Any>(strategy!));
        Assert.Null(first.GetStrategy(new TI.Any()));
    }

    [Theory]
    [MemberData(nameof(DefaultStatics))]
    public void CompilerOwnsCompleteStaticStrategies(string name, Type expected)
    {
        var first = Registry(new ILCompiler("first"));
        var second = Registry(new ILCompiler("second"));
        Assert.True(first.IsComplete);
        Assert.True(second.IsComplete);
        var strategy = first.GetStaticStrategy(name);
        Assert.IsType(expected, strategy);
        Assert.Same(strategy, first.GetStaticStrategy(name));
        Assert.NotSame(strategy, second.GetStaticStrategy(name));
        Assert.Throws<InvalidOperationException>(() => first.RegisterStatic("late", strategy!));
        Assert.Null(first.GetStaticStrategy("late"));
    }

    [Fact]
    public void ForwardLookupsAndMissingTypesRemainAvailableAcrossCompletion()
    {
        var registry = new TypeEmitterRegistry();
        var instance = new StringEmitter();
        var statics = new StringStaticEmitter();
        Assert.False(registry.IsComplete);
        Assert.Null(registry.GetStrategy(new TI.String()));
        Assert.Null(registry.GetStaticStrategy("String"));
        registry.Register<TI.String>(instance);
        registry.RegisterStatic("String", statics);
        Assert.Same(instance, registry.GetStrategy(new TI.String()));
        Assert.Same(statics, registry.GetStaticStrategy("String"));
        registry.CompleteRegistration();
        Assert.True(registry.IsComplete);
        Assert.Same(instance, registry.GetStrategy(new TI.String()));
        Assert.Same(statics, registry.GetStaticStrategy("String"));
        Assert.Null(registry.GetStrategy(new TI.Any()));
        Assert.Null(registry.GetStaticStrategy("missing"));
        Assert.Throws<InvalidOperationException>(registry.CompleteRegistration);
        Assert.Throws<InvalidOperationException>(() => registry.Register<TI.Any>(instance));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterStatic("missing", statics));
        Assert.Null(registry.GetStrategy(new TI.Any()));
        Assert.Null(registry.GetStaticStrategy("missing"));
    }

    [Fact]
    public void DuplicateKeysCannotReplaceEitherDispatchTable()
    {
        var registry = new TypeEmitterRegistry();
        var instance = new StringEmitter();
        var statics = new StringStaticEmitter();
        registry.Register<TI.String>(instance);
        registry.RegisterStatic("String", statics);
        Assert.Throws<InvalidOperationException>(() => registry.Register<TI.String>(new StringEmitter()));
        Assert.Throws<InvalidOperationException>(() => registry.RegisterStatic("String", new StringStaticEmitter()));
        Assert.Same(instance, registry.GetStrategy(new TI.String()));
        Assert.Same(statics, registry.GetStaticStrategy("String"));
    }

    [Fact]
    public void NullStrategiesDoNotReserveKeys()
    {
        var registry = new TypeEmitterRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register<TI.String>(null!));
        Assert.Throws<ArgumentNullException>(() => registry.RegisterStatic("String", null!));
        var instance = new StringEmitter();
        var statics = new StringStaticEmitter();
        registry.Register<TI.String>(instance);
        registry.RegisterStatic("String", statics);
        registry.CompleteRegistration();
        Assert.Same(instance, registry.GetStrategy(new TI.String()));
        Assert.Same(statics, registry.GetStaticStrategy("String"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void InvalidStaticNamesAreRejected(string? name)
    {
        var registry = new TypeEmitterRegistry();
        Assert.ThrowsAny<ArgumentException>(() => registry.RegisterStatic(name!, new StringStaticEmitter()));
        registry.CompleteRegistration();
        Assert.Null(registry.GetStaticStrategy("String"));
    }

    [Fact]
    public void StaticNamesAreOrdinalAndInstanceKeysRemainIndependent()
    {
        var registry = new TypeEmitterRegistry();
        var upper = new StringStaticEmitter();
        var lower = new StringStaticEmitter();
        registry.RegisterStatic("String", upper);
        registry.RegisterStatic("string", lower);
        registry.Register<TI.String>(new StringEmitter());
        registry.CompleteRegistration();
        Assert.Same(upper, registry.GetStaticStrategy("String"));
        Assert.Same(lower, registry.GetStaticStrategy("string"));
        Assert.Null(registry.GetStaticStrategy("STRING"));
        Assert.NotNull(registry.GetStrategy(new TI.String()));
    }

    [Fact]
    public void DefaultAliasesPreserveTheirOriginalSharing()
    {
        var registry = TypeEmitterRegistry.CreateDefault();
        Assert.Same(registry.GetStrategy(new TI.String()), registry.GetStrategy(new TI.StringLiteral("value")));
        Assert.Same(registry.GetStrategy(new TI.Iterator(new TI.Any())), registry.GetStrategy(new TI.Generator(new TI.Any())));
        Assert.NotSame(registry.GetStrategy(new TI.Array(new TI.Any())), registry.GetStrategy(new TI.Tuple([], 0)));
    }

    [Fact]
    public void GlobalThisDelegationUsesTheOwningRegistry()
    {
        var first = TypeEmitterRegistry.CreateDefault();
        var second = TypeEmitterRegistry.CreateDefault();
        var field = typeof(GlobalThisStaticEmitter).GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Same(first, field.GetValue(first.GetStaticStrategy("globalThis")));
        Assert.Same(second, field.GetValue(second.GetStaticStrategy("globalThis")));
    }

    [Fact]
    public void ClassInstancesRetainFallbackEvenWhenAnInstanceKeyIsRegistered()
    {
        var registry = new TypeEmitterRegistry();
        registry.Register<TI.Instance>(new StringEmitter());
        var receiver = new TI.Instance(new TI.Any());
        Assert.Null(registry.GetStrategy(receiver));
        registry.CompleteRegistration();
        Assert.Null(registry.GetStrategy(receiver));
        Assert.Null(TypeEmitterRegistry.CreateDefault().GetStrategy(receiver));
    }

    private static TypeEmitterRegistry Registry(ILCompiler compiler) =>
        (TypeEmitterRegistry)typeof(ILCompiler)
            .GetField("_typeEmitterRegistry", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(compiler)!;
}
