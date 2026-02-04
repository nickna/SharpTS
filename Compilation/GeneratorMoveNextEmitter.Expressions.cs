using System.IO;
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
        string path = _ctx?.CurrentModulePath ?? "";
        string url = path;
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }
        string dirname = string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path) ?? "";

        // Create Dictionary<string, object> and add properties
        _il.Emit(OpCodes.Newobj, Types.DictionaryStringObjectCtor);

        // Add "url" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "url");
        _il.Emit(OpCodes.Ldstr, url);
        _il.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);

        // Add "filename" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "filename");
        _il.Emit(OpCodes.Ldstr, path);
        _il.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);

        // Add "dirname" property
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Ldstr, "dirname");
        _il.Emit(OpCodes.Ldstr, dirname);
        _il.Emit(OpCodes.Callvirt, Types.DictionaryStringObjectSetItem);

        // Wrap in SharpTSObject
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        SetStackUnknown();
    }

    #endregion
}
