using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Runtime.DotNet;

namespace SharpTS.Modules;

/// <summary>Resolution and validation for module-scoped CLR extension-method imports.</summary>
public static class DotNetExtensionImports
{
    public const string Prefix = "dotnet-extensions:";

    public static bool IsSpecifier(string specifier) =>
        specifier.StartsWith(Prefix, StringComparison.Ordinal);

    public static ParsedModule CreateModule(string virtualPath)
    {
        string typeName = virtualPath[Prefix.Length..];
        Type? container = DotNetTypeRegistry.ResolveFriendly(typeName);
        if (container == null || !(container.IsPublic || container.IsNestedPublic))
            throw new Exception(
                $"Module Error: cannot resolve public extension container '{typeName}'.");
        if (!(container.IsAbstract && container.IsSealed))
            throw new Exception(
                $"Module Error: extension container '{typeName}' must be a static class.");
        if (!container.GetMethods(System.Reflection.BindingFlags.Public |
                                  System.Reflection.BindingFlags.Static)
                .Any(m => m.IsDefined(typeof(ExtensionAttribute), inherit: false)))
        {
            throw new Exception(
                $"Module Error: '{typeName}' contains no public extension methods.");
        }

        return new ParsedModule(virtualPath, [])
        {
            IsScript = false,
            IsTypeChecked = true,
            DotNetExtensionContainer = container
        };
    }

    public static void EnsureSideEffectImport(
        ParsedModule importingModule,
        ParsedModule extensionModule,
        Stmt.Import import)
    {
        if (import.DefaultImport != null ||
            import.NamespaceImport != null ||
            import.NamedImports != null ||
            import.IsTypeOnly)
        {
            throw new Exception(
                $"Module Error: '{import.ModulePath}' must be imported for side effects: " +
                $"import \"{import.ModulePath}\";");
        }

        Type container = extensionModule.DotNetExtensionContainer!;
        if (!importingModule.DotNetExtensionTypes.Contains(container))
            importingModule.DotNetExtensionTypes.Add(container);
    }
}
