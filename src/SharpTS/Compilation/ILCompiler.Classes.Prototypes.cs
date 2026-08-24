using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void DefineClassPrototypeConstructor(TypeBuilder typeBuilder)
    {
        if (_classes.PrototypeConstructors.ContainsKey(typeBuilder))
            return;

        _classes.PrototypeConstructors[typeBuilder] = typeBuilder.DefineConstructor(
            MethodAttributes.Family,
            CallingConventions.Standard,
            [_runtime.ClassPrototypeMarkerType]);
    }

    private void EmitClassPrototypeConstructor(TypeBuilder typeBuilder)
    {
        DefineClassPrototypeConstructor(typeBuilder);
        var constructor = _classes.PrototypeConstructors[typeBuilder];
        var il = constructor.GetILGenerator();

        EmitClassPrototypeBaseConstructorCall(il, typeBuilder);

        // Only compiler bookkeeping is initialized. JavaScript fields, private
        // slots, decorators, constructor bodies, and dynamic property storage
        // deliberately do not run. The latter materializes through $EnsureFields
        // if the prototype later receives an ordinary dynamic property.
        il.Emit(OpCodes.Ret);
    }

    private void EmitClassPrototypeBaseConstructorCall(ILGenerator il, TypeBuilder typeBuilder)
    {
        Type baseType = typeBuilder.BaseType ?? _types.Object;
        il.Emit(OpCodes.Ldarg_0);

        if (TryResolveClassPrototypeConstructor(baseType, out var prototypeConstructor))
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, prototypeConstructor);
            return;
        }

        switch (baseType.Name)
        {
            case "$Array":
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Call, _runtime.TSArrayCtorFromCtorArgs);
                return;

            case "$Promise":
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, _runtime.TSPromiseCtor);
                return;

            case "$AggregateError":
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, _runtime.TSAggregateErrorCtor);
                return;

            case "$Error":
            case "$TypeError":
            case "$RangeError":
            case "$ReferenceError":
            case "$SyntaxError":
            case "$URIError":
            case "$EvalError":
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, GetEmittedErrorConstructor(baseType.Name[1..]));
                return;
        }

        var baseConstructor = _types.TryGetConstructor(baseType);
        if (baseConstructor == null)
        {
            throw new Diagnostics.Exceptions.CompileException(
                $"Class '{typeBuilder.Name}' cannot create its prototype because base type " +
                $"'{baseType}' has no accessible parameterless constructor.");
        }

        il.Emit(OpCodes.Call, baseConstructor);
    }

    private bool TryResolveClassPrototypeConstructor(
        Type type,
        out ConstructorInfo constructor)
    {
        Type definition = type;
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            definition = type.GetGenericTypeDefinition();

        if (definition is TypeBuilder builder
            && _classes.PrototypeConstructors.TryGetValue(builder, out var openConstructor))
        {
            constructor = ReferenceEquals(type, definition)
                ? openConstructor
                : EmitterTypeHelpers.ResolveConstructor(type, openConstructor);
            return true;
        }

        constructor = null!;
        return false;
    }

    private static int GetClassConstructorLength(IEnumerable<Stmt.Function> methods)
    {
        var constructor = methods.FirstOrDefault(method =>
            !method.IsStatic && method.Name.Lexeme == "constructor" && method.Body != null);
        if (constructor == null)
            return 0;

        int length = 0;
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.IsRest || parameter.DefaultValue != null)
                break;
            length++;
        }
        return length;
    }

    private void EmitClassPrototypeRegistration(
        ILGenerator il,
        TypeBuilder typeBuilder,
        int constructorLength)
    {
        // CLR cannot instantiate an abstract Type. Concrete subclasses still use
        // the abstract base's compiler-only constructor to initialize its fields.
        if ((typeBuilder.Attributes & TypeAttributes.Abstract) != 0)
            return;

        DefineClassPrototypeConstructor(typeBuilder);

        Type selfType = typeBuilder;
        ConstructorInfo constructor = _classes.PrototypeConstructors[typeBuilder];
        if (typeBuilder.IsGenericTypeDefinition)
        {
            selfType = EmitGenerics.MakeGenericType(typeBuilder, typeBuilder.GetGenericArguments());
            constructor = EmitterTypeHelpers.ResolveConstructor(selfType, constructor);
        }

        il.Emit(OpCodes.Ldtoken, selfType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, constructor);

        Type? basePrototypeType = null;
        Type? baseType = typeBuilder.BaseType;
        if (baseType != null && TryResolveClassPrototypeConstructor(baseType, out _))
            basePrototypeType = baseType;

        if (basePrototypeType == null)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            il.Emit(OpCodes.Ldtoken, basePrototypeType);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        }

        il.Emit(OpCodes.Call, _runtime.RegisterClassPrototypeMethod);

        // Materialize the ordinary own properties created by
        // ClassDefinitionEvaluation. Generated instances expose methods through
        // compile-time dispatch, but reflective operations on C.prototype must
        // see stable descriptors for the constructor and each declared method.
        il.Emit(OpCodes.Ldtoken, selfType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Call, _runtime.GetClassPrototypeMethod);
        var prototypeLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, prototypeLocal);

        void EmitPrototypeDataDescriptor(string propertyName, Action emitValue)
        {
            var descriptorLocal = il.DeclareLocal(_runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, _runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, descriptorLocal);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            emitValue();
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ldstr, propertyName);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Call, _runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
        }

        EmitPrototypeDataDescriptor("constructor", () =>
        {
            il.Emit(OpCodes.Ldtoken, selfType);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        });

        // Function `length` is an own, non-writable/non-enumerable,
        // configurable data property whose value is the number of formal
        // parameters before the first default/rest parameter.
        var lengthDescriptorLocal = il.DeclareLocal(_runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, _runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_R8, (double)constructorLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldtoken, selfType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Call, _runtime.PDSDefineProperty);
        il.Emit(OpCodes.Pop);

        Dictionary<string, MethodBuilder>? instanceMethods = null;
        Dictionary<string, MethodBuilder>? staticMethods = null;
        Dictionary<string, MethodBuilder>? staticGetters = null;
        Dictionary<string, MethodBuilder>? staticSetters = null;
        foreach (var (className, builder) in _classes.Builders)
        {
            if (ReferenceEquals(builder, typeBuilder))
            {
                _classes.InstanceMethods.TryGetValue(className, out instanceMethods);
                _classes.StaticMethods.TryGetValue(className, out staticMethods);
                _classes.StaticGetters.TryGetValue(className, out staticGetters);
                _classes.StaticSetters.TryGetValue(className, out staticSetters);
                break;
            }
        }
        if (instanceMethods == null)
        {
            foreach (var (classExpression, builder) in _classExprs.Builders)
            {
                if (ReferenceEquals(builder, typeBuilder))
                {
                    _classExprs.InstanceMethods.TryGetValue(classExpression, out instanceMethods);
                    _classExprs.StaticMethods.TryGetValue(classExpression, out staticMethods);
                    break;
                }
            }
        }

        if (instanceMethods != null)
        {
            foreach (var (methodName, methodBuilder) in instanceMethods)
            {
                if (methodName == "constructor"
                    || methodName.StartsWith("$symmethod_", StringComparison.Ordinal))
                    continue;

                EmitPrototypeDataDescriptor(methodName, () =>
                {
                    il.Emit(OpCodes.Ldloc, prototypeLocal);
                    il.Emit(OpCodes.Ldtoken, methodBuilder);
                    il.Emit(OpCodes.Ldtoken, selfType);
                    il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
                    il.Emit(OpCodes.Castclass, _types.MethodInfo);
                    il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
                });
            }
        }

        if (staticMethods != null)
        {
            foreach (var (methodName, methodBuilder) in staticMethods)
            {
                if (methodName.StartsWith("$symmethod_", StringComparison.Ordinal))
                    continue;

                EmitStaticDataDescriptor(methodName, methodBuilder);
            }
        }

        var staticAccessorNames = new HashSet<string>(StringComparer.Ordinal);
        if (staticGetters != null)
            staticAccessorNames.UnionWith(staticGetters.Keys);
        if (staticSetters != null)
            staticAccessorNames.UnionWith(staticSetters.Keys);
        foreach (var accessorName in staticAccessorNames)
        {
            var descriptorLocal = il.DeclareLocal(_runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, _runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, descriptorLocal);
            if (staticGetters?.GetValueOrDefault(accessorName) is { } getter)
            {
                il.Emit(OpCodes.Ldloc, descriptorLocal);
                EmitStaticFunction(getter);
                il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorGetter.GetSetMethod()!);
            }
            if (staticSetters?.GetValueOrDefault(accessorName) is { } setter)
            {
                il.Emit(OpCodes.Ldloc, descriptorLocal);
                EmitStaticFunction(setter);
                il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorSetter.GetSetMethod()!);
            }
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldtoken, selfType);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldstr, accessorName);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Call, _runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
        }

        void EmitStaticDataDescriptor(string propertyName, MethodBuilder methodBuilder)
        {
            var descriptorLocal = il.DeclareLocal(_runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, _runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, descriptorLocal);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            EmitStaticFunction(methodBuilder);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorWritable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.CompiledPropertyDescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldtoken, selfType);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldstr, propertyName);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Call, _runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
        }

        void EmitStaticFunction(MethodBuilder methodBuilder)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldtoken, methodBuilder);
            il.Emit(OpCodes.Ldtoken, selfType);
            il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
        }
    }
}
