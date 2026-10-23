using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>A source primitive data field's CLR ABI and boxed guest storage entries.</summary>
public sealed record PrimitiveGuestField(
    Stmt.Field Source, string SourceName, TypeBuilder DeclaringType, Type NativeType,
    FieldBuilder BackingField, MethodBuilder NativeGetter, MethodBuilder NativeSetter,
    MethodBuilder GuestGetter, MethodBuilder GuestSetter, MethodBuilder HasRawOverride);

internal sealed record GuestDataField(
    Stmt.Field Source, TypeBuilder DeclaringType, Type NativeType,
    FieldBuilder? BackingField, MethodBuilder? NativeGetter, MethodBuilder? NativeSetter,
    MethodBuilder GuestGetter, MethodBuilder GuestSetter, MethodBuilder HasRawOverride)
{
    internal string SourceName => Source.Name.Lexeme;
    internal bool IsPrimitive => BackingField != null &&
        (NativeType == typeof(double) || NativeType == typeof(bool) || NativeType == typeof(string));
    internal PrimitiveGuestField Primitive => new(Source, SourceName, DeclaringType, NativeType,
        BackingField!, NativeGetter!, NativeSetter!, GuestGetter, GuestSetter, HasRawOverride);
}
