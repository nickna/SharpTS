using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassGenericParameterRegistryTests
{
    private static ModuleBuilder Module() => new PersistedAssemblyBuilder(
        new AssemblyName(Guid.NewGuid().ToString("N")), typeof(object).Assembly).DefineDynamicModule("Main");

    [Fact]
    public void DeclarationSnapshotsParametersAndSeparatesSameNamedOwners()
    {
        var module = Module();
        var first = module.DefineType("Left.Box");
        var second = module.DefineType("Right.Box");
        var parameters = first.DefineGenericParameters("T", "U").ToArray();
        var other = second.DefineGenericParameters("T");
        var registry = new ClassGenericParameterRegistry();
        registry.Declare(first, parameters);
        registry.Declare(second, other);
        var declared = registry.Require(first);
        parameters[0] = other[0];
        Assert.Same(first.GetGenericArguments()[0], declared[0]);
        Assert.Same(second.GetGenericArguments()[0], registry.Require(second)[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<GenericTypeParameterBuilder>)declared).Clear());
        registry.CompleteEmission([first, second]);
        Assert.True(registry.IsComplete);
        Assert.Same(declared, registry.Require(first));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(first, declared));
        Assert.Throws<InvalidOperationException>(() => registry.CompleteEmission([first, second]));
    }

    [Fact]
    public void InvalidDeclarationsAreAtomicAndCompletionRequiresEveryOwner()
    {
        var module = Module();
        var owner = module.DefineType("Box");
        var parameters = owner.DefineGenericParameters("T", "U");
        var foreign = module.DefineType("Other");
        var foreignParameters = foreign.DefineGenericParameters("T", "U");
        var plain = module.DefineType("Plain");
        var registry = new ClassGenericParameterRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Require(plain));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(owner, []));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(owner, foreignParameters));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(owner, [parameters[1], parameters[0]]));
        Assert.Throws<InvalidOperationException>(() => registry.Require(owner));
        registry.Declare(owner, parameters);
        Assert.Throws<InvalidOperationException>(() => registry.Declare(owner, parameters));
        Assert.Throws<InvalidOperationException>(() => registry.CompleteEmission([owner, plain]));
        Assert.False(registry.IsComplete);
        registry.Declare(plain, []);
        Assert.Empty(registry.Require(plain));
        registry.CompleteEmission([owner, plain, owner]);
        Assert.True(registry.IsComplete);
    }

    [Fact]
    public void CompletionDetectsOwnerMetadataChangedAfterDeclaration()
    {
        var owner = Module().DefineType("LateGeneric");
        var registry = new ClassGenericParameterRegistry();
        registry.Declare(owner, []);
        owner.DefineGenericParameters("T");
        Assert.Throws<InvalidOperationException>(() => registry.CompleteEmission([owner]));
        Assert.False(registry.IsComplete);
    }
}
