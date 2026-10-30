using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ExternalSignatureEmissionTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "ExternalSignatureFixture.dll");

    [Fact]
    public void ActualCoreAtomsNormalizeAllReturnViewsAndOtherContextsCannotBorrowThem()
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        using var other = new AssemblyReferenceLoader([Fixture]);
        var method = loader.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Echo")!;
        var view = loader.ForEmission(method);
        Assert.NotSame(method, view);
        Assert.Same(typeof(string), view.ReturnType);
        Assert.Same(typeof(string), view.ReturnParameter.ParameterType);
        Assert.Same(typeof(string), view.ReturnParameter.GetModifiedParameterType().UnderlyingSystemType);
        Assert.Same(view, loader.ForEmission(method));
        var foreign = other.TryResolve("ExternalSignatureFixture.Api")!.GetMethod("Echo")!;
        Assert.Same(foreign, loader.ForEmission(foreign));
        Assert.Same(foreign.ReturnType, loader.CoreAtomForEmission(foreign.ReturnType));
        var fake = new FakeCoreName(loader.TryResolve("ExternalSignatureFixture.Api")!);
        Assert.Same(fake, loader.CoreAtomForEmission(fake));
        Assert.NotSame(typeof(string), loader.ForEmission(fake).UnderlyingSystemType);
        Assert.Throws<NotSupportedException>(() => view.Invoke(null, ["never execute"]));
    }

    [Theory]
    [InlineData("Echo")]
    [InlineData("Boolean")]
    [InlineData("Integer")]
    [InlineData("Number")]
    [InlineData("ObjectValue")]
    [InlineData("ObjectEcho")]
    [InlineData("Nothing")]
    [InlineData("Strings")]
    [InlineData("Jagged")]
    [InlineData("Matrix")]
    [InlineData("OutString")]
    [InlineData("RefString")]
    [InlineData("ReadOnlyRef")]
    [InlineData("Identity")]
    [InlineData("List")]
    public void SavedMemberSignaturesMatchTheSelectedDefinitionIncludingModifiers(string name)
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var method = loader.TryResolve("ExternalSignatureFixture.Api")!.GetMethod(name)!;
        byte[] image = TokenImage(loader, method);
        Assert.Equal(DefinitionShape(method), ReferenceShape(image, method));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(image)));
    }

    [Theory]
    [InlineData("constructor")]
    [InlineData("field")]
    [InlineData("volatile")]
    [InlineData("getter")]
    [InlineData("setter")]
    [InlineData("indexer")]
    [InlineData("interface-in")]
    [InlineData("closed-constructor")]
    [InlineData("closed-field")]
    [InlineData("closed-method")]
    [InlineData("closed-property")]
    public void AccessorAndConstructedOwnerUnwrappingRetainTheEmissionView(string control)
    {
        using var loader = new AssemblyReferenceLoader([Fixture]);
        var api = loader.TryResolve("ExternalSignatureFixture.Api")!;
        MemberInfo member = control switch
        {
            "constructor" => api.GetConstructors().Single(),
            "field" => api.GetField("Value")!,
            "volatile" => api.GetField("Volatile")!,
            "getter" => api.GetProperty("Property")!.GetMethod!,
            "setter" => api.GetProperty("Property")!.SetMethod!,
            "indexer" => api.GetProperty("Item")!.GetMethod!,
            "interface-in" => loader.TryResolve("ExternalSignatureFixture.IReadOnlyInput")!.GetMethod("Read")!,
            _ => ConstructedMember()
        };
        byte[] image = TokenImage(loader, member);
        Assert.Equal(DefinitionShape(member), ReferenceShape(image, member));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(image)));
        if (control.StartsWith("closed-", StringComparison.Ordinal))
        {
            using var pe = new PEReader(new MemoryStream(image));
            var metadata = pe.GetMetadataReader();
            var reference = metadata.MemberReferences.Select(metadata.GetMemberReference).Single(value => metadata.GetString(value.Name) == member.Name && value.Parent.Kind == HandleKind.TypeSpecification);
            Assert.Equal(HandleKind.TypeSpecification, reference.Parent.Kind);
            var shape = metadata.GetTypeSpecification((TypeSpecificationHandle)reference.Parent).DecodeSignature(new ShapeProvider(), null);
            Assert.Contains("primitive:String", shape);
        }
        MemberInfo ConstructedMember()
        {
            var definition = loader.TryResolve("ExternalSignatureFixture.Box`1")!;
            var closed = definition.MakeGenericType(loader.TryResolve("System.String")!);
            return control switch
            {
                "closed-constructor" => closed.GetConstructors().Single(),
                "closed-field" => closed.GetField("Value")!,
                "closed-method" => closed.GetMethod("Echo")!,
                _ => closed.GetProperty("Property")!.GetMethod!
            };
        }
    }

    private static byte[] TokenImage(AssemblyReferenceLoader loader, MemberInfo member)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("signature_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("signature");
        var type = module.DefineType("Entry", TypeAttributes.Public);
        var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        switch (member)
        {
            case MethodInfo target: il.Emit(OpCodes.Ldtoken, loader.ForEmission(target)); break;
            case ConstructorInfo target:
                il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Newobj, loader.ForEmission(target)); break;
            case FieldInfo target: il.Emit(OpCodes.Ldtoken, loader.ForEmission(target)); break;
        }
        il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret); type.CreateType();
        using var output = new MemoryStream(); assembly.Save(output); return output.ToArray();
    }

    private static string DefinitionShape(MemberInfo member)
    {
        using var pe = new PEReader(File.OpenRead(Fixture));
        var metadata = pe.GetMetadataReader();
        EntityHandle handle = MetadataTokens.EntityHandle(member.MetadataToken);
        return handle.Kind == HandleKind.FieldDefinition
            ? metadata.GetFieldDefinition((FieldDefinitionHandle)handle).DecodeSignature(new ShapeProvider(), null)
            : Shape(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).DecodeSignature(new ShapeProvider(), null));
    }
    private static string ReferenceShape(byte[] image, MemberInfo selected)
    {
        using var pe = new PEReader(new MemoryStream(image)); var metadata = pe.GetMetadataReader();
        var member = metadata.MemberReferences.Select(metadata.GetMemberReference).Single(value => metadata.GetString(value.Name) == selected.Name &&
            (value.Parent.Kind == HandleKind.TypeSpecification || value.Parent.Kind == HandleKind.TypeReference &&
                metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)value.Parent).Name) == selected.DeclaringType!.Name));
        return member.GetKind() == MemberReferenceKind.Field ? member.DecodeFieldSignature(new ShapeProvider(), null)
            : Shape(member.DecodeMethodSignature(new ShapeProvider(), null));
    }
    private static string Shape(MethodSignature<string> value) => $"{value.Header.RawValue}:{value.GenericParameterCount}:{value.ReturnType}({string.Join(',', value.ParameterTypes)})";

    private sealed class FakeCoreName(Type inner) : TypeDelegator(inner)
    { public override string FullName => "System.String"; }

    private sealed class ShapeProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string element, ArrayShape shape) => $"array{shape.Rank}<{element}>";
        public string GetByReferenceType(string element) => "ref<" + element + ">";
        public string GetFunctionPointerType(MethodSignature<string> value) => "function<" + Shape(value) + ">";
        public string GetGenericInstantiation(string type, ImmutableArray<string> arguments) => type + "<" + string.Join(',', arguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string type, bool required) => (required ? "req(" : "opt(") + modifier + ")" + type;
        public string GetPinnedType(string type) => "pinned<" + type + ">";
        public string GetPointerType(string type) => "ptr<" + type + ">";
        public string GetPrimitiveType(PrimitiveTypeCode code) => "primitive:" + code;
        public string GetSZArrayType(string type) => "szarray<" + type + ">";
        public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte kind)
        { var type = metadata.GetTypeDefinition(handle); return $"{kind}:{metadata.GetString(metadata.GetAssemblyDefinition().Name)}:{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}"; }
        public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte kind)
        {
            var type = metadata.GetTypeReference(handle);
            string scope = type.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) : "nested";
            return $"{kind}:{scope}:{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
        }
        public string GetTypeFromSpecification(MetadataReader metadata, object? context, TypeSpecificationHandle handle, byte kind) =>
            metadata.GetTypeSpecification(handle).DecodeSignature(this, context);
    }
}
