using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitFunctionPrototypeType(ModuleBuilder moduleBuilder,
        EmittedFunctionPrototypeRuntime prototype, FieldInfo undefined,
        EmittedNativeCallableRuntime? nativeCallables = null)
    {
        // Keep the existing dictionary/descriptor surface while exposing the
        // ordinary callable bridge used by Proxy and hybrid runtime consumers.
        // Guest construction still rejects dictionary carriers as nonconstructors.
        var type = EmitTypeDefinitions.DefineType(moduleBuilder, "$FunctionPrototype",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.DictionaryStringObject);
        prototype.Type = type;
        var constructor = type.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, Type.EmptyTypes);
        prototype.Constructor = constructor;
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Ret);

        foreach (var (name, parameters) in new[]
        {
            ("Invoke", new[] { _types.ObjectArray }),
            ("InvokeWithThis", new[] { _types.Object, _types.ObjectArray })
        })
        {
            var method = type.DefineMethod(name, MethodAttributes.Public,
                _types.Object, parameters);
            il = method.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, undefined);
            il.Emit(OpCodes.Ret);
            if (name == "Invoke") nativeCallables?.Register(type, method);
        }
        type.CreateType();
    }
}
