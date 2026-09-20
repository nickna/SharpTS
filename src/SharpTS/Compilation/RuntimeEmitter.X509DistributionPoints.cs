using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitX509ValidateDistributionPoints(TypeBuilder type,
        MethodBuilder validateNames, MethodBuilder canonicalName)
    {
        var wrap = EmitX509WrapNameContents(type);
        var method = type.DefineMethod("ValidateDistributionPoints", MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var points = il.DeclareLocal(typeof(AsnReader));
        var point = il.DeclareLocal(typeof(AsnReader));
        var name = il.DeclareLocal(typeof(AsnReader));
        var names = il.DeclareLocal(typeof(AsnReader));
        var hasName = il.DeclareLocal(typeof(bool));
        var hasIssuer = il.DeclareLocal(typeof(bool));
        var unusedBits = il.DeclareLocal(typeof(int));
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        var failure = il.DefineLabel();
        var afterName = il.DefineLabel();
        var relative = il.DefineLabel();
        var endName = il.DefineLabel();
        var afterReasons = il.DefineLabel();
        var afterIssuer = il.DefineLabel();
        var sequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        var empty = typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!;
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        void ContextTag(int value, bool constructed, bool nullable = true)
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
            ContextTag(value, false, false);
            il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
            il.Emit(OpCodes.Brfalse, skip);
        }
        il.Emit(OpCodes.Ldarg_0);
        EmitX509NewDerReader(il, options);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, points);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, points);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, points);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, point);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasName);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasIssuer);
        UnlessTag(point, 0, afterName);
        il.Emit(OpCodes.Ldloc, point);
        ContextTag(0, true);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, name);
        UnlessTag(name, 0, relative);
        il.Emit(OpCodes.Ldloc, name);
        ContextTag(0, true);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, wrap);
        il.Emit(OpCodes.Call, validateNames);
        il.Emit(OpCodes.Br, endName);
        il.MarkLabel(relative);
        il.Emit(OpCodes.Ldloc, name);
        ContextTag(1, true);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSetOf", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, wrap);
        il.Emit(OpCodes.Call, canonicalName);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(endName);
        il.Emit(OpCodes.Ldloc, name);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasName);
        il.MarkLabel(afterName);
        UnlessTag(point, 1, afterReasons);
        il.Emit(OpCodes.Ldloc, point);
        il.Emit(OpCodes.Ldloca, unusedBits);
        ContextTag(1, false);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadBitString", [typeof(int).MakeByRefType(), typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(afterReasons);
        UnlessTag(point, 2, afterIssuer);
        il.Emit(OpCodes.Ldloc, point);
        ContextTag(2, true);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, names);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Stloc, hasIssuer);
        il.Emit(OpCodes.Ldloc, names);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, wrap);
        il.Emit(OpCodes.Call, validateNames);
        il.MarkLabel(afterIssuer);
        il.Emit(OpCodes.Ldloc, point);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ldloc, hasName);
        il.Emit(OpCodes.Ldloc, hasIssuer);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Brfalse, failure);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(failure);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509WrapNameContents(TypeBuilder type)
    {
        var method = type.DefineMethod("WrapNameContents", MethodAttributes.Private | MethodAttributes.Static,
            typeof(byte[]), [typeof(AsnReader), typeof(bool)]);
        var il = method.GetILGenerator();
        var writer = il.DeclareLocal(typeof(AsnWriter));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var encoded = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        var noSet = il.DefineLabel();
        var endSet = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
        il.Emit(OpCodes.Newobj, typeof(AsnWriter).GetConstructor([typeof(AsnEncodingRules)])!);
        il.Emit(OpCodes.Stloc, writer);
        EmitX509NameWriterScope(il, writer, noTag, "PushSequence");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noSet);
        EmitX509NameWriterScope(il, writer, noTag, "PushSetOf");
        il.MarkLabel(noSet);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetProperty("HasData")!.GetMethod!);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Stloc, encoded);
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Ldloca, encoded);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetProperty("Span")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("WriteEncodedValue", [typeof(ReadOnlySpan<byte>)])!);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, endSet);
        EmitX509NameWriterScope(il, writer, noTag, "PopSetOf");
        il.MarkLabel(endSet);
        EmitX509NameWriterScope(il, writer, noTag, "PopSequence");
        il.Emit(OpCodes.Ldloc, writer);
        il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("Encode", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
