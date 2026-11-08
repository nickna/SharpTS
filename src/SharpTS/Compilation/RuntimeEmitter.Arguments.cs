using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitArgumentsLength(EmittedRuntime runtime)
    {
        var il = runtime.Arguments.DefineLength.GetILGenerator();
        var storage = runtime.DescriptorStorage;
        var descriptor = il.DeclareLocal(storage.DescriptorType);
        il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, storage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Call, storage.DefineValidatedProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArgumentsInitializer(EmittedRuntime runtime)
    {
        var il = runtime.Arguments.Initialize.GetILGenerator();
        il.Emit(OpCodes.Call, runtime.ObjectPrototypes.Populate);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypes.Prototype);
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.SetPrototype);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, runtime.Arguments.DefineLength);

        var descriptor = il.DeclareLocal(runtime.DescriptorStorage.DescriptorType);
        il.Emit(OpCodes.Newobj, runtime.DescriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        _types.EmitLoadMethodInfo(il, runtime.ArrayOperations.Values);
        il.Emit(OpCodes.Ldstr, "values");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.FunctionConstruction.GetOrCreate);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Symbols.GetStorage);
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.Iterator);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item"));

        if (_features.UsesArguments)
        {
            // The unmapped arguments object owns a non-configurable callee
            // accessor. Its getter and setter are the same throwing function.
            var thrower = runtime.RuntimeClass.Type.DefineMethod("ArgumentsCalleeThrower",
                MethodAttributes.Private | MethodAttributes.Static, _types.Object, Type.EmptyTypes);
            GuestErrorEmitter.ThrowTypeError(thrower.GetILGenerator(), runtime,
                "Cannot access 'callee' on an unmapped arguments object");

            var done = il.DefineLabel();
            var accessor = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, done);
            _types.EmitLoadMethodInfo(il, thrower);
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, runtime.FunctionConstruction.GetOrCreate);
            il.Emit(OpCodes.Stloc, accessor);
            il.Emit(OpCodes.Newobj, runtime.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descriptor);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldloc, accessor);
            il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorGetter.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldloc, accessor);
            il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorSetter.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "callee");
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Call, runtime.DescriptorStorage.DefineValidatedProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(done);
        }
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Defines <c>$Arguments : List&lt;object&gt;</c> — a marker subclass used to
    /// represent the JS <c>arguments</c> object in compiled mode. Inherits all
    /// List&lt;object&gt; behavior so existing dispatchers (Isinst List, Castclass
    /// List) continue working transparently. Distinguishable at runtime via
    /// <c>Isinst $Arguments</c> for two ECMA-262 spec checks:
    ///   - <c>Object.prototype.toString.call(arguments) === "[object Arguments]"</c>
    ///   - <c>Array.isArray(arguments) === false</c>
    /// </summary>
    /// <remarks>
    /// Without this marker, <c>arguments</c> bound as a plain List&lt;object&gt;
    /// is indistinguishable from a regular array, and the brand-tagger / IsArray
    /// fall through to the [object Array] / true paths, breaking ~30 test262
    /// tests that probe these spec details on the arguments object.
    /// </remarks>
    internal void EmitArgumentsTypeDefinition(ModuleBuilder moduleBuilder, EmittedArgumentsRuntime arguments)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Arguments",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ListOfObject
        );
        arguments.Type = typeBuilder;

        // Ctor: public $Arguments() : base() { }
        var defaultCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var defaultIL = defaultCtor.GetILGenerator();
        defaultIL.Emit(OpCodes.Ldarg_0);
        defaultIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.ListOfObject));
        defaultIL.Emit(OpCodes.Ret);
        arguments.DefaultCtor = defaultCtor;

        // Ctor: public $Arguments(IEnumerable<object> source) : base(source) { }
        var enumCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.IEnumerableOfObject]
        );
        var enumIL = enumCtor.GetILGenerator();
        enumIL.Emit(OpCodes.Ldarg_0);
        enumIL.Emit(OpCodes.Ldarg_1);
        enumIL.Emit(OpCodes.Call, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        enumIL.Emit(OpCodes.Ret);
        arguments.EnumerableCtor = enumCtor;

        typeBuilder.CreateType();
    }
}
