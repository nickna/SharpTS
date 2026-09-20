using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // These helpers are declared within the certificate's generated type. No
    // builder or certificate metadata survives this construction operation.
    private MethodBuilder EmitX509NormalizeIssuerNameValue(TypeBuilder type)
    {
        var method = type.DefineMethod("NormalizeIssuerNameValue", MethodAttributes.Private | MethodAttributes.Static,
            typeof(string), [typeof(string)]);
        var il = method.GetILGenerator();
        var result = il.DeclareLocal(typeof(StringBuilder));
        var index = il.DeclareLocal(typeof(int));
        var character = il.DeclareLocal(typeof(char));
        var space = il.DeclareLocal(typeof(bool));
        var loop = il.DefineLabel();
        var next = il.DefineLabel();
        var whitespace = il.DefineLabel();
        var append = il.DefineLabel();
        var noSpace = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, result);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetMethod!);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Chars")!.GetMethod!);
        il.Emit(OpCodes.Stloc, character);
        foreach (var ch in new[] { ' ', '\t', '\r', '\n', '\v', '\f' })
        {
            il.Emit(OpCodes.Ldloc, character);
            il.Emit(OpCodes.Ldc_I4, (int)ch);
            il.Emit(OpCodes.Beq, whitespace);
        }
        il.Emit(OpCodes.Ldloc, space);
        il.Emit(OpCodes.Brfalse, noSpace);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noSpace);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, space);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Ldc_I4, (int)'A');
        il.Emit(OpCodes.Blt, append);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Ldc_I4, (int)'Z');
        il.Emit(OpCodes.Bgt, append);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Ldc_I4, 'a' - 'A');
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_U2);
        il.Emit(OpCodes.Stloc, character);
        il.MarkLabel(append);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(whitespace);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetProperty("Length")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Stloc, space);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509CanonicalIssuerName(TypeBuilder type, MethodBuilder normalize)
    {
        var readNameString = EmitX509ReadNameString(type);
        var method = type.DefineMethod("CanonicalIssuerName", MethodAttributes.Private | MethodAttributes.Static,
            typeof(string), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var sequence = il.DeclareLocal(typeof(AsnReader));
        var set = il.DeclareLocal(typeof(AsnReader));
        var attribute = il.DeclareLocal(typeof(AsnReader));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var writer = il.DeclareLocal(typeof(AsnWriter));
        var encoded = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var setLoop = il.DefineLabel();
        var attributeLoop = il.DefineLabel();
        var endSet = il.DefineLabel();
        var done = il.DefineLabel();
        var textValue = il.DefineLabel();
        var rawValue = il.DefineLabel();
        var endAttribute = il.DefineLabel();
        var readSequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        var empty = typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Ldloc, options);
        il.Emit(OpCodes.Newobj, typeof(AsnReader).GetConstructor([typeof(ReadOnlyMemory<byte>), typeof(AsnEncodingRules), typeof(AsnReaderOptions)])!);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, readSequence);
        il.Emit(OpCodes.Stloc, sequence);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Newobj, typeof(AsnWriter).GetConstructor([typeof(AsnEncodingRules)])!);
        il.Emit(OpCodes.Stloc, writer);
        EmitX509NameWriterScope(il, writer, noTag, "PushSequence");
        il.MarkLabel(setLoop);
        il.Emit(OpCodes.Ldloc, sequence);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, sequence);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSetOf", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, set);
        EmitX509NameWriterScope(il, writer, noTag, "PushSetOf");
        il.MarkLabel(attributeLoop);
        il.Emit(OpCodes.Ldloc, set);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, endSet);
        il.Emit(OpCodes.Ldloc, set);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, readSequence);
        il.Emit(OpCodes.Stloc, attribute);
        EmitX509NameWriterScope(il, writer, noTag, "PushSequence");
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Ldloc, attribute);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadObjectIdentifier", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("WriteObjectIdentifier", [typeof(string), typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ldloc, attribute);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloca, tag);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetProperty("TagClass")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4, (int)TagClass.Universal);
        il.Emit(OpCodes.Bne_Un, rawValue);
        foreach (int value in new[] { 12, 19, 20, 22, 26, 28, 30 })
        {
            il.Emit(OpCodes.Ldloca, tag);
            il.Emit(OpCodes.Call, typeof(Asn1Tag).GetProperty("TagValue")!.GetMethod!);
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Beq, textValue);
        }
        il.MarkLabel(rawValue);
        il.Emit(OpCodes.Ldloc, attribute);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Stloc, encoded);
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Ldloca, encoded);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetProperty("Span")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("WriteEncodedValue", [typeof(ReadOnlySpan<byte>)])!);
        il.Emit(OpCodes.Br, endAttribute);
        il.MarkLabel(textValue);
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Ldc_I4, (int)UniversalTagNumber.UTF8String);
        il.Emit(OpCodes.Ldloc, attribute);
        il.Emit(OpCodes.Ldloca, tag);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetProperty("TagValue")!.GetMethod!);
        il.Emit(OpCodes.Call, readNameString);
        il.Emit(OpCodes.Call, normalize);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("WriteCharacterString", [typeof(UniversalTagNumber), typeof(string), typeof(Asn1Tag?)])!);
        il.MarkLabel(endAttribute);
        il.Emit(OpCodes.Ldloc, attribute);
        il.Emit(OpCodes.Callvirt, empty);
        EmitX509NameWriterScope(il, writer, noTag, "PopSequence");
        il.Emit(OpCodes.Br, attributeLoop);
        il.MarkLabel(endSet);
        EmitX509NameWriterScope(il, writer, noTag, "PopSetOf");
        il.Emit(OpCodes.Br, setLoop);
        il.MarkLabel(done);
        EmitX509NameWriterScope(il, writer, noTag, "PopSequence");
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("Encode", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToHexString", [typeof(byte[])])!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitX509NameWriterScope(ILGenerator il, LocalBuilder writer, LocalBuilder tag, string name)
    {
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Ldloc, tag);
        var method = typeof(AsnWriter).GetMethod(name, [typeof(Asn1Tag?)])!;
        il.Emit(OpCodes.Callvirt, method);
        if (method.ReturnType != typeof(void)) il.Emit(OpCodes.Pop);
    }

    private MethodBuilder EmitX509SignatureKeyMatches(TypeBuilder type)
    {
        var method = type.DefineMethod("SignatureKeyMatches", MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool), [typeof(string), typeof(string)]);
        var il = method.GetILGenerator();
        var equal = typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!;
        var success = il.DefineLabel();
        // Keep algorithm families explicit: issuance checks compatibility, not the signature bytes.
        (string[] Signatures, string[] Keys)[] families =
        [
            ([.. new[] { 2, 3, 4, 5, 11, 12, 13, 14, 15, 16 }.Select(n => $"1.2.840.113549.1.1.{n}"),
              .. Enumerable.Range(13, 4).Select(n => $"2.16.840.1.101.3.4.3.{n}")], ["1.2.840.113549.1.1.1"]),
            (["1.2.840.113549.1.1.10"], ["1.2.840.113549.1.1.1", "1.2.840.113549.1.1.10"]),
            (["1.2.840.10045.4.1", .. Enumerable.Range(1, 4).Select(n => $"1.2.840.10045.4.3.{n}"),
              .. Enumerable.Range(9, 4).Select(n => $"2.16.840.1.101.3.4.3.{n}")], ["1.2.840.10045.2.1"]),
            (["1.2.840.10040.4.3", .. Enumerable.Range(1, 8).Select(n => $"2.16.840.1.101.3.4.3.{n}")], ["1.2.840.10040.4.1"]),
            (["1.3.101.112"], ["1.3.101.112"]),
            (["1.3.101.113"], ["1.3.101.113"])
        ];
        foreach (var (signatures, keys) in families)
        {
            var matched = il.DefineLabel();
            var next = il.DefineLabel();
            foreach (var signature in signatures)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, signature);
                il.Emit(OpCodes.Call, equal);
                il.Emit(OpCodes.Brtrue, matched);
            }
            il.Emit(OpCodes.Br, next);
            il.MarkLabel(matched);
            foreach (var key in keys)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, key);
                il.Emit(OpCodes.Call, equal);
                il.Emit(OpCodes.Brtrue, success);
            }
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(success);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509IssuerMetadata(TypeBuilder type, MethodBuilder canonicalName, MethodBuilder validateGeneralNames)
    {
        var keyMatches = EmitX509SignatureKeyMatches(type);
        var validateExtensions = EmitX509ValidateStandardExtensions(type, validateGeneralNames, canonicalName);
        var method = type.DefineMethod("MatchesIssuerMetadata", MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool), [typeof(X509Certificate2), typeof(X509Certificate2)]);
        var il = method.GetILGenerator();
        var result = il.DeclareLocal(typeof(bool));
        var extension = il.DeclareLocal(typeof(X509Extension));
        var usage = il.DeclareLocal(typeof(X509KeyUsageFlags));
        var required = il.DeclareLocal(typeof(X509KeyUsageFlags));
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var fields = il.DeclareLocal(typeof(AsnReader));
        var names = il.DeclareLocal(typeof(AsnReader));
        var directory = il.DeclareLocal(typeof(AsnReader));
        var encoded = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var key = il.DeclareLocal(typeof(string));
        var checkedDirectory = il.DeclareLocal(typeof(bool));
        var end = il.DefineLabel();
        var failure = il.DefineLabel();
        var success = il.DefineLabel();
        var equal = typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!;
        var rawData = typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!;
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        var empty = typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!;
        var readSequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        var hex = typeof(Convert).GetMethod("ToHexString", [typeof(byte[])])!;

        void Extension(int argument, string oid)
        {
            il.Emit(OpCodes.Ldarg, argument);
            il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetMethod!);
            il.Emit(OpCodes.Ldstr, oid);
            il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Item", [typeof(string)])!.GetMethod!);
        }
        void Name(int argument, string property)
        {
            il.Emit(OpCodes.Ldarg, argument);
            il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty(property)!.GetMethod!);
            il.Emit(OpCodes.Callvirt, rawData);
            il.Emit(OpCodes.Call, canonicalName);
        }
        void NewReader()
        {
            il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
            il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
            il.Emit(OpCodes.Ldloc, options);
            il.Emit(OpCodes.Newobj, typeof(AsnReader).GetConstructor([typeof(ReadOnlyMemory<byte>), typeof(AsnEncodingRules), typeof(AsnReaderOptions)])!);
        }
        void ContextTag(int value, bool constructed = false, bool nullable = true)
        {
            il.Emit(OpCodes.Ldc_I4, (int)TagClass.ContextSpecific);
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(constructed ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, typeof(Asn1Tag).GetConstructor([typeof(TagClass), typeof(int), typeof(bool)])!);
            if (nullable) il.Emit(OpCodes.Newobj, typeof(Asn1Tag?).GetConstructor([typeof(Asn1Tag)])!);
        }
        void UnlessTag(LocalBuilder source, int value, Label skip)
        {
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Callvirt, hasData);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
            il.Emit(OpCodes.Stloc, tag);
            il.Emit(OpCodes.Ldloca, tag);
            ContextTag(value, nullable: false);
            il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
            il.Emit(OpCodes.Brfalse, skip);
        }

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, validateExtensions);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, validateExtensions);
        il.Emit(OpCodes.Brfalse, failure);
        Name(0, "IssuerName");
        Name(1, "SubjectName");
        il.Emit(OpCodes.Call, equal);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("SignatureAlgorithm")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(Oid).GetProperty("Value")!.GetMethod!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("PublicKey")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(PublicKey).GetProperty("Oid")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(Oid).GetProperty("Value")!.GetMethod!);
        il.Emit(OpCodes.Call, keyMatches);
        il.Emit(OpCodes.Brfalse, failure);
        var afterUsage = il.DefineLabel();
        var checkUsage = il.DefineLabel();
        Extension(1, "2.5.29.15");
        il.Emit(OpCodes.Stloc, extension);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Brfalse, afterUsage);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(X509Extension).GetProperty("Critical")!.GetMethod!);
        il.Emit(OpCodes.Newobj, typeof(X509KeyUsageExtension).GetConstructor([typeof(AsnEncodedData), typeof(bool)])!);
        il.Emit(OpCodes.Callvirt, typeof(X509KeyUsageExtension).GetProperty("KeyUsages")!.GetMethod!);
        il.Emit(OpCodes.Stloc, usage);
        il.Emit(OpCodes.Ldc_I4, (int)X509KeyUsageFlags.KeyCertSign);
        il.Emit(OpCodes.Stloc, required);
        Extension(0, "1.3.6.1.5.5.7.1.14");
        il.Emit(OpCodes.Brfalse, checkUsage);
        il.Emit(OpCodes.Ldc_I4, (int)X509KeyUsageFlags.DigitalSignature);
        il.Emit(OpCodes.Stloc, required);
        il.MarkLabel(checkUsage);
        il.Emit(OpCodes.Ldloc, usage);
        il.Emit(OpCodes.Ldloc, required);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, failure);
        il.MarkLabel(afterUsage);
        Extension(0, "2.5.29.35");
        il.Emit(OpCodes.Stloc, extension);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Brfalse, success);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, rawData);
        NewReader();
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, readSequence);
        il.Emit(OpCodes.Stloc, fields);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);

        var afterKey = il.DefineLabel();
        UnlessTag(fields, 0, afterKey);
        il.Emit(OpCodes.Ldloc, fields);
        ContextTag(0);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadOctetString", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Call, hex);
        il.Emit(OpCodes.Stloc, key);
        Extension(1, "2.5.29.14");
        il.Emit(OpCodes.Stloc, extension);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Brfalse, afterKey);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, rawData);
        NewReader();
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadOctetString", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Call, hex);
        il.Emit(OpCodes.Call, equal);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);
        il.MarkLabel(afterKey);

        var afterNames = il.DefineLabel();
        var nameLoop = il.DefineLabel();
        var skipName = il.DefineLabel();
        UnlessTag(fields, 1, afterNames);
        il.Emit(OpCodes.Ldloc, fields);
        ContextTag(1, true);
        il.Emit(OpCodes.Callvirt, readSequence);
        il.Emit(OpCodes.Stloc, names);
        il.MarkLabel(nameLoop);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, afterNames);
        il.Emit(OpCodes.Ldloc, checkedDirectory);
        il.Emit(OpCodes.Brtrue, skipName);
        UnlessTag(names, 4, skipName);
        il.Emit(OpCodes.Ldloc, names);
        ContextTag(4, true);
        il.Emit(OpCodes.Callvirt, readSequence);
        il.Emit(OpCodes.Stloc, directory);
        il.Emit(OpCodes.Ldloc, directory);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Stloc, encoded);
        il.Emit(OpCodes.Ldloca, encoded);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, canonicalName);
        Name(1, "IssuerName");
        il.Emit(OpCodes.Call, equal);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, directory);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, checkedDirectory);
        il.Emit(OpCodes.Br, nameLoop);
        il.MarkLabel(skipName);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nameLoop);
        il.MarkLabel(afterNames);

        var afterSerial = il.DefineLabel();
        UnlessTag(fields, 2, afterSerial);
        il.Emit(OpCodes.Ldloc, fields);
        ContextTag(2);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadInteger", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate).GetMethod("GetSerialNumber", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(BigInteger).GetConstructor([typeof(ReadOnlySpan<byte>), typeof(bool), typeof(bool)])!);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Equality", [typeof(BigInteger), typeof(BigInteger)])!);
        il.Emit(OpCodes.Brfalse, failure);
        il.MarkLabel(afterSerial);
        il.Emit(OpCodes.Ldloc, fields);
        il.Emit(OpCodes.Callvirt, empty);
        il.MarkLabel(success);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, result);
        il.MarkLabel(failure);
        il.Emit(OpCodes.Leave, end);
        foreach (var exception in new[] { typeof(AsnContentException), typeof(CryptographicException) })
        {
            il.BeginCatchBlock(exception);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Leave, end);
        }
        il.EndExceptionBlock();
        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509ValidateStandardExtensions(TypeBuilder type, MethodBuilder validateGeneralNames, MethodBuilder canonicalName)
    {
        var validateProxy = EmitX509ValidateProxyExtension(type);
        var validateLegacyType = EmitX509ValidateLegacyCertificateType(type);
        var validateBasicConstraints = EmitX509ValidateBasicConstraints(type);
        var validateNameConstraints = EmitX509ValidateNameConstraints(type, validateGeneralNames);
        var validateDistributionPoints = EmitX509ValidateDistributionPoints(type, validateGeneralNames, canonicalName);
        var validateIpResources = EmitX509ValidateResources(type, true);
        var validateAsResources = EmitX509ValidateResources(type, false);
        var method = type.DefineMethod("ValidateStandardExtensions", MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool), [typeof(X509Certificate2)]);
        var il = method.GetILGenerator();
        var extensions = il.DeclareLocal(typeof(X509ExtensionCollection));
        var extension = il.DeclareLocal(typeof(X509Extension));
        var seen = il.DeclareLocal(typeof(HashSet<string>));
        var index = il.DeclareLocal(typeof(int));
        var oid = il.DeclareLocal(typeof(string));
        var loop = il.DefineLabel();
        var next = il.DefineLabel();
        var success = il.DefineLabel();
        var failure = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetMethod!);
        il.Emit(OpCodes.Stloc, extensions);
        il.Emit(OpCodes.Newobj, typeof(HashSet<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, seen);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, extensions);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Count")!.GetMethod!);
        il.Emit(OpCodes.Bge, success);
        il.Emit(OpCodes.Ldloc, extensions);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Item", [typeof(int)])!.GetMethod!);
        il.Emit(OpCodes.Stloc, extension);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("Oid")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(Oid).GetProperty("Value")!.GetMethod!);
        il.Emit(OpCodes.Stloc, oid);
        var notDistributionPoints = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Ldstr, "2.5.29.31");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notDistributionPoints);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, validateDistributionPoints);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(notDistributionPoints);
        foreach (var (resourceOid, validator) in new[]
        {
            ("1.3.6.1.5.5.7.1.7", validateIpResources), ("1.3.6.1.5.5.7.1.8", validateAsResources)
        })
        {
            var notResource = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, oid);
            il.Emit(OpCodes.Ldstr, resourceOid);
            il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
            il.Emit(OpCodes.Brfalse, notResource);
            il.Emit(OpCodes.Ldloc, seen);
            il.Emit(OpCodes.Ldloc, oid);
            il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
            il.Emit(OpCodes.Brfalse, failure);
            il.Emit(OpCodes.Ldloc, extension);
            il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
            il.Emit(OpCodes.Call, validator);
            il.Emit(OpCodes.Br, next);
            il.MarkLabel(notResource);
        }
        var notConstraints = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Ldstr, "2.5.29.30");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notConstraints);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, validateNameConstraints);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(notConstraints);
        var notLegacy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Ldstr, "2.16.840.1.113730.1.1");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notLegacy);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, validateLegacyType);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(notLegacy);
        var notSan = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Ldstr, "2.5.29.17");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notSan);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, validateGeneralNames);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(notSan);
        var notProxy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Ldstr, "1.3.6.1.5.5.7.1.14");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notProxy);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Call, validateProxy);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(notProxy);
        var notBasicConstraints = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Ldstr, "2.5.29.19");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brfalse, notBasicConstraints);
        il.Emit(OpCodes.Ldloc, seen);
        il.Emit(OpCodes.Ldloc, oid);
        il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Ldloc, extension);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, validateBasicConstraints);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Br, next);
        il.MarkLabel(notBasicConstraints);
        (string Oid, Type Type, string Property)[] decoders =
        [
            ("2.5.29.35", typeof(X509AuthorityKeyIdentifierExtension), "KeyIdentifier"),
            ("2.5.29.14", typeof(X509SubjectKeyIdentifierExtension), "SubjectKeyIdentifier"),
            ("2.5.29.15", typeof(X509KeyUsageExtension), "KeyUsages"),
            ("2.5.29.37", typeof(X509EnhancedKeyUsageExtension), "EnhancedKeyUsages")
        ];
        foreach (var decoder in decoders)
        {
            var other = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, oid);
            il.Emit(OpCodes.Ldstr, decoder.Oid);
            il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
            il.Emit(OpCodes.Brfalse, other);
            il.Emit(OpCodes.Ldloc, seen);
            il.Emit(OpCodes.Ldloc, oid);
            il.Emit(OpCodes.Callvirt, typeof(HashSet<string>).GetMethod("Add")!);
            il.Emit(OpCodes.Brfalse, failure);
            il.Emit(OpCodes.Ldloc, extension);
            bool rawDataConstructor = decoder.Type == typeof(X509AuthorityKeyIdentifierExtension);
            if (rawDataConstructor)
                il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
            il.Emit(OpCodes.Ldloc, extension);
            il.Emit(OpCodes.Callvirt, typeof(X509Extension).GetProperty("Critical")!.GetMethod!);
            il.Emit(OpCodes.Newobj, decoder.Type.GetConstructor([rawDataConstructor ? typeof(byte[]) : typeof(AsnEncodedData), typeof(bool)])!);
            if (rawDataConstructor)
            {
                var rawIssuer = il.DeclareLocal(typeof(ReadOnlyMemory<byte>?));
                var issuerMemory = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
                var issuerBytes = il.DeclareLocal(typeof(byte[]));
                var noIssuer = il.DefineLabel();
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Callvirt, typeof(X509AuthorityKeyIdentifierExtension).GetProperty("RawIssuer")!.GetMethod!);
                il.Emit(OpCodes.Stloc, rawIssuer);
                il.Emit(OpCodes.Ldloca, rawIssuer);
                il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>?).GetProperty("HasValue")!.GetMethod!);
                il.Emit(OpCodes.Brfalse, noIssuer);
                il.Emit(OpCodes.Ldloca, rawIssuer);
                il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>?).GetProperty("Value")!.GetMethod!);
                il.Emit(OpCodes.Stloc, issuerMemory);
                il.Emit(OpCodes.Ldloca, issuerMemory);
                il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("ToArray")!);
                il.Emit(OpCodes.Stloc, issuerBytes);
                il.Emit(OpCodes.Ldloc, issuerBytes);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldc_I4, 0x30);
                il.Emit(OpCodes.Stelem_I1);
                il.Emit(OpCodes.Ldloc, issuerBytes);
                il.Emit(OpCodes.Call, validateGeneralNames);
                il.MarkLabel(noIssuer);
            }
            il.Emit(OpCodes.Callvirt, decoder.Type.GetProperty(decoder.Property)!.GetMethod!);
            if (decoder.Type == typeof(X509KeyUsageExtension)) il.Emit(OpCodes.Brfalse, failure);
            else il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, next);
            il.MarkLabel(other);
        }
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(success);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(failure);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509ValidateProxyExtension(TypeBuilder type)
    {
        var hasAuthority = EmitX509ValidateBasicConstraints(type, requireAuthority: true);
        var method = type.DefineMethod("ValidateProxyExtension", MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool), [typeof(X509Certificate2), typeof(X509Extension)]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var proxy = il.DeclareLocal(typeof(AsnReader));
        var policy = il.DeclareLocal(typeof(AsnReader));
        var constraints = il.DeclareLocal(typeof(X509Extension));
        var readPolicy = il.DefineLabel();
        var afterPolicy = il.DefineLabel();
        var afterConstraints = il.DefineLabel();
        var failure = il.DefineLabel();
        var empty = typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!;
        var sequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Ldloc, options);
        il.Emit(OpCodes.Newobj, typeof(AsnReader).GetConstructor([typeof(ReadOnlyMemory<byte>), typeof(AsnEncodingRules), typeof(AsnReaderOptions)])!);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, proxy);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ldloc, proxy);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, readPolicy);
        il.Emit(OpCodes.Ldloc, proxy);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloca, tag);
        il.Emit(OpCodes.Ldsfld, typeof(Asn1Tag).GetField("Integer")!);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
        il.Emit(OpCodes.Brfalse, readPolicy);
        il.Emit(OpCodes.Ldloc, proxy);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadInteger", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(readPolicy);
        il.Emit(OpCodes.Ldloc, proxy);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, policy);
        il.Emit(OpCodes.Ldloc, proxy);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ldloc, policy);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadObjectIdentifier", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, policy);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, afterPolicy);
        il.Emit(OpCodes.Ldloc, policy);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadOctetString", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(afterPolicy);
        il.Emit(OpCodes.Ldloc, policy);
        il.Emit(OpCodes.Callvirt, empty);
        void Extension(string oid)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetMethod!);
            il.Emit(OpCodes.Ldstr, oid);
            il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Item", [typeof(string)])!.GetMethod!);
        }
        Extension("2.5.29.19");
        il.Emit(OpCodes.Stloc, constraints);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Brfalse, afterConstraints);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetMethod!);
        il.Emit(OpCodes.Call, hasAuthority);
        il.Emit(OpCodes.Brtrue, failure);
        il.MarkLabel(afterConstraints);
        Extension("2.5.29.17");
        il.Emit(OpCodes.Brtrue, failure);
        Extension("2.5.29.18");
        il.Emit(OpCodes.Brtrue, failure);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(failure);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509ValidateLegacyCertificateType(TypeBuilder type)
    {
        var method = type.DefineMethod("ValidateLegacyCertificateType", MethodAttributes.Private | MethodAttributes.Static,
            typeof(void), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var unusedBits = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldarg_0);
        EmitX509NewDerReader(il, options);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloca, unusedBits);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadBitString", [typeof(int).MakeByRefType(), typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
