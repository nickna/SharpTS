using SharpTS.Parsing;

namespace SharpTS.Compilation;

public sealed partial class RuntimeFeatureDetector
{
    // Declaration facts retain the checked annotation after contextual inference
    // erases a nested initializer's shape. Binding identity keeps lexical shadows
    // independent; the ordinary marker walks the initializer's actual children.
    private bool IsOpaqueCheckedBinding(Token name) =>
        _typeMap?.GetDeclaredValueType(name) is { } type && ContainsOpaqueType(type);
}
