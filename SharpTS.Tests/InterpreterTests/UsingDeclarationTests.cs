// MIGRATION NOTICE
// =============================================================================
// All 'using' declaration tests have been migrated to SharedTests/UsingDeclarationTests.cs
// to run against both interpreter and compiler execution modes.
//
// The shared tests cover:
// - Basic disposal at block end
// - Null value handling (skips disposal)
// - Reverse order disposal for multiple resources
// - Disposal on exception and return
// - Comma-separated declarations
// - Variable accessibility inside block
// - Type annotations
// - Nested blocks with correct disposal order
// - Correct 'this' binding in dispose methods
// - Error handling when dispose throws
//
// COMPILER GAPS FIXED:
// The following issues were fixed to achieve parity:
// 1. Missing ArrowEntryPointDCFields in EmitFunctionBody context
// 2. Missing EntryPointDisplayClassStaticField in EmitExeEntryPointWithUserMain
// 3. Missing Stsfld to store entry-point display class in static field
//
// All 11 using declaration tests now pass in both interpreter and compiler modes.
//
// See: SharpTS.Tests/SharedTests/UsingDeclarationTests.cs
// =============================================================================

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for the 'using' and 'await using' declarations (TypeScript 5.2+ explicit resource management).
/// All tests have been migrated to SharedTests/UsingDeclarationTests.cs.
/// </summary>
public class UsingDeclarationTests
{
    // All tests have been migrated to SharedTests/UsingDeclarationTests.cs
}
