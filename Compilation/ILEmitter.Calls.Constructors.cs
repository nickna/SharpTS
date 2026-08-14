using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Object construction (new expressions) emission for the IL emitter.
/// Built-in constructors (40+ types) are handled by ExpressionEmitterBase.TryEmitBuiltInConstructor.
/// This override adds ILEmitter-specific features: namespace imports, imported class aliases,
/// external .NET types, class expressions, typed parameter conversion, and namespace class construction.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitNew(Expr.New n)
    {
        // `new this(...)` inside a static method/accessor constructs the enclosing class.
        // The callee is Expr.This, which doesn't resolve to a class name via the normal
        // Variable/Get chain extraction — rewrite as Expr.Variable(CurrentClassName).
        if (n.Callee is Expr.This && !_ctx.IsInstanceMethod && _ctx.CurrentClassName != null)
        {
            var synthName = new Token(TokenType.IDENTIFIER, _ctx.CurrentClassName, null, 0);
            n = n with { Callee = new Expr.Variable(synthName) };
        }

        var (namespaceParts, className) = ExtractQualifiedNameFromCallee(n.Callee);

        // A block-scoped class is a runtime lexical binding to a pre-emitted
        // Type token. Construct through the value path so TDZ and shadowing are
        // respected instead of resolving the internal Type globally.
        if (n.Callee is Expr.Variable classVariable
            && _ctx.Locals.TryGetTag(classVariable.Name.Lexeme, out var classTag)
            && classTag is Stmt.Class)
        {
            EmitCalleeExprConstruction(n);
            return;
        }

        // 1. Built-in constructors (Date, Map, Set, Promise, Headers, etc.) - handled by base
        if (namespaceParts.Count == 0 && n.Callee is Expr.Variable && TryEmitBuiltInConstructor(className, n.Arguments))
            return;

        // 2. Intl namespace constructors (Intl.NumberFormat, etc.) - handled by base
        if (TryEmitIntlConstructor(namespaceParts, className, n.Arguments))
            return;

        // 3. Module-qualified constructors (util.TextEncoder, etc.) - handled by base
        if (TryEmitModuleQualifiedConstructor(namespaceParts, className, n.Arguments))
            return;

        // 4. ILEmitter-specific: resolve class name with namespace imports and imported class aliases
        string resolvedClassName = ResolveClassNameForNew(namespaceParts, className);

        // 5. ILEmitter-specific: external .NET type construction
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out var externalType) ||
            _ctx.TypeMapper.ExternalTypes.TryGetValue(resolvedClassName, out externalType))
        {
            EmitExternalTypeConstruction(externalType, n.Arguments);
            return;
        }

        // 6. Class declaration constructor with typed parameter emission
        var ctorBuilder = _ctx.ClassRegistry?.GetConstructorByQualifiedName(resolvedClassName);
        if (_ctx.Classes.TryGetValue(resolvedClassName, out var typeBuilder) && ctorBuilder != null)
        {
            EmitTypedClassConstruction(typeBuilder, ctorBuilder, resolvedClassName, n);
            return;
        }

        // 7. ILEmitter-specific: class expression constructor
        if (_ctx.VarToClassExpr != null &&
            _ctx.VarToClassExpr.TryGetValue(className, out var classExpr) &&
            _ctx.ClassExprConstructors != null &&
            _ctx.ClassExprConstructors.TryGetValue(classExpr, out var classExprCtor))
        {
            EmitTypedClassExprConstruction(classExpr, classExprCtor, n);
            return;
        }

        // 8. ILEmitter-specific: namespace-qualified class (e.g., new Shapes.Circle(2))
        if (namespaceParts.Count > 0 && TryEmitNamespaceClassConstruction(namespaceParts, className, n.Arguments, n.TypeArgs))
            return;

        // 9. Fallback: local variable or resolver
        EmitFallbackConstruction(className, n);
    }

    /// <summary>
    /// Resolves class name considering namespace imports and imported class aliases.
    /// Extends base with additional external type checks.
    /// </summary>
    protected override string ResolveClassNameForNew(List<string> namespaceParts, string className)
    {
        if (namespaceParts.Count > 0)
        {
            string nsAlias = namespaceParts[0];
            if (_ctx.NamespaceImports?.TryGetValue(nsAlias, out var modulePath) == true)
            {
                if (_ctx.ExportedClasses?.TryGetValue(modulePath, out var exportedClasses) == true &&
                    exportedClasses.TryGetValue(className, out var qualifiedName))
                    return qualifiedName;

                string nsPath = string.Join("_", namespaceParts);
                return $"{nsPath}_{className}";
            }
            else
            {
                string nsPath = string.Join("_", namespaceParts);
                return $"{nsPath}_{className}";
            }
        }

        if (_ctx.ImportedClassAliases?.TryGetValue(className, out var importedClassName) == true)
            return importedClassName;

        return _ctx.ResolveClassName(className);
    }

    /// <summary>
    /// Emits class construction with typed parameter conversion (EmitConversionForParameter)
    /// and generic class support. This is more optimal than the base's EnsureBoxed approach.
    /// </summary>
    private void EmitTypedClassConstruction(TypeBuilder typeBuilder, ConstructorBuilder ctorBuilder, string resolvedClassName, Expr.New n)
    {
        var genericParams = _ctx.ClassRegistry!.GetGenericParams(resolvedClassName);
        bool isGeneric = genericParams != null && genericParams.Length > 0;

        // Generic class with inferred type arguments (e.g. `new Box(5)` for `class Box<T>`).
        // Emitting Newobj against the open generic TypeDef throws TypeLoadException at load
        // time (ECMA-335 II.9.4 — the open definition is not a loadable type). Infer the
        // closed type from the constructor arguments before constructing. (#274)
        if (isGeneric && (n.TypeArgs == null || n.TypeArgs.Count == 0))
        {
            EmitInferredGenericClassConstruction(typeBuilder, ctorBuilder, genericParams!, n);
            return;
        }

        Type targetType = typeBuilder;
        ConstructorInfo targetCtor = ctorBuilder;

        // Handle generic class instantiation with explicit type arguments (e.g., new Box<number>(42))
        if (isGeneric && n.TypeArgs != null && n.TypeArgs.Count > 0)
        {
            Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();
            targetType = EmitGenerics.MakeGenericType(typeBuilder, typeArgs);
            targetCtor = EmitterTypeHelpers.ResolveConstructor(targetType, ctorBuilder);
        }

        var ctorParams = ctorBuilder.GetParameters();
        int expectedParamCount = ctorParams.Length;

        // Emit arguments with proper type conversions
        for (int i = 0; i < n.Arguments.Count; i++)
        {
            EmitExpression(n.Arguments[i]);
            if (i < ctorParams.Length)
                EmitConversionForParameter(n.Arguments[i], ctorParams[i].ParameterType);
            else
                EmitBoxIfNeeded(n.Arguments[i]);
        }

        for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            EmitOmittedArgument(ctorParams[i].ParameterType);

        IL.Emit(OpCodes.Newobj, targetCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Constructs a generic class whose type arguments were left to inference
    /// (e.g. <c>new Box(5)</c> for <c>class Box&lt;T&gt;</c>). Arguments are emitted into
    /// typed locals so their <em>actual</em> stack types can drive type-argument inference:
    /// each type parameter that appears directly as a constructor parameter type is bound to
    /// the corresponding argument's CLR type, and any parameter not pinned by an argument
    /// falls back to <c>System.Object</c>. The open generic is then closed before <c>Newobj</c>,
    /// avoiding the <c>TypeLoadException</c> the CLR raises when asked to load the open TypeDef. (#274)
    ///
    /// Inferring from observed stack types (rather than predicting them) keeps the closed
    /// constructor's generic-parameter slots in exact agreement with the emitted values, so no
    /// per-argument boxing/unboxing fix-ups are needed for those positions.
    /// </summary>
    private void EmitInferredGenericClassConstruction(
        TypeBuilder typeBuilder, ConstructorBuilder ctorBuilder,
        GenericTypeParameterBuilder[] genericParams, Expr.New n)
    {
        var openParams = ctorBuilder.GetParameters();

        // 1. Emit each argument into a typed local, recording its stack type.
        var argSlots = new List<(LocalBuilder local, Type clrType, StackType stackType)>(n.Arguments.Count);
        foreach (var arg in n.Arguments)
        {
            EmitExpression(arg);
            StackType st = StackType;
            Type clr = st switch
            {
                StackType.Double => _ctx.Types.Double,
                StackType.Boolean => _ctx.Types.Boolean,
                StackType.String => _ctx.Types.String,
                _ => _ctx.Types.Object
            };
            if (clr == _ctx.Types.Object)
                EmitBoxIfNeeded(arg);
            var local = IL.DeclareLocal(clr);
            IL.Emit(OpCodes.Stloc, local);
            argSlots.Add((local, clr, st));
        }

        // 2. Bind each type parameter from a constructor parameter that is exactly that
        //    type parameter (the open ctor's parameter types reference the generic params).
        var inferred = new Type?[genericParams.Length];
        for (int p = 0; p < openParams.Length && p < argSlots.Count; p++)
        {
            var pt = openParams[p].ParameterType;
            if (pt.IsGenericParameter && pt.GenericParameterPosition < inferred.Length
                && inferred[pt.GenericParameterPosition] == null)
            {
                inferred[pt.GenericParameterPosition] = argSlots[p].clrType;
            }
        }

        // 3. Fill any parameter no argument pinned. Object is the erased-generic default and
        //    matches the interpreter, which treats unconstrained type parameters as `any`.
        for (int i = 0; i < inferred.Length; i++)
            inferred[i] ??= _ctx.Types.Object;

        Type targetType = EmitGenerics.MakeGenericType(typeBuilder, inferred!);
        ConstructorInfo targetCtor = EmitterTypeHelpers.ResolveConstructor(targetType, ctorBuilder);

        // 4. Reload arguments. Generic-parameter slots already hold a value whose CLR type
        //    equals the closed parameter type, so they load as-is; concrete parameters get the
        //    standard conversion against their (open == closed) parameter type.
        for (int i = 0; i < openParams.Length; i++)
        {
            var pt = openParams[i].ParameterType;
            if (i < argSlots.Count)
            {
                IL.Emit(OpCodes.Ldloc, argSlots[i].local);
                SetStackType(argSlots[i].stackType);
                if (!pt.IsGenericParameter)
                    EmitConversionForParameter(n.Arguments[i], pt);
            }
            else if (pt.IsGenericParameter)
            {
                // Under-applied generic-typed parameter: pad with undefined for an object closure,
                // else the closed type's CLR default. (#739)
                EmitOmittedArgument(inferred[pt.GenericParameterPosition]!);
            }
            else
            {
                EmitOmittedArgument(pt);
            }
        }

        IL.Emit(OpCodes.Newobj, targetCtor);
        SetStackUnknown();
    }

    /// <summary>
    /// Emits class expression construction with typed parameter conversion. Generic class
    /// expressions (<c>const Box = class&lt;T&gt; {...}</c>) emit a real open .NET generic type, so
    /// — exactly like generic class declarations (#274) — the open definition must be closed
    /// (via inference or explicit type arguments) before <c>Newobj</c>; emitting against the open
    /// TypeDef throws "the containing type is not fully instantiated" at load time (#291).
    /// </summary>
    private void EmitTypedClassExprConstruction(Expr.ClassExpr classExpr, ConstructorBuilder classExprCtor, Expr.New n)
    {
        var genericParams = _ctx.ClassExprGenericParams?.GetValueOrDefault(classExpr);
        bool isGeneric = genericParams != null && genericParams.Length > 0;

        if (isGeneric && _ctx.ClassExprBuilders != null
            && _ctx.ClassExprBuilders.TryGetValue(classExpr, out var classExprTypeBuilder))
        {
            // Inferred type arguments (e.g. `new Box("hi")`): reuse the declaration-path inference.
            if (n.TypeArgs == null || n.TypeArgs.Count == 0)
            {
                EmitInferredGenericClassConstruction(classExprTypeBuilder, classExprCtor, genericParams!, n);
                return;
            }

            // Explicit type arguments (e.g. `new Box<number>(42)`): close the generic directly.
            Type[] typeArgs = n.TypeArgs.Select(ResolveTypeArg).ToArray();
            Type closedType = EmitGenerics.MakeGenericType(classExprTypeBuilder, typeArgs);
            ConstructorInfo closedCtor = EmitterTypeHelpers.ResolveConstructor(closedType, classExprCtor);
            EmitClassExprCtorCall(closedCtor, closedCtor.GetParameters(), n);
            return;
        }

        EmitClassExprCtorCall(classExprCtor, classExprCtor.GetParameters(), n);
    }

    /// <summary>
    /// Emits argument conversion, default-fill, and the <c>Newobj</c> for a (possibly closed)
    /// class-expression constructor.
    /// </summary>
    private void EmitClassExprCtorCall(ConstructorInfo ctor, ParameterInfo[] ctorParams, Expr.New n)
    {
        int expectedParamCount = ctorParams.Length;

        for (int i = 0; i < n.Arguments.Count; i++)
        {
            EmitExpression(n.Arguments[i]);
            if (i < ctorParams.Length)
                EmitConversionForParameter(n.Arguments[i], ctorParams[i].ParameterType);
            else
                EmitBoxIfNeeded(n.Arguments[i]);
        }

        for (int i = n.Arguments.Count; i < expectedParamCount; i++)
            EmitOmittedArgument(ctorParams[i].ParameterType);

        IL.Emit(OpCodes.Newobj, ctor);
        SetStackUnknown();
    }

    /// <summary>
    /// Fallback constructor emission for <c>new</c> expressions that didn't match a
    /// compile-time known class, class expression, or namespace class.
    ///
    /// For property-access callees like <c>new exports.Foo(...)</c>, evaluates the full
    /// callee and — if the runtime value is a .NET Type (stored by an earlier class-ref
    /// load) — constructs via reflection with ctor-arity-aware argument padding. If the
    /// runtime value is not a Type (e.g. a <c>$TSFunction</c> from a namespace import of
    /// a built-in module), falls back to pushing <c>null</c> rather than throwing, matching
    /// legacy behavior for those paths until a full TSFunction-as-constructor dispatch exists.
    /// </summary>
    private void EmitFallbackConstruction(string className, Expr.New n)
    {
        // Any callee that isn't a bare Variable goes through EmitCalleeExprConstruction
        // — it evaluates the expression once into a local and either constructs from a
        // Type or routes to NewOnFunction. Covers Get/GetIndex (mod.Foo, arr[0]),
        // TypeAssertion / Grouping / NonNullAssertion wrappers (very common — `(outer()
        // as any)('W')`), and direct Call/IIFE callees (`new (outer())('W')`).
        // Variable callees stay on the Ldloc-then-runtime-check path below so the
        // `if (!(this instanceof F)) return new F(args)` idiom (yallist, semver) keeps
        // its current Ldnull semantics for the unresolved-variable subcase — function-
        // identity and instanceof aren't yet aligned for $TSFunction values, so routing
        // those through NewOnFunction would recurse.
        if (n.Callee is not Expr.Variable)
        {
            EmitCalleeExprConstruction(n);
            return;
        }

        // Function declarations take priority over resolver/locals. The
        // resolver can spuriously claim ownership of names that match function
        // declarations (closure-analyzer-driven captures that resolve to null
        // at runtime). Function declarations are always reachable as static
        // methods on the Program class, so the explicit Functions check wins.
        // With Stage 0b (auto-prototype) + Stage 0c (NewOnFunction sets
        // prototype, instanceof walks chain), the yallist/semver
        // `if (!(this instanceof F)) return new F(args)` idiom no longer
        // recurses — the inner `new F(args)` constructs an instance whose
        // prototype chain links to F.prototype, so `this instanceof F` is
        // true on the outer recursive call and the redirect short-circuits.
        if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(className), out var funcMethod))
        {
            // Use TSFunctionGetOrCreate (rather than the bare TSFunctionCtorWithCache)
            // so the wrapper we hand NewOnFunction is the SAME identity as the
            // wrapper bound to the variable `funcMethod.Name`. Without this, the
            // function-prototype's `constructor` (which auto-creates the first
            // time the prototype is read — via whichever wrapper hits the slot)
            // and the variable's wrapper end up as two distinct $TSFunction
            // instances for the same MethodInfo, and `(new F()).constructor === F`
            // is false. Affects test262 assert.throws(CustomError, fn) and any
            // legacy `function Ctor(){}` style.
            IL.Emit(OpCodes.Ldtoken, funcMethod);
            if (_ctx.ProgramType != null)
            {
                IL.Emit(OpCodes.Ldtoken, _ctx.ProgramType);
                IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandleWithType);
            }
            else
            {
                IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
            }
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
            int arity = 0;
            foreach (var param in funcMethod.GetParameters())
            {
                if (param.IsOptional) continue;
                if (param.ParameterType == typeof(List<object>)) continue;
                if (param.Name?.StartsWith("__") == true) continue;
                arity++;
            }
            IL.Emit(OpCodes.Ldstr, className);
            IL.Emit(OpCodes.Ldc_I4, arity);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.TSFunctionGetOrCreate);
        }
        else
        {
            var local = _ctx.Locals.GetLocal(className);
            if (local != null)
            {
                IL.Emit(OpCodes.Ldloc, local);
            }
            else if (_resolver.TryLoadVariable(className) != null)
            {
                // Variable loaded onto stack by the resolver
            }
            else
            {
                // Last resort: evaluate the bare-Variable callee through the
                // standard expression emit path, which knows about non-resolver
                // globals (JSON, Math, Reflect, etc.) — those are non-constructable
                // singletons that should reach NewOnFunction's "not a constructor"
                // TypeError throw rather than vanish into a silent Ldnull.
                EmitExpression(n.Callee);
            }
        }

        // The loaded value is statically typed as `object` (DC fields, top-level static vars,
        // and captured fields are all object-typed). `EmitReflectionConstructFromType` expects
        // a `System.Type` on the stack — passing an `object` directly trips an ILVerify
        // `StackUnexpected` that the JIT sometimes tolerates and sometimes miscompiles into a
        // 0xC0000005 access violation when the value happens to not be a Type (e.g. a cross-
        // module CJS class reference loaded from the entry-point DC field as plain object).
        // Runtime-check for Type and fall through to null for non-Type values — mirrors
        // EmitCalleeExprConstruction's shape so both callee forms behave consistently.
        var objTemp = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objTemp);

        var notTypeLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Isinst, _ctx.Types.Type);
        IL.Emit(OpCodes.Brfalse, notTypeLabel);

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Castclass, _ctx.Types.Type);
        EmitReflectionConstructFromType(n);
        IL.Emit(OpCodes.Br, doneLabel);

        IL.MarkLabel(notTypeLabel);
        EmitNewOnFunctionCall(objTemp, n.Arguments);

        IL.MarkLabel(doneLabel);
    }

    /// <summary>
    /// For <c>new someExpr(...)</c> where <c>someExpr</c> is a property/index access or
    /// a call expression: evaluate, runtime-check for Type, and construct via reflection.
    /// Non-Type callable values (<c>$TSFunction</c>, <c>$BoundTSFunction</c>) route through
    /// <c>$Runtime.NewOnFunction</c> so the constructor body runs and <c>this</c> binds to
    /// the fresh instance — same JS <c>new</c> protocol the Ldloc branch in
    /// <see cref="EmitFallbackConstruction"/> uses. The callee-expression path doesn't
    /// hit the <c>if (!(this instanceof F)) return new F(args)</c> idiom that motivated
    /// the unresolved-variable Ldnull branch above (that idiom always names the
    /// constructor as a bare variable), so routing here is safe.
    /// </summary>
    private void EmitCalleeExprConstruction(Expr.New n)
    {
        EmitExpression(n.Callee);
        // Box value-typed callees (e.g. `new true;`, `new 1;`) before storing to an
        // object slot — without this the JIT crashes with a fatal CLR error on
        // the upcoming Stloc/Ldloc into `object`.
        EmitBoxIfNeeded(n.Callee);
        var objTemp = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objTemp);

        var notTypeLabel = IL.DefineLabel();
        var doneLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Isinst, _ctx.Types.Type);
        IL.Emit(OpCodes.Brfalse, notTypeLabel);

        // It's a Type — construct via reflection.
        IL.Emit(OpCodes.Ldloc, objTemp);
        IL.Emit(OpCodes.Castclass, _ctx.Types.Type);
        EmitReflectionConstructFromType(n);
        IL.Emit(OpCodes.Br, doneLabel);

        IL.MarkLabel(notTypeLabel);
        EmitNewOnFunctionCall(objTemp, n.Arguments);

        IL.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits <c>$Runtime.NewOnFunction(callee, args)</c> — the JS <c>new</c> protocol
    /// for runtime-valued function callees (<c>$TSFunction</c>, <c>$BoundTSFunction</c>).
    /// Leaves the constructed object on the stack. Callee is read from
    /// <paramref name="calleeLocal"/>; args are evaluated and packed into an object[].
    /// </summary>
    private void EmitNewOnFunctionCall(LocalBuilder calleeLocal, List<Expr> arguments)
    {
        IL.Emit(OpCodes.Ldloc, calleeLocal);

        // Evaluate left-to-right and flatten every spread through the same
        // iterator-aware path used by ordinary calls. Construction previously
        // packed Expr.Spread nodes as one array-valued argument, which both
        // lost spread semantics and skipped abrupt iterator completions.
        EmitArgsArrayWithSpread(arguments);

        IL.Emit(OpCodes.Call, _ctx.Runtime!.NewOnFunction);
        SetStackUnknown();
    }

    /// <summary>
    /// Expects a Type on the stack. Emits IL to invoke the Type's first public constructor,
    /// padding the argument array to the ctor's declared arity with nulls so JS-style
    /// under-application still finds a matching overload.
    /// </summary>
    private void EmitReflectionConstructFromType(Expr.New n)
    {
        var typeLocal = IL.DeclareLocal(_ctx.Types.Type);
        IL.Emit(OpCodes.Stloc, typeLocal);

        List<LocalBuilder> argTemps = [];
        foreach (var arg in n.Arguments)
        {
            EmitExpression(arg);
            EmitBoxIfNeeded(arg);
            var t = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, t);
            argTemps.Add(t);
        }

        // An aliased RegExp constructor is the emitted $RegExp Type token,
        // but its CLR constructors do not have a parameterless overload and
        // cannot distinguish omitted arguments from JavaScript null. Route
        // this one built-in through the same boxed-argument helper as a direct
        // `new RegExp(...)` call.
        var constructionDone = IL.DefineLabel();

        // Object.prototype.constructor is represented by the System.Object
        // Type token. Construct a guest $Object so the result participates in
        // JavaScript property/prototype lookup instead of exposing a raw CLR
        // System.Object with no visible `constructor`, toString, or valueOf.
        var notObjectType = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, typeLocal);
        IL.Emit(OpCodes.Ldtoken, _ctx.Types.Object);
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
        IL.Emit(OpCodes.Bne_Un, notObjectType);
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(_ctx.Types.DictionaryStringObject));
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSObjectCtor);
        IL.Emit(OpCodes.Br, constructionDone);
        IL.MarkLabel(notObjectType);

        var regExpType = _ctx.Runtime!.TSRegExpType;
        if (regExpType is not null)
        {
            var notRegExpType = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, typeLocal);
            IL.Emit(OpCodes.Ldtoken, regExpType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            IL.Emit(OpCodes.Bne_Un, notRegExpType);
            if (argTemps.Count > 0)
                IL.Emit(OpCodes.Ldloc, argTemps[0]);
            else
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
            if (argTemps.Count > 1)
                IL.Emit(OpCodes.Ldloc, argTemps[1]);
            else
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
            IL.Emit(OpCodes.Call, _ctx.Runtime.RegExpFromArgs);
            IL.Emit(OpCodes.Br, constructionDone);
            IL.MarkLabel(notRegExpType);
        }

        // ctor = type.GetConstructors()[0]
        var getConstructorsMethod = _ctx.Types.GetMethod(
            typeof(Type),
            "GetConstructors",
            Type.EmptyTypes);
        IL.Emit(OpCodes.Ldloc, typeLocal);
        IL.Emit(OpCodes.Callvirt, getConstructorsMethod);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ldelem_Ref);
        var ctorLocal = IL.DeclareLocal(typeof(ConstructorInfo));
        IL.Emit(OpCodes.Stloc, ctorLocal);

        // arity = ctor.GetParameters().Length
        var getParametersMethod = typeof(MethodBase).GetMethod("GetParameters", Type.EmptyTypes)!;
        IL.Emit(OpCodes.Ldloc, ctorLocal);
        IL.Emit(OpCodes.Callvirt, getParametersMethod);
        IL.Emit(OpCodes.Ldlen);
        IL.Emit(OpCodes.Conv_I4);
        var arityLocal = IL.DeclareLocal(typeof(int));
        IL.Emit(OpCodes.Stloc, arityLocal);

        // args = new object[arity]; default-initialized to null.
        IL.Emit(OpCodes.Ldloc, arityLocal);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        var argsArrayLocal = IL.DeclareLocal(_ctx.Types.ObjectArray);
        IL.Emit(OpCodes.Stloc, argsArrayLocal);

        for (int i = 0; i < argTemps.Count; i++)
        {
            var skipLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, arityLocal);
            IL.Emit(OpCodes.Bge, skipLabel);
            IL.Emit(OpCodes.Ldloc, argsArrayLocal);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldloc, argTemps[i]);
            IL.Emit(OpCodes.Stelem_Ref);
            IL.MarkLabel(skipLabel);
        }

        // ctor.Invoke(args)
        var invokeMethod = typeof(ConstructorInfo).GetMethod("Invoke", [typeof(object[])])!;
        IL.Emit(OpCodes.Ldloc, ctorLocal);
        IL.Emit(OpCodes.Ldloc, argsArrayLocal);
        IL.Emit(OpCodes.Callvirt, invokeMethod);
        IL.MarkLabel(constructionDone);
    }

    /// <summary>
    /// Tries to emit class construction for a namespace-qualified class (e.g., new Shapes.Circle(2)).
    /// </summary>
    private bool TryEmitNamespaceClassConstruction(List<string> namespaceParts, string className, List<Expr> arguments, List<string>? typeArgs)
    {
        string nsPath = namespaceParts[0];
        if (_ctx.NamespaceFields == null || !_ctx.NamespaceFields.TryGetValue(nsPath, out var nsField))
            return false;

        IL.Emit(OpCodes.Ldsfld, nsField);

        for (int i = 1; i < namespaceParts.Count; i++)
        {
            nsPath = $"{nsPath}.{namespaceParts[i]}";
            if (_ctx.NamespaceFields.TryGetValue(nsPath, out var nestedField))
            {
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldsfld, nestedField);
            }
            else
            {
                IL.Emit(OpCodes.Ldstr, namespaceParts[i]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceGet);
            }
        }

        IL.Emit(OpCodes.Ldstr, className);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.TSNamespaceGet);

        if (typeArgs != null && typeArgs.Count > 0)
        {
            IL.Emit(OpCodes.Castclass, _ctx.Types.Type);
            IL.Emit(OpCodes.Ldc_I4, typeArgs.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Type);

            for (int i = 0; i < typeArgs.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                Type resolvedType = ResolveTypeArg(typeArgs[i]);
                IL.Emit(OpCodes.Ldtoken, resolvedType);
                IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
                IL.Emit(OpCodes.Stelem_Ref);
            }

            var makeGenericTypeMethod = _ctx.Types.GetMethod(
                _ctx.Types.Type,
                "MakeGenericType",
                _ctx.Types.MakeArrayType(_ctx.Types.Type));
            IL.Emit(OpCodes.Callvirt, makeGenericTypeMethod);
        }

        IL.Emit(OpCodes.Ldc_I4, arguments.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(arguments[i]);
            EmitBoxIfNeeded(arguments[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        var createInstanceMethod = _ctx.Types.GetMethod(_ctx.Types.Activator, "CreateInstance", _ctx.Types.Type, _ctx.Types.ObjectArray);
        IL.Emit(OpCodes.Call, createInstanceMethod!);

        SetStackUnknown();
        return true;
    }
}
