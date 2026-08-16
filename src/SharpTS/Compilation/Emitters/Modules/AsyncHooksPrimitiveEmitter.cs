using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for <c>primitive:async_hooks</c>. The single <c>create()</c>
/// factory instantiates a fresh <c>$AsyncLocalStorage</c> (the runtime class
/// emitted by <see cref="RuntimeEmitter.EmitAsyncLocalStorageClass"/>). The
/// user-facing <c>AsyncLocalStorage</c> class lives in
/// <c>stdlib/node/async_hooks.ts</c> as a TS class wrapping this instance.
/// </summary>
public sealed class AsyncHooksPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:async_hooks";

    private static readonly string[] _exportedMembers = ["create"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName != "create") return false;

        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Newobj, ctx.Runtime!.TSAsyncLocalStorageCtor);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName) => false;

    public bool IsExportedProperty(string memberName) => false;
}
