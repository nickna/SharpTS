using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js https.Server — a real HTTPS server (#1049). Unlike the
/// former cleartext proxy to http, this accepts TLS connections directly (TcpListener + SslStream,
/// reusing the tls #1032 cert handling) and runs a minimal HTTP/1.1 request/response pipeline over
/// each TLS connection.
/// </summary>
/// <remarks>
/// Events: 'request', 'listening', 'close', 'error', 'secureConnection', 'tlsClientError'.
/// Each connection serves a single request then closes (Connection: close) — keep-alive is not
/// implemented for the TLS server. The compiled https server is a documented follow-up
/// (interpreter-first), consistent with the epic's phased plan.
/// </remarks>
public class SharpTSHttpsServer : SharpTSEventEmitter, IDisposable
{
    /// <inheritdoc />
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private readonly ISharpTSCallable? _requestHandler;
    private X509Certificate2? _certificate;
    private List<SslApplicationProtocol>? _alpnProtocols;
    private TcpListener? _listener;
    private bool _isListening;
    private Interp? _interpreter;
    private CancellationTokenSource? _cts;
    private int _port;
    private string _host = "0.0.0.0";

    public SharpTSHttpsServer(SharpTSObject? options, ISharpTSCallable? requestHandler)
    {
        _requestHandler = requestHandler;
        if (options != null)
            LoadCertificate(options);
    }

    private void LoadCertificate(SharpTSObject options)
    {
        var certPem = options.GetProperty("cert") as string;
        var keyPem = options.GetProperty("key") as string;
        var passphrase = options.GetProperty("passphrase") as string;

        if ((certPem == null || keyPem == null) && options.GetProperty("secureContext") is SharpTSObject sc)
        {
            certPem ??= sc.GetProperty("cert") as string;
            keyPem ??= sc.GetProperty("key") as string;
        }

        if (certPem != null && keyPem != null)
        {
            var cert = X509Certificate2.CreateFromPem(certPem, keyPem);
            // Export/reimport so the private key is usable by SslStream on Windows.
            _certificate = X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
        }
        else if (options.GetProperty("pfx") is { } pfxObj)
        {
            byte[]? pfxBytes = pfxObj switch
            {
                SharpTSBuffer b => b.Data,
                string s => Convert.FromBase64String(s),
                _ => null
            };
            if (pfxBytes != null)
                _certificate = X509CertificateLoader.LoadPkcs12(pfxBytes, passphrase);
        }

        if (options.GetProperty("ALPNProtocols") is SharpTSArray alpnArray)
        {
            _alpnProtocols = alpnArray.OfType<string>().Select(s => new SslApplicationProtocol(s)).ToList();
        }
    }

    public bool Listening => _isListening;

    public override object? GetMember(string name)
    {
        return name switch
        {
            "listening" => Listening,
            "listen" => BuiltInMethod.CreateV2("listen", 1, 3, (interp, receiver, args) =>
                receiver.ToObject() is SharpTSHttpsServer s ? s.Listen(interp, args) : receiver).Bind(this),
            "close" => BuiltInMethod.CreateV2("close", 0, 1, (interp, receiver, args) =>
                receiver.ToObject() is SharpTSHttpsServer s ? s.Close(interp, args) : receiver).Bind(this),
            "address" => BuiltInMethod.CreateV2("address", 0, (_, receiver, _) =>
                RuntimeValue.FromBoxed(receiver.ToObject() is SharpTSHttpsServer s ? s.GetAddress() : null)).Bind(this),
            "setTimeout" => BuiltInMethod.CreateV2("setTimeout", 0, 2, (_, receiver, _) => receiver).Bind(this),
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue Listen(Interp interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_isListening) throw new Exception("Runtime Error: Server is already listening");
        if (_certificate == null) throw new Exception("Runtime Error: https.createServer requires key and cert (or pfx)");

        _interpreter = interpreter;
        ISharpTSCallable? callback = null;

        if (args.Length > 0 && args[0].IsNumber)
            _port = (int)args[0].AsNumberUnsafe();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].ToObject() is ISharpTSCallable cb) { callback = cb; break; }
            if (args[i].IsString) _host = args[i].AsStringUnsafe();
        }

        var ip = _host is "0.0.0.0" or "::" ? IPAddress.Any
            : IPAddress.TryParse(_host, out var parsed) ? parsed : IPAddress.Loopback;
        _listener = new TcpListener(ip, _port);
        _listener.Start();
        if (_port == 0)
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _isListening = true;
        _cts = new CancellationTokenSource();
        interpreter.Ref();

        callback?.Call(interpreter, []);
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
                TcpClient tcpClient;
                try { tcpClient = await _listener.AcceptTcpClientAsync(token); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                _ = Task.Run(async () =>
                {
                    SslStream? ssl = null;
                    try
                    {
                        ssl = new SslStream(tcpClient.GetStream(), false);
                        var authOptions = new SslServerAuthenticationOptions
                        {
                            ServerCertificate = cert,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        };
                        if (_alpnProtocols != null)
                            authOptions.ApplicationProtocols = _alpnProtocols;

                        await ssl.AuthenticateAsServerAsync(authOptions);

                        var parsed = await HttpProtocol.ReadRequestAsync(ssl, token);
                        if (parsed == null) { Cleanup(ssl, tcpClient); return; }

                        interpreter.ScheduleTimer(0, 0, () =>
                        {
                            try { DispatchRequest(interpreter, ssl, tcpClient, parsed); }
                            catch (Exception ex) { EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]); }
                        }, isInterval: false);
                    }
                    catch (Exception ex)
                    {
                        interpreter.ScheduleTimer(0, 0, () =>
                            EmitEvent(interpreter, "tlsClientError", [new SharpTSError(ex.Message)]), isInterval: false);
                        Cleanup(ssl, tcpClient);
                    }
                }, token);
            }
        }, token);
    }

    private void DispatchRequest(Interp interpreter, SslStream ssl, TcpClient tcpClient, HttpProtocol.ParsedRequest parsed)
    {
        var req = new SharpTSHttpsServerRequest(parsed);
        var res = new SharpTSHttpsServerResponse(ssl, tcpClient);

        EmitEvent(interpreter, "request", [req, res]);
        _requestHandler?.Call(interpreter, [req, res]);

        // Body was already read off the wire; deliver it after listeners are attached.
        req.DeliverBody(interpreter, parsed.Body);
    }

    private static void Cleanup(SslStream? ssl, TcpClient tcpClient)
    {
        try { ssl?.Dispose(); } catch { }
        try { tcpClient.Close(); } catch { }
    }

    private RuntimeValue Close(Interp interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_isListening) return RuntimeValue.FromObject(this);
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _isListening = false;
        _interpreter?.Unref();

        if (args.Length > 0 && args[0].ToObject() is ISharpTSCallable cb)
            cb.Call(interpreter, []);
        EmitEvent(interpreter, "close", []);
        return RuntimeValue.FromObject(this);
    }

    private object? GetAddress()
    {
        if (!_isListening || _listener == null) return null;
        var ep = (IPEndPoint)_listener.LocalEndpoint;
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["address"] = ep.Address.ToString(),
            ["family"] = ep.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
            ["port"] = (double)ep.Port
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _cts?.Dispose();
        _certificate?.Dispose();
        _isListening = false;
    }

    public override string ToString() => $"Server {{ listening: {Listening} }}";
}
