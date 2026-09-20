using System.Formats.Asn1;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder EmitX509ReadNameString(TypeBuilder type)
    {
        var method = type.DefineMethod("ReadNameString", MethodAttributes.Private | MethodAttributes.Static,
            typeof(string), [typeof(AsnReader), typeof(int)]);
        var il = method.GetILGenerator();
        var noTag = il.DeclareLocal(typeof(Asn1Tag?));
        var content = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var result = il.DeclareLocal(typeof(string));
        var universal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, 28);
        il.Emit(OpCodes.Beq, universal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, noTag);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadCharacterString", [typeof(UniversalTagNumber), typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(universal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("PeekContentBytes")!);
        il.Emit(OpCodes.Stloc, content);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue")!);
        il.Emit(OpCodes.Pop);
        var end = il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(UTF32Encoding).GetConstructor([typeof(bool), typeof(bool), typeof(bool)])!);
        il.Emit(OpCodes.Ldloca, content);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetProperty("Span")!.GetMethod!);
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", [typeof(ReadOnlySpan<byte>)])!);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Leave, end);
        il.BeginCatchBlock(typeof(DecoderFallbackException));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "Invalid UniversalString name.");
        il.Emit(OpCodes.Newobj, typeof(AsnContentException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
