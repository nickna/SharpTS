using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitCanonicalClassConstructorValue(ILGenerator il, Type classType)
    {
        il.Emit(OpCodes.Ldtoken, classType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        // CLR generic context can close an owner token even though TypeScript
        // type arguments do not create a different guest constructor value.
        if (classType.IsGenericType)
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
    }

    private void DefineClassPrototypeConstructor(TypeBuilder typeBuilder)
    {
        if (_classes.PrototypeConstructors.ContainsKey(typeBuilder))
            return;

        _classes.PrototypeConstructors[typeBuilder] = typeBuilder.DefineConstructor(
            MethodAttributes.FamORAssem,
            CallingConventions.Standard,
            [_runtime.ClassPrototypes.MarkerType]);
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
                il.Emit(OpCodes.Call, _runtime.ArrayStorage.CtorFromCtorArgs);
                return;

            case "$Promise":
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, _runtime.RequirePromise().Ctor);
                return;

            case "$AggregateError":
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, _runtime.Errors.AggregateErrorConstructor);
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
        DefineClassPrototypeConstructor(typeBuilder);

        Type selfType = typeBuilder;
        ConstructorInfo constructor = _classes.PrototypeConstructors[typeBuilder];
        if (typeBuilder.IsGenericTypeDefinition)
        {
            selfType = EmitGenerics.MakeGenericType(typeBuilder, typeBuilder.GetGenericArguments());
            constructor = EmitterTypeHelpers.ResolveConstructor(selfType, constructor);
        }

        Type? baseType = typeBuilder.BaseType;
        // Populate the native base before registration selects its parent. This
        // also lets registration preserve a generic family's explicit override.
        if (baseType == _runtime.ArrayStorage.Type)
            il.Emit(OpCodes.Call, _runtime.ArrayOperations.PrototypePopulateMethod);
        var nativeErrorPrototypePopulate = GetNativeErrorPrototypePopulate(baseType);
        if (nativeErrorPrototypePopulate != null)
            il.Emit(OpCodes.Call, nativeErrorPrototypePopulate);

        il.Emit(OpCodes.Ldtoken, selfType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        if ((typeBuilder.Attributes & TypeAttributes.Abstract) != 0)
        {
            // The guest prototype exists independently of its abstract CLR
            // owner. Concrete descendants still chain the bookkeeping constructor.
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Newobj, _runtime.ObjectStorage.Constructor);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Newobj, constructor);
        }

        Type? basePrototypeType = null;
        if (baseType != null && (baseType == _runtime.ArrayStorage.Type
            || nativeErrorPrototypePopulate != null
            || TryResolveClassPrototypeConstructor(baseType, out _)))
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

        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.Register);

        // Create the constructor property before the source-ordered instance
        // member installer. Reflective operations use these stable descriptors
        // independently of generated instance method dispatch.
        il.Emit(OpCodes.Ldtoken, selfType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.Get);
        var prototypeLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, prototypeLocal);

        void EmitPrototypeDataDescriptor(string propertyName, Action emitValue)
        {
            var descriptorLocal = il.DeclareLocal(_runtime.DescriptorStorage.DescriptorType);
            il.Emit(OpCodes.Newobj, _runtime.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descriptorLocal);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            emitValue();
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ldstr, propertyName);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Call, _runtime.DescriptorStorage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        EmitPrototypeDataDescriptor("constructor", () =>
        {
            EmitCanonicalClassConstructorValue(il, selfType);
        });

        // Function `length` is an own, non-writable/non-enumerable,
        // configurable data property whose value is the number of formal
        // parameters before the first default/rest parameter.
        var lengthDescriptorLocal = il.DeclareLocal(_runtime.DescriptorStorage.DescriptorType);
        il.Emit(OpCodes.Newobj, _runtime.DescriptorStorage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_R8, (double)constructorLength);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldtoken, selfType);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloc, lengthDescriptorLocal);
        il.Emit(OpCodes.Call, _runtime.DescriptorStorage.DefineProperty);
        il.Emit(OpCodes.Pop);

        Dictionary<string, MethodBuilder>? staticMethods = null;
        foreach (var (className, builder) in _classes.Builders)
        {
            if (ReferenceEquals(builder, typeBuilder))
            {
                _classes.StaticMethods.TryGetValue(className, out staticMethods);
                break;
            }
        }
        if (staticMethods == null)
        {
            foreach (var (classExpression, builder) in _classExprs.Builders)
            {
                if (ReferenceEquals(builder, typeBuilder))
                {
                    _classExprs.StaticMethods.TryGetValue(classExpression, out staticMethods);
                    break;
                }
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

        // Instance methods/accessors share the later source-ordered installer.
        // Expression definitions likewise install on their owned prototype.
        EmitKnownDeclaredAccessorDescriptors(il, typeBuilder, staticOnly: true);
        if (GetNominalClassDeclaration(typeBuilder) == null)
            EmitReceiverAutoAccessorDescriptors(il, typeBuilder);

        void EmitStaticDataDescriptor(string propertyName, MethodBuilder methodBuilder)
        {
            var descriptorLocal = il.DeclareLocal(_runtime.DescriptorStorage.DescriptorType);
            il.Emit(OpCodes.Newobj, _runtime.DescriptorStorage.DescriptorConstructor);
            il.Emit(OpCodes.Stloc, descriptorLocal);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            EmitStaticFunction(methodBuilder);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!);
            il.Emit(OpCodes.Ldtoken, selfType);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldstr, propertyName);
            il.Emit(OpCodes.Ldloc, descriptorLocal);
            il.Emit(OpCodes.Call, _runtime.DescriptorStorage.DefineProperty);
            il.Emit(OpCodes.Pop);
        }

        void EmitStaticFunction(MethodBuilder methodBuilder)
        {
            if (DefineDeclarationReceiverMethod(typeBuilder, methodBuilder) is { } adapter)
            {
                il.Emit(OpCodes.Newobj, adapter.Constructor);
                EmitMethodInfoLiteral(il, adapter.Method, adapter.Type);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldtoken, methodBuilder);
                il.Emit(OpCodes.Ldtoken, selfType);
                il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
                il.Emit(OpCodes.Castclass, _types.MethodInfo);
            }
            il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
        }
    }

    private MethodBuilder? GetNativeErrorPrototypePopulate(Type? baseType)
    {
        var errors = _runtime.Errors;
        foreach (var (errorType, populate) in new[]
        {
            (errors.Type, errors.PrototypePopulate),
            (errors.TypeErrorType, errors.TypeErrorPrototypePopulate),
            (errors.RangeErrorType, errors.RangeErrorPrototypePopulate),
            (errors.ReferenceErrorType, errors.ReferenceErrorPrototypePopulate),
            (errors.SyntaxErrorType, errors.SyntaxErrorPrototypePopulate),
            (errors.URIErrorType, errors.URIErrorPrototypePopulate),
            (errors.EvalErrorType, errors.EvalErrorPrototypePopulate),
            (errors.AggregateErrorType, errors.AggregateErrorPrototypePopulate)
        })
        {
            if (baseType == errorType) return populate;
        }
        return null;
    }

    private Stmt.Class? GetNominalClassDeclaration(TypeBuilder typeBuilder)
        => _classes.Declarations.FirstOrDefault(source => !_runtimeClassDeclarations.ContainsKey(source)
            && _classes.DeclarationNames.GetValueOrDefault(source) == typeBuilder.FullName);

    private void EmitKnownDeclaredAccessorDescriptors(ILGenerator il, TypeBuilder typeBuilder, bool staticOnly = false)
    {
        var declaration = GetNominalClassDeclaration(typeBuilder);
        bool hasDeferredKeys = _classes.DeferredDefinitions.TryGet(typeBuilder, out _);
        var computedAccessors = _classes.ComputedMembers.GetAccessors(typeBuilder);
        foreach (var accessor in (declaration?.Accessors ?? []).OrderBy(accessor => accessor.Name.SourceOrderOffset))
        {
            if (staticOnly && !accessor.IsStatic) continue;
            string? name = accessor.ComputedKey == null ? accessor.Name.Lexeme
                : accessor.IsStatic && !hasDeferredKeys ? GetConstantComputedAccessorName(accessor.ComputedKey) : null;
            if (name is null) continue;

            // A later data method replaces both halves of an earlier accessor.
            if (declaration!.Methods.Any(method => method.Body != null && !method.IsPrivate && method.ComputedKey == null
                    && method.IsStatic == accessor.IsStatic && (method.IsStatic || method.Name.Lexeme != "constructor")
                    && method.Name.Lexeme == name && method.Name.SourceOrderOffset > accessor.Name.SourceOrderOffset)) continue;
            MethodBuilder? accessorMethod = null;
            if (accessor.ComputedKey == null)
                _classes.AccessorBuilders.TryGetValue(accessor, out accessorMethod);
            else
                accessorMethod = computedAccessors.FirstOrDefault(entry => ReferenceEquals(entry.Accessor, accessor)).Method;
            if (accessorMethod != null)
                EmitDeclaredAccessorDescriptor(il, typeBuilder, accessor, accessorMethod,
                    () => il.Emit(OpCodes.Ldstr, name));
        }
    }
}
