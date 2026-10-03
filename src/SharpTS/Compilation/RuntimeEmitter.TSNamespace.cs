using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTSNamespaceClass(ModuleBuilder moduleBuilder, EmittedNamespaceRuntime namespaces,
        FieldInfo undefinedInstance)
    {
        // Define class: public sealed class $TSNamespace
        // Mirrors SharpTSNamespace but is emitted into the compiled assembly
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TSNamespace",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        namespaces.Type = typeBuilder;

        // Field: private readonly Dictionary<string, object?> _members
        var membersField = typeBuilder.DefineField("_members", _types.DictionaryStringObject, FieldAttributes.Private);

        // Field: public string Name
        var nameField = typeBuilder.DefineField("_name", _types.String, FieldAttributes.Private);

        var bindingMapType = _types.MakeGenericType(_types.DictionaryOpen, _types.String, _types.FieldInfo);
        var bindingsField = typeBuilder.DefineField("_bindings", bindingMapType, FieldAttributes.Private);

        // Constructor: public $TSNamespace(string name)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        namespaces.Constructor = ctorBuilder;

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
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(bindingMapType));
        ctorIL.Emit(OpCodes.Stfld, bindingsField);
        ctorIL.Emit(OpCodes.Ret);

        // Missing properties return undefined; explicitly stored null remains null.
        var getBuilder = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        namespaces.Get = getBuilder;

        var getIL = getBuilder.GetILGenerator();
        var valueLocal = getIL.DeclareLocal(_types.Object);
        var foundLabel = getIL.DefineLabel();

        // Namespace bodies and object aliases share the exported backing field.
        var bindingLocal = getIL.DeclareLocal(_types.FieldInfo);
        var unboundGet = getIL.DefineLabel();
        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, bindingsField);
        getIL.Emit(OpCodes.Ldarg_1);
        getIL.Emit(OpCodes.Ldloca, bindingLocal);
        getIL.Emit(OpCodes.Callvirt, _types.GetMethod(bindingMapType, "TryGetValue", _types.String, _types.FieldInfo.MakeByRefType()));
        getIL.Emit(OpCodes.Brfalse, unboundGet);
        getIL.Emit(OpCodes.Ldloc, bindingLocal);
        getIL.Emit(OpCodes.Ldnull);
        getIL.Emit(OpCodes.Callvirt, _types.FieldInfoGetValue);
        getIL.Emit(OpCodes.Ret);
        getIL.MarkLabel(unboundGet);

        getIL.Emit(OpCodes.Ldarg_0);
        getIL.Emit(OpCodes.Ldfld, membersField);
        getIL.Emit(OpCodes.Ldarg_1);
        getIL.Emit(OpCodes.Ldloca, valueLocal);
        getIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        getIL.Emit(OpCodes.Brtrue, foundLabel);
        getIL.Emit(OpCodes.Ldsfld, undefinedInstance);
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
        namespaces.Set = setBuilder;

        var setIL = setBuilder.GetILGenerator();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldfld, membersField);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Ldarg_2);
        setIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        var setBindingLocal = setIL.DeclareLocal(_types.FieldInfo);
        var unboundSet = setIL.DefineLabel();
        setIL.Emit(OpCodes.Ldarg_0);
        setIL.Emit(OpCodes.Ldfld, bindingsField);
        setIL.Emit(OpCodes.Ldarg_1);
        setIL.Emit(OpCodes.Ldloca, setBindingLocal);
        setIL.Emit(OpCodes.Callvirt, _types.GetMethod(bindingMapType, "TryGetValue", _types.String, _types.FieldInfo.MakeByRefType()));
        setIL.Emit(OpCodes.Brfalse, unboundSet);
        setIL.Emit(OpCodes.Ldloc, setBindingLocal);
        setIL.Emit(OpCodes.Ldnull);
        setIL.Emit(OpCodes.Ldarg_2);
        setIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "SetValue", _types.Object, _types.Object));
        setIL.MarkLabel(unboundSet);
        setIL.Emit(OpCodes.Ret);

        var bindBuilder = typeBuilder.DefineMethod("Bind", MethodAttributes.Public,
            _types.Void, [_types.String, _types.FieldInfo]);
        namespaces.Bind = bindBuilder;
        var bindIL = bindBuilder.GetILGenerator();
        bindIL.Emit(OpCodes.Ldarg_0);
        bindIL.Emit(OpCodes.Ldfld, bindingsField);
        bindIL.Emit(OpCodes.Ldarg_1);
        bindIL.Emit(OpCodes.Ldarg_2);
        bindIL.Emit(OpCodes.Callvirt, _types.GetMethod(bindingMapType, "set_Item", _types.String, _types.FieldInfo));
        bindIL.Emit(OpCodes.Ret);

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
