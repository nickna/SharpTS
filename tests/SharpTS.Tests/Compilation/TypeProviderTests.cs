using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.Compilation;

public sealed class TypeProviderTests
{
    [Fact]
    public void MetadataCaches_DoNotRootCollectibleDynamicAssemblies()
    {
        var emittedType = ExerciseMetadataLookups();

        for (var attempt = 0; attempt < 10 && emittedType.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(emittedType.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseMetadataLookups()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"TypeProviderCollectible_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Main");
        var builder = module.DefineType("EmittedType", TypeAttributes.Public);
        builder.DefineDefaultConstructor(MethodAttributes.Public);

        var method = builder.DefineMethod(
            "Method",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);

        var getter = builder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            typeof(int),
            Type.EmptyTypes);
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldc_I4_0);
        getterIl.Emit(OpCodes.Ret);
        var property = builder.DefineProperty("Value", PropertyAttributes.None, typeof(int), null);
        property.SetGetMethod(getter);

        var emittedType = builder.CreateType()!;

        _ = TypeProvider.Runtime.GetMethod(emittedType, "Method", Type.EmptyTypes);
        _ = TypeProvider.Runtime.GetProperty(emittedType, "Value");
        _ = TypeProvider.Runtime.GetConstructor(emittedType, Type.EmptyTypes);

        return new WeakReference(emittedType);
    }
}
