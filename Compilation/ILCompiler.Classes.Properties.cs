using System.Collections.Frozen;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Property and field handling for class compilation.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Gets the .NET type for a field based on its TypeScript type annotation.
    /// Uses TypeInfo from TypeMap for accurate type resolution (including typed arrays).
    /// </summary>
    /// <param name="field">The field statement</param>
    /// <param name="className">The class name to look up field types from TypeMap</param>
    /// <param name="classGenericParams">Optional generic type parameters for the class</param>
    private Type GetFieldType(Stmt.Field field, string? className = null, GenericTypeParameterBuilder[]? classGenericParams = null)
    {
        if (field.TypeAnnotation == null)
            return typeof(object);

        // Check if the type annotation is a generic type parameter
        if (classGenericParams != null)
        {
            var param = classGenericParams.FirstOrDefault(p => p.Name == field.TypeAnnotation);
            if (param != null)
            {
                return param; // Return the GenericTypeParameterBuilder as the type
            }
        }

        // Try to get typed field info from TypeMap for accurate typing
        if (className != null)
        {
            var classType = _typeMap.GetClassType(className);
            if (classType != null && classType.FieldTypes.TryGetValue(field.Name.Lexeme, out var fieldTypeInfo))
            {
                // Skip typed arrays for now - runtime creates List<object> which can't be cast to List<T>
                // Union types, primitives, and classes are safe to type
                if (fieldTypeInfo is not TypeSystem.TypeInfo.Array)
                {
                    return _typeMapper.MapTypeInfoStrict(fieldTypeInfo);
                }
            }
        }

        return TypeMapper.GetClrType(field.TypeAnnotation);
    }

    /// <summary>
    /// Defines a real .NET property with backing field for an instance field.
    /// The property uses typed backing field internally but exposes object-typed
    /// getter/setter for compatibility with existing emission code.
    /// </summary>
    /// <param name="typeBuilder">The type builder for the class</param>
    /// <param name="className">The class name</param>
    /// <param name="field">The field statement</param>
    /// <param name="classGenericParams">Optional generic type parameters for the class</param>
    private void DefineInstanceProperty(TypeBuilder typeBuilder, string className, Stmt.Field field, GenericTypeParameterBuilder[]? classGenericParams = null)
    {
        string fieldName = field.Name.Lexeme;
        string pascalName = NamingConventions.ToPascalCase(fieldName);
        Type propertyType = GetFieldType(field, className, classGenericParams);

        // Track as declared property (using PascalCase for .NET interop)
        _declaredPropertyNames[className].Add(pascalName);
        _propertyTypes[className][pascalName] = propertyType;

        if (field.IsReadonly)
        {
            _readonlyPropertyNames[className].Add(pascalName);
        }

        // Define private backing field with __ prefix to avoid conflicts
        // Uses the actual typed field for efficiency
        var backingField = typeBuilder.DefineField(
            $"__{pascalName}",
            propertyType,
            FieldAttributes.Private
        );
        _propertyBackingFields[className][pascalName] = backingField;

        // Apply field-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyFieldDecorators(field, backingField);
        }

        // Define the property with PascalCase name (for C# interop)
        var property = typeBuilder.DefineProperty(
            pascalName,
            PropertyAttributes.None,
            propertyType,
            null
        );
        _classProperties[className][pascalName] = property;

        // Define getter method - returns actual property type for proper C# interop
        var getter = typeBuilder.DefineMethod(
            $"get_{pascalName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            propertyType,  // Return actual type for C# interop
            Type.EmptyTypes
        );

        var getterIL = getter.GetILGenerator();
        getterIL.Emit(OpCodes.Ldarg_0);             // this
        getterIL.Emit(OpCodes.Ldfld, backingField); // this.__fieldName (typed value)
        getterIL.Emit(OpCodes.Ret);                 // Return typed value directly

        property.SetGetMethod(getter);

        // Track getter for direct dispatch (using PascalCase key)
        if (!_instanceGetters.TryGetValue(className, out var classGetters))
        {
            classGetters = [];
            _instanceGetters[className] = classGetters;
        }
        classGetters[pascalName] = getter;

        // Define setter method - accepts actual property type for proper C# interop
        // For readonly fields: define the setter but don't register it for direct dispatch,
        // so type-checked code won't allow setting, but constructor can via runtime reflection
        {
            var setter = typeBuilder.DefineMethod(
                $"set_{pascalName}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                typeof(void),    // Standard setter returns void
                [propertyType]   // Accept actual type for C# interop
            );

            var setterIL = setter.GetILGenerator();
            setterIL.Emit(OpCodes.Ldarg_0);          // this
            setterIL.Emit(OpCodes.Ldarg_1);          // value (already typed)
            setterIL.Emit(OpCodes.Stfld, backingField);  // this.__fieldName = value
            setterIL.Emit(OpCodes.Ret);

            // Only link to PropertyBuilder for non-readonly (C# interop visibility)
            if (!field.IsReadonly)
            {
                property.SetSetMethod(setter);
            }

            // Track setter for direct dispatch ONLY for non-readonly fields (using PascalCase key)
            // This enforces readonly semantics at compile-time while allowing
            // constructor assignment via runtime SetFieldsProperty
            if (!field.IsReadonly)
            {
                if (!_instanceSetters.TryGetValue(className, out var classSetters))
                {
                    classSetters = [];
                    _instanceSetters[className] = classSetters;
                }
                classSetters[pascalName] = setter;
            }
        }
    }

    /// <summary>
    /// Emits IL to convert the value on the stack to the target type.
    /// </summary>
    private static void EmitTypeConversion(ILGenerator il, ILEmitter emitter, Expr source, Type targetType)
    {
        if (targetType == typeof(object))
        {
            // Need to box value types
            emitter.EmitBoxIfNeeded(source);
        }
        else if (targetType == typeof(double))
        {
            // Ensure we have a double on the stack
            emitter.EnsureDouble();
        }
        else if (targetType == typeof(bool))
        {
            // Ensure we have a bool on the stack
            emitter.EnsureBoolean();
        }
        else if (targetType == typeof(string))
        {
            // Strings don't need conversion from string
            // But may need conversion from object
            emitter.EnsureString();
        }
        else if (targetType.IsValueType)
        {
            // For other value types, try to unbox
            emitter.EmitBoxIfNeeded(source);
            il.Emit(OpCodes.Unbox_Any, targetType);
        }
        else
        {
            // Reference types - box if needed then cast
            emitter.EmitBoxIfNeeded(source);
            if (targetType != typeof(object))
            {
                il.Emit(OpCodes.Castclass, targetType);
            }
        }
    }
}
