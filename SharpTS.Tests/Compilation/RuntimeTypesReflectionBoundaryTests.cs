using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.Compilation;

public sealed class RuntimeTypesReflectionBoundaryTests
{
    private sealed class TSSymbol;

    private sealed class ManagedCallable
    {
        public bool WasInvoked { get; private set; }

        public object? Invoke(object?[] args)
        {
            Assert.Empty(args);
            WasInvoked = true;
            return null;
        }
    }

    [Fact]
    public void DisposeResource_PreservesOpenWorldManagedCallableSupport()
    {
        var resource = new object();
        var disposeSymbol = new TSSymbol();
        var callable = new ManagedCallable();

        RuntimeTypes.SetIndex(resource, disposeSymbol, callable);
        RuntimeTypes.DisposeResource(resource, disposeSymbol);

        Assert.True(callable.WasInvoked);
    }
}
