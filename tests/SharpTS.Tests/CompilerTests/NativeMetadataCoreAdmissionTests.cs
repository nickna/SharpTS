using System.Reflection;
using SharpTS.Compilation;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeMetadataCoreAdmissionTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");

    [Fact]
    public void SnapshotUsesOnlyExactOwnedAtomsWithoutNamesEqualityOrUnderlyingUnwrap()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        using var other = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        var integer = api.GetMethod("Integer")!.ReturnType;
        var snapshot = loader.CaptureCoreAliases();
        Assert.Same(typeof(int), snapshot.Normalize(integer));
        var foreign = other.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Integer")!.ReturnType;
        Assert.Same(foreign, snapshot.Normalize(foreign));
        var fake = new HostileCoreName(integer);
        Assert.Same(fake, snapshot.Normalize(fake));
        Assert.Same(api, snapshot.Normalize(api));
        var array = api.GetMethod("Integers")!.ReturnType;
        Assert.Same(array, snapshot.Normalize(array));
        loader.Dispose();
        Assert.Same(typeof(int), snapshot.Normalize(integer));
    }

    [Fact]
    public void AdmissionKeepsNullSnapshotBehaviorUnsafeFlowsAndOriginalTypeValueDecisions()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        using var other = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        var integer = api.GetMethod("Integer")!.ReturnType;
        var snapshot = loader.CaptureCoreAliases();
        var original = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime);
        var admitted = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime, snapshot);
        Assert.False(Safe(original, integer, NativeClrAdmission.Marshalling.MethodResult));
        Assert.True(Safe(admitted, integer, NativeClrAdmission.Marshalling.MethodResult));
        Assert.True(Safe(admitted, api.GetMethod("Integers")!.ReturnType, NativeClrAdmission.Marshalling.MethodResult));
        Assert.True(Safe(admitted, api.GetMethod("Jagged")!.ReturnType, NativeClrAdmission.Marshalling.MethodResult));
        Assert.False(Safe(admitted, other.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Integer")!.ReturnType, NativeClrAdmission.Marshalling.MethodResult));
        Assert.False(Safe(admitted, api, NativeClrAdmission.Marshalling.MethodResult));
        Assert.False(Safe(admitted, api.GetMethod("ObjectValue")!.ReturnType, NativeClrAdmission.Marshalling.MethodResult));
        Assert.False(Safe(admitted, integer, NativeClrAdmission.Marshalling.TypeValue));
        Assert.False(Safe(admitted, integer, NativeClrAdmission.Marshalling.Constructed));
        Assert.True(Safe(admitted, api, NativeClrAdmission.Marshalling.OriginalAssignment));
    }

    [Fact]
    public void CopiedArrayConversionUsesOnlyOwnedScalarLeavesAndOriginalStructure()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        using var other = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        var admitted = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime, loader.CaptureCoreAliases());
        var original = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime);
        var integers = api.GetMethod("Integers")!.GetParameters()[0].ParameterType;
        var jagged = api.GetMethod("Jagged")!.GetParameters()[0].ParameterType;
        Assert.False(ArrayConversion(original, integers));
        Assert.True(ArrayConversion(admitted, integers));
        Assert.True(ArrayConversion(admitted, jagged));
        Assert.False(ArrayConversion(admitted, typeof(object[])));
        Assert.False(ArrayConversion(admitted, other.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Integers")!.ReturnType));
        Assert.False(ArrayConversion(admitted, api.GetMethod("Matrix")!.ReturnType));
        Assert.Same(integers, api.GetMethod("Integers")!.GetParameters()[0].ParameterType);
    }

    private static bool ArrayConversion(NativeClrAdmission admission, Type type) =>
        (bool)typeof(NativeClrAdmission).GetMethod("IsIndependentScalarArray", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(admission, [type])!;

    private static bool Safe(NativeClrAdmission admission, Type type, NativeClrAdmission.Marshalling mode) =>
        (bool)typeof(NativeClrAdmission).GetMethod("IsSafe", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(admission, [type, mode])!;

    private sealed class HostileCoreName(Type type) : TypeDelegator(type)
    {
        public override string? FullName => throw new InvalidOperationException("A name cannot authorize core identity.");
        public override Type UnderlyingSystemType => throw new InvalidOperationException("A wrapper cannot borrow core identity.");
        public override bool Equals(object? value) => throw new InvalidOperationException("Virtual equality cannot authorize identity.");
        public override bool Equals(Type? value) => throw new InvalidOperationException("Virtual equality cannot authorize identity.");
        public override int GetHashCode() => throw new InvalidOperationException("Virtual hashing cannot authorize identity.");
    }
}
