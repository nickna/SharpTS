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
    private void EmitDnsResolverFactoryMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.DnsResolverFactory = EmitReflectionHelper(typeBuilder, "DnsCreateResolverState", 0);
        runtime.DnsResolverSetServers = EmitReflectionHelper(typeBuilder, "DnsResolverSetServers", 2);
        runtime.DnsResolverGetServers = EmitReflectionHelper(typeBuilder, "DnsResolverGetServers", 1);
        runtime.DnsResolverCancel = EmitReflectionHelper(typeBuilder, "DnsResolverCancel", 1);
        runtime.DnsResolverGetGeneration = EmitReflectionHelper(typeBuilder, "DnsResolverGetGeneration", 1);
        runtime.DnsResolverSetLocalAddress = EmitReflectionHelper(typeBuilder, "DnsResolverSetLocalAddress", 3);
        runtime.DnsResolverResolve = EmitReflectionHelper(typeBuilder, "DnsResolverResolve", 1);
    }
}
