using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ExternalGenericMetadataSelectionTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");

    [Fact]
    public void SelectionUsesExactCoreAndOwnedMetadataIdentitiesOnly()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        using var foreign = new AssemblyReferenceLoader([Fixture]);
        var actualString = loader.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Echo")!.ReturnType;
        Assert.True(loader.TryGenericSelectionType(typeof(string), out var selected));
        Assert.Same(actualString, selected);
        Assert.True(loader.TryGenericSelectionType(actualString, out selected));
        Assert.Same(actualString, selected);
        Assert.False(loader.TryGenericSelectionType(foreign.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Echo")!.ReturnType, out _));
        Assert.False(loader.TryGenericSelectionType(new FakeString(typeof(string)), out _));
        Assert.False(loader.TryGenericSelectionType(new FakeString(actualString), out _));
        Assert.False(loader.TryGenericSelectionType(typeof(ExternalGenericMetadataSelectionTests), out _));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UnrepresentableGenericOwner"), AssemblyBuilderAccess.Run);
        Assert.False(loader.TryGenericSelectionType(assembly.DefineDynamicModule("M").DefineType("Owner"), out _));
        Assert.False(loader.TryGenericSelectionType(typeof(List<string>), out _));
        foreach (var shape in new[] { typeof(string[]), typeof(string[,]), typeof(string[][]), typeof(string).MakeByRefType() })
        {
            Assert.True(loader.TryGenericSelectionType(shape, out selected));
            Assert.Same(actualString.Assembly, selected.Assembly);
            Assert.Equal(shape.IsArray, selected.IsArray);
            Assert.Equal(shape.IsByRef, selected.IsByRef);
        }
    }

    [Theory]
    [InlineData("Identity", "string", true)]
    [InlineData("Identity", "void", false)]
    [InlineData("Identity", "byref", false)]
    [InlineData("Identity", "open", false)]
    [InlineData("ClassOnly", "string", true)]
    [InlineData("ClassOnly", "double", false)]
    [InlineData("StructOnly", "double", true)]
    [InlineData("StructOnly", "string", false)]
    [InlineData("StructOnly", "nullable", false)]
    [InlineData("NewOnly", "derived", true)]
    [InlineData("NewOnly", "double", true)]
    [InlineData("NewOnly", "string", false)]
    [InlineData("NewOnly", "private", false)]
    [InlineData("NewOnly", "abstract", false)]
    [InlineData("Comparable", "string", true)]
    [InlineData("Comparable", "double", true)]
    [InlineData("Comparable", "object", false)]
    [InlineData("BaseOnly", "derived", true)]
    [InlineData("BaseOnly", "object", false)]
    public void OriginalMetadataMethodsCloseOnlyWhenConstraintsAreProved(string name, string argument, bool admitted)
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        var method = api.GetMethod(name)!;
        Type type = argument switch {
            "string" => typeof(string), "double" => typeof(double), "object" => typeof(object),
            "void" => typeof(void), "byref" => typeof(string).MakeByRefType(),
            "nullable" => api.GetMethod("NullableValue")!.ReturnType,
            "open" => loader.TryResolve("ExternalSignatureFixture.Box`1")!,
            "derived" => loader.TryResolve("ExternalSignatureFixture.ConstraintDerived")!,
            "private" => loader.TryResolve("ExternalSignatureFixture.PrivateConstructor")!,
            _ => loader.TryResolve("ExternalSignatureFixture.AbstractConstructor")!
        };
        var before = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name == "ExternalSignatureFixture").ToArray();
        var closed = loader.CloseGenericCandidates([method], [type], [type], null);
        Assert.Equal(admitted ? 1 : 0, closed.Length);
        if (admitted)
        {
            Assert.Same(method, closed[0].GetGenericMethodDefinition());
            Assert.Same(method.Module, closed[0].Module);
            Assert.Same(method.Module.Assembly, closed[0].DeclaringType!.Assembly);
        }
        Assert.Equal(before, AppDomain.CurrentDomain.GetAssemblies().Where(assembly => assembly.GetName().Name == "ExternalSignatureFixture").ToArray());
    }

    [Fact]
    public void InferenceRetainsArraysParamsNumericBridgesContextAndDependentConstraints()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        MethodInfo Close(string name, Type?[] arguments, Type[]? explicitTypes = null, Type? expected = null) =>
            Assert.Single(loader.CloseGenericCandidates([api.GetMethod(name)!], arguments, explicitTypes, expected));
        Assert.Equal("System.String", Close("Identity", [typeof(string)]).GetGenericArguments()[0].FullName);
        Assert.Equal("System.String", Close("First", [typeof(string[])]).GetGenericArguments()[0].FullName);
        Assert.Equal("System.String", Close("Many", [typeof(string), typeof(string)]).GetGenericArguments()[0].FullName);
        Assert.Equal("System.String", Close("WithNumber", [typeof(string), typeof(double)]).GetGenericArguments()[0].FullName);
        Assert.Equal("System.String", Close("Create", [], expected: typeof(string)).GetGenericArguments()[0].FullName);
        Close("Dependent", [typeof(string)], [typeof(string), typeof(string)]);
        Close("ArrayDependent", [typeof(string[][])], [typeof(string[][]), typeof(string)]);
        Assert.Empty(loader.CloseGenericCandidates([api.GetMethod("ArrayDependent")!], [typeof(double[][])], [typeof(double[][]), typeof(string)], null));
        Assert.Equal("System.String", Close("FromFactory", [api.GetMethod("StringFactory")!.ReturnType]).GetGenericArguments()[0].FullName);
        Assert.Empty(loader.CloseGenericCandidates([api.GetMethod("Dependent")!], [typeof(string)], [typeof(string), typeof(double)], null));
        Assert.Empty(loader.CloseGenericCandidates([api.GetMethod("Identity")!], [typeof(string)], [typeof(string), typeof(string)], null));
        var owner = loader.TryResolve("ExternalSignatureFixture.Box`1")!.MakeGenericType(api.GetMethod("Echo")!.ReturnType);
        Assert.Single(loader.CloseGenericCandidates([owner.GetMethod("Constrained")!], [typeof(string)], [typeof(string)], null));
        Assert.Empty(loader.CloseGenericCandidates([owner.GetMethod("Constrained")!], [typeof(double)], [typeof(double)], null));
    }

    [Fact]
    public void SavedClosedCallUsesTheOriginalLibraryDefinitionAndPrimitiveStringArgument()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var original = loader.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Identity")!;
        var selected = Assert.Single(loader.CloseGenericCandidates([original], [typeof(string)], [typeof(string)], null));
        Assert.Same(original, selected.GetGenericMethodDefinition());
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ClosedExternalSignature"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("M");
        var owner = module.DefineType("Entry", TypeAttributes.Public);
        var method = owner.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "selected");
        il.Emit(OpCodes.Call, loader.ForEmission(selected));
        il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret); owner.CreateType();
        using var output = new MemoryStream(); assembly.Save(output);
        byte[] image = output.ToArray();
        using var pe = new PEReader(new MemoryStream(image));
        var metadata = pe.GetMetadataReader();
        var specification = metadata.GetMethodSpecification(System.Reflection.Metadata.Ecma335.MetadataTokens.MethodSpecificationHandle(1));
        Assert.Equal(new byte[] { 0x0a, 0x01, 0x0e }, metadata.GetBlobBytes(specification.Signature));
        var reference = metadata.GetMemberReference((MemberReferenceHandle)specification.Method);
        Assert.Equal(original.Name, metadata.GetString(reference.Name));
        var type = metadata.GetTypeReference((TypeReferenceHandle)reference.Parent);
        Assert.Equal(original.DeclaringType!.Name, metadata.GetString(type.Name));
        Assert.Equal(original.DeclaringType.Namespace, metadata.GetString(type.Namespace));
        var library = metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
        Assert.Equal(original.Module.Assembly.GetName().Name, metadata.GetString(library.Name));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(image)));
    }

    [Fact]
    public void RuntimeMethodInferenceKeepsTheExistingConstraintAndIdentityBehavior()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var method = typeof(ExternalGenericMetadataSelectionTests).GetMethod(nameof(RuntimeIdentity), BindingFlags.NonPublic | BindingFlags.Static)!;
        var closed = Assert.Single(loader.CloseGenericCandidates([method], [typeof(string)], [typeof(string)], null));
        Assert.Same(typeof(string), closed.GetGenericArguments()[0]);
        Assert.Empty(loader.CloseGenericCandidates([method], [typeof(double)], [typeof(double)], null));
    }

    private static T RuntimeIdentity<T>(T value) where T : class => value;
    private sealed class FakeString(Type source) : TypeDelegator(source) { public override string? FullName => "System.String"; }
}
