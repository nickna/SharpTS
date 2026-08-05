using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

// Split out of BuiltInModuleTypes.cs (#1143): dns/net/tls/http/dgram and stream module type signatures.
public static partial class BuiltInModuleTypes
{
    /// <summary>
    /// Gets the exported types for the dns module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetDnsModuleTypes()
    {
        var numberType = TypeInfo.Primitive.Number;
        var stringType = TypeInfo.String.Shared;
        var anyType = TypeInfo.Any.Shared;

        // Result type for lookup: { address: string, family: number }
        var lookupResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["address"] = stringType,
            ["family"] = numberType
        }.ToFrozenDictionary());

        // Result type for lookupService: { hostname: string, service: string }
        var lookupServiceResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["hostname"] = stringType,
            ["service"] = stringType
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            // dns.lookup(hostname[, options][, callback]) -> { address, family }
            // (sync direct-return form when no callback; Node form invokes the callback)
            ["lookup"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                lookupResultType,
                RequiredParams: 1
            ),

            // dns.lookupService(address, port[, callback]) -> { hostname, service }
            ["lookupService"] = new TypeInfo.Function(
                [stringType, numberType, anyType],
                lookupServiceResultType,
                RequiredParams: 2
            ),

            // Async callback-based methods
            // dns.resolve(hostname[, rrtype], callback) -> void
            ["resolve"] = new TypeInfo.Function(
                [stringType, anyType, anyType],
                TypeInfo.Void.Shared,
                RequiredParams: 2),
            // dns.resolve4(hostname, callback) -> void
            ["resolve4"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolve6(hostname, callback) -> void
            ["resolve6"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.reverse(ip, callback) -> void
            ["reverse"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveMx(hostname, callback) -> void
            ["resolveMx"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveTxt(hostname, callback) -> void
            ["resolveTxt"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveSrv(hostname, callback) -> void
            ["resolveSrv"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveCname(hostname, callback) -> void
            ["resolveCname"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveNs(hostname, callback) -> void
            ["resolveNs"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveSoa(hostname, callback) -> void
            ["resolveSoa"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolvePtr(hostname, callback) -> void
            ["resolvePtr"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveCaa(hostname, callback) -> void
            ["resolveCaa"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),
            // dns.resolveNaptr(hostname, callback) -> void
            ["resolveNaptr"] = new TypeInfo.Function(
                [stringType, anyType],
                TypeInfo.Void.Shared),

            // dns.Resolver class constructor
            ["Resolver"] = new TypeInfo.Function([], anyType, RequiredParams: 0),

            // Default lookup result order (#1072)
            ["setDefaultResultOrder"] = new TypeInfo.Function([stringType], TypeInfo.Void.Shared),
            ["getDefaultResultOrder"] = new TypeInfo.Function([], stringType),

            // dns.promises sub-module
            ["promises"] = anyType,

            // Constants
            ["ADDRCONFIG"] = numberType,
            ["V4MAPPED"] = numberType,
            ["ALL"] = numberType,
            ["NODATA"] = stringType,
            ["FORMERR"] = stringType,
            ["SERVFAIL"] = stringType,
            ["NOTFOUND"] = stringType,
            ["NOTIMP"] = stringType,
            ["REFUSED"] = stringType,
            ["BADQUERY"] = stringType,
            ["BADNAME"] = stringType,
            ["BADFAMILY"] = stringType,
            ["BADRESP"] = stringType,
            ["CONNREFUSED"] = stringType,
            ["TIMEOUT"] = stringType,
            ["EOF"] = stringType,
            ["FILE"] = stringType,
            ["NOMEM"] = stringType,
            ["DESTRUCTION"] = stringType,
            ["BADSTR"] = stringType,
            ["BADFLAGS"] = stringType,
            ["NONAME"] = stringType,
            ["BADHINTS"] = stringType,
            ["NOTINITIALIZED"] = stringType,
            ["LOADIPHLPAPI"] = stringType,
            ["ADDRGETNETWORKPARAMS"] = stringType,
            ["CANCELLED"] = stringType
        };
    }
    /// <summary>
    /// Gets the exported types for the dns/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetDnsPromisesModuleTypes()
    {
        var stringType = TypeInfo.String.Shared;
        var anyType = TypeInfo.Any.Shared;
        var numberType = TypeInfo.Primitive.Number;

        var lookupResultType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["address"] = stringType,
            ["family"] = numberType
        }.ToFrozenDictionary());

        var stringArrayType = new TypeInfo.Array(stringType);

        return new Dictionary<string, TypeInfo>
        {
            ["lookup"] = new TypeInfo.Function(
                [stringType, anyType],
                new TypeInfo.Promise(lookupResultType),
                RequiredParams: 1),
            ["resolve"] = new TypeInfo.Function(
                [stringType, stringType],
                new TypeInfo.Promise(stringArrayType),
                RequiredParams: 1),
            ["resolve4"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolve6"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["reverse"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveMx"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveTxt"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveSrv"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveCname"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveNs"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveSoa"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolvePtr"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(stringArrayType)),
            ["resolveCaa"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["resolveNaptr"] = new TypeInfo.Function(
                [stringType],
                new TypeInfo.Promise(anyType)),
            ["setDefaultResultOrder"] = new TypeInfo.Function([stringType], TypeInfo.Void.Shared),
            ["getDefaultResultOrder"] = new TypeInfo.Function([], stringType)
        };
    }
    /// <summary>
    /// Gets the exported types for the net module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetNetModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var stringType = TypeInfo.String.Shared;
        var numberType = TypeInfo.Primitive.Number;
        var voidType = TypeInfo.Void.Shared;
        var booleanType = BooleanType;

        // EventEmitter methods shared by Server and Socket
        var eventEmitterMembers = new Dictionary<string, TypeInfo>
        {
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["emit"] = new TypeInfo.Function([stringType, anyType], booleanType, RequiredParams: 1, HasRestParam: true),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType)
        };

        // Socket type
        var socketMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            ["connect"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["write"] = new TypeInfo.Function([anyType, anyType, anyType], booleanType, RequiredParams: 1),
            ["end"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["destroy"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["setEncoding"] = new TypeInfo.Function([stringType], anyType),
            ["setTimeout"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 1),
            ["setNoDelay"] = new TypeInfo.Function([booleanType], anyType, RequiredParams: 0),
            ["setKeepAlive"] = new TypeInfo.Function([booleanType, numberType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["pipe"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["remoteAddress"] = stringType,
            ["remotePort"] = numberType,
            ["remoteFamily"] = stringType,
            ["localAddress"] = stringType,
            ["localPort"] = numberType,
            ["bytesRead"] = numberType,
            ["bytesWritten"] = numberType,
            ["connecting"] = booleanType,
            ["destroyed"] = booleanType,
            ["readyState"] = stringType,
            ["writableLength"] = numberType,
            ["writableHighWaterMark"] = numberType,
            ["writableNeedDrain"] = booleanType,
            ["localFamily"] = stringType,
            ["pending"] = booleanType,
            ["allowHalfOpen"] = booleanType
        };
        var socketType = new TypeInfo.Record(socketMembers.ToFrozenDictionary());

        // Server type
        var serverMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            ["listen"] = new TypeInfo.Function([anyType, anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["close"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["getConnections"] = new TypeInfo.Function([anyType], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["listening"] = booleanType,
            ["maxConnections"] = numberType
        };
        var serverType = new TypeInfo.Record(serverMembers.ToFrozenDictionary());

        // net.SocketAddress instance type (#1069)
        var socketAddressType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["address"] = stringType,
            ["family"] = stringType,
            ["port"] = numberType,
            ["flowlabel"] = numberType,
            ["toJSON"] = new TypeInfo.Function([], anyType)
        }.ToFrozenDictionary());

        // net.BlockList instance type (#1069)
        var blockListType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["addAddress"] = new TypeInfo.Function([anyType, stringType], voidType, RequiredParams: 1),
            ["addRange"] = new TypeInfo.Function([anyType, anyType, stringType], voidType, RequiredParams: 2),
            ["addSubnet"] = new TypeInfo.Function([anyType, numberType, stringType], voidType, RequiredParams: 2),
            ["check"] = new TypeInfo.Function([anyType, stringType], booleanType, RequiredParams: 1),
            ["rules"] = new TypeInfo.Array(stringType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createServer"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            // connect(options|port|path[, host][, connectListener]) — three positional args
            ["createConnection"] = new TypeInfo.Function([anyType, anyType, anyType], socketType, RequiredParams: 1),
            ["connect"] = new TypeInfo.Function([anyType, anyType, anyType], socketType, RequiredParams: 1),
            ["isIP"] = new TypeInfo.Function([stringType], numberType),
            ["isIPv4"] = new TypeInfo.Function([stringType], booleanType),
            ["isIPv6"] = new TypeInfo.Function([stringType], booleanType),
            ["Server"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            ["Socket"] = new TypeInfo.Function([anyType], socketType, RequiredParams: 0),
            ["BlockList"] = new TypeInfo.Function([], blockListType, RequiredParams: 0),
            ["SocketAddress"] = new TypeInfo.Function([anyType], socketAddressType, RequiredParams: 0),
            ["getDefaultAutoSelectFamily"] = new TypeInfo.Function([], booleanType),
            ["setDefaultAutoSelectFamily"] = new TypeInfo.Function([booleanType], voidType),
            ["getDefaultAutoSelectFamilyAttemptTimeout"] = new TypeInfo.Function([], numberType),
            ["setDefaultAutoSelectFamilyAttemptTimeout"] = new TypeInfo.Function([numberType], voidType)
        };
    }
    /// <summary>
    /// Gets the exported types for the tls module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetTlsModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var stringType = TypeInfo.String.Shared;
        var numberType = TypeInfo.Primitive.Number;
        var booleanType = BooleanType;

        // EventEmitter methods shared by Server and Socket
        var eventEmitterMembers = new Dictionary<string, TypeInfo>
        {
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["emit"] = new TypeInfo.Function([stringType, anyType], booleanType, RequiredParams: 1, HasRestParam: true),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType)
        };

        // TLSSocket type - extends Socket with TLS-specific members
        var tlsSocketMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            // Inherited Socket methods
            ["connect"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["write"] = new TypeInfo.Function([anyType, anyType, anyType], booleanType, RequiredParams: 1),
            ["end"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["destroy"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["setEncoding"] = new TypeInfo.Function([stringType], anyType),
            ["setTimeout"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 1),
            ["setNoDelay"] = new TypeInfo.Function([booleanType], anyType, RequiredParams: 0),
            ["setKeepAlive"] = new TypeInfo.Function([booleanType, numberType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["pipe"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["remoteAddress"] = stringType,
            ["remotePort"] = numberType,
            ["remoteFamily"] = stringType,
            ["localAddress"] = stringType,
            ["localPort"] = numberType,
            ["bytesRead"] = numberType,
            ["bytesWritten"] = numberType,
            ["connecting"] = booleanType,
            ["destroyed"] = booleanType,
            ["readyState"] = stringType,
            // TLS-specific properties
            ["authorized"] = booleanType,
            ["authorizationError"] = new TypeInfo.Union([stringType, TypeInfo.Null.Shared]),
            ["encrypted"] = booleanType,
            ["alpnProtocol"] = new TypeInfo.Union([stringType, TypeInfo.Null.Shared]),
            ["servername"] = new TypeInfo.Union([stringType, TypeInfo.Undefined.Shared]),
            // TLS-specific methods
            ["getCipher"] = new TypeInfo.Function([], anyType),
            ["getPeerCertificate"] = new TypeInfo.Function([booleanType], anyType, RequiredParams: 0),
            ["getProtocol"] = new TypeInfo.Function([], new TypeInfo.Union([stringType, TypeInfo.Null.Shared])),
            ["renegotiate"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 0),
            // Advanced TLS APIs (throw "not supported" on this runtime — see #1032 SslStream ceilings)
            ["getSession"] = new TypeInfo.Function([], anyType),
            ["setSession"] = new TypeInfo.Function([anyType], anyType),
            ["getTLSTicket"] = new TypeInfo.Function([], anyType),
            ["getPeerFinished"] = new TypeInfo.Function([], anyType),
            ["getFinished"] = new TypeInfo.Function([], anyType),
            ["setMaxSendFragment"] = new TypeInfo.Function([numberType], anyType),
            ["exportKeyingMaterial"] = new TypeInfo.Function([numberType, stringType, anyType], anyType, RequiredParams: 2)
        };
        var tlsSocketType = new TypeInfo.Record(tlsSocketMembers.ToFrozenDictionary());

        // TLS Server type
        var serverMembers = new Dictionary<string, TypeInfo>(eventEmitterMembers)
        {
            ["listen"] = new TypeInfo.Function([anyType, anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["close"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["getConnections"] = new TypeInfo.Function([anyType], anyType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["listening"] = booleanType,
            ["maxConnections"] = numberType
        };
        var serverType = new TypeInfo.Record(serverMembers.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createServer"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            ["connect"] = new TypeInfo.Function([anyType, anyType, anyType, anyType], tlsSocketType, RequiredParams: 1),
            ["createSecureContext"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            // checkServerIdentity(host, cert) → Error | undefined
            ["checkServerIdentity"] = new TypeInfo.Function([stringType, anyType],
                new TypeInfo.Union([anyType, TypeInfo.Undefined.Shared])),
            ["getCiphers"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["rootCertificates"] = new TypeInfo.Array(stringType),
            ["Server"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            ["TLSSocket"] = new TypeInfo.Function([anyType], tlsSocketType, RequiredParams: 0),
            ["DEFAULT_MIN_VERSION"] = stringType,
            ["DEFAULT_MAX_VERSION"] = stringType
        };
    }
    /// <summary>
    /// Gets the exported types for the http module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetHttpModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var stringType = TypeInfo.String.Shared;
        var numberType = TypeInfo.Primitive.Number;
        var voidType = TypeInfo.Void.Shared;
        var callbackType = new TypeInfo.Function([anyType, anyType], voidType);

        // Server type with full EventEmitter support
        var serverType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // Server-specific methods
            ["listen"] = new TypeInfo.Function([numberType, anyType, anyType], anyType, RequiredParams: 1),
            ["close"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["listening"] = TypeInfo.Primitive.Boolean,

            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["emit"] = new TypeInfo.Function([stringType, anyType], BooleanType, RequiredParams: 1, HasRestParam: true),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["rawListeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["prependListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["prependOnceListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType),

            // Server lifecycle (#1045)
            ["closeAllConnections"] = new TypeInfo.Function([], voidType),
            ["closeIdleConnections"] = new TypeInfo.Function([], voidType),
            ["setTimeout"] = new TypeInfo.Function([numberType, anyType], anyType, RequiredParams: 0),
            ["keepAliveTimeout"] = numberType,
            ["headersTimeout"] = numberType,
            ["requestTimeout"] = numberType,
            ["timeout"] = numberType,
            ["maxHeadersCount"] = numberType,
            ["maxRequestsPerSocket"] = numberType
        }.ToFrozenDictionary());

        // STATUS_CODES type - with string index signature for dynamic property access
        var statusCodesType = new TypeInfo.Record(
            new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            StringIndexType: stringType  // Allow any string key to return a string
        );

        // METHODS type - array of strings
        var methodsType = new TypeInfo.Array(stringType);

        // Agent type with full API surface
        var boolType = TypeInfo.Primitive.Boolean;
        var agentType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["keepAlive"] = boolType,
            ["keepAliveMsecs"] = numberType,
            ["maxSockets"] = numberType,
            ["maxTotalSockets"] = numberType,
            ["maxFreeSockets"] = numberType,
            ["timeout"] = numberType,
            ["scheduling"] = stringType,
            ["sockets"] = anyType,
            ["freeSockets"] = anyType,
            ["requests"] = anyType,
            ["destroy"] = new TypeInfo.Function([], voidType),
            ["getName"] = new TypeInfo.Function([anyType], stringType, RequiredParams: 0),
            ["createConnection"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, RequiredParams: 1, HasRestParam: true),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0)
        }.ToFrozenDictionary());

        // Agent constructor type
        var agentConstructorType = new TypeInfo.Function([anyType], agentType, RequiredParams: 0);

        return new Dictionary<string, TypeInfo>
        {
            // createServer accepts an optional options object (https TLS opts: key/cert/ca/pfx/
            // passphrase/SNICallback/ALPNProtocols, #1049) and/or a (req,res) handler.
            ["createServer"] = new TypeInfo.Function([anyType, anyType], serverType, RequiredParams: 0),
            // request/get return a ClientRequest (#1043) typed as any so the writable + event
            // surface (write/end/setHeader/on('response')/...) type-checks without over-constraining.
            ["request"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 1),
            ["get"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 1),
            ["METHODS"] = methodsType,
            ["STATUS_CODES"] = statusCodesType,
            ["globalAgent"] = agentType,
            ["Agent"] = agentConstructorType,
            // Utilities + constants (#1052)
            ["validateHeaderName"] = new TypeInfo.Function([stringType, stringType], voidType, RequiredParams: 1),
            ["validateHeaderValue"] = new TypeInfo.Function([stringType, anyType], voidType, RequiredParams: 2),
            ["maxHeaderSize"] = numberType,
            ["setMaxIdleHTTPParsers"] = new TypeInfo.Function([numberType], voidType, RequiredParams: 0)
        };
    }
    /// <summary>
    /// Gets the exported types for the stream module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetStreamModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var stringType = TypeInfo.String.Shared;
        var boolType = TypeInfo.Primitive.Boolean;
        var numberType = TypeInfo.Primitive.Number;
        var voidType = TypeInfo.Void.Shared;

        // Stream instance type (shared members for all stream types)
        var streamInstanceType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, HasRestParam: true),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),
            ["setMaxListeners"] = new TypeInfo.Function([numberType], anyType),
            ["getMaxListeners"] = new TypeInfo.Function([], numberType),

            // Readable methods
            ["read"] = new TypeInfo.Function([numberType], anyType, RequiredParams: 0),
            ["push"] = new TypeInfo.Function([anyType], boolType),
            ["pipe"] = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 1),
            ["unpipe"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["setEncoding"] = new TypeInfo.Function([stringType], anyType),
            ["destroy"] = new TypeInfo.Function([anyType], anyType, RequiredParams: 0),
            ["unshift"] = new TypeInfo.Function([anyType], anyType),
            ["pause"] = new TypeInfo.Function([], anyType),
            ["resume"] = new TypeInfo.Function([], anyType),
            ["isPaused"] = new TypeInfo.Function([], boolType),

            // Readable properties
            ["readable"] = boolType,
            ["readableEnded"] = boolType,
            ["readableLength"] = numberType,
            ["readableHighWaterMark"] = numberType,
            ["readableEncoding"] = stringType,
            ["readableFlowing"] = new TypeInfo.Union([boolType, TypeInfo.Null.Shared]),
            ["readableObjectMode"] = boolType,
            ["destroyed"] = boolType,

            // Writable methods
            ["write"] = new TypeInfo.Function([anyType, stringType, anyType], boolType, RequiredParams: 1),
            ["end"] = new TypeInfo.Function([anyType, stringType, anyType], anyType, RequiredParams: 0),
            ["cork"] = new TypeInfo.Function([], voidType),
            ["uncork"] = new TypeInfo.Function([], voidType),
            ["setDefaultEncoding"] = new TypeInfo.Function([stringType], anyType),

            // Writable properties
            ["writable"] = boolType,
            ["writableEnded"] = boolType,
            ["writableFinished"] = boolType,
            ["writableLength"] = numberType,
            ["writableCorked"] = numberType,
            ["writableHighWaterMark"] = numberType,
            ["writableObjectMode"] = boolType,

            // Stream path properties (for ReadStream/WriteStream)
            ["path"] = stringType,
            ["bytesRead"] = numberType,
            ["bytesWritten"] = numberType,

            // Stream utility methods
            ["toArray"] = new TypeInfo.Function([], new TypeInfo.Array(anyType)),
            ["forEach"] = new TypeInfo.Function([anyType], voidType),
            ["map"] = new TypeInfo.Function([anyType], anyType),
            ["filter"] = new TypeInfo.Function([anyType], anyType),

            // Async-iterator helpers (#1025): consuming helpers return Promises,
            // transform-returning helpers (drop/take/flatMap/asIndexedPairs) return a stream.
            ["reduce"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(anyType), RequiredParams: 1),
            ["some"] = new TypeInfo.Function([anyType], new TypeInfo.Promise(boolType)),
            ["every"] = new TypeInfo.Function([anyType], new TypeInfo.Promise(boolType)),
            ["find"] = new TypeInfo.Function([anyType], new TypeInfo.Promise(anyType)),
            ["flatMap"] = new TypeInfo.Function([anyType], anyType),
            ["drop"] = new TypeInfo.Function([numberType], anyType),
            ["take"] = new TypeInfo.Function([numberType], anyType),
            ["asIndexedPairs"] = new TypeInfo.Function([], anyType),

            // Async-iterable surface (#1024): `for await (const x of readable)`.
            ["@@asyncIterator"] = new TypeInfo.Function([], anyType)
        }.ToFrozenDictionary());

        // Readable constructor with static methods
        var readableConstructorType = new TypeInfo.Interface(
            Name: "Readable",
            Members: new Dictionary<string, TypeInfo>
            {
                ["from"] = new TypeInfo.Function([anyType, anyType], streamInstanceType, RequiredParams: 1),
                ["isReadable"] = new TypeInfo.Function([anyType], boolType),
                ["toWeb"] = new TypeInfo.Function([anyType], anyType),     // #1029
                ["fromWeb"] = new TypeInfo.Function([anyType], streamInstanceType) // #1029
            }.ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // Writable constructor with static methods
        var writableConstructorType = new TypeInfo.Interface(
            Name: "Writable",
            Members: new Dictionary<string, TypeInfo>
            {
                ["isWritable"] = new TypeInfo.Function([anyType], boolType)
            }.ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // Duplex constructor
        var duplexConstructorType = new TypeInfo.Interface(
            Name: "Duplex",
            Members: new Dictionary<string, TypeInfo>
            {
                ["from"] = new TypeInfo.Function([anyType, anyType], streamInstanceType, RequiredParams: 1)
            }.ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // Transform constructor
        var transformConstructorType = new TypeInfo.Interface(
            Name: "Transform",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // PassThrough constructor
        var passThroughConstructorType = new TypeInfo.Interface(
            Name: "PassThrough",
            Members: new Dictionary<string, TypeInfo>().ToFrozenDictionary(),
            OptionalMembers: FrozenSet<string>.Empty,
            ConstructorSignatures:
            [
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [],
                    ReturnType: streamInstanceType),
                new TypeInfo.ConstructorSignature(
                    TypeParams: null,
                    ParamTypes: [anyType],
                    ReturnType: streamInstanceType)
            ]
        );

        // finished function type
        var finishedType = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 1);

        // pipeline function type (rest params)
        var pipelineType = new TypeInfo.Function([anyType, anyType], anyType, RequiredParams: 2, HasRestParam: true);

        // addAbortSignal function type
        var addAbortSignalType = new TypeInfo.Function([anyType, anyType], anyType);

        // compose function type (#1028): compose(...streams) → Duplex
        var composeType = new TypeInfo.Function([anyType], streamInstanceType, RequiredParams: 1, HasRestParam: true);

        // #1030 statics
        var isErroredType = new TypeInfo.Function([anyType], boolType);
        var getDefaultHwmType = new TypeInfo.Function([boolType], numberType, RequiredParams: 0);
        var setDefaultHwmType = new TypeInfo.Function([boolType, numberType], voidType);

        // promises sub-module
        var promisesType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            ["pipeline"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 2, HasRestParam: true),
            ["finished"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 1)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["Readable"] = readableConstructorType,
            ["Writable"] = writableConstructorType,
            ["Duplex"] = duplexConstructorType,
            ["Transform"] = transformConstructorType,
            ["PassThrough"] = passThroughConstructorType,
            ["finished"] = finishedType,
            ["pipeline"] = pipelineType,
            ["addAbortSignal"] = addAbortSignalType,
            ["compose"] = composeType,
            ["isErrored"] = isErroredType,
            ["getDefaultHighWaterMark"] = getDefaultHwmType,
            ["setDefaultHighWaterMark"] = setDefaultHwmType,
            ["promises"] = promisesType
        };
    }
    /// <summary>
    /// Gets the exported types for the stream/promises module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetStreamPromisesModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var voidType = TypeInfo.Void.Shared;

        return new Dictionary<string, TypeInfo>
        {
            ["pipeline"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 2, HasRestParam: true),
            ["finished"] = new TypeInfo.Function([anyType, anyType], new TypeInfo.Promise(voidType), RequiredParams: 1)
        };
    }
    /// <summary>
    /// Gets the exported types for the <c>stream/web</c> module (WHATWG Web Streams).
    /// </summary>
    /// <remarks>
    /// All constructors are typed as <c>Any</c> — members are resolved
    /// dynamically at runtime. Matches the Headers/BroadcastChannel pattern.
    /// </remarks>
    public static Dictionary<string, TypeInfo> GetStreamWebModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        return new Dictionary<string, TypeInfo>
        {
            ["ReadableStream"] = anyType,
            ["WritableStream"] = anyType,
            ["TransformStream"] = anyType,
            ["ByteLengthQueuingStrategy"] = anyType,
            ["CountQueuingStrategy"] = anyType,
            ["ReadableStreamDefaultReader"] = anyType,
            ["ReadableStreamDefaultController"] = anyType,
            ["WritableStreamDefaultWriter"] = anyType,
            ["WritableStreamDefaultController"] = anyType,
            ["TransformStreamDefaultController"] = anyType,
        };
    }
    /// <summary>
    /// Gets the exported types for the dgram module.
    /// </summary>
    public static Dictionary<string, TypeInfo> GetDgramModuleTypes()
    {
        var anyType = TypeInfo.Any.Shared;
        var stringType = TypeInfo.String.Shared;
        var voidType = TypeInfo.Void.Shared;
        var numberType = TypeInfo.Primitive.Number;
        var boolType = TypeInfo.Primitive.Boolean;

        // Socket instance type (extends EventEmitter)
        var socketType = new TypeInfo.Record(new Dictionary<string, TypeInfo>
        {
            // EventEmitter methods
            ["on"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["once"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["emit"] = new TypeInfo.Function([stringType, anyType], boolType, RequiredParams: 1, HasRestParam: true),
            ["off"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["removeAllListeners"] = new TypeInfo.Function([stringType], anyType, RequiredParams: 0),
            ["addListener"] = new TypeInfo.Function([stringType, anyType], anyType),
            ["listeners"] = new TypeInfo.Function([stringType], new TypeInfo.Array(anyType)),
            ["listenerCount"] = new TypeInfo.Function([stringType], numberType),
            ["eventNames"] = new TypeInfo.Function([], new TypeInfo.Array(stringType)),

            // Socket methods
            ["bind"] = new TypeInfo.Function([anyType, anyType, anyType], anyType, RequiredParams: 0),
            ["send"] = new TypeInfo.Function([anyType, anyType, anyType, anyType, anyType, anyType], anyType, RequiredParams: 1),
            ["close"] = new TypeInfo.Function([anyType], voidType, RequiredParams: 0),
            ["address"] = new TypeInfo.Function([], anyType),
            ["setBroadcast"] = new TypeInfo.Function([boolType], voidType),
            ["setTTL"] = new TypeInfo.Function([numberType], voidType),
            ["setMulticastTTL"] = new TypeInfo.Function([numberType], voidType),
            ["addMembership"] = new TypeInfo.Function([stringType, stringType], voidType, RequiredParams: 1),
            ["dropMembership"] = new TypeInfo.Function([stringType, stringType], voidType, RequiredParams: 1),
            ["addSourceSpecificMembership"] = new TypeInfo.Function([stringType, stringType, stringType], voidType, RequiredParams: 2),
            ["dropSourceSpecificMembership"] = new TypeInfo.Function([stringType, stringType, stringType], voidType, RequiredParams: 2),
            ["setMulticastLoopback"] = new TypeInfo.Function([boolType], voidType),
            ["setMulticastInterface"] = new TypeInfo.Function([stringType], voidType),
            ["ref"] = new TypeInfo.Function([], anyType),
            ["unref"] = new TypeInfo.Function([], anyType),
            ["connect"] = new TypeInfo.Function([numberType, stringType, anyType], voidType, RequiredParams: 1),
            ["disconnect"] = new TypeInfo.Function([], voidType),
            ["remoteAddress"] = new TypeInfo.Function([], anyType),
            ["getRecvBufferSize"] = new TypeInfo.Function([], numberType),
            ["setRecvBufferSize"] = new TypeInfo.Function([numberType], voidType),
            ["getSendBufferSize"] = new TypeInfo.Function([], numberType),
            ["setSendBufferSize"] = new TypeInfo.Function([numberType], voidType)
        }.ToFrozenDictionary());

        return new Dictionary<string, TypeInfo>
        {
            ["createSocket"] = new TypeInfo.Function([anyType, anyType], socketType, RequiredParams: 1),
            ["Socket"] = new TypeInfo.Function([anyType, anyType], socketType, RequiredParams: 1)
        };
    }
}
