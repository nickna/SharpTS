using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// #99 increment 2 — the lib.d.ts loader. Parses the ambient TypeScript lib declarations embedded as
/// <c>libdefs/*.d.ts</c> resources and resolves them to modeled <see cref="TypeInfo"/>, so the type
/// checker resolves lib names (e.g. <c>SymbolConstructor</c>) from real declarations instead of
/// hand-modeled shapes. Built once and cached process-wide (the lib types are the same for every
/// checker instance). A later increment expands the embedded sources toward the full vendored
/// <c>external/typescript/src/lib/*.d.ts</c>.
/// </summary>
internal static class LibTypeLoader
{
    private static FrozenDictionary<string, TypeInfo>? _types;

    /// <summary>Ambient lib type name → its resolved <see cref="TypeInfo"/>.</summary>
    public static FrozenDictionary<string, TypeInfo> Types => _types ??= Build();

    public static bool TryGet(string name, out TypeInfo type)
    {
        if (Types.TryGetValue(name, out var t)) { type = t; return true; }
        type = null!;
        return false;
    }

    private static FrozenDictionary<string, TypeInfo> Build()
    {
        var statements = new List<Stmt>();
        var assembly = typeof(LibTypeLoader).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.Contains("libdefs") || !resourceName.EndsWith(".d.ts"))
                continue;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var parseResult = new Parser(new Lexer(reader.ReadToEnd()).ScanTokens()).Parse();
            if (parseResult.IsSuccess)
                statements.AddRange(parseResult.Statements);
        }
        // Resolve to TypeInfo via a throwaway checker; its diagnostics are discarded (lib sources are
        // trusted). Empty-dict on any failure so a broken embed degrades to the prior hand-modeling.
        return statements.Count > 0
            ? new TypeChecker().ExtractLibTypes(statements)
            : FrozenDictionary<string, TypeInfo>.Empty;
    }
}
