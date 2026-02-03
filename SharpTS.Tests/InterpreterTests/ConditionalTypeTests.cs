// =============================================================================
// MIGRATION NOTICE
// =============================================================================
// All conditional type tests have been migrated to SharedTests/ConditionalTypeTests.cs
// to run against both interpreter and compiler execution modes.
//
// The shared tests cover:
// - Basic conditional types (T extends U ? X : Y)
// - Distribution over unions
// - Infer keyword for type extraction
// - Nested and recursive conditionals
// - Utility type implementations (NonNullable, Extract, Exclude, Awaited, etc.)
// - Edge cases (never, unknown, void, literal unions)
//
// See: SharpTS.Tests/SharedTests/ConditionalTypeTests.cs
// =============================================================================
