using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassPropertyDispatchRegistryTests
{
    [Fact]
    public void ForwardReferencesRemainAvailableAndEveryBodyMustBeEmitted()
    {
        var registry = new ClassPropertyDispatchRegistry();
        var (owner, declaration) = NewDeclaration();
        registry.Declare("C", owner, declaration);
        Assert.Same(declaration, registry.Require("C"));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        EmitBodies(declaration, includeHasProperty: false);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        EmitBoolean(declaration.HasProperty);
        registry.MarkBodiesEmitted("C");
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.Same(declaration, registry.Require("C"));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("D", owner, declaration));
        var type = owner.CreateType()!;
        var instance = Activator.CreateInstance(type);
        Assert.Equal(false, type.GetMethod("HasProperty")!.Invoke(instance, ["missing"]));
    }

    [Fact]
    public void InvalidDeclarationsDoNotPublishAliasesOrReplaceCanonicalOwners()
    {
        var registry = new ClassPropertyDispatchRegistry();
        var first = NewDeclaration();
        var second = NewDeclaration();
        Assert.Equal(first.Owner.Name, second.Owner.Name);
        Assert.False(registry.TryGet("C", out _));
        Assert.Throws<InvalidOperationException>(() => registry.Require("C"));
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", first.Owner, second.Declaration));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", first.Owner,
            first.Declaration with { HasProperty = first.Declaration.GetProperty }));
        Assert.False(registry.Contains("C"));
        registry.Declare("C", first.Owner, first.Declaration);
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", second.Owner, second.Declaration));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("alias", first.Owner, first.Declaration));
        Assert.False(registry.Contains("alias"));
        registry.Declare("other.C", second.Owner, second.Declaration);
        Assert.Same(first.Declaration, registry.Require("C"));
        Assert.Same(second.Declaration, registry.Require("other.C"));
        var separate = new ClassPropertyDispatchRegistry();
        Assert.False(separate.Contains("C"));
        separate.CompleteEmission();
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
    }

    private static (TypeBuilder Owner, ClassPropertyDispatch Declaration) NewDeclaration()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var owner = assembly.DefineDynamicModule("Main").DefineType("C", TypeAttributes.Public);
        var field = owner.DefineField("_fields", typeof(Dictionary<string, object>), FieldAttributes.Private);
        MethodBuilder Method(string name, Type result, params Type[] parameters)
            => owner.DefineMethod(name, MethodAttributes.Public, result, parameters);
        return (owner, new ClassPropertyDispatch(
            Method("EnsureFields", field.FieldType), Method("GetFields", field.FieldType),
            Method("GetProperty", typeof(object), typeof(string)),
            Method("SetProperty", typeof(void), typeof(string), typeof(object)),
            Method("HasProperty", typeof(bool), typeof(string)), field));
    }

    private static void EmitBodies(ClassPropertyDispatch declaration, bool includeHasProperty)
    {
        foreach (var method in new[] { declaration.EnsureFields, declaration.GetFields, declaration.GetProperty })
        {
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
        declaration.SetProperty.GetILGenerator().Emit(OpCodes.Ret);
        if (includeHasProperty)
            EmitBoolean(declaration.HasProperty);
    }

    private static void EmitBoolean(MethodBuilder method)
    {
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
