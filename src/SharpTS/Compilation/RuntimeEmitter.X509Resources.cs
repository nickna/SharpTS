using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitX509ValidateResources(TypeBuilder type, bool ip)
    {
        var choice = EmitX509ResourceChoice(type, ip);
        var method = type.DefineMethod(ip ? "ValidateIpResources" : "ValidateAsResources",
            MethodAttributes.Private | MethodAttributes.Static, typeof(void), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var resources = il.DeclareLocal(typeof(AsnReader));
        var entry = il.DeclareLocal(typeof(AsnReader));
        var sequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        var empty = typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!;
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        il.Emit(OpCodes.Ldarg_0);
        EmitX509NewDerReader(il, options);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, resources);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);
        if (ip)
        {
            var loop = il.DefineLabel();
            var done = il.DefineLabel();
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, resources);
            il.Emit(OpCodes.Callvirt, hasData);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, resources);
            il.Emit(OpCodes.Ldloc, noTag);
            il.Emit(OpCodes.Callvirt, sequence);
            il.Emit(OpCodes.Stloc, entry);
            il.Emit(OpCodes.Ldloc, entry);
            il.Emit(OpCodes.Ldloc, noTag);
            il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadOctetString", [typeof(Asn1Tag?)])!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, entry);
            il.Emit(OpCodes.Call, choice);
            il.Emit(OpCodes.Ldloc, entry);
            il.Emit(OpCodes.Callvirt, empty);
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(done);
        }
        else
        {
            for (int kind = 0; kind <= 1; kind++)
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, resources);
                il.Emit(OpCodes.Callvirt, hasData);
                il.Emit(OpCodes.Brfalse, next);
                il.Emit(OpCodes.Ldloc, resources);
                il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
                il.Emit(OpCodes.Stloc, tag);
                il.Emit(OpCodes.Ldloca, tag);
                ContextTag();
                il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
                il.Emit(OpCodes.Brfalse, next);
                il.Emit(OpCodes.Ldloc, resources);
                ContextTag();
                il.Emit(OpCodes.Newobj, typeof(Asn1Tag?).GetConstructor([typeof(Asn1Tag)])!);
                il.Emit(OpCodes.Callvirt, sequence);
                il.Emit(OpCodes.Stloc, entry);
                il.Emit(OpCodes.Ldloc, entry);
                il.Emit(OpCodes.Call, choice);
                il.Emit(OpCodes.Ldloc, entry);
                il.Emit(OpCodes.Callvirt, empty);
                il.MarkLabel(next);
                void ContextTag()
                {
                    il.Emit(OpCodes.Ldc_I4, (int)TagClass.ContextSpecific);
                    il.Emit(OpCodes.Ldc_I4, kind);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Newobj, typeof(Asn1Tag).GetConstructor([typeof(TagClass), typeof(int), typeof(bool)])!);
                }
            }
        }
        il.Emit(OpCodes.Ldloc, resources);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitX509ResourceChoice(TypeBuilder type, bool ip)
    {
        var method = type.DefineMethod(ip ? "ValidateIpResourceChoice" : "ValidateAsResourceChoice",
            MethodAttributes.Private | MethodAttributes.Static, typeof(void), [typeof(AsnReader)]);
        var il = method.GetILGenerator();
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var entries = il.DeclareLocal(typeof(AsnReader));
        var range = il.DeclareLocal(typeof(AsnReader));
        var unusedBits = il.DeclareLocal(typeof(int));
        var notNull = il.DefineLabel();
        var loop = il.DefineLabel();
        var isRange = il.DefineLabel();
        var done = il.DefineLabel();
        var sequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloca, tag);
        il.Emit(OpCodes.Ldsfld, typeof(Asn1Tag).GetField("Null")!);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
        il.Emit(OpCodes.Brfalse, notNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadNull", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, entries);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, entries);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetProperty("HasData")!.GetMethod!);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, entries);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloca, tag);
        il.Emit(OpCodes.Ldsfld, typeof(Asn1Tag).GetField(ip ? "PrimitiveBitString" : "Integer")!);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
        il.Emit(OpCodes.Brfalse, isRange);
        Scalar(entries);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(isRange);
        il.Emit(OpCodes.Ldloc, entries);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, sequence);
        il.Emit(OpCodes.Stloc, range);
        Scalar(range);
        Scalar(range);
        il.Emit(OpCodes.Ldloc, range);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
        return method;

        void Scalar(LocalBuilder source)
        {
            il.Emit(OpCodes.Ldloc, source);
            if (ip) il.Emit(OpCodes.Ldloca, unusedBits);
            il.Emit(OpCodes.Ldloc, noTag);
            il.Emit(OpCodes.Callvirt, ip
                ? typeof(AsnReader).GetMethod("ReadBitString", [typeof(int).MakeByRefType(), typeof(Asn1Tag?)])!
                : typeof(AsnReader).GetMethod("ReadInteger", [typeof(Asn1Tag?)])!);
            il.Emit(OpCodes.Pop);
        }
    }
}
