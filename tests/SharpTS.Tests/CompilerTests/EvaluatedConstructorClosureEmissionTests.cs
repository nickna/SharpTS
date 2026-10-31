using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EvaluatedConstructorClosureEmissionTests
{
    [Fact]
    public void LegacyClosureKeepsActualIdentitySuppliedSelectionAndConstraintFallback()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("legacy_constructor_closure"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var marker = module.DefineType("FactoryMarker", TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
        var constructor = marker.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)!);
        il.Emit(OpCodes.Ret); marker.CreateType();
        foreach (string name in new[] { "First", "Second", "Bound" })
        {
            var owner = module.DefineType(name, TypeAttributes.Public);
            var parameter = owner.DefineGenericParameters("T")[0];
            if (name == "Bound") parameter.SetBaseTypeConstraint(typeof(Exception));
            owner.DefineDefaultConstructor(MethodAttributes.Public);
            owner.SetCustomAttribute(constructor, CustomAttributeEncoder.Encode(constructor, "factory"));
            owner.CreateType();
        }
        var helper = module.DefineType("Helper", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var close = helper.DefineMethod("Close", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Type), [typeof(Type), typeof(Type[]), typeof(Type)]);
        EvaluatedConstructorClosureEmission.EmitLegacy(close.GetILGenerator(), TypeProvider.Runtime, marker);
        helper.CreateType();
        using var image = new MemoryStream(); assembly.Save(image);
        using (var verify = new MemoryStream(image.ToArray()))
        using (var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]))
            Assert.Empty(verifier.Verify(verify));
        var saved = Assembly.Load(image.ToArray()); var method = saved.GetType("Helper")!.GetMethod("Close")!;
        var first = saved.GetTypes().Single(type => type.Name.StartsWith("First", StringComparison.Ordinal));
        var second = saved.GetTypes().Single(type => type.Name.StartsWith("Second", StringComparison.Ordinal));
        var bound = saved.GetTypes().Single(type => type.Name.StartsWith("Bound", StringComparison.Ordinal));
        Type Invoke(Type actual, Type[]? supplied, Type expected) => (Type)method.Invoke(null, [actual, supplied, expected])!;
        Assert.Equal(first.MakeGenericType(typeof(string)), Invoke(first, [typeof(string)], first));
        Assert.Equal(first.MakeGenericType(typeof(object)), Invoke(first, [typeof(string)], second));
        Assert.Equal(bound.MakeGenericType(typeof(ArgumentException)), Invoke(bound, [typeof(ArgumentException)], bound));
        Assert.Equal(bound.MakeGenericType(typeof(Exception)), Invoke(bound, [typeof(ArgumentException)], second));
        var closed = first.MakeGenericType(typeof(double));
        Assert.Same(closed, Invoke(closed, [typeof(string)], first));
        Assert.Same(typeof(Dictionary<string, object>), Invoke(typeof(Dictionary<string, object>), null, typeof(object)));
        Assert.Equal(typeof(List<string>), Invoke(typeof(List<>), [typeof(string)], typeof(object)));
        Assert.Same(typeof(List<>), Invoke(typeof(List<>), null, typeof(object)));
    }

    [Fact]
    public void DeclarationsRejectWrongModuleOrAbi()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("constructor_metadata_declarations"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var owner = module.DefineType("Queries");
        var close = owner.DefineMethod("Close", MethodAttributes.Static, typeof(Type), [typeof(Type), typeof(Type[]), typeof(Type)]);
        var first = owner.DefineMethod("First", MethodAttributes.Static, typeof(ConstructorInfo), [typeof(Type)]);
        _ = new EmittedNativeConstructorMetadataRuntime(module, close, first);
        var other = new PersistedAssemblyBuilder(new AssemblyName("other_constructor_metadata"), typeof(object).Assembly).DefineDynamicModule("other");
        Assert.Throws<InvalidOperationException>(() => new EmittedNativeConstructorMetadataRuntime(other, close, first));
        first.SetReturnType(typeof(object));
        Assert.Throws<InvalidOperationException>(() => new EmittedNativeConstructorMetadataRuntime(module, close, first));
    }
}