using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the <c>$CJSModule</c> runtime class — a spec-compliant Node.js-style module object
/// backing the <c>module</c> binding inside CJS module bodies. Each compiled CJS module
/// init allocates one instance and stores it in a local named <c>module</c> so that the
/// user's source can read <c>module.exports</c>, perform aliased writes like
/// <c>var freeModule = module; freeModule.exports = X</c>, and feature-detect via
/// <c>typeof module === 'object'</c> — none of which work through the previous
/// "intercept literal <c>module.exports = X</c>" path alone.
/// </summary>
/// <remarks>
/// The <c>exports</c> property is backed by a <see cref="FieldInfo"/> pointing at the
/// module's <c>$exports</c> static field; get/set route through reflection so writes via
/// any alias chain propagate to the one location <c>require()</c> reads from. Reflection
/// cost is paid only on <c>module.exports</c> access — not on the much hotter
/// <c>module.exports.foo</c> property chain past the first hop.
/// </remarks>
public partial class RuntimeEmitter
{
    private void EmitCjsModuleClass(ModuleBuilder moduleBuilder, EmittedCommonJsRuntime commonJs)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$CJSModule",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Backing fields
        commonJs.ExportsFieldInfoField = typeBuilder.DefineField(
            "_exportsField", typeof(FieldInfo), FieldAttributes.Private);
        commonJs.IdField = typeBuilder.DefineField(
            "_id", _types.String, FieldAttributes.Private);
        commonJs.FilenameField = typeBuilder.DefineField(
            "_filename", _types.String, FieldAttributes.Private);
        commonJs.LoadedField = typeBuilder.DefineField(
            "_loaded", _types.Boolean, FieldAttributes.Private);
        commonJs.PathsField = typeBuilder.DefineField(
            "_paths", _types.Object, FieldAttributes.Private);
        commonJs.ChildrenField = typeBuilder.DefineField(
            "_children", _types.Object, FieldAttributes.Private);
        commonJs.ParentField = typeBuilder.DefineField(
            "_parent", _types.Object, FieldAttributes.Private);

        EmitCjsModuleCtor(typeBuilder, commonJs);
        EmitCjsModuleExportsProperty(typeBuilder, commonJs);
        EmitCjsModuleSimpleProperty(typeBuilder, "id", _types.String, commonJs.IdField);
        EmitCjsModuleSimpleProperty(typeBuilder, "filename", _types.String, commonJs.FilenameField);
        EmitCjsModuleSimpleProperty(typeBuilder, "loaded", _types.Boolean, commonJs.LoadedField);
        EmitCjsModuleSimpleProperty(typeBuilder, "paths", _types.Object, commonJs.PathsField);
        EmitCjsModuleSimpleProperty(typeBuilder, "children", _types.Object, commonJs.ChildrenField);
        EmitCjsModuleSimpleProperty(typeBuilder, "parent", _types.Object, commonJs.ParentField);
        EmitCjsModuleGetMember(typeBuilder, commonJs);

        var builtType = typeBuilder.CreateType()!;
        commonJs.Type = builtType;
        commonJs.Ctor = builtType.GetConstructor(
            [typeof(FieldInfo), _types.String, _types.String, _types.Object, _types.Object])!;
        _ = builtType.GetProperty("exports")!.GetGetMethod()!;
        commonJs.ExportsSetter = builtType.GetProperty("exports")!.GetSetMethod()!;
    }

    /// <summary>
    /// ctor(FieldInfo exportsField, string id, string filename, object paths, object parent)
    /// — exportsField is the compiled module's $exports static field (used for live
    /// write-through on <c>module.exports = X</c>); loaded starts false; children starts
    /// as an empty array.
    /// </summary>
    private void EmitCjsModuleCtor(TypeBuilder typeBuilder, EmittedCommonJsRuntime commonJs)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(FieldInfo), _types.String, _types.String, _types.Object, _types.Object]
        );
        var il = ctor.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, commonJs.ExportsFieldInfoField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, commonJs.IdField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, commonJs.FilenameField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, commonJs.LoadedField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Stfld, commonJs.PathsField);

        // children = new List<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, commonJs.ChildrenField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stfld, commonJs.ParentField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <c>exports</c> property — getter reads <c>_exportsField.GetValue(null)</c> and
    /// setter writes via <c>_exportsField.SetValue(null, value)</c>. Live round-trip
    /// through the module's own static field so <c>require()</c> always sees the latest
    /// value regardless of how it was assigned.
    /// </summary>
    private void EmitCjsModuleExportsProperty(TypeBuilder typeBuilder, EmittedCommonJsRuntime commonJs)
    {
        var prop = typeBuilder.DefineProperty("exports", PropertyAttributes.None, _types.Object, null);

        var getter = typeBuilder.DefineMethod(
            "get_exports",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        var gil = getter.GetILGenerator();
        gil.Emit(OpCodes.Ldarg_0);
        gil.Emit(OpCodes.Ldfld, commonJs.ExportsFieldInfoField);
        gil.Emit(OpCodes.Ldnull);
        gil.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [_types.Object])!);
        gil.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            "set_exports",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Void,
            [_types.Object]
        );
        var sil = setter.GetILGenerator();
        sil.Emit(OpCodes.Ldarg_0);
        sil.Emit(OpCodes.Ldfld, commonJs.ExportsFieldInfoField);
        sil.Emit(OpCodes.Ldnull);
        sil.Emit(OpCodes.Ldarg_1);
        sil.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("SetValue", [_types.Object, _types.Object])!);
        sil.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    /// <summary>Auto-property backed by a single field — for id/filename/loaded/paths/children/parent.</summary>
    private void EmitCjsModuleSimpleProperty(TypeBuilder typeBuilder, string name, Type propertyType, FieldBuilder field)
    {
        var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, propertyType, null);

        var getter = typeBuilder.DefineMethod(
            $"get_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            propertyType,
            Type.EmptyTypes
        );
        var gil = getter.GetILGenerator();
        gil.Emit(OpCodes.Ldarg_0);
        gil.Emit(OpCodes.Ldfld, field);
        gil.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);

        var setter = typeBuilder.DefineMethod(
            $"set_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Void,
            [propertyType]
        );
        var sil = setter.GetILGenerator();
        sil.Emit(OpCodes.Ldarg_0);
        sil.Emit(OpCodes.Ldarg_1);
        sil.Emit(OpCodes.Stfld, field);
        sil.Emit(OpCodes.Ret);
        prop.SetSetMethod(setter);
    }

    /// <summary>
    /// <c>GetMember(string name)</c> — reflection-friendly accessor used by compiled
    /// <c>$Runtime.GetFieldsProperty</c> dispatch to route <c>module.foo</c> property
    /// access to the right field when <c>foo</c> isn't a compile-time-known property.
    /// Matches the pattern other emitted wrapper types use (e.g. <c>$Stats</c>).
    /// </summary>
    private void EmitCjsModuleGetMember(TypeBuilder typeBuilder, EmittedCommonJsRuntime commonJs)
    {
        var method = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        var il = method.GetILGenerator();

        var strEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var notFoundLabel = il.DefineLabel();

        void Branch(string name, Action loadValue)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, skipLabel);
            loadValue();
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skipLabel);
        }

        Branch("exports", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.ExportsFieldInfoField);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [_types.Object])!);
        });
        Branch("id", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.IdField);
        });
        Branch("filename", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.FilenameField);
        });
        Branch("loaded", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.LoadedField);
            il.Emit(OpCodes.Box, _types.Boolean);
        });
        Branch("paths", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.PathsField);
        });
        Branch("children", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.ChildrenField);
        });
        Branch("parent", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, commonJs.ParentField);
        });

        il.MarkLabel(notFoundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
