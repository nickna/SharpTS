using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js tls.Server.
/// Extends SharpTSEventEmitter for full event handling support.
/// Events: 'secureConnection', 'tlsClientError', 'listening', 'close', 'error'
/// </summary>
public class SharpTSTlsServer : SharpTSEventEmitter, IDisposable
{
    /// <inheritdoc />
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private TcpListener? _listener;
    private bool _isListening;
    private Interp? _interpreter;
    private CancellationTokenSource? _cts;
    private ISharpTSCallable? _connectionListener;
    private int _port;
    private string _host = "0.0.0.0";
    private readonly List<SharpTSTlsSocket> _connections = [];
    private int _maxConnections = int.MaxValue;
    private X509Certificate2? _certificate;
    private bool _requestCert;
    private bool _rejectUnauthorized = true; // Node's tls.createServer default
    private List<SslApplicationProtocol>? _alpnProtocols;
    private ISharpTSCallable? _sniCallback;

    /// <summary>
    /// Creates a new TLS server with certificate options and an optional connection listener.
    /// </summary>
    public SharpTSTlsServer(SharpTSObject? options = null, ISharpTSCallable? connectionListener = null)
    {
        _connectionListener = connectionListener;

        if (options != null)
        {
            // Parse certificate from PEM strings — directly, or from a SecureContext
            // (tls.createSecureContext result) passed as options.secureContext.
            var certPem = options.GetProperty("cert") as string;
            var keyPem = options.GetProperty("key") as string;
            if ((certPem == null || keyPem == null) && options.GetProperty("secureContext") is SharpTSObject sc)
            {
                certPem ??= sc.GetProperty("cert") as string;
                keyPem ??= sc.GetProperty("key") as string;
            }

            if (certPem != null && keyPem != null)
            {
                _certificate = X509Certificate2.CreateFromPem(certPem, keyPem);
                // On Windows, we need to export/reimport for SslStream compatibility
                _certificate = X509CertificateLoader.LoadPkcs12(_certificate.Export(X509ContentType.Pfx), null);
            }

            if (options.GetProperty("requestCert") is bool reqCert)
                _requestCert = reqCert;
            if (options.GetProperty("rejectUnauthorized") is bool reject)
                _rejectUnauthorized = reject;

            // Parse ALPNProtocols
            if (options.GetProperty("ALPNProtocols") is SharpTSArray alpnArray)
            {
                _alpnProtocols = alpnArray
                    .OfType<string>()
                    .Select(s => new SslApplicationProtocol(s))
                    .ToList();
            }

            // Parse SNICallback
            if (options.GetProperty("SNICallback") is ISharpTSCallable sniCb)
                _sniCallback = sniCb;
        }
    }

    /// <summary>
    /// Whether the server is currently listening.
    /// </summary>
    public bool Listening => _isListening;

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "listening" => Listening,
            "listen" => BuiltInMethod.CreateV2("listen", 0, 4, Listen),
            "close" => BuiltInMethod.CreateV2("close", 0, 1, Close),
            "address" => BuiltInMethod.CreateV2("address", 0, GetAddress),
            "getConnections" => BuiltInMethod.CreateV2("getConnections", 1, GetConnections),
            "ref" => BuiltInMethod.CreateV2("ref", 0, Ref),
            "unref" => BuiltInMethod.CreateV2("unref", 0, Unref),
            "maxConnections" => (double)_maxConnections,
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Sets a member by name.
    /// </summary>
    public void SetMember(string name, object? value)
    {
        if (name == "maxConnections" && value is double d)
            _maxConnections = (int)d;
    }

    private RuntimeValue Listen(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_isListening)
            throw new Exception("Runtime Error: Server is already listening");

        if (_certificate == null)
            throw new Exception("Runtime Error: TLS server requires key and cert options");

        _interpreter = interpreter;

        ISharpTSCallable? callback = null;

        if (args.Length > 0 && args[0].ToObject() is SharpTSObject options)
        {
            if (options.GetProperty("port") is double p) _port = (int)p;
            if (options.GetProperty("host") is string h) _host = h;
            if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb) callback = cb;
        }
        else
        {
            int argIdx = 0;
            if (argIdx < args.Length && args[argIdx].IsNumber)
            {
                _port = (int)args[argIdx].AsNumberUnsafe();
                argIdx++;
            }
            if (argIdx < args.Length && args[argIdx].IsString)
            {
                _host = args[argIdx].AsStringUnsafe();
                argIdx++;
            }
            if (argIdx < args.Length && args[argIdx].IsNumber)
                argIdx++;
            if (argIdx < args.Length && args[argIdx].ToObject() is ISharpTSCallable cb)
                callback = cb;
            if (callback == null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].ToObject() is ISharpTSCallable c) { callback = c; break; }
                }
            }
        }

        var ipAddress = _host == "0.0.0.0" || _host == "::"
            ? IPAddress.Any
            : IPAddress.TryParse(_host, out var parsed) ? parsed : IPAddress.Loopback;

        _listener = new TcpListener(ipAddress, _port);

        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
            return RuntimeValue.FromObject(this);
        }

        if (_port == 0)
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _isListening = true;
        _cts = new CancellationTokenSource();

        interpreter.Ref();

        if (callback != null)
            callback.Call(interpreter, []);
        EmitEvent(interpreter, "listening", []);

        StartAccepting(interpreter);

        return RuntimeValue.FromObject(this);
    }

    private void StartAccepting(Interp interpreter)
    {
        var token = _cts!.Token;
        var cert = _certificate!;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(token);

                    if (_connections.Count >= _maxConnections)
                    {
                        tcpClient.Close();
                        continue;
                    }

                    // Perform TLS handshake in the background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var sslStream = new SslStream(tcpClient.GetStream(), false);

                            var authOptions = new SslServerAuthenticationOptions
                            {
                                ServerCertificate = cert,
                                ClientCertificateRequired = _requestCert,
                                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                                // rejectUnauthorized was parsed from options but never
                                // applied — .NET's default validation always rejected
                                // invalid client certs, so { rejectUnauthorized: false }
                                // (accept the handshake, expose authorized=false; Node
                                // semantics) could not work. Only consulted when a
                                // client certificate was actually requested.
                                RemoteCertificateValidationCallback = (_, _, _, errors) =>
                                    !_requestCert || !_rejectUnauthorized || errors == SslPolicyErrors.None,
                            };

                            if (_alpnProtocols != null)
                                authOptions.ApplicationProtocols = _alpnProtocols;

                            if (_sniCallback != null)
                            {
                                var sniCb = _sniCallback;
                                var interp = interpreter;
                                authOptions.ServerCertificateSelectionCallback = (sender, hostName) =>
                                {
                                    try
                                    {
                                        var result = sniCb.Call(interp, [hostName]);
                                        if (result is SharpTSObject ctx)
                                        {
                                            var ctxCert = ctx.GetProperty("cert") as string;
                                            var ctxKey = ctx.GetProperty("key") as string;
                                            if (ctxCert != null && ctxKey != null)
                                            {
                                                var newCert = X509Certificate2.CreateFromPem(ctxCert, ctxKey);
                                                return X509CertificateLoader.LoadPkcs12(newCert.Export(X509ContentType.Pfx), null);
                                            }
                                        }
                                    }
                                    catch { }
                                    return cert;
                                };
                            }

                            await sslStream.AuthenticateAsServerAsync(authOptions);

                            interpreter.ScheduleTimer(0, 0, () =>
                            {
                                var tlsSocket = new SharpTSTlsSocket(tcpClient, sslStream);
                                _connections.Add(tlsSocket);

                                _connectionListener?.Call(interpreter, [tlsSocket]);
                                EmitEvent(interpreter, "secureConnection", [tlsSocket]);

                                tlsSocket.StartReading(interpreter);
                            }, isInterval: false);
                        }
                        catch (Exception ex)
                        {
                            interpreter.ScheduleTimer(0, 0, () =>
                            {
                                EmitEvent(interpreter, "tlsClientError", [new SharpTSError(ex.Message)]);
                            }, isInterval: false);

                            try { tcpClient.Close(); } catch { }
                        }
                    }, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
            }
        }, token);
    }

    private RuntimeValue Close(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_isListening)
            return RuntimeValue.FromObject(this);

        _cts?.Cancel();

        try { _listener?.Stop(); } catch { }

        _isListening = false;
        _interpreter?.Unref();

        ISharpTSCallable? callback = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        callback?.Call(interpreter, []);

        EmitEvent(interpreter, "close", []);

        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue GetAddress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_isListening || _listener == null) return RuntimeValue.Null;

        var ep = (IPEndPoint)_listener.LocalEndpoint;
        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["address"] = ep.Address.ToString(),
            ["family"] = ep.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
            ["port"] = (double)ep.Port
        }));
    }

    private RuntimeValue GetConnections(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].ToObject() is ISharpTSCallable callback)
        {
            callback.Call(interpreter, [null, (double)_connections.Count]);
        }
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Ref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter?.Ref();
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Unref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter?.Unref();
        return RuntimeValue.FromObject(this);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
        _connections.Clear();
        _certificate?.Dispose();
    }

    public override string ToString() => $"TLSServer {{ listening: {Listening} }}";
}
