using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Instance method definition and emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    /// <summary>
    /// Defines all class methods (without emitting bodies) so they're available for
    /// direct dispatch in async state machines.
    /// </summary>
    private void DefineAllClassMethods(IEnumerable<Stmt> statements)
    {
        foreach (var classStmt in CollectClassDeclarations(statements))
            DefineClassMethodsOnly(classStmt);
    }

    /// <summary>
    /// Emits $IHasFields interface method bodies for all classes.
    /// Called after DefineAllClassMethods so that MethodBuilders are available.
    /// </summary>
    private void EmitAllHasFieldsInterfaceMethodBodies(IEnumerable<Stmt> statements)
    {
        foreach (var classStmt in CollectClassDeclarations(statements))
        {
            if (classStmt.IsDeclare)
                continue;

            string qualifiedClassName = GetQualifiedClassDeclarationName(classStmt);

            // Skip external types
            if (_classes.ExternalTypes.ContainsKey(qualifiedClassName) ||
                _classes.ExternalTypes.ContainsKey(classStmt.Name.Lexeme))
                continue;

            EmitHasFieldsInterfaceMethodBodies(qualifiedClassName, classStmt);
        }
    }

    /// <summary>
    /// Defines method signatures and registers them in _classes.InstanceMethods without emitting bodies.
    /// Also pre-defines the constructor so it's available for EmitNew in async contexts.
    /// </summary>
    private void DefineClassMethodsOnly(Stmt.Class classStmt)
    {
        // Skip @DotNetType external type classes - they don't have TypeBuilders
        if (classStmt.IsDeclare)
            return;

        var ctx = GetDefinitionContext();
        string qualifiedClassName = GetQualifiedClassDeclarationName(classStmt, resolve: true);

        // Also skip if this is an external type (registered via @DotNetType decorator)
        if (_classes.ExternalTypes.ContainsKey(qualifiedClassName) ||
            _classes.ExternalTypes.ContainsKey(classStmt.Name.Lexeme))
            return;

        if (!_classes.Builders.TryGetValue(qualifiedClassName, out var typeBuilder))
            return;  // Skip if no TypeBuilder exists for this class

        // This must precede the method-signature idempotency return below. In
        // multi-module builds, duplicate simple class names can revisit method
        // registration through a resolved name while still requiring a distinct
        // prototype constructor on each module-qualified TypeBuilder.
        DefineClassPrototypeConstructor(typeBuilder);

        // Skip if instance methods are already defined for this class
        if (_classes.InstanceMethods.ContainsKey(qualifiedClassName))
            return;

        // Pre-define constructor (if not already defined)
        if (!_classes.Constructors.ContainsKey(qualifiedClassName))
        {
            var constructor = classStmt.Methods.FirstOrDefault(m => !m.IsStatic && m.Name.Lexeme == "constructor" && m.Body != null);
            // Use typed parameters from TypeMap
            Type[] ctorParamTypes;
            if (constructor != null)
            {
                ctorParamTypes = ParameterTypeResolver.ResolveConstructorParameters(
                    classStmt.Name.Lexeme, constructor.Parameters, _typeMapper, _typeMap);
            }
            else if (classStmt.SuperclassExpr != null)
            {
                // No explicit constructor - inherit parent's parameter types
                string qualifiedSuperclass = ctx.ResolveClassName(Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!);
                if (_classes.Constructors.TryGetValue(qualifiedSuperclass, out var parentCtor))
                {
                    ctorParamTypes = parentCtor.GetParameters().Select(p => p.ParameterType).ToArray();
                }
                else if (_classes.ErrorSubclasses.Contains(qualifiedClassName))
                {
                    // Direct AggregateError subclasses forward (errors, message);
                    // the other native Error constructors accept message only.
                    ctorParamTypes = Expr.GetSuperclassLeafName(classStmt.SuperclassExpr) == "AggregateError"
                        ? [typeof(object), typeof(object)]
                        : [typeof(object)];
                }
                else if (_classes.PromiseSubclasses.Contains(qualifiedClassName))
                {
                    // Promise subclass with no constructor — accept the executor arg (#242)
                    ctorParamTypes = [typeof(object)];
                }
                else if (_classes.ArraySubclasses.Contains(qualifiedClassName))
                {
                    // The implicit derived constructor forwards the complete JS
                    // argument list to Array, whose arity is unbounded.
                    ctorParamTypes = [typeof(object[])];
                }
                else
                {
                    ctorParamTypes = [];
                }
            }
            else
            {
                ctorParamTypes = [];
            }

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                ctorParamTypes
            );

            if (constructor == null && _classes.ArraySubclasses.Contains(qualifiedClassName))
                MarkJsVariadicConstructor(ctorBuilder);

            _classes.Constructors[qualifiedClassName] = ctorBuilder;
            RegisterArgumentsCapturingMethod(ctorBuilder, constructor?.Body);
            if (constructor == null && classStmt.SuperclassExpr != null)
            {
                string qualifiedSuperclass = ctx.ResolveClassName(
                    Expr.GetSuperclassLeafName(classStmt.SuperclassExpr)!);
                if (_classes.Constructors.TryGetValue(qualifiedSuperclass, out var parentCtor)
                    && _functions.MethodsCapturingArguments.Contains(parentCtor))
                {
                    // The implicit derived constructor forwards the original
                    // caller list to super(...args). Keep the published snapshot
                    // alive until the first base constructor that binds arguments.
                    _functions.MethodsCapturingArguments.Add(ctorBuilder);
                }
            }
        }

        // Initialize static methods dictionary for this class
        if (!_classes.StaticMethods.ContainsKey(qualifiedClassName))
        {
            _classes.StaticMethods[qualifiedClassName] = [];
        }

        // Pre-define static methods (so they're available during async MoveNext emission).
        // Per-method idempotency check: in multi-module compilation this method runs twice
        // for every class (once during the per-module pre-define pass, once at the start of
        // ModulePhase8 method-body emission). Without the per-name guard, a second
        // DefineMethod call would create a SECOND empty MethodBuilder on the TypeBuilder for
        // the same name+signature; the dict overwrites with the new (still empty) builder,
        // EmitStaticMethodBody fills the new one, and the abandoned first MethodBuilder
        // shows up via reflection with no body — surface as BadImageFormatException at any
        // reflective Invoke. Tracked as #58.
        foreach (var method in classStmt.Methods.Where(m =>
                     m.Body != null && m.IsStatic && !m.IsPrivate && m.ComputedKey == null))
        {
            if (_classes.StaticMethods[qualifiedClassName].ContainsKey(method.Name.Lexeme))
                continue;

            // Use typed parameters from TypeMap
            var paramTypes = ParameterTypeResolver.ResolveMethodParameters(
                classStmt.Name.Lexeme, method.Name.Lexeme, method.Parameters, _typeMapper, _typeMap);
            // Set return type based on method kind
            // Must check async generator FIRST since it has both IsAsync and IsGenerator true
            var returnType = (method.IsAsync && method.IsGenerator) ? _types.IAsyncEnumerableOfObject :
                             method.IsAsync ? _types.TaskOfObject :
                             method.IsGenerator ? _types.IEnumerableOfObject :
                             typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes
            );

            _classes.StaticMethods[qualifiedClassName][method.Name.Lexeme] = methodBuilder;
            RegisterArgumentsCapturingMethod(methodBuilder, method.Body);
        }

        // Define instance methods (skip overload signatures with no body, and computed
        // symbol-keyed methods — handled by DefineSymbolMethods below).
        foreach (var method in classStmt.Methods.Where(m =>
                     m.Body != null && !m.IsPrivate && m.ComputedKey == null))
        {
            if (method.IsStatic || method.Name.Lexeme == "constructor")
                continue;

            // Use typed parameters from TypeMap
            var paramTypes = ParameterTypeResolver.ResolveMethodParameters(
                classStmt.Name.Lexeme, method.Name.Lexeme, method.Parameters, _typeMapper, _typeMap);

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            // Set return type based on method kind
            // Must check async generator FIRST since it has both IsAsync and IsGenerator true
            Type returnType = (method.IsAsync && method.IsGenerator) ? _types.IAsyncEnumerableOfObject :
                              method.IsAsync ? typeof(Task<object>) :
                              method.IsGenerator ? _types.IEnumerableOfObject :
                              typeof(object);

            var methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );

            // Track instance method for direct dispatch
            if (!_classes.InstanceMethods.TryGetValue(qualifiedClassName, out var classMethods))
            {
                classMethods = [];
                _classes.InstanceMethods[qualifiedClassName] = classMethods;
            }
            classMethods[method.Name.Lexeme] = methodBuilder;
            RegisterArgumentsCapturingMethod(methodBuilder, method.Body);

            // Store the method builder for body emission later
            // Use typeBuilder.Name to match the lookup in EmitMethod
            if (!_classes.PreDefinedMethods.TryGetValue(typeBuilder.Name, out var preDefined))
            {
                preDefined = [];
                _classes.PreDefinedMethods[typeBuilder.Name] = preDefined;
            }
            preDefined[method.Name.Lexeme] = methodBuilder;
        }

        // Define computed symbol-keyed methods ([Symbol.iterator]() {…}) under unique synthetic
        // names so they flow through the normal method-body machinery (incl. generator/async state
        // machines) and are dispatched via the runtime symbol-method registry (#647).
        DefineSymbolMethods(typeBuilder, classStmt, qualifiedClassName);

        // Define accessors with PascalCase naming
        // Note: Explicit accessors keep object-typed signatures because their bodies
        // use dynamic field storage. Field-backed properties already have typed signatures.
        if (classStmt.Accessors != null)
        {
            string className = typeBuilder.Name;

            foreach (var accessor in classStmt.Accessors)
            {
                // Symbol-keyed computed accessors (#266) have no static .NET member
                // name; pre-define a synthetic method here, register it in the class
                // .cctor, and dispatch through the runtime symbol-accessor registry.
                if (accessor.ComputedKey != null)
                {
                    DefineSymbolAccessorMethod(typeBuilder, accessor);
                    continue;
                }
                string accessorName = accessor.Name.Lexeme;
                string pascalName = NamingConventions.ToPascalCase(accessorName);
                string methodName = accessor.Kind.Type == TokenType.GET
                    ? $"get_{pascalName}"
                    : $"set_{pascalName}";
                if (accessor.IsStatic)
                    methodName = $"$static_{methodName}";

                // Explicit accessors use object types (their bodies work with dynamic field storage)
                Type[] paramTypes = accessor.Kind.Type == TokenType.SET
                    ? [typeof(object)]
                    : [];

                MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
                methodAttrs |= accessor.IsStatic ? MethodAttributes.Static : MethodAttributes.Virtual;
                if (accessor.IsAbstract)
                {
                    methodAttrs |= MethodAttributes.Abstract;
                }

                var methodBuilder = typeBuilder.DefineMethod(
                    methodName,
                    methodAttrs,
                    typeof(object),  // Explicit accessors return object
                    paramTypes
                );

                // Track getter/setter. Static accessors register in StaticGetters/StaticSetters
                // keyed by original (camelCase) name, matching the auto-accessor convention and
                // ClassRegistry.TryGetStaticGetter/Setter lookups. Instance accessors keep the
                // existing PascalCase key convention.
                if (accessor.Kind.Type == TokenType.GET)
                {
                    if (accessor.IsStatic)
                    {
                        if (!_classes.StaticGetters.TryGetValue(className, out var classStaticGetters))
                        {
                            classStaticGetters = [];
                            _classes.StaticGetters[className] = classStaticGetters;
                        }
                        classStaticGetters[accessorName] = methodBuilder;
                    }
                    else
                    {
                        if (!_classes.InstanceGetters.TryGetValue(className, out var classGetters))
                        {
                            classGetters = [];
                            _classes.InstanceGetters[className] = classGetters;
                        }
                        classGetters[pascalName] = methodBuilder;
                    }
                }
                else
                {
                    if (accessor.IsStatic)
                    {
                        if (!_classes.StaticSetters.TryGetValue(className, out var classStaticSetters))
                        {
                            classStaticSetters = [];
                            _classes.StaticSetters[className] = classStaticSetters;
                        }
                        classStaticSetters[accessorName] = methodBuilder;
                    }
                    else
                    {
                        if (!_classes.InstanceSetters.TryGetValue(className, out var classSetters))
                        {
                            classSetters = [];
                            _classes.InstanceSetters[className] = classSetters;
                        }
                        classSetters[pascalName] = methodBuilder;
                    }
                }

                // Store for body emission. Key by typeBuilder.Name (matches EmitAccessor's lookup
                // which uses typeBuilder.Name). The user-facing classStmt.Name.Lexeme (e.g. "Parser")
                // can differ from the emitted type name (e.g. "$M_parser_Parser") in multi-module
                // CJS compilation — keying by Lexeme caused EmitAccessor's TryGetValue to miss,
                // leading it to define a second (empty) method with the same name and crash at
                // class-load time with BadImageFormatException.
                if (!_classes.PreDefinedAccessors.TryGetValue(className, out var preDefinedAcc))
                {
                    preDefinedAcc = [];
                    _classes.PreDefinedAccessors[className] = preDefinedAcc;
                }
                preDefinedAcc[methodName] = methodBuilder;

                // Only instance accessors become CLR PropertyBuilder members.
                // Static JS accessors live on the constructor object (the Type
                // value) and are dispatched through StaticGetters/Setters.
                if (!accessor.IsStatic)
                {
                    if (!_typedInterop.ExplicitAccessors.TryGetValue(className, out var accessors))
                    {
                        accessors = [];
                        _typedInterop.ExplicitAccessors[className] = accessors;
                    }

                    if (!accessors.TryGetValue(pascalName, out var accessorInfo))
                        accessorInfo = (null, null, typeof(object));

                    accessors[pascalName] = accessor.Kind.Type == TokenType.GET
                        ? (methodBuilder, accessorInfo.Setter, typeof(object))
                        : (accessorInfo.Getter, methodBuilder, typeof(object));
                }
            }

            // Create PropertyBuilders for explicit accessors
            CreateExplicitAccessorProperties(typeBuilder, className);
        }
    }

    /// <summary>
    /// Creates PropertyBuilders for explicit accessors after all getter/setter methods are defined.
    /// </summary>
    private void CreateExplicitAccessorProperties(TypeBuilder typeBuilder, string className)
    {
        if (!_typedInterop.ExplicitAccessors.TryGetValue(className, out var accessors))
            return;

        foreach (var (pascalName, (getter, setter, propertyType)) in accessors)
        {
            if (getter == null && setter == null)
                continue;

            // Determine property type: prefer getter return type, then setter param, then fallback
            Type propType = propertyType;
            if (getter != null && getter.ReturnType != typeof(void))
            {
                propType = getter.ReturnType;
            }
            else if (setter != null)
            {
                var setterParams = setter.GetParameters();
                if (setterParams.Length > 0)
                {
                    propType = setterParams[0].ParameterType;
                }
            }

            var property = typeBuilder.DefineProperty(
                pascalName,
                PropertyAttributes.None,
                propType,
                null
            );

            if (getter != null)
                property.SetGetMethod(getter);
            if (setter != null)
                property.SetSetMethod(setter);

            // Track the property
            if (!_typedInterop.ClassProperties.TryGetValue(className, out var classProps))
            {
                classProps = [];
                _typedInterop.ClassProperties[className] = classProps;
            }
            classProps[pascalName] = property;
        }
    }

    private void EmitClassMethods(Stmt.Class classStmt)
    {
        if (!_classes.EmittedMethodBodies.Add(classStmt))
            return;

        // Skip @DotNetType external type classes - they don't have TypeBuilders
        if (classStmt.IsDeclare)
            return;

        // Get qualified class name (must match what DefineClass used)
        string qualifiedClassName = GetQualifiedClassDeclarationName(classStmt);

        // Also skip if this is an external type (registered via @DotNetType decorator)
        if (_classes.ExternalTypes.ContainsKey(qualifiedClassName) ||
            _classes.ExternalTypes.ContainsKey(classStmt.Name.Lexeme))
            return;

        if (!_classes.Builders.TryGetValue(qualifiedClassName, out var typeBuilder))
            return;  // Skip if no TypeBuilder exists for this class
        var fieldsField = _classes.InstanceFieldsField[qualifiedClassName];

        // Initialize static methods dictionary for this class
        if (!_classes.StaticMethods.ContainsKey(qualifiedClassName))
        {
            _classes.StaticMethods[qualifiedClassName] = [];
        }

        // Define static methods first (so we can reference them in the static constructor).
        // Skip overload signatures (no body) and computed symbol-keyed methods (handled by
        // DefineSymbolMethods / EmitSymbolMethods).
        foreach (var method in classStmt.Methods.Where(m =>
                     m.Body != null && !m.IsPrivate && m.ComputedKey == null))
        {
            if (method.IsStatic)
            {
                DefineStaticMethod(typeBuilder, qualifiedClassName, method);
            }
        }

        // Emit constructor
        EmitConstructor(typeBuilder, classStmt, fieldsField);

        // Emit the compiler-only constructor used to create Constructor.prototype
        // without running JavaScript constructor bodies or field initializers.
        EmitClassPrototypeConstructor(typeBuilder, fieldsField);

        // Emit method bodies (skip overload signatures with no body, and computed
        // symbol-keyed methods — emitted by EmitSymbolMethods below).
        // This must happen BEFORE static constructor so static blocks can call static methods
        foreach (var method in classStmt.Methods.Where(m =>
                     m.Body != null && !m.IsPrivate && m.ComputedKey == null))
        {
            if (method.IsStatic)
            {
                EmitStaticMethodBody(qualifiedClassName, method);
            }
            else if (method.Name.Lexeme != "constructor")
            {
                EmitMethod(typeBuilder, method, fieldsField);
            }
        }

        // #790: emit override-arity bridges so a base-typed call reaches a derived override that
        // adds trailing optional/default params (whose wider CLR arity would otherwise take a new
        // vtable slot instead of overriding the base).
        EmitOverrideArityBridges(typeBuilder, classStmt, qualifiedClassName);

        // Emit computed symbol-keyed method bodies (#647). Before the static constructor so the
        // registry registrations there can reference them.
        EmitSymbolMethods(typeBuilder, qualifiedClassName, fieldsField);

        // Emit static constructor for static property initializers and static blocks
        // This is done AFTER method bodies so static blocks can call static methods
        EmitStaticConstructor(typeBuilder, classStmt, qualifiedClassName);

        // Emit accessor methods
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                // Symbol-keyed computed accessors (#266) are emitted together below
                // (their bodies live on synthetic methods pre-defined in Pass 1).
                if (accessor.ComputedKey != null) continue;
                EmitAccessor(typeBuilder, accessor, fieldsField);
            }
            EmitSymbolAccessors(typeBuilder, fieldsField);
        }

        // Emit ES2022 private method bodies
        EmitPrivateMethodBodies(typeBuilder, classStmt, fieldsField, qualifiedClassName);
    }

    /// <summary>
    /// #790: For a derived instance method that overrides a base method by adding trailing
    /// optional/default parameters, the override's CLR arity differs from the base (e.g.
    /// <c>Derived.m(double, object)</c> vs <c>Base.m(double)</c>), so it takes a NEW vtable slot
    /// instead of overriding — and a base-typed call (<c>(b: Base).m(3)</c>) dispatches to the base
    /// method, never reaching the override. (The same-<i>arity</i> case is handled by
    /// <see cref="ParameterTypeResolver"/>'s hierarchy-consistent widening, #705/#723/#787.)
    ///
    /// This emits, for each shorter ancestor signature of the method, a synthetic <c>virtual</c>
    /// (non-newslot, so it auto-overrides) bridge matching that ancestor's exact CLR signature,
    /// forwarding to the derived full method with the missing trailing params filled by the
    /// <c>$Undefined</c> sentinel. The full method's existing default prologue then fires the
    /// defaults in order — identical to what a direct call site does via <c>EmitOmittedArgument</c>,
    /// so a default may reference an earlier param (#698). Forwarding via <c>callvirt</c> means a
    /// base-/mid-typed call lands on the most-derived implementation in deeper chains.
    ///
    /// Scoped to sync (return type <c>object</c>) instance methods: an async/generator ancestor has
    /// a different return type, so a CLR override is impossible.
    /// </summary>
    private void EmitOverrideArityBridges(TypeBuilder typeBuilder, Stmt.Class classStmt, string qualifiedClassName)
    {
        if (!_classes.InstanceMethods.TryGetValue(qualifiedClassName, out var ownMethods))
            return;

        foreach (var method in classStmt.Methods.Where(m =>
                     m.Body != null && !m.IsStatic && !m.IsPrivate && !m.IsAbstract &&
                     !m.IsAsync && !m.IsGenerator && m.ComputedKey == null &&
                     m.Name.Lexeme != "constructor"))
        {
            string name = method.Name.Lexeme;
            if (!ownMethods.TryGetValue(name, out var fullBuilder))
                continue;

            var fullParams = fullBuilder.GetParameters();
            int fullArity = fullParams.Length;
            if (fullArity == 0)
                continue; // cannot add trailing params below arity 0

            // Collect the distinct shorter ancestor CLR signatures (one bridge per arity, nearest
            // ancestor wins). Each distinct ancestor arity is a separate vtable slot to override.
            var byArity = new Dictionary<int, Type[]>();
            string? current = _classes.Superclass.GetValueOrDefault(qualifiedClassName);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (current != null && visited.Add(current))
            {
                if (_classes.InstanceMethods.TryGetValue(current, out var ancMethods) &&
                    ancMethods.TryGetValue(name, out var ancBuilder))
                {
                    var ancParams = ancBuilder.GetParameters();
                    int ancArity = ancParams.Length;
                    // Only a strictly-shorter, object-returning (sync) ancestor signature whose
                    // shared prefix matches ours can be bridged: the bridge must equal the ancestor
                    // slot's signature to bind, and forward those args into our full method.
                    if (ancArity < fullArity && !byArity.ContainsKey(ancArity) &&
                        ancBuilder.ReturnType == typeof(object) &&
                        PrefixTypesMatch(ancParams, fullParams, ancArity))
                    {
                        byArity[ancArity] = ancParams.Select(p => p.ParameterType).ToArray();
                    }
                }
                current = _classes.Superclass.GetValueOrDefault(current);
            }

            foreach (var (bridgeArity, bridgeSig) in byArity)
            {
                var bridge = typeBuilder.DefineMethod(
                    name,
                    MethodAttributes.Public | MethodAttributes.Virtual,
                    typeof(object),
                    bridgeSig);

                var il = bridge.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);                       // this
                for (int i = 0; i < bridgeArity; i++)
                    il.Emit(OpCodes.Ldarg, i + 1);              // forward provided args
                for (int i = bridgeArity; i < fullArity; i++)
                    EmitOmittedBridgeArgument(il, fullParams[i].ParameterType);
                il.Emit(OpCodes.Callvirt, fullBuilder);         // dispatch to most-derived full method
                il.Emit(OpCodes.Ret);
            }
        }
    }

    /// <summary>True when the first <paramref name="count"/> parameter types of two signatures are
    /// identical — a precondition for the override bridge to forward args without conversion.</summary>
    private static bool PrefixTypesMatch(ParameterInfo[] a, ParameterInfo[] b, int count)
    {
        for (int i = 0; i < count; i++)
            if (a[i].ParameterType != b[i].ParameterType)
                return false;
        return true;
    }

    /// <summary>
    /// Emits the omitted-argument sentinel for a trailing slot the bridge does not receive, mirroring
    /// <c>EmitOmittedArgument</c>: an <c>object</c> slot (every widened optional/defaulted param) gets
    /// the emitted runtime's <c>$Undefined</c> sentinel — which fires the default prologue and is
    /// observable through <c>typeof</c>/<c>=== undefined</c>; a value-type slot gets its CLR default;
    /// non-object slots receive their CLR default. Defaulted and optional slots are widened to
    /// <c>object</c> hierarchy-consistently by <see cref="ParameterTypeResolver"/>. Standalone-safe:
    /// references the emitted <c>UndefinedInstance</c> field,
    /// not SharpTS.dll.
    /// </summary>
    private void EmitOmittedBridgeArgument(System.Reflection.Emit.ILGenerator il, Type slotType)
    {
        if (slotType == typeof(object))
        {
            if (_runtime != null)
                il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
            else
                il.Emit(OpCodes.Ldnull);
        }
        else if (slotType == typeof(double)) { il.Emit(OpCodes.Ldc_R8, 0.0); }
        else if (slotType == typeof(int)) { il.Emit(OpCodes.Ldc_I4_0); }
        else if (slotType == typeof(bool)) { il.Emit(OpCodes.Ldc_I4_0); }
        else if (slotType == typeof(float)) { il.Emit(OpCodes.Ldc_R4, 0.0f); }
        else if (slotType == typeof(long)) { il.Emit(OpCodes.Ldc_I8, 0L); }
        else if (slotType.IsValueType)
        {
            var local = il.DeclareLocal(slotType);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Initobj, slotType);
            il.Emit(OpCodes.Ldloc, local);
        }
        else { il.Emit(OpCodes.Ldnull); }
    }

    /// <summary>
    /// Emits bodies for ES2022 private methods (both instance and static).
    /// </summary>
    private void EmitPrivateMethodBodies(TypeBuilder typeBuilder, Stmt.Class classStmt, FieldInfo fieldsField, string qualifiedClassName)
    {
        // Emit instance private method bodies
        if (_classes.PrivateMethods.TryGetValue(qualifiedClassName, out var instancePrivateMethods))
        {
            foreach (var method in classStmt.Methods.Where(m => m.IsPrivate && !m.IsStatic && m.Body != null))
            {
                string methodName = method.Name.Lexeme;
                if (methodName.StartsWith('#'))
                    methodName = methodName[1..];

                if (instancePrivateMethods.TryGetValue(methodName, out var methodBuilder))
                {
                    EmitPrivateMethodBody(typeBuilder, methodBuilder, method, fieldsField, qualifiedClassName, isStatic: false);
                }
            }
        }

        // Emit static private method bodies
        if (_classes.StaticPrivateMethods.TryGetValue(qualifiedClassName, out var staticPrivateMethods))
        {
            foreach (var method in classStmt.Methods.Where(m => m.IsPrivate && m.IsStatic && m.Body != null))
            {
                string methodName = method.Name.Lexeme;
                if (methodName.StartsWith('#'))
                    methodName = methodName[1..];

                if (staticPrivateMethods.TryGetValue(methodName, out var methodBuilder))
                {
                    EmitPrivateMethodBody(typeBuilder, methodBuilder, method, fieldsField, qualifiedClassName, isStatic: true);
                }
            }
        }
    }

    /// <summary>
    /// Emits the body of a private method.
    /// </summary>
    private void EmitPrivateMethodBody(
        TypeBuilder typeBuilder,
        MethodBuilder methodBuilder,
        Stmt.Function method,
        FieldInfo fieldsField,
        string qualifiedClassName,
        bool isStatic)
    {
        // #703: a private method referenced as a value (e.g. `this.#m` passed as a callback)
        // pads omitted optional args with the `undefined` sentinel on the value-call path.
        MarkPadsUndefined(methodBuilder);

        // #720: async/generator private methods must be emitted through a state machine, exactly like
        // their public counterparts — not linearly into __private_<name>, which leaves a bare object on
        // the stack for an `async` method declared to return Task<object> (invalid IL) and rejects
        // `yield` ("Yield not supported"). Route by method kind; the parameter-default prologue moves
        // into the state machine's entry (the state-machine emitters apply EmitDefaultParameters
        // themselves). The qualified class name is threaded so nested private member access resolves.
        if (method.IsAsync && method.IsGenerator)
        {
            // Static async generators are not yet supported (the async-generator state machine is
            // instance-only; the public static form fails the same way, #761): fall through to the
            // linear path, which reports the existing "Yield not supported" error rather than invalid IL.
            if (!isStatic)
            {
                EmitAsyncGeneratorMethodBody(methodBuilder, method, fieldsField, currentClassName: qualifiedClassName);
                return;
            }
        }
        else if (method.IsAsync)
        {
            EmitAsyncMethodBody(methodBuilder, method, isStatic ? null : fieldsField,
                isInstanceMethod: !isStatic, currentClassName: qualifiedClassName);
            return;
        }
        else if (method.IsGenerator)
        {
            // Instance and static (static generator support landed in #692) both route through the
            // generator state machine; static uses no `this`/fields slot.
            EmitGeneratorMethodBody(methodBuilder, method, isStatic ? null : fieldsField,
                isInstanceMethod: !isStatic, currentClassName: qualifiedClassName);
            return;
        }
        // A static async generator falls through to the linear emission below, which reports
        // "Yield not supported" (the public static async-generator gap, #761), not invalid IL.

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, methodBuilder);
        ctx.IsStrictMode = true;
        ctx.FieldsField = isStatic ? null : fieldsField;
        ctx.IsInstanceMethod = !isStatic;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = qualifiedClassName;
        ApplyCapturedTopLevelVariableAccess(ctx);
        // Arrow-closure DC field maps — required so arrow closures created inside
        // this method populate their captured-DC fields at newobj time.
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
        ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        ctx.ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null;
        ctx.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields.Count > 0 ? _arrowScopeDCExtraFields : null;
        // CJS resolution — needed so `exports`, `module.exports`, and `require(...)`
        // work inside class method bodies nested in a CJS module.
        ApplyCommonJsModuleAccess(ctx);

        // Define parameters with typed parameter types from method signature
        var methodParams = methodBuilder.GetParameters();
        int paramOffset = isStatic ? 0 : 1;  // Instance methods have 'this' at index 0
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + paramOffset, paramType);
        }

        var emitter = new ILEmitter(ctx);

        // Apply parameter defaults at the top of the body. A defaulted (`x = ...`) or optional
        // (`x?: T`) private-method parameter whose argument was omitted arrives as the `undefined`
        // sentinel (call sites pad omitted trailing args — see EmitPrivateCallUndefinedPadding);
        // this fires the default the same way function/public-method bodies do. Private methods are
        // emitted with all-`object` parameter slots, so none are skipped for being value types. (#696,
        // covers the private-method case of #705's explicit-undefined repro too.)
        emitter.EmitDefaultParameters(
            method.Parameters,
            isInstanceMethod: !isStatic,
            hasOwnThis: false,
            paramTypes: methodParams.Select(p => p.ParameterType).ToArray());

        // Emit method body
        if (method.Body != null)
        {
            // #1237: materialize inner function declarations in place, matching the public-method
            // path so an inner `function` declared inside a private method becomes a binding.
            WireInPlaceInnerFunctions(ctx);

            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Finalize returns or emit default return
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // ECMA-262: a method that completes without an explicit `return <expr>`
            // (here, falling off the end) has completion value `undefined`. Route
            // through EmitDefaultReturnValue so an `object` slot materializes the
            // `$Undefined` sentinel (not CLR null); typed/void slots keep their
            // correct defaults. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Pre-defines a uniquely-named .NET method for each computed symbol-keyed class method
    /// (<c>[Symbol.iterator]() {…}</c>, incl. the generator/async forms) so they flow through the
    /// normal per-method emitters. Recorded in <see cref="ClassState.SymbolMethods"/> for body emission
    /// (<see cref="EmitSymbolMethods"/>) and for runtime symbol-method registration in the class .cctor (#647).
    /// </summary>
    private void DefineSymbolMethods(TypeBuilder typeBuilder, Stmt.Class classStmt, string qualifiedClassName)
    {
        string className = typeBuilder.Name;
        if (_classes.SymbolMethods.ContainsKey(className))
            return;  // already defined (idempotent across multi-module pre-define/emit passes)

        var computed = classStmt.Methods.Where(m =>
            !m.IsPrivate && m.ComputedKey != null && m.Body != null).ToList();
        if (computed.Count == 0)
            return;

        var list = new List<(Stmt.Function, Expr, MethodBuilder)>();
        for (int i = 0; i < computed.Count; i++)
        {
            var method = computed[i];
            // Unique, deterministic name: multiple computed methods must not collide, and the synthetic
            // `<computed>` lexeme is not a dispatchable name.
            string uniqueName = $"$symmethod_{i}";
            var renamed = method with { Name = new Token(TokenType.IDENTIFIER, uniqueName, null, method.Name.Line) };

            // Display-class analysis/registration ran against the original computed-method AST.
            // Body emission uses the renamed copy so it can resolve the synthetic MethodBuilder;
            // carry the identity-keyed environment registration across that copy as well.
            if (_syncMethodFunctionDCKeys.TryGetValue(method, out var syncMethodDCKey))
                _syncMethodFunctionDCKeys[renamed] = syncMethodDCKey;

            // Param types resolve from each parameter's annotation (the type map is keyed by the @@name,
            // not the synthetic IL name) — fine: computed iterator methods are typically parameterless.
            var paramTypes = ParameterTypeResolver.ResolveMethodParameters(
                classStmt.Name.Lexeme, uniqueName, renamed.Parameters, _typeMapper, _typeMap);

            // Async generator first (it sets both flags).
            Type returnType = (method.IsAsync && method.IsGenerator) ? _types.IAsyncEnumerableOfObject :
                              method.IsAsync ? _types.TaskOfObject :
                              method.IsGenerator ? _types.IEnumerableOfObject :
                              typeof(object);

            // Non-virtual (like symbol accessors): the registry holds the exact MethodInfo and its
            // base-chain walk handles inheritance, so virtual override dispatch isn't needed.
            MethodAttributes attrs = MethodAttributes.Public | MethodAttributes.HideBySig;
            if (method.IsStatic)
                attrs |= MethodAttributes.Static;

            var mb = typeBuilder.DefineMethod(uniqueName, attrs, returnType, paramTypes);

            // Register under the unique name so EmitMethod/EmitStaticMethodBody resolve the pre-defined builder.
            if (method.IsStatic)
            {
                _classes.StaticMethods[qualifiedClassName][uniqueName] = mb;
            }
            else
            {
                if (!_classes.InstanceMethods.TryGetValue(qualifiedClassName, out var im))
                {
                    im = [];
                    _classes.InstanceMethods[qualifiedClassName] = im;
                }
                im[uniqueName] = mb;
                if (!_classes.PreDefinedMethods.TryGetValue(className, out var pd))
                {
                    pd = [];
                    _classes.PreDefinedMethods[className] = pd;
                }
                pd[uniqueName] = mb;
            }

            list.Add((renamed, method.ComputedKey!, mb));
        }
        _classes.SymbolMethods[className] = list;
        if (DefineDeferredComputedMethodKeyRegistrar(typeBuilder) is { } deferred)
            _classes.DeferredComputedClassKeys[classStmt] = deferred;
    }

    /// <summary>
    /// Emits the bodies of the computed symbol-keyed methods recorded by <see cref="DefineSymbolMethods"/>,
    /// reusing the normal per-method emitters so the generator/async state machines compose.
    /// </summary>
    private void EmitSymbolMethods(TypeBuilder typeBuilder, string qualifiedClassName, FieldInfo fieldsField)
    {
        if (!_classes.SymbolMethods.TryGetValue(typeBuilder.Name, out var list))
            return;
        foreach (var (method, _key, _builder) in list)
        {
            if (method.IsStatic)
                EmitStaticMethodBody(qualifiedClassName, method);
            else
                EmitMethod(typeBuilder, method, fieldsField);
        }
    }

    private void EmitMethod(TypeBuilder typeBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        MethodBuilder methodBuilder;

        // Check if method was pre-defined in DefineClassMethodsOnly
        if (_classes.PreDefinedMethods.TryGetValue(typeBuilder.Name, out var preDefined) &&
            preDefined.TryGetValue(method.Name.Lexeme, out var existingMethod))
        {
            methodBuilder = existingMethod;
        }
        else
        {
            // Define the method (fallback for when DefineClassMethodsOnly wasn't called)
            // Use typed parameters from TypeMap
            var paramTypes = ParameterTypeResolver.ResolveMethodParameters(
                typeBuilder.Name, method.Name.Lexeme, method.Parameters, _typeMapper, _typeMap);

            MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
            if (method.IsAbstract)
            {
                methodAttrs |= MethodAttributes.Abstract;
            }

            // Set return type based on method kind
            Type returnType = method.IsAsync ? typeof(Task<object>) :
                              method.IsGenerator ? _types.IEnumerableOfObject :
                              typeof(object);

            methodBuilder = typeBuilder.DefineMethod(
                method.Name.Lexeme,
                methodAttrs,
                returnType,
                paramTypes
            );

            // Track instance method for direct dispatch (use FullName to match namespace-qualified lookup)
            if (!_classes.InstanceMethods.TryGetValue(typeBuilder.FullName!, out var classMethods))
            {
                classMethods = [];
                _classes.InstanceMethods[typeBuilder.FullName!] = classMethods;
            }
            classMethods[method.Name.Lexeme] = methodBuilder;
        }

        // #703: a user class method invoked as a value (extracted, `.bind()`-ed, or passed
        // as a callback → `$TSFunction.Invoke`) must pad omitted trailing optional args with
        // the `undefined` sentinel, matching the direct-call path. Marking is safe for direct
        // calls — it only affects the value-call padding mask. Covers sync/async/generator
        // instance methods (they all share this builder before the kind-specific branch).
        MarkPadsUndefined(methodBuilder);
        MarkFunctionLength(methodBuilder, method.Parameters);

        // Apply method-level decorators as .NET attributes
        if (_decoratorMode != DecoratorMode.None)
        {
            ApplyMethodDecorators(method, methodBuilder);
        }

        // Abstract methods have no body
        if (method.IsAbstract)
        {
            return;
        }

        // Async generator methods use combined async generator state machine
        // Must check this FIRST since it has both IsAsync and IsGenerator true
        if (method.IsAsync && method.IsGenerator)
        {
            EmitAsyncGeneratorMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        // Async methods use state machine generation
        if (method.IsAsync)
        {
            EmitAsyncMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        // Generator methods use generator state machine generation
        if (method.IsGenerator)
        {
            EmitGeneratorMethodBody(methodBuilder, method, fieldsField);
            return;
        }

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, methodBuilder);
        ctx.FieldsField = fieldsField;
        ctx.IsInstanceMethod = true;
        // Async arrow support (for async arrows inside non-async methods)
        ctx.AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null;
        ctx.AsyncArrowOuterBuilders = _async.ArrowOuterBuilders;
        ctx.AsyncArrowParentBuilders = _async.ArrowParentBuilders;
        ApplyLockDecoratorFields(ctx);
        // Check for method-level "use strict" directive
        ctx.IsStrictMode = true;
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = typeBuilder.Name;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        SetupSyncMethodFunctionDisplayClass(ctx, il, method);
        // Module-level variable access. For class method bodies we augment
        // TopLevelStaticVars with this module's ESM export fields so bare
        // identifiers like `braceExpand` inside a class method resolve to
        // the module-level `export const braceExpand = ...`. Scoped to the
        // class-method context to avoid perturbing imports/module-init paths.
        ApplyCapturedTopLevelVariableAccess(ctx, memberBodyExports: true);
        // CJS resolution — needed so `exports`, `module.exports`, and `require(...)`
        // work inside class method bodies nested in a CJS module.
        ApplyCommonJsModuleAccess(ctx);
        // Add class generic type parameters to context
        if (_classes.GenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters with their types
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            // Instance methods have 'this' at index 0, so params start at index 1
            Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1, paramType);
        }

        var emitter = new ILEmitter(ctx);

        // Variables for @lock decorator support
        LocalBuilder? prevReentrancyLocal = null;
        LocalBuilder? lockTakenLocal = null;
        FieldBuilder? syncLockField = null;
        FieldBuilder? reentrancyField = null;

        // Set up @lock decorator - reentrancy-aware Monitor pattern
        if (hasLock && _locks.SyncLockFields.TryGetValue(typeBuilder.Name, out syncLockField) &&
            _locks.ReentrancyFields.TryGetValue(typeBuilder.Name, out reentrancyField))
        {
            prevReentrancyLocal = il.DeclareLocal(typeof(int));     // int __prevReentrancy
            lockTakenLocal = il.DeclareLocal(typeof(bool));         // bool __lockTaken

            // Set up deferred return handling for the lock's exception block
            // Use the builder to define the label so it's tracked for validation
            ctx.ReturnValueLocal = il.DeclareLocal(typeof(object));
            ctx.ReturnLabel = ctx.ILBuilder.DefineLabel("lock_deferred_return");
            ctx.ExceptionBlockDepth++;

            // int __prevReentrancy = _lockReentrancy.Value;
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, reentrancyField);                // this._lockReentrancy
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.GetMethod!);
            il.Emit(OpCodes.Stloc, prevReentrancyLocal);

            // _lockReentrancy.Value = __prevReentrancy + 1;
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, reentrancyField);                // this._lockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // bool __lockTaken = false;
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, lockTakenLocal);

            // if (__prevReentrancy == 0) { Monitor.Enter(_syncLock, ref __lockTaken); }
            var skipEnterLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bne_Un, skipEnterLabel);

            // Monitor.Enter(this._syncLock, ref __lockTaken);
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, syncLockField);                  // this._syncLock
            il.Emit(OpCodes.Ldloca, lockTakenLocal);                // ref __lockTaken
            il.Emit(OpCodes.Call, _types.MonitorEnter);

            il.MarkLabel(skipEnterLabel);

            // Begin try block - use builder to keep exception depth in sync
            ctx.ILBuilder.BeginExceptionBlock();
        }

        var defaultParamTypes = methodBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            method.Parameters,
            method.Body,
            defaultParamTypes,
            argumentOffset: 1);
        InitializeSyncMethodCapturedParameters(
            ctx, il, method, methodBuilder, argumentOffset: 1);

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            // #1237: an inner `function` declared inside a method is collected (its method/display
            // class are emitted) but was never materialized into a binding here, so every reference
            // fell through to ThrowUndefinedVariable. Wire the in-place materializer so each inner
            // function declaration is created at its textual position by the statement emitter's
            // Stmt.Function arm. Methods have no function-level display class, so a top-of-body hoist
            // (EmitInnerFunctionHoisting) would snapshot captured method-locals before they are
            // assigned; in-place materialization captures them correctly. See WireInPlaceInnerFunctions.
            WireInPlaceInnerFunctions(ctx);

            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Close @lock decorator - finally block
        if (hasLock && prevReentrancyLocal != null && lockTakenLocal != null &&
            syncLockField != null && reentrancyField != null)
        {
            // Store the implicit completion value if no explicit return was emitted.
            // ReturnValueLocal is guaranteed non-null here (set up earlier in hasLock
            // block) and is always typed `object`, so the default is the `$Undefined`
            // sentinel — a method falling off the end completes with `undefined`. (#588)
            EmitDefaultReturnValue(il, ctx.ReturnValueLocal!.LocalType);
            il.Emit(OpCodes.Stloc, ctx.ReturnValueLocal!);
            ctx.ILBuilder.Emit_Leave(ctx.ReturnLabel);

            // Begin finally block - use builder for exception block tracking
            ctx.ILBuilder.BeginFinallyBlock();

            // _lockReentrancy.Value = __prevReentrancy;
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, reentrancyField);                // this._lockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // if (__lockTaken) { Monitor.Exit(_syncLock); }
            var skipExitLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lockTakenLocal);
            il.Emit(OpCodes.Brfalse, skipExitLabel);

            // Monitor.Exit(this._syncLock);
            il.Emit(OpCodes.Ldarg_0);                               // this
            il.Emit(OpCodes.Ldfld, syncLockField);                  // this._syncLock
            il.Emit(OpCodes.Call, _types.MonitorExit);

            il.MarkLabel(skipExitLabel);

            // End try/finally block - use builder for exception block tracking
            ctx.ILBuilder.EndExceptionBlock();

            ctx.ExceptionBlockDepth--;

            // Mark return label and emit actual return - use builder since label was defined with builder
            ctx.ILBuilder.MarkLabel(ctx.ReturnLabel);
            il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal!);  // Non-null in hasLock path
            il.Emit(OpCodes.Ret);
        }
        // Finalize any deferred returns from exception blocks (non-@lock path)
        else if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Falling off the end completes with `undefined` (ECMA-262). Route through
            // EmitDefaultReturnValue so an `object` slot materializes the `$Undefined`
            // sentinel instead of CLR null. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }

    }
}
