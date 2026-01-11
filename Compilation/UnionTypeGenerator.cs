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
    private readonly Dictionary<string, Type> _generatedUnions = new();
    private readonly TypeMapper _typeMapper;

    public UnionTypeGenerator(TypeMapper typeMapper)
    {
        _typeMapper = typeMapper;
    }

    /// <summary>
    /// Gets or creates a discriminated union type for the given TypeScript union.
    /// </summary>
    public Type GetOrCreateUnionType(TSTypeInfo.Union union, ModuleBuilder moduleBuilder)
    {
        string key = GetUnionKey(union);

        if (_generatedUnions.TryGetValue(key, out var existing))
            return existing;

        var unionType = GenerateUnionType(union, moduleBuilder, key);
        _generatedUnions[key] = unionType;
        return unionType;
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

    private Type GenerateUnionType(TSTypeInfo.Union union, ModuleBuilder moduleBuilder, string key)
    {
        var types = union.FlattenedTypes;
        string typeName = $"Union_{key}";

        // Define the union as a struct
        var typeBuilder = moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType)
        );

        // Add StructLayout attribute for auto layout
        var structLayoutCtor = typeof(StructLayoutAttribute).GetConstructor([typeof(LayoutKind)])!;
        var structLayoutAttr = new CustomAttributeBuilder(structLayoutCtor, [LayoutKind.Auto]);
        typeBuilder.SetCustomAttribute(structLayoutAttr);

        // Define _tag field (byte for efficiency, supports up to 256 union members)
        var tagField = typeBuilder.DefineField("_tag", typeof(byte), FieldAttributes.Private | FieldAttributes.InitOnly);

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
                FieldAttributes.Private | FieldAttributes.InitOnly
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

        // Generate Value property (returns boxed object)
        EmitValueProperty(typeBuilder, tagField, valueFields, mappedTypes);

        // Generate implicit conversion operators
        for (int i = 0; i < types.Count; i++)
        {
            EmitImplicitConversion(typeBuilder, tagField, valueFields, mappedTypes, i);
        }

        // Generate ToString override
        EmitToString(typeBuilder, tagField, valueFields, mappedTypes, types);

        // Generate Equals and GetHashCode
        EmitEquals(typeBuilder, tagField, valueFields, mappedTypes);
        EmitGetHashCode(typeBuilder, tagField, valueFields, mappedTypes);

        return typeBuilder.CreateType()!;
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

    private void EmitValueProperty(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes)
    {
        var property = typeBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(object),
            null
        );

        var getter = typeBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(object),
            Type.EmptyTypes
        );

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
    }

    private void EmitImplicitConversion(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes, int index)
    {
        Type fromType = mappedTypes[index];

        // Skip if the type is object or void (can't have implicit conversion from object)
        if (fromType == typeof(object) || fromType == typeof(void))
            return;

        var method = typeBuilder.DefineMethod(
            "op_Implicit",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeBuilder,
            [fromType]
        );

        var il = method.GetILGenerator();

        // Create new instance with tag=index and the value in the correct slot
        // new Union(tag, default, ..., value, ..., default)

        il.Emit(OpCodes.Ldc_I4, index); // tag

        for (int i = 0; i < mappedTypes.Count; i++)
        {
            if (i == index)
            {
                il.Emit(OpCodes.Ldarg_0); // The actual value
            }
            else
            {
                // Default value for other slots
                EmitDefaultValue(il, mappedTypes[i]);
            }
        }

        // Get the private constructor
        var paramTypes = new Type[mappedTypes.Count + 1];
        paramTypes[0] = typeof(byte);
        for (int i = 0; i < mappedTypes.Count; i++)
            paramTypes[i + 1] = mappedTypes[i];

        // We need to use Newobj with the constructor, but TypeBuilder doesn't have GetConstructor
        // So we'll emit a local and set fields directly
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

        // default: return "Unknown"
        il.Emit(OpCodes.Ldstr, "Unknown");
        il.Emit(OpCodes.Br_S, endLabel);

        // case N: return $"{TypeName}: {value}"
        for (int i = 0; i < valueFields.Count; i++)
        {
            il.MarkLabel(labels[i]);

            string typeName = GetTypePropertyName(types[i], i);
            il.Emit(OpCodes.Ldstr, $"{typeName}: ");

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, valueFields[i]);

            // Box and call ToString
            if (mappedTypes[i].IsValueType)
                il.Emit(OpCodes.Box, mappedTypes[i]);

            var toStringMethod = typeof(object).GetMethod("ToString", Type.EmptyTypes)!;
            il.Emit(OpCodes.Callvirt, toStringMethod);

            var concatMethod = typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!;
            il.Emit(OpCodes.Call, concatMethod);

            il.Emit(OpCodes.Br_S, endLabel);
        }

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        // Override object.ToString
        typeBuilder.DefineMethodOverride(method, typeof(object).GetMethod("ToString")!);
    }

    private void EmitEquals(TypeBuilder typeBuilder, FieldBuilder tagField,
        List<FieldBuilder> valueFields, List<Type> mappedTypes)
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
        il.Emit(OpCodes.Call, typeBuilder.GetMethod("get_Value")!);
        il.Emit(OpCodes.Ldloca_S, otherLocal);
        il.Emit(OpCodes.Call, typeBuilder.GetMethod("get_Value")!);
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
        List<FieldBuilder> valueFields, List<Type> mappedTypes)
    {
        var method = typeBuilder.DefineMethod(
            "GetHashCode",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // return HashCode.Combine(_tag, Value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, tagField);
        il.Emit(OpCodes.Conv_I4);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeBuilder.GetMethod("get_Value")!);

        var hashCombineMethod = typeof(HashCode).GetMethod("Combine", [typeof(int), typeof(object)])!;
        il.Emit(OpCodes.Call, hashCombineMethod);
        il.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(method, typeof(object).GetMethod("GetHashCode")!);
    }
}
