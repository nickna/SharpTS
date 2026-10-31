using SharpTS.TypeSystem;

namespace SharpTS.Declaration;

internal static class TypeInfoDeclarationRenderer
{
    public static string Render(TypeInfo type) => new CheckedTypeRenderer(false).Render(type);
    internal static string RenderTypeParameterDeclaration(TypeInfo.TypeParameter parameter) =>
        new CheckedTypeRenderer(false).RenderTypeParameterDeclaration(parameter);
}

public sealed class DeclarationEmitException(string message) : Exception(message);
