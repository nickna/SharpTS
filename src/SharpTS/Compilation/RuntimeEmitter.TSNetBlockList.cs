using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $SocketAddress and $BlockList classes for compiled net support (#1069).
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSSocketAddress /
/// SharpTSBlockList. Both types are pure-BCL (IPAddress + byte[] compares) so
/// compiled net stays fully standalone.
///
/// Emitted fully (CreateType) before $NetSocket/$NetServer Phase 1 so the server's
/// _blockList field consumers and the module factories can reference the created
/// members. Guest-facing validation errors use the pre-allocated
/// $Runtime.CreateException MethodBuilder (DefineRuntimeClassPhase1).
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _socketAddressTypeBuilder = null!;
    private FieldBuilder _socketAddressAddressField = null!;
    private FieldBuilder _socketAddressFamilyField = null!;
    private FieldBuilder _socketAddressPortField = null!;
    private FieldBuilder _socketAddressFlowLabelField = null!;

    private TypeBuilder _blockListTypeBuilder = null!;
    private FieldBuilder _blockListRulesField = null!;
    private MethodBuilder _blockListParseAddrMethod = null!;
    private MethodBuilder _blockListCompareBytesMethod = null!;
    private MethodBuilder _blockListCanonAddrMethod = null!;
    private MethodBuilder _blockListExtractAddrMethod = null!;
    private MethodBuilder _blockListExtractIsV6Method = null!;
    private MethodBuilder _blockListSubnetBoundsMethod = null!;
    private MethodBuilder _blockListAddRuleMethod = null!;
    private MethodBuilder _blockListMatchBytesMethod = null!;

    /// <summary>
    /// Emits both types. Called inside the UsesNet gate, before net Phase 1.
    /// </summary>
    private void EmitTSNetBlockListTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitSocketAddressType(moduleBuilder, runtime);
        EmitBlockListType(moduleBuilder, runtime);
    }

    // ════════════════════════════════════════════════════════════════
    //  $SocketAddress
    // ════════════════════════════════════════════════════════════════

    private void EmitSocketAddressType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$SocketAddress",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );
        _socketAddressTypeBuilder = typeBuilder;

        _socketAddressAddressField = typeBuilder.DefineField("_address", _types.String, FieldAttributes.Assembly);
        _socketAddressFamilyField = typeBuilder.DefineField("_family", _types.String, FieldAttributes.Assembly);
        _socketAddressPortField = typeBuilder.DefineField("_port", _types.Double, FieldAttributes.Private);
        _socketAddressFlowLabelField = typeBuilder.DefineField("_flowlabel", _types.Double, FieldAttributes.Private);

        EmitSocketAddressCtor(typeBuilder, runtime);
        EmitSocketAddressGetMember(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $SocketAddress(object options) — Node defaults (family "ipv4",
    /// address "127.0.0.1"/"::", port 0, flowlabel 0) with best-effort option parsing.
    /// </summary>
    private void EmitSocketAddressCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.SocketAddressCtor = ctor;

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        // Defaults
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "ipv4");
        il.Emit(OpCodes.Stfld, _socketAddressFamilyField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "127.0.0.1");
        il.Emit(OpCodes.Stfld, _socketAddressAddressField);

        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, done);

        var valLocal = il.DeclareLocal(_types.Object);

        // family
        {
            var noFamily = il.DefineLabel();
            var notV6 = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "family");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noFamily);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brfalse, noFamily);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
            il.Emit(OpCodes.Stfld, _socketAddressFamilyField);
            // if (_family == "ipv6") _address = "::"
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _socketAddressFamilyField);
            il.Emit(OpCodes.Ldstr, "ipv6");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
            il.Emit(OpCodes.Brfalse, notV6);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "::");
            il.Emit(OpCodes.Stfld, _socketAddressAddressField);
            il.MarkLabel(notV6);
            il.MarkLabel(noFamily);
        }

        // address
        {
            var noAddress = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "address");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noAddress);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brfalse, noAddress);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stfld, _socketAddressAddressField);
            il.MarkLabel(noAddress);
        }

        // port
        {
            var noPort = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "port");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noPort);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brfalse, noPort);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Stfld, _socketAddressPortField);
            il.MarkLabel(noPort);
        }

        // flowlabel
        {
            var noFlow = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "flowlabel");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noFlow);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brfalse, noFlow);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Stfld, _socketAddressFlowLabelField);
            il.MarkLabel(noFlow);
        }

        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSocketAddressGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var addressLabel = il.DefineLabel();
        var familyLabel = il.DefineLabel();
        var portLabel = il.DefineLabel();
        var flowLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitStringCheck(il, 1, "address", addressLabel);
        EmitStringCheck(il, 1, "family", familyLabel);
        EmitStringCheck(il, 1, "port", portLabel);
        EmitStringCheck(il, 1, "flowlabel", flowLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        il.MarkLabel(addressLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _socketAddressAddressField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(familyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _socketAddressFamilyField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(portLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _socketAddressPortField);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(flowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _socketAddressFlowLabelField);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  $BlockList
    // ════════════════════════════════════════════════════════════════

    private void EmitBlockListType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$BlockList",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );
        _blockListTypeBuilder = typeBuilder;
        runtime.BlockListType = typeBuilder;

        // _rules: List<object> of object[4] { boxed bool isV6, byte[] start, byte[] end, string display }
        _blockListRulesField = typeBuilder.DefineField("_rules", _types.ListOfObject, FieldAttributes.Private);

        // ctor: _rules = new List<object>()
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
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

        // Static/private helpers first (referenced by the public surface)
        EmitBlockListParseAddr(typeBuilder);
        EmitBlockListCompareBytes(typeBuilder);
        EmitBlockListCanonAddr(typeBuilder);
        EmitBlockListExtractAddr(typeBuilder);
        EmitBlockListExtractIsV6(typeBuilder);
        EmitBlockListSubnetBounds(typeBuilder);
        EmitBlockListAddRule(typeBuilder);
        EmitBlockListMatchBytes(typeBuilder);

        // Public surface
        EmitBlockListAddAddress(typeBuilder, runtime);
        EmitBlockListAddRange(typeBuilder, runtime);
        EmitBlockListAddSubnet(typeBuilder, runtime);
        EmitBlockListCheck(typeBuilder, runtime);
        EmitBlockListCheckIp(typeBuilder, runtime);
        EmitBlockListGetMember(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: private static byte[] _ParseAddr(string addr, bool wantV6)
    /// Canonical family bytes: 4 for ipv4 (accepting IPv4-mapped IPv6), 16 for ipv6;
    /// null when the address doesn't parse in the requested family.
    /// </summary>
    private void EmitBlockListParseAddr(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_ParseAddr",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.ByteArray,
            [_types.String, _types.Boolean]
        );
        _blockListParseAddrMethod = method;

        var il = method.GetILGenerator();
        var ipLocal = il.DeclareLocal(typeof(IPAddress));
        var retNull = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, ipLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, retNull);

        var v4Path = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, v4Path);

        // wantV6: return family == InterNetworkV6 ? bytes : null
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Bne_Un, retNull);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(v4Path);
        // family == InterNetwork → bytes
        var notPlainV4 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetwork);
        il.Emit(OpCodes.Bne_Un, notPlainV4);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPlainV4);
        // family == InterNetworkV6 && IsIPv4MappedToIPv6 → MapToIPv4().GetAddressBytes()
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Bne_Un, retNull);
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

    /// <summary>
    /// Emits: private static int _CompareBytes(byte[] a, byte[] b) — lexicographic.
    /// </summary>
    private void EmitBlockListCompareBytes(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_CompareBytes",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            [_types.ByteArray, _types.ByteArray]
        );
        _blockListCompareBytesMethod = method;

        var il = method.GetILGenerator();
        var iLocal = il.DeclareLocal(_types.Int32);
        var loopTop = il.DefineLabel();
        var loopCond = il.DefineLabel();
        var nextIter = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopTop);
        // if (a[i] == b[i]) continue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Beq, nextIter);
        // return a[i] < b[i] ? -1 : 1
        var retOne = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Bge_Un, retOne);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(retOne);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nextIter);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loopTop);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static string _CanonAddr(string addr) — parsed normalization for display.
    /// </summary>
    private void EmitBlockListCanonAddr(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_CanonAddr",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        _blockListCanonAddrMethod = method;

        var il = method.GetILGenerator();
        var ipLocal = il.DeclareLocal(typeof(IPAddress));
        var asIs = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, ipLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, asIs);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(asIs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static string _ExtractAddr(object arg) — string as-is,
    /// $SocketAddress → its address, else null.
    /// </summary>
    private void EmitBlockListExtractAddr(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_ExtractAddr",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        _blockListExtractAddrMethod = method;

        var il = method.GetILGenerator();
        var notString = il.DefineLabel();
        var notSa = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _socketAddressTypeBuilder);
        il.Emit(OpCodes.Brfalse, notSa);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _socketAddressTypeBuilder);
        il.Emit(OpCodes.Ldfld, _socketAddressAddressField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSa);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static bool _ExtractIsV6(object addrArg, object familyArg) —
    /// a $SocketAddress carries its own family; otherwise the family string decides
    /// (default ipv4).
    /// </summary>
    private void EmitBlockListExtractIsV6(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_ExtractIsV6",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        _blockListExtractIsV6Method = method;

        var il = method.GetILGenerator();
        var notSa = il.DefineLabel();
        var notString = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _socketAddressTypeBuilder);
        il.Emit(OpCodes.Brfalse, notSa);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _socketAddressTypeBuilder);
        il.Emit(OpCodes.Ldfld, _socketAddressFamilyField);
        il.Emit(OpCodes.Ldstr, "ipv6");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSa);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ldstr, "ipv6");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notString);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static void _SubnetBounds(byte[] addr, int prefix, byte[] start, byte[] end)
    /// Fills the inclusive [network, broadcast] bounds for addr/prefix.
    /// </summary>
    private void EmitBlockListSubnetBounds(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_SubnetBounds",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            [_types.ByteArray, _types.Int32, _types.ByteArray, _types.ByteArray]
        );
        _blockListSubnetBoundsMethod = method;

        var il = method.GetILGenerator();
        var iLocal = il.DeclareLocal(_types.Int32);
        var bitsLocal = il.DeclareLocal(_types.Int32);
        var maskLocal = il.DeclareLocal(_types.Int32);
        var loopTop = il.DefineLabel();
        var loopCond = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopTop);
        // bits = prefix - i * 8
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, bitsLocal);

        // mask = bits >= 8 ? 0xFF : bits <= 0 ? 0 : (0xFF << (8 - bits)) & 0xFF
        var fullMask = il.DefineLabel();
        var zeroMask = il.DefineLabel();
        var maskDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bitsLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Bge, fullMask);
        il.Emit(OpCodes.Ldloc, bitsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, zeroMask);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Ldloc, bitsLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, maskLocal);
        il.Emit(OpCodes.Br, maskDone);
        il.MarkLabel(fullMask);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.Stloc, maskLocal);
        il.Emit(OpCodes.Br, maskDone);
        il.MarkLabel(zeroMask);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, maskLocal);
        il.MarkLabel(maskDone);

        // start[i] = (byte)(addr[i] & mask)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldloc, maskLocal);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // end[i] = (byte)(addr[i] | (~mask & 0xFF))
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldloc, maskLocal);
        il.Emit(OpCodes.Not);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loopTop);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private void _AddRule(bool isV6, byte[] start, byte[] end, string display)
    /// </summary>
    private void EmitBlockListAddRule(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_AddRule",
            MethodAttributes.Private,
            typeof(void),
            [_types.Boolean, _types.ByteArray, _types.ByteArray, _types.String]
        );
        _blockListAddRuleMethod = method;

        var il = method.GetILGenerator();
        var ruleLocal = il.DeclareLocal(typeof(object[]));

        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, ruleLocal);
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _blockListRulesField);
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private bool _MatchBytes(byte[] bytes, bool isV6) — rule scan.
    /// </summary>
    private void EmitBlockListMatchBytes(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_MatchBytes",
            MethodAttributes.Private,
            _types.Boolean,
            [_types.ByteArray, _types.Boolean]
        );
        _blockListMatchBytesMethod = method;

        var il = method.GetILGenerator();
        var iLocal = il.DeclareLocal(_types.Int32);
        var ruleLocal = il.DeclareLocal(typeof(object[]));
        var loopTop = il.DefineLabel();
        var loopCond = il.DefineLabel();
        var nextIter = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopTop);
        // rule = (object[])_rules[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _blockListRulesField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
        il.Emit(OpCodes.Castclass, typeof(object[]));
        il.Emit(OpCodes.Stloc, ruleLocal);

        // if ((bool)rule[0] != isV6) continue
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Bne_Un, nextIter);

        // if (_CompareBytes(rule[1], bytes) <= 0 && _CompareBytes(bytes, rule[2]) <= 0) return true
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _blockListCompareBytesMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, nextIter);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, ruleLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Call, _blockListCompareBytesMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, nextIter);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nextIter);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _blockListRulesField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Blt, loopTop);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits a guest-visible TypeError throw (message + TSTypeErrorCtor + CreateException).
    /// </summary>
    private void EmitBlockListThrow(ILGenerator il, EmittedRuntime runtime, string message)
    {
        il.Emit(OpCodes.Ldstr, message);
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
    }

    /// <summary>
    /// Emits: public object AddAddress(object address, object family)
    /// </summary>
    private void EmitBlockListAddAddress(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AddAddress",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var addrLocal = il.DeclareLocal(_types.String);
        var isV6Local = il.DeclareLocal(_types.Boolean);
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _blockListExtractAddrMethod);
        il.Emit(OpCodes.Stloc, addrLocal);
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _blockListExtractIsV6Method);
        il.Emit(OpCodes.Stloc, isV6Local);

        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // display = "Address: " + fam + " " + canon
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldstr, "Address: ");
        EmitBlockListFamilyString(il, isV6Local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Call, _blockListCanonAddrMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Call, _blockListAddRuleMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        EmitBlockListThrow(il, runtime, "net.BlockList.addAddress: invalid address");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Pushes "IPv6" or "IPv4" onto the stack based on the bool local.</summary>
    private void EmitBlockListFamilyString(ILGenerator il, LocalBuilder isV6Local)
    {
        var v6 = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Brtrue, v6);
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(v6);
        il.Emit(OpCodes.Ldstr, "IPv6");
        il.MarkLabel(done);
    }

    /// <summary>
    /// Emits: public object AddRange(object start, object end, object family)
    /// </summary>
    private void EmitBlockListAddRange(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AddRange",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var startAddrLocal = il.DeclareLocal(_types.String);
        var endAddrLocal = il.DeclareLocal(_types.String);
        var isV6Local = il.DeclareLocal(_types.Boolean);
        var startBytesLocal = il.DeclareLocal(_types.ByteArray);
        var endBytesLocal = il.DeclareLocal(_types.ByteArray);
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _blockListExtractAddrMethod);
        il.Emit(OpCodes.Stloc, startAddrLocal);
        il.Emit(OpCodes.Ldloc, startAddrLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _blockListExtractAddrMethod);
        il.Emit(OpCodes.Stloc, endAddrLocal);
        il.Emit(OpCodes.Ldloc, endAddrLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, _blockListExtractIsV6Method);
        il.Emit(OpCodes.Stloc, isV6Local);

        il.Emit(OpCodes.Ldloc, startAddrLocal);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, startBytesLocal);
        il.Emit(OpCodes.Ldloc, startBytesLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldloc, endAddrLocal);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, endBytesLocal);
        il.Emit(OpCodes.Ldloc, endBytesLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // if (_CompareBytes(start, end) > 0) throw
        il.Emit(OpCodes.Ldloc, startBytesLocal);
        il.Emit(OpCodes.Ldloc, endBytesLocal);
        il.Emit(OpCodes.Call, _blockListCompareBytesMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, throwLabel);

        // display = "Range: " + fam + " " + canonStart + "-" + canonEnd
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Ldloc, startBytesLocal);
        il.Emit(OpCodes.Ldloc, endBytesLocal);
        il.Emit(OpCodes.Ldstr, "Range: ");
        EmitBlockListFamilyString(il, isV6Local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldloc, startAddrLocal);
        il.Emit(OpCodes.Call, _blockListCanonAddrMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, "-");
        il.Emit(OpCodes.Ldloc, endAddrLocal);
        il.Emit(OpCodes.Call, _blockListCanonAddrMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Call, _blockListAddRuleMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        EmitBlockListThrow(il, runtime, "net.BlockList.addRange: invalid range");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object AddSubnet(object network, object prefix, object family)
    /// </summary>
    private void EmitBlockListAddSubnet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AddSubnet",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var addrLocal = il.DeclareLocal(_types.String);
        var isV6Local = il.DeclareLocal(_types.Boolean);
        var prefixLocal = il.DeclareLocal(_types.Int32);
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        var startLocal = il.DeclareLocal(_types.ByteArray);
        var endLocal = il.DeclareLocal(_types.ByteArray);
        var maxLocal = il.DeclareLocal(_types.Int32);
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _blockListExtractAddrMethod);
        il.Emit(OpCodes.Stloc, addrLocal);
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, prefixLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, _blockListExtractIsV6Method);
        il.Emit(OpCodes.Stloc, isV6Local);

        // max = isV6 ? 128 : 32; if (prefix < 0 || prefix > max) throw
        var v6Max = il.DefineLabel();
        var maxDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Brtrue, v6Max);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Stloc, maxLocal);
        il.Emit(OpCodes.Br, maxDone);
        il.MarkLabel(v6Max);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Stloc, maxLocal);
        il.MarkLabel(maxDone);
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Ldloc, maxLocal);
        il.Emit(OpCodes.Bgt, throwLabel);

        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // start = new byte[len]; end = new byte[len]; _SubnetBounds(bytes, prefix, start, end)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, prefixLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Call, _blockListSubnetBoundsMethod);

        // display = "Subnet: " + fam + " " + canon + "/" + prefix
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldstr, "Subnet: ");
        EmitBlockListFamilyString(il, isV6Local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Call, _blockListCanonAddrMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldloca, prefixLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Call, _blockListAddRuleMethod);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        EmitBlockListThrow(il, runtime, "net.BlockList.addSubnet: invalid subnet");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Check(object address, object family) — boxed bool;
    /// never throws (unparseable input is simply not blocked). A v6-family query
    /// also checks a v4-mapped address against the v4 rules.
    /// </summary>
    private void EmitBlockListCheck(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Check",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var addrLocal = il.DeclareLocal(_types.String);
        var isV6Local = il.DeclareLocal(_types.Boolean);
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        var retFalse = il.DefineLabel();
        var retTrue = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _blockListExtractAddrMethod);
        il.Emit(OpCodes.Stloc, addrLocal);
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Brfalse, retFalse);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _blockListExtractIsV6Method);
        il.Emit(OpCodes.Stloc, isV6Local);

        var v4Only = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isV6Local);
        il.Emit(OpCodes.Brfalse, v4Only);

        // v6 query: match v6 rules, then v4 rules via the v4-mapped extraction
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytesLocal);
        var tryMapped = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Brfalse, tryMapped);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _blockListMatchBytesMethod);
        il.Emit(OpCodes.Brtrue, retTrue);
        il.MarkLabel(tryMapped);
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Brfalse, retFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _blockListMatchBytesMethod);
        il.Emit(OpCodes.Brtrue, retTrue);
        il.Emit(OpCodes.Br, retFalse);

        il.MarkLabel(v4Only);
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _blockListParseAddrMethod);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Brfalse, retFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _blockListMatchBytesMethod);
        il.Emit(OpCodes.Brtrue, retTrue);

        il.MarkLabel(retFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(retTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool CheckIp(IPAddress ip) — host-side check used by the
    /// $NetServer accept closure. IPv4-mapped peers are checked as IPv4.
    /// </summary>
    private void EmitBlockListCheckIp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CheckIp",
            MethodAttributes.Public,
            _types.Boolean,
            [typeof(IPAddress)]
        );
        runtime.BlockListCheckIp = method;

        var il = method.GetILGenerator();
        var ipLocal = il.DeclareLocal(typeof(IPAddress));

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, ipLocal);

        // if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4()
        var notMapped = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("IsIPv4MappedToIPv6")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notMapped);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("MapToIPv4")!);
        il.Emit(OpCodes.Stloc, ipLocal);
        il.MarkLabel(notMapped);

        // return _MatchBytes(ip.GetAddressBytes(), ip.AddressFamily == InterNetworkV6)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Call, _blockListMatchBytesMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetMember(string name) — "rules" returns a $Array of
    /// the rule display strings.
    /// </summary>
    private void EmitBlockListGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var rulesLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitStringCheck(il, 1, "rules", rulesLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        il.MarkLabel(rulesLabel);
        {
            var listLocal = il.DeclareLocal(_types.ListOfObject);
            var iLocal = il.DeclareLocal(_types.Int32);
            var loopTop = il.DefineLabel();
            var loopCond = il.DefineLabel();

            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
            il.Emit(OpCodes.Stloc, listLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            il.Emit(OpCodes.Br, loopCond);

            il.MarkLabel(loopTop);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _blockListRulesField);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
            il.Emit(OpCodes.Castclass, typeof(object[]));
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);

            il.MarkLabel(loopCond);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _blockListRulesField);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
            il.Emit(OpCodes.Blt, loopTop);

            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }
}
