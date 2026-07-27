using System.Collections.Concurrent;
using System.Reflection;
using SharpTS.Parsing;

namespace SharpTS.Modules;

/// <summary>Reads the TypeScript 5.5.4 <c>lib.*.d.ts</c> files embedded in SharpTS.</summary>
internal static class TypeScriptLibProvider
{
    internal static readonly Version CompilerVersion = new(5, 5, 4);
    internal const string VirtualPathPrefix = "typescript-lib:";
    private const string ResourcePrefix = "SharpTS.TypeScriptLib.";
    private static readonly ConcurrentDictionary<string, Lazy<ParsedLibrary>> ParsedLibraries =
        new(StringComparer.OrdinalIgnoreCase);

    internal sealed record ParsedLibrary(
        List<Stmt> Statements,
        IReadOnlyList<TripleSlashDirective> Directives);

    public static string NormalizeFileName(string name)
    {
        name = name.Trim();
        if (name.StartsWith(VirtualPathPrefix, StringComparison.Ordinal))
            name = name[VirtualPathPrefix.Length..];
        if (!name.StartsWith("lib.", StringComparison.OrdinalIgnoreCase))
            name = "lib." + name;
        if (!name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            name += ".d.ts";
        return name.ToLowerInvariant();
    }

    public static string GetVirtualPath(string name) =>
        VirtualPathPrefix + NormalizeFileName(name);

    public static string GetDisplayName(string fileName)
    {
        fileName = NormalizeFileName(fileName);
        const string prefix = "lib.";
        const string suffix = ".d.ts";
        return fileName.Length > prefix.Length + suffix.Length
            ? fileName[prefix.Length..^suffix.Length]
            : "default";
    }

    public static bool TryRead(string name, out string source)
    {
        string fileName = NormalizeFileName(name);
        using Stream? stream = typeof(TypeScriptLibProvider).Assembly
            .GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
        {
            source = "";
            return false;
        }

        using var reader = new StreamReader(stream);
        source = reader.ReadToEnd();
        return true;
    }

    /// <summary>
    /// Parses each immutable compiler library once per process. Program and
    /// conformance resolvers create fresh module graphs, but the embedded ASTs
    /// are read-only checker inputs and can safely be shared.
    /// </summary>
    public static bool TryGetParsed(string name, out ParsedLibrary? library)
    {
        string fileName = NormalizeFileName(name);
        if (!TryRead(fileName, out _))
        {
            library = null;
            return false;
        }

        library = ParsedLibraries.GetOrAdd(
            fileName,
            static key => new Lazy<ParsedLibrary>(
                () => ParseLibrary(key),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return true;
    }

    private static ParsedLibrary ParseLibrary(string fileName)
    {
        if (!TryRead(fileName, out string source))
            throw new InvalidOperationException($"Embedded TypeScript library '{fileName}' disappeared.");

        var lexer = new Lexer(source);
        string virtualPath = GetVirtualPath(fileName);
        var parser = new Parser(lexer.ScanTokens())
            .AsDeclarationFile()
            .WithFilePath(virtualPath);
        var parsed = parser.Parse();
        if (!parsed.IsSuccess)
            throw new Exception(parsed.Diagnostics.First().ToString());

        return new ParsedLibrary(
            parsed.Statements,
            lexer.TripleSlashDirectives.ToArray());
    }

    public static IReadOnlyList<string> AvailableLibraries =>
        typeof(TypeScriptLibProvider).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix + "lib.", StringComparison.Ordinal)
                && n.EndsWith(".d.ts", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
}
