using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Auto-accessor (TypeScript 4.9+) compilation support.
/// Auto-accessors compile to .NET properties with private backing fields.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines an auto-accessor as a .NET property with private backing field.
    /// </summary>
    /// <param name="typeBuilder">The type builder for the class</param>
    /// <param name="className">The qualified class name</param>
    /// <param name="autoAccessor">The auto-accessor declaration</param>
    /// <param name="classGenericParams">Optional generic type parameters for the class</param>
    private void DefineAutoAccessorProperty(
        TypeBuilder typeBuilder,
        string className,
        Stmt.AutoAccessor autoAccessor,
        GenericTypeParameterBuilder[]? classGenericParams)
    {
        string propName = autoAccessor.Name.Lexeme;
        string pascalName = NamingConventions.ToPascalCase(propName);
        Type propertyType = GetAutoAccessorType(autoAccessor, className, classGenericParams);

        // Track as declared property
        _typedInterop.DeclaredPropertyNames[className].Add(pascalName);
        _typedInterop.PropertyTypes[className][pascalName] = propertyType;

        if (autoAccessor.IsReadonly)
        {
            _typedInterop.ReadonlyPropertyNames[className].Add(pascalName);
        }

        // Define private backing field: __backing_PropertyName
        FieldAttributes fieldAttrs = FieldAttributes.Private;
        if (autoAccessor.IsStatic)
        {
            fieldAttrs |= FieldAttributes.Static;
        }

        var backingField = typeBuilder.DefineField(
            $"__backing_{pascalName}",
            propertyType,
            fieldAttrs
        );

        // Track backing field
        _typedInterop.PropertyBackingFields[className][pascalName] = backingField;

        // Apply decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None && autoAccessor.Decorators != null)
        {
            ApplyAutoAccessorDecorators(autoAccessor, backingField);
        }

        // Define the property
        var property = typeBuilder.DefineProperty(
            pascalName,
            PropertyAttributes.None,
            propertyType,
            null
        );
        _typedInterop.ClassProperties[className][pascalName] = property;

        // Build getter method attributes
        MethodAttributes getterAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        if (autoAccessor.IsStatic)
        {
            getterAttrs |= MethodAttributes.Static;
        }
        else
        {
            getterAttrs |= MethodAttributes.Virtual;
            if (autoAccessor.IsOverride)
            {
                getterAttrs |= MethodAttributes.ReuseSlot;
            }
            else
            {
                getterAttrs |= MethodAttributes.NewSlot;
            }
        }

        // Define getter method
        var getter = typeBuilder.DefineMethod(
            $"get_{pascalName}",
            getterAttrs,
            propertyType,
            Type.EmptyTypes
        );

        var getterIL = getter.GetILGenerator();
        if (autoAccessor.IsStatic)
        {
            getterIL.Emit(OpCodes.Ldsfld, backingField);
        }
        else
        {
            getterIL.Emit(OpCodes.Ldarg_0);
            getterIL.Emit(OpCodes.Ldfld, backingField);
        }
        getterIL.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);

        // Track getter for direct dispatch
        if (autoAccessor.IsStatic)
        {
            if (!_classes.StaticGetters.TryGetValue(className, out var staticGetters))
            {
                staticGetters = [];
                _classes.StaticGetters[className] = staticGetters;
            }
            staticGetters[propName] = getter;  // Use original name for static dispatch
        }
        else
        {
            if (!_classes.InstanceGetters.TryGetValue(className, out var classGetters))
            {
                classGetters = [];
                _classes.InstanceGetters[className] = classGetters;
            }
            classGetters[pascalName] = getter;
        }

        // Define setter (unless readonly)
        if (!autoAccessor.IsReadonly)
        {
            MethodAttributes setterAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
            if (autoAccessor.IsStatic)
            {
                setterAttrs |= MethodAttributes.Static;
            }
            else
            {
                setterAttrs |= MethodAttributes.Virtual;
                if (autoAccessor.IsOverride)
                {
                    setterAttrs |= MethodAttributes.ReuseSlot;
                }
                else
                {
                    setterAttrs |= MethodAttributes.NewSlot;
                }
            }

            var setter = typeBuilder.DefineMethod(
                $"set_{pascalName}",
                setterAttrs,
                typeof(void),
                [propertyType]
            );

            var setterIL = setter.GetILGenerator();
            if (autoAccessor.IsStatic)
            {
                setterIL.Emit(OpCodes.Ldarg_0); // value
                setterIL.Emit(OpCodes.Stsfld, backingField);
            }
            else
            {
                setterIL.Emit(OpCodes.Ldarg_0); // this
                setterIL.Emit(OpCodes.Ldarg_1); // value
                setterIL.Emit(OpCodes.Stfld, backingField);
            }
            setterIL.Emit(OpCodes.Ret);

            property.SetSetMethod(setter);

            // Track setter for direct dispatch
            if (autoAccessor.IsStatic)
            {
                if (!_classes.StaticSetters.TryGetValue(className, out var staticSetters))
                {
                    staticSetters = [];
                    _classes.StaticSetters[className] = staticSetters;
                }
                staticSetters[propName] = setter;  // Use original name for static dispatch
            }
            else
            {
                if (!_classes.InstanceSetters.TryGetValue(className, out var classSetters))
                {
                    classSetters = [];
                    _classes.InstanceSetters[className] = classSetters;
                }
                classSetters[pascalName] = setter;
            }
        }
    }

    /// <summary>
    /// Gets the .NET type for an auto-accessor based on its type annotation.
    /// </summary>
    private Type GetAutoAccessorType(
        Stmt.AutoAccessor autoAccessor,
        string className,
        GenericTypeParameterBuilder[]? classGenericParams)
    {
        if (autoAccessor.TypeAnnotation == null)
            return typeof(object);

        // Check if the type annotation is a generic type parameter
        if (classGenericParams != null)
        {
            var param = classGenericParams.FirstOrDefault(p => p.Name == autoAccessor.TypeAnnotation);
            if (param != null)
            {
                return param;
            }
        }

        // Try to get type from TypeMap for accurate typing
        var classType = _typeMap.GetClassType(className);
        if (classType != null && classType.Getters.TryGetValue(autoAccessor.Name.Lexeme, out var accessorTypeInfo))
        {
            return _typeMapper.MapTypeInfoStrict(accessorTypeInfo);
        }

        return TypeMapper.GetClrType(autoAccessor.TypeAnnotation);
    }

    /// <summary>
    /// Applies decorators to an auto-accessor backing field.
    /// </summary>
    private static void ApplyAutoAccessorDecorators(Stmt.AutoAccessor autoAccessor, FieldBuilder backingField)
    {
        if (autoAccessor.Decorators == null) return;

        foreach (var decorator in autoAccessor.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                backingField.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Emits auto-accessor initializer in a constructor.
    /// </summary>
    private void EmitAutoAccessorInitializer(
        ILEmitter emitter,
        Stmt.AutoAccessor autoAccessor,
        string className,
        bool isStatic)
    {
        string pascalName = NamingConventions.ToPascalCase(autoAccessor.Name.Lexeme);

        if (!_typedInterop.PropertyBackingFields[className].TryGetValue(pascalName, out var backingField))
            return;

        if (autoAccessor.Initializer == null)
            return;

        var il = emitter.ILGen;
        Type targetType = _typedInterop.PropertyTypes[className][pascalName];

        if (isStatic)
        {
            emitter.EmitExpression(autoAccessor.Initializer);
            EmitTypeConversion(il, emitter, autoAccessor.Initializer, targetType);
            il.Emit(OpCodes.Stsfld, backingField);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0); // this
            emitter.EmitExpression(autoAccessor.Initializer);
            EmitTypeConversion(il, emitter, autoAccessor.Initializer, targetType);
            il.Emit(OpCodes.Stfld, backingField);
        }
    }
}

