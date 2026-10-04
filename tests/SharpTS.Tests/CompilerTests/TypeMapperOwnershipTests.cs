using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;
using TI = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Tests.CompilerTests;

public sealed class TypeMapperOwnershipTests
{
    private static ModuleBuilder NewModule() => AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("mapper_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("main");

    [Fact]
    public void ClassConfigurationCannotBeReplacedOrCrossModules()
    {
        var module = NewModule();
        var mapper = new TypeMapper(module);
        var foreign = NewModule().DefineType("Foreign");
        Assert.Throws<InvalidOperationException>(() => mapper.SetClassBuilders(new() { ["Foreign"] = foreign }));
        Assert.Throws<ArgumentNullException>(() => mapper.SetClassBuilders(null!));
        var classes = new Dictionary<string, TypeBuilder>();
        mapper.SetClassBuilders(classes);
        mapper.SetClassBuilders(classes);
        Assert.Throws<InvalidOperationException>(() => mapper.SetClassBuilders(new()));
        var local = module.DefineType("Local");
        classes.Add("Local", local);
        Assert.Same(local, mapper.GetClassType("Local"));
        classes.Add("Foreign", foreign);
        Assert.Throws<InvalidOperationException>(() => mapper.GetClassType("Foreign"));
        Assert.Same(local, mapper.GetClassType("Local"));
        Assert.Same(typeof(object), new TypeMapper(NewModule()).GetClassType("Local"));
    }

    [Fact]
    public void UnionConfigurationPreservesItsMapperAndCompletedOwner()
    {
        var module = NewModule();
        var mapper = new TypeMapper(module);
        Assert.Throws<InvalidOperationException>(() => mapper.SetUnionGenerator(new(new TypeMapper(module))));
        Assert.Throws<ArgumentNullException>(() => mapper.SetUnionGenerator(null!));
        var unions = new UnionTypeGenerator(mapper);
        mapper.SetUnionGenerator(unions);
        mapper.SetUnionGenerator(unions);
        Assert.Throws<InvalidOperationException>(() => mapper.SetUnionGenerator(new(mapper)));
        var union = new TI.Union([TI.Primitive.Number, TI.String.Shared]);
        var declared = mapper.MapTypeInfoStrict(union);
        unions.FinalizeAllUnionTypes();
        Assert.Equal(declared.FullName, mapper.MapTypeInfoStrict(union).FullName);
    }

    [Fact]
    public void RuntimeConfigurationCannotRetargetCachedDelegateAdapters()
    {
        var module = NewModule();
        var mapper = new TypeMapper(module);
        Assert.Throws<InvalidOperationException>(() => mapper.DelegateAdapters);
        var foreign = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(NewModule(), new RuntimeFeatureDetector().Detect([]));
        Assert.Throws<InvalidOperationException>(() => mapper.SetRuntime(foreign));
        Assert.Throws<ArgumentNullException>(() => mapper.SetRuntime(null!));
        Assert.Throws<InvalidOperationException>(() => mapper.DelegateAdapters);
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, new RuntimeFeatureDetector().Detect([]));
        mapper.SetRuntime(runtime);
        var adapters = mapper.DelegateAdapters;
        var handle = adapters.GetOrEmit(typeof(Func<object, object>));
        mapper.SetRuntime(runtime);
        Assert.Same(adapters, mapper.DelegateAdapters);
        Assert.Throws<InvalidOperationException>(() => mapper.SetRuntime(foreign));
        Assert.Equal(handle, mapper.DelegateAdapters.GetOrEmit(typeof(Func<object, object>)));
        Assert.Same(module, handle.Ctor.DeclaringType!.Module);
    }
}
