using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Interpreter-specific generator tests.
///
/// Note: All generator tests have been migrated to:
/// - SharedTests/GeneratorTests.cs (tests that work in both modes)
/// - CompilerTests/GeneratorCompilerTests.cs (compiler-specific protocol tests with parity checks)
///
/// This file is kept for potential future interpreter-specific tests
/// or for tests of features that work differently in the interpreter.
/// </summary>
public class GeneratorTests
{
    // All tests have been migrated to SharedTests and CompilerTests.
    //
    // Migrated to SharedTests/GeneratorTests.cs:
    // - Generator_ForOfLoop_IteratesAllValues
    // - Generator_YieldStarArray_DelegatesCorrectly
    // - Generator_YieldStarGenerator_DelegatesCorrectly
    // - Generator_WhileLoop_YieldsMultipleTimes
    // - Generator_Closure_CapturesVariables
    // - Generator_WithParameters_UsesParameters
    // - Generator_IfStatement_ConditionalYield
    // - Generator_YieldStarMap_DelegatesEntries
    // - Generator_YieldStarSet_DelegatesValues
    //
    // Migrated to CompilerTests/GeneratorCompilerTests.cs (with parity checks):
    // - Generator_BasicYield_ReturnsValues
    // - Generator_EmptyGenerator_ReturnsDoneImmediately
    // - Generator_IteratorResult_HasCorrectStructure
    // - Generator_MultipleInstances_IndependentState
    // - Generator_YieldStarString_DelegatesCharacters
    // - Generator_YieldStarMap_DelegatesEntries (parity test)
    // - Generator_YieldStarSet_DelegatesValues (parity test)
}
