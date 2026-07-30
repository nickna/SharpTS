using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// The emit path's single seam for generic-type instantiation (#1324 gate). Under Native AOT, a
/// RUNTIME generic definition rejects builder-type arguments with
/// <see cref="PlatformNotSupportedException"/> ("created by a custom ReflectionContext"), where
/// CoreCLR returns a <c>TypeBuilderInstantiation</c>. The fallback constructs the persisted
/// reflection-emit implementation's <c>TypeBuilderInstantiation</c> directly — the same shape
/// <see cref="PersistedAssemblyBuilder"/>'s own <c>TypeBuilder.MakeGenericType</c> produces, so
/// the metadata writer consumes it identically. Every <c>MakeGenericType</c> in
/// <c>Compilation/</c> routes through here (directly or via <see cref="TypeProvider"/>).
/// </summary>
internal static class EmitGenerics
{
    internal static Type MakeGenericType(Type genericDefinition, params Type[] typeArguments)
    {
        try
        {
            return genericDefinition.MakeGenericType(typeArguments);
        }
        catch (PlatformNotSupportedException) when (
            Array.Exists(typeArguments, static t => t is TypeBuilder || t.Module is ModuleBuilder))
        {
            return MakeTypeBuilderInstantiation(genericDefinition, typeArguments);
        }
    }

    private static MethodInfo? _tbiFactory;

    private static Type MakeTypeBuilderInstantiation(Type genericDefinition, Type[] typeArguments)
    {
        // Internal API of the managed System.Reflection.Emit (the persisted implementation —
        // the only one that exists under Native AOT). Kept alive by the TrimmerRootDescriptor
        // in AotTrimmerRoots.xml. If the internal shape ever changes, this fails loudly here
        // rather than corrupting output.
        _tbiFactory ??= Type.GetType(
                "System.Reflection.Emit.TypeBuilderInstantiation, System.Reflection.Emit",
                throwOnError: true)!
            .GetMethod(
                "MakeGenericType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(Type), typeof(Type[])])
            ?? throw new PlatformNotSupportedException(
                "TypeBuilderInstantiation.MakeGenericType(Type, Type[]) not found — the reflection-emit internals changed.");
        return (Type)_tbiFactory.Invoke(null, [genericDefinition, typeArguments])!;
    }
}
