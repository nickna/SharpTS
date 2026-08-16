using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Defines a test collection for timer tests.
/// Tests in this collection run sequentially (not in parallel) to avoid
/// race conditions caused by the timer implementation's use of async callbacks
/// that can interfere with concurrent test execution.
/// </summary>
[CollectionDefinition("TimerTests", DisableParallelization = true)]
public class TimerTestsCollection
{
}
