using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ExternalOptionalDefaultTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");

    [Fact]
    public void OnlyActualSameContextParametersUseRawConstants()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        using var foreign = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        Assert.Equal("fallback", loader.DefaultForEmission(api.GetMethod("OptionalString")!.GetParameters()[0]));
        Assert.Equal(true, loader.DefaultForEmission(api.GetMethod("OptionalBoolean")!.GetParameters()[0]));
        Assert.Equal(7, loader.DefaultForEmission(api.GetMethod("OptionalInteger")!.GetParameters()[0]));
        Assert.Equal(3.5, loader.DefaultForEmission(api.GetMethod("OptionalNumber")!.GetParameters()[0]));
        Assert.Equal(9, Assert.IsType<int>(loader.DefaultForEmission(api.GetMethod("OptionalEnum")!.GetParameters()[0])));
        Assert.Null(loader.DefaultForEmission(api.GetMethod("OptionalNull")!.GetParameters()[0]));
        Assert.False(api.GetMethod("OptionalMissing")!.GetParameters()[0].HasDefaultValue);
        var foreignParameter = foreign.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("OptionalString")!.GetParameters()[0];
        var expected = Record.Exception(() => _ = foreignParameter.DefaultValue);
        var actual = Record.Exception(() => _ = loader.DefaultForEmission(foreignParameter));
        Assert.NotNull(expected); Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal("runtime-default", loader.DefaultForEmission(new RuntimeParameter(api.GetMethod("OptionalString")!)));
    }

    [Fact]
    public void RuntimeAndCompilerOwnedParametersKeepTheirExistingDefaultValuePath()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var runtime = typeof(ExternalOptionalDefaultTests).GetMethod(nameof(RuntimeDefault), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(RuntimeChoice.Second, Assert.IsType<RuntimeChoice>(loader.DefaultForEmission(runtime.GetParameters()[0])));
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("OwnedOptional"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("M");
        var owner = module.DefineType("Owner", TypeAttributes.Public);
        var method = owner.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        Assert.Equal("runtime-default", loader.DefaultForEmission(new RuntimeParameter(method)));
    }

    private enum RuntimeChoice { First = 4, Second = 9 }
    private static RuntimeChoice RuntimeDefault(RuntimeChoice value = RuntimeChoice.Second) => value;
    private sealed class RuntimeParameter(MemberInfo member) : ParameterInfo
    {
        public override MemberInfo Member => member;
        public override object DefaultValue => "runtime-default";
        public override object RawDefaultValue => throw new InvalidOperationException("An unproved parameter must not borrow a metadata owner to use this getter.");
    }
}
