using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// TLS tests own real listeners and perform multiple asynchronous handshakes. Running them in
/// the non-parallel phase prevents unrelated CPU-heavy collections from delaying OS networking
/// while preserving aggressive parallelism for the rest of the suite.
/// </summary>
[CollectionDefinition("TlsTests", DisableParallelization = true)]
public class TlsTestsCollection
{
}
