using System.Reflection;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Byte-exact assertions for the ECMA-335 §II.23.3 blob writer. The encoder
/// replaced CustomAttributeBuilder for the Native AOT compiler (#1324 Phase 2);
/// a wrong byte here produces metadata other tools misread, so the expected
/// blobs are written out literally.
/// </summary>
public class CustomAttributeEncoderTests
{
    private static ConstructorInfo Ctor<TAttribute>(params Type[] parameters) =>
        typeof(TAttribute).GetConstructor(parameters)
            ?? throw new InvalidOperationException(
                $"{typeof(TAttribute).Name} has no ({string.Join(", ", parameters.Select(p => p.Name))}) constructor.");

    [Fact]
    public void Parameterless_attribute_is_prolog_plus_zero_named_args()
    {
        var blob = CustomAttributeEncoder.Encode(Ctor<SerializableAttribute>());

        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00 }, blob);
        Assert.Equal(CustomAttributeEncoder.EmptyBlob, blob);
    }

    [Fact]
    public void String_argument_is_packed_length_prefixed_utf8()
    {
        var blob = CustomAttributeEncoder.Encode(Ctor<ObsoleteAttribute>(typeof(string)), "old");

        Assert.Equal(
            new byte[]
            {
                0x01, 0x00,             // prolog
                0x03, (byte)'o', (byte)'l', (byte)'d',
                0x00, 0x00,             // named-arg count
            },
            blob);
    }

    [Fact]
    public void Null_string_argument_is_the_null_serstring_marker()
    {
        var blob = CustomAttributeEncoder.Encode(Ctor<ObsoleteAttribute>(typeof(string)), (string?)null);

        Assert.Equal(new byte[] { 0x01, 0x00, 0xFF, 0x00, 0x00 }, blob);
    }

    [Fact]
    public void String_over_0x7F_bytes_uses_the_two_byte_packed_length()
    {
        string value = new('x', 0x80);
        var blob = CustomAttributeEncoder.Encode(Ctor<ObsoleteAttribute>(typeof(string)), value);

        Assert.Equal(0x01, blob[0]);
        Assert.Equal(0x00, blob[1]);
        // §II.23.2: 0x80 encodes as 0x80 0x80 (high bit set + 14-bit big-endian value).
        Assert.Equal(0x80, blob[2]);
        Assert.Equal(0x80, blob[3]);
        Assert.Equal(2 + 2 + 0x80 + 2, blob.Length);
    }

    [Fact]
    public void Bool_and_string_arguments_encode_in_parameter_order()
    {
        var blob = CustomAttributeEncoder.Encode(
            Ctor<ObsoleteAttribute>(typeof(string), typeof(bool)), "m", true);

        Assert.Equal(
            new byte[]
            {
                0x01, 0x00,
                0x01, (byte)'m',
                0x01,                   // bool true
                0x00, 0x00,
            },
            blob);
    }

    [Fact]
    public void Enum_argument_serializes_as_its_underlying_integer()
    {
        var blob = CustomAttributeEncoder.Encode(
            Ctor<AttributeUsageAttribute>(typeof(AttributeTargets)), AttributeTargets.Class);

        Assert.Equal(
            new byte[]
            {
                0x01, 0x00,
                0x04, 0x00, 0x00, 0x00, // AttributeTargets.Class = 4, int32 little-endian
                0x00, 0x00,
            },
            blob);
    }

    [Fact]
    public void Type_argument_serializes_as_assembly_qualified_serstring()
    {
        var blob = CustomAttributeEncoder.Encode(
            Ctor<System.ComponentModel.TypeConverterAttribute>(typeof(Type)), typeof(int));

        // prolog + packed length + UTF-8 AQN + zero named args.
        string expectedName = typeof(int).AssemblyQualifiedName!;
        Assert.Equal(0x01, blob[0]);
        var text = System.Text.Encoding.UTF8.GetString(blob, 3, blob.Length - 5);
        Assert.Equal(expectedName, text);
    }

    [Fact]
    public void Null_type_argument_is_the_null_serstring_marker()
    {
        var blob = CustomAttributeEncoder.Encode(
            Ctor<System.ComponentModel.TypeConverterAttribute>(typeof(Type)), (Type?)null);

        Assert.Equal(new byte[] { 0x01, 0x00, 0xFF, 0x00, 0x00 }, blob);
    }

    [Fact]
    public void Null_value_type_argument_is_a_named_error_not_an_NRE()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CustomAttributeEncoder.Encode(
                Ctor<ObsoleteAttribute>(typeof(string), typeof(bool)), "m", null));

        Assert.Contains("null is not a valid fixed-arg value", ex.Message);
        Assert.Contains("Boolean", ex.Message);
    }

    [Fact]
    public void Argument_count_mismatch_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            CustomAttributeEncoder.Encode(Ctor<ObsoleteAttribute>(typeof(string))));
    }

    [Fact]
    public void Round_trips_through_a_persisted_assembly()
    {
        // End-to-end fidelity: the blob applied via SetCustomAttribute must read
        // back with identical constructor arguments.
        var ab = new System.Reflection.Emit.PersistedAssemblyBuilder(
            new AssemblyName("CaBlobRoundTrip"), typeof(object).Assembly);
        var type = ab.DefineDynamicModule("m").DefineType("T", TypeAttributes.Public);
        var ctor = Ctor<ObsoleteAttribute>(typeof(string), typeof(bool));
        type.SetCustomAttribute(ctor, CustomAttributeEncoder.Encode(ctor, "gone", true));
        type.CreateType();

        using var ms = new MemoryStream();
        ab.Save(ms);
        ms.Position = 0;

        using var pe = new System.Reflection.PortableExecutable.PEReader(ms);
        var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        var attribute = reader.CustomAttributes
            .Select(reader.GetCustomAttribute)
            .Single(a => a.Parent.Kind == System.Reflection.Metadata.HandleKind.TypeDefinition);
        var blob = reader.GetBlobBytes(attribute.Value);

        Assert.Equal(
            new byte[]
            {
                0x01, 0x00,
                0x04, (byte)'g', (byte)'o', (byte)'n', (byte)'e',
                0x01,
                0x00, 0x00,
            },
            blob);
    }
}
