using System.Reflection;
using Xunit.Sdk;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Supplies both execution modes to a [Theory]. Replaces the
/// <c>[MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]</c>
/// scaffold that was repeated ~5,900 times across the suite:
/// <code>
/// [Theory, ModeData]
/// public void MyTest(ExecutionMode mode) { ... }
/// </code>
/// </summary>
public sealed class ModeDataAttribute : DataAttribute
{
    public override IEnumerable<object[]> GetData(MethodInfo testMethod) => ExecutionModes.All;
}

/// <summary>
/// Supplies only <see cref="ExecutionMode.Interpreted"/> — for tests of
/// interpreter-only behavior (REPL semantics, interpreter-internal seams).
/// </summary>
public sealed class InterpretedOnlyDataAttribute : DataAttribute
{
    public override IEnumerable<object[]> GetData(MethodInfo testMethod) => ExecutionModes.InterpretedOnly;
}

/// <summary>
/// Supplies only <see cref="ExecutionMode.Compiled"/> — for tests of
/// compiled-mode-only behavior (IL shape, standalone-DLL constraints).
/// </summary>
public sealed class CompiledOnlyDataAttribute : DataAttribute
{
    public override IEnumerable<object[]> GetData(MethodInfo testMethod) => ExecutionModes.CompiledOnly;
}
