using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the opaque native rule store used by the TypeScript net.BlockList facade.
/// The emitted type deliberately contains no guest-facing validation, display, or
/// check API: it only mirrors mutations and supports server-thread peer filtering.
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _blockListTypeBuilder = null!;
    private FieldBuilder _blockListRulesField = null!;
    private MethodBuilder _blockListParseAddrMethod = null!;
    private MethodBuilder _blockListCompareBytesMethod = null!;
    private MethodBuilder _blockListSubnetBoundsMethod = null!;
    private MethodBuilder _blockListAddRuleMethod = null!;
    private MethodBuilder _blockListMatchBytesMethod = null!;

    private void EmitTSNetBlockListTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$BlockList",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            typeof(object));
        _blockListTypeBuilder = typeBuilder;
        runtime.BlockListType = typeBuilder;

        // Each rule is object[3] { boxed bool isV6, byte[] start, byte[] end }.
        _blockListRulesField = typeBuilder.DefineField(
            "_rules", _types.ListOfObject, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        runtime.BlockListCtor = ctor;
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
            il.Emit(OpCodes.Stfld, _blockListRulesField);
            il.Emit(OpCodes.Ret);
        }

        EmitBlockListParseAddr(typeBuilder);
        EmitBlockListCompareBytes(typeBuilder);
        EmitBlockListSubnetBounds(typeBuilder);
        EmitBlockListAddRule(typeBuilder);
        EmitBlockListMatchBytes(typeBuilder);
        EmitBlockListAddAddress(typeBuilder);
        EmitBlockListAddRange(typeBuilder);
        EmitBlockListAddSubnet(typeBuilder);
        EmitBlockListCheckIp(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitBlockListParseAddr(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_ParseAddr",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.ByteArray,
            [_types.String, _types.Boolean]);
        _blockListParseAddrMethod = method;

        var il = method.GetILGenerator();
        var ipLocal = il.DeclareLocal(typeof(IPAddress));
        var retNull = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, ipLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod(
            "TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, retNull);

        var v4Path = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, v4Path);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Bne_Un, retNull);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(v4Path);
        var mappedPath = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetwork);
        il.Emit(OpCodes.Bne_Un, mappedPath);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(mappedPath);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("IsIPv4MappedToIPv6")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, retNull);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("MapToIPv4")!);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(retNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBlockListCompareBytes(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_CompareBytes",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            [_types.ByteArray, _types.ByteArray]);
        _blockListCompareBytesMethod = method;

        var il = method.GetILGenerator();
        var index = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel();
        var condition = il.DefineLabel();
        var next = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, condition);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Beq, next);
        var greater = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Bge_Un, greater);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(greater);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(condition);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBlockListSubnetBounds(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_SubnetBounds",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            [_types.ByteArray, _types.Int32, _types.ByteArray, _types.ByteArray]);
        _blockListSubnetBoundsMethod = method;

        var il = method.GetILGenerator();
        var index = il.DeclareLocal(_types.Int32);
        var bits = il.DeclareLocal(_types.Int32);
        var mask = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel();
        var condition = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, condition);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, bits);

        var fullMask = il.DefineLabel();
        var zeroMask = il.DefineLabel();
        var maskDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Bge, fullMask);
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, zeroMask);
        il.Emit(OpCodes.Ldc_I4, 0xff);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldc_I4, 0xff);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, mask);
        il.Emit(OpCodes.Br, maskDone);
        il.MarkLabel(fullMask);
        il.Emit(OpCodes.Ldc_I4, 0xff);
        il.Emit(OpCodes.Stloc, mask);
        il.Emit(OpCodes.Br, maskDone);
        il.MarkLabel(zeroMask);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, mask);
        il.MarkLabel(maskDone);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldloc, mask);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldloc, mask);
        il.Emit(OpCodes.Not);
        il.Emit(OpCodes.Ldc_I4, 0xff);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(condition);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loop);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBlockListAddRule(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_AddRule",
            MethodAttributes.Private,
            typeof(void),
            [_types.Boolean, _types.ByteArray, _types.ByteArray]);
        _blockListAddRuleMethod = method;

        var il = method.GetILGenerator();
        var rule = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, rule);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _blockListRulesField);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBlockListMatchBytes(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_MatchBytes",
            MethodAttributes.Private,
            _types.Boolean,
            [_types.ByteArray, _types.Boolean]);
        _blockListMatchBytesMethod = method;

        var il = method.GetILGenerator();
        var index = il.DeclareLocal(_types.Int32);
        var rule = il.DeclareLocal(typeof(object[]));
        var loop = il.DefineLabel();
        var condition = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, condition);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _blockListRulesField);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
        il.Emit(OpCodes.Castclass, typeof(object[]));
        il.Emit(OpCodes.Stloc, rule);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Bne_Un, next);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _blockListCompareBytesMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, next);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, rule);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Call, _blockListCompareBytesMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, next);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(condition);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _blockListRulesField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Blt, loop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBlockListAddAddress(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "AddAddress", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var address = il.DeclareLocal(_types.String);
        var isV6 = il.DeclareLocal(_types.Boolean);
        var bytes = il.DeclareLocal(_types.ByteArray);
        var invalid = il.DefineLabel();
        EmitBlockListAddressAndFamily(il, 1, 2, address, isV6, invalid);
        il.Emit(OpCodes.Ldloc, address);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Call, _blockListAddRuleMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        EmitInvalidBlockListMutation(il, invalid);
    }

    private void EmitBlockListAddRange(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "AddRange", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var startAddress = il.DeclareLocal(_types.String);
        var endAddress = il.DeclareLocal(_types.String);
        var isV6 = il.DeclareLocal(_types.Boolean);
        var start = il.DeclareLocal(_types.ByteArray);
        var end = il.DeclareLocal(_types.ByteArray);
        var invalid = il.DefineLabel();
        EmitBlockListAddressAndFamily(il, 1, 3, startAddress, isV6, invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, endAddress);
        il.Emit(OpCodes.Ldloc, startAddress);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, start);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, endAddress);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, end);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Call, _blockListCompareBytesMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Call, _blockListAddRuleMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        EmitInvalidBlockListMutation(il, invalid);
    }

    private void EmitBlockListAddSubnet(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "AddSubnet", MethodAttributes.Public, _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        var il = method.GetILGenerator();
        var address = il.DeclareLocal(_types.String);
        var isV6 = il.DeclareLocal(_types.Boolean);
        var prefix = il.DeclareLocal(_types.Int32);
        var bytes = il.DeclareLocal(_types.ByteArray);
        var start = il.DeclareLocal(_types.ByteArray);
        var end = il.DeclareLocal(_types.ByteArray);
        var invalid = il.DefineLabel();
        EmitBlockListAddressAndFamily(il, 1, 3, address, isV6, invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, prefix);
        il.Emit(OpCodes.Ldloc, prefix);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, invalid);
        var v6Limit = il.DefineLabel();
        var limitDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Brtrue, v6Limit);
        il.Emit(OpCodes.Ldloc, prefix);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Bgt, invalid);
        il.Emit(OpCodes.Br, limitDone);
        il.MarkLabel(v6Limit);
        il.Emit(OpCodes.Ldloc, prefix);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Bgt, invalid);
        il.MarkLabel(limitDone);
        il.Emit(OpCodes.Ldloc, address);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Brfalse, invalid);
        EmitNewByteArrayLike(il, bytes, start);
        EmitNewByteArrayLike(il, bytes, end);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldloc, prefix);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Call, _blockListSubnetBoundsMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isV6);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldloc, end);
        il.Emit(OpCodes.Call, _blockListAddRuleMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        EmitInvalidBlockListMutation(il, invalid);
    }

    private void EmitBlockListCheckIp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CheckIp", MethodAttributes.Public, _types.Boolean, [typeof(IPAddress)]);
        runtime.BlockListCheckIp = method;
        var il = method.GetILGenerator();
        var address = il.DeclareLocal(typeof(IPAddress));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, address);
        var notMapped = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, address);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("IsIPv4MappedToIPv6")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notMapped);
        il.Emit(OpCodes.Ldloc, address);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("MapToIPv4")!);
        il.Emit(OpCodes.Stloc, address);
        il.MarkLabel(notMapped);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, address);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ldloc, address);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Call, _blockListMatchBytesMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBlockListAddressAndFamily(
        ILGenerator il,
        int addressArgument,
        int familyArgument,
        LocalBuilder address,
        LocalBuilder isV6,
        Label invalid)
    {
        il.Emit(OpCodes.Ldarg, addressArgument);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg, addressArgument);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, address);
        var notString = il.DefineLabel();
        var familyDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, familyArgument);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldarg, familyArgument);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ldstr, "ipv6");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, isV6);
        il.Emit(OpCodes.Br, familyDone);
        il.MarkLabel(notString);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isV6);
        il.MarkLabel(familyDone);
    }

    private static void EmitNewByteArrayLike(ILGenerator il, LocalBuilder source, LocalBuilder target)
    {
        il.Emit(OpCodes.Ldloc, source);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stloc, target);
    }

    private static void EmitInvalidBlockListMutation(ILGenerator il, Label invalid)
    {
        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldstr, "Invalid native BlockList mutation");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }
}
