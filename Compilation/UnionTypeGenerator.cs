using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using SharpTS.TypeSystem;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Generates discriminated union types for TypeScript union types.
/// </summary>
/// <remarks>
/// Creates .NET struct types that represent TypeScript unions like <c>string | number</c>.
/// Each union type has:
/// <list type="bullet">
///   <item>A <c>_tag</c> field to discriminate which type is active</item>
///   <item>Value fields for each union member type</item>
///   <item><c>IsXxx</c> properties for type checking</item>
///   <item><c>AsXxx</c> properties for type-safe access</item>
///   <item>Implicit conversion operators from each member type</item>
///   <item>A <c>Value</c> property returning the boxed current value</item>
/// </list>
/// </remarks>
public class UnionTypeGenerator
{
    private readonly Dictionary<string, TypeBuilder> _unionTypeBuilders = new();
    private readonly Dictionary<string, Type> _finalizedUnions = new();
    private readonly Dictionary<(string unionKey, Type fromType), MethodBuilder> _implicitConversions = new();
    private readonly TypeMapper _typeMapper;

    /// <summary>
    /// The union type marker interface to implement. When compiling to standalone DLLs,
    /// this is set to the emitted $IUnionType interface. Otherwise, defaults to IUnionType.
    /// </summary>
    public Type UnionTypeInterface { get; set; } = typeof(IUnionType);

    public UnionTypeGenerator(TypeMapper typeMapper)
    {
        _typeMapper = typeMapper;
    }

    /// <summary>
    /// Gets the implicit conversion method from a source type to a union type.
    /// Returns the MethodBuilder if not yet finalized, or the finalized MethodInfo.
    /// </summary>
    public MethodInfo? GetImplicitConversion(Type unionType, Type fromType)
    {
        string key = unionType.Name.Replace("Union_", "");

        // Check if we have a stored MethodBuilder for this conversion
        if (_implicitConversions.TryGetValue((key, fromType), out var methodBuilder))
            return methodBuilder;

        // If finalized, try reflection
        if (_finalizedUnions.TryGetValue(key, out var finalized))
        {
            return finalized.GetMethod("op_Implicit",
                BindingFlags.Public | BindingFlags.Static,
                null, [fromType], null);
        }

        return null;
    }

    /// <summary>
    /// Gets or creates a discriminated union type for the given TypeScript union.
    /// Returns a TypeBuilder that will be finalized later.
    /// </summary>
    public Type GetOrCreateUnionType(TSTypeInfo.Union union, ModuleBuilder moduleBuilder)
    {
        string key = GetUnionKey(union);

        // Return finalized type if already created
        if (_finalizedUnions.TryGetValue(key, out var finalized))
            return finalized;

        // Return existing TypeBuilder if already defined
        if (_unionTypeBuilders.TryGetValue(key, out var existing))
            return existing;

        var typeBuilder = GenerateUnionTypeBuilder(union, moduleBuilder, key);
        _unionTypeBuilders[key] = typeBuilder;
        return typeBuilder;
    }

    /// <summary>
    /// Finalizes all union types by calling CreateType().
    /// Must be called before assembly metadata generation.
    /// </summary>
    public void FinalizeAllUnionTypes()
    {
        foreach (var (key, typeBuilder) in _unionTypeBuilders)
        {
            if (!_finalizedUnions.ContainsKey(key))
            {
                _finalizedUnions[key] = typeBuilder.CreateType()!;
            }
        }
    }

    /// <summary>
    /// Generates a unique key for a union type based on its member types.
    /// </summary>
    private string GetUnionKey(TSTypeInfo.Union union)
    {
        var types = union.FlattenedTypes
            .Select(GetTypeKey)
            .OrderBy(t => t)
            .ToList();
        return string.Join("_", types);
    }

    private string GetTypeKey(TSTypeInfo type) => type switch
    {
        TSTypeInfo.Primitive p => p.Type.ToString().Replace("TYPE_", ""),
        TSTypeInfo.String => "STRING", // New String type
        TSTypeInfo.Class c => c.Name,
        TSTypeInfo.Instance i when i.ClassType is TSTypeInfo.Class c => c.Name,
        TSTypeInfo.Array => "Array",
        TSTypeInfo.Null => "Null",
        TSTypeInfo.Void => "Void",
        TSTypeInfo.Any => "Any",
        TSTypeInfo.BigInt => "BigInt",
        TSTypeInfo.Date => "Date",
        TSTypeInfo.RegExp => "RegExp",
        TSTypeInfo.Symbol => "Symbol",
        TSTypeInfo.Function => "Function",
        TSTypeInfo.Promise => "Promise",
        TSTypeInfo.Record => "Object",
        TSTypeInfo.Map => "Map",
        TSTypeInfo.Set => "Set",
        TSTypeInfo.WeakMap => "WeakMap",
        TSTypeInfo.WeakSet => "WeakSet",
        _ => type.GetType().Name
    };

    private TypeBuilder GenerateUnionTypeBuilder(TSTypeInfo.Union union, ModuleBuilder moduleBuilder, string key)
    {
        var types = union.FlattenedTypes;
        string typeName = $"Union_{key}";

        // Define the union as a struct implementing the union type marker interface
        var typeBuilder = moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [UnionTypeInterface]
        );

        // Add StructLayout attribute for auto layout
        var structLayoutCtor = typeof(StructLayoutAttribute).GetConstructor([typeof(LayoutKind)])!;
        var structLayoutAttr = new CustomAttributeBuilder(structLayoutCtor, [LayoutKind.Auto]);
        typeBuilder.SetCustomAttribute(structLayoutAttr);

        // Define _tag field (byte for efficiency, supports up to 256 union members)
        // Note: Cannot use InitOnly because implicit conversion operators need to set fields
        var tagField = typeBuilder.DefineField("_tag", typeof(byte), FieldAttributes.Private);

        // Define value fields for each type
        List<FieldBuilder> valueFields = [];
        List<Type> mappedTypes = [];

        for (int i = 0; i < types.Count; i++)
        {
            Type mappedType = _typeMapper.MapTypeInfoStrict(types[i]);
            mappedTypes.Add(mappedType);

            var field = typeBuilder.DefineField(
                $"_v{i}",
                mappedType,
                FieldAttributes.Private
            );
            valueFields.Add(field);
        }

        // Generate constructor that takes tag and all values
        EmitPrivateConstructor(typeBuilder, tagField, valueFields, mappedTypes);

        // Generate IsXxx properties
        for (int i = 0; i < types.Count; i++)
        {
            string propName = GetTypePropertyName(types[i], i);
            EmitIsProperty(typeBuilder, tagField, i, propName);
        }

        // Generate AsXxx properties with type checking
        for (int i = 0; i < types.Count; i++)
        {
            string propName = GetTypePropertyName(types[i], i);
            EmitAsProperty(typeBuilder, tagField, valueFields[i], mappedTypes[i], i, propName);
        }

        // Generate Value property (returns boxed object) - needed for runtime access
        var valueGetter = EmitValueProperty(typeBuilder, tagField, valueFields, mappedTypes);

        // Generate implicit conversion operators - needed for typed parameter passing
        for (int i = 0; i < types.Count; i++)
        {
            var conversionMethod = EmitImplicitConversion(typeBuilder, tagField, valueFields, mappedTypes, i);
            if (conversionMethod != null)
            {
                _implicitConversions[(key, mappedTypes[i])] = conversionMethod;
            }
        }

        // Generate ToString override
        EmitToString(typeBuilder, tagField, valueFields, mappedTypes, types);

        // Generate Equals and GetHashCode (pass valueGetter to avoid GetMethod on TypeBuilder)
        EmitEquals(typeBuilder, tagField, valueFields, mappedTypes, valueGetter);
        EmitGetHashCode(typeBuilder, tagField, valueFields, mappedTypes, valueGetter);

        return typeBuilder;
    }

    private string GetTypePropertyName(TSTypeInfo type, int index) => type switch
    {
        TSTypeInfo.String => "String", // New String type
        TSTypeInfo.Primitive { Type: Parsing.TokenType.TYPE_STRING } => "String",
        TSTypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } => "Number",
        TSTypeInfo.Primitive { Type: Parsing.TokenType.TYPE_BOOLEAN } => "Boolean",
        TSTypeInfo.Class c => c.Name,
        TSTypeInfo.Instance i when i.ClassType is TSTypeInfo.Class c => c.Name,
        TSTypeInfo.Null => "Null",
        TSTypeInfo.Array => "Array",
        TSTypeInfo.BigInt => "BigInt",
        TSTypeInfo.Date => "Date",
        TSTypeInfo.Function => "Function",
        _ => $"Type{index}"
    };

    private void EmitPrivateConstructor(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes)
    {
        // Constructor takes (byte tag, T0 v0, T1 v1, ...)
        var paramTypes = new Type[mappedTypes.Count + 1];
        paramTypes[0] = typeof(byte);
        for (int i = 0; i < mappedTypes.Count; i++)
            paramTypes[i + 1] = mappedTypes[i];

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            paramTypes
        );

        var il = ctor.GetILGenerator();

        // this._tag = tag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, tagField);

        // this._vN = vN
        for (int i = 0; i < valueFields.Count; i++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg, i + 2);
            il.Emit(OpCodes.Stfld, valueFields[i]);
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitIsProperty(TypeBuilder typeBuilder, FieldBuilder tagField, int index, string typeName)
    {
        var property = typeBuilder.DefineProperty(
            $"Is{typeName}",
            PropertyAttributes.None,
            typeof(bool),
            null
        );

        var getter = typeBuilder.DefineMethod(
            $"get_Is{typeName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(bool),
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();

        // return _tag == index
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    private void EmitAsProperty(TypeBuilder typeBuilder, FieldBuilder tagField, FieldBuilder valueField,
        Type valueType, int index, string typeName)
    {
        var property = typeBuilder.DefineProperty(
            $"As{typeName}",
            PropertyAttributes.None,
            valueType,
            null
        );

        var getter = typeBuilder.DefineMethod(
            $"get_As{typeName}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            valueType,
            Type.EmptyTypes
        );

        var il = getter.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // if (_tag != index) throw new InvalidCastException()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Beq_S, returnLabel);

        // throw new InvalidCastException("...")
        il.Emit(OpCodes.Ldstr, $"Cannot access As{typeName} when union holds a different type");
        var exceptionCtor = typeof(InvalidCastException).GetConstructor([typeof(string)])!;
        il.Emit(OpCodes.Newobj, exceptionCtor);
        il.Emit(OpCodes.Throw);

        // return _vN
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, valueField);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    private MethodBuilder EmitValueProperty(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes)
    {
        var property = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(object),
            null
        );

        // Must be Virtual to implement interface method
        var getter = typeBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(object),
            Type.EmptyTypes
        );

        // Map this method to the interface's get_Value
        var interfaceGetter = UnionTypeInterface.GetProperty("Value")?.GetGetMethod();
        if (interfaceGetter != null)
        {
            typeBuilder.DefineMethodOverride(getter, interfaceGetter);
        }

        var il = getter.GetILGenerator();
        var labels = new Label[valueFields.Count];
        var endLabel = il.DefineLabel();

        for (int i = 0; i < valueFields.Count; i++)
            labels[i] = il.DefineLabel();

        // switch (_tag)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Switch, labels);

        // default: return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br_S, endLabel);

        // case N: return (object)_vN
        for (int i = 0; i < valueFields.Count; i++)
        {
            il.MarkLabel(labels[i]);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, valueFields[i]);

            // Box value types
            if (mappedTypes[i].IsValueType)
                il.Emit(OpCodes.Box, mappedTypes[i]);

            il.Emit(OpCodes.Br_S, endLabel);
        }

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        return getter;
    }

    private MethodBuilder? EmitImplicitConversion(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes, int index)
    {
        Type fromType = mappedTypes[index];

        // Skip if the type is object or void (can't have implicit conversion from object)
        if (fromType == typeof(object) || fromType == typeof(void))
            return null;

        var method = typeBuilder.DefineMethod(
            "op_Implicit",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeBuilder,
            [fromType]
        );

        var il = method.GetILGenerator();

        // Create new instance by initializing local and setting fields directly
        // (TypeBuilder doesn't support GetConstructor before CreateType)
        var local = il.DeclareLocal(typeBuilder);

        // Initialize local to default
        il.Emit(OpCodes.Ldloca_S, local);
        il.Emit(OpCodes.Initobj, typeBuilder);

        // Set tag field
        il.Emit(OpCodes.Ldloca_S, local);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Stfld, tagField);

        // Set value field
        il.Emit(OpCodes.Ldloca_S, local);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, valueFields[index]);

        // Return the local
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitDefaultValue(ILGenerator il, Type type)
    {
        if (!type.IsValueType)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else if (type == typeof(int) || type == typeof(byte) || type == typeof(bool))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(long))
        {
            il.Emit(OpCodes.Ldc_I8, 0L);
        }
        else if (type == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, 0f);
        }
        else if (type == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else
        {
            // For other value types, use a local
            var local = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldloca_S, local);
            il.Emit(OpCodes.Initobj, type);
            il.Emit(OpCodes.Ldloc, local);
        }
    }

    private void EmitToString(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes, List<TSTypeInfo> types)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(string),
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var labels = new Label[valueFields.Count];
        var endLabel = il.DefineLabel();

        for (int i = 0; i < valueFields.Count; i++)
            labels[i] = il.DefineLabel();

        // switch (_tag)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Switch, labels);

        // default: return ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br_S, endLabel);

        // case N: return value.ToString() (just the value, no type prefix for TypeScript semantics)
        for (int i = 0; i < valueFields.Count; i++)
        {
            il.MarkLabel(labels[i]);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, valueFields[i]);

            // Box and call ToString
            if (mappedTypes[i].IsValueType)
                il.Emit(OpCodes.Box, mappedTypes[i]);

            var toStringMethod = typeof(object).GetMethod("ToString", Type.EmptyTypes)!;
            il.Emit(OpCodes.Callvirt, toStringMethod);

            il.Emit(OpCodes.Br_S, endLabel);
        }

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        // Override object.ToString
        typeBuilder.DefineMethodOverride(method, typeof(object).GetMethod("ToString")!);
    }

    private void EmitEquals(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes, MethodBuilder valueGetter)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(bool),
            [typeof(object)]
        );

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (obj is not UnionType other) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Brfalse_S, falseLabel);

        // if (this._tag != other._tag) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, typeBuilder);
        var otherLocal = il.DeclareLocal(typeBuilder);
        il.Emit(OpCodes.Stloc, otherLocal);
        il.Emit(OpCodes.Ldloca_S, otherLocal);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Bne_Un_S, falseLabel);

        // Compare Value properties (simplified - uses boxed comparison)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueGetter);
        il.Emit(OpCodes.Ldloca_S, otherLocal);
        il.Emit(OpCodes.Call, valueGetter);
        var equalsMethod = typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!;
        il.Emit(OpCodes.Call, equalsMethod);
        il.Emit(OpCodes.Br_S, endLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(method, typeof(object).GetMethod("Equals", [typeof(object)])!);
    }

    private void EmitGetHashCode(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes, MethodBuilder valueGetter)
    {
        var method = typeBuilder.DefineMethod(
            "GetHashCode",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // return _tag ^ (Value?.GetHashCode() ?? 0);
        // Load tag as int
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Conv_I4);

        // Get Value and call GetHashCode if not null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, valueGetter);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue_S, notNullLabel);

        // null case: use 0
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br_S, endLabel);

        // not null: call GetHashCode
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetHashCode")!);

        il.MarkLabel(endLabel);
        // XOR tag with value hash
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(method, typeof(object).GetMethod("GetHashCode")!);
    }
}
