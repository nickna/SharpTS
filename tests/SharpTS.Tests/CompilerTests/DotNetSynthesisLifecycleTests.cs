using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[CollectionDefinition("DotNetSynthesisLifecycle", DisableParallelization = true)]
public class DotNetSynthesisLifecycleCollection;

[Collection("DotNetSynthesisLifecycle")]
public class DotNetSynthesisLifecycleTests
{
    [Fact]
    public void CacheResetPreservesClrIdentityForPreviouslyCheckedInstances()
    {
        var original = DotNetTypeSynthesizer.Synthesize(typeof(StringBuilder));
        var retained = new TypeInfo.Instance(original);
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(retained, out var before));
        Assert.Same(typeof(StringBuilder), before);

        DotNetTypeSynthesizer.ClearCache();
        var replacement = DotNetTypeSynthesizer.Synthesize(typeof(StringBuilder));
        Assert.NotSame(original, replacement);
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(retained, out var after));
        Assert.Same(typeof(StringBuilder), after);
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(new TypeInfo.Instance(replacement), out var current));
        Assert.Same(typeof(StringBuilder), current);
    }

    [Fact]
    public void FailedConstructionDoesNotPublishOrPoisonAType()
    {
        var attempts = 0;
        var cache = new DotNetSynthesisCache(type =>
        {
            if (++attempts == 1) throw new InvalidOperationException("incomplete");
            return new TypeInfo.MutableClass(type.Name).Freeze();
        });
        Assert.Throws<InvalidOperationException>(() => cache.GetOrCreate(typeof(string)));
        var result = cache.GetOrCreate(typeof(string));
        Assert.Same(result, cache.GetOrCreate(typeof(string)));
        Assert.Equal(2, attempts);
        Assert.True(cache.TryGetClrType(result, out var actual));
        Assert.Same(typeof(string), actual);
    }

    [Fact]
    public void EqualGuestDeclarationsCannotAcquireAnImportedIdentity()
    {
        var imported = DotNetTypeSynthesizer.Synthesize(typeof(StringBuilder));
        var copy = imported with { };
        Assert.Equal(imported, copy);
        Assert.False(DotNetTypeSynthesizer.TryGetClrType(new TypeInfo.Instance(copy), out _));
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(new TypeInfo.Instance(imported), out _));
    }

    [Fact]
    public async Task ConcurrentImportersReceiveOneCompletedIdentity()
    {
        var cache = new DotNetSynthesisCache(type => new TypeInfo.MutableClass(type.Name).Freeze());
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            var result = cache.GetOrCreate(typeof(StringBuilder));
            Assert.True(cache.TryGetClrType(result, out var type));
            Assert.Same(typeof(StringBuilder), type);
            return result;
        })));
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public async Task ResetDuringConstructionKeepsBothGenerationsResolvable()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var cache = new DotNetSynthesisCache(type =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            return new TypeInfo.MutableClass(type.Name).Freeze();
        });
        var pending = Task.Run(() => cache.GetOrCreate(typeof(StringBuilder)));
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            cache.Reset();
            var current = cache.GetOrCreate(typeof(StringBuilder));
            release.Set();
            var previous = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotSame(previous, current);
            Assert.Same(current, cache.GetOrCreate(typeof(StringBuilder)));
            Assert.True(cache.TryGetClrType(previous, out var oldType));
            Assert.True(cache.TryGetClrType(current, out var newType));
            Assert.Same(oldType, newType);
            Assert.Same(typeof(StringBuilder), oldType);
        }
        finally
        {
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void UnusedCollectibleTypesAreNotRootedBySynthesis()
    {
        var weak = SynthesizeCollectible();
        for (var i = 0; i < 10 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(weak.IsAlive);
    }

    public sealed class FluentFixture
    {
        public FluentFixture Next() => this;
    }

    [Fact]
    public void SelfReturnForwardReferenceSurvivesReset()
    {
        var declaration = DotNetTypeSynthesizer.Synthesize(typeof(FluentFixture));
        var method = Assert.IsType<TypeInfo.Function>(declaration.Methods["next"]);
        var result = Assert.IsType<TypeInfo.Instance>(method.ReturnType);
        Assert.Same(declaration, result.ResolvedClassType);
        DotNetTypeSynthesizer.ClearCache();
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(result, out var type));
        Assert.Same(typeof(FluentFixture), type);
    }

    [Fact]
    public void RetainedDeclarationKeepsItsCollectibleClrTypeUsable()
    {
        var declaration = RetainCollectibleDeclaration();
        DotNetTypeSynthesizer.ClearCache();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(new TypeInfo.Instance(declaration), out var type));
        Assert.Equal("RetainedImport", type.Name);
        Assert.NotNull(Activator.CreateInstance(type));
        GC.KeepAlive(declaration);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TypeInfo.Class RetainCollectibleDeclaration()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("RetainedSynthesis_" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main")
            .DefineType("RetainedImport", TypeAttributes.Public).CreateType()!;
        return DotNetTypeSynthesizer.Synthesize(type);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SynthesizeCollectible()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SynthesisLifetime_" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main")
            .DefineType("CollectibleImport", TypeAttributes.Public).CreateType()!;
        var declaration = DotNetTypeSynthesizer.Synthesize(type);
        Assert.True(DotNetTypeSynthesizer.TryGetClrType(new TypeInfo.Instance(declaration), out var actual));
        Assert.Same(type, actual);
        return new WeakReference(type);
    }
}
