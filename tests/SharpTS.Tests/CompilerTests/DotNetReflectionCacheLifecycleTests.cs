using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Runtime.DotNet;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[CollectionDefinition("DotNetReflectionCacheLifecycle", DisableParallelization = true)]
public class DotNetReflectionCacheLifecycleCollection;

[Collection("DotNetReflectionCacheLifecycle")]
public class DotNetReflectionCacheLifecycleTests
{
    [Theory]
    [InlineData("name")]
    [InlineData("method")]
    [InlineData("property")]
    [InlineData("event")]
    [InlineData("indexer")]
    public void UnusedCollectibleTypesAreNotRootedByLookup(string lookup)
    {
        DotNetTypeRegistry.ClearCache();
        var weak = LookupCollectible(lookup);
        for (var i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(weak.IsAlive);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("property")]
    [InlineData("event")]
    [InlineData("indexer")]
    public void ConstructedGenericTypesDoNotRootCollectibleArguments(string lookup)
    {
        var weak = LookupCollectible(lookup, constructed: true);
        Collect();
        Assert.False(weak.IsAlive);
    }

    [Fact]
    public void ResetReplacesCachedArraysWithoutInvalidatingRetainedMetadata()
    {
        var type = typeof(List<int>);
        var methods = DotNetTypeRegistry.GetMethods(type, "add", false);
        var indexers = DotNetTypeRegistry.GetIndexers(type, true);
        Assert.Same(methods, DotNetTypeRegistry.GetMethods(type, "add", false));
        Assert.Same(indexers, DotNetTypeRegistry.GetIndexers(type, true));
        DotNetTypeRegistry.ClearCache();
        Assert.NotSame(methods, DotNetTypeRegistry.GetMethods(type, "add", false));
        Assert.NotSame(indexers, DotNetTypeRegistry.GetIndexers(type, true));
        var list = new List<int>();
        Assert.Single(methods).Invoke(list, [7]);
        Assert.Single(indexers).SetValue(list, 9, [0]);
        Assert.Equal(9, list[0]);
    }

    [Fact]
    public async Task ConcurrentLookupsShareCompletedResults()
    {
        DotNetTypeRegistry.ClearCache();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            DotNetTypeRegistry.GetMethods(typeof(List<int>), "add", false))));
        Assert.All(results, result => Assert.Same(results[0], result));
        Assert.Single(results[0]);
    }

    private sealed class ControlledType(Func<int, MethodInfo[]> lookup) : TypeDelegator(typeof(List<int>))
    {
        private int _calls;
        public override MethodInfo[] GetMethods(BindingFlags bindingAttr) =>
            lookup(Interlocked.Increment(ref _calls));
    }

    [Fact]
    public void FailedReflectionLookupRemainsRetryable()
    {
        var type = new ControlledType(call => call == 1
            ? throw new TypeLoadException("incomplete")
            : typeof(List<int>).GetMethods());
        Assert.Throws<TypeLoadException>(() => DotNetTypeRegistry.GetMethods(type, "add", false));
        var methods = DotNetTypeRegistry.GetMethods(type, "add", false);
        Assert.Single(methods);
        Assert.Same(methods, DotNetTypeRegistry.GetMethods(type, "add", false));
    }

    [Fact]
    public async Task ResetDuringLookupDoesNotRepopulateTheNewGeneration()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var type = new ControlledType(call =>
        {
            if (call == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            return typeof(List<int>).GetMethods();
        });
        var pending = Task.Run(() => DotNetTypeRegistry.GetMethods(type, "add", false));
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            DotNetTypeRegistry.ClearCache();
            var current = DotNetTypeRegistry.GetMethods(type, "add", false);
            release.Set();
            var previous = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotSame(previous, current);
            Assert.Same(current, DotNetTypeRegistry.GetMethods(type, "add", false));
            Assert.Single(previous);
            Assert.Single(current);
        }
        finally
        {
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void RetainedCollectibleMetadataRemainsUsableAfterReset()
    {
        var indexer = RetainCollectibleIndexer();
        DotNetTypeRegistry.ClearCache();
        Collect();
        var list = Activator.CreateInstance(indexer.DeclaringType!)!;
        var element = Activator.CreateInstance(indexer.PropertyType);
        indexer.DeclaringType!.GetMethod("Add")!.Invoke(list, [element]);
        Assert.Same(element, indexer.GetValue(list, [0]));
        GC.KeepAlive(indexer);
    }

    private static void Collect()
    {
        for (var i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static PropertyInfo RetainCollectibleIndexer() =>
        Assert.Single(DotNetTypeRegistry.GetIndexers(typeof(List<>).MakeGenericType(CreateCollectible()), false));

    private static Type CreateCollectible()
    {
        var name = "CollectibleReflection_" + Guid.NewGuid().ToString("N");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);
        return assembly.DefineDynamicModule("main")
            .DefineType(name, TypeAttributes.Public).CreateType()!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LookupCollectible(string lookup, bool constructed = false)
    {
        var element = CreateCollectible();
        var type = constructed ? typeof(List<>).MakeGenericType(element) : element;
        Assert.True(type.IsCollectible);
        switch (lookup)
        {
            case "name": Assert.Same(type, DotNetTypeRegistry.Resolve(type.FullName!)); break;
            case "method": Assert.NotEmpty(DotNetTypeRegistry.GetMethods(type, "toString", false)); break;
            case "property":
                if (constructed) Assert.IsAssignableFrom<PropertyInfo>(DotNetTypeRegistry.GetPropertyOrField(type, "count", false));
                else Assert.Null(DotNetTypeRegistry.GetPropertyOrField(type, "missing", false));
                break;
            case "event": Assert.Null(DotNetTypeRegistry.GetEvent(type, "missing", false)); break;
            case "indexer":
                if (constructed) Assert.Single(DotNetTypeRegistry.GetIndexers(type, false));
                else Assert.Empty(DotNetTypeRegistry.GetIndexers(type, false));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(lookup));
        }
        return new WeakReference(element);
    }
}
