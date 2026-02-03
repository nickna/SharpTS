// =============================================================================
// MIGRATION NOTICE
// =============================================================================
// All import alias tests have been migrated to SharedTests/ImportAliasTests.cs
// to run against both interpreter and compiler execution modes.
//
// The shared tests cover:
// - Basic function and variable aliases
// - Class aliases (with instantiation)
// - Nested namespace paths
// - Interface aliases (type-only)
// - Enum aliases
// - Import aliases inside namespaces
// - Exported import aliases
// - Multiple aliases
// - Aliasing nested namespaces
// - Generic class aliases
//
// All 12 tests pass in both interpreter and compiler modes.
//
// Error case tests remain in this file for interpreter-only execution:
// - Error_InvalidNamespace
// - Error_InvalidMember
// - Error_IntermediateNotNamespace
//
// See: SharpTS.Tests/SharedTests/ImportAliasTests.cs
// =============================================================================

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ImportAliasTests
{
    [Fact]
    public void Error_InvalidNamespace()
    {
        var code = @"
            import X = NonExistent.Member;
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(code));
        Assert.Contains("Namespace 'NonExistent' is not defined", ex.Message);
    }

    [Fact]
    public void Error_InvalidMember()
    {
        var code = @"
            namespace NS {
                export const x: number = 1;
            }
            import y = NS.nonexistent;
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(code));
        Assert.Contains("does not exist in namespace", ex.Message);
    }

    [Fact]
    public void Error_IntermediateNotNamespace()
    {
        var code = @"
            namespace NS {
                export const x: number = 1;
            }
            import y = NS.x.z;
        ";
        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(code));
        Assert.Contains("not a namespace", ex.Message);
    }
}
