using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ObjectShapeRegistryTests
{
    [Fact]
    public void SelectedKeysAreSnapshottedAndIncompleteCompletionIsRepairable()
    {
        var registry = new ObjectShapeRegistry();
        Assert.Throws<InvalidOperationException>(registry.CompleteDeclarations);
        Assert.Throws<InvalidOperationException>(() => registry.Declare("a", null!));
        Assert.Throws<ArgumentNullException>(() => registry.BeginDeclarations(null!));
        Assert.Throws<ArgumentNullException>(() => registry.BeginDeclarations(["a", null!]));
        var keys = new List<string> { "a", "a", "A" };
        registry.BeginDeclarations(keys);
        keys.Clear();
        Assert.Throws<InvalidOperationException>(() => registry.BeginDeclarations([]));
        var first = NewShape();
        var second = NewShape();
        Assert.Throws<ArgumentNullException>(() => registry.Declare(null!, first));
        Assert.Throws<ArgumentNullException>(() => registry.Declare("a", null!));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("missing", first));
        registry.Declare("a", first);
        Assert.Throws<InvalidOperationException>(() => registry.Declare("a", second));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("A", first));
        Assert.Single(registry.ByKey);
        Assert.Single(registry.ByClrType);
        Assert.Throws<InvalidOperationException>(registry.CompleteDeclarations);
        Assert.False(registry.IsComplete);
        registry.Declare("A", second);
        registry.CompleteDeclarations();
        Assert.True(registry.IsComplete);
        Assert.Same(first, registry.ByKey["a"]);
        Assert.Same(second, registry.ByClrType[second.ClrType]);
        Assert.Throws<InvalidOperationException>(() => registry.Declare("a", first));
        Assert.Throws<InvalidOperationException>(registry.CompleteDeclarations);
    }

    [Fact]
    public void EmptySelectionCompletesWithoutSharingState()
    {
        var first = new ObjectShapeRegistry();
        var second = new ObjectShapeRegistry();
        first.BeginDeclarations([]);
        first.CompleteDeclarations();
        Assert.True(first.IsComplete);
        Assert.False(second.IsComplete);
        Assert.Empty(first.ByKey);
        Assert.Empty(first.ByClrType);
        second.BeginDeclarations([""]);
        second.Declare("", NewShape());
        second.CompleteDeclarations();
        Assert.Empty(first.ByKey);
        Assert.Single(second.ByKey);
    }

    [Fact]
    public void ViewsRemainStableAndRejectMutationThroughCollectionInterfaces()
    {
        var registry = new ObjectShapeRegistry();
        var keys = registry.ByKey;
        var types = registry.ByClrType;
        registry.BeginDeclarations(["key"]);
        var shape = NewShape();
        registry.Declare("key", shape);
        Assert.Same(shape, keys["key"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary)keys).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<Type, ObjectShapeTypeInfo>)types).Clear());
        registry.CompleteDeclarations();
        Assert.Same(keys, registry.ByKey);
        Assert.Same(types, registry.ByClrType);
        Assert.Throws<NotSupportedException>(() => ((IDictionary)types).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ObjectShapeTypeInfo>)keys).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)shape.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary)shape.FieldBuilders).Clear());
    }

    [Fact]
    public void FieldInputsAreCopiedAndForwardHandlesRemainUsable()
    {
        var type = NewType();
        var field = type.DefineField("value", typeof(double), FieldAttributes.Public);
        var keyField = type.DefineField("$Keys", typeof(object[]), FieldAttributes.Static);
        var fields = new List<(string Name, TokenType Kind)> { ("value", TokenType.TYPE_NUMBER) };
        var builders = new Dictionary<string, FieldBuilder> { ["value"] = field };
        var info = new ObjectShapeTypeInfo(type, fields, builders, keyField);
        fields.Clear(); builders.Clear();
        Assert.Equal(("value", TokenType.TYPE_NUMBER), Assert.Single(info.Fields));
        Assert.Same(field, info.FieldBuilders["value"]);
        var registry = new ObjectShapeRegistry();
        registry.BeginDeclarations(["value"]);
        registry.Declare("value", info);
        var method = type.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static,
            typeof(double), [type.MakeByRefType()]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, registry.ByKey["value"].FieldBuilders["value"]);
        il.Emit(OpCodes.Ret);
        registry.CompleteDeclarations();
        var finalized = type.CreateType()!;
        var instance = Activator.CreateInstance(finalized)!;
        finalized.GetField("value")!.SetValue(instance, 42d);
        Assert.Equal(42d, finalized.GetMethod("Read")!.Invoke(null, [instance]));
        Assert.Same(type, registry.ByKey["value"].ClrType);
    }

    [Fact]
    public void InvalidFieldMapsCannotBecomePublishedShapeMetadata()
    {
        var type = NewType();
        var other = NewType();
        var field = type.DefineField("value", typeof(double), FieldAttributes.Public);
        var key = type.DefineField("$Keys", typeof(object[]), FieldAttributes.Static);
        var fields = new[] { ("value", TokenType.TYPE_NUMBER) };
        var map = new Dictionary<string, FieldBuilder> { ["value"] = field };
        Assert.Throws<ArgumentNullException>(() => new ObjectShapeTypeInfo(null!, fields, map, key));
        Assert.Throws<ArgumentNullException>(() => new ObjectShapeTypeInfo(type, null!, map, key));
        Assert.Throws<ArgumentNullException>(() => new ObjectShapeTypeInfo(type, fields, null!, key));
        Assert.Throws<ArgumentNullException>(() => new ObjectShapeTypeInfo(type, fields, map, null!));
        Assert.Throws<InvalidOperationException>(() => new ObjectShapeTypeInfo(type, [.. fields, .. fields], map, key));
        Assert.Throws<InvalidOperationException>(() => new ObjectShapeTypeInfo(type, fields, new Dictionary<string, FieldBuilder>(), key));
        Assert.Throws<InvalidOperationException>(() => new ObjectShapeTypeInfo(type, [], map, key));
        Assert.Throws<InvalidOperationException>(() => new ObjectShapeTypeInfo(other, fields, map, key));
        Assert.Throws<InvalidOperationException>(() => new ObjectShapeTypeInfo(type, [("value", TokenType.TYPE_NUMBER)],
            new Dictionary<string, FieldBuilder> { ["value"] = key }, key));
        Assert.Throws<InvalidOperationException>(() => new ObjectShapeTypeInfo(type, fields, map, field));
    }

    private static TypeBuilder NewType() => AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("ShapeRegistry_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("main").DefineType("Shape", TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(ValueType));

    private static ObjectShapeTypeInfo NewShape()
    {
        var type = NewType();
        var field = type.DefineField("value", typeof(double), FieldAttributes.Public);
        return new ObjectShapeTypeInfo(type, [("value", TokenType.TYPE_NUMBER)],
            new Dictionary<string, FieldBuilder> { ["value"] = field },
            type.DefineField("$Keys", typeof(object[]), FieldAttributes.Static));
    }
}
