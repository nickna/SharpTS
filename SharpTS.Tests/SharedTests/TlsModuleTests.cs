using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the tls module (TLS/SSL sockets).
/// </summary>
public class TlsModuleTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                console.log(typeof tls.createServer);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsModuleNodePrefix(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'node:tls';
                console.log(typeof tls.createServer);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsModuleExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                console.log(typeof tls.createServer !== 'undefined');
                console.log(typeof tls.connect !== 'undefined');
                console.log(typeof tls.createSecureContext !== 'undefined');
                console.log(tls.DEFAULT_MIN_VERSION);
                console.log(tls.DEFAULT_MAX_VERSION);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\nTLSv1.2\nTLSv1.3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsModuleConstants(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { DEFAULT_MIN_VERSION, DEFAULT_MAX_VERSION } from 'tls';
                console.log(DEFAULT_MIN_VERSION === 'TLSv1.2');
                console.log(DEFAULT_MAX_VERSION === 'TLSv1.3');
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsCreateServerReturnsServer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const server = tls.createServer({
                    key: 'dummy',
                    cert: 'dummy'
                });
                console.log(typeof server.listen);
                console.log(typeof server.close);
                console.log(typeof server.address);
                console.log(typeof server.on);
                console.log(server.listening);
                """
        };
        // Note: server creation doesn't require valid certs (only listen/handshake does)
        // But our constructor parses PEM, so we need real-ish PEM or handle gracefully.
        // Instead test with no cert options:
        files["./main.ts"] = """
            import * as tls from 'tls';
            const server = tls.createServer();
            console.log(typeof server.listen);
            console.log(typeof server.close);
            console.log(typeof server.address);
            console.log(typeof server.on);
            console.log(server.listening);
            """;
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsConnectReturnsSocket(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const socket = tls.TLSSocket();
                console.log(typeof socket.getCipher);
                console.log(typeof socket.getPeerCertificate);
                console.log(typeof socket.getProtocol);
                console.log(typeof socket.write);
                console.log(typeof socket.on);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsSocketProperties(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const socket = tls.TLSSocket();
                console.log(socket.encrypted);
                console.log(socket.authorized);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsCreateSecureContext(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const ctx = tls.createSecureContext();
                console.log(ctx !== null && ctx !== undefined);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public async Task TlsServerAndClientHandshake()
    {
        // Pure C# test to verify TLS handshake works at the .NET level
        var (certPem, keyPem) = GenerateSelfSignedCert();

        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var certObj = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(certPem, keyPem);
        certObj = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            certObj.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var serverTask = Task.Run(async () =>
        {
            var tcpClient = await listener.AcceptTcpClientAsync(cts.Token);
            var sslStream = new System.Net.Security.SslStream(tcpClient.GetStream(), false);
            await sslStream.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions
            {
                ServerCertificate = certObj,
            }, cts.Token);
            var writer = new System.IO.StreamWriter(sslStream);
            await writer.WriteAsync("hello from server");
            await writer.FlushAsync();
            sslStream.Close();
            tcpClient.Close();
        }, cts.Token);

        var clientTask = Task.Run(async () =>
        {
            var tcpClient = new System.Net.Sockets.TcpClient();
            await tcpClient.ConnectAsync(System.Net.IPAddress.Loopback, port, cts.Token);
            var sslStream = new System.Net.Security.SslStream(tcpClient.GetStream(), false,
                (_, _, _, _) => true);
            await sslStream.AuthenticateAsClientAsync("localhost");
            var reader = new System.IO.StreamReader(sslStream);
            var data = await reader.ReadToEndAsync(cts.Token);
            sslStream.Close();
            tcpClient.Close();
            return data;
        }, cts.Token);

        await Task.WhenAll(serverTask, clientTask);
        Assert.Equal("hello from server", await clientTask);
        listener.Stop();
        certObj.Dispose();
    }

    [Fact]
    public async Task TlsServerAndClientHandshake_Interpreted()
    {
        // Generate self-signed cert programmatically
        var (certPem, keyPem) = GenerateSelfSignedCert();

        var source = $$"""
            import * as tls from 'tls';

            const options = {
                key: `{{keyPem}}`,
                cert: `{{certPem}}`
            };

            let serverSocket: any;
            const server = tls.createServer(options, (socket: any) => {
                serverSocket = socket;
                socket.write('hello from server');
                socket.end();
            });

            server.on('tlsClientError', (err: any) => {
                console.log('tls client error: ' + err.message);
                server.close();
            });

            server.listen(0, () => {
                const addr = server.address();
                const client = tls.connect({
                    port: addr.port,
                    host: '127.0.0.1',
                    rejectUnauthorized: false
                });

                client.on('secureConnect', () => {
                    console.log('secure connection established');
                    console.log(client.encrypted);
                });

                client.setEncoding('utf8');
                let data = '';
                client.on('data', (chunk: string) => {
                    data += chunk;
                });
                client.on('end', () => {
                    console.log('received: ' + data);
                    client.destroy();
                    if (serverSocket) serverSocket.destroy();
                    server.close();
                });
            });
            """;

        var files = new Dictionary<string, string> { ["./main.ts"] = source };
        string? output = null;
        var task = Task.Run(() => output = TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            Assert.Fail("TLS handshake test timed out");
        }
        Assert.Contains("secure connection established", output!);
        Assert.Contains("true", output!);
        Assert.Contains("received: hello from server", output!);
    }

    [Fact]
    public async Task TlsRejectUnauthorized_Interpreted()
    {
        // Generate self-signed cert
        var (certPem, keyPem) = GenerateSelfSignedCert();

        var source = $$"""
            import * as tls from 'tls';

            const options = {
                key: `{{keyPem}}`,
                cert: `{{certPem}}`
            };

            const server = tls.createServer(options, (socket: any) => {
                socket.destroy();
            });

            server.listen(0, () => {
                const addr = server.address();
                const client = tls.connect({
                    port: addr.port,
                    host: '127.0.0.1',
                    rejectUnauthorized: true
                });

                client.on('error', (err: any) => {
                    console.log('error caught');
                    server.close();
                });

                client.on('secureConnect', () => {
                    console.log('should not reach here');
                    client.destroy();
                    server.close();
                });
            });
            """;

        var files = new Dictionary<string, string> { ["./main.ts"] = source };
        string? output = null;
        var task = Task.Run(() => output = TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted));
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            Assert.Fail($"TLS reject test timed out. Partial output: [{output ?? "null"}]");
        }
        Assert.Contains("error caught", output!);
    }

    /// <summary>
    /// Generates a self-signed X509 certificate for testing.
    /// Returns (certPem, keyPem) tuple.
    /// </summary>
    private static (string certPem, string keyPem) GenerateSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var certReq = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=localhost",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1
        );

        // Add SAN for localhost
        var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        certReq.CertificateExtensions.Add(sanBuilder.Build());

        var cert = certReq.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365)
        );

        var certPem = cert.ExportCertificatePem();
        var keyPem = rsa.ExportPkcs8PrivateKeyPem();

        // Escape backticks for JS template literals
        return (certPem.Replace("`", "\\`"), keyPem.Replace("`", "\\`"));
    }

    #region ALPN and SNI Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsSocket_Servername_Property(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const socket = tls.TLSSocket();
                console.log(socket.servername === undefined);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("true", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsConnect_ALPNProtocols(ExecutionMode mode)
    {
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key, ALPNProtocols: ['h2', 'http/1.1'] }, (socket: any) => {
                    console.log('alpn:' + socket.alpnProtocol);
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', {
                        rejectUnauthorized: false,
                        ALPNProtocols: ['h2', 'http/1.1']
                    }, () => {
                        client.end();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("alpn:h2", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsServer_ALPNProtocols(ExecutionMode mode)
    {
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key, ALPNProtocols: ['http/1.1'] }, (socket: any) => {
                    console.log('server-alpn:' + socket.alpnProtocol);
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', {
                        rejectUnauthorized: false,
                        ALPNProtocols: ['h2', 'http/1.1']
                    }, () => {
                        client.end();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("server-alpn:http/1.1", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsClient_AlpnProtocol_Exposed(ExecutionMode mode)
    {
        // The client sends ALPNProtocols and exposes the negotiated client.alpnProtocol — both modes.
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key, ALPNProtocols: ['h2', 'http/1.1'] }, (socket: any) => {
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', {
                        rejectUnauthorized: false,
                        ALPNProtocols: ['h2', 'http/1.1']
                    }, () => {
                        console.log('client-alpn:' + client.alpnProtocol);
                        client.end();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("client-alpn:h2", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsServer_SNICallback_Accepted(ExecutionMode mode)
    {
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({
                    cert, key,
                    SNICallback: (hostname: string) => {
                        return { cert, key };
                    }
                });
                console.log(typeof server.listen === 'function');
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("true", output);
    }

    #endregion

    #region rejectUnauthorized parity (#1040)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsRejectUnauthorized_Parity(ExecutionMode mode)
    {
        // rejectUnauthorized:true against a self-signed peer fails the handshake ('error'),
        // identically in interp and compiled.
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key }, (socket: any) => { socket.destroy(); });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', { rejectUnauthorized: true });
                    client.on('error', () => { console.log('rejected'); server.close(); });
                    client.on('secureConnect', () => { console.log('should-not-connect'); client.destroy(); server.close(); });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("rejected\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsRoundTrip_ServerToClientData_Parity(ExecutionMode mode)
    {
        // Full client↔server round trip: server writes, client receives, identical in both modes.
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key }, (socket: any) => {
                    socket.write('hello from server');
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect({ port: addr.port, host: '127.0.0.1', rejectUnauthorized: false });
                    client.setEncoding('utf8');
                    let data = '';
                    client.on('data', (c: string) => { data += c; });
                    client.on('end', () => { console.log('received: ' + data); client.destroy(); });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("received: hello from server\n", output);
    }

    #endregion

    #region Introspection parity (#1034)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsSocket_Introspection_Parity(ExecutionMode mode)
    {
        // After a real handshake, getCipher/getProtocol/getPeerCertificate/authorized/encrypted
        // must read the negotiated SslStream identically in interp and compiled mode.
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key }, (socket: any) => {
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', { rejectUnauthorized: false }, () => {
                        const cipher = client.getCipher();
                        console.log('cipher-name:' + (cipher !== null && typeof cipher.name === 'string' && cipher.name.length > 0));
                        console.log('version-ok:' + (cipher !== null && (cipher.version as string).indexOf('TLSv1.') === 0));
                        console.log('proto-ok:' + ((client.getProtocol() as string).indexOf('TLSv1.') === 0));
                        const pc = client.getPeerCertificate();
                        console.log('peer-localhost:' + (pc.subject.indexOf('localhost') >= 0));
                        // Self-signed peer with rejectUnauthorized:false ⇒ authorized=false + a reason (Node semantics).
                        console.log('authorized:' + client.authorized);
                        console.log('authError:' + (typeof client.authorizationError === 'string' && client.authorizationError.length > 0));
                        console.log('encrypted:' + client.encrypted);
                        client.end();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "cipher-name:true\nversion-ok:true\nproto-ok:true\npeer-localhost:true\nauthorized:false\nauthError:true\nencrypted:true\n",
            output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsSocket_PeerCertSubjectAltName_Parity(ExecutionMode mode)
    {
        // getPeerCertificate().subjectaltname is formatted "DNS:…, IP Address:…" identically in
        // both modes, and is consumable by tls.checkServerIdentity.
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key }, (socket: any) => {
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', { rejectUnauthorized: false }, () => {
                        const pc = client.getPeerCertificate();
                        console.log('san-dns:' + (pc.subjectaltname.indexOf('DNS:localhost') >= 0));
                        console.log('identity-ok:' + (tls.checkServerIdentity('localhost', pc) === undefined));
                        client.end();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("san-dns:true\nidentity-ok:true\n", output);
    }

    #endregion

    #region I/O + renegotiate parity (#1035)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TlsSocket_WriteEchoAndClose_Parity(ExecutionMode mode)
    {
        // Bidirectional write-through over the negotiated SslStream + close lifecycle,
        // plus renegotiate() returning the socket — identical interp == compiled.
        var (certPem, keyPem) = GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const server = tls.createServer({ cert, key }, (socket: any) => {
                    socket.setEncoding('utf8');
                    socket.on('data', (d: string) => { socket.write('echo:' + d); });
                    socket.on('end', () => { socket.end(); });
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', { rejectUnauthorized: false }, () => {
                        console.log('reneg-self:' + (client.renegotiate({}, () => {}) === client));
                        client.write('ping');
                        client.end();
                    });
                    client.setEncoding('utf8');
                    let recv = '';
                    client.on('data', (d: string) => { recv += d; });
                    client.on('close', () => {
                        console.log('recv:' + recv);
                        console.log('closed:true');
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("reneg-self:true\nrecv:echo:ping\nclosed:true\n", output);
    }

    #endregion
}
