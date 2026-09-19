using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class FrameworkEmitMetadataTests
{
    [Theory]
    [InlineData("md5", typeof(MD5), 16, -1)]
    [InlineData("sha1", typeof(SHA1), 20, -1)]
    [InlineData("sha256", typeof(SHA256), 32, -1)]
    [InlineData("sha384", typeof(SHA384), 48, -1)]
    [InlineData("sha512", typeof(SHA512), 64, -1)]
    [InlineData("sha3-256", typeof(SHA3_256), 32, -1)]
    [InlineData("sha3-384", typeof(SHA3_384), 48, -1)]
    [InlineData("sha3-512", typeof(SHA3_512), 64, -1)]
    [InlineData("shake128", typeof(Shake128), 16, 16)]
    [InlineData("shake256", typeof(Shake256), 32, 32)]
    public void DigestMetadataUsesTheExpectedFrameworkOverloadAndPlatformGuard(
        string name, Type declaringType, int outputLength, int xofDefault)
    {
        var row = Assert.Single(FrameworkEmitMetadata.CryptoHashes, row => row.Name == name);
        Assert.Equal(declaringType, row.HashData.DeclaringType);
        Assert.Equal("HashData", row.HashData.Name);
        Assert.Equal(typeof(byte[]), row.HashData.ReturnType);
        Assert.True(row.HashData.IsStatic);
        Assert.False(row.HashData.Module.Assembly.IsDynamic);
        Assert.Equal(xofDefault, row.XofDefault);
        Assert.Equal(xofDefault > 0 ? [typeof(byte[]), typeof(int)] : new[] { typeof(byte[]) },
            row.HashData.GetParameters().Select(parameter => parameter.ParameterType));
        var expectedSupport = declaringType.GetProperty("IsSupported", BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(expectedSupport?.GetMethod, row.IsSupported);
        if (row.IsSupported is not null && !(bool)row.IsSupported.Invoke(null, null)!)
            return;
        byte[] input = [97, 98, 99];
        object[] args = xofDefault > 0 ? [input, xofDefault] : [input];
        Assert.Equal(outputLength, Assert.IsType<byte[]>(row.HashData.Invoke(null, args)).Length);
    }

    [Fact]
    public void DigestCatalogCannotBeMutatedThroughACollectionView()
    {
        var catalog = FrameworkEmitMetadata.CryptoHashes;
        Assert.Equal(new[] { "md5", "sha1", "sha256", "sha384", "sha512", "sha3-256", "sha3-384", "sha3-512", "shake128", "shake256" },
            catalog.Select(row => row.Name));
        var list = Assert.IsAssignableFrom<IList>(catalog);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = catalog[1]);
        Assert.Throws<NotSupportedException>(list.Clear);
        Assert.Equal(catalog, FrameworkEmitMetadata.CryptoHashes);
        var changedCopy = catalog.SetItem(0, catalog[1]);
        Assert.Equal("sha1", changedCopy[0].Name);
        Assert.Equal("md5", FrameworkEmitMetadata.CryptoHashes[0].Name);
    }

    [Fact]
    public void SpanHandlesComposeIntoVerifiedStandaloneCode()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("framework_span_metadata"), typeof(object).Assembly);
        var type = builder.DefineDynamicModule("main").DefineType("SpanSearch", TypeAttributes.Public);
        var method = type.DefineMethod("Find", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(List<double>), typeof(int), typeof(double)]);
        var il = method.GetILGenerator();
        var span = il.DeclareLocal(typeof(Span<double>));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, FrameworkEmitMetadata.DoubleListAsSpan);
        il.Emit(OpCodes.Stloc, span);
        il.Emit(OpCodes.Ldloca, span);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, FrameworkEmitMetadata.DoubleSpanSlice);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, FrameworkEmitMetadata.DoubleSpanIndexOf);
        il.Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var saved = Assembly.Load(bytes.ToArray());
        Assert.DoesNotContain(saved.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
        var find = saved.GetType("SpanSearch")!.GetMethod("Find")!.CreateDelegate<Func<List<double>, int, double, int>>();
        List<double> values = [1, double.NaN, -0.0, 1];
        Assert.Equal(1, find(values, 0, double.NaN));
        Assert.Equal(0, find(values, 2, +0.0));
        Assert.Equal(2, find(values, 1, 1));
        Assert.Equal(-1, find(values, values.Count, 1));
        values[1] = 42;
        Assert.Equal(1, find(values, 0, 42));
    }
}
