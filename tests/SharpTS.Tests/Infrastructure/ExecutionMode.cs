namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Specifies the execution mode for running TypeScript tests.
/// Used to parameterize tests that should run against both interpreter and compiler.
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Execute via the tree-walking interpreter.
    /// </summary>
    Interpreted,

    /// <summary>
    /// Compile to .NET IL and execute the compiled assembly.
    /// </summary>
    Compiled
}

/// <summary>
/// Provides execution mode data for xUnit Theory tests.
/// </summary>
public static class ExecutionModes
{
    /// <summary>
    /// Returns both execution modes for use with [MemberData].
    /// </summary>
    public static IEnumerable<object[]> All => new[]
    {
        new object[] { ExecutionMode.Interpreted },
        new object[] { ExecutionMode.Compiled }
    };

    /// <summary>
    /// Returns only the interpreted mode.
    /// </summary>
    public static IEnumerable<object[]> InterpretedOnly => new[]
    {
        new object[] { ExecutionMode.Interpreted }
    };

    /// <summary>
    /// Returns only the compiled mode.
    /// </summary>
    public static IEnumerable<object[]> CompiledOnly => new[]
    {
        new object[] { ExecutionMode.Compiled }
    };
}
