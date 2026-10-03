using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private static void EmitStrictUriUtf8(ILGenerator il)
    {
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, typeof(UTF8Encoding).GetConstructor([typeof(bool), typeof(bool)])!);
    }

    private void EmitUriEncodeValidation(ILGenerator il, LocalBuilder source)
    {
        EmitStrictUriUtf8(il);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetByteCount", [_types.String])!);
        il.Emit(OpCodes.Pop);
    }

    private void EmitUriDecodeValidation(ILGenerator il, LocalBuilder source)
    {
        var length = il.DeclareLocal(_types.Int32);
        var position = il.DeclareLocal(_types.Int32);
        var count = il.DeclareLocal(_types.Int32);
        var bytes = il.DeclareLocal(_types.ByteArray);
        var loop = il.DefineLabel();
        var run = il.DefineLabel();
        var decode = il.DefineLabel();
        var next = il.DefineLabel();
        var invalid = il.DefineLabel();
        var done = il.DefineLabel();
        var getChar = _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!;
        var isHex = _types.GetMethod(_types.Uri, "IsHexDigit", [_types.Char])!;

        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "get_Length"));
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytes);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Callvirt, getChar);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Bne_Un, next);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, count);

        il.MarkLabel(run);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, invalid);
        foreach (var offset in new[] { 1, 2 })
        {
            il.Emit(OpCodes.Ldloc, source);
            il.Emit(OpCodes.Ldloc, position);
            il.Emit(OpCodes.Ldc_I4, offset);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, getChar);
            il.Emit(OpCodes.Call, isHex);
            il.Emit(OpCodes.Brfalse, invalid);
        }
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloca, position);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Uri, "HexUnescape", [_types.String, _types.Int32.MakeByRefType()])!);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, decode);
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Callvirt, getChar);
        il.Emit(OpCodes.Ldc_I4, '%');
        il.Emit(OpCodes.Beq, run);

        il.MarkLabel(decode);
        EmitStrictUriUtf8(il);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetString", [_types.ByteArray, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, position);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, position);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(invalid);
        EmitMalformedUriError(il);
        il.MarkLabel(done);
    }

    private void EmitMalformedUriError(ILGenerator il)
    {
        // The existing host-exception bridge recognizes this guest error prefix
        // and creates a URIError, including when invocation uses reflection.
        il.Emit(OpCodes.Ldstr, "URIError: URI malformed");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }
}
