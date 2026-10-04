using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SharpTS.Compilation;
using SharpTS.Runtime.DotNet;
using Xunit;

namespace SharpTS.Tests.Compilation;

[Collection("DotNetReflectionCacheLifecycle")]
public sealed class TypeProviderCacheOwnershipTests
{
    public sealed class Probe(string value)
    {
        public string Value { get; } = value;
        public void Accept(string value) { }
    }

    private sealed class CountingType : TypeDelegator
    {
        public int MethodLookups { get; private set; }
        public int ConstructorLookups { get; private set; }
        public CountingType() : base(typeof(Probe)) { }
        protected override MethodInfo? GetMethodImpl(string name, BindingFlags flags, Binder? binder,
            CallingConventions convention, Type[]? types, ParameterModifier[]? modifiers)
        {
            MethodLookups++;
            return typeof(Probe).GetMethod(name, flags, binder, convention, types!, modifiers);
        }
        protected override ConstructorInfo? GetConstructorImpl(BindingFlags flags, Binder? binder,
            CallingConventions convention, Type[] types, ParameterModifier[]? modifiers)
        {
            ConstructorLookups++;
            return typeof(Probe).GetConstructor(flags, binder, convention, types, modifiers);
        }
    }

    [Fact]
    public void SignatureKeysDoNotBorrowCallerArrays()
    {
        var type = new CountingType();
        Type[] methodParameters = [typeof(string)];
        var method = TypeProvider.Runtime.GetMethod(type, "Accept", methodParameters);
        methodParameters[0] = typeof(int);
        Assert.Same(method, TypeProvider.Runtime.GetMethod(type, "Accept", typeof(string)));
        Assert.Equal(1, type.MethodLookups);
        Type[] constructorParameters = [typeof(string)];
        var constructor = TypeProvider.Runtime.GetConstructor(type, constructorParameters);
        constructorParameters[0] = typeof(int);
        Assert.Same(constructor, TypeProvider.Runtime.GetConstructor(type, typeof(string)));
        Assert.Equal(1, type.ConstructorLookups);
    }

    [Fact]
    public void ReturnedReflectionArraysCannotPoisonLaterConsumers()
    {
        DotNetTypeRegistry.ClearCache();
        var methods = DotNetTypeRegistry.GetMethods(typeof(List<int>), "add", false);
        var method = Assert.Single(methods);
        methods[0] = null!;
        Assert.Same(method, Assert.Single(DotNetTypeRegistry.GetMethods(typeof(List<int>), "add", false)));
        var indexers = DotNetTypeRegistry.GetIndexers(typeof(List<int>), true);
        var indexer = Assert.Single(indexers);
        indexers[0] = null!;
        Assert.Same(indexer, Assert.Single(DotNetTypeRegistry.GetIndexers(typeof(List<int>), true)));
    }

    [Fact]
    public void MetadataDoesNotRootCollectibleLoadedAssemblies()
    {
        var context = LookupLoadedCollectible();
        for (int i = 0; i < 15 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(context.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LookupLoadedCollectible()
    {
        var context = new AssemblyLoadContext("metadata_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(typeof(Probe).Assembly.Location);
        var type = assembly.GetType(typeof(Probe).FullName!)!;
        Assert.True(type.IsCollectible);
        Assert.False(type.Assembly.IsDynamic);
        using (context.EnterContextualReflection())
            Assert.Same(type, TypeProvider.Runtime.Resolve(type.AssemblyQualifiedName!));
        _ = TypeProvider.Runtime.GetMethod(type, "Accept", typeof(string));
        _ = TypeProvider.Runtime.GetProperty(type, "Value");
        _ = TypeProvider.Runtime.GetConstructor(type, typeof(string));
        context.Unload();
        return new WeakReference(context);
    }
}
