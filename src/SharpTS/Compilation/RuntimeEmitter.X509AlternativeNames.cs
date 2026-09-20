using System.Formats.Asn1;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitX509ValidateGeneralNames(TypeBuilder type, MethodBuilder canonicalName)
    {
        var method = type.DefineMethod("ValidateGeneralNames", MethodAttributes.Private | MethodAttributes.Static,
            typeof(void), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var namesTag = il.DeclareLocal(typeof(Asn1Tag?));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var names = il.DeclareLocal(typeof(AsnReader));
        var writer = il.DeclareLocal(typeof(AsnWriter));
        var encoded = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var nameReader = il.DeclareLocal(typeof(AsnReader));
        var directory = il.DeclareLocal(typeof(AsnReader));
        var directoryTag = il.DeclareLocal(typeof(Asn1Tag));
        var currentTag = il.DeclareLocal(typeof(Asn1Tag));
        var notDirectory = il.DefineLabel();
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        EmitX509NewDerReader(il, options);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, names);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.Emit(OpCodes.Ldc_I4, (int)TagClass.ContextSpecific);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(Asn1Tag).GetConstructor([typeof(TagClass), typeof(int), typeof(bool)])!);
        il.Emit(OpCodes.Newobj, typeof(Asn1Tag?).GetConstructor([typeof(Asn1Tag)])!);
        il.Emit(OpCodes.Stloc, namesTag);
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Newobj, typeof(AsnWriter).GetConstructor([typeof(AsnEncodingRules)])!);
        il.Emit(OpCodes.Stloc, writer);
        // Validate GeneralNames through the AKI decoder, avoiding the SAN decoder's IP-length restriction.
        EmitX509NameWriterScope(il, writer, noTag, "PushSequence");
        EmitX509NameWriterScope(il, writer, namesTag, "PushSequence");
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetProperty("HasData")!.GetMethod!);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Stloc, encoded);
        il.Emit(OpCodes.Ldc_I4, (int)TagClass.ContextSpecific);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(Asn1Tag).GetConstructor([typeof(TagClass), typeof(int), typeof(bool)])!);
        il.Emit(OpCodes.Stloc, directoryTag);
        il.Emit(OpCodes.Ldloc, encoded);
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Ldloc, options);
        il.Emit(OpCodes.Newobj, typeof(AsnReader).GetConstructor([typeof(ReadOnlyMemory<byte>), typeof(AsnEncodingRules), typeof(AsnReaderOptions)])!);
        il.Emit(OpCodes.Stloc, nameReader);
        il.Emit(OpCodes.Ldloc, nameReader);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, currentTag);
        il.Emit(OpCodes.Ldloca, currentTag);
        il.Emit(OpCodes.Ldloc, directoryTag);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
        il.Emit(OpCodes.Brfalse, notDirectory);
        il.Emit(OpCodes.Ldloc, nameReader);
        il.Emit(OpCodes.Ldloc, directoryTag);
        il.Emit(OpCodes.Newobj, typeof(Asn1Tag?).GetConstructor([typeof(Asn1Tag)])!);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, directory);
        il.Emit(OpCodes.Ldloc, directory);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        var directoryBytes = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        il.Emit(OpCodes.Stloc, directoryBytes);
        il.Emit(OpCodes.Ldloca, directoryBytes);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, canonicalName);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, directory);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.Emit(OpCodes.Ldloc, nameReader);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.MarkLabel(notDirectory);
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Ldloca, encoded);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetProperty("Span")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("WriteEncodedValue", [typeof(ReadOnlySpan<byte>)])!);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        EmitX509NameWriterScope(il, writer, namesTag, "PopSequence");
        EmitX509NameWriterScope(il, writer, noTag, "PopSequence");
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("Encode", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(X509AuthorityKeyIdentifierExtension).GetConstructor([typeof(byte[]), typeof(bool)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitX509NewDerReader(ILGenerator il, LocalBuilder options)
    {
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Ldloc, options);
        il.Emit(OpCodes.Newobj, typeof(AsnReader).GetConstructor([typeof(ReadOnlyMemory<byte>), typeof(AsnEncodingRules), typeof(AsnReaderOptions)])!);
    }

    private MethodBuilder EmitX509RenderAlternativeNames(TypeBuilder type, MethodBuilder validate)
    {
        var method = type.DefineMethod("RenderAlternativeNames", MethodAttributes.Private | MethodAttributes.Static,
            typeof(void), [typeof(byte[]), typeof(List<string>), typeof(List<string>), typeof(List<string>)]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var names = il.DeclareLocal(typeof(AsnReader));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var value = il.DeclareLocal(typeof(string));
        var bytes = il.DeclareLocal(typeof(byte[]));
        var length = il.DeclareLocal(typeof(int));
        var loop = il.DefineLabel();
        var dns = il.DefineLabel();
        var uri = il.DefineLabel();
        var ip = il.DefineLabel();
        var done = il.DefineLabel();
        var end = il.DefineLabel();
        var add = typeof(List<string>).GetMethod("Add")!;
        var concat = typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!;
        void ReadText()
        {
            il.Emit(OpCodes.Ldloc, names);
            il.Emit(OpCodes.Ldc_I4, (int)UniversalTagNumber.IA5String);
            il.Emit(OpCodes.Ldloc, tag);
            il.Emit(OpCodes.Newobj, typeof(Asn1Tag?).GetConstructor([typeof(Asn1Tag)])!);
            il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadCharacterString", [typeof(UniversalTagNumber), typeof(Asn1Tag?)])!);
            il.Emit(OpCodes.Stloc, value);
        }
        void AddPart(string prefix)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, prefix);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Call, concat);
            il.Emit(OpCodes.Callvirt, add);
        }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, validate);
        il.Emit(OpCodes.Ldarg_0);
        EmitX509NewDerReader(il, options);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, names);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetProperty("HasData")!.GetMethod!);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, tag);
        foreach (var (number, label) in new[] { (2, dns), (6, uri), (7, ip) })
        {
            il.Emit(OpCodes.Ldloca, tag);
            il.Emit(OpCodes.Call, typeof(Asn1Tag).GetProperty("TagValue")!.GetMethod!);
            il.Emit(OpCodes.Ldc_I4, number);
            il.Emit(OpCodes.Beq, label);
        }
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(dns);
        ReadText();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, add);
        AddPart("DNS:");
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(uri);
        ReadText();
        AddPart("URI:");
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(ip);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Newobj, typeof(Asn1Tag?).GetConstructor([typeof(Asn1Tag)])!);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadOctetString", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, length);
        var validIp = il.DefineLabel();
        var addIp = il.DefineLabel();
        foreach (int size in new[] { 4, 16 })
        {
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldc_I4, size);
            il.Emit(OpCodes.Beq, validIp);
        }
        il.Emit(OpCodes.Ldstr, "<invalid length=");
        il.Emit(OpCodes.Ldloca, length);
        il.Emit(OpCodes.Call, typeof(System.Globalization.CultureInfo).GetProperty("InvariantCulture")!.GetMethod!);
        il.Emit(OpCodes.Call, typeof(int).GetMethod("ToString", [typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ldstr, ">");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, addIp);
        il.MarkLabel(validIp);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Newobj, typeof(IPAddress).GetConstructor([typeof(byte[])])!);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, add);
        il.MarkLabel(addIp);
        AddPart("IP Address:");
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Leave, end);
        foreach (var exception in new[] { typeof(AsnContentException), typeof(CryptographicException) })
        {
            il.BeginCatchBlock(exception);
            il.Emit(OpCodes.Pop);
            for (short argument = 1; argument <= 3; argument++)
            {
                il.Emit(OpCodes.Ldarg, argument);
                il.Emit(OpCodes.Callvirt, typeof(List<string>).GetMethod("Clear")!);
            }
            il.Emit(OpCodes.Leave, end);
        }
        il.EndExceptionBlock();
        il.MarkLabel(end);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
