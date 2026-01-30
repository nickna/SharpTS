using SharpTS.Parsing.Visitors;

namespace SharpTS.TypeSystem;

/// <summary>
/// Handler registrations for the TypeChecker.
/// Configures a NodeRegistry with handlers for all AST node types.
/// </summary>
public static class TypeCheckerRegistry
{
    /// <summary>
    /// Creates and configures a NodeRegistry for the TypeChecker.
    /// Uses reflection-based auto-registration to discover Visit* methods.
    /// </summary>
    /// <returns>A frozen registry ready for dispatch.</returns>
    public static NodeRegistry<TypeChecker, TypeInfo, VoidResult> Create()
    {
        return new NodeRegistry<TypeChecker, TypeInfo, VoidResult>()
            .AutoRegister()
            .Freeze();
    }
}
