using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitX509ValidateNameConstraints(TypeBuilder type, MethodBuilder validateGeneralNames)
    {
        var method = type.DefineMethod("ValidateNameConstraints", MethodAttributes.Private | MethodAttributes.Static,
            typeof(void), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var constraints = il.DeclareLocal(typeof(AsnReader));
        var subtrees = il.DeclareLocal(typeof(AsnReader));
        var subtree = il.DeclareLocal(typeof(AsnReader));
        var writer = il.DeclareLocal(typeof(AsnWriter));
        var encoded = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var sequence = typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!;
        var empty = typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!;
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        void ContextTag(int value, bool constructed, bool nullable)
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
        il.Emit(OpCodes.Stloc, constraints);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, empty);
        // Only validate the cached extension structure; do not apply chain name policy.
        for (int kind = 0; kind <= 1; kind++)
        {
            var nextKind = il.DefineLabel();
            var loop = il.DefineLabel();
            UnlessTag(constraints, kind, nextKind);
            il.Emit(OpCodes.Ldloc, constraints);
            ContextTag(kind, true, true);
            il.Emit(OpCodes.Callvirt, sequence);
            il.Emit(OpCodes.Stloc, subtrees);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, subtrees);
            il.Emit(OpCodes.Callvirt, hasData);
            il.Emit(OpCodes.Brfalse, nextKind);
            il.Emit(OpCodes.Ldloc, subtrees);
            il.Emit(OpCodes.Ldloc, noTag);
            il.Emit(OpCodes.Callvirt, sequence);
            il.Emit(OpCodes.Stloc, subtree);
            il.Emit(OpCodes.Ldc_I4, (int)AsnEncodingRules.DER);
            il.Emit(OpCodes.Newobj, typeof(AsnWriter).GetConstructor([typeof(AsnEncodingRules)])!);
            il.Emit(OpCodes.Stloc, writer);
            EmitX509NameWriterScope(il, writer, noTag, "PushSequence");
            il.Emit(OpCodes.Ldloc, subtree);
            il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
            il.Emit(OpCodes.Stloc, encoded);
            il.Emit(OpCodes.Ldloc, writer);
            il.Emit(OpCodes.Ldloca, encoded);
            il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetProperty("Span")!.GetMethod!);
            il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("WriteEncodedValue", [typeof(ReadOnlySpan<byte>)])!);
            EmitX509NameWriterScope(il, writer, noTag, "PopSequence");
            il.Emit(OpCodes.Ldloc, writer);
            il.Emit(OpCodes.Callvirt, typeof(AsnWriter).GetMethod("Encode", Type.EmptyTypes)!);
            il.Emit(OpCodes.Call, validateGeneralNames);
            for (int bound = 0; bound <= 1; bound++)
            {
                var nextBound = il.DefineLabel();
                UnlessTag(subtree, bound, nextBound);
                il.Emit(OpCodes.Ldloc, subtree);
                ContextTag(bound, false, true);
                il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadInteger", [typeof(Asn1Tag?)])!);
                il.Emit(OpCodes.Pop);
                il.MarkLabel(nextBound);
            }
            il.Emit(OpCodes.Ldloc, subtree);
            il.Emit(OpCodes.Callvirt, empty);
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(nextKind);
        }
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Callvirt, empty);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
