using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Command-line argument tests have been migrated to SharedTests/CommandLineArgumentTests.cs
/// to run in both interpreter and compiled modes.
///
/// Migrated tests:
/// - SliceTwo pattern (WithScriptArgs, JoinsCorrectly, EmptyArgs)
/// - Argument preservation (Spaces, ManyArguments, SpecialCharacters)
/// - Argv structure (Length, IndexOne)
///
/// The tests below remain interpreter-only because they test ProcessBuiltIns
/// internal state management that is specific to the interpreter infrastructure.
/// </summary>
[Collection("ScriptArgs")]
public class CommandLineArgumentTests
{
    [Fact]
    public void ProcessArgv_CacheInvalidation_AfterSetScriptArgs()
    {
        // First call sets some args
        ProcessBuiltIns.SetScriptArguments("/script1.ts", ["a", "b"]);
        try
        {
            var source1 = """
                console.log(process.argv.slice(2).join(','));
                """;
            var output1 = TestHarness.RunInterpreted(source1);
            Assert.Equal("a,b\n", output1);

            // Second call changes args - cache should be invalidated
            ProcessBuiltIns.SetScriptArguments("/script2.ts", ["x", "y", "z"]);
            var source2 = """
                console.log(process.argv.slice(2).join(','));
                """;
            var output2 = TestHarness.RunInterpreted(source2);
            Assert.Equal("x,y,z\n", output2);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }
}
