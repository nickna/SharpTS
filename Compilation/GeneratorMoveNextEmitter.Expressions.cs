using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    // EmitExpression dispatch is inherited from ExpressionEmitterBase

    #region Abstract Implementations

    protected override void EmitSuper(Expr.Super s)
    {
        // Not implemented in generator context - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Not implemented in generator context - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    protected override void EmitImportMeta(Expr.ImportMeta im)
    {
        // Get current module path and convert to file:// URL
        string url = _ctx?.CurrentModulePath ?? "";
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }

        // Create Dictionary<string, object> and add "url" property
        _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "url");
        _il.Emit(OpCodes.Ldstr, url);
        _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Wrap in SharpTSObject
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        SetStackUnknown();
    }

    protected override void EmitUnknownExpression(Expr expr)
    {
        // Unsupported expression - push null
        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    #endregion
}
