using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'string_decoder' module.
/// </summary>
/// <remarks>
/// Provides the StringDecoder class for decoding Buffer objects into strings
/// while properly handling multi-byte character sequences.
/// </remarks>
public sealed class StringDecoderModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "string_decoder";

    private static readonly string[] _exportedMembers = ["StringDecoder"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        // StringDecoder is a constructor, not a direct method
        // Method calls would be on StringDecoder instances, handled elsewhere
        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName == "StringDecoder")
        {
            // Return the StringDecoder constructor wrapper
            var ctx = emitter.Context;
            var il = ctx.IL;

            // Get or create the StringDecoder constructor wrapper function
            il.Emit(OpCodes.Call, ctx.Runtime!.StringDecoderGetConstructor);
            return true;
        }

        return false;
    }
}
