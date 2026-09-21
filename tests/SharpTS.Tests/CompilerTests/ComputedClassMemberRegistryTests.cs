using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ComputedClassMemberRegistryTests
{
    [Fact]
    public void ForwardDeclarationsAreReadOnlyAndCompletionRequiresEveryBody()
    {
        var registry = new ComputedClassMemberRegistry();
        var (owner, method, accessor, methodBuilder, accessorBuilder) = NewMembers();
        var input = new List<(Stmt.Function, Expr, MethodBuilder)> { (method, method.ComputedKey!, methodBuilder) };
        registry.DeclareMethods(owner, input);
        input.Clear();
        registry.DeclareAccessor(owner, accessor, accessorBuilder);
        var methods = registry.GetMethods(owner);
        var accessors = registry.GetAccessors(owner);
        Assert.Same(methodBuilder, Assert.Single(methods).Builder);
        Assert.Same(accessorBuilder, Assert.Single(accessors).Method);
        Assert.Throws<NotSupportedException>(() => ((IList)methods).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)accessors).Clear());
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodyEmitted(methodBuilder));
        EmitValue(methodBuilder);
        registry.MarkBodyEmitted(methodBuilder);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodyEmitted(methodBuilder));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        EmitValue(accessorBuilder);
        registry.MarkBodyEmitted(accessorBuilder);
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.Same(methods, registry.GetMethods(owner));
        Assert.Same(accessors, registry.GetAccessors(owner));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethods(owner, []));
        Assert.Throws<InvalidOperationException>(() => registry.DeclareAccessor(owner, accessor, accessorBuilder));
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodyEmitted(accessorBuilder));
        var type = owner.CreateType()!;
        var instance = Activator.CreateInstance(type);
        Assert.Equal("value", type.GetMethod("Method")!.Invoke(instance, null));
        Assert.Equal("value", type.GetMethod("Accessor")!.Invoke(instance, null));
    }

    [Fact]
    public void EqualTypeNamesAndSeparateCompilationsDoNotAliasMetadata()
    {
        var registry = new ComputedClassMemberRegistry();
        var first = NewMembers();
        var second = NewMembers();
        Assert.Equal(first.Owner.Name, second.Owner.Name);
        registry.DeclareMethods(first.Owner, [(first.Method, first.Method.ComputedKey!, first.MethodBuilder)]);
        registry.DeclareMethods(second.Owner, [(second.Method, second.Method.ComputedKey!, second.MethodBuilder)]);
        Assert.Same(first.MethodBuilder, Assert.Single(registry.GetMethods(first.Owner)).Builder);
        Assert.Same(second.MethodBuilder, Assert.Single(registry.GetMethods(second.Owner)).Builder);
        Assert.Empty(registry.GetAccessors(first.Owner));
        var empty = new ComputedClassMemberRegistry();
        Assert.False(empty.HasMethods(first.Owner));
        Assert.Empty(empty.GetMethods(first.Owner));
        Assert.Empty(empty.GetAccessors(first.Owner));
        Assert.Throws<InvalidOperationException>(() => empty.MarkBodyEmitted(first.MethodBuilder));
        empty.CompleteEmission();
        Assert.False(registry.IsComplete);
    }

    [Fact]
    public void InvalidAndDuplicateDeclarationsCannotPartiallyPublish()
    {
        var registry = new ComputedClassMemberRegistry();
        var item = NewMembers();
        var other = NewMembers();
        var entry = (item.Method, item.Method.ComputedKey!, item.MethodBuilder);
        Assert.Throws<ArgumentNullException>(() => registry.DeclareMethods(item.Owner, null!));
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethods(item.Owner, [entry, entry]));
        Assert.False(registry.HasMethods(item.Owner));
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethods(item.Owner, [(item.Method, item.Method.ComputedKey!, other.MethodBuilder)]));
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethods(item.Owner, [(item.Method, new Expr.Literal("foreign"), item.MethodBuilder)]));
        Assert.Throws<InvalidOperationException>(() => registry.DeclareAccessor(item.Owner, item.Accessor, other.AccessorBuilder));
        Assert.Empty(registry.GetAccessors(item.Owner));
        registry.DeclareMethods(item.Owner, [entry]);
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethods(item.Owner, []));
        registry.DeclareAccessor(item.Owner, item.Accessor, item.AccessorBuilder);
        Assert.Throws<InvalidOperationException>(() => registry.DeclareAccessor(item.Owner, item.Accessor, item.AccessorBuilder));
        Assert.Single(registry.GetMethods(item.Owner));
        Assert.Single(registry.GetAccessors(item.Owner));
    }

    private static void EmitValue(MethodBuilder builder)
    {
        var il = builder.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ret);
    }

    private static (TypeBuilder Owner, Stmt.Function Method, Stmt.Accessor Accessor,
        MethodBuilder MethodBuilder, MethodBuilder AccessorBuilder) NewMembers()
    {
        var source = Assert.IsType<Stmt.Class>(Assert.Single(new Parser(new Lexer("class C { [methodKey]() { return 1; } get [accessorKey]() { return 2; } }").ScanTokens()).ParseOrThrow()));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var owner = assembly.DefineDynamicModule("module").DefineType("SameName", TypeAttributes.Public);
        var method = owner.DefineMethod("Method", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        var accessor = owner.DefineMethod("Accessor", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        return (owner, Assert.Single(source.Methods), Assert.Single(source.Accessors!), method, accessor);
    }
}
