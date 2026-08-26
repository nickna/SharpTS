using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>Compiler-only proof for an exact non-escaping custom iterator.</summary>
public sealed record StableCustomIteratorInfo(
    Expr.ArrowFunction IteratorMethod,
    Expr.ArrowFunction NextMethod,
    string ResultFingerprint,
    int ValueFieldIndex,
    int DoneFieldIndex);
