using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Tests.CompilerTests;

public sealed class UnionTypeGeneratorLifecycleTests
{
    public interface CustomUnion { object Value { get; } }
    public interface InvalidUnion { string Value { get; } }

    [Fact]
    public void SuppliedInterfaceOwnsTheGeneratedValueContract()
    {
        var module = NewModule();
        var mapper = new TypeMapper(module);
        Assert.Throws<ArgumentException>(() => new UnionTypeGenerator(mapper, typeof(IDisposable)));
        Assert.Throws<ArgumentException>(() => new UnionTypeGenerator(mapper, typeof(InvalidUnion)));
        Assert.Empty(module.GetTypes());
        var owner = new UnionTypeGenerator(mapper, typeof(CustomUnion));
        owner.GetOrCreateUnionType(Union(), module);
        owner.FinalizeAllUnionTypes();
        var type = owner.GetOrCreateUnionType(Union(), module);
        Assert.True(typeof(CustomUnion).IsAssignableFrom(type));
        Assert.False(typeof(IUnionType).IsAssignableFrom(type));
        var conversion = type.GetMethods().Single(m => m.Name == "op_Implicit"
            && m.GetParameters()[0].ParameterType == typeof(string));
        var value = Assert.IsAssignableFrom<CustomUnion>(conversion.Invoke(null, ["hello"]));
        Assert.Equal("hello", value.Value);
    }

    [Fact]
    public void DependenciesAreFixedAndDeclarationsCannotCrossModules()
    {
        var module = NewModule();
        var mapper = new TypeMapper(module);
        Assert.Throws<ArgumentNullException>(() => new UnionTypeGenerator(null!));
        Assert.Throws<ArgumentException>(() => new UnionTypeGenerator(mapper, typeof(object)));
        var owner = new UnionTypeGenerator(mapper);
        Assert.Equal(typeof(IUnionType), owner.UnionTypeInterface);
        Assert.Null(typeof(UnionTypeGenerator).GetProperty(nameof(owner.UnionTypeInterface))!.SetMethod);
        Assert.Throws<ArgumentNullException>(() => owner.GetOrCreateUnionType(null!, module));
        Assert.Throws<ArgumentNullException>(() => owner.GetOrCreateUnionType(Union(), null!));
        var foreign = NewModule();
        Assert.Throws<InvalidOperationException>(() => owner.GetOrCreateUnionType(Union(), foreign));
        var declared = owner.GetOrCreateUnionType(Union(), module);
        Assert.Same(module, declared.Module);
        Assert.Throws<InvalidOperationException>(() => owner.GetOrCreateUnionType(Union(), foreign));
        Assert.Empty(foreign.GetTypes());
    }

    [Fact]
    public void ForwardConversionsRemainValidAfterFinalization()
    {
        var module = NewModule();
        var owner = new UnionTypeGenerator(new TypeMapper(module));
        var declaration = Assert.IsAssignableFrom<TypeBuilder>(owner.GetOrCreateUnionType(Union(), module));
        Assert.Same(declaration, owner.GetOrCreateUnionType(Union(), module));
        var conversion = Assert.IsAssignableFrom<MethodBuilder>(owner.GetImplicitConversion(declaration, typeof(double)));
        var consumer = module.DefineType("Consumer", TypeAttributes.Public);
        var method = consumer.DefineMethod("Box", MethodAttributes.Public | MethodAttributes.Static,
            typeof(IUnionType), [typeof(double)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, conversion);
        il.Emit(OpCodes.Box, declaration);
        il.Emit(OpCodes.Ret);
        owner.FinalizeAllUnionTypes();
        Assert.True(owner.IsComplete);
        var finalized = owner.GetOrCreateUnionType(Union(), module);
        Assert.Same(finalized, owner.GetOrCreateUnionType(Union(), module));
        Assert.Same(conversion, owner.GetImplicitConversion(finalized, typeof(double)));
        Assert.Null(owner.GetImplicitConversion(finalized, typeof(DateTime)));
        var result = Assert.IsAssignableFrom<IUnionType>(consumer.CreateType()!.GetMethod("Box")!.Invoke(null, [42d]));
        Assert.Equal(42d, result.Value);
        Assert.Throws<InvalidOperationException>(() => owner.GetOrCreateUnionType(new TI.Union([TI.Primitive.Boolean, TI.String.Shared]), module));
        Assert.Throws<InvalidOperationException>(owner.FinalizeAllUnionTypes);
    }

    [Fact]
    public void EmptyCompletionIsExplicitAndSeparateGeneratorsDoNotShareTypes()
    {
        var emptyModule = NewModule();
        var empty = new UnionTypeGenerator(new TypeMapper(emptyModule));
        empty.FinalizeAllUnionTypes();
        Assert.True(empty.IsComplete);
        Assert.Empty(emptyModule.GetTypes());
        Assert.Throws<InvalidOperationException>(() => empty.GetOrCreateUnionType(Union(), emptyModule));
        var firstModule = NewModule(); var secondModule = NewModule();
        var first = new UnionTypeGenerator(new TypeMapper(firstModule));
        var second = new UnionTypeGenerator(new TypeMapper(secondModule));
        var a = first.GetOrCreateUnionType(Union(), firstModule);
        var b = second.GetOrCreateUnionType(Union(), secondModule);
        Assert.NotSame(a, b);
        first.FinalizeAllUnionTypes();
        Assert.False(second.IsComplete);
        second.FinalizeAllUnionTypes();
        Assert.NotSame(first.GetOrCreateUnionType(Union(), firstModule), second.GetOrCreateUnionType(Union(), secondModule));
    }

    [Fact]
    public void FailedFinalizationCannotPublishNullMetadataOnRetry()
    {
        var module = NewModule();
        var owner = new UnionTypeGenerator(new TypeMapper(module));
        owner.GetOrCreateUnionType(Union(), module);
        var invalidUnion = new TI.Union([TI.Primitive.Boolean, TI.String.Shared]);
        var invalid = Assert.IsAssignableFrom<TypeBuilder>(owner.GetOrCreateUnionType(invalidUnion, module));
        invalid.AddInterfaceImplementation(typeof(IMissingUnionMember));

        Assert.Throws<TypeLoadException>(owner.FinalizeAllUnionTypes);
        Assert.False(owner.IsComplete);
        var completed = owner.GetOrCreateUnionType(Union(), module);
        Assert.False(completed is TypeBuilder);
        Assert.Throws<InvalidOperationException>(owner.FinalizeAllUnionTypes);
        Assert.False(owner.IsComplete);
        Assert.Same(completed, owner.GetOrCreateUnionType(Union(), module));
        Assert.Same(invalid, owner.GetOrCreateUnionType(invalidUnion, module));
    }

    public interface IMissingUnionMember { void Run(); }

    private static TI.Union Union() => new([TI.Primitive.Number, TI.String.Shared]);

    private static ModuleBuilder NewModule() => AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("UnionLifecycle_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("main");
}
