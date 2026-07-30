using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTSNamespaceClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSNamespace
        // Mirrors SharpTSNamespace but is emitted into the compiled assembly
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TSNamespace",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSNamespaceType = typeBuilder;

        // Field: private readonly Dictionary<string, object?> _members
        var membersField = typeBuilder.DefineField("_members", _types.DictionaryStringObject, FieldAttributes.Private);

        // Field: public string Name
        var nameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);

        // Constructor: public $TSNamespace(string name)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSNamespaceCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // _name = name
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, nameField);
        // _members = new Dictionary<string, object?>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        ctorIL.Emit(OpCodes.Stfld, membersField);
        ctorIL.Emit(OpCodes.Ret);

        // Get method: public object? Get(string name) => _members.TryGetValue(name, out var value) ? value : null;
        var getBuilder = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSNamespaceGet = getBuilder;

        var getIL = getBuilder.GetILGenerator();
        var valueLocal = getIL.DeclareLocal(_types.Object);
        var foundLabel = getIL.DefineLabel();
        var notFoundLabel = getIL.DefineLabel();

        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, membersField);
        getIL.Emit(OpCodes.Ldarg_1);
        getIL.Emit(OpCodes.Ldloca, valueLocal);
        getIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        getIL.Emit(OpCodes.Brtrue, foundLabel);
        getIL.Emit(OpCodes.Ldnull);
        getIL.Emit(OpCodes.Ret);
        getIL.MarkLabel(foundLabel);
        getIL.Emit(OpCodes.Ldloc, valueLocal);
        getIL.Emit(OpCodes.Ret);

        // Set method: public void Set(string name, object? value) => _members[name] = value;
        var setBuilder = typeBuilder.DefineMethod(
            "Set",
            MethodAttributes.Public,
            _types.Void,
            [_types.String, _types.Object]
        );
        runtime.TSNamespaceSet = setBuilder;

        var setIL = setBuilder.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldfld, membersField);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Ldarg_2);
        setIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        setIL.Emit(OpCodes.Ret);

        // ToString method: public override string ToString() => $"[namespace {Name}]"
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[namespace ");
        toStringIL.Emit(OpCodes.Ldarg_0);
        toStringIL.Emit(OpCodes.Ldfld, nameField);
        toStringIL.Emit(OpCodes.Ldstr, "]");
        toStringIL.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        toStringIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}
