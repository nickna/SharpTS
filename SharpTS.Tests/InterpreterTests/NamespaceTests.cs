// =============================================================================
// MIGRATION NOTICE
// =============================================================================
// All namespace tests have been migrated to SharedTests/NamespaceTests.cs
// to run against both interpreter and compiler execution modes.
//
// COMPILER GAPS FIXED:
// The following issues were fixed in this session:
//
// 1. Classes in namespaces - Fixed by adding runtime instantiation via
//    Activator.CreateInstance when classes are accessed through namespaces.
//    See: ILEmitter.Calls.Constructors.cs - TryEmitNamespaceClassConstruction()
//
// 2. Generic classes in namespaces - Fixed by calling MakeGenericType
//    with resolved type arguments before instantiation.
//
// 3. Enums in namespaces - Fixed by emitting a Dictionary<string, object?>
//    with enum members and storing it in the namespace object.
//    See: ILEmitter.Namespaces.cs - EmitEnumInNamespace()
//
// All 13 namespace tests now pass in both interpreter and compiler modes.
//
// See: SharpTS.Tests/SharedTests/NamespaceTests.cs
// =============================================================================
