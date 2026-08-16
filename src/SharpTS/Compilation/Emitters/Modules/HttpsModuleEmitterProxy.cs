using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Proxies to HttpModuleEmitter for the 'https' module (same API as http in SharpTS).
/// </summary>
public sealed class HttpsModuleEmitterProxy : IBuiltInModuleEmitter
{
    private readonly HttpModuleEmitter _inner = new();

    public string ModuleName => "https";

    public IReadOnlyList<string> GetExportedMembers() => _inner.GetExportedMembers();

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
        => _inner.TryEmitMethodCall(emitter, methodName, arguments);

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
        => _inner.TryEmitPropertyGet(emitter, propertyName);

    public bool IsExportedProperty(string memberName)
        => _inner.IsExportedProperty(memberName);
}
