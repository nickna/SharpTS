using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Runtime.DotNet;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class MetadataParamArrayTests
{
    [Theory]
    [InlineData("Echo", false)]
    [InlineData("Strings", false)]
    [InlineData("Params", true)]
    public void MetadataAndRuntimeAttributesAgreeWithoutExecutingMetadata(string methodName, bool expected)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
        using var loader = new AssemblyReferenceLoader([path]);
        var metadata = loader.TryResolve("ExternalSignatureFixture.Api")!.GetMethod(methodName)!.GetParameters().Single();
        Assert.Equal(expected, DotNetMethodResolver.IsParamArray(metadata));
        var runtime = typeof(ExternalSignatureFixture.Api).GetMethod(methodName)!.GetParameters().Single();
        Assert.Equal(runtime.IsDefined(typeof(ParamArrayAttribute), false), DotNetMethodResolver.IsParamArray(runtime));
    }

    [Fact]
    public void SameFullNameFromAnotherTypeCannotClaimTheCoreParamArrayAttribute()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");
        using var loader = new AssemblyReferenceLoader([path]);
        var method = loader.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Strings")!;
        var fake = new FakeParameter(method.GetParameters().Single(), loader.TryResolve("ExternalSignatureFixture.Api")!);
        Assert.False(DotNetMethodResolver.IsParamArray(fake));
    }
    private sealed class FakeParameter(ParameterInfo inner, Type foreign) : ParameterInfo
    {
        public override MemberInfo Member => inner.Member;
        public override Type ParameterType => inner.ParameterType;
        public override IList<CustomAttributeData> GetCustomAttributesData() => [new FakeAttribute(foreign)];
        public override bool IsDefined(Type attributeType, bool inherit) => throw new InvalidOperationException("Metadata runtime attribute API must not be called.");
    }
    private sealed class FakeAttribute(Type foreign) : CustomAttributeData
    { public override Type AttributeType => new FakeName(foreign); }
    private sealed class FakeName(Type inner) : TypeDelegator(inner)
    { public override string FullName => "System.ParamArrayAttribute"; }
}
