using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Defines the "ScriptArgs" collection. The two CommandLineArgumentTests classes
/// mutate process-level argv state (ProcessBuiltIns script args), so they must
/// not run concurrently with other collections reading process.argv.
/// </summary>
/// <remarks>
/// Until the 2026-07 cleanup this definition did not exist, so the
/// <c>[Collection("ScriptArgs")]</c> attributes bound to an implicit,
/// parallelizable collection — exactly the silent hazard the ClusterTests
/// collection documents.
/// </remarks>
[CollectionDefinition("ScriptArgs", DisableParallelization = true)]
public class ScriptArgsCollection
{
}
