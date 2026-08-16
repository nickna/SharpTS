using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'tls' module.
/// </summary>
/// <remarks>
/// Provides TLS/SSL networking functionality:
/// - createServer(options?, callback?) - create a TLS server
/// - connect(port, host?, options?, callback?) - create a TLS client connection
/// - createSecureContext(options?) - create a secure context object
/// - DEFAULT_MIN_VERSION, DEFAULT_MAX_VERSION - protocol version constants
/// </remarks>
public static class TlsModuleInterpreter
{
    /// <summary>
    /// The list returned by tls.getCiphers() — lowercased TLS 1.2/1.3 cipher-suite names.
    /// This is the single source of truth: the compiled $Runtime.TlsGetCiphers emitter reads
    /// this same array, so interp == compiled by construction.
    /// </summary>
    public static readonly string[] StandardCiphers =
    [
        "tls_aes_128_gcm_sha256",
        "tls_aes_256_gcm_sha384",
        "tls_chacha20_poly1305_sha256",
        "ecdhe-ecdsa-aes128-gcm-sha256",
        "ecdhe-rsa-aes128-gcm-sha256",
        "ecdhe-ecdsa-aes256-gcm-sha384",
        "ecdhe-rsa-aes256-gcm-sha384",
        "ecdhe-ecdsa-chacha20-poly1305",
        "ecdhe-rsa-chacha20-poly1305",
        "dhe-rsa-aes128-gcm-sha256",
        "dhe-rsa-aes256-gcm-sha384",
        "ecdhe-rsa-aes128-sha256",
        "ecdhe-rsa-aes256-sha384",
        "aes128-gcm-sha256",
        "aes256-gcm-sha384",
        "aes128-sha256",
        "aes256-sha256"
    ];

    /// <summary>
    /// Gets all exported values for the tls module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["createServer"] = BuiltInMethod.CreateV2("createServer", 0, 2, CreateServer),
            ["connect"] = BuiltInMethod.CreateV2("connect", 1, 4, Connect),
            ["createSecureContext"] = BuiltInMethod.CreateV2("createSecureContext", 0, 1, CreateSecureContext),
            ["checkServerIdentity"] = BuiltInMethod.CreateV2("checkServerIdentity", 2, CheckServerIdentity),
            ["getCiphers"] = BuiltInMethod.CreateV2("getCiphers", 0, GetCiphers),
            ["rootCertificates"] = RootCertificatesArray(),
            ["Server"] = BuiltInMethod.CreateV2("Server", 0, 2, CreateServer),
            ["TLSSocket"] = BuiltInMethod.CreateV2("TLSSocket", 0, 1, CreateTlsSocket),
            ["DEFAULT_MIN_VERSION"] = "TLSv1.2",
            ["DEFAULT_MAX_VERSION"] = "TLSv1.3"
        };
    }

    /// <summary>tls.getCiphers() — lowercased supported cipher-suite names.</summary>
    private static RuntimeValue GetCiphers(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arr = new SharpTSArray();
        foreach (var c in StandardCiphers)
            arr.Add(c);
        return RuntimeValue.FromObject(arr);
    }

    private static string[]? _rootCertCache;

    /// <summary>The bundled trusted root CA certificates, as PEM strings (from the platform trust store).</summary>
    internal static string[] RootCertificatePems()
    {
        if (_rootCertCache != null) return _rootCertCache;
        var list = new List<string>();
        foreach (var loc in new[] { System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser,
                                    System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.Root, loc);
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                foreach (var cert in store.Certificates)
                    list.Add(cert.ExportCertificatePem());
            }
            catch { /* trust store may be unavailable on some platforms */ }
        }
        _rootCertCache = list.ToArray();
        return _rootCertCache;
    }

    private static SharpTSArray RootCertificatesArray()
    {
        var arr = new SharpTSArray();
        foreach (var pem in RootCertificatePems())
            arr.Add(pem);
        return arr;
    }

    /// <summary>
    /// Creates a new TLS server.
    /// tls.createServer(options?, connectionListener?)
    /// </summary>
    private static RuntimeValue CreateServer(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        SharpTSObject? options = null;
        ISharpTSCallable? connectionListener = null;

        if (args.Length > 0)
        {
            if (args[0].ToObject() is SharpTSObject opts)
            {
                options = opts;
                if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb)
                    connectionListener = cb;
            }
            else if (args[0].ToObject() is ISharpTSCallable cb)
            {
                connectionListener = cb;
            }
        }

        return RuntimeValue.FromObject(new SharpTSTlsServer(options, connectionListener));
    }

    /// <summary>
    /// Creates a TLS connection to a remote server.
    /// tls.connect(port, host?, options?, callback?)
    /// tls.connect(options, callback?)
    /// </summary>
    private static RuntimeValue Connect(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var socket = new SharpTSTlsSocket();
        int port;
        string host = "localhost";
        SharpTSObject? options = null;
        ISharpTSCallable? callback = null;

        if (args.Length > 0 && args[0].ToObject() is SharpTSObject opts)
        {
            // tls.connect(options, callback?)
            options = opts;
            port = (int)(double)(opts.GetProperty("port") ?? throw new Exception("Runtime Error: port is required"));
            if (opts.GetProperty("host") is string h) host = h;
            if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb) callback = cb;
        }
        else if (args.Length > 0 && args[0].IsNumber)
        {
            // tls.connect(port, host?, options?, callback?)
            port = (int)args[0].AsNumberUnsafe();
            int idx = 1;
            if (idx < args.Length && args[idx].IsString) { host = args[idx].AsStringUnsafe(); idx++; }
            if (idx < args.Length && args[idx].ToObject() is SharpTSObject o) { options = o; idx++; }
            if (idx < args.Length && args[idx].ToObject() is ISharpTSCallable cb) callback = cb;
            // Also check: connect(port, callback)
            if (callback == null)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i].ToObject() is ISharpTSCallable c) { callback = c; break; }
                }
            }
        }
        else
        {
            throw new Exception("Runtime Error: tls.connect requires port number or options object");
        }

        socket.ConnectTls(interpreter, port, host, options, callback);
        return RuntimeValue.FromObject(socket);
    }

    /// <summary>
    /// Creates a secure context object.
    /// tls.createSecureContext(options?)
    /// </summary>
    private static RuntimeValue CreateSecureContext(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var context = new Dictionary<string, object?>();

        if (args.Length > 0 && args[0].ToObject() is SharpTSObject options)
        {
            if (options.GetProperty("cert") is string cert) context["cert"] = cert;
            if (options.GetProperty("key") is string key) context["key"] = key;
            if (options.GetProperty("ca") is string ca) context["ca"] = ca;
            if (options.GetProperty("minVersion") is string minVer) context["minVersion"] = minVer;
            if (options.GetProperty("maxVersion") is string maxVer) context["maxVersion"] = maxVer;
        }

        return RuntimeValue.FromObject(new SharpTSObject(context));
    }

    /// <summary>
    /// Creates a new unconnected TLS socket.
    /// </summary>
    private static RuntimeValue CreateTlsSocket(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromObject(new SharpTSTlsSocket());
    }

    /// <summary>
    /// tls.checkServerIdentity(hostname, cert) — Node's default identity check.
    /// Verifies the hostname against the certificate's subjectaltname (DNS/IP entries,
    /// with simple wildcard support) or, when no SAN is present, the subject CN.
    /// Returns undefined on match, or an Error on mismatch.
    /// </summary>
    private static RuntimeValue CheckServerIdentity(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string host = args.Length > 0 && args[0].IsString ? args[0].AsStringUnsafe() : "";
        string? san = null;
        string? subjectDN = null;
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject cert)
        {
            san = cert.GetProperty("subjectaltname") as string;
            if (cert.GetProperty("subject") is string subjStr) subjectDN = subjStr;
            else if (cert.GetProperty("subject") is SharpTSObject subjObj) subjectDN = subjObj.GetProperty("CN") as string;
        }

        var error = CheckIdentityCore(host, san, subjectDN);
        return error == null
            ? RuntimeValue.Undefined
            : RuntimeValue.FromObject(new SharpTSError(error) { Code = "ERR_TLS_CERT_ALTNAME_INVALID" });
    }

    /// <summary>
    /// Shared identity-check logic (kept in sync with the compiled $Runtime.TlsCheckServerIdentity IL).
    /// <paramref name="subjectDN"/> may be a full DN ("CN=localhost, O=…") or a bare CN; the CN value
    /// is extracted when no SAN entries apply. Returns null when the host is valid, else a Node-style
    /// error message.
    /// </summary>
    internal static string? CheckIdentityCore(string host, string? subjectAltName, string? subjectDN)
    {
        var names = new List<string>();
        if (!string.IsNullOrEmpty(subjectAltName))
        {
            foreach (var raw in subjectAltName.Split(','))
            {
                var part = raw.Trim();
                if (part.StartsWith("DNS:", StringComparison.Ordinal)) names.Add(part.Substring(4));
                else if (part.StartsWith("IP Address:", StringComparison.Ordinal)) names.Add(part.Substring(11));
            }
        }
        if (names.Count == 0 && !string.IsNullOrEmpty(subjectDN))
        {
            var cn = ExtractCN(subjectDN!);
            if (!string.IsNullOrEmpty(cn)) names.Add(cn!);
        }

        foreach (var name in names)
            if (HostMatches(host, name))
                return null;

        var altText = names.Count > 0 ? string.Join(", ", names) : "<no cert names>";
        return $"Host: {host}. is not in the cert's altnames: {altText}";
    }

    private static string? ExtractCN(string subjectDN)
    {
        int i = subjectDN.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return subjectDN; // already a bare CN
        int start = i + 3;
        int end = subjectDN.IndexOf(',', start);
        return (end < 0 ? subjectDN.Substring(start) : subjectDN.Substring(start, end - start)).Trim();
    }

    private static bool HostMatches(string host, string name)
    {
        if (name.StartsWith("*.", StringComparison.Ordinal))
        {
            // Wildcard matches exactly one leading label: *.example.com ⇒ foo.example.com
            int dot = host.IndexOf('.');
            return dot > 0 && string.Equals(host.Substring(dot), name.Substring(1), StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(host, name, StringComparison.OrdinalIgnoreCase);
    }
}
