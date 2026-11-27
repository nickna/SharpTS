using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitDomExceptionClass(ModuleBuilder module, EmittedErrorRuntime errors)
    {
        var type = EmitTypeDefinitions.DefineType(module, "$DOMException",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit, errors.Type);
        errors.DomExceptionType = type;
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
            [_types.String, _types.String]);
        errors.DomExceptionConstructor = constructor;
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, errors.NameMessageConstructor);
        il.Emit(OpCodes.Ret);
        type.CreateType();
    }

    private MethodBuilder EmitDomExceptionFactory(TypeBuilder owner, EmittedErrorRuntime errors,
        MethodBuilder toJsString, Type undefinedType)
    {
        var factory = owner.DefineMethod("CreateDOMException", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.ObjectArray]);
        var il = factory.GetILGenerator();
        var message = il.DeclareLocal(_types.String);
        ReadArgument(0, "");
        il.Emit(OpCodes.Stloc, message);
        il.Emit(OpCodes.Ldloc, message);
        ReadArgument(1, SharpTSDomException.DefaultName);
        il.Emit(OpCodes.Newobj, errors.DomExceptionConstructor);
        il.Emit(OpCodes.Ret);
        return factory;

        void ReadArgument(int index, string fallback)
        {
            var missing = il.DefineLabel();
            var convert = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ble, missing);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Isinst, undefinedType);
            il.Emit(OpCodes.Brfalse, convert);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(missing);
            il.Emit(OpCodes.Ldstr, fallback);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(convert);
            il.Emit(OpCodes.Call, toJsString);
            il.MarkLabel(done);
        }
    }

    private void EmitDomExceptionPrototypePopulate(TypeBuilder owner, EmittedRuntime runtime)
    {
        var errors = runtime.Errors;
        var storage = runtime.DescriptorStorage;
        var prototype = errors.DomExceptionPrototype;
        var il = errors.DomExceptionPrototypePopulate.GetILGenerator();
        var descriptor = il.DeclareLocal(storage.DescriptorType);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        EmitPrototypePopulateGuard(il, prototype);
        EmitInstallConstructor(il, runtime, prototype, descriptor, setItem,
            () => EmitRuntimeConstructorValue(il, errors.DomExceptionType));
        il.Emit(OpCodes.Call, errors.PrototypePopulate);
        il.Emit(OpCodes.Ldsfld, prototype);
        il.Emit(OpCodes.Ldsfld, errors.Prototype);
        il.Emit(OpCodes.Call, storage.SetPrototype);

        InstallGetter("name", body => body.Emit(OpCodes.Callvirt, errors.NameGetter));
        InstallGetter("message", body => body.Emit(OpCodes.Callvirt, errors.MessageGetter));
        InstallGetter("code", body => EmitDomExceptionCode(body, errors.NameGetter));
        il.Emit(OpCodes.Ldsfld, prototype);
        il.Emit(OpCodes.Call, runtime.Symbols.GetStorage);
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.ToStringTag);
        EmitReadOnlyDescriptor(() => il.Emit(OpCodes.Ldstr, SharpTSDomException.TypeName),
            enumerable: false, configurable: true);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));

        var codes = SharpTSDomException.LegacyCodes;
        for (int i = 0; i < codes.Length; i++)
        {
            il.Emit(OpCodes.Ldsfld, prototype);
            InstallConstant(codes[i].Constant, i + 1);
            EmitRuntimeConstructorValue(il, errors.DomExceptionType);
            InstallConstant(codes[i].Constant, i + 1);
        }
        il.Emit(OpCodes.Ret);

        void InstallGetter(string name, Action<ILGenerator> emitResult)
        {
            var method = owner.DefineMethod("DOMExceptionGet_" + name,
                MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
            method.DefineParameter(1, ParameterAttributes.None, "__this");
            var body = method.GetILGenerator();
            var receiver = body.DeclareLocal(errors.DomExceptionType);
            var valid = body.DefineLabel();
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Isinst, errors.DomExceptionType);
            body.Emit(OpCodes.Stloc, receiver);
            body.Emit(OpCodes.Ldloc, receiver);
            body.Emit(OpCodes.Brtrue, valid);
            GuestErrorEmitter.ThrowError(body, runtime, errors.TypeErrorConstructor,
                $"DOMException {name} called on an incompatible receiver");
            body.MarkLabel(valid);
            body.Emit(OpCodes.Ldloc, receiver);
            emitResult(body);
            body.Emit(OpCodes.Ret);

            il.Emit(OpCodes.Ldsfld, prototype);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
            il.Emit(OpCodes.Dup);
            var parameters = EmitReflectedMethodLiteralViaHandle(il, method);
            il.Emit(OpCodes.Ldstr, "get " + name);
            il.Emit(OpCodes.Ldc_I4_0);
            EmitReflectedFunctionFactory(il, runtime.FunctionConstruction.GetOrCreate, parameters);
            il.Emit(OpCodes.Callvirt, storage.DescriptorGetter.GetSetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, storage.DescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Call, storage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        // The descriptor target is already on the stack.
        void InstallConstant(string name, int code)
        {
            il.Emit(OpCodes.Ldstr, name);
            EmitReadOnlyDescriptor(() =>
            {
                il.Emit(OpCodes.Ldc_R8, (double)code);
                il.Emit(OpCodes.Box, _types.Double);
            }, enumerable: true, configurable: false);
            il.Emit(OpCodes.Call, storage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        void EmitReadOnlyDescriptor(Action emitValue, bool enumerable, bool configurable)
        {
            il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
            il.Emit(OpCodes.Dup);
            emitValue();
            il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, storage.DescriptorWritable.GetSetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(configurable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, storage.DescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(enumerable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, storage.DescriptorEnumerable.GetSetMethod()!);
        }
    }

    private void EmitDomExceptionCode(ILGenerator il, MethodBuilder nameGetter)
    {
        var name = il.DeclareLocal(_types.String);
        var codeFound = il.DefineLabel();
        il.Emit(OpCodes.Callvirt, nameGetter);
        il.Emit(OpCodes.Stloc, name);
        var codes = SharpTSDomException.LegacyCodes;
        for (int i = 0; i < codes.Length; i++)
        {
            if (codes[i].Name is not { } errorName) continue;
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, name);
            il.Emit(OpCodes.Ldstr, errorName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldc_R8, (double)(i + 1));
            il.Emit(OpCodes.Br, codeFound);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.MarkLabel(codeFound);
        il.Emit(OpCodes.Box, _types.Double);
    }
}
