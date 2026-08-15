using System.Reflection;
using System.Reflection.Emit;

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

    private void EmitClassPrototypeConstructor(TypeBuilder typeBuilder, FieldInfo fieldsField)
    {
        DefineClassPrototypeConstructor(typeBuilder);
        var constructor = _classes.PrototypeConstructors[typeBuilder];
        var il = constructor.GetILGenerator();

        EmitClassPrototypeBaseConstructorCall(il, typeBuilder);

        // Only compiler bookkeeping is initialized. JavaScript fields, private
        // slots, decorators, and constructor bodies deliberately do not run.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stfld, fieldsField);
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

    private void EmitClassPrototypeRegistration(ILGenerator il, TypeBuilder typeBuilder)
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
    }
}
