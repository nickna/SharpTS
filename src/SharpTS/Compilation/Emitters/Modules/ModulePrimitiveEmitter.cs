using System.Collections.Immutable;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Compiled backing for <c>node:module.createRequire()</c>. Compiled CommonJS
/// require lowering is syntax-directed: a local binding named <c>require</c>
/// followed by a string-literal call is resolved at compile time. The returned
/// placeholder is therefore never invoked for the supported pattern.
/// </summary>
public sealed class ModulePrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:module";

    private static readonly IReadOnlyList<string> _exportedMembers = (ImmutableArray<string>)["createRequire"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName != "createRequire") return false;
        emitter.Context.IL.Emit(OpCodes.Ldnull);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName) => false;
}
