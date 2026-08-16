using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js tls.TLSSocket.
/// Extends SharpTSSocket, wrapping the underlying stream with SslStream.
/// </summary>
public class SharpTSTlsSocket : SharpTSSocket
{
    private SslStream? _sslStream;
    private bool _authorized;
    private string? _authorizationError;
    private string? _alpnProtocol;
    private X509Certificate2? _peerCertificate;
    private string? _servername;
    private bool _rejectUnauthorized;
    private CancellationTokenSource? _handshakeCts;
    private CancellationTokenRegistration _handshakeShutdownRegistration;
    private int _handshakeRefHeld;

    /// <summary>
    /// Creates a new unconnected TLS socket (client-side).
    /// </summary>
    public SharpTSTlsSocket()
    {
    }

    /// <summary>
    /// Creates a TLS socket wrapping an existing TCP client with an already-negotiated SslStream (server-side).
    /// </summary>
    public SharpTSTlsSocket(System.Net.Sockets.TcpClient client, SslStream sslStream)
        : base(client)
    {
        _sslStream = sslStream;
        _stream = sslStream; // Replace the NetworkStream with SslStream
        _authorized = sslStream.IsAuthenticated && sslStream.IsMutuallyAuthenticated || sslStream.IsAuthenticated;
        _peerCertificate = sslStream.RemoteCertificate as X509Certificate2;
        _alpnProtocol = sslStream.NegotiatedApplicationProtocol.ToString();
        if (string.IsNullOrEmpty(_alpnProtocol)) _alpnProtocol = null;
    }

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            // TLS-specific properties
            "authorized" => _authorized,
            "authorizationError" => (object?)_authorizationError,
            "encrypted" => _sslStream != null,
            "alpnProtocol" => (object?)_alpnProtocol ?? SharpTSUndefined.Instance,
            "servername" => (object?)_servername ?? SharpTSUndefined.Instance,

            // TLS-specific methods
            "getCipher" => BuiltInMethod.CreateV2("getCipher", 0, GetCipher),
            "getPeerCertificate" => BuiltInMethod.CreateV2("getPeerCertificate", 0, 1, GetPeerCertificate),
            "getProtocol" => BuiltInMethod.CreateV2("getProtocol", 0, GetProtocol),
            "renegotiate" => BuiltInMethod.CreateV2("renegotiate", 0, 2, Renegotiate),

            // Advanced TLS APIs not exposed by .NET SslStream — throw a clear error rather than
            // a silent no-op (documented ceilings; kept in sync with the compiled $TlsSocket).
            "getSession" => UnsupportedMethod("getSession"),
            "setSession" => UnsupportedMethod("setSession"),
            "getTLSTicket" => UnsupportedMethod("getTLSTicket"),
            "getPeerFinished" => UnsupportedMethod("getPeerFinished"),
            "getFinished" => UnsupportedMethod("getFinished"),
            "setMaxSendFragment" => UnsupportedMethod("setMaxSendFragment"),
            "exportKeyingMaterial" => UnsupportedMethod("exportKeyingMaterial"),

            // Fall through to base Socket members
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Connects to a remote host with TLS.
    /// </summary>
    internal void ConnectTls(Interp interpreter, int port, string host, SharpTSObject? options, ISharpTSCallable? callback)
    {
        _interpreter = interpreter;
        _servername = options?.GetProperty("servername") as string ?? host;

        if (options?.GetProperty("rejectUnauthorized") is bool reject)
            _rejectUnauthorized = reject;
        else
            _rejectUnauthorized = true; // Default: reject unauthorized

        if (callback != null)
            AddListenerDirect("secureConnect", callback);

        _client = new System.Net.Sockets.TcpClient();

        // Keep event loop alive during async TLS handshake
        interpreter.Ref();
        _handshakeRefHeld = 1;

        var capturedHost = host;
        var capturedPort = port;
        var capturedServername = _servername;
        var capturedReject = _rejectUnauthorized;

        _handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(interpreter.ShutdownToken);
        _handshakeShutdownRegistration = _handshakeCts.Token.Register(() =>
        {
            try { _sslStream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            ReleaseHandshakeRef(interpreter);
        });
        _ = ConnectTlsAsync(
            interpreter,
            capturedHost,
            capturedPort,
            capturedServername,
            capturedReject,
            options,
            _handshakeCts);
    }

    private async Task ConnectTlsAsync(
        Interp interpreter,
        string host,
        int port,
        string servername,
        bool rejectUnauthorized,
        SharpTSObject? options,
        CancellationTokenSource handshakeCts)
    {
        var token = handshakeCts.Token;
        var completionScheduled = false;
        try
        {
            await _client!.ConnectAsync(host, port, token);
            var networkStream = _client.GetStream();

            // Always observe the chain validation result so authorized/authorizationError
            // reflect Node semantics (false + reason for a self-signed/untrusted peer), even
            // when rejectUnauthorized:false lets the handshake proceed.
            var capturedErrors = SslPolicyErrors.None;
            _sslStream = new SslStream(networkStream, false,
                (sender, cert, chain, errors) =>
                {
                    capturedErrors = errors;
                    return !rejectUnauthorized || errors == SslPolicyErrors.None;
                });

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = servername,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            };

            if (options?.GetProperty("ALPNProtocols") is SharpTSArray alpnArray)
            {
                sslOptions.ApplicationProtocols = alpnArray
                    .OfType<string>()
                    .Select(s => new SslApplicationProtocol(s))
                    .ToList();
            }

            await _sslStream.AuthenticateAsClientAsync(sslOptions, token);

            if (token.IsCancellationRequested)
                return;

            _stream = _sslStream;
            _authorized = capturedErrors == SslPolicyErrors.None;
            _authorizationError = _authorized ? null : DescribePolicyErrors(capturedErrors);
            _peerCertificate = _sslStream.RemoteCertificate as X509Certificate2;
            _alpnProtocol = _sslStream.NegotiatedApplicationProtocol.ToString();
            if (string.IsNullOrEmpty(_alpnProtocol)) _alpnProtocol = null;

            completionScheduled = true;
            interpreter.ScheduleTimer(0, 0, () =>
            {
                if (interpreter.ShutdownToken.IsCancellationRequested)
                {
                    CloseTransportForShutdown();
                    return;
                }

                CompleteHandshakeCancellation(handshakeCts);
                // Unref the handshake ref; StartReading will add its own
                ReleaseHandshakeRef(interpreter);
                EmitEvent(interpreter, "secureConnect", []);
                StartReading(interpreter);
            }, isInterval: false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CloseTransportForShutdown();
        }
        catch (AuthenticationException ex)
        {
            if (!token.IsCancellationRequested)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    ReleaseHandshakeRef(interpreter);
                    if (!interpreter.ShutdownToken.IsCancellationRequested)
                        EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
                }, isInterval: false);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    ReleaseHandshakeRef(interpreter);
                    if (!interpreter.ShutdownToken.IsCancellationRequested)
                        EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
                }, isInterval: false);
            }
        }
        finally
        {
            if (!completionScheduled &&
                ReferenceEquals(Volatile.Read(ref _handshakeCts), handshakeCts))
                CompleteHandshakeCancellation(handshakeCts);
        }
    }

    private void CompleteHandshakeCancellation(CancellationTokenSource handshakeCts)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _handshakeCts, null, handshakeCts), handshakeCts))
            return;

        _handshakeShutdownRegistration.Dispose();
        handshakeCts.Dispose();
    }

    private void ReleaseHandshakeRef(Interp interpreter)
    {
        if (Interlocked.Exchange(ref _handshakeRefHeld, 0) == 1)
            interpreter.Unref();
    }

    /// <summary>Stops pending TLS I/O without enqueueing guest callbacks during host shutdown.</summary>
    internal void CloseTransportForShutdown()
    {
        try { _handshakeCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _sslStream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        if (_interpreter != null)
            ReleaseHandshakeRef(_interpreter);
    }

    private RuntimeValue GetCipher(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_sslStream == null)
            return RuntimeValue.Null;

        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = _sslStream.NegotiatedCipherSuite.ToString(),
            ["standardName"] = _sslStream.NegotiatedCipherSuite.ToString(),
            ["version"] = GetProtocolString(_sslStream.SslProtocol)
        }));
    }

    private RuntimeValue GetPeerCertificate(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_peerCertificate == null)
            return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>()));

        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["subject"] = _peerCertificate.Subject,
            ["issuer"] = _peerCertificate.Issuer,
            ["valid_from"] = _peerCertificate.NotBefore.ToString("R"),
            ["valid_to"] = _peerCertificate.NotAfter.ToString("R"),
            ["serialNumber"] = _peerCertificate.SerialNumber,
            ["fingerprint"] = _peerCertificate.Thumbprint,
            ["subjectaltname"] = SubjectAltName(_peerCertificate)
        }));
    }

    private RuntimeValue GetProtocol(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_sslStream == null) return RuntimeValue.Null;
        return RuntimeValue.FromBoxed(GetProtocolString(_sslStream.SslProtocol));
    }

    private RuntimeValue Renegotiate(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // Node.js renegotiate() - not widely used, return this for chaining
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Returns a built-in that throws a clear "not supported on this runtime" error — used for the
    /// TLS APIs .NET SslStream doesn't expose (session tickets, Finished messages, keying material,
    /// max-fragment, PSK). See the "Known .NET / SslStream ceilings" in epic #1032.
    /// </summary>
    private static BuiltInMethod UnsupportedMethod(string name) =>
        BuiltInMethod.CreateV2(name, 0, int.MaxValue, (interp, receiver, args) =>
            throw new SharpTS.Runtime.Exceptions.ThrowException(new SharpTSError(
                $"tls.TLSSocket.{name}() is not supported on this runtime (not exposed by .NET SslStream)")));

    /// <summary>
    /// Maps SslStream chain-validation errors to a Node-ish authorizationError string.
    /// Kept in sync with the compiled $TlsConnectClosure validation path.
    /// </summary>
    internal static string DescribePolicyErrors(SslPolicyErrors errors)
    {
        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            return "self-signed certificate";
        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            return "Hostname/IP does not match certificate's altnames";
        if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            return "no certificate provided";
        return errors.ToString();
    }

    /// <summary>
    /// Formats a certificate's Subject Alternative Name extension the way Node does:
    /// "DNS:localhost, IP Address:127.0.0.1". Returns null if no SAN extension is present.
    /// </summary>
    internal static string? SubjectAltName(X509Certificate2 cert)
    {
        foreach (var ext in cert.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17") continue;
            var san = ext as X509SubjectAlternativeNameExtension
                      ?? new X509SubjectAlternativeNameExtension(ext.RawData);
            var parts = new List<string>();
            foreach (var dns in san.EnumerateDnsNames())
                parts.Add("DNS:" + dns);
            foreach (var ip in san.EnumerateIPAddresses())
                parts.Add("IP Address:" + ip.ToString());
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
        return null;
    }

    private static string? GetProtocolString(SslProtocols protocol)
    {
        return protocol switch
        {
            SslProtocols.Tls12 => "TLSv1.2",
            SslProtocols.Tls13 => "TLSv1.3",
#pragma warning disable SYSLIB0039 // Obsolete TLS versions - needed for protocol string mapping
            SslProtocols.Tls11 => "TLSv1.1",
            SslProtocols.Tls => "TLSv1",
#pragma warning restore SYSLIB0039
            _ => protocol.ToString()
        };
    }

    public override string ToString() => $"TLSSocket {{ encrypted: {_sslStream != null}, authorized: {_authorized} }}";
}
