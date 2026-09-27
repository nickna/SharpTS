using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PrivateClassElementRegistryTests
{
    [Fact]
    public void DeclarationSnapshotsPreserveForwardReferencesAndRequireCompleteBodies()
    {
        var registry = new PrivateClassElementRegistry();
        var owner = NewOwner();
        var storage = owner.DefineField("brand", typeof(object), FieldAttributes.Static);
        var method = owner.DefineMethod("Read", MethodAttributes.Public, typeof(int), Type.EmptyTypes);
        var staticMethod = owner.DefineMethod("StaticRead", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        List<string> names = ["second", "first"];
        Dictionary<string, MethodBuilder> methods = new() { ["read"] = method };
        registry.Declare("C", owner, storage, names, new Dictionary<string, FieldBuilder>(), methods,
            new Dictionary<string, MethodBuilder> { ["read"] = staticMethod });
        names.Clear();
        methods.Clear();
        var declaration = registry.Require("C");
        Assert.Equal(new[] { "second", "first" }, declaration.FieldNames);
        Assert.Same(method, declaration.Methods["read"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, MethodBuilder>)declaration.Methods).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)declaration.FieldNames).Clear());
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        EmitResult(method);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        EmitResult(staticMethod);
        registry.MarkBodiesEmitted("C");
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.Same(declaration, registry.Require("C"));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        Assert.Throws<InvalidOperationException>(() => DeclareEmpty(registry, "D", NewOwner()));
        var type = owner.CreateType()!;
        Assert.Equal(42, type.GetMethod("Read")!.Invoke(Activator.CreateInstance(type), null));
        Assert.Equal(42, type.GetMethod("StaticRead")!.Invoke(null, null));
    }

    [Fact]
    public void InvalidDeclarationsAreAtomicAndTypeNamesDoNotDetermineOwnership()
    {
        var registry = new PrivateClassElementRegistry();
        var owner = NewOwner();
        var other = NewOwner();
        var foreign = other.DefineField("foreign", typeof(object), FieldAttributes.Static);
        Assert.False(registry.TryGet("C", out _));
        Assert.Throws<InvalidOperationException>(() => registry.Require("C"));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", owner, foreign, [],
            new Dictionary<string, FieldBuilder>(), new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>()));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", owner, null, ["field"],
            new Dictionary<string, FieldBuilder>(), new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>()));
        Assert.False(registry.TryGet("C", out _));
        DeclareEmpty(registry, "C", owner);
        Assert.Throws<InvalidOperationException>(() => DeclareEmpty(registry, "alias", owner));
        Assert.Throws<InvalidOperationException>(() => DeclareEmpty(registry, "C", other));
        Assert.False(registry.TryGet("alias", out _));
        DeclareEmpty(registry, "module.C", other);
        Assert.NotSame(registry.Require("C"), registry.Require("module.C"));
        registry.MarkBodiesEmitted("C");
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        registry.MarkBodiesEmitted("module.C");
        registry.CompleteEmission();
        var independent = new PrivateClassElementRegistry();
        Assert.False(independent.TryGet("C", out _));
        independent.CompleteEmission();
    }

    private static void DeclareEmpty(PrivateClassElementRegistry registry, string name, TypeBuilder owner)
        => registry.Declare(name, owner, null, [], new Dictionary<string, FieldBuilder>(),
            new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>());

    private static TypeBuilder NewOwner()
        => AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Main").DefineType("C", TypeAttributes.Public);

    private static void EmitResult(MethodBuilder method)
    {
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 42);
        il.Emit(OpCodes.Ret);
    }
}
