using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Object construction (new expressions) emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitNew(Expr.New n)
    {
        // Built-in types only apply when there's no namespace path
        bool isSimpleName = n.NamespacePath == null || n.NamespacePath.Count == 0;

        // Special case: new Date(...) constructor
        if (isSimpleName && n.ClassName.Lexeme == "Date")
        {
            EmitNewDate(n.Arguments);
            return;
        }

        // Special case: new Map(...) constructor
        if (isSimpleName && n.ClassName.Lexeme == "Map")
        {
            EmitNewMap(n.Arguments);
            return;
        }

        // Special case: new Set(...) constructor
        if (isSimpleName && n.ClassName.Lexeme == "Set")
        {
            EmitNewSet(n.Arguments);
            return;
        }

        // Special case: new WeakMap() constructor
        if (isSimpleName && n.ClassName.Lexeme == "WeakMap")
        {
            EmitNewWeakMap();
            return;
        }

        // Special case: new WeakSet() constructor
        if (isSimpleName && n.ClassName.Lexeme == "WeakSet")
        {
            EmitNewWeakSet();
            return;
        }

        // Special case: new RegExp(...) constructor
        if (isSimpleName && n.ClassName.Lexeme == "RegExp")
        {
            EmitNewRegExp(n.Arguments);
            return;
        }

        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (n.NamespacePath != null && n.NamespacePath.Count > 0)
        {
            // Build qualified name for namespace classes: Namespace_SubNs_ClassName
            string nsPath = string.Join("_", n.NamespacePath.Select(t => t.Lexeme));
            resolvedClassName = $"{nsPath}_{n.ClassName.Lexeme}";
        }
        else
        {
            resolvedClassName = _ctx.ResolveClassName(n.ClassName.Lexeme);
        }

        // Check for external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(n.ClassName.Lexeme, out var externalType) ||
            _ctx.TypeMapper.ExternalTypes.TryGetValue(resolvedClassName, out externalType))
        {
            EmitExternalTypeConstruction(externalType, n.Arguments);
            return;
        }

        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) &&
            _ctx.ClassConstructors != null &&
            _ctx.ClassConstructors.TryGetValue(resolvedClassName, out var ctorBuilder))
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation (e.g., new Box<number>(42))
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassGenericParams?.TryGetValue(resolvedClassName, out var _) == true)
            {
                // Resolve type arguments
                Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();

                // Create the constructed generic type
                targetType = typeBuilder.MakeGenericType(typeArgs);

                // Get the constructor on the constructed type
                targetCtor = TypeBuilder.GetConstructor(targetType, ctorBuilder);
            }

            // Get constructor parameters for typed emission
            var ctorParams = ctorBuilder.GetParameters();
            int expectedParamCount = ctorParams.Length;

            // Emit arguments with proper type conversions
            for (int i = 0; i < n.Arguments.Count; i++)
            {
                EmitExpression(n.Arguments[i]);
                if (i < ctorParams.Length)
                {
                    EmitConversionForParameter(n.Arguments[i], ctorParams[i].ParameterType);
                }
                else
                {
                    EmitBoxIfNeeded(n.Arguments[i]);
                }
            }

            // Pad missing optional arguments with appropriate default values
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                EmitDefaultForType(ctorParams[i].ParameterType);
            }

            // Call the constructor directly using newobj
            IL.Emit(OpCodes.Newobj, targetCtor);
            SetStackUnknown();  // newobj returns object reference
        }
        else if (_ctx.VarToClassExpr != null &&
                 _ctx.VarToClassExpr.TryGetValue(n.ClassName.Lexeme, out var classExpr) &&
                 _ctx.ClassExprConstructors != null &&
                 _ctx.ClassExprConstructors.TryGetValue(classExpr, out var classExprCtor))
        {
            // Class expression with known constructor - use direct newobj (handles default parameters)
            var classExprCtorParams = classExprCtor.GetParameters();
            int expectedParamCount = classExprCtorParams.Length;

            // Emit arguments with proper type conversions
            for (int i = 0; i < n.Arguments.Count; i++)
            {
                EmitExpression(n.Arguments[i]);
                if (i < classExprCtorParams.Length)
                {
                    EmitConversionForParameter(n.Arguments[i], classExprCtorParams[i].ParameterType);
                }
                else
                {
                    EmitBoxIfNeeded(n.Arguments[i]);
                }
            }

            // Pad missing optional arguments with appropriate default values
            for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            {
                EmitDefaultForType(classExprCtorParams[i].ParameterType);
            }

            // Call the constructor directly using newobj
            IL.Emit(OpCodes.Newobj, classExprCtor);
            SetStackUnknown();  // newobj returns object reference
        }
        else
        {
            // Fallback: try to instantiate via local variable (imported class as Type)
            var local = _ctx.Locals.GetLocal(n.ClassName.Lexeme);
            if (local != null)
            {
                // The local contains a Type object - use Activator.CreateInstance
                // Load the Type first
                IL.Emit(OpCodes.Ldloc, local);

                // Create an object array for the arguments
                IL.Emit(OpCodes.Ldc_I4, n.Arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

                for (int i = 0; i < n.Arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(n.Arguments[i]);
                    EmitBoxIfNeeded(n.Arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }

                // Call Activator.CreateInstance(Type, object[])
                // Stack: Type, object[]
                var createInstanceMethod = _ctx.Types.GetMethod(_ctx.Types.Activator, "CreateInstance", _ctx.Types.Type, _ctx.Types.ObjectArray);
                IL.Emit(OpCodes.Call, createInstanceMethod!);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
        }
    }

    /// <summary>
    /// Emits code for new Map(...) construction.
    /// </summary>
    private void EmitNewMap(List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            // new Map() - empty map
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateMap);
        }
        else
        {
            // new Map(entries) - map from entries
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateMapFromEntries);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new Set(...) construction.
    /// </summary>
    private void EmitNewSet(List<Expr> arguments)
    {
        if (arguments.Count == 0)
        {
            // new Set() - empty set
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateSet);
        }
        else
        {
            // new Set(values) - set from array
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateSetFromArray);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new WeakMap() construction.
    /// </summary>
    private void EmitNewWeakMap()
    {
        // new WeakMap() - empty weak map (no constructor arguments supported)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateWeakMap);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new WeakSet() construction.
    /// </summary>
    private void EmitNewWeakSet()
    {
        // new WeakSet() - empty weak set (no constructor arguments supported)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateWeakSet);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new Date(...) construction.
    /// </summary>
    private void EmitNewDate(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                // new Date() - current date/time
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateNoArgs);
                break;

            case 1:
                // new Date(value) - milliseconds or ISO string
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateFromValue);
                break;

            default:
                // new Date(year, month, day?, hours?, minutes?, seconds?, ms?)
                // Emit all 7 arguments, using 0 for missing ones
                for (int i = 0; i < 7; i++)
                {
                    if (i < arguments.Count)
                    {
                        EmitExpressionAsDouble(arguments[i]);
                    }
                    else
                    {
                        // Default values: day=1, others=0
                        IL.Emit(OpCodes.Ldc_R8, i == 2 ? 1.0 : 0.0);
                    }
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateFromComponents);
                break;
        }
        // All Date constructors return an object, reset stack type
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new RegExp(...) construction.
    /// </summary>
    private void EmitNewRegExp(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                // new RegExp() - empty pattern
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExpWithFlags);
                break;

            case 1:
                // new RegExp(pattern) - pattern only
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure pattern is a string
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExp);
                break;

            default:
                // new RegExp(pattern, flags) - pattern and flags
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure pattern is a string
                EmitExpression(arguments[1]);
                EmitBoxIfNeeded(arguments[1]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure flags is a string
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExpWithFlags);
                break;
        }
        SetStackUnknown();
    }
}
