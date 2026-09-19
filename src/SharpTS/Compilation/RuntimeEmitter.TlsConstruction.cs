using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // These immutable construction values exist only within one EmitAll call.
    private sealed record TlsSocketFields(
        FieldBuilder SslStream,
        FieldBuilder Authorized,
        FieldBuilder AuthError,
        FieldBuilder AlpnProtocol,
        FieldBuilder Servername,
        FieldBuilder PeerCert);

    private sealed record TlsServerFields(
        FieldBuilder IsListening,
        FieldBuilder Callback,
        FieldBuilder Cert,
        FieldBuilder Key,
        FieldBuilder RequestCert,
        FieldBuilder Listener,
        FieldBuilder Port,
        FieldBuilder Alpn);

    private sealed record TlsSocketHelpers(
        MethodBuilder BuildAlpnList, MethodBuilder AlpnString,
        MethodBuilder LoadCert, MethodBuilder DescribeErrors);
    private sealed record TlsSocketConstruction(TlsSocketFields Fields, TlsSocketHelpers Helpers);
    private sealed record TlsServerConstruction(TlsServerFields Fields, MethodBuilder AcceptWorker);
    private sealed record TlsConstruction(TlsSocketConstruction Socket, TlsServerConstruction Server);

    private readonly record struct TlsClosureMethods(ConstructorBuilder Constructor, MethodBuilder Run);
    private sealed record TlsAcceptClosures(TlsClosureMethods Accept, TlsClosureMethods Error);
    private readonly record struct TlsConnectConstruction(ConstructorBuilder Constructor, MethodBuilder Connect);

    private static TlsConstruction RequireTlsConstruction(TlsConstruction? construction)
        => construction ?? throw new InvalidOperationException("TLS construction requires the TLS feature declarations.");
}
