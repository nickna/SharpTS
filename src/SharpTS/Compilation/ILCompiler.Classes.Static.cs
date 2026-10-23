using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Static method and static constructor emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    private readonly Dictionary<TypeBuilder, Action> _deferredClassRegistrarBodies = new(ReferenceEqualityComparer.Instance);

    private void DefineStaticMethod(TypeBuilder typeBuilder, string className, Stmt.Function method)
    {
        // Skip if already pre-defined in DefineClassMethodsOnly
        if (_classes.StaticMethods.TryGetValue(className, out var existingMethods) &&
            existingMethods.ContainsKey(method.Name.Lexeme))
        {
            return;
        }

        // Resolve typed parameters from TypeMap
        var paramTypes = ParameterTypeResolver.ResolveMethodParameters(
            className, method.Name.Lexeme, method.Parameters, _typeMapper, _typeMap);
        // Keep return type as object (async methods return Task<object>)
        var returnType = method.IsAsync ? _types.TaskOfObject : typeof(object);
        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            paramTypes
        );

        methodBuilder.SetCustomAttribute(_runtime.ObjectKeys.ClassStaticPropertyConstructor,
            CustomAttributeEncoder.Encode(_runtime.ObjectKeys.ClassStaticPropertyConstructor, method.Name.Lexeme, false));

        // Initialize dictionary if needed
        if (!_classes.StaticMethods.ContainsKey(className))
        {
            _classes.StaticMethods[className] = [];
        }
        _classes.StaticMethods[className][method.Name.Lexeme] = methodBuilder;
    }

    private void EmitStaticConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, string qualifiedClassName)
    {
        // Registrar adapters may contain forward class references. Emit their
        // bodies only after every class constructor/method signature exists.
        if (_deferredClassRegistrarBodies.Remove(typeBuilder, out var emitRegistrar)) emitRegistrar();

        // Check if we need a static constructor
        // Note: Declare fields are excluded - they have no initialization
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && !f.IsPrivate && !f.IsDeclare && f.Initializer != null).ToList();
        var staticPrivateFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.IsPrivate && f.Initializer != null).ToList();
        var staticAutoAccessorsWithInit = classStmt.AutoAccessors?.Where(a => a.IsStatic && a.Initializer != null).ToList() ?? [];
        bool hasStaticLockFields = _locks.StaticSyncLockFields.ContainsKey(qualifiedClassName);
        var privateElements = _classes.PrivateElements.Require(qualifiedClassName);
        bool hasStaticInitializers = classStmt.StaticInitializers != null && classStmt.StaticInitializers.Count > 0;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, cctor);
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ctx.CurrentClassName = qualifiedClassName; // Required for static member access via 'this'
        ctx.IsStaticConstructorContext = true;
        ctx.IsStaticSuperContext = true;
        ApplyCapturedTopLevelVariableAccess(ctx);

        // Add class generic type parameters to context (required for static blocks in generic classes)
        if (_classes.GenericParameters.Require(typeBuilder) is { Count: > 0 } classGenericParams)
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        var emitter = new ILEmitter(ctx);
        if (privateElements.StaticPresence is { } presence)
        {
            il.Emit(OpCodes.Newobj, typeof(HashSet<string>).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stsfld, presence);
        }

        // A compact class uses weak side storage; initialize its table before
        // user-observable static initialization can construct or mutate an instance.
        var dynamicFieldsTable = _classes.InstanceFieldsField[qualifiedClassName];
        if (dynamicFieldsTable.IsStatic)
        {
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dynamicFieldsTable.FieldType));
            il.Emit(OpCodes.Stsfld, dynamicFieldsTable);
        }

        // ECMAScript creates Constructor.prototype as part of class evaluation.
        // Register it before user static fields/blocks so they can observe it.
        EmitClassPrototypeRegistration(
            il, typeBuilder, GetClassConstructorLength(classStmt.Methods));

        // Initialize static @lock decorator fields
        if (_locks.StaticSyncLockFields.TryGetValue(qualifiedClassName, out var staticSyncLockField))
        {
            // _staticSyncLock = new object();
            il.Emit(OpCodes.Newobj, _types.ObjectDefaultCtor);
            il.Emit(OpCodes.Stsfld, staticSyncLockField);
        }

        if (_locks.StaticAsyncLockFields.TryGetValue(qualifiedClassName, out var staticAsyncLockField))
        {
            // _staticAsyncLock = new SemaphoreSlim(1, 1);
            il.Emit(OpCodes.Ldc_I4_1);  // initialCount = 1
            il.Emit(OpCodes.Ldc_I4_1);  // maxCount = 1
            il.Emit(OpCodes.Newobj, _types.SemaphoreSlimCtor);
            il.Emit(OpCodes.Stsfld, staticAsyncLockField);
        }

        if (_locks.StaticReentrancyFields.TryGetValue(qualifiedClassName, out var staticReentrancyField))
        {
            // _staticLockReentrancy = new AsyncLocal<int>();
            il.Emit(OpCodes.Newobj, typeof(AsyncLocal<int>).GetConstructor([])!);
            il.Emit(OpCodes.Stsfld, staticReentrancyField);
        }

        // Initialize ES2022 private field storage (ConditionalWeakTable)
        if (privateElements.Storage is { } privateFieldStorage)
        {
            // __privateFields = new ConditionalWeakTable<object, Dictionary<string, object?>>()
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(cwtType));
            il.Emit(OpCodes.Stsfld, privateFieldStorage);
        }

        if (_classes.DeferredDefinitions.TryGet(typeBuilder, out var deferredDefinition))
        {
            if (deferredDefinition.CapturedKeys != null)
                EmitComputedMemberRegistrations(emitter, il, typeBuilder, deferredDefinition);
            il.Emit(OpCodes.Ret);
            il = deferredDefinition.Initializer.GetILGenerator();
            ctx = CreateModuleMemberContext(il, deferredDefinition.Initializer);
            ctx.CurrentClassBuilder = typeBuilder;
            ctx.EmittingTypeBuilder = typeBuilder;
            ctx.CurrentClassName = qualifiedClassName;
            ctx.IsStaticConstructorContext = true;
            ctx.IsStaticSuperContext = true;
            ApplyCapturedTopLevelVariableAccess(ctx);
            emitter = new ILEmitter(ctx);
        }

        // Methods exist before user static fields/blocks execute. Publish once
        // here, so a guest replacement or deletion during initialization remains
        // authoritative. Deferred keys were already published by the registrar.
        EmitComputedMemberRegistrations(emitter, il, typeBuilder);

        // Use StaticInitializers for proper declaration order if available
        if (hasStaticInitializers)
        {
            // Get static field builders
            _classes.StaticFields.TryGetValue(qualifiedClassName, out var classStaticFields);
            var staticPrivateFieldBuilders = privateElements.StaticFields;

            // Emit static initializers in declaration order
            foreach (var initializer in classStmt.StaticInitializers!)
            {
                switch (initializer)
                {
                    case Stmt.Field field when field.IsStatic && !field.IsDeclare:
                        if (field.ComputedKey != null)
                        {
                            _classes.DeferredDefinitions.TryGetFieldKey(field, out var computedKey);
                            EmitComputedStaticFieldInitializer(emitter, il, typeBuilder, field, computedKey);
                            break;
                        }
                        if (field.Initializer != null)
                        {
                            emitter.EmitExpression(field.Initializer);
                            emitter.EmitBoxIfNeeded(field.Initializer);

                            if (field.IsPrivate)
                            {
                                string fieldName = field.Name.Lexeme;
                                if (fieldName.StartsWith('#'))
                                    fieldName = fieldName[1..];
                                il.Emit(OpCodes.Stsfld, staticPrivateFieldBuilders[fieldName]);
                            }
                            else if (classStaticFields != null)
                            {
                                var staticField = classStaticFields[field.Name.Lexeme];
                                il.Emit(OpCodes.Stsfld, staticField);
                            }
                        }
                        if (field.IsPrivate)
                            EmitStaticPrivatePresence(il, privateElements, field.Name.Lexeme.TrimStart('#'));
                        break;

                    case Stmt.StaticBlock block:
                        // Enter the ordinary lexical scope/cell storage for this block;
                        // closures retain its cells while sibling blocks stay independent.
                        emitter.EmitStatement(new Stmt.Block(block.Body));
                        break;
                }
            }
        }
        else
        {
            // Old behavior: initialize static private fields with their initializers
            if (privateElements.StaticFields.Count > 0)
            {
                var staticPrivateFieldBuilders = privateElements.StaticFields;
                foreach (var field in classStmt.Fields.Where(f => f.IsStatic && f.IsPrivate && f.Initializer != null))
                {
                    string fieldName = field.Name.Lexeme;
                    if (fieldName.StartsWith('#'))
                        fieldName = fieldName[1..];

                    var staticPrivateField = staticPrivateFieldBuilders[fieldName];
                    emitter.EmitExpression(field.Initializer!);
                    emitter.EmitBoxIfNeeded(field.Initializer!);
                    il.Emit(OpCodes.Stsfld, staticPrivateField);
                    EmitStaticPrivatePresence(il, privateElements, fieldName);
                }
            }

            // Initialize static field initializers
            if (staticFieldsWithInit.Count > 0 && _classes.StaticFields.TryGetValue(qualifiedClassName, out var classStaticFields))
            {
                foreach (var field in staticFieldsWithInit)
                {
                    // Emit the initializer expression
                    emitter.EmitExpression(field.Initializer!);
                    emitter.EmitBoxIfNeeded(field.Initializer!);

                    // Store in static field using the stored FieldBuilder
                    var staticField = classStaticFields[field.Name.Lexeme];
                    il.Emit(OpCodes.Stsfld, staticField);
                }
            }
        }

        // Initialize static auto-accessor backing fields (TypeScript 4.9+)
        foreach (var autoAccessor in staticAutoAccessorsWithInit)
        {
            EmitAutoAccessorInitializer(emitter, autoAccessor, qualifiedClassName, isStatic: true);
        }

        il.Emit(OpCodes.Ret);
        if (deferredDefinition != null)
            _classes.DeferredDefinitions.MarkInitializerEmitted(deferredDefinition);
    }

    private void EmitStaticPrivatePresence(ILGenerator il, PrivateClassElements elements, string name)
    {
        il.Emit(OpCodes.Ldsfld, EmitterTypeHelpers.SelfFieldReference(elements.StaticPresence!));
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(HashSet<string>), "Add", typeof(string)));
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Installs instance and computed static members in definition order.
    /// </summary>
    private void EmitComputedMemberRegistrations(ILEmitter emitter, ILGenerator il, TypeBuilder typeBuilder,
        DeferredClassDefinition? capturedDefinition = null)
    {
        if (capturedDefinition == null && _classes.DeferredDefinitions.TryGet(typeBuilder, out _)) return;
        var capturedKeys = capturedDefinition?.CapturedKeys;
        var noCapturedKeys = il.DefineLabel();
        if (capturedKeys != null)
        {
            // The erased owner's .cctor precedes guest key evaluation. Later
            // closed owners reuse those keys for their own prototype carriers.
            il.Emit(OpCodes.Ldsfld, capturedKeys);
            il.Emit(OpCodes.Brfalse, noCapturedKeys);
        }
        foreach (var member in GetClassMemberRegistrations(typeBuilder))
        {
            if (capturedKeys != null && member.Builder?.IsStatic == true) continue;
            // Constant static accessors were installed before user initialization.
            if (member.Accessor is { IsStatic: true } && GetConstantComputedAccessorName(member.Key) != null
                && GetNominalClassDeclaration(typeBuilder) != null) continue;
            EmitClassMemberRegistration(il, typeBuilder, member, () => EmitKey(member));
        }
        il.MarkLabel(noCapturedKeys);

        void EmitKey(ClassMemberRegistration member)
        {
            var key = member.Key;
            if (member.Method?.ComputedKey == null && member.Accessor?.ComputedKey == null)
            {
                il.Emit(OpCodes.Ldstr, (string)((Expr.Literal)key).Value!);
                return;
            }
            if (capturedKeys == null)
            {
                EmitComputedPropertyKey(emitter, il, key);
                return;
            }
            int index = Enumerable.Range(0, capturedDefinition!.Keys.Count)
                .First(index => ReferenceEquals(capturedDefinition.Keys[index], key));
            il.Emit(OpCodes.Ldsfld, capturedKeys);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
        }
    }

    private sealed record ClassMemberRegistration(Stmt.Function? Method, Stmt.Accessor? Accessor,
        Stmt.AutoAccessor? AutoAccessor, Expr Key, MethodBuilder? Builder, int Position);

    private IEnumerable<ClassMemberRegistration> GetClassMemberRegistrations(TypeBuilder owner)
    {
        var members = new List<ClassMemberRegistration>();
        foreach (var (method, key, builder) in _classes.ComputedMembers.GetMethods(owner))
            members.Add(new(method, null, null, key, builder, method.Name.SourceOrderOffset));
        foreach (var (accessor, builder) in _classes.ComputedMembers.GetAccessors(owner))
            members.Add(new(null, accessor, null, accessor.ComputedKey!, builder, accessor.Name.SourceOrderOffset));
        if (GetNominalClassDeclaration(owner) is { } declaration)
        {
            var methods = _classes.InstanceMethods.GetValueOrDefault(owner.FullName!);
            foreach (var method in declaration.Methods.Where(method => method.Body != null && !method.IsStatic
                         && !method.IsPrivate && method.ComputedKey == null && method.Name.Lexeme != "constructor"))
                if (methods?.TryGetValue(method.Name.Lexeme, out var builder) == true)
                    members.Add(new(method, null, null, new Expr.Literal(method.Name.Lexeme), builder, method.Name.SourceOrderOffset));
            foreach (var accessor in (declaration.Accessors ?? []).Where(accessor => !accessor.IsStatic && accessor.ComputedKey == null))
                if (_classes.AccessorBuilders.TryGetValue(accessor, out var builder))
                    members.Add(new(null, accessor, null, new Expr.Literal(accessor.Name.Lexeme), builder, accessor.Name.SourceOrderOffset));
            foreach (var source in _receiverAutoAccessors.Where(entry => ReferenceEquals(entry.Value.Owner, owner)).Select(entry => entry.Key))
                members.Add(new(null, null, source, new Expr.Literal(source.Name.Lexeme), null, source.Name.SourceOrderOffset));
        }
        return members.OrderBy(member => member.Position);
    }

    private void EmitClassMemberRegistration(ILGenerator il, TypeBuilder owner, ClassMemberRegistration member,
        Action emitKey, Type? descriptorOwner = null)
    {
        if (member.AutoAccessor is { } autoAccessor)
        {
            EmitReceiverAutoAccessorDescriptors(il, owner, autoAccessor, descriptorOwner);
            return;
        }
        var key = il.DeclareLocal(_types.Object);
        emitKey(); il.Emit(OpCodes.Stloc, key);
        if (member.Accessor is { } accessor)
        {
            EmitDeclaredAccessorDescriptor(il, owner, accessor, member.Builder!,
                () => il.Emit(OpCodes.Ldloc, key), descriptorOwner);
            return;
        }
        var method = member.Method!;
        var done = il.DefineLabel();
        if (CanOwnDeclaredSymbolMethodValue(owner))
        {
            EmitDeclaredComputedMethodDescriptor(il, owner, method, member.Builder!, key, descriptorOwner,
                sourceOrdered: !method.IsStatic);
            il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Isinst, _runtime.Symbols.Type);
            il.Emit(OpCodes.Brtrue, done);
        }
        if (method.ComputedKey != null)
        {
            il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldloc, key);
            EmitMethodInfoLiteral(il, member.Builder!, owner);
            il.Emit(method.IsStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _runtime.SymbolAccessors.RegisterMethod);
        }
        il.MarkLabel(done);
    }

    private void DefineDeferredComputedMethodKeyRegistrar(object source, TypeBuilder typeBuilder, IReadOnlyList<Stmt.Field> fields)
    {
        if (_classes.DeferredDefinitions.TryGet(typeBuilder, out var existing))
        {
            if (!_classes.DeferredDefinitions.TryGet(source, out var bySource) || !ReferenceEquals(existing, bySource))
                throw new InvalidOperationException("The deferred class type belongs to a different declaration.");
            return;
        }
        var deferred = new List<(Expr Key, MethodBuilder? Builder, bool IsStatic, bool? IsGetter, int Position, Stmt.Field? Field)>();
        foreach (var (method, key, builder) in _classes.ComputedMembers.GetMethods(typeBuilder))
            deferred.Add((key, builder, method.IsStatic, null, method.Name.SourceOrderOffset, null));
        foreach (var (accessor, builder) in _classes.ComputedMembers.GetAccessors(typeBuilder))
            deferred.Add((accessor.ComputedKey!, builder, accessor.IsStatic, accessor.Kind.Type == TokenType.GET, accessor.Name.SourceOrderOffset, null));
        foreach (var field in fields.Where(field => field.ComputedKey != null && !field.IsDeclare))
            deferred.Add((field.ComputedKey!, null, field.IsStatic, null, field.Name.SourceOrderOffset, field));
        // All closed owners replay the same source keys alongside ordinary
        // members, preserving their shared creation order without re-evaluation.
        bool hasGenericKeys = source is Stmt.Class && typeBuilder.IsGenericTypeDefinition && deferred.Count > 0;
        // Field names are evaluated with the definition, even when no key suspends.
        if (!hasGenericKeys && !deferred.Any(entry => entry.Field != null || ExpressionContainsSuspension(entry.Key)
            || ExpressionContainsLexicalNewTarget(entry.Key)))
            return;

        deferred = deferred.OrderBy(entry => entry.Position).ToList();
        var initializer = typeBuilder.DefineMethod("$initializeDeferredClass",
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void, Type.EmptyTypes);

        // TypeScript type arguments do not create separate class definitions. Keep
        // captured keys outside generic CLR instantiations so all instances share them.
        var keyOwner = typeBuilder.IsGenericTypeDefinition ? _programType : typeBuilder;
        var registrar = keyOwner.DefineMethod(
            typeBuilder.IsGenericTypeDefinition ? $"$registerDeferredComputedKeys_{typeBuilder.Name}" : "$registerDeferredComputedKeys",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]);
        var fieldKeys = new Dictionary<Stmt.Field, FieldBuilder>(ReferenceEqualityComparer.Instance);
        FieldBuilder? capturedKeys = null;
        if (hasGenericKeys)
        {
            capturedKeys = keyOwner.DefineField($"$computedMemberKeys_{typeBuilder.Name}", _types.ObjectArray,
                FieldAttributes.Assembly | FieldAttributes.Static);
        }

        for (int i = 0; i < deferred.Count; i++)
        {
            var field = deferred[i].Field;
            if (field != null)
            {
                var keyField = keyOwner.DefineField($"$computedFieldKey_{typeBuilder.Name}_{i}", _types.Object,
                    FieldAttributes.Assembly | FieldAttributes.Static);
                fieldKeys.Add(field, keyField);
            }
        }

        _deferredClassRegistrarBodies.Add(typeBuilder, () =>
        {
            var il = registrar.GetILGenerator();
            if (capturedKeys != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, capturedKeys);
            }
            for (int i = 0; i < deferred.Count; i++)
            {
                if (deferred[i].Field is { } field)
                {
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Stsfld, fieldKeys[field]);
                }
            }
            foreach (var member in GetClassMemberRegistrations(typeBuilder))
            {
                EmitClassMemberRegistration(il, typeBuilder, member, () =>
                {
                    bool computed = member.Method?.ComputedKey != null || member.Accessor?.ComputedKey != null;
                    if (!computed)
                    {
                        il.Emit(OpCodes.Ldstr, (string)((Expr.Literal)member.Key).Value!);
                        return;
                    }
                    int index = Enumerable.Range(0, deferred.Count).First(index => ReferenceEquals(deferred[index].Key, member.Key));
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
                }, CanonicalGenericClassOwner(typeBuilder));
            }

            il.Emit(OpCodes.Call, typeBuilder.IsGenericTypeDefinition
                ? EmitterTypeHelpers.ResolveMethod(
                    CanonicalGenericClassOwner(typeBuilder), initializer)
                : initializer);
            il.Emit(OpCodes.Ret);
        });
        var keys = deferred.Select(entry => entry.Key).ToArray();
        _classes.DeferredDefinitions.Declare(source, typeBuilder, initializer, registrar, keys, fieldKeys, capturedKeys);
        if (hasGenericKeys)
            typeBuilder.SetCustomAttribute(_runtime.ClassInitialization.CanonicalStaticStorageAttributeConstructor,
                CustomAttributeEncoder.EmptyBlob);
    }

    private void EmitComputedStaticFieldInitializer(ILEmitter emitter, ILGenerator il,
        TypeBuilder owner, Stmt.Field field, FieldBuilder? key)
    {
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        if (key != null)
            il.Emit(OpCodes.Ldsfld, key);
        else
        {
            emitter.EmitExpression(field.ComputedKey!);
            emitter.EmitBoxIfNeeded(field.ComputedKey!);
        }
        if (field.Initializer != null)
        {
            emitter.EmitExpression(field.Initializer);
            emitter.EmitBoxIfNeeded(field.Initializer);
        }
        else
            il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);

        // A field defines an own W/E/C data property. Assignment would instead
        // refuse configurable readonly constructor metadata such as name/length
        // or invoke an inherited setter instead of creating the field.
        var value = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, value);
        EmitOwnDataDefinition(il, value);
    }

    private static bool ExpressionContainsSuspension(Expr expression)
    {
        var visitor = new SuspensionPresenceVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class SuspensionPresenceVisitor : Parsing.Visitors.AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitClass(Stmt.Class stmt)
        {
            foreach (var expression in ClassDefinitionExpressions.Enumerate(stmt))
                Visit(expression);
        }
        protected override void VisitClassExpr(Expr.ClassExpr expr)
        {
            foreach (var expression in ClassDefinitionExpressions.Enumerate(expr))
                Visit(expression);
        }

        protected override void VisitAwait(Expr.Await expr)
        {
            Found = true;
            ShouldContinue = false;
        }

        protected override void VisitYield(Expr.Yield expr)
        {
            Found = true;
            ShouldContinue = false;
        }
    }

    private void EmitComputedPropertyKey(ILEmitter emitter, ILGenerator il, Expr key)
    {
        emitter.EmitExpression(key);
        emitter.EmitBoxIfNeeded(key);
        il.Emit(OpCodes.Call, _runtime.Reflect.ToPropertyKey);
    }

    private void EmitMethodInfoLiteral(ILGenerator il, MethodBuilder method, TypeBuilder declaringType)
    {
        il.Emit(OpCodes.Ldtoken, method);
        il.Emit(OpCodes.Ldtoken, declaringType);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
    }

    private void EmitStaticMethodBody(string className, Stmt.Function method)
    {
        // #703: a static method invoked as a value pads omitted optional args with the
        // `undefined` sentinel on the value-call path. Mark before the branches below so it
        // covers sync, async, and generator (#692) static methods (same builder).
        MarkPadsUndefined(_classes.StaticMethods[className][method.Name.Lexeme]);
        MarkFunctionLength(_classes.StaticMethods[className][method.Name.Lexeme], method.Parameters);
        if (!method.IsGenerator)
            MarkNonConstructible(_classes.StaticMethods[className][method.Name.Lexeme]);

        // Static async generator methods (#778) hoist their guest receiver without a CLR instance
        // parameter. Checked FIRST since a `static async *m()` has both IsAsync and
        // IsGenerator set, so it must not fall into the sync-generator or plain-async branch below.
        if (method.IsGenerator && method.IsAsync)
        {
            var agMethodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];
            EmitAsyncGeneratorMethodBody(agMethodBuilder, method, fieldsField: null, isInstanceMethod: false);
            return;
        }

        // Static generator methods (#692) likewise hoist their guest receiver.
        if (method.IsGenerator && !method.IsAsync)
        {
            var genMethodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];
            EmitGeneratorMethodBody(genMethodBuilder, method, fieldsField: null, isInstanceMethod: false);
            return;
        }

        // Async static methods use state machine generation
        if (method.IsAsync)
        {
            EmitStaticAsyncMethodBody(className, method);
            return;
        }

        var typeBuilder = _classes.Builders[className];
        var methodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, methodBuilder);
        ctx.IsStrictMode = true;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.IsStaticSuperContext = true;
        ctx.EmittingTypeBuilder = typeBuilder;
        ApplyLockDecoratorFields(ctx);
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = className;
        ApplyCapturedTopLevelVariableAccess(ctx);
        SetupSyncMethodFunctionDisplayClass(ctx, il, method);

        // Define parameters with types (starting at index 0, not 1 since no 'this')
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i, paramType);
        }

        var emitter = new ILEmitter(ctx);

        // Apply parameter defaults. Static methods get no OverloadGenerator forwarding (only
        // free functions do), so without this a defaulted argument that is omitted or explicit
        // `undefined` would never fire its default (omit → null/0, undefined → NaN/cast error).
        // Value-type-defaulted params are widened to an object slot by ParameterTypeResolver so
        // the prologue can observe the `$Undefined` sentinel. (#705)
        var staticDefaultParamTypes = methodBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            method.Parameters,
            method.Body,
            staticDefaultParamTypes,
            argumentOffset: 0);
        InitializeSyncMethodCapturedParameters(
            ctx, il, method, methodBuilder, argumentOffset: 0);

        // Variables for @lock decorator support
        LocalBuilder? prevReentrancyLocal = null;
        LocalBuilder? lockTakenLocal = null;
        FieldBuilder? staticSyncLockField = null;
        FieldBuilder? staticReentrancyField = null;

        // Set up @lock decorator - reentrancy-aware Monitor pattern for static methods
        if (hasLock && _locks.StaticSyncLockFields.TryGetValue(className, out staticSyncLockField) &&
            _locks.StaticReentrancyFields.TryGetValue(className, out staticReentrancyField))
        {
            prevReentrancyLocal = il.DeclareLocal(typeof(int));     // int __prevReentrancy
            lockTakenLocal = il.DeclareLocal(typeof(bool));         // bool __lockTaken

            // Set up deferred return handling for the lock's exception block
            // Use the builder to define the label so it's tracked for validation
            ctx.ReturnValueLocal = il.DeclareLocal(typeof(object));
            ctx.ReturnLabel = ctx.ILBuilder.DefineLabel("static_lock_deferred_return");
            ctx.ExceptionBlockDepth++;

            // int __prevReentrancy = _staticLockReentrancy.Value;
            il.Emit(OpCodes.Ldsfld, staticReentrancyField);         // _staticLockReentrancy
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.GetMethod!);
            il.Emit(OpCodes.Stloc, prevReentrancyLocal);

            // _staticLockReentrancy.Value = __prevReentrancy + 1;
            il.Emit(OpCodes.Ldsfld, staticReentrancyField);         // _staticLockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // bool __lockTaken = false;
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, lockTakenLocal);

            // if (__prevReentrancy == 0) { Monitor.Enter(_staticSyncLock, ref __lockTaken); }
            var skipEnterLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bne_Un, skipEnterLabel);

            // Monitor.Enter(_staticSyncLock, ref __lockTaken);
            il.Emit(OpCodes.Ldsfld, staticSyncLockField);           // _staticSyncLock
            il.Emit(OpCodes.Ldloca, lockTakenLocal);                // ref __lockTaken
            il.Emit(OpCodes.Call, _types.MonitorEnter);

            il.MarkLabel(skipEnterLabel);

            // Begin try block - use builder to keep exception depth in sync
            ctx.ILBuilder.BeginExceptionBlock();
        }

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            // #1237: materialize inner function declarations in place, matching the instance-method
            // path so an inner `function` declared inside a static method becomes a binding.
            WireInPlaceInnerFunctions(ctx);

            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Close @lock decorator - finally block for static methods
        if (hasLock && prevReentrancyLocal != null && lockTakenLocal != null &&
            staticSyncLockField != null && staticReentrancyField != null)
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

            // _staticLockReentrancy.Value = __prevReentrancy;
            il.Emit(OpCodes.Ldsfld, staticReentrancyField);         // _staticLockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // if (__lockTaken) { Monitor.Exit(_staticSyncLock); }
            var skipExitLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lockTakenLocal);
            il.Emit(OpCodes.Brfalse, skipExitLabel);

            // Monitor.Exit(_staticSyncLock);
            il.Emit(OpCodes.Ldsfld, staticSyncLockField);           // _staticSyncLock
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

    private void EmitStaticAsyncMethodBody(string className, Stmt.Function method)
    {
        var typeBuilder = _classes.Builders[className];
        var methodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];

        // Analyze async function to determine await points and hoisted variables
        var analysis = AddAsyncReturnCompletionState(
            _async.Analyzer.Analyze(method, preserveDebugBindings: EmitDebugSymbols));

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

        // Build state machine type
        var smBuilder = new AsyncStateMachineBuilder(_moduleBuilder, _types, _async.StateMachineCounter++);
        ConfigureDebugStateMachineOwner(smBuilder, typeBuilder, methodBuilder.Name);
        ConfigureDebugHoistedBindings(smBuilder, method.Parameters, method.Body, analysis.BlockScopeRenames);
        var hasAsyncArrows = analysis.AsyncArrows.Count > 0;
        smBuilder.DefineStateMachine(
            $"{className}_{method.Name.Lexeme}",
            analysis,
            _types.Object,
            isInstanceMethod: false,  // Static method!
            hasAsyncArrows: hasAsyncArrows,
            hasLock: hasLock,
            hoistedParameterTypes: GetStableAsyncParameterFieldTypes(method),
            hasDynamicThis: true
        );

        if (EmitDebugSymbols)
            RegisterStateMachine(methodBuilder, smBuilder.StateMachineType, smBuilder.MoveNextMethod,
                EmittedStateMachineKind.Async, smBuilder.SetStateMachineMethod);

        // #682/#follow-up: attach the static method's function display class (registered in Phase 4) to
        // the state machine — shares verifiable reference storage for both async-arrow and nested
        // sync-arrow captured writes. Null when nothing is shared.
        string? methodDCKey = SetupAsyncMethodFunctionDC(smBuilder, method);

        // Build state machines for any async arrows found in this method
        DefineAsyncArrowStateMachines(analysis.AsyncArrows, smBuilder);

        // Get static lock fields if @lock decorator is present
        FieldBuilder? staticAsyncLockField = null;
        FieldBuilder? staticLockReentrancyField = null;
        if (hasLock)
        {
            _locks.StaticAsyncLockFields.TryGetValue(className, out staticAsyncLockField);
            _locks.StaticReentrancyFields.TryGetValue(className, out staticLockReentrancyField);
        }

        // Emit stub method body (creates state machine and starts it)
        // Pass isInstanceMethod: false and static lock fields
        EmitAsyncStubMethod(
            methodBuilder,
            smBuilder,
            method,
            isInstanceMethod: false,  // Static method!
            staticAsyncLockField,
            staticLockReentrancyField,
            functionDCKey: methodDCKey);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, smBuilder.MoveNextMethod);
        ctx.IsStrictMode = true;
        // Static method: IsInstanceMethod stays false (the default).
        ctx.IsStaticSuperContext = true;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ctx.AsyncArrowBuilders = _async.ArrowBuilders;
        ctx.AsyncArrowOuterBuilders = _async.ArrowOuterBuilders;
        ctx.AsyncArrowParentBuilders = _async.ArrowParentBuilders;
        ApplyLockDecoratorFields(ctx);
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = className;

        // #682: route promoted captures through the static method's function display class.
        WireAsyncMethodFunctionDC(ctx, smBuilder, methodDCKey);

        // Emit MoveNext body
        var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis, _types);
        moveNextEmitter.EmitMoveNext(method.Body, ctx, _types.Object, method.Parameters);

        // Emit MoveNext bodies for async arrows. Delegate to the shared EmitAsyncArrowMoveNext (which
        // builds a fresh per-arrow ctx with the arrow's own IL) rather than reusing this method's ctx —
        // the latter routed strategy emissions via `ctx.IL` into the method's IL stream, producing
        // invalid IL for any suspending arrow in an async method (see EmitAsyncMethodBody for details).
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_async.ArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                EmitAsyncArrowMoveNext(arrowBuilder, arrowInfo.Arrow, ctx);
            }
        }

        // Finalize async arrow state machine types
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_async.ArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                arrowBuilder.CreateType();
            }
        }

        // Finalize state machine type
        smBuilder.CreateType();
    }
}
