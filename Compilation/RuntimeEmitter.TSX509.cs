using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $X509Certificate class for standalone crypto.X509Certificate support (#1064).
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSX509Certificate.
/// Compiled-deferred members (clear runtime error): checkEmail, toLegacyObject;
/// infoAccess returns undefined; subjectAltName omits rfc822/URI entries
/// (the BCL SAN extension enumerates only DNS names and IP addresses).
/// All emitted IL is pure BCL — no SharpTS.dll reference.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitTSX509Class(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tb = moduleBuilder.DefineType(
            "$X509Certificate",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        // Fields — everything cheap-to-read is precomputed in the ctor.
        var certField = tb.DefineField("_cert", typeof(X509Certificate2), FieldAttributes.Private);
        var subjectField = tb.DefineField("_subject", _types.String, FieldAttributes.Private);
        var issuerField = tb.DefineField("_issuer", _types.String, FieldAttributes.Private);
        var cnField = tb.DefineField("_cn", _types.String, FieldAttributes.Private);
        var sanField = tb.DefineField("_san", _types.String, FieldAttributes.Private);
        var dnsField = tb.DefineField("_dnsNames", typeof(List<string>), FieldAttributes.Private);
        var ipsField = tb.DefineField("_ipNames", typeof(List<string>), FieldAttributes.Private);
        var caField = tb.DefineField("_ca", _types.Boolean, FieldAttributes.Private);

        // Static helpers
        var formatName = EmitX509FormatName(tb);
        var formatValidity = EmitX509FormatValidity(tb);
        var colonHex = EmitX509ColonHex(tb);
        var hostMatches = EmitX509HostMatches(tb);
        var extractCn = EmitX509ExtractCn(tb);
        var verifyWithPem = EmitX509VerifyWithPem(tb);

        var ctor = EmitX509Ctor(tb, runtime, certField, subjectField, issuerField, cnField,
            sanField, dnsField, ipsField, caField, formatName, extractCn);
        runtime.X509CertificateCtor = ctor;

        // --- simple string/bool property getters over precomputed fields ---
        EmitX509FieldGetter(tb, "subject", "get_Subject", subjectField);
        EmitX509FieldGetter(tb, "issuer", "get_Issuer", issuerField);
        EmitX509FieldGetter(tb, "subjectAltName", "get_SubjectAltName", sanField);

        EmitX509CaGetter(tb, caField);
        EmitX509SerialGetter(tb, certField);
        EmitX509ValidityGetter(tb, "validFrom", "get_ValidFrom", certField, formatValidity, notBefore: true);
        EmitX509ValidityGetter(tb, "validTo", "get_ValidTo", certField, formatValidity, notBefore: false);
        EmitX509ValidityDateGetter(tb, runtime, "validFromDate", "get_ValidFromDate", certField, notBefore: true);
        EmitX509ValidityDateGetter(tb, runtime, "validToDate", "get_ValidToDate", certField, notBefore: false);
        EmitX509FingerprintGetter(tb, "fingerprint", "get_Fingerprint", certField, colonHex, "SHA1");
        EmitX509FingerprintGetter(tb, "fingerprint256", "get_Fingerprint256", certField, colonHex, "SHA256");
        EmitX509FingerprintGetter(tb, "fingerprint512", "get_Fingerprint512", certField, colonHex, "SHA512");
        EmitX509RawGetter(tb, runtime, certField);
        EmitX509PublicKeyGetter(tb, runtime, certField);
        EmitX509KeyUsageGetter(tb, runtime, certField);
        EmitX509ExtKeyUsageGetter(tb, runtime, certField);
        EmitX509InfoAccessGetter(tb);

        // --- methods ---
        EmitX509Verify(tb, certField, verifyWithPem);
        EmitX509CheckHost(tb, runtime, dnsField, cnField, hostMatches);
        EmitX509CheckIp(tb, runtime, ipsField);
        EmitX509CheckIssued(tb, certField, subjectField, issuerField, verifyWithPem);
        EmitX509ToString(tb, certField);
        EmitX509NotSupported(tb, "CheckEmail", "X509Certificate.checkEmail is not supported in compiled mode (interpreter only); see SharpTS #1064");
        EmitX509NotSupported(tb, "ToLegacyObject", "X509Certificate.toLegacyObject is not supported in compiled mode (interpreter only); see SharpTS #1064");

        tb.CreateType();
    }

    /// <summary>
    /// Emits the runtime-class factory for `X509Certificate(pemOrDer)` named-import /
    /// dynamic-call dispatch. `new crypto.X509Certificate(...)` compiles directly to the ctor.
    /// </summary>
    private void EmitX509CertificateFactory(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_X509Certificate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.X509CertificateCtor!);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("crypto", "X509Certificate", method);
    }

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    private ConstructorBuilder EmitX509Ctor(
        TypeBuilder tb, EmittedRuntime runtime,
        FieldBuilder certField, FieldBuilder subjectField, FieldBuilder issuerField,
        FieldBuilder cnField, FieldBuilder sanField, FieldBuilder dnsField,
        FieldBuilder ipsField, FieldBuilder caField,
        MethodBuilder formatName, MethodBuilder extractCn)
    {
        var ctor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Object]);
        var il = ctor.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // --- load the certificate: string PEM / $Buffer (PEM or DER) ---
        var certLocal = il.DeclareLocal(typeof(X509Certificate2));
        var strLabel = il.DefineLabel();
        var bufLabel = il.DefineLabel();
        var loadedLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, strLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, bufLabel);

        il.Emit(OpCodes.Ldstr, "X509Certificate: argument must be a PEM string or Buffer");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        // PEM string
        il.MarkLabel(strLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Call, typeof(X509Certificate2).GetMethod("CreateFromPem", [typeof(ReadOnlySpan<char>)])!);
        il.Emit(OpCodes.Stloc, certLocal);
        il.Emit(OpCodes.Br, loadedLabel);

        // Buffer: PEM if it starts with '-', else DER
        il.MarkLabel(bufLabel);
        var dataLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, dataLocal);

        var derLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Ble, derLabel);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Bne_Un, derLabel);

        // PEM in a Buffer
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetString", [typeof(byte[])])!);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Call, typeof(X509Certificate2).GetMethod("CreateFromPem", [typeof(ReadOnlySpan<char>)])!);
        il.Emit(OpCodes.Stloc, certLocal);
        il.Emit(OpCodes.Br, loadedLabel);

        // DER
        il.MarkLabel(derLabel);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Call, typeof(X509CertificateLoader).GetMethod("LoadCertificate", [typeof(byte[])])!);
        il.Emit(OpCodes.Stloc, certLocal);

        il.MarkLabel(loadedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, certLocal);
        il.Emit(OpCodes.Stfld, certField);

        // --- subject / issuer / cn ---
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, certLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("SubjectName")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, formatName);
        il.Emit(OpCodes.Stfld, subjectField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, certLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("IssuerName")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, formatName);
        il.Emit(OpCodes.Stfld, issuerField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, subjectField);
        il.Emit(OpCodes.Call, extractCn);
        il.Emit(OpCodes.Stfld, cnField);

        // --- SAN + basic constraints ---
        var listStringCtor = typeof(List<string>).GetConstructor(Type.EmptyTypes)!;
        var listStringAdd = typeof(List<string>).GetMethod("Add", [typeof(string)])!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, listStringCtor);
        il.Emit(OpCodes.Stfld, dnsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, listStringCtor);
        il.Emit(OpCodes.Stfld, ipsField);

        var partsLocal = il.DeclareLocal(typeof(List<string>));
        il.Emit(OpCodes.Newobj, listStringCtor);
        il.Emit(OpCodes.Stloc, partsLocal);

        // for (int i = 0; i < cert.Extensions.Count; i++)
        var extsLocal = il.DeclareLocal(typeof(X509ExtensionCollection));
        il.Emit(OpCodes.Ldloc, certLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, extsLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        var extLocal = il.DeclareLocal(typeof(X509Extension));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopCheck = il.DefineLabel();
        var loopBody = il.DefineLabel();
        var loopNext = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCheck);

        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldloc, extsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Item", [typeof(int)])!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, extLocal);

        // if (ext is X509BasicConstraintsExtension bc) _ca = bc.CertificateAuthority
        var notBcLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Isinst, typeof(X509BasicConstraintsExtension));
        il.Emit(OpCodes.Brfalse, notBcLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Castclass, typeof(X509BasicConstraintsExtension));
        il.Emit(OpCodes.Callvirt, typeof(X509BasicConstraintsExtension).GetProperty("CertificateAuthority")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, caField);
        il.MarkLabel(notBcLabel);

        // if (ext.Oid?.Value == "2.5.29.17") parse SAN via X509SubjectAlternativeNameExtension
        var notSanLabel = il.DefineLabel();
        var oidLocal = il.DeclareLocal(typeof(System.Security.Cryptography.Oid));
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("Oid")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, oidLocal);
        il.Emit(OpCodes.Ldloc, oidLocal);
        il.Emit(OpCodes.Brfalse, notSanLabel);
        il.Emit(OpCodes.Ldloc, oidLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Security.Cryptography.Oid).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "2.5.29.17");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, notSanLabel);

        // var sanExt = new X509SubjectAlternativeNameExtension(ext.RawData, false)
        var sanExtLocal = il.DeclareLocal(typeof(X509SubjectAlternativeNameExtension));
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnEncodedData).GetProperty("RawData")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(X509SubjectAlternativeNameExtension).GetConstructor([typeof(byte[]), typeof(bool)])!);
        il.Emit(OpCodes.Stloc, sanExtLocal);

        // var dnsList = sanExt.EnumerateDnsNames().ToList()
        var toListString = EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethod("ToList")!, typeof(string));
        var dnsListLocal = il.DeclareLocal(typeof(List<string>));
        il.Emit(OpCodes.Ldloc, sanExtLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509SubjectAlternativeNameExtension).GetMethod("EnumerateDnsNames", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, toListString);
        il.Emit(OpCodes.Stloc, dnsListLocal);

        // foreach dns: _dnsNames.Add(d); parts.Add("DNS:" + d)
        var jLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var dnsCheck = il.DefineLabel();
        var dnsBody = il.DefineLabel();
        il.Emit(OpCodes.Br, dnsCheck);
        il.MarkLabel(dnsBody);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, dnsField);
        il.Emit(OpCodes.Ldloc, dnsListLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listStringAdd);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldstr, "DNS:");
        il.Emit(OpCodes.Ldloc, dnsListLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, listStringAdd);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.MarkLabel(dnsCheck);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, dnsListLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, dnsBody);

        // var ipList = sanExt.EnumerateIPAddresses().ToList()
        var toListIp = EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethod("ToList")!, typeof(IPAddress));
        var ipListLocal = il.DeclareLocal(typeof(List<IPAddress>));
        il.Emit(OpCodes.Ldloc, sanExtLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509SubjectAlternativeNameExtension).GetMethod("EnumerateIPAddresses", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, toListIp);
        il.Emit(OpCodes.Stloc, ipListLocal);

        var ipStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var ipCheck = il.DefineLabel();
        var ipBody = il.DefineLabel();
        il.Emit(OpCodes.Br, ipCheck);
        il.MarkLabel(ipBody);
        il.Emit(OpCodes.Ldloc, ipListLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<IPAddress>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, ipStrLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ipsField);
        il.Emit(OpCodes.Ldloc, ipStrLocal);
        il.Emit(OpCodes.Callvirt, listStringAdd);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldstr, "IP Address:");
        il.Emit(OpCodes.Ldloc, ipStrLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Callvirt, listStringAdd);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.MarkLabel(ipCheck);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, ipListLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<IPAddress>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, ipBody);

        il.MarkLabel(notSanLabel);

        il.MarkLabel(loopNext);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopCheck);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, extsLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, loopBody);

        // _san = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : null
        var noSanLabel = il.DefineLabel();
        var sanDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, noSanLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", [_types.String, typeof(string[])])!);
        il.Emit(OpCodes.Stfld, sanField);
        il.Emit(OpCodes.Br, sanDoneLabel);
        il.MarkLabel(noSanLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, sanField);
        il.MarkLabel(sanDoneLabel);

        il.Emit(OpCodes.Ret);
        return ctor;
    }

    // ------------------------------------------------------------------
    // Static helpers
    // ------------------------------------------------------------------

    /// <summary>string FormatName(X500DistinguishedName): multi-line, cert order.</summary>
    private MethodBuilder EmitX509FormatName(TypeBuilder tb)
    {
        var method = tb.DefineMethod(
            "FormatName",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [typeof(X500DistinguishedName)]);
        var il = method.GetILGenerator();

        // var lines = dn.Format(true).Split(new[]{'\n'}, RemoveEmptyEntries)
        var linesLocal = il.DeclareLocal(typeof(string[]));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(X500DistinguishedName).GetMethod("Format", [typeof(bool)])!);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'\n');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Ldc_I4_1); // StringSplitOptions.RemoveEmptyEntries
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Split", [typeof(char[]), typeof(StringSplitOptions)])!);
        il.Emit(OpCodes.Stloc, linesLocal);

        // var result = new List<string>(); walk BACKWARD so lines come out in cert order
        var resultLocal = il.DeclareLocal(typeof(List<string>));
        il.Emit(OpCodes.Newobj, typeof(List<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        var tLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, linesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Br, check);

        il.MarkLabel(body);
        // t = lines[i].Trim(new[]{'\r',' '})
        il.Emit(OpCodes.Ldloc, linesLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'\r');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Trim", [typeof(char[])])!);
        il.Emit(OpCodes.Stloc, tLocal);

        // if (t.Length > 0) result.Add(t)
        il.Emit(OpCodes.Ldloc, tLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, tLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetMethod("Add", [typeof(string)])!);
        il.MarkLabel(skip);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, body);

        // return string.Join("\n", result.ToArray())
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", [_types.String, typeof(string[])])!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>string FormatValidity(DateTime): "Jan  1 00:00:00 2020 GMT" (OpenSSL style).</summary>
    private MethodBuilder EmitX509FormatValidity(TypeBuilder tb)
    {
        var method = tb.DefineMethod(
            "FormatValidity",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [typeof(DateTime)]);
        var il = method.GetILGenerator();

        var utcLocal = il.DeclareLocal(typeof(DateTime));
        il.Emit(OpCodes.Ldarga, 0);
        il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToUniversalTime", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, utcLocal);

        var invGetter = typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!;

        // string.Format(inv, "{0} {1,2} {2} GMT", MMM, (object)Day, "HH:mm:ss yyyy")
        il.Emit(OpCodes.Call, invGetter);
        il.Emit(OpCodes.Ldstr, "{0} {1,2} {2} GMT");

        il.Emit(OpCodes.Ldloca, utcLocal);
        il.Emit(OpCodes.Ldstr, "MMM");
        il.Emit(OpCodes.Call, invGetter);
        il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToString", [typeof(string), typeof(IFormatProvider)])!);

        il.Emit(OpCodes.Ldloca, utcLocal);
        il.Emit(OpCodes.Call, typeof(DateTime).GetProperty("Day")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.Int32);

        il.Emit(OpCodes.Ldloca, utcLocal);
        il.Emit(OpCodes.Ldstr, "HH:mm:ss yyyy");
        il.Emit(OpCodes.Call, invGetter);
        il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToString", [typeof(string), typeof(IFormatProvider)])!);

        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Format",
            [typeof(IFormatProvider), typeof(string), typeof(object), typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>string ColonHex(byte[]): "AB:0C:…" uppercase.</summary>
    private MethodBuilder EmitX509ColonHex(TypeBuilder tb)
    {
        var method = tb.DefineMethod(
            "ColonHex",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.ByteArray]);
        var il = method.GetILGenerator();

        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var noColon = il.DefineLabel();
        il.Emit(OpCodes.Br, check);

        il.MarkLabel(body);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Brfalse, noColon);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4, (int)':');
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(char)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noColon);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelema, typeof(byte));
        il.Emit(OpCodes.Ldstr, "X2");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(byte).GetMethod("ToString", [typeof(string), typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, body);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>bool HostMatches(string pattern, string name): leading-label wildcard match.</summary>
    private MethodBuilder EmitX509HostMatches(TypeBuilder tb)
    {
        var method = tb.DefineMethod(
            "HostMatches",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.String]);
        var il = method.GetILGenerator();

        // Normalize both: TrimEnd('.').ToLowerInvariant()
        var pLocal = il.DeclareLocal(_types.String);
        var nLocal = il.DeclareLocal(_types.String);

        void EmitNormalize(int argIndex, LocalBuilder target)
        {
            il.Emit(argIndex == 0 ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, typeof(char));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, (int)'.');
            il.Emit(OpCodes.Stelem_I2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "TrimEnd", [typeof(char[])])!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant", Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, target);
        }

        EmitNormalize(0, pLocal);
        EmitNormalize(1, nLocal);

        // if (p.IndexOf('*') < 0) return p == n
        var hasWildcard = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'*');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "IndexOf", [typeof(char)])!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, hasWildcard);
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasWildcard);

        // pl = p.Split('.'); nl = n.Split('.')
        var plLocal = il.DeclareLocal(typeof(string[]));
        var nlLocal = il.DeclareLocal(typeof(string[]));

        void EmitSplitDots(LocalBuilder src, LocalBuilder target)
        {
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, typeof(char));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4, (int)'.');
            il.Emit(OpCodes.Stelem_I2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Split", [typeof(char[])])!);
            il.Emit(OpCodes.Stloc, target);
        }

        EmitSplitDots(pLocal, plLocal);
        EmitSplitDots(nLocal, nlLocal);

        var returnFalse = il.DefineLabel();
        var returnTrue = il.DefineLabel();

        // if (pl.Length != nl.Length) return false
        il.Emit(OpCodes.Ldloc, plLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Ldloc, nlLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Bne_Un, returnFalse);

        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Br, check);

        il.MarkLabel(body);
        // if (pl[i] == "*")
        var notStarLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, plLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "*");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, notStarLabel);
        //   if (i != 0) return false
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Brtrue, returnFalse);
        //   if (nl[i].Length == 0) return false
        il.Emit(OpCodes.Ldloc, nlLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnFalse);
        il.Emit(OpCodes.Br, next);

        il.MarkLabel(notStarLabel);
        // if (pl[i] != nl[i]) return false
        il.Emit(OpCodes.Ldloc, plLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldloc, nlLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, returnFalse);

        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, plLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, body);

        il.MarkLabel(returnTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>string? ExtractCn(string subject): first "CN=" line's value.</summary>
    private MethodBuilder EmitX509ExtractCn(TypeBuilder tb)
    {
        var method = tb.DefineMethod(
            "ExtractCn",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]);
        var il = method.GetILGenerator();

        var linesLocal = il.DeclareLocal(typeof(string[]));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'\n');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Split", [typeof(char[])])!);
        il.Emit(OpCodes.Stloc, linesLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var next = il.DefineLabel();
        il.Emit(OpCodes.Br, check);

        il.MarkLabel(body);
        il.Emit(OpCodes.Ldloc, linesLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "CN=");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, linesLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, linesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, body);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// bool VerifyWithPem(byte[] certDer, string publicKeyPem): splits the signed
    /// certificate with AsnReader and verifies tbs against the signature using the
    /// given SPKI PEM key (RSA/PKCS1 first, EC/DER fallback).
    /// </summary>
    private MethodBuilder EmitX509VerifyWithPem(TypeBuilder tb)
    {
        var method = tb.DefineMethod(
            "VerifyWithPem",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.ByteArray, _types.String]);
        var il = method.GetILGenerator();

        var romLocal = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var optionsLocal = il.DeclareLocal(typeof(AsnReaderOptions));
        var nullTagLocal = il.DeclareLocal(typeof(Asn1Tag?));
        var readerLocal = il.DeclareLocal(typeof(AsnReader));
        var certSeqLocal = il.DeclareLocal(typeof(AsnReader));
        var tbsRomLocal = il.DeclareLocal(typeof(ReadOnlyMemory<byte>));
        var tbsLocal = il.DeclareLocal(_types.ByteArray);
        var oidLocal = il.DeclareLocal(_types.String);
        var sigLocal = il.DeclareLocal(_types.ByteArray);
        var unusedLocal = il.DeclareLocal(_types.Int32);
        var hashLocal = il.DeclareLocal(typeof(HashAlgorithmName));
        var resultLocal = il.DeclareLocal(_types.Boolean);

        // default(AsnReaderOptions), default(Asn1Tag?)
        il.Emit(OpCodes.Ldloca, optionsLocal);
        il.Emit(OpCodes.Initobj, typeof(AsnReaderOptions));
        il.Emit(OpCodes.Ldloca, nullTagLocal);
        il.Emit(OpCodes.Initobj, typeof(Asn1Tag?));

        // reader = new AsnReader((ReadOnlyMemory<byte>)certDer, AsnEncodingRules.DER, options)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Stloc, romLocal);
        il.Emit(OpCodes.Ldloc, romLocal);
        il.Emit(OpCodes.Ldc_I4_2); // AsnEncodingRules.DER == 2
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Newobj, typeof(AsnReader).GetConstructor([typeof(ReadOnlyMemory<byte>), typeof(AsnEncodingRules), typeof(AsnReaderOptions)])!);
        il.Emit(OpCodes.Stloc, readerLocal);

        // certSeq = reader.ReadSequence(null)
        il.Emit(OpCodes.Ldloc, readerLocal);
        il.Emit(OpCodes.Ldloc, nullTagLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, certSeqLocal);

        // tbs = certSeq.ReadEncodedValue().ToArray()
        il.Emit(OpCodes.Ldloc, certSeqLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadEncodedValue", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tbsRomLocal);
        il.Emit(OpCodes.Ldloca, tbsRomLocal);
        il.Emit(OpCodes.Call, typeof(ReadOnlyMemory<byte>).GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tbsLocal);

        // oid = certSeq.ReadSequence(null).ReadObjectIdentifier(null)
        il.Emit(OpCodes.Ldloc, certSeqLocal);
        il.Emit(OpCodes.Ldloc, nullTagLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadSequence", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Ldloc, nullTagLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadObjectIdentifier", [typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, oidLocal);

        // sig = certSeq.ReadBitString(out unused, null)
        il.Emit(OpCodes.Ldloc, certSeqLocal);
        il.Emit(OpCodes.Ldloca, unusedLocal);
        il.Emit(OpCodes.Ldloc, nullTagLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsnReader).GetMethod("ReadBitString", [typeof(int).MakeByRefType(), typeof(Asn1Tag?)])!);
        il.Emit(OpCodes.Stloc, sigLocal);

        // hash = OID switch
        var sha1Label = il.DefineLabel();
        var sha256Label = il.DefineLabel();
        var sha384Label = il.DefineLabel();
        var sha512Label = il.DefineLabel();
        var haveHashLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        void EmitOidCheck(string oid, Label target)
        {
            il.Emit(OpCodes.Ldloc, oidLocal);
            il.Emit(OpCodes.Ldstr, oid);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, target);
        }

        EmitOidCheck("1.2.840.113549.1.1.11", sha256Label);
        EmitOidCheck("1.2.840.10045.4.3.2", sha256Label);
        EmitOidCheck("1.2.840.113549.1.1.12", sha384Label);
        EmitOidCheck("1.2.840.10045.4.3.3", sha384Label);
        EmitOidCheck("1.2.840.113549.1.1.13", sha512Label);
        EmitOidCheck("1.2.840.10045.4.3.4", sha512Label);
        EmitOidCheck("1.2.840.113549.1.1.5", sha1Label);
        EmitOidCheck("1.2.840.10045.4.1", sha1Label);
        il.Emit(OpCodes.Br, throwLabel);

        void EmitHashCase(Label label, string prop)
        {
            il.MarkLabel(label);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, prop)!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, hashLocal);
            il.Emit(OpCodes.Br, haveHashLabel);
        }

        EmitHashCase(sha1Label, "SHA1");
        EmitHashCase(sha256Label, "SHA256");
        EmitHashCase(sha384Label, "SHA384");
        EmitHashCase(sha512Label, "SHA512");

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported certificate signature algorithm OID ");
        il.Emit(OpCodes.Ldloc, oidLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(haveHashLabel);

        // try RSA, on CryptographicException fall back to ECDsa
        var exitLabel = il.DefineLabel();
        var ecLabel = il.DefineLabel();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldloc, tbsLocal);
        il.Emit(OpCodes.Ldloc, sigLocal);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Call, typeof(RSASignaturePadding).GetProperty("Pkcs1")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("VerifyData", [typeof(byte[]), typeof(byte[]), typeof(HashAlgorithmName), typeof(RSASignaturePadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Leave, exitLabel);

        il.BeginCatchBlock(typeof(CryptographicException));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        var rsaNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, rsaNullLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.MarkLabel(rsaNullLabel);
        il.Emit(OpCodes.Leave, ecLabel);
        il.EndExceptionBlock();

        // EC path
        il.MarkLabel(ecLabel);
        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        il.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldloc, tbsLocal);
        il.Emit(OpCodes.Ldloc, sigLocal);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Ldc_I4_1); // DSASignatureFormat.Rfc3279DerSequence
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("VerifyData", [typeof(byte[]), typeof(byte[]), typeof(HashAlgorithmName), typeof(DSASignatureFormat)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        return method;
    }

    // ------------------------------------------------------------------
    // Property getters
    // ------------------------------------------------------------------

    private void EmitX509FieldGetter(TypeBuilder tb, string propName, string getterName, FieldBuilder field)
    {
        var prop = tb.DefineProperty(propName, PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod(getterName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509CaGetter(TypeBuilder tb, FieldBuilder caField)
    {
        var prop = tb.DefineProperty("ca", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_Ca",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, caField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509SerialGetter(TypeBuilder tb, FieldBuilder certField)
    {
        var prop = tb.DefineProperty("serialNumber", PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_SerialNumber",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("SerialNumber")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509ValidityGetter(TypeBuilder tb, string propName, string getterName,
        FieldBuilder certField, MethodBuilder formatValidity, bool notBefore)
    {
        var prop = tb.DefineProperty(propName, PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = tb.DefineMethod(getterName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty(notBefore ? "NotBefore" : "NotAfter")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, formatValidity);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509ValidityDateGetter(TypeBuilder tb, EmittedRuntime runtime,
        string propName, string getterName, FieldBuilder certField, bool notBefore)
    {
        var prop = tb.DefineProperty(propName, PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod(getterName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();

        if (runtime.TSDateCtorMilliseconds == null)
        {
            il.Emit(OpCodes.Ldstr, $"X509Certificate.{propName} requires Date support in the compiled program");
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.InvalidOperationException, [_types.String])!);
            il.Emit(OpCodes.Throw);
        }
        else
        {
            var dtLocal = il.DeclareLocal(typeof(DateTime));
            var dtoLocal = il.DeclareLocal(typeof(DateTimeOffset));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, certField);
            il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty(notBefore ? "NotBefore" : "NotAfter")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, dtLocal);
            il.Emit(OpCodes.Ldloca, dtLocal);
            il.Emit(OpCodes.Call, typeof(DateTime).GetMethod("ToUniversalTime", Type.EmptyTypes)!);
            il.Emit(OpCodes.Newobj, typeof(DateTimeOffset).GetConstructor([typeof(DateTime)])!);
            il.Emit(OpCodes.Stloc, dtoLocal);
            il.Emit(OpCodes.Ldloca, dtoLocal);
            il.Emit(OpCodes.Call, typeof(DateTimeOffset).GetMethod("ToUnixTimeMilliseconds", Type.EmptyTypes)!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Newobj, runtime.TSDateCtorMilliseconds);
        }
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509FingerprintGetter(TypeBuilder tb, string propName, string getterName,
        FieldBuilder certField, MethodBuilder colonHex, string hashProp)
    {
        var prop = tb.DefineProperty(propName, PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = tb.DefineMethod(getterName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, hashProp)!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetMethod("GetCertHash", [typeof(HashAlgorithmName)])!);
        il.Emit(OpCodes.Call, colonHex);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509RawGetter(TypeBuilder tb, EmittedRuntime runtime, FieldBuilder certField)
    {
        var prop = tb.DefineProperty("raw", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_Raw",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("RawData")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    /// <summary>Emits: string SpkiPem(X509Certificate2) — used by publicKey and checkIssued.</summary>
    private void EmitX509PublicKeyGetter(TypeBuilder tb, EmittedRuntime runtime, FieldBuilder certField)
    {
        // helper: static string SpkiPem(X509Certificate2)
        var spkiPem = tb.DefineMethod("SpkiPem",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.String, [typeof(X509Certificate2)]);
        {
            var il = spkiPem.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "PUBLIC KEY");
            il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("PublicKey")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, typeof(System.Security.Cryptography.X509Certificates.PublicKey).GetMethod("ExportSubjectPublicKeyInfo", Type.EmptyTypes)!);
            il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
            il.Emit(OpCodes.Call, typeof(PemEncoding).GetMethod("WriteString", [typeof(ReadOnlySpan<char>), typeof(ReadOnlySpan<byte>)])!);
            il.Emit(OpCodes.Ret);
        }
        _x509SpkiPemHelper = spkiPem;

        var prop = tb.DefineProperty("publicKey", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_PublicKey",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var gil = getter.GetILGenerator();
        gil.Emit(OpCodes.Ldarg_0);
        gil.Emit(OpCodes.Ldfld, certField);
        gil.Emit(OpCodes.Call, spkiPem);
        gil.Emit(OpCodes.Ldc_I4_0); // isPrivate: false
        gil.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorAsym);
        gil.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private MethodBuilder? _x509SpkiPemHelper;

    private void EmitX509KeyUsageGetter(TypeBuilder tb, EmittedRuntime runtime, FieldBuilder certField)
    {
        var prop = tb.DefineProperty("keyUsage", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_KeyUsage",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();

        var extsLocal = il.DeclareLocal(typeof(X509ExtensionCollection));
        var iLocal = il.DeclareLocal(_types.Int32);
        var kuLocal = il.DeclareLocal(typeof(X509KeyUsageExtension));
        var flagsLocal = il.DeclareLocal(_types.Int32);
        var listLocal = il.DeclareLocal(_types.ListOfObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, extsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var next = il.DefineLabel();
        var found = il.DefineLabel();
        il.Emit(OpCodes.Br, check);

        il.MarkLabel(body);
        il.Emit(OpCodes.Ldloc, extsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Item", [typeof(int)])!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(X509KeyUsageExtension));
        il.Emit(OpCodes.Stloc, kuLocal);
        il.Emit(OpCodes.Ldloc, kuLocal);
        il.Emit(OpCodes.Brtrue, found);

        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, extsLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, body);

        // not found → undefined (null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(found);
        il.Emit(OpCodes.Ldloc, kuLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509KeyUsageExtension).GetProperty("KeyUsages")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, flagsLocal);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, listLocal);

        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!;
        void EmitFlagCheck(X509KeyUsageFlags flag, string name)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, flagsLocal);
            il.Emit(OpCodes.Ldc_I4, (int)flag);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Callvirt, listAdd);
            il.MarkLabel(skipLabel);
        }

        EmitFlagCheck(X509KeyUsageFlags.DigitalSignature, "Digital Signature");
        EmitFlagCheck(X509KeyUsageFlags.NonRepudiation, "Non Repudiation");
        EmitFlagCheck(X509KeyUsageFlags.KeyEncipherment, "Key Encipherment");
        EmitFlagCheck(X509KeyUsageFlags.DataEncipherment, "Data Encipherment");
        EmitFlagCheck(X509KeyUsageFlags.KeyAgreement, "Key Agreement");
        EmitFlagCheck(X509KeyUsageFlags.KeyCertSign, "Certificate Sign");
        EmitFlagCheck(X509KeyUsageFlags.CrlSign, "CRL Sign");
        EmitFlagCheck(X509KeyUsageFlags.EncipherOnly, "Encipher Only");
        EmitFlagCheck(X509KeyUsageFlags.DecipherOnly, "Decipher Only");

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitX509ExtKeyUsageGetter(TypeBuilder tb, EmittedRuntime runtime, FieldBuilder certField)
    {
        var prop = tb.DefineProperty("extKeyUsage", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_ExtKeyUsage",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();

        var extsLocal = il.DeclareLocal(typeof(X509ExtensionCollection));
        var iLocal = il.DeclareLocal(_types.Int32);
        var ekuLocal = il.DeclareLocal(typeof(X509EnhancedKeyUsageExtension));
        var oidsLocal = il.DeclareLocal(typeof(OidCollection));
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var jLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, extsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var found = il.DefineLabel();
        il.Emit(OpCodes.Br, check);

        il.MarkLabel(body);
        il.Emit(OpCodes.Ldloc, extsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Item", [typeof(int)])!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(X509EnhancedKeyUsageExtension));
        il.Emit(OpCodes.Stloc, ekuLocal);
        il.Emit(OpCodes.Ldloc, ekuLocal);
        il.Emit(OpCodes.Brtrue, found);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, extsLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509ExtensionCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, body);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(found);
        il.Emit(OpCodes.Ldloc, ekuLocal);
        il.Emit(OpCodes.Callvirt, typeof(X509EnhancedKeyUsageExtension).GetProperty("EnhancedKeyUsages")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, oidsLocal);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var jCheck = il.DefineLabel();
        var jBody = il.DefineLabel();
        il.Emit(OpCodes.Br, jCheck);
        il.MarkLabel(jBody);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, oidsLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, typeof(OidCollection).GetProperty("Item", [typeof(int)])!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(System.Security.Cryptography.Oid).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.MarkLabel(jCheck);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, oidsLocal);
        il.Emit(OpCodes.Callvirt, typeof(OidCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, jBody);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    /// <summary>infoAccess → undefined (compiled deviation; the interp renders AIA).</summary>
    private void EmitX509InfoAccessGetter(TypeBuilder tb)
    {
        var prop = tb.DefineProperty("infoAccess", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = tb.DefineMethod("get_InfoAccess",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    // ------------------------------------------------------------------
    // Methods
    // ------------------------------------------------------------------

    private void EmitX509Verify(TypeBuilder tb, FieldBuilder certField, MethodBuilder verifyWithPem)
    {
        var method = tb.DefineMethod("Verify",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        // pem = (string)key.GetType().GetMethod("export").Invoke(key, new object[]{ null })
        var pemLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "export");
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string)])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Stloc, pemLocal);

        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pemLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.Emit(OpCodes.Ldstr, "X509Certificate.verify requires a public KeyObject argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("RawData")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, pemLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, verifyWithPem);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitX509CheckHost(TypeBuilder tb, EmittedRuntime runtime, FieldBuilder dnsField, FieldBuilder cnField, MethodBuilder hostMatches)
    {
        var method = tb.DefineMethod("CheckHost",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var nameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, nameLocal);

        var returnNull = il.DefineLabel();
        var returnName = il.DefineLabel();
        var cnFallback = il.DefineLabel();

        // if (_dnsNames.Count == 0) goto cnFallback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, dnsField);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, cnFallback);

        // loop dns names
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var check = il.DefineLabel();
        var body = il.DefineLabel();
        il.Emit(OpCodes.Br, check);
        il.MarkLabel(body);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, dnsField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, hostMatches);
        il.Emit(OpCodes.Brtrue, returnName);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, dnsField);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, body);
        il.Emit(OpCodes.Br, returnNull);

        // CN fallback
        il.MarkLabel(cnFallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, cnField);
        il.Emit(OpCodes.Brfalse, returnNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, cnField);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Call, hostMatches);
        il.Emit(OpCodes.Brtrue, returnName);
        il.Emit(OpCodes.Br, returnNull);

        il.MarkLabel(returnName);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(returnNull);
        // Node returns undefined (not null) on a failed match
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitX509CheckIp(TypeBuilder tb, EmittedRuntime runtime, FieldBuilder ipsField)
    {
        var method = tb.DefineMethod("CheckIP",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var returnNull = il.DefineLabel();
        var ipLocal = il.DeclareLocal(typeof(IPAddress));
        var normLocal = il.DeclareLocal(_types.String);

        // if (!IPAddress.TryParse(arg.ToString(), out ip)) return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldloca, ipLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [typeof(string), typeof(IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, returnNull);

        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, normLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var check = il.DefineLabel();
        var body = il.DefineLabel();
        var returnNorm = il.DefineLabel();
        il.Emit(OpCodes.Br, check);
        il.MarkLabel(body);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ipsField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, normLocal);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, returnNorm);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(check);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ipsField);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, body);
        il.Emit(OpCodes.Br, returnNull);

        il.MarkLabel(returnNorm);
        il.Emit(OpCodes.Ldloc, normLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(returnNull);
        // Node returns undefined (not null) on a failed match
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitX509CheckIssued(TypeBuilder tb, FieldBuilder certField,
        FieldBuilder subjectField, FieldBuilder issuerField, MethodBuilder verifyWithPem)
    {
        var method = tb.DefineMethod("CheckIssued",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object]);
        var il = method.GetILGenerator();

        var returnFalse = il.DefineLabel();
        var otherLocal = il.DeclareLocal(tb);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, tb);
        il.Emit(OpCodes.Stloc, otherLocal);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Brfalse, returnFalse);

        // if (_issuer != other._subject) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, issuerField);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Ldfld, subjectField);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, returnFalse);

        // return VerifyWithPem(_cert.RawData, SpkiPem(other._cert))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("RawData")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Call, _x509SpkiPemHelper!);
        il.Emit(OpCodes.Call, verifyWithPem);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitX509ToString(TypeBuilder tb, FieldBuilder certField)
    {
        // public override string ToString() → PEM + "\n"
        var toString = tb.DefineMethod("ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        var il = toString.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, certField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetMethod("ExportCertificatePem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(toString, _types.GetMethodNoParams(_types.Object, "ToString"));

        // toJSON → same PEM
        var toJson = tb.DefineMethod("ToJSON",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        var jil = toJson.GetILGenerator();
        jil.Emit(OpCodes.Ldarg_0);
        jil.Emit(OpCodes.Callvirt, toString);
        jil.Emit(OpCodes.Ret);
    }

    private void EmitX509NotSupported(TypeBuilder tb, string methodName, string message)
    {
        var method = tb.DefineMethod(methodName,
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.InvalidOperationException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }
}
