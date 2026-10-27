using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.TypeScriptConformance;

internal sealed record CheckedTypeScriptProgram(
    TypeScriptConformanceMetadata Metadata,
    string VirtualRoot,
    IReadOnlyList<ParsedModule> Modules,
    TypeMap Types,
    IReadOnlyDictionary<SourceDocument, IReadOnlyList<CheckedSourceTypeObservation>> Observations);
