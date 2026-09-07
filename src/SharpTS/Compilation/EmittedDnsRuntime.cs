using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// DNS metadata for one compilation. Handles become readable as they are declared,
/// before their bodies are emitted; completion freezes the component for consumers.
/// </summary>
public sealed class EmittedDnsRuntime
{
    internal EmittedDnsRuntime()
    {
        PromisesWrapperMethods = new System.Collections.ObjectModel.ReadOnlyDictionary<string, MethodBuilder>(_promiseWrappers);
    }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _lookup;
    public MethodBuilder Lookup
    {
        get => Require(_lookup);
        internal set => Set(ref _lookup, value);
    }

    private MethodBuilder? _getDefaultResultOrder;
    public MethodBuilder GetDefaultResultOrder
    {
        get => Require(_getDefaultResultOrder);
        internal set => Set(ref _getDefaultResultOrder, value);
    }

    private MethodBuilder? _setDefaultResultOrder;
    public MethodBuilder SetDefaultResultOrder
    {
        get => Require(_setDefaultResultOrder);
        internal set => Set(ref _setDefaultResultOrder, value);
    }

    private MethodBuilder? _lookupService;
    public MethodBuilder LookupService
    {
        get => Require(_lookupService);
        internal set => Set(ref _lookupService, value);
    }

    private MethodBuilder? _getLookup;
    public MethodBuilder GetLookup
    {
        get => Require(_getLookup);
        internal set => Set(ref _getLookup, value);
    }

    private MethodBuilder? _getLookupService;
    public MethodBuilder GetLookupService
    {
        get => Require(_getLookupService);
        internal set => Set(ref _getLookupService, value);
    }

    private MethodBuilder? _resolveRecord;
    public MethodBuilder ResolveRecord
    {
        get => Require(_resolveRecord);
        internal set => Set(ref _resolveRecord, value);
    }

    private MethodBuilder? _convertList;
    public MethodBuilder ConvertList
    {
        get => Require(_convertList);
        internal set => Set(ref _convertList, value);
    }

    private MethodBuilder? _doQuery;
    public MethodBuilder DoQuery
    {
        get => Require(_doQuery);
        internal set => Set(ref _doQuery, value);
    }

    private MethodBuilder? _getTimeoutMs;
    public MethodBuilder GetTimeoutMs
    {
        get => Require(_getTimeoutMs);
        internal set => Set(ref _getTimeoutMs, value);
    }

    private MethodBuilder? _parseServerEndpoint;
    public MethodBuilder ParseServerEndpoint
    {
        get => Require(_parseServerEndpoint);
        internal set => Set(ref _parseServerEndpoint, value);
    }

    private MethodBuilder? _getSystemDns;
    public MethodBuilder GetSystemDns
    {
        get => Require(_getSystemDns);
        internal set => Set(ref _getSystemDns, value);
    }

    private MethodBuilder? _buildQuery;
    public MethodBuilder BuildQuery
    {
        get => Require(_buildQuery);
        internal set => Set(ref _buildQuery, value);
    }

    private MethodBuilder? _sendReceive;
    public MethodBuilder SendReceive
    {
        get => Require(_sendReceive);
        internal set => Set(ref _sendReceive, value);
    }

    private MethodBuilder? _readName;
    public MethodBuilder ReadName
    {
        get => Require(_readName);
        internal set => Set(ref _readName, value);
    }

    private MethodBuilder? _skipName;
    public MethodBuilder SkipName
    {
        get => Require(_skipName);
        internal set => Set(ref _skipName, value);
    }

    private MethodBuilder? _parseResponse;
    public MethodBuilder ParseResponse
    {
        get => Require(_parseResponse);
        internal set => Set(ref _parseResponse, value);
    }

    private MethodBuilder? _readCharString;
    public MethodBuilder ReadCharString
    {
        get => Require(_readCharString);
        internal set => Set(ref _readCharString, value);
    }

    private MethodBuilder? _readUInt32;
    public MethodBuilder ReadUInt32
    {
        get => Require(_readUInt32);
        internal set => Set(ref _readUInt32, value);
    }

    private MethodBuilder? _readUInt16;
    public MethodBuilder ReadUInt16
    {
        get => Require(_readUInt16);
        internal set => Set(ref _readUInt16, value);
    }

    private MethodBuilder? _parseRecord;
    public MethodBuilder ParseRecord
    {
        get => Require(_parseRecord);
        internal set => Set(ref _parseRecord, value);
    }

    private MethodBuilder? _sendViaTcp;
    public MethodBuilder SendViaTcp
    {
        get => Require(_sendViaTcp);
        internal set => Set(ref _sendViaTcp, value);
    }

    private MethodBuilder? _readExact;
    public MethodBuilder ReadExact
    {
        get => Require(_readExact);
        internal set => Set(ref _readExact, value);
    }

    private MethodBuilder? _encodeName;
    public MethodBuilder EncodeName
    {
        get => Require(_encodeName);
        internal set => Set(ref _encodeName, value);
    }

    private MethodBuilder? _getPromisesNamespace;
    public MethodBuilder GetPromisesNamespace
    {
        get => Require(_getPromisesNamespace);
        internal set => Set(ref _getPromisesNamespace, value);
    }

    private MethodBuilder? _resolverFactory;
    public MethodBuilder ResolverFactory
    {
        get => Require(_resolverFactory);
        internal set => Set(ref _resolverFactory, value);
    }

    private MethodBuilder? _resolverSetServers;
    public MethodBuilder ResolverSetServers
    {
        get => Require(_resolverSetServers);
        internal set => Set(ref _resolverSetServers, value);
    }

    private MethodBuilder? _resolverGetServers;
    public MethodBuilder ResolverGetServers
    {
        get => Require(_resolverGetServers);
        internal set => Set(ref _resolverGetServers, value);
    }

    private MethodBuilder? _resolverCancel;
    public MethodBuilder ResolverCancel
    {
        get => Require(_resolverCancel);
        internal set => Set(ref _resolverCancel, value);
    }

    private MethodBuilder? _resolverGetGeneration;
    public MethodBuilder ResolverGetGeneration
    {
        get => Require(_resolverGetGeneration);
        internal set => Set(ref _resolverGetGeneration, value);
    }

    private MethodBuilder? _resolverSetLocalAddress;
    public MethodBuilder ResolverSetLocalAddress
    {
        get => Require(_resolverSetLocalAddress);
        internal set => Set(ref _resolverSetLocalAddress, value);
    }

    private MethodBuilder? _resolverResolve;
    public MethodBuilder ResolverResolve
    {
        get => Require(_resolverResolve);
        internal set => Set(ref _resolverResolve, value);
    }

    private readonly Dictionary<string, MethodBuilder> _promiseWrappers = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> PromisesWrapperMethods { get; }

    internal void RegisterPromiseWrapper(string name, MethodBuilder method)
    {
        EnsureMutable();
        _promiseWrappers.Add(name, method);
    }

    private static MethodBuilder Require(MethodBuilder? method, [CallerMemberName] string name = "") =>
        method ?? throw new InvalidOperationException($"DNS metadata '{name}' has not been declared.");

    private void Set(ref MethodBuilder? field, MethodBuilder value)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("DNS metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Lookup;
        _ = GetDefaultResultOrder;
        _ = SetDefaultResultOrder;
        _ = LookupService;
        _ = GetLookup;
        _ = GetLookupService;
        _ = ResolveRecord;
        _ = ConvertList;
        _ = DoQuery;
        _ = GetTimeoutMs;
        _ = ParseServerEndpoint;
        _ = GetSystemDns;
        _ = BuildQuery;
        _ = SendReceive;
        _ = ReadName;
        _ = SkipName;
        _ = ParseResponse;
        _ = ReadCharString;
        _ = ReadUInt32;
        _ = ReadUInt16;
        _ = ParseRecord;
        _ = SendViaTcp;
        _ = ReadExact;
        _ = EncodeName;
        _ = GetPromisesNamespace;
        _ = ResolverFactory;
        _ = ResolverSetServers;
        _ = ResolverGetServers;
        _ = ResolverCancel;
        _ = ResolverGetGeneration;
        _ = ResolverSetLocalAddress;
        _ = ResolverResolve;
        string[] requiredWrappers =
        [
            "DnsPromisesResolve4", "DnsPromisesResolve6", "DnsPromisesResolveMx",
            "DnsPromisesResolveTxt", "DnsPromisesResolveSrv", "DnsPromisesResolveCname",
            "DnsPromisesResolveNs", "DnsPromisesResolveSoa", "DnsPromisesResolvePtr",
            "DnsPromisesResolveCaa", "DnsPromisesResolveNaptr", "DnsPromisesLookup",
            "DnsPromisesLookupService", "DnsPromisesResolve", "DnsPromisesReverse",
            "DnsResolverResolveAsync"
        ];
        foreach (string name in requiredWrappers)
        {
            if (!_promiseWrappers.ContainsKey(name))
                throw new InvalidOperationException($"DNS promise wrapper '{name}' has not been declared.");
        }
        IsComplete = true;
    }
}
