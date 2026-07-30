using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// A custom attribute encoded to its raw ECMA-335 §II.23.3 blob, applied via the
/// <c>SetCustomAttribute(ConstructorInfo, byte[])</c> overloads.
/// </summary>
public readonly record struct EncodedCustomAttribute(ConstructorInfo Ctor, byte[] Blob);

/// <summary>
/// Encodes custom-attribute blobs directly, replacing <see cref="CustomAttributeBuilder"/>
/// (#1324 gate): under Native AOT the legacy runtime reflection-emit surface —
/// CustomAttributeBuilder included — is a <see cref="PlatformNotSupportedException"/> thrower,
/// while <see cref="PersistedAssemblyBuilder"/>'s builders and their
/// <c>SetCustomAttribute(ConstructorInfo, byte[])</c> overloads are fully supported. The emitter
/// only ever uses positional arguments (no named fields/properties), so the encoding is the
/// §II.23.3 prolog, the fixed args in constructor-parameter order, and a zero named-arg count.
/// </summary>
internal static class CustomAttributeEncoder
{
    /// <summary>The blob for a parameterless attribute: prolog 0x0001 + 0 named args.</summary>
    internal static readonly byte[] EmptyBlob = [0x01, 0x00, 0x00, 0x00];

    internal static byte[] Encode(ConstructorInfo ctor, params object?[] args)
    {
        var parameters = ctor.GetParameters();
        if (parameters.Length != args.Length)
            throw new ArgumentException(
                $"Attribute constructor {ctor.DeclaringType?.Name} expects {parameters.Length} args, got {args.Length}.");
        if (args.Length == 0)
            return EmptyBlob;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0x0001); // prolog
        for (int i = 0; i < args.Length; i++)
            WriteFixedArg(w, parameters[i].ParameterType, args[i]);
        w.Write((ushort)0); // named-arg count
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteFixedArg(BinaryWriter w, Type parameterType, object? value)
    {
        if (parameterType.IsEnum)
        {
            // Enum fixed args serialize as their underlying integer type.
            WriteFixedArg(w, Enum.GetUnderlyingType(parameterType), Convert.ChangeType(value, Enum.GetUnderlyingType(parameterType)));
            return;
        }

        if (parameterType == typeof(string))
        {
            WriteSerString(w, (string?)value);
            return;
        }

        if (parameterType == typeof(Type))
        {
            WriteSerString(w, SerializedTypeName((Type)value!));
            return;
        }

        switch (Type.GetTypeCode(parameterType))
        {
            case TypeCode.Boolean: w.Write((byte)((bool)value! ? 1 : 0)); break;
            case TypeCode.Char: w.Write((ushort)(char)value!); break;
            case TypeCode.SByte: w.Write((sbyte)value!); break;
            case TypeCode.Byte: w.Write((byte)value!); break;
            case TypeCode.Int16: w.Write((short)value!); break;
            case TypeCode.UInt16: w.Write((ushort)value!); break;
            case TypeCode.Int32: w.Write((int)value!); break;
            case TypeCode.UInt32: w.Write((uint)value!); break;
            case TypeCode.Int64: w.Write((long)value!); break;
            case TypeCode.UInt64: w.Write((ulong)value!); break;
            case TypeCode.Single: w.Write((float)value!); break;
            case TypeCode.Double: w.Write((double)value!); break;
            default:
                throw new NotSupportedException(
                    $"CustomAttributeEncoder: unsupported fixed-arg type '{parameterType}'. " +
                    "Extend WriteFixedArg if the emitter starts using it.");
        }
    }

    /// <summary>
    /// The CA type-string form: full name for types in the assembly being emitted (builders),
    /// assembly-qualified otherwise — matching what compilers write.
    /// </summary>
    private static string SerializedTypeName(Type type) =>
        type is TypeBuilder || type.Module is ModuleBuilder
            ? type.FullName!
            : type.AssemblyQualifiedName ?? type.FullName!;

    private static void WriteSerString(BinaryWriter w, string? s)
    {
        if (s is null)
        {
            w.Write((byte)0xFF);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(s);
        WritePackedLength(w, bytes.Length);
        w.Write(bytes);
    }

    // ECMA-335 §II.23.2 compressed unsigned integer.
    private static void WritePackedLength(BinaryWriter w, int length)
    {
        if (length < 0x80)
        {
            w.Write((byte)length);
        }
        else if (length < 0x4000)
        {
            w.Write((byte)(0x80 | (length >> 8)));
            w.Write((byte)(length & 0xFF));
        }
        else
        {
            w.Write((byte)(0xC0 | (length >> 24)));
            w.Write((byte)((length >> 16) & 0xFF));
            w.Write((byte)((length >> 8) & 0xFF));
            w.Write((byte)(length & 0xFF));
        }
    }
}
