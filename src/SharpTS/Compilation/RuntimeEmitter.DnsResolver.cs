using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the narrow late-bound state seam used by the TypeScript dns.Resolver
/// facade. Query callback shaping and scheduling stay in stdlib/node/dns.ts.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits resolver state/configuration helpers plus the synchronous reflection
    /// target that the shared DNS async runner wraps as a Promise.
    /// </summary>
    private void EmitDnsResolverFactoryMethod(TypeBuilder typeBuilder, EmittedDnsRuntime dns)
    {
        dns.ResolverFactory = EmitReflectionHelper(typeBuilder, "DnsCreateResolverState", 0);
        dns.ResolverSetServers = EmitReflectionHelper(typeBuilder, "DnsResolverSetServers", 2);
        dns.ResolverGetServers = EmitReflectionHelper(typeBuilder, "DnsResolverGetServers", 1);
        dns.ResolverCancel = EmitReflectionHelper(typeBuilder, "DnsResolverCancel", 1);
        dns.ResolverGetGeneration = EmitReflectionHelper(typeBuilder, "DnsResolverGetGeneration", 1);
        dns.ResolverSetLocalAddress = EmitReflectionHelper(typeBuilder, "DnsResolverSetLocalAddress", 3);
        dns.ResolverResolve = EmitReflectionHelper(typeBuilder, "DnsResolverResolve", 1);
    }
}
