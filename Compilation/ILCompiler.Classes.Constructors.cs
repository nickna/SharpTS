using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Constructor emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    private void EmitConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        var defCtx = GetDefinitionContext();
        // Use qualified class name to match DefineClass/EmitClassMethods
        string className = defCtx.GetQualifiedClassName(classStmt.Name.Lexeme);

        // Find constructor implementation (with body), not overload signatures
        var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor" && m.Body != null);

        // Reuse pre-defined constructor if available (from DefineClassMethodsOnly)
        ConstructorBuilder ctorBuilder;
        if (_classes.Constructors.TryGetValue(className, out var existingCtor))
        {
            ctorBuilder = existingCtor;
        }
        else
        {
            // Fallback: resolve typed parameters.
            // For Error subclasses without a constructor, accept a string? message param
            // so `new SimpleError("msg")` works.
            var paramTypes = constructor != null
                ? ParameterTypeResolver.ResolveConstructorParameters(className, constructor.Parameters, _typeMapper, _typeMap)
                : _classes.ErrorSubclasses.Contains(className)
                    ? [typeof(object)]  // Accept any value; converted to string by base Error constructor
                    : _classes.PromiseSubclasses.Contains(className)
                        ? [typeof(object)]  // Executor arg, forwarded to PromiseFromExecutor (#242)
                        : [];
            ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes
            );
            _classes.Constructors[className] = ctorBuilder;
        }

        var il = ctorBuilder.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, ctorBuilder);
        ctx.CurrentSuperclassName = Expr.GetSuperclassLeafName(classStmt.SuperclassExpr);
        // Typed interop support
        ctx.PropertyBackingFields = _typedInterop.PropertyBackingFields;
        ctx.ClassProperties = _typedInterop.ClassProperties;
        ctx.DeclaredPropertyNames = _typedInterop.DeclaredPropertyNames;
        ctx.ReadonlyPropertyNames = _typedInterop.ReadonlyPropertyNames;
        ctx.PropertyTypes = _typedInterop.PropertyTypes;
        ctx.ExtrasFields = _typedInterop.ExtrasFields;
        ctx.UnionGenerator = _unionGenerator;
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = className;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ApplyCapturedTopLevelVariableAccess(ctx);
        // Arrow-closure DC field maps — without these, arrow closures created inside
        // the constructor (e.g. `arr.map(v => v < MAX)` referencing a module-level
        // captured var) won't populate their `$entryPointDC`/`$functionDC`/`$arrowDC`
        // fields on the newobj'd display class, and the arrow body's `ldfld $entryPointDC`
        // dereferences null at runtime.
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
        ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        ctx.ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null;
        ctx.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields.Count > 0 ? _arrowScopeDCExtraFields : null;
        // Constructors have a void signature; without this the `return;`
        // inside a ctor body defaults to object and emits `ldnull` before
        // the `ret`, producing an invalid method.
        ctx.CurrentMethodReturnType = typeof(void);

        // Add class generic type parameters to context
        if (_classes.GenericParams.TryGetValue(className, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _extras dictionary FIRST (before calling parent constructor)
        // This allows parent constructor to access fields via SetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Initialize @lock decorator fields if present
        if (_locks.SyncLockFields.TryGetValue(className, out var syncLockField))
        {
            // this._syncLock = new object();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, _types.ObjectDefaultCtor);
            il.Emit(OpCodes.Stfld, syncLockField);
        }

        if (_locks.AsyncLockFields.TryGetValue(className, out var asyncLockField))
        {
            // this._asyncLock = new SemaphoreSlim(1, 1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);  // initialCount = 1
            il.Emit(OpCodes.Ldc_I4_1);  // maxCount = 1
            il.Emit(OpCodes.Newobj, _types.SemaphoreSlimCtor);
            il.Emit(OpCodes.Stfld, asyncLockField);
        }

        if (_locks.ReentrancyFields.TryGetValue(className, out var reentrancyField))
        {
            // this._lockReentrancy = new AsyncLocal<int>();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, typeof(AsyncLocal<int>).GetConstructor([])!);
            il.Emit(OpCodes.Stfld, reentrancyField);
        }

        // Call parent constructor
        // If the class has an explicit constructor with super(), the super() in body will handle it.
        // If the class has no explicit constructor but has a superclass, we must call the parent constructor.
        // If the class has no superclass, we call Object constructor.
        string? qualifiedSuperclass = classStmt.SuperclassExpr != null ? defCtx.ResolveClassName(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!) : null;
        bool isErrorSubclass = classStmt.SuperclassExpr != null
            && Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!);
        // Direct `extends Array` (#233): base is the emitted $Array, chained
        // via its ctor-args constructor.
        bool isDirectArraySubclass = classStmt.SuperclassExpr != null
            && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "Array"
            && (qualifiedSuperclass == null || !_classes.Builders.ContainsKey(qualifiedSuperclass));
        // Direct `extends Promise` (#242): base is the emitted $Promise,
        // chained via PromiseFromExecutor (which also adopts a raw
        // Task<object?> in place of an executor — the derived-promise
        // construction path).
        bool isDirectPromiseSubclass = classStmt.SuperclassExpr != null
            && Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "Promise"
            && (qualifiedSuperclass == null || !_classes.Builders.ContainsKey(qualifiedSuperclass));
        if (constructor == null && isDirectArraySubclass)
        {
            // No explicit constructor, extends Array — empty array per
            // implicit `constructor(...args) { super(...args) }` with no args.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Call, _runtime.TSArrayCtorFromCtorArgs);
        }
        else if (constructor == null && isDirectPromiseSubclass)
        {
            // No explicit constructor, extends Promise — implicit
            // `constructor(executor) { super(executor) }`.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1); // executor (object?)
            il.Emit(OpCodes.Call, _runtime.PromiseFromExecutor);
            il.Emit(OpCodes.Call, _runtime.TSPromiseCtor);
        }
        else if (constructor == null && qualifiedSuperclass != null && isErrorSubclass)
        {
            // No explicit constructor, extends Error — forward message arg to base Error constructor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1); // message parameter (object?)
            il.Emit(OpCodes.Castclass, _types.String); // Cast object? → string? (null-safe)
            var baseCtor = GetEmittedErrorConstructor(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!);
            il.Emit(OpCodes.Call, (System.Reflection.ConstructorInfo)baseCtor);
        }
        else if (constructor == null && qualifiedSuperclass != null && _classes.Constructors.TryGetValue(qualifiedSuperclass, out var parentCtor))
        {
            // No explicit constructor but has superclass - forward all arguments to parent constructor
            il.Emit(OpCodes.Ldarg_0);
            var parentParams = parentCtor.GetParameters();
            for (int i = 0; i < parentParams.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, i + 1);  // +1 because arg 0 is 'this'
            }

            // Handle generic superclass with type arguments (e.g., extends Box<string>)
            // We need to call the constructor on the closed generic type, not the open generic
            ConstructorInfo ctorToCall = parentCtor;
            Type? baseType = typeBuilder.BaseType;
            if (baseType != null && baseType.IsGenericType && baseType.IsConstructedGenericType)
            {
                // Get the constructor for the closed generic type
                ctorToCall = EmitterTypeHelpers.ResolveConstructor(baseType, parentCtor);
            }

            il.Emit(OpCodes.Call, ctorToCall);
        }
        else if (constructor != null && qualifiedSuperclass != null && _classes.Constructors.ContainsKey(qualifiedSuperclass))
        {
            // Explicit constructor with a user-class superclass: the super(...)
            // call in the body chains the base constructor via
            // SuperConstructorHandler. Emitting Object..ctor here would call the
            // WRONG base (Object instead of the real parent) and leave the
            // verifier seeing a base-ctor `call` on an already-initialized
            // `this` — ILVerify CallCtor (#287). Emit nothing; super() handles it.
        }
        else if (!isErrorSubclass && !isDirectArraySubclass && !isDirectPromiseSubclass)
        {
            // No superclass (base is Object): initialize via Object..ctor.
            // For Error/Array/Promise subclasses with an explicit constructor, skip this —
            // super() in the constructor body calls the base constructor via
            // SuperConstructorHandler.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.ObjectDefaultCtor);
        }

        // Emit instance field initializers to backing fields (before constructor body)
        // Note: Declare fields are excluded - they have no initialization
        var instanceFieldsWithInit = classStmt.Fields.Where(f => !f.IsStatic && !f.IsPrivate && !f.IsDeclare && f.Initializer != null).ToList();
        if (instanceFieldsWithInit.Count > 0)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;
            var initEmitter = new ILEmitter(ctx);

            foreach (var field in instanceFieldsWithInit)
            {
                // Handle computed property names (e.g., [mySymbol]: string = "value")
                if (field.ComputedKey != null)
                {
                    // Computed keys use dynamic SetIndex to support Symbol keys
                    // Stack: this
                    il.Emit(OpCodes.Ldarg_0);
                    // Evaluate computed key expression (e.g., the Symbol)
                    initEmitter.EmitExpression(field.ComputedKey);
                    initEmitter.EmitBoxIfNeeded(field.ComputedKey);
                    // Emit initializer value
                    initEmitter.EmitExpression(field.Initializer!);
                    initEmitter.EmitBoxIfNeeded(field.Initializer!);
                    // Call Runtime.SetIndex(object, key, value)
                    il.Emit(OpCodes.Call, _runtime.SetIndex);
                    continue;
                }

                string fieldName = field.Name.Lexeme;
                string pascalName = NamingConventions.ToPascalCase(fieldName);

                // Check if this is a declared property with a backing field (using PascalCase key)
                if (_typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields) &&
                    backingFields.TryGetValue(pascalName, out var backingField))
                {
                    // Store directly in backing field
                    il.Emit(OpCodes.Ldarg_0);  // this

                    // Emit initializer expression
                    initEmitter.EmitExpression(field.Initializer!);

                    // Convert to proper type if needed
                    Type targetType = _typedInterop.PropertyTypes[className][pascalName];
                    EmitTypeConversion(il, initEmitter, field.Initializer!, targetType);

                    il.Emit(OpCodes.Stfld, backingField);
                }
                else
                {
                    // Fallback: store in _extras dictionary (for fields without backing fields)
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, fieldsField);
                    il.Emit(OpCodes.Ldstr, fieldName);
                    // number[] unboxing: an `arr: number[] = []` field is created as a numeric $Array
                    // (escaping by nature — a field is aliasable), so `this.arr[i]=v` writes unboxed.
                    if (!initEmitter.TryEmitNumericEmptyArrayInit(field.TypeAnnotation, field.Initializer))
                    {
                        initEmitter.EmitExpression(field.Initializer!);
                        initEmitter.EmitBoxIfNeeded(field.Initializer!);
                    }
                    il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
                }
            }
        }

        // Initialize instance declare fields (without initializers) to null in _extras dictionary
        // TypeScript semantics: uninitialized fields return null/undefined, not CLR defaults
        var instanceDeclareFields = classStmt.Fields.Where(f =>
            !f.IsStatic && !f.IsPrivate && f.IsDeclare && f.Initializer == null && f.ComputedKey == null).ToList();
        foreach (var field in instanceDeclareFields)
        {
            string fieldName = field.Name.Lexeme;
            // Store null in _extras dictionary
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fieldsField);
            il.Emit(OpCodes.Ldstr, fieldName);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        }

        // ES2022: Initialize instance private fields
        // Private fields use a ConditionalWeakTable for GC-friendly per-instance storage
        EmitPrivateFieldInitialization(il, className, classStmt, ctx);

        // TypeScript 4.9+: Initialize instance auto-accessor backing fields
        if (classStmt.AutoAccessors != null)
        {
            var instanceAutoAccessors = classStmt.AutoAccessors.Where(a => !a.IsStatic && a.Initializer != null).ToList();
            if (instanceAutoAccessors.Count > 0)
            {
                ctx.FieldsField = fieldsField;
                ctx.IsInstanceMethod = true;
                var autoAccessorEmitter = new ILEmitter(ctx);

                foreach (var autoAccessor in instanceAutoAccessors)
                {
                    EmitAutoAccessorInitializer(autoAccessorEmitter, autoAccessor, className, isStatic: false);
                }
            }
        }

        // Emit constructor body
        if (constructor != null)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;

            // Define parameters with types
            var ctorParams = ctorBuilder.GetParameters();
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                Type paramType = i < ctorParams.Length ? ctorParams[i].ParameterType : typeof(object);
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1, paramType);
            }

            var emitter = new ILEmitter(ctx);

            // Apply parameter defaults before the body. Constructors get no OverloadGenerator
            // forwarding, so without this a defaulted constructor argument that is omitted or
            // explicit `undefined` would never fire its default. This must run before the body
            // because parameter-property assignments (`this.x = x`, prepended to the body by the
            // parser) and any default expression referencing the param read the defaulted value.
            // Value-type-defaulted params are widened to object by ParameterTypeResolver so the
            // prologue can observe the `$Undefined` sentinel. (#705)
            var ctorDefaultParamTypes = ctorBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
            emitter.EmitDefaultParameters(constructor.Parameters, isInstanceMethod: true, paramTypes: ctorDefaultParamTypes);

            if (constructor.Body != null)
            {
                foreach (var stmt in constructor.Body)
                {
                    emitter.EmitStatement(stmt);
                }
            }
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to initialize ES2022 private fields for a new instance.
    /// Creates a Dictionary with initial values and adds it to the ConditionalWeakTable.
    /// </summary>
    private void EmitPrivateFieldInitialization(
        ILGenerator il,
        string className,
        Stmt.Class classStmt,
        CompilationContext ctx)
    {
        // Check if this class has instance private fields
        if (!_classes.PrivateFieldStorage.TryGetValue(className, out var storageField))
            return;

        // Get the list of private field names
        if (!_classes.PrivateFieldNames.TryGetValue(className, out var fieldNames) || fieldNames.Count == 0)
            return;

        var instancePrivateFields = classStmt.Fields
            .Where(f => f.IsPrivate && !f.IsStatic)
            .ToList();

        ctx.FieldsField = null; // Not using _fields for private field init
        ctx.IsInstanceMethod = true;
        var initEmitter = new ILEmitter(ctx);

        // Create local for the fields dictionary
        var dictType = typeof(Dictionary<string, object?>);
        var dictLocal = il.DeclareLocal(dictType);

        // Dictionary<string, object?> __fields = new Dictionary<string, object?>(capacity)
        il.Emit(OpCodes.Ldc_I4, fieldNames.Count);
        il.Emit(OpCodes.Newobj, dictType.GetConstructor([typeof(int)])!);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Add each private field with its initializer value (or null)
        foreach (var field in instancePrivateFields)
        {
            string fieldName = field.Name.Lexeme;
            if (fieldName.StartsWith('#'))
                fieldName = fieldName[1..];

            // __fields[fieldName] = initializer ?? null
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, fieldName);

            if (field.Initializer != null)
            {
                initEmitter.EmitExpression(field.Initializer);
                initEmitter.EmitBoxIfNeeded(field.Initializer);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!);
        }

        // __privateFields.Add(this, __fields)
        var cwtType = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>)
            .MakeGenericType(typeof(object), typeof(Dictionary<string, object?>));
        var addMethod = cwtType.GetMethod("Add", [typeof(object), typeof(Dictionary<string, object?>)])!;

        il.Emit(OpCodes.Ldsfld, storageField);       // __privateFields
        il.Emit(OpCodes.Ldarg_0);                    // this
        il.Emit(OpCodes.Ldloc, dictLocal);           // __fields
        il.Emit(OpCodes.Callvirt, addMethod);
    }
}
