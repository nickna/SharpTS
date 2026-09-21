using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class DeferredClassDefinitionRegistryTests
{
    [Fact]
    public void ForwardReferencesShareOneRecordAndCompletionRequiresBodies()
    {
        var registry = new DeferredClassDefinitionRegistry();
        var (source, owner, initializer, registrar, fields) = NewDeclaration();
        var keys = new List<Expr> { source.Fields[0].ComputedKey! };
        var definition = registry.Declare(source, owner, initializer, registrar, keys, fields);
        keys.Clear();
        fields.Clear();
        Assert.True(registry.TryGet(source, out var bySource));
        Assert.True(registry.TryGet(owner, out var byType));
        Assert.Same(definition, bySource);
        Assert.Same(definition, byType);
        Assert.Single(definition.Keys);
        Assert.Throws<NotSupportedException>(() => ((IList)definition.Keys).Clear());
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkInitializerEmitted(definition));
        initializer.GetILGenerator().Emit(OpCodes.Ret);
        var il = registrar.GetILGenerator();
        il.Emit(OpCodes.Call, initializer);
        il.Emit(OpCodes.Ret);
        registry.MarkInitializerEmitted(definition);
        Assert.Throws<InvalidOperationException>(() => registry.MarkInitializerEmitted(definition));
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkInitializerEmitted(definition));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(source, owner, initializer, registrar, [], fields));
        owner.CreateType()!.GetMethod("Register")!.Invoke(null, [Array.Empty<object>()]);
    }

    [Fact]
    public void EmptyRegistryAndSeparateCompilationsHaveExplicitAvailability()
    {
        var first = new DeferredClassDefinitionRegistry();
        var second = new DeferredClassDefinitionRegistry();
        var (source, owner, initializer, registrar, fields) = NewDeclaration();
        first.CompleteEmission();
        Assert.False(first.TryGet(source, out _));
        Assert.False(first.TryGet(owner, out _));
        Assert.Throws<InvalidOperationException>(() => first.RequireFieldKey(source.Fields[0]));
        var definition = second.Declare(source, owner, initializer, registrar, [source.Fields[0].ComputedKey!], fields);
        Assert.False(second.IsComplete);
        Assert.False(first.TryGet(source, out _));
        var third = new DeferredClassDefinitionRegistry();
        Assert.Throws<InvalidOperationException>(() => third.MarkInitializerEmitted(definition));
        third.CompleteEmission();
    }

    [Fact]
    public void InvalidDeclarationsAreAtomicAndDuplicatesCannotReplaceForwardReferences()
    {
        var registry = new DeferredClassDefinitionRegistry();
        var (source, owner, initializer, registrar, fields) = NewDeclaration();
        var other = NewDeclaration();
        var key = source.Fields[0].ComputedKey!;
        Assert.Throws<ArgumentNullException>(() => registry.Declare(source, owner, initializer, registrar, null!, fields));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(source, owner, other.Initializer, registrar, [key], fields));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(source, owner, initializer, registrar, [null!], fields));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(source, owner, initializer, registrar, [key], new Dictionary<Stmt.Field, FieldBuilder>()));
        var invalid = new Dictionary<Stmt.Field, FieldBuilder> { [source.Fields[0]] = other.Fields.Values.Single() };
        Assert.Throws<InvalidOperationException>(() => registry.Declare(source, owner, initializer, registrar, [key], invalid));
        Assert.False(registry.TryGet(source, out _));
        Assert.False(registry.TryGetFieldKey(source.Fields[0], out _));
        var definition = registry.Declare(source, owner, initializer, registrar, [key], fields);
        Assert.Throws<InvalidOperationException>(() => registry.Declare(source, owner, initializer, registrar, [key], fields));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(other.Source, owner, initializer, registrar, [other.Source.Fields[0].ComputedKey!], other.Fields));
        Assert.True(registry.TryGet(owner, out var retained));
        Assert.Same(definition, retained);
        Assert.Same(fields.Values.Single(), registry.RequireFieldKey(source.Fields[0]));
    }

    private static (Stmt.Class Source, TypeBuilder Owner, MethodBuilder Initializer, MethodBuilder Registrar,
        Dictionary<Stmt.Field, FieldBuilder> Fields) NewDeclaration()
    {
        var source = Assert.IsType<Stmt.Class>(Assert.Single(new Parser(new Lexer("class C { ['key'] = 1; }").ScanTokens()).ParseOrThrow()));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var owner = assembly.DefineDynamicModule("module").DefineType("C", TypeAttributes.Public);
        var initializer = owner.DefineMethod("Initialize", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var registrar = owner.DefineMethod("Register", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(object[])]);
        var field = owner.DefineField("Key", typeof(object), FieldAttributes.Static);
        return (source, owner, initializer, registrar, new Dictionary<Stmt.Field, FieldBuilder>(ReferenceEqualityComparer.Instance) { [source.Fields[0]] = field });
    }
}
