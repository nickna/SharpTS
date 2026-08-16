using System.Reflection.Emit;
using SharpTS.Declaration;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.DotNet;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    /// <summary>
    /// Tries to emit static property get access on an external .NET type (via @DotNetType).
    /// Returns true if successful, false if property not found.
    /// </summary>
    private bool TryEmitExternalStaticPropertyGet(Type externalType, string propertyName)
    {
        // Try PascalCase first (most .NET properties use PascalCase)
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Try to find a static property (first PascalCase, then original name)
        var property = ManagedDotNetInterop.GetProperty(
                           externalType, pascalPropertyName,
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Static)
                       ?? ManagedDotNetInterop.GetProperty(
                           externalType, propertyName,
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Static);
        if (property != null &&
            DotNetInteropClassifier.UnsupportedSlotReason(property.PropertyType) != null)
        {
            property = null;
        }

        if (property != null)
        {
            var getter = property.GetGetMethod();
            if (getter != null)
            {
                IL.Emit(OpCodes.Call, getter);

                if (property.PropertyType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, property.PropertyType);
                }
                SetStackUnknown();
                return true;
            }
        }

        // Try to find a static field
        var field = ManagedDotNetInterop.GetField(
                        externalType, pascalPropertyName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static)
                    ?? ManagedDotNetInterop.GetField(
                        externalType, propertyName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static);
        if (field != null &&
            DotNetInteropClassifier.UnsupportedSlotReason(field.FieldType) != null)
        {
            field = null;
        }

        if (field != null)
        {
            // Literal (const) fields — enum members included — have no storage slot, so
            // Ldsfld is invalid IL on them. Embed the compile-time constant instead.
            if (field.IsLiteral)
            {
                EmitExternalLiteralField(field);
                SetStackUnknown();
                return true;
            }

            IL.Emit(OpCodes.Ldsfld, field);

            if (field.FieldType.IsValueType)
            {
                IL.Emit(OpCodes.Box, field.FieldType);
            }
            SetStackUnknown();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Loads a literal (const) field's value as a boxed object. Enum members are boxed to the
    /// enum type (so they round-trip into .NET parameters and ToString correctly); numeric
    /// constants normalize to double, mirroring the interpreter's <c>DotNetMarshaller.WrapReturn</c>.
    /// </summary>
    private void EmitExternalLiteralField(System.Reflection.FieldInfo field)
    {
        object? raw = field.GetRawConstantValue();

        if (field.FieldType.IsEnum && raw != null)
        {
            // Box expects the underlying integral on the stack when boxing an enum type.
            var underlying = Enum.GetUnderlyingType(field.FieldType);
            if (underlying == typeof(long) || underlying == typeof(ulong))
            {
                IL.Emit(OpCodes.Ldc_I8, raw is ulong ul ? unchecked((long)ul) : Convert.ToInt64(raw));
            }
            else
            {
                IL.Emit(OpCodes.Ldc_I4, raw is uint ui ? unchecked((int)ui) : Convert.ToInt32(raw));
            }
            IL.Emit(OpCodes.Box, field.FieldType);
            return;
        }

        switch (raw)
        {
            case null:
                IL.Emit(OpCodes.Ldnull);
                break;
            case string s:
                IL.Emit(OpCodes.Ldstr, s);
                break;
            case bool b:
                IL.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                break;
            case char c:
                // The interpreter's WrapReturn maps char to a one-character string; match it.
                IL.Emit(OpCodes.Ldstr, c.ToString());
                break;
            default:
                IL.Emit(OpCodes.Ldc_R8, Convert.ToDouble(raw));
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                break;
        }
    }

    /// <summary>
    /// Returns true if the expression is <c>this.name</c> inside a static method/block context
    /// and a matching static field exists on the current class, and outputs the resolved
    /// class name and FieldBuilder. Used by postfix/prefix/compound operators to bypass
    /// runtime GetProperty/SetProperty and emit direct static-field IL.
    /// </summary>
    private bool TryResolveStaticThisField(Expr.Get get, out string className, out System.Reflection.Emit.FieldBuilder field)
    {
        className = null!;
        field = null!;
        if (get.Object is not Expr.This) return false;
        if (_ctx.IsInstanceMethod || _ctx.CurrentClassBuilder == null) return false;
        var name = _ctx.CurrentClassName;
        if (name == null) return false;
        // Own-only: this resolver feeds read-modify-write operator paths (postfix/prefix/compound),
        // which must bind to a field the class itself declares (inherited-field writes create a shadow).
        if (!_ctx.ClassRegistry!.TryGetOwnStaticField(name, get.Name.Lexeme, out var fb) || fb == null)
            return false;
        className = name;
        field = fb;
        return true;
    }

    /// <summary>
    /// Emits static member access on a class (used for both ClassName.property and this.property in static context).
    /// Returns true if successful, false if property not found.
    /// </summary>
    private bool EmitStaticMemberAccess(string className, System.Reflection.Emit.TypeBuilder classBuilder, string propertyName)
    {
        // Try static getter first (for auto-accessors and explicit static accessors)
        if (_ctx.ClassRegistry!.TryGetCallableStaticGetter(className, propertyName, classBuilder, out var staticGetter))
        {
            IL.Emit(OpCodes.Call, staticGetter!);

            // Track the stack type for proper boxing behavior
            string pascalPropName = NamingConventions.ToPascalCase(propertyName);
            if (_ctx.PropertyTypes != null &&
                _ctx.PropertyTypes.TryGetValue(className, out var propTypes) &&
                propTypes.TryGetValue(pascalPropName, out var propType))
            {
                if (propType == _ctx.Types.Double)
                {
                    SetStackType(StackType.Double);
                }
                else if (propType == _ctx.Types.Boolean)
                {
                    SetStackType(StackType.Boolean);
                }
                else if (propType == _ctx.Types.String)
                {
                    SetStackType(StackType.String);
                }
                else
                {
                    SetStackUnknown();
                }
            }
            else
            {
                SetStackUnknown();
            }
            return true;
        }

        // Try to find static field using stored FieldBuilders
        if (_ctx.ClassRegistry!.TryGetCallableStaticField(className, propertyName, classBuilder, out var staticField))
        {
            EmitStaticFieldLoadWithShadow(className, classBuilder, propertyName, staticField!);
            return true;
        }

        // Try static private fields - strip leading # if present
        string privateName = propertyName.StartsWith('#') ? propertyName[1..] : propertyName;
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, privateName, out var staticPrivateField))
        {
            IL.Emit(OpCodes.Ldsfld, staticPrivateField!);
            SetStackUnknown();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits static member set on a class (used for both ClassName.property = value and this.property = value in static context).
    /// Returns true if successful, false if property not found.
    /// </summary>
    private bool EmitStaticMemberSet(string className, System.Reflection.Emit.TypeBuilder classBuilder, string propertyName, Expr value)
    {
        // Try static setter first (for auto-accessors and explicit static accessors)
        if (_ctx.ClassRegistry!.TryGetCallableStaticSetter(className, propertyName, classBuilder, out var staticSetter))
        {
            // The setter's parameter type is authoritative — auto-accessors use typed params
            // (Double/Boolean/String), explicit accessors use Object. Either way this matches
            // the IL signature, avoiding stack/arg mismatches on the Call below.
            Type propertyType = staticSetter!.GetParameters()[0].ParameterType;

            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            var staticSetterResultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, staticSetterResultTemp);

            if (propertyType.IsValueType)
            {
                IL.Emit(OpCodes.Unbox_Any, propertyType);
            }
            else if (!_ctx.Types.IsObject(propertyType))
            {
                IL.Emit(OpCodes.Castclass, propertyType);
            }

            IL.Emit(OpCodes.Call, staticSetter!);

            // Auto-accessor setters return void; explicit accessor setters return object.
            // Pop the setter's return value to keep the stack balanced.
            if (staticSetter!.ReturnType != typeof(void))
            {
                IL.Emit(OpCodes.Pop);
            }

            IL.Emit(OpCodes.Ldloc, staticSetterResultTemp);
            return true;
        }

        // Try static fields own-only — a data-field write through a subclass creates an own shadow,
        // it must not mutate the base's storage (see TryGetOwnCallableStaticField).
        if (_ctx.ClassRegistry!.TryGetOwnCallableStaticField(className, propertyName, classBuilder, out var callableStaticField))
        {
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            IL.Emit(OpCodes.Stsfld, callableStaticField!);
            return true;
        }

        // Try static private fields
        string privateFieldName = propertyName.StartsWith('#') ? propertyName[1..] : propertyName;
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, privateFieldName, out var staticPrivateField))
        {
            EmitExpression(value);
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            IL.Emit(OpCodes.Stsfld, staticPrivateField!);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits property get access on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalPropertyGet(Expr receiver, Type externalType, string propertyName)
    {
        // Try PascalCase first (most .NET properties use PascalCase)
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Try to find the property (first PascalCase, then original name)
        var property = ManagedDotNetInterop.GetProperty(
                           externalType, pascalPropertyName,
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Instance)
                       ?? ManagedDotNetInterop.GetProperty(
                           externalType, propertyName,
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Instance);
        if (property != null &&
            DotNetInteropClassifier.UnsupportedSlotReason(property.PropertyType) != null)
        {
            property = null;
        }

        if (property == null)
        {
            // Try to find a field instead
            var field = ManagedDotNetInterop.GetField(
                            externalType, pascalPropertyName,
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance)
                        ?? ManagedDotNetInterop.GetField(
                            externalType, propertyName,
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance);
            if (field != null &&
                DotNetInteropClassifier.UnsupportedSlotReason(field.FieldType) != null)
            {
                field = null;
            }

            if (field != null)
            {
                // Emit field access
                EmitExpression(receiver);
                EmitBoxIfNeeded(receiver);
                PrepareReceiverForMemberAccess(externalType);
                IL.Emit(OpCodes.Ldfld, field);
                BoxResultIfValueType(field.FieldType);
                SetStackUnknown();
                return;
            }

            throw new CompileException($"Property or field '{propertyName}' not found on external type {externalType.FullName}");
        }

        var getter = property.GetGetMethod();
        if (getter == null)
        {
            throw new CompileException($"Property '{property.Name}' on external type {externalType.FullName} has no getter");
        }

        // Emit property access
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        bool isValueType = PrepareReceiverForMemberAccess(externalType);
        IL.Emit(isValueType ? OpCodes.Call : OpCodes.Callvirt, getter);
        BoxResultIfValueType(property.PropertyType);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits property set access on an external .NET type (via @DotNetType).
    /// </summary>
    private void EmitExternalPropertySet(Expr receiver, Type externalType, string propertyName, Expr value)
    {
        // Try PascalCase first (most .NET properties use PascalCase)
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Try to find the property (first PascalCase, then original name)
        var property = ManagedDotNetInterop.GetProperty(
                           externalType, pascalPropertyName,
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Instance)
                       ?? ManagedDotNetInterop.GetProperty(
                           externalType, propertyName,
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.Instance);
        if (property != null &&
            DotNetInteropClassifier.UnsupportedSlotReason(property.PropertyType) != null)
        {
            property = null;
        }

        if (property == null)
        {
            // Try to find a field instead
            var field = ManagedDotNetInterop.GetField(
                            externalType, pascalPropertyName,
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance)
                        ?? ManagedDotNetInterop.GetField(
                            externalType, propertyName,
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance);
            if (field != null &&
                DotNetInteropClassifier.UnsupportedSlotReason(field.FieldType) != null)
            {
                field = null;
            }

            if (field != null)
            {
                // Emit field set
                EmitExpression(receiver);
                EmitBoxIfNeeded(receiver);
                IL.Emit(OpCodes.Castclass, externalType);
                EmitExpression(value);
                EmitExternalTypeConversion(field.FieldType);

                // Save value for expression result
                IL.Emit(OpCodes.Dup);
                var valueTemp = IL.DeclareLocal(field.FieldType);
                IL.Emit(OpCodes.Stloc, valueTemp);

                IL.Emit(OpCodes.Stfld, field);

                // Put value back on stack as boxed result
                IL.Emit(OpCodes.Ldloc, valueTemp);
                if (field.FieldType.IsValueType)
                {
                    IL.Emit(OpCodes.Box, field.FieldType);
                }
                SetStackUnknown();
                return;
            }

            throw new CompileException($"Property or field '{propertyName}' not found on external type {externalType.FullName}");
        }

        var setter = property.GetSetMethod();
        if (setter == null)
        {
            throw new CompileException($"Property '{property.Name}' on external type {externalType.FullName} has no setter");
        }

        // Emit property set
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, externalType);
        EmitExpression(value);
        EmitExternalTypeConversion(property.PropertyType);

        // Save value for expression result
        IL.Emit(OpCodes.Dup);
        var propValueTemp = IL.DeclareLocal(property.PropertyType);
        IL.Emit(OpCodes.Stloc, propValueTemp);

        IL.Emit(OpCodes.Callvirt, setter);

        // Put value back on stack as boxed result
        IL.Emit(OpCodes.Ldloc, propValueTemp);
        if (property.PropertyType.IsValueType)
        {
            IL.Emit(OpCodes.Box, property.PropertyType);
        }
        SetStackUnknown();
    }
}
