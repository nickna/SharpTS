using System.Formats.Asn1;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitX509ValidateBasicConstraints(TypeBuilder type, bool requireAuthority = false)
    {
        var method = type.DefineMethod(requireAuthority ? "BasicConstraintsHasAuthority" : "ValidateBasicConstraints", MethodAttributes.Private | MethodAttributes.Static,
            typeof(bool), [typeof(byte[])]);
        var il = method.GetILGenerator();
        var options = il.DeclareLocal(typeof(AsnReaderOptions));
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var reader = il.DeclareLocal(typeof(AsnReader));
        var constraints = il.DeclareLocal(typeof(AsnReader));
        var tag = il.DeclareLocal(typeof(Asn1Tag));
        var pathLength = il.DeclareLocal(typeof(BigInteger));
        var valid = il.DeclareLocal(typeof(bool));
        var authority = il.DeclareLocal(typeof(bool));
        var afterBoolean = il.DefineLabel();
        var done = il.DefineLabel();
        var hasData = typeof(AsnReader).GetProperty("HasData")!.GetMethod!;
        il.Emit(OpCodes.Ldarg_0);
        EmitX509NewDerReader(il, options);
        il.Emit(OpCodes.Stloc, reader);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, constraints);
        il.Emit(OpCodes.Ldloc, reader);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, afterBoolean);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekTag")!);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloca, tag);
        il.Emit(OpCodes.Ldsfld, typeof(Asn1Tag).GetField("Boolean")!);
        il.Emit(OpCodes.Call, typeof(Asn1Tag).GetMethod("HasSameClassAndValue")!);
        il.Emit(OpCodes.Brfalse, afterBoolean);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadBoolean", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, authority);
        il.MarkLabel(afterBoolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, valid);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Callvirt, hasData);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadInteger", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, pathLength);
        il.Emit(OpCodes.Ldloca, pathLength);
        il.Emit(OpCodes.Call, typeof(BigInteger).GetProperty("Sign")!.GetMethod!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Stloc, valid);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, constraints);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ThrowIfNotEmpty")!);
        il.Emit(OpCodes.Ldloc, valid);
        if (requireAuthority)
        {
            il.Emit(OpCodes.Ldloc, authority);
            il.Emit(OpCodes.And);
        }
        il.Emit(OpCodes.Ret);
        return method;
    }
}
