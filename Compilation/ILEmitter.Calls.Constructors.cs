using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Object construction (new expressions) emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    /// <summary>
    /// Extracts the simple class name from a new expression callee for IL emission.
    /// </summary>
    private static string? GetSimpleClassName(Expr callee)
    {
        return callee is Expr.Variable v ? v.Name.Lexeme : null;
    }

    /// <summary>
    /// Checks if the callee is a simple identifier (not a member access or complex expression).
    /// </summary>
    private static bool IsSimpleIdentifier(Expr callee) => callee is Expr.Variable;

    /// <summary>
    /// Extracts a qualified class name from a callee expression for namespace paths.
    /// Returns (namespaceParts, className) tuple where namespaceParts may be empty for simple names.
    /// </summary>
    private static (List<string> namespaceParts, string className) ExtractQualifiedName(Expr callee)
    {
        List<string> parts = [];
        CollectGetChain(callee, parts);

        if (parts.Count == 0)
            return ([], "");

        var namespaceParts = parts.Count > 1 ? parts.Take(parts.Count - 1).ToList() : [];
        var className = parts[^1];
        return (namespaceParts, className);
    }

    /// <summary>
    /// Collects identifiers from a Get chain (e.g., Namespace.SubNs.Class) into a list.
    /// </summary>
    private static void CollectGetChain(Expr expr, List<string> parts)
    {
        switch (expr)
        {
            case Expr.Variable v:
                parts.Add(v.Name.Lexeme);
                break;
            case Expr.Get g:
                CollectGetChain(g.Object, parts);
                parts.Add(g.Name.Lexeme);
                break;
        }
    }

    protected override void EmitNew(Expr.New n)
    {
        // Built-in types only apply when callee is a simple identifier
        bool isSimpleName = IsSimpleIdentifier(n.Callee);
        string? simpleClassName = GetSimpleClassName(n.Callee);

        // Special case: new Date(...) constructor
        if (isSimpleName && simpleClassName == "Date")
        {
            EmitNewDate(n.Arguments);
            return;
        }

        // Special case: new Map(...) constructor
        if (isSimpleName && simpleClassName == "Map")
        {
            EmitNewMap(n.Arguments);
            return;
        }

        // Special case: new Set(...) constructor
        if (isSimpleName && simpleClassName == "Set")
        {
            EmitNewSet(n.Arguments);
            return;
        }

        // Special case: new WeakMap() constructor
        if (isSimpleName && simpleClassName == "WeakMap")
        {
            EmitNewWeakMap();
            return;
        }

        // Special case: new WeakSet() constructor
        if (isSimpleName && simpleClassName == "WeakSet")
        {
            EmitNewWeakSet();
            return;
        }

        // Special case: new RegExp(...) constructor
        if (isSimpleName && simpleClassName == "RegExp")
        {
            EmitNewRegExp(n.Arguments);
            return;
        }

        // Special case: new Error(...) and error subtype constructors
        if (isSimpleName && simpleClassName != null && IsErrorTypeName(simpleClassName))
        {
            EmitNewError(simpleClassName, n.Arguments);
            return;
        }

        // Special case: new EventEmitter() constructor
        if (isSimpleName && simpleClassName == "EventEmitter")
        {
            EmitNewEventEmitter();
            return;
        }

        // Special case: new Readable(...) constructor
        if (isSimpleName && simpleClassName == "Readable")
        {
            EmitNewReadable(n.Arguments);
            return;
        }

        // Special case: new Writable(...) constructor
        if (isSimpleName && simpleClassName == "Writable")
        {
            EmitNewWritable(n.Arguments);
            return;
        }

        // Special case: new Duplex(...) constructor
        if (isSimpleName && simpleClassName == "Duplex")
        {
            EmitNewDuplex(n.Arguments);
            return;
        }

        // Special case: new Transform(...) constructor
        if (isSimpleName && simpleClassName == "Transform")
        {
            EmitNewTransform(n.Arguments);
            return;
        }

        // Special case: new PassThrough(...) constructor
        if (isSimpleName && simpleClassName == "PassThrough")
        {
            EmitNewPassThrough(n.Arguments);
            return;
        }

        // Special case: new TextEncoder() constructor
        if (isSimpleName && simpleClassName == "TextEncoder")
        {
            EmitNewTextEncoder();
            return;
        }

        // Special case: new TextDecoder(...) constructor
        if (isSimpleName && simpleClassName == "TextDecoder")
        {
            EmitNewTextDecoder(n.Arguments);
            return;
        }

        // Special case: new StringDecoder(...) constructor
        if (isSimpleName && simpleClassName == "StringDecoder")
        {
            EmitNewStringDecoder(n.Arguments);
            return;
        }

        // Extract qualified name from callee expression
        var (namespaceParts, className) = ExtractQualifiedName(n.Callee);

        // Special case: new util.TextEncoder() or new util.TextDecoder() (module-qualified)
        if (namespaceParts.Count == 1 && className == "TextEncoder")
        {
            EmitNewTextEncoder();
            return;
        }
        if (namespaceParts.Count == 1 && className == "TextDecoder")
        {
            EmitNewTextDecoder(n.Arguments);
            return;
        }
        // Special case: new sd.StringDecoder() (module-qualified)
        if (namespaceParts.Count == 1 && className == "StringDecoder")
        {
            EmitNewStringDecoder(n.Arguments);
            return;
        }

        // Resolve class name (may be qualified for namespace classes or multi-module compilation)
        string resolvedClassName;
        if (namespaceParts.Count > 0)
        {
            // Build qualified name for namespace classes: Namespace_SubNs_ClassName
            string nsPath = string.Join("_", namespaceParts);
            resolvedClassName = $"{nsPath}_{className}";
        }
        else
        {
            resolvedClassName = _ctx.ResolveClassName(className);
        }

        // Check for external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out var externalType) ||
            _ctx.TypeMapper.ExternalTypes.TryGetValue(resolvedClassName, out externalType))
        {
            EmitExternalTypeConstruction(externalType, n.Arguments);
            return;
        }

        var ctorBuilder = _ctx.ClassRegistry?.GetConstructorByQualifiedName(resolvedClassName);
        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) && ctorBuilder != null)
        {
            Type targetType = typeBuilder;
            ConstructorInfo targetCtor = ctorBuilder;

            // Handle generic class instantiation (e.g., new Box<number>(42))
            if (n.TypeArgs != null && n.TypeArgs.Count > 0 &&
                _ctx.ClassRegistry!.GetGenericParams(resolvedClassName) != null)
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
                 _ctx.VarToClassExpr.TryGetValue(className, out var classExpr) &&
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
            var local = _ctx.Locals.GetLocal(className);
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

    /// <summary>
    /// Emits code for new Error(...) and error subtype construction.
    /// </summary>
    private void EmitNewError(string errorTypeName, List<Expr> arguments)
    {
        // Push the error type name
        IL.Emit(OpCodes.Ldstr, errorTypeName);

        // Create arguments list
        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EmitBoxIfNeeded(arguments[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // Call runtime CreateError(errorTypeName, args)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateError);
        SetStackUnknown();
    }

    /// <summary>
    /// Checks if a name is a built-in Error type name.
    /// Delegates to ErrorBuiltIns for centralized type name knowledge.
    /// </summary>
    private static bool IsErrorTypeName(string name) => ErrorBuiltIns.IsErrorTypeName(name);

    /// <summary>
    /// Emits code for new EventEmitter() construction.
    /// </summary>
    private void EmitNewEventEmitter()
    {
        // new EventEmitter() - no arguments
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSEventEmitterCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new TextEncoder() construction.
    /// </summary>
    private void EmitNewTextEncoder()
    {
        // new TextEncoder() - no arguments (always UTF-8)
        // Use the emitted $TextEncoder constructor for standalone execution
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSTextEncoderCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new TextDecoder(...) construction.
    /// </summary>
    private void EmitNewTextDecoder(List<Expr> arguments)
    {
        // new TextDecoder(encoding?, options?)
        // Use the emitted $TextDecoder constructor for standalone execution

        // Encoding
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
        }
        else
        {
            IL.Emit(OpCodes.Ldnull);
        }

        // Fatal option (default: false)
        IL.Emit(OpCodes.Ldc_I4_0);

        // IgnoreBOM option (default: false)
        IL.Emit(OpCodes.Ldc_I4_0);

        // TODO: Parse options object for fatal and ignoreBOM if provided
        // For now, just use defaults

        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSTextDecoderCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new StringDecoder(...) construction.
    /// </summary>
    private void EmitNewStringDecoder(List<Expr> arguments)
    {
        // new StringDecoder(encoding?)
        // Use the emitted $StringDecoder constructor for standalone execution

        // Encoding (default: "utf8")
        if (arguments.Count > 0)
        {
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
        }
        else
        {
            IL.Emit(OpCodes.Ldstr, "utf8");
        }

        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSStringDecoderCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new Readable(...) construction.
    /// </summary>
    private void EmitNewReadable(List<Expr> arguments)
    {
        // new Readable(options?) - options are ignored for now
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSReadableCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new Writable(...) construction.
    /// </summary>
    private void EmitNewWritable(List<Expr> arguments)
    {
        // new Writable(options?)
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSWritableCtor);

        // If options provided with write callback, set it
        if (arguments.Count > 0)
        {
            var optionsLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();

            // Store instance
            var instanceLocal = IL.DeclareLocal(_ctx.Runtime!.TSWritableType);
            IL.Emit(OpCodes.Stloc, instanceLocal);

            // Emit options and check for write callback
            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);

            // If options is null, skip
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, optionsLabel);

            // Try to get 'write' property: $Runtime.GetProperty(options, "write")
            IL.Emit(OpCodes.Ldstr, "write");
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

            // Store write callback
            var writeCallbackLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, writeCallbackLocal);

            // If write callback is not null, set it on the instance
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            IL.Emit(OpCodes.Brfalse, endLabel);

            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            var setWriteCallback = _ctx.Runtime!.TSWritableType.GetMethod("SetWriteCallback");
            IL.Emit(OpCodes.Callvirt, setWriteCallback!);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(optionsLabel);
            IL.Emit(OpCodes.Pop); // Pop the null options

            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }

        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new Duplex(...) construction.
    /// </summary>
    private void EmitNewDuplex(List<Expr> arguments)
    {
        // new Duplex(options?)
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSDuplexCtor);

        // If options provided with write callback, set it
        if (arguments.Count > 0)
        {
            var optionsLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();

            var instanceLocal = IL.DeclareLocal(_ctx.Runtime!.TSDuplexType);
            IL.Emit(OpCodes.Stloc, instanceLocal);

            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);

            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, optionsLabel);

            IL.Emit(OpCodes.Ldstr, "write");
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

            var writeCallbackLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, writeCallbackLocal);

            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            IL.Emit(OpCodes.Brfalse, endLabel);

            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, writeCallbackLocal);
            var setWriteCallback = _ctx.Runtime!.TSDuplexType.GetMethod("SetWriteCallback");
            IL.Emit(OpCodes.Callvirt, setWriteCallback!);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(optionsLabel);
            IL.Emit(OpCodes.Pop);

            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }

        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new Transform(...) construction.
    /// </summary>
    private void EmitNewTransform(List<Expr> arguments)
    {
        // new Transform(options?)
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSTransformCtor);

        // If options provided with transform callback, set it
        if (arguments.Count > 0)
        {
            var optionsLabel = IL.DefineLabel();
            var afterTransformLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();

            var instanceLocal = IL.DeclareLocal(_ctx.Runtime!.TSTransformType);
            IL.Emit(OpCodes.Stloc, instanceLocal);

            EmitExpression(arguments[0]);
            EmitBoxIfNeeded(arguments[0]);

            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brfalse, optionsLabel);

            // Get 'transform' property
            IL.Emit(OpCodes.Dup); // Keep options on stack for flush
            IL.Emit(OpCodes.Ldstr, "transform");
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

            var transformCallbackLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, transformCallbackLocal);

            IL.Emit(OpCodes.Ldloc, transformCallbackLocal);
            IL.Emit(OpCodes.Brfalse, afterTransformLabel);

            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, transformCallbackLocal);
            var setTransformCallback = _ctx.Runtime!.TSTransformType.GetMethod("SetTransformCallback");
            IL.Emit(OpCodes.Callvirt, setTransformCallback!);

            IL.MarkLabel(afterTransformLabel);

            // Get 'flush' property (options still on stack)
            IL.Emit(OpCodes.Ldstr, "flush");
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

            var flushCallbackLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, flushCallbackLocal);

            IL.Emit(OpCodes.Ldloc, flushCallbackLocal);
            IL.Emit(OpCodes.Brfalse, endLabel);

            IL.Emit(OpCodes.Ldloc, instanceLocal);
            IL.Emit(OpCodes.Ldloc, flushCallbackLocal);
            var setFlushCallback = _ctx.Runtime!.TSTransformType.GetMethod("SetFlushCallback");
            IL.Emit(OpCodes.Callvirt, setFlushCallback!);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(optionsLabel);
            IL.Emit(OpCodes.Pop);

            IL.MarkLabel(endLabel);
            IL.Emit(OpCodes.Ldloc, instanceLocal);
        }

        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new PassThrough(...) construction.
    /// </summary>
    private void EmitNewPassThrough(List<Expr> arguments)
    {
        // new PassThrough(options?) - options are ignored, just passes through
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSPassThroughCtor);
        SetStackUnknown();
    }
}
