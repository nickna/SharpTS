using System.Net.Security;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $TlsSocket and $TlsServer classes in pure IL for the TLS module.
///
/// $TlsSocket extends $NetSocket (mirroring interp <see cref="SharpTS.Runtime.Types.SharpTSTlsSocket"/>
/// : SharpTSSocket), so it inherits the real socket I/O — write/end/destroy/read-pump and the
/// data/end/close lifecycle — operating over the negotiated <see cref="System.Net.Security.SslStream"/>
/// stored in the base <c>_stream</c> field. TLS-specific introspection (getCipher/getProtocol/
/// getPeerCertificate/authorized) reads the retained SslStream. All handshake + introspection is
/// emitted as pure-BCL IL (SslStream is BCL), so a --compile'd tls program is genuinely standalone —
/// no SharpTS.dll dependency, no <c>RequireSharpTSRuntime</c>.
/// </summary>
/// <remarks>
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSTlsSocket
/// and SharpTS.Runtime.Types.SharpTSTlsServer
/// </remarks>
public partial class RuntimeEmitter
{
    // ---- $TlsSocket field builders ----
    private TypeBuilder _tlsSocketTypeBuilder = null!;
    private FieldBuilder _tlsSocketSslStreamField = null!;      // SslStream (negotiated; also stored in base _stream)
    private FieldBuilder _tlsSocketAuthorizedField = null!;     // bool
    private FieldBuilder _tlsSocketAuthErrorField = null!;      // object (string message or null)
    private FieldBuilder _tlsSocketAlpnProtocolField = null!;   // object (string or null)
    private FieldBuilder _tlsSocketServernameField = null!;     // object (string or null)
    private FieldBuilder _tlsSocketPeerCertField = null!;       // X509Certificate2 (peer cert)
    private MethodBuilder _tlsProtoStringMethod = null!;        // static string _ProtoString(SslProtocols)
    private MethodBuilder _tlsBuildAlpnListMethod = null!;      // static List<SslApplicationProtocol> _BuildAlpnList(string[])
    private MethodBuilder _tlsAlpnStringMethod = null!;         // static string _AlpnString(SslStream)
    private MethodBuilder _tlsLoadCertMethod = null!;           // static X509Certificate2 _LoadCert(string cert, string key)
    private MethodBuilder _tlsSanStringMethod = null!;          // static string _SanString(X509Certificate2)
    internal MethodBuilder _tlsDescribeErrMethod = null!;       // static string _DescribePolicyErrors(SslPolicyErrors)

    // ---- $TlsServer field builders ----
    private TypeBuilder _tlsServerTypeBuilder = null!;
    private FieldBuilder _tlsServerIsListeningField = null!;
    private FieldBuilder _tlsServerCallbackField = null!;
    private FieldBuilder _tlsServerCertField = null!;       // string (PEM cert)
    private FieldBuilder _tlsServerKeyField = null!;        // string (PEM key)
    private FieldBuilder _tlsServerRequestCertField = null!; // bool (requestCert)
    private FieldBuilder _tlsServerListenerField = null!;   // TcpListener
    private FieldBuilder _tlsServerPortField = null!;       // int
    private FieldBuilder _tlsServerAlpnField = null!;       // string[] (ALPN protocol names)
    private MethodBuilder _tlsServerAcceptWorkerMethod = null!;

    /// <summary>
    /// Phase 1: emits $TlsSocket and $TlsServer types (fields, constructors, method bodies)
    /// but defers CreateType() to <see cref="EmitTlsSocketFinalize"/> / <see cref="EmitTlsServerFinalize"/>.
    /// Must be called after $NetSocket Phase 1 ($TlsSocket extends it) and $EventEmitter.
    /// </summary>
    private void EmitTlsTypesPhase1(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitTlsSocketClass(moduleBuilder, runtime);
        EmitTlsServerClass(moduleBuilder, runtime);
    }

    /// <summary>
    /// Phase 2: finalize $TlsSocket. Must come after $NetSocket.CreateType() (base) and after
    /// the connect closure that populates $TlsSocket fields has been defined.
    /// </summary>
    private void EmitTlsSocketFinalize(EmittedRuntime runtime)
    {
        _tlsSocketTypeBuilder.CreateType();
    }

    // ========================================================================
    // $TlsSocket : $NetSocket
    // ========================================================================

    private void EmitTlsSocketClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TlsSocket",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.NetSocketType  // extends $NetSocket — inherits real socket I/O over the SslStream
        );
        _tlsSocketTypeBuilder = typeBuilder;
        runtime.TlsSocketType = typeBuilder;

        // Fields (Assembly so the connect/accept workers can populate them)
        _tlsSocketSslStreamField = typeBuilder.DefineField("_sslStream", typeof(SslStream), FieldAttributes.Assembly);
        _tlsSocketAuthorizedField = typeBuilder.DefineField("_authorized", _types.Boolean, FieldAttributes.Assembly);
        _tlsSocketAuthErrorField = typeBuilder.DefineField("_authError", _types.Object, FieldAttributes.Assembly);
        _tlsSocketAlpnProtocolField = typeBuilder.DefineField("_alpnProtocol", _types.Object, FieldAttributes.Assembly);
        _tlsSocketServernameField = typeBuilder.DefineField("_servername", _types.Object, FieldAttributes.Assembly);
        _tlsSocketPeerCertField = typeBuilder.DefineField("_peerCert", typeof(X509Certificate2), FieldAttributes.Assembly);

        // Constructor: public $TlsSocket() : base()  (calls $NetSocket default ctor)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TlsSocketCtor = ctor;
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, runtime.NetSocketCtor);
        ctorIL.Emit(OpCodes.Ret);

        // Static helpers
        EmitTlsProtoStringHelper(typeBuilder);
        EmitTlsBuildAlpnListHelper(typeBuilder);
        EmitTlsAlpnStringHelper(typeBuilder);
        EmitTlsLoadCertHelper(typeBuilder);
        EmitTlsSanStringHelper(typeBuilder);
        EmitTlsDescribePolicyErrorsHelper(typeBuilder);

        // TLS-specific methods
        EmitTlsSocketGetCipher(typeBuilder);
        EmitTlsSocketGetProtocol(typeBuilder);
        EmitTlsSocketGetPeerCertificate(typeBuilder);
        EmitTlsSocketRenegotiate(typeBuilder);
        // Advanced TLS APIs not exposed by .NET SslStream — throw a clear error (not a silent
        // no-op), matching interp SharpTSTlsSocket. See the #1032 "Known SslStream ceilings".
        foreach (var m in new[] { "GetSession", "SetSession", "GetTLSTicket", "GetPeerFinished",
                                  "GetFinished", "SetMaxSendFragment", "ExportKeyingMaterial" })
            EmitTlsSocketUnsupported(typeBuilder, runtime, m);
        EmitTlsSocketGetMember(typeBuilder, runtime);

        // NOTE: CreateType() deferred to EmitTlsSocketFinalize (Phase 2).
    }

    /// <summary>
    /// Emits: private static string _ProtoString(SslProtocols p)
    /// Maps the negotiated protocol to its Node string ("TLSv1.3"/"TLSv1.2"), else p.ToString().
    /// </summary>
    private void EmitTlsProtoStringHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_ProtoString",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.String,
            [typeof(SslProtocols)]
        );
        _tlsProtoStringMethod = method;
        var il = method.GetILGenerator();

        var notTls13 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)SslProtocols.Tls13);
        il.Emit(OpCodes.Bne_Un, notTls13);
        il.Emit(OpCodes.Ldstr, "TLSv1.3");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTls13);

        var notTls12 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)SslProtocols.Tls12);
        il.Emit(OpCodes.Bne_Un, notTls12);
        il.Emit(OpCodes.Ldstr, "TLSv1.2");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTls12);

        // default: ((object)p).ToString()
        var argLocal = il.DeclareLocal(typeof(SslProtocols));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, argLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Box, typeof(SslProtocols));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static List&lt;SslApplicationProtocol&gt; _BuildAlpnList(string[] names)
    /// </summary>
    private void EmitTlsBuildAlpnListHelper(TypeBuilder typeBuilder)
    {
        var listType = typeof(List<SslApplicationProtocol>);
        var method = typeBuilder.DefineMethod(
            "_BuildAlpnList",
            MethodAttributes.Assembly | MethodAttributes.Static,
            listType,
            [typeof(string[])]
        );
        _tlsBuildAlpnListMethod = method;
        var il = method.GetILGenerator();

        var listLocal = il.DeclareLocal(listType);
        var iLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loopTop = il.DefineLabel();
        var loopCheck = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCheck);

        il.MarkLabel(loopTop);
        // list.Add(new SslApplicationProtocol(names[i]))
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Newobj, typeof(SslApplicationProtocol).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCheck);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loopTop);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static string _AlpnString(SslStream s)
    /// Returns the negotiated ALPN protocol name, or null if none was negotiated.
    /// </summary>
    private void EmitTlsAlpnStringHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_AlpnString",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.String,
            [typeof(SslStream)]
        );
        _tlsAlpnStringMethod = method;
        var il = method.GetILGenerator();

        // var p = s.NegotiatedApplicationProtocol; var str = p.ToString();
        var protoLocal = il.DeclareLocal(typeof(SslApplicationProtocol));
        var strLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetProperty("NegotiatedApplicationProtocol")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, protoLocal);
        il.Emit(OpCodes.Ldloca, protoLocal);
        il.Emit(OpCodes.Constrained, typeof(SslApplicationProtocol));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);

        // if (string.IsNullOrEmpty(str)) return null; else return str;
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brfalse, notEmpty);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notEmpty);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static X509Certificate2 _LoadCert(string certPem, string keyPem)
    /// Parses the PEM cert+key, round-tripping through PKCS#12 for SslStream compatibility on Windows.
    /// </summary>
    private void EmitTlsLoadCertHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_LoadCert",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(X509Certificate2),
            [_types.String, _types.String]
        );
        _tlsLoadCertMethod = method;
        var il = method.GetILGenerator();

        var asSpan = typeof(MemoryExtensions).GetMethod("AsSpan", [_types.String])!;
        var createFromPem = typeof(X509Certificate2).GetMethod("CreateFromPem",
            [typeof(ReadOnlySpan<char>), typeof(ReadOnlySpan<char>)])!;
        var exportMethod = typeof(X509Certificate2).GetMethod("Export", [typeof(X509ContentType)])!;
        // X509CertificateLoader.LoadPkcs12(byte[], string?) carries trailing optional params
        // (keyStorageFlags, loaderLimits) on .NET 10, so an exact 2-arg lookup misses it.
        // Resolve the byte[]/string overload and supply the optional-arg defaults in IL.
        var loadPkcs12 = typeof(X509CertificateLoader).GetMethods()
            .First(m => m.Name == "LoadPkcs12"
                && m.GetParameters() is var p
                && p.Length >= 2
                && p[0].ParameterType == typeof(byte[])
                && p[1].ParameterType == _types.String);

        // var cert = X509Certificate2.CreateFromPem(certPem.AsSpan(), keyPem.AsSpan());
        var certLocal = il.DeclareLocal(typeof(X509Certificate2));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, asSpan);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, asSpan);
        il.Emit(OpCodes.Call, createFromPem);
        il.Emit(OpCodes.Stloc, certLocal);

        // return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null[, defaults...]);
        il.Emit(OpCodes.Ldloc, certLocal);
        il.Emit(OpCodes.Ldc_I4, (int)X509ContentType.Pfx);
        il.Emit(OpCodes.Callvirt, exportMethod);
        il.Emit(OpCodes.Ldnull); // password
        // Supply remaining optional params' defaults (X509KeyStorageFlags=0, loaderLimits=null, …).
        var loadParams = loadPkcs12.GetParameters();
        for (int i = 2; i < loadParams.Length; i++)
        {
            var pt = loadParams[i].ParameterType;
            if (pt.IsValueType)
                il.Emit(OpCodes.Ldc_I4_0); // enums (X509KeyStorageFlags) — DefaultKeySet == 0
            else
                il.Emit(OpCodes.Ldnull);   // reference types (Pkcs12LoaderLimits?)
        }
        il.Emit(OpCodes.Call, loadPkcs12);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: internal static string _DescribePolicyErrors(SslPolicyErrors errors)
    /// Mirrors interp SharpTSTlsSocket.DescribePolicyErrors.
    /// </summary>
    private void EmitTlsDescribePolicyErrorsHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "_DescribePolicyErrors",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.String,
            [typeof(SslPolicyErrors)]
        );
        _tlsDescribeErrMethod = method;
        var il = method.GetILGenerator();

        // if ((errors & ChainErrors) != 0) return "self-signed certificate"
        var notChain = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)SslPolicyErrors.RemoteCertificateChainErrors);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, notChain);
        il.Emit(OpCodes.Ldstr, "self-signed certificate");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notChain);

        // if ((errors & NameMismatch) != 0) return "Hostname/IP does not match certificate's altnames"
        var notName = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)SslPolicyErrors.RemoteCertificateNameMismatch);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, notName);
        il.Emit(OpCodes.Ldstr, "Hostname/IP does not match certificate's altnames");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notName);

        // if ((errors & NotAvailable) != 0) return "no certificate provided"
        var notAvail = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)SslPolicyErrors.RemoteCertificateNotAvailable);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, notAvail);
        il.Emit(OpCodes.Ldstr, "no certificate provided");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notAvail);

        // default: errors.ToString()
        var eLocal = il.DeclareLocal(typeof(SslPolicyErrors));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, eLocal);
        il.Emit(OpCodes.Ldloca, eLocal);
        il.Emit(OpCodes.Constrained, typeof(SslPolicyErrors));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: internal static string _SanString(X509Certificate2 cert)
    /// Formats the Subject Alternative Name extension as "DNS:host, IP Address:1.2.3.4" (Node style),
    /// or null when absent. Mirrors interp SharpTSTlsSocket.SubjectAltName.
    /// </summary>
    private void EmitTlsSanStringHelper(TypeBuilder typeBuilder)
    {
        var sanExtType = typeof(X509SubjectAlternativeNameExtension);
        var method = typeBuilder.DefineMethod(
            "_SanString",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.String,
            [typeof(X509Certificate2)]
        );
        _tlsSanStringMethod = method;
        var il = method.GetILGenerator();

        var listType = typeof(List<string>);
        var listAdd = listType.GetMethod("Add")!;
        var listCount = listType.GetProperty("Count")!.GetGetMethod()!;
        var concat2 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!;
        var joinEnum = _types.GetMethod(_types.String, "Join", [_types.String, typeof(IEnumerable<string>)])!;

        // ext = cert.Extensions["2.5.29.17"]; if (ext == null) return null;
        var extLocal = il.DeclareLocal(typeof(System.Security.Cryptography.X509Certificates.X509Extension));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty("Extensions")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "2.5.29.17");
        il.Emit(OpCodes.Callvirt, typeof(System.Security.Cryptography.X509Certificates.X509ExtensionCollection).GetMethod("get_Item", [_types.String])!);
        il.Emit(OpCodes.Stloc, extLocal);
        var retNull = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Brfalse, retNull);

        // san = ext as X509SubjectAlternativeNameExtension; if (san == null) return null;
        var sanLocal = il.DeclareLocal(sanExtType);
        il.Emit(OpCodes.Ldloc, extLocal);
        il.Emit(OpCodes.Isinst, sanExtType);
        il.Emit(OpCodes.Stloc, sanLocal);
        il.Emit(OpCodes.Ldloc, sanLocal);
        il.Emit(OpCodes.Brfalse, retNull);

        // parts = new List<string>()
        var partsLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, partsLocal);

        // foreach (var dns in san.EnumerateDnsNames()) parts.Add("DNS:" + dns);
        EmitTlsSanForeach(il, sanLocal, partsLocal, listAdd, concat2, "DNS:",
            sanExtType.GetMethod("EnumerateDnsNames", Type.EmptyTypes)!,
            typeof(IEnumerable<string>), typeof(IEnumerator<string>), needsToString: false);

        // foreach (var ip in san.EnumerateIPAddresses()) parts.Add("IP Address:" + ip.ToString());
        EmitTlsSanForeach(il, sanLocal, partsLocal, listAdd, concat2, "IP Address:",
            sanExtType.GetMethod("EnumerateIPAddresses", Type.EmptyTypes)!,
            typeof(IEnumerable<System.Net.IPAddress>), typeof(IEnumerator<System.Net.IPAddress>),
            needsToString: true);

        // return parts.Count > 0 ? string.Join(", ", parts) : null;
        var someParts = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Brtrue, someParts);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(someParts);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Call, joinEnum);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(retNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a foreach over <paramref name="enumerateMethod"/> that appends prefix+item (item.ToString()
    /// when <paramref name="needsToString"/>) to the parts list.
    /// </summary>
    private void EmitTlsSanForeach(ILGenerator il, LocalBuilder sanLocal, LocalBuilder partsLocal, MethodInfo listAdd,
        MethodInfo concat2, string prefix, MethodInfo enumerateMethod, Type enumerableType, Type enumeratorType, bool needsToString)
    {
        var getEnumerator = enumerableType.GetMethod("GetEnumerator", Type.EmptyTypes)!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var getCurrent = enumeratorType.GetProperty("Current")!.GetGetMethod()!;

        var enumLocal = il.DeclareLocal(enumeratorType);
        il.Emit(OpCodes.Ldloc, sanLocal);
        il.Emit(OpCodes.Callvirt, enumerateMethod);
        il.Emit(OpCodes.Callvirt, getEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        var top = il.DefineLabel();
        var chk = il.DefineLabel();
        il.Emit(OpCodes.Br, chk);
        il.MarkLabel(top);
        // parts.Add(prefix + current[.ToString()])
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldstr, prefix);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, getCurrent);
        if (needsToString)
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, concat2);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.MarkLabel(chk);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, moveNext);
        il.Emit(OpCodes.Brtrue, top);
    }

    /// <summary>
    /// Emits: public object? GetCipher()
    /// Reads the negotiated cipher suite + protocol from the retained SslStream.
    /// </summary>
    private void EmitTlsSocketGetCipher(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod("GetCipher", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var setItem = _types.GetMethod(dictType, "set_Item", _types.String, _types.Object);

        // if (_sslStream == null) return null
        var hasStream = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketSslStreamField);
        il.Emit(OpCodes.Brtrue, hasStream);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasStream);

        // cipherName = _sslStream.NegotiatedCipherSuite.ToString()
        var cipherLocal = il.DeclareLocal(_types.String);
        var suiteLocal = il.DeclareLocal(typeof(System.Net.Security.TlsCipherSuite));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketSslStreamField);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetProperty("NegotiatedCipherSuite")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, suiteLocal);
        il.Emit(OpCodes.Ldloca, suiteLocal);
        il.Emit(OpCodes.Constrained, typeof(System.Net.Security.TlsCipherSuite));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, cipherLocal);

        // version = _ProtoString(_sslStream.SslProtocol)
        var versionLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketSslStreamField);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetProperty("SslProtocol")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _tlsProtoStringMethod);
        il.Emit(OpCodes.Stloc, versionLocal);

        // dict = { name, standardName, version }
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Stloc, dictLocal);
        EmitTlsDictPut(il, dictLocal, "name", () => il.Emit(OpCodes.Ldloc, cipherLocal), setItem);
        EmitTlsDictPut(il, dictLocal, "standardName", () => il.Emit(OpCodes.Ldloc, cipherLocal), setItem);
        EmitTlsDictPut(il, dictLocal, "version", () => il.Emit(OpCodes.Ldloc, versionLocal), setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? GetProtocol()
    /// </summary>
    private void EmitTlsSocketGetProtocol(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod("GetProtocol", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();

        var hasStream = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketSslStreamField);
        il.Emit(OpCodes.Brtrue, hasStream);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasStream);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketSslStreamField);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetProperty("SslProtocol")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _tlsProtoStringMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? GetPeerCertificate(object detailed)
    /// Builds { subject, issuer, valid_from, valid_to, serialNumber, fingerprint } from _peerCert.
    /// </summary>
    private void EmitTlsSocketGetPeerCertificate(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod("GetPeerCertificate", MethodAttributes.Public, _types.Object, [_types.Object]);
        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var setItem = _types.GetMethod(dictType, "set_Item", _types.String, _types.Object);

        // if (_peerCert == null) return new Dictionary()
        var hasCert = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketPeerCertField);
        il.Emit(OpCodes.Brtrue, hasCert);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasCert);

        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Stloc, dictLocal);

        // subject / issuer / serialNumber / fingerprint
        EmitTlsDictPut(il, dictLocal, "subject", () => EmitLoadCertStringProp(il, "Subject"), setItem);
        EmitTlsDictPut(il, dictLocal, "issuer", () => EmitLoadCertStringProp(il, "Issuer"), setItem);
        EmitTlsDictPut(il, dictLocal, "serialNumber", () => EmitLoadCertStringProp(il, "SerialNumber"), setItem);
        EmitTlsDictPut(il, dictLocal, "fingerprint", () => EmitLoadCertStringProp(il, "Thumbprint"), setItem);

        // valid_from = _peerCert.NotBefore.ToString("R")
        EmitTlsDictPut(il, dictLocal, "valid_from", () => EmitLoadCertDateProp(il, "NotBefore"), setItem);
        EmitTlsDictPut(il, dictLocal, "valid_to", () => EmitLoadCertDateProp(il, "NotAfter"), setItem);

        // subjectaltname = _SanString(_peerCert)  (may be null)
        EmitTlsDictPut(il, dictLocal, "subjectaltname", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tlsSocketPeerCertField);
            il.Emit(OpCodes.Call, _tlsSanStringMethod);
        }, setItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitLoadCertStringProp(ILGenerator il, string prop)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketPeerCertField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty(prop)!.GetGetMethod()!);
    }

    private void EmitLoadCertDateProp(ILGenerator il, string prop)
    {
        var dtLocal = il.DeclareLocal(_types.DateTime);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketPeerCertField);
        il.Emit(OpCodes.Callvirt, typeof(X509Certificate2).GetProperty(prop)!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, dtLocal);
        il.Emit(OpCodes.Ldloca, dtLocal);
        il.Emit(OpCodes.Ldstr, "R");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DateTime, "ToString", [_types.String])!);
    }

    /// <summary>
    /// Emits: public object? Renegotiate(object options, object callback)
    /// SslStream does not expose user-driven renegotiation cleanly; returns this (matches interp).
    /// </summary>
    private void EmitTlsSocketRenegotiate(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod("Renegotiate", MethodAttributes.Public, _types.Object, [_types.Object, _types.Object]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a 0-arg method that throws a "$methodName is not supported on this runtime" $Error.
    /// The leading "Get/Set" is lowercased to derive the JS name for the message.
    /// </summary>
    private void EmitTlsSocketUnsupported(TypeBuilder typeBuilder, EmittedRuntime runtime, string methodName)
    {
        var method = typeBuilder.DefineMethod(methodName, MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        var il = method.GetILGenerator();
        var jsName = char.ToLowerInvariant(methodName[0]) + methodName.Substring(1);
        il.Emit(OpCodes.Ldstr, $"tls.TLSSocket.{jsName}() is not supported on this runtime (not exposed by .NET SslStream)");
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        // Wrap the $Error so the compiled guest try/catch can catch it (a raw $Error is not a
        // System.Exception and would surface as a RuntimeWrappedException).
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public object? GetMember(string name)
    /// TLS props (authorized/encrypted/alpnProtocol/servername/authorizationError); falls back to
    /// $NetSocket.GetMember for the inherited socket properties (remoteAddress, bytesRead, …).
    /// </summary>
    private void EmitTlsSocketGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("GetMember", MethodAttributes.Public, _types.Object, [_types.String]);
        var il = method.GetILGenerator();

        var authorizedLabel = il.DefineLabel();
        var encryptedLabel = il.DefineLabel();
        var alpnLabel = il.DefineLabel();
        var servernameLabel = il.DefineLabel();
        var authErrLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitTlsStringCheck(il, "authorized", authorizedLabel);
        EmitTlsStringCheck(il, "encrypted", encryptedLabel);
        EmitTlsStringCheck(il, "alpnProtocol", alpnLabel);
        EmitTlsStringCheck(il, "servername", servernameLabel);
        EmitTlsStringCheck(il, "authorizationError", authErrLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        // authorized -> box _authorized
        il.MarkLabel(authorizedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketAuthorizedField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // encrypted -> _sslStream != null
        il.MarkLabel(encryptedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketSslStreamField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // alpnProtocol -> _alpnProtocol ?? undefined
        il.MarkLabel(alpnLabel);
        EmitTlsFieldOrUndefined(il, _tlsSocketAlpnProtocolField, runtime);
        il.Emit(OpCodes.Ret);

        // servername -> _servername ?? undefined
        il.MarkLabel(servernameLabel);
        EmitTlsFieldOrUndefined(il, _tlsSocketServernameField, runtime);
        il.Emit(OpCodes.Ret);

        // authorizationError -> _authError (may be null)
        il.MarkLabel(authErrLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsSocketAuthErrorField);
        il.Emit(OpCodes.Ret);

        // default -> base.GetMember(name) (inherited $NetSocket props)
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NetSocketGetMember);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTlsStringCheck(ILGenerator il, string value, Label target)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, target);
    }

    private void EmitTlsFieldOrUndefined(ILGenerator il, FieldBuilder field, EmittedRuntime runtime)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Dup);
        var has = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, has);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.MarkLabel(has);
    }

    private void EmitTlsDictPut(ILGenerator il, LocalBuilder dictLocal, string key, Action loadValue, MethodInfo setItem)
    {
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, key);
        loadValue();
        il.Emit(OpCodes.Callvirt, setItem);
    }

    // ========================================================================
    // $TlsServer : $EventEmitter
    // ========================================================================

    private void EmitTlsServerClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TlsServer",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        _tlsServerTypeBuilder = typeBuilder;
        _ = typeBuilder;

        // Fields
        _tlsServerIsListeningField = typeBuilder.DefineField("_isListening", _types.Boolean, FieldAttributes.Private);
        _tlsServerCallbackField = typeBuilder.DefineField("_callback", _types.Object, FieldAttributes.Assembly);
        _tlsServerCertField = typeBuilder.DefineField("_cert", _types.String, FieldAttributes.Assembly);
        _tlsServerKeyField = typeBuilder.DefineField("_key", _types.String, FieldAttributes.Assembly);
        _tlsServerRequestCertField = typeBuilder.DefineField("_requestCert", _types.Boolean, FieldAttributes.Assembly);
        _tlsServerListenerField = typeBuilder.DefineField("_listener", typeof(System.Net.Sockets.TcpListener), FieldAttributes.Assembly);
        _tlsServerPortField = typeBuilder.DefineField("_port", _types.Int32, FieldAttributes.Private);
        _tlsServerAlpnField = typeBuilder.DefineField("_alpn", typeof(string[]), FieldAttributes.Assembly);

        // Accept worker stub (body emitted later — needs closure type)
        _tlsServerAcceptWorkerMethod = typeBuilder.DefineMethod(
            "_TlsAcceptWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        // Constructor: public $TlsServer(object? options, object? callback)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]
        );
        runtime.TlsServerCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, _tlsServerIsListeningField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, _tlsServerCallbackField);

        var skipOptions = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Brfalse, skipOptions);

        var dictType = _types.DictionaryStringObject;
        var dictTryGet = dictType.GetMethod("TryGetValue")!;
        var tempLocal = ctorIL.DeclareLocal(_types.Object);

        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Isinst, dictType);
        ctorIL.Emit(OpCodes.Brfalse, skipOptions);

        var optLocal = ctorIL.DeclareLocal(dictType);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Castclass, dictType);
        ctorIL.Emit(OpCodes.Stloc, optLocal);

        // cert
        EmitTlsCtorExtractString(ctorIL, optLocal, tempLocal, dictTryGet, "cert", _tlsServerCertField);
        // key
        EmitTlsCtorExtractString(ctorIL, optLocal, tempLocal, dictTryGet, "key", _tlsServerKeyField);

        // secureContext fallback: if cert/key absent, pull them from options.secureContext
        // (a tls.createSecureContext result).
        var noSecureCtx = ctorIL.DefineLabel();
        var scLocal = ctorIL.DeclareLocal(dictType);
        ctorIL.Emit(OpCodes.Ldloc, optLocal);
        ctorIL.Emit(OpCodes.Ldstr, "secureContext");
        ctorIL.Emit(OpCodes.Ldloca, tempLocal);
        ctorIL.Emit(OpCodes.Callvirt, dictTryGet);
        ctorIL.Emit(OpCodes.Brfalse, noSecureCtx);
        ctorIL.Emit(OpCodes.Ldloc, tempLocal);
        ctorIL.Emit(OpCodes.Isinst, dictType);
        ctorIL.Emit(OpCodes.Stloc, scLocal);
        ctorIL.Emit(OpCodes.Ldloc, scLocal);
        ctorIL.Emit(OpCodes.Brfalse, noSecureCtx);
        // if (_cert == null) extract sc["cert"]
        var skipScCert = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldfld, _tlsServerCertField);
        ctorIL.Emit(OpCodes.Brtrue, skipScCert);
        EmitTlsCtorExtractString(ctorIL, scLocal, tempLocal, dictTryGet, "cert", _tlsServerCertField);
        ctorIL.MarkLabel(skipScCert);
        // if (_key == null) extract sc["key"]
        var skipScKey = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldfld, _tlsServerKeyField);
        ctorIL.Emit(OpCodes.Brtrue, skipScKey);
        EmitTlsCtorExtractString(ctorIL, scLocal, tempLocal, dictTryGet, "key", _tlsServerKeyField);
        ctorIL.MarkLabel(skipScKey);
        ctorIL.MarkLabel(noSecureCtx);

        // requestCert (bool)
        var noReq = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldloc, optLocal);
        ctorIL.Emit(OpCodes.Ldstr, "requestCert");
        ctorIL.Emit(OpCodes.Ldloca, tempLocal);
        ctorIL.Emit(OpCodes.Callvirt, dictTryGet);
        ctorIL.Emit(OpCodes.Brfalse, noReq);
        ctorIL.Emit(OpCodes.Ldloc, tempLocal);
        ctorIL.Emit(OpCodes.Isinst, _types.Boolean);
        ctorIL.Emit(OpCodes.Brfalse, noReq);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldloc, tempLocal);
        ctorIL.Emit(OpCodes.Unbox_Any, _types.Boolean);
        ctorIL.Emit(OpCodes.Stfld, _tlsServerRequestCertField);
        ctorIL.MarkLabel(noReq);

        // ALPNProtocols → string[]
        var noAlpn = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldloc, optLocal);
        ctorIL.Emit(OpCodes.Ldstr, "ALPNProtocols");
        ctorIL.Emit(OpCodes.Ldloca, tempLocal);
        ctorIL.Emit(OpCodes.Callvirt, dictTryGet);
        ctorIL.Emit(OpCodes.Brfalse, noAlpn);
        ctorIL.Emit(OpCodes.Ldloc, tempLocal);
        ctorIL.Emit(OpCodes.Isinst, _types.ListOfObject);
        ctorIL.Emit(OpCodes.Brfalse, noAlpn);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldloc, tempLocal);
        ctorIL.Emit(OpCodes.Castclass, _types.ListOfObject);
        ctorIL.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethod("OfType")!, _types.String));
        ctorIL.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethod("ToArray")!, _types.String));
        ctorIL.Emit(OpCodes.Stfld, _tlsServerAlpnField);
        ctorIL.MarkLabel(noAlpn);

        ctorIL.MarkLabel(skipOptions);
        ctorIL.Emit(OpCodes.Ret);

        // Properties + methods
        EmitTlsServerProperties(typeBuilder);
        EmitTlsServerListen(typeBuilder, runtime);
        EmitTlsServerClose(typeBuilder, runtime);
        EmitTlsServerAddress(typeBuilder, runtime);
        EmitTlsServerGetMember(typeBuilder, runtime);

        // NOTE: CreateType() deferred to EmitTlsServerFinalize (needs accept worker body)
    }

    private void EmitTlsCtorExtractString(ILGenerator il, LocalBuilder optLocal, LocalBuilder tempLocal, MethodInfo dictTryGet, string key, FieldBuilder field)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optLocal);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldloca, tempLocal);
        il.Emit(OpCodes.Callvirt, dictTryGet);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stfld, field);
        il.MarkLabel(skip);
    }

    private void EmitTlsServerProperties(TypeBuilder typeBuilder)
    {
        var listeningProp = typeBuilder.DefineProperty("Listening", PropertyAttributes.None, _types.Boolean, null);
        var getListening = typeBuilder.DefineMethod(
            "get_Listening",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        var il = getListening.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerIsListeningField);
        il.Emit(OpCodes.Ret);
        listeningProp.SetGetMethod(getListening);
    }

    private void EmitTlsServerListen(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Listen",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Parse port from arg1 (double → int)
        var portLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, portLocal);
        var portParsed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, portParsed);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, portLocal);
        il.MarkLabel(portParsed);

        // Parse host from arg2 (string), default "0.0.0.0"
        var hostLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Stloc, hostLocal);
        var hostParsed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, hostParsed);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, hostLocal);
        il.MarkLabel(hostParsed);

        // Find callback in args (scan from right)
        var callbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);
        for (int argIdx = 4; argIdx >= 1; argIdx--)
        {
            var skipCb = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, skipCb);
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Stloc, callbackLocal);
            il.MarkLabel(skipCb);
        }

        // var ip = (host == "0.0.0.0" || host == "::") ? IPAddress.Any : (IPAddress.TryParse(host) ? parsed : IPAddress.Loopback)
        var ipLocal = il.DeclareLocal(typeof(System.Net.IPAddress));
        var useAny = il.DefineLabel();
        var ipDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, useAny);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Ldstr, "::");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, useAny);
        // IPAddress.TryParse(host, out parsed)
        var parsedLocal = il.DeclareLocal(typeof(System.Net.IPAddress));
        var notParsed = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, typeof(System.Net.IPAddress).GetMethod("TryParse", [_types.String, typeof(System.Net.IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notParsed);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Stloc, ipLocal);
        il.Emit(OpCodes.Br, ipDone);
        il.MarkLabel(notParsed);
        il.Emit(OpCodes.Ldsfld, typeof(System.Net.IPAddress).GetField("Loopback")!);
        il.Emit(OpCodes.Stloc, ipLocal);
        il.Emit(OpCodes.Br, ipDone);
        il.MarkLabel(useAny);
        il.Emit(OpCodes.Ldsfld, typeof(System.Net.IPAddress).GetField("Any")!);
        il.Emit(OpCodes.Stloc, ipLocal);
        il.MarkLabel(ipDone);

        // listener = new TcpListener(ip, port); listener.Start();
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Newobj, typeof(System.Net.Sockets.TcpListener).GetConstructor([typeof(System.Net.IPAddress), _types.Int32])!);
        var listenerLocal = il.DeclareLocal(typeof(System.Net.Sockets.TcpListener));
        il.Emit(OpCodes.Stloc, listenerLocal);
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.Sockets.TcpListener).GetMethod("Start", Type.EmptyTypes)!);

        // _listener = listener
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Stfld, _tlsServerListenerField);

        // If port == 0, read actual
        var portNotZero = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Brtrue, portNotZero);
        il.Emit(OpCodes.Ldloc, listenerLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.Sockets.TcpListener).GetProperty("LocalEndpoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(System.Net.IPEndPoint));
        il.Emit(OpCodes.Callvirt, typeof(System.Net.IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, portLocal);
        il.MarkLabel(portNotZero);

        // _port = port
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Stfld, _tlsServerPortField);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tlsServerIsListeningField);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Call callback if provided
        var noCallback = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noCallback);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noCallback);

        // Emit 'listening' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._TlsAcceptWorker))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, _tlsServerAcceptWorkerMethod);
        il.Emit(OpCodes.Newobj, typeof(System.Threading.WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(System.Threading.ThreadPool).GetMethod("QueueUserWorkItem", [typeof(System.Threading.WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTlsServerClose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        var isListeningLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerIsListeningField);
        il.Emit(OpCodes.Brtrue, isListeningLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListeningLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tlsServerIsListeningField);

        // Stop listener
        var noListener = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerListenerField);
        il.Emit(OpCodes.Brfalse, noListener);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerListenerField);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.Sockets.TcpListener).GetMethod("Stop")!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tlsServerListenerField);
        il.MarkLabel(noListener);

        // EventLoop.Unref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);

        // Call callback (arg1) if provided (TSFunction or BoundTSFunction)
        {
            var noCb = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, noCb);
            var notTSFunc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, notTSFunc);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, noCb);
            il.MarkLabel(notTSFunc);
            var notBound = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
            il.Emit(OpCodes.Brfalse, notBound);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(notBound);
            il.MarkLabel(noCb);
        }

        // Emit 'close' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTlsServerAddress(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Address",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;

        var hasListener = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerListenerField);
        il.Emit(OpCodes.Brtrue, hasListener);
        // Return dict with port from _port field
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerPortField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasListener);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Stloc, dictLocal);

        var epLocal = il.DeclareLocal(typeof(System.Net.IPEndPoint));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerListenerField);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.Sockets.TcpListener).GetProperty("LocalEndpoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(System.Net.IPEndPoint));
        il.Emit(OpCodes.Stloc, epLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.IPEndPoint).GetProperty("Address")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTlsServerGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );

        var il = method.GetILGenerator();

        var listeningLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, listeningLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        il.MarkLabel(listeningLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tlsServerIsListeningField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
