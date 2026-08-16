using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Collection for tests that redirect DNS resolution through the
/// SHARPTS_DNS_SERVER / SHARPTS_DNS_TIMEOUT_MS environment variables. Those are
/// process-global, so the collection disables parallelization: nothing else may
/// run while a test has the resolver pointed at a loopback fake server, or
/// unrelated network tests would be redirected too.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DnsFakeServerEnvCollection
{
    public const string Name = "DnsFakeServerEnv";
}
