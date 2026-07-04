using SharpTS.Parsing;
using SharpTS.TypeSystem;
using TSTypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Compilation;

/// <summary>
/// Centralized parameter and return type resolution from TypeMap.
/// Provides typed .NET types instead of defaulting to object.
/// </summary>
public static class ParameterTypeResolver
{
    /// <summary>
    /// Resolves parameter types from TypeMap function type info.
    /// Falls back to object if type info is not available.
    /// </summary>
    /// <param name="parameters">Parameters from AST</param>
    /// <param name="typeMapper">TypeMapper for converting TypeInfo to .NET Type</param>
    /// <param name="funcType">Function type from TypeMap (may be null)</param>
    /// <param name="typeMap">
    /// TypeMap used to consult the #372 undefined-reachable-parameter flag. May be null (e.g. arrow
    /// resolution without a recorded function type), in which case no parameter is widened on that basis.
    /// </param>
    /// <returns>Array of .NET types for each parameter</returns>
    public static Type[] ResolveParameters(
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TSTypeInfo.Function? funcType,
        TypeMap? typeMap = null)
    {
        if (funcType == null || funcType.ParamTypes.Count != parameters.Count)
        {
            // Fallback: try to resolve from parameter type annotations
            var fallback = parameters.Select(p => WidenIfUndefinedReachableParam(ResolveParameterType(p, typeMapper), p, typeMap)).ToArray();
            WidenDefaultedParamsToObject(fallback, parameters, typeof(object));
            return fallback;
        }

        // Map each parameter type, but use 'object' for:
        // 1. Optional parameters without explicit defaults (preserves null-checking)
        // 2. BigInteger parameters (operations expect boxed values)
        // 3. Rest parameters — `$TSFunction.Invoke` / `AdjustArgs` only recognize
        //    `List<object>` when packing trailing args; using a typed list like
        //    `List<string>` for `...parts: string[]` breaks the dispatch path
        //    (method gets invoked without rest packing, trailing args are dropped).
        var resolved = funcType.ParamTypes
            .Select((pt, i) =>
            {
                var mappedType = typeMapper.MapTypeInfoStrict(pt);

                // BigInteger parameters need to stay as object because BigInt operations
                // in the emitter expect boxed values
                if (mappedType == typeof(System.Numerics.BigInteger))
                {
                    return typeof(object);
                }

                // If parameter is optional (no explicit default), use object so undefined
                // can be passed as the missing-argument sentinel (JS spec)
                if (i < parameters.Count &&
                    parameters[i].DefaultValue == null &&
                    parameters[i].IsOptional)
                {
                    return typeof(object);
                }

                // Rest parameter — dispatch helper expects List<object> as the marker.
                if (i < parameters.Count && parameters[i].IsRest)
                {
                    return typeof(List<object>);
                }

                // Function-typed params: runtime values are $TSFunction or other
                // callable classes (e.g. PromisifyCallback), not .NET Delegate
                // subclasses. Signatures that demand Delegate reject them at
                // MethodInfo.Invoke time.
                if (mappedType == typeof(Delegate) || mappedType.IsSubclassOf(typeof(Delegate)))
                {
                    return typeof(object);
                }

                return CoerceParamSlotType(
                    WidenIfUndefinedReachableParam(mappedType, parameters[i], typeMap), pt, typeMapper);
            })
            .ToArray();

        WidenDefaultedParamsToObject(resolved, parameters, typeof(object));
        return resolved;
    }

    /// <summary>
    /// Widens any parameter that has a <b>default value</b> to an <c>object</c> slot. Required for
    /// every function kind that applies parameter defaults via the runtime entry prologue
    /// (<see cref="ILEmitter.EmitDefaultParameters"/>): arrow functions and function expressions
    /// (which get no overloads), and — since #705 — function declarations, class methods, and
    /// constructors. The prologue detects a missing/undefined argument by comparing the slot against
    /// null and the <c>$Undefined</c> sentinel, and value-call padding (<c>$TSFunction.AdjustArgs</c>)
    /// fills omitted slots with that sentinel. Only an <c>object</c> slot can hold it: a value-type
    /// slot can never be null (and emits invalid <c>ldarg; brfalse</c> IL — so the prologue skips it
    /// and the default never fires), and a typed reference slot (e.g. <c>string</c>) coerces the
    /// sentinel to a real value (the string <c>"undefined"</c>) before the prologue can observe it.
    /// Either way the default could never fire. Applied centrally in <see cref="ResolveParameters"/>,
    /// <see cref="ResolveMethodParameters"/>, and <see cref="ResolveConstructorParameters"/> so the
    /// define-time and emit-time signatures always agree. (#646, #705)
    /// </summary>
    public static void WidenDefaultedParamsToObject(Type[] resolved, List<Stmt.Parameter> parameters, Type objectType)
    {
        for (int i = 0; i < parameters.Count && i < resolved.Length; i++)
        {
            // A rest parameter is never "omitted" (it collects zero-or-more trailing args into a
            // List<object>) and its `= ` form is illegal TS, but guard anyway so a future change
            // can't turn the rest marker List<object> into a plain object slot and break dispatch.
            if (parameters[i].DefaultValue != null && !parameters[i].IsRest && resolved[i] != objectType)
                resolved[i] = objectType;
        }
    }

    /// <summary>
    /// #372: widen a <c>number</c>/<c>boolean</c> parameter slot (unboxed <c>double</c>/<c>bool</c>) to
    /// <c>object</c> when the type checker flagged it as possibly holding the runtime <c>undefined</c>
    /// sentinel (a body reassignment of an <c>any</c>/<c>undefined</c> value, e.g.
    /// <c>function p(x: number) { x = undefined as any; }</c>). The unboxed slot cannot carry the
    /// sentinel — it would coerce to <c>NaN</c>/<c>false</c> (or raw garbage for a never-stored slot).
    /// A no-op for any other type, so it is safe to apply at every parameter mapping site.
    /// </summary>
    private static Type WidenIfUndefinedReachableParam(Type mapped, Stmt.Parameter param, TypeMap? typeMap)
    {
        if ((mapped == typeof(double) || mapped == typeof(bool)) &&
            typeMap != null && typeMap.IsUndefinedReachableNumericParam(param))
            return typeof(object);
        return mapped;
    }

    /// <summary>
    /// Adjusts the strict CLR slot type for a parameter so it can hold the value's actual runtime
    /// representation, falling back to <c>object</c> when the strict slot would be unsound. Mirrors
    /// the equivalent fallbacks already applied to return slots in <see cref="ResolveReturnType"/>;
    /// the asymmetry (return slots widened, parameter slots not) was the root cause of #568/#573.
    /// </summary>
    /// <remarks>
    /// Three families need an object slot:
    /// <list type="bullet">
    /// <item>Date/RegExp/array/Map/Set — strictly mapped to DateTime/Regex/List/Dictionary/HashSet,
    /// but their runtime values are $TSDate/$RegExp/$Array/$Map/$Set carried as object; a typed slot
    /// fails ILVerify (StackUnexpected) and a castclass throws at the call/use site. (#573)</item>
    /// <item>A union admitting <c>undefined</c> (e.g. <c>string | undefined</c>): the runtime
    /// $Undefined sentinel is not a CLR instance of the non-nullish member's slot, so storing/reading
    /// it throws InvalidCastException. A value-type union additionally maps to <c>Nullable&lt;T&gt;</c>,
    /// which the emitter has no store path for. Locals already use object here; parameters must
    /// too. (#568)</item>
    /// <item><c>symbol</c> — strictly mapped to String, but a runtime symbol is a $Symbol reference,
    /// so a String slot can't hold it. (#573 scope)</item>
    /// </list>
    /// Primitives, plain strings, and class instances keep their sound strict slot.
    /// </remarks>
    private static Type CoerceParamSlotType(Type mapped, TSTypeInfo source, TypeMapper typeMapper)
    {
        if (typeMapper.IsDynamicRuntimeType(mapped))
            return typeof(object);
        if (Nullable.GetUnderlyingType(mapped) != null)
            return typeof(object);
        if (source is TSTypeInfo.Symbol)
            return typeof(object);
        if (UnionAdmitsUndefined(source))
            return typeof(object);
        if (IsPromiseTaskSlot(mapped))
            return typeof(object);
        return mapped;
    }

    /// <summary>
    /// True when <paramref name="mapped"/> is a CLR <c>Task</c>/<c>Task&lt;T&gt;</c> produced by
    /// strictly mapping a <c>Promise&lt;T&gt;</c> annotation. A parameter so typed receives the runtime
    /// <c>$Promise</c> object at the call boundary, never a real CLR <c>Task</c> (the promise
    /// primitives return <c>$Promise</c>, and no path constructs a bare <c>Task</c> guest value), so
    /// the slot must widen to <c>object</c>. Mirrors the identical <c>Promise&lt;T&gt;</c> → object
    /// widening already applied to non-async return slots in <see cref="ResolveReturnType"/> (#393);
    /// its absence on parameters left the <c>fs</c> facade callback helpers emitting a <c>$Promise</c>
    /// into a <c>Task&lt;object&gt;</c> slot, which the JIT tolerates but IL verification rejects
    /// (#1246). The widening is unconditional — even an async function's <c>Promise&lt;T&gt;</c> param
    /// receives a passed-in <c>$Promise</c>; only the enclosing function's <i>return</i> builds a real
    /// Task.
    /// </summary>
    private static bool IsPromiseTaskSlot(Type mapped) =>
        mapped == typeof(Task) ||
        (mapped.IsGenericType && mapped.GetGenericTypeDefinition() == typeof(Task<>));

    /// <summary>
    /// True when <paramref name="type"/> is a union one of whose members is the <c>undefined</c>
    /// type, so its runtime values include the $Undefined sentinel.
    /// </summary>
    private static bool UnionAdmitsUndefined(TSTypeInfo type) =>
        type is TSTypeInfo.Union u && u.FlattenedTypes.Any(t => t is TSTypeInfo.Undefined);

    /// <summary>
    /// Resolves a single parameter's type from its annotation or defaults to object.
    /// </summary>
    private static Type ResolveParameterType(Stmt.Parameter param, TypeMapper typeMapper)
    {
        if (param.Type == null)
            return typeof(object);

        // Parse the type annotation and map to .NET type
        var typeInfo = ParseTypeAnnotation(param.Type);
        return CoerceParamSlotType(typeMapper.MapTypeInfoStrict(typeInfo), typeInfo, typeMapper);
    }

    /// <summary>
    /// Resolves the return type for a function.
    /// </summary>
    /// <param name="returnTypeInfo">Return type from TypeMap (may be null)</param>
    /// <param name="isAsync">Whether this is an async function</param>
    /// <param name="typeMapper">TypeMapper for conversion</param>
    /// <param name="returnMayBeUndefined">
    /// True when the type checker found a return value of static type <c>any</c>/<c>unknown</c>
    /// flowing into this <c>number</c>/<c>boolean</c> return (e.g. <c>return undefined as any</c>).
    /// An unboxed <c>double</c>/<c>bool</c> slot cannot carry the runtime <c>undefined</c> sentinel,
    /// so it is widened to <c>object</c> for just those functions to avoid silently coercing
    /// <c>undefined</c> to <c>NaN</c>/<c>false</c>. (#344)
    /// </param>
    /// <returns>.NET type for the return value</returns>
    public static Type ResolveReturnType(
        TSTypeInfo? returnTypeInfo,
        bool isAsync,
        TypeMapper typeMapper,
        bool returnMayBeUndefined = false)
    {
        Type baseType;

        if (returnTypeInfo == null)
        {
            baseType = typeof(object);
        }
        else
        {
            baseType = typeMapper.MapTypeInfoStrict(returnTypeInfo);

            // A `number`/`boolean` return reached by an `undefined`-admitting value (any/unknown)
            // must use an object slot — the unboxed double/bool slot would coerce the runtime
            // `undefined` sentinel to NaN/false. Only the genuinely-undefined-reachable functions
            // pay this; the common sound numeric/boolean return keeps its unboxed slot. (#344)
            if (returnMayBeUndefined && (baseType == typeof(double) || baseType == typeof(bool)))
            {
                baseType = typeof(object);
            }

            // BigInteger returns need to stay as object because BigInt operations
            // in the emitter expect boxed values
            if (baseType == typeof(System.Numerics.BigInteger))
            {
                baseType = typeof(object);
            }

            // Function types return $TSFunction objects, not Delegate, so use object
            if (baseType == typeof(Delegate) || baseType.IsSubclassOf(typeof(Delegate)))
            {
                baseType = typeof(object);
            }

            // String return slots are unsound. TS inference admits `undefined` in a
            // `string`-typed expression (e.g. `cond ? "x" : undefined` infers `string`;
            // an explicit `: string` annotation is rejected for `return undefined`, so
            // inference is the reachable path), but a .NET `string` slot cannot carry the
            // `$Undefined` sentinel. A castclass at the return site throws
            // InvalidCastException at runtime, and isinst/coercion corrupts `undefined`
            // into null/"undefined" (observable through Map keys, typeof, ===). Unlike
            // `double`/`bool` slots there is no boxing to avoid — strings are reference
            // types — so a `string` slot buys nothing. Fall back to object. (#318)
            if (baseType == typeof(string))
            {
                baseType = typeof(object);
            }

            // Nullable value types (like number | null -> double?) need to stay as object
            // because the emitter doesn't have special handling for Nullable<T>
            if (Nullable.GetUnderlyingType(baseType) != null)
            {
                baseType = typeof(object);
            }

            // Discriminated union structs (Union_*) don't work as method return types
            // because MethodInfo.Invoke boxes them opaquely — IsTruthy, equality comparers,
            // and property access can't see the underlying value. Fall back to object so
            // the raw primitives/strings are returned directly.
            if (baseType.IsValueType && baseType.Name.StartsWith("Union_"))
            {
                baseType = typeof(object);
            }

            // Typed array/map/set returns map to List<T>/Dictionary<,>/HashSet<> and Date/RegExp
            // returns map to DateTime/Regex, but their runtime representation is a dynamic
            // $Array/$Map/$Set/$TSDate/$RegExp carried as object — not CLR-assignable to the
            // declared slot. Returning that value into the typed slot raises ILVerify
            // StackUnexpected (and a castclass would throw InvalidCastException at runtime). Fall
            // back to object so the dynamic value is returned directly. (#278, #573)
            if (typeMapper.IsDynamicRuntimeType(baseType))
            {
                baseType = typeof(object);
            }

            // A non-async function/arrow declared to return `Promise<T>` maps (strictly) to
            // Task<T>/Task, but its body never produces a real CLR Task — it returns the runtime
            // `$TSPromise` carried as object (e.g. the timers/promises re-export wrappers, which
            // return $Runtime.SetTimeoutPromise(...)). That object is not CLR-assignable to the
            // Task slot, so the `ret` raises ILVerify StackUnexpected; the JIT tolerates the
            // reference-type store (the program still runs), but `--verify` rejects it, and a
            // castclass at the return site would throw InvalidCastException since the value is not
            // a Task. Fall back to object so the dynamic promise is returned directly. Async
            // functions don't reach this resolver — they hardcode a Task<object> stub whose state
            // machine builds a real Task. (#393)
            if (!isAsync && (baseType == typeof(Task) ||
                (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(Task<>))))
            {
                baseType = typeof(object);
            }
        }

        // Wrap async return types in Task<T>
        if (isAsync)
        {
            if (baseType == typeof(void))
                return typeof(Task);
            return typeof(Task<>).MakeGenericType(baseType);
        }

        return baseType;
    }

    /// <summary>
    /// Resolves parameter types for a class method.
    /// </summary>
    /// <remarks>
    /// A parameter with a <b>value-type default</b> (<c>x: number = N</c>) needs an <c>object</c>
    /// slot so the entry prologue can observe the <c>$Undefined</c> sentinel and fire the default
    /// (a <c>double</c>/<c>bool</c> slot cannot — it coerces the sentinel to <c>NaN</c>/<c>false</c>).
    /// <list type="bullet">
    /// <item><b>Static methods</b> are non-virtual, so widening such a param is always safe — done
    /// directly (#705/#723).</item>
    /// <item><b>Instance methods</b> are virtual: widening one override's slot would change its CLR
    /// signature and silently break override matching (the derived method lands in a new vtable
    /// slot). So the decision is made <i>hierarchy-consistently</i> — a position is widened across
    /// the WHOLE override group when any member makes it an optional value-type parameter, keeping
    /// every override's signature identical (see <see cref="ComputeInstanceMethodWidenMask"/>).
    /// (#737)</item>
    /// </list>
    /// Reference-type defaults need no slot change (a reference slot already holds null, which the
    /// prologue treats as undefined), so they are left alone in both cases.
    /// </remarks>
    public static Type[] ResolveMethodParameters(
        string className,
        string methodName,
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        if (typeMap == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        var classType = typeMap.GetClassType(className);
        if (classType == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        TSTypeInfo.Function? funcType = null;
        bool isStatic = false;

        // Check instance methods
        if (classType.Methods.TryGetValue(methodName, out var methodType))
        {
            funcType = ExtractFunctionType(methodType);
        }
        // Check static methods
        else if (classType.StaticMethods.TryGetValue(methodName, out var staticMethodType))
        {
            funcType = ExtractFunctionType(staticMethodType);
            isStatic = true;
        }

        Type[] resolved;
        if (funcType == null || funcType.ParamTypes.Count != parameters.Count)
        {
            resolved = parameters.Select(p => WidenIfUndefinedReachableParam(ResolveParameterType(p, typeMapper), p, typeMap)).ToArray();
        }
        else
        {
            // Map each parameter type, handling optional parameters and BigInteger
            resolved = funcType.ParamTypes
                .Select((pt, i) =>
                {
                    Type mappedType;
                    try
                    {
                        mappedType = typeMapper.MapTypeInfoStrict(pt);
                    }
                    catch
                    {
                        // Union types may throw during early method definition phase
                        // when TypeBuilder isn't finalized yet. Fall back to object.
                        return typeof(object);
                    }

                    // BigInteger parameters need to stay as object because BigInt operations
                    // in the emitter expect boxed values
                    if (mappedType == typeof(System.Numerics.BigInteger))
                    {
                        return typeof(object);
                    }

                    // If parameter is optional (no explicit default), use object so undefined
                    // can be passed as the missing-argument sentinel (JS spec)
                    if (i < parameters.Count &&
                        parameters[i].DefaultValue == null &&
                        parameters[i].IsOptional)
                    {
                        return typeof(object);
                    }

                    return i < parameters.Count
                        ? CoerceParamSlotType(WidenIfUndefinedReachableParam(mappedType, parameters[i], typeMap), pt, typeMapper)
                        : CoerceParamSlotType(mappedType, pt, typeMapper);
                })
                .ToArray();
        }

        WidenValueTypeDefaultedMethodParams(resolved, parameters, className, methodName, isStatic, typeMapper, typeMap);
        return resolved;
    }

    /// <summary>
    /// Widens a method parameter slot to <c>object</c> wherever a value-type default needs to be able
    /// to hold the <c>$Undefined</c> sentinel — non-virtually for static methods, and
    /// hierarchy-consistently for (virtual) instance methods. No-op when no value-type defaults are
    /// involved, so the common method keeps its fast unboxed slots. (#705/#723/#737)
    /// </summary>
    private static void WidenValueTypeDefaultedMethodParams(
        Type[] resolved,
        List<Stmt.Parameter> parameters,
        string className,
        string methodName,
        bool isStatic,
        TypeMapper typeMapper,
        TypeMap typeMap)
    {
        if (isStatic)
        {
            // Non-virtual: widening a value-type-defaulted param can never break override matching.
            for (int i = 0; i < parameters.Count && i < resolved.Length; i++)
                if (parameters[i].DefaultValue != null && !parameters[i].IsRest && resolved[i].IsValueType)
                    resolved[i] = typeof(object);
            return;
        }

        // Virtual: widen each value-type slot the override group needs as object, so every override
        // keeps an identical CLR signature (preserving vtable dispatch).
        var mask = ComputeInstanceMethodWidenMask(className, methodName, resolved.Length, typeMapper, typeMap);
        for (int i = 0; i < resolved.Length; i++)
            if (mask[i] && resolved[i].IsValueType)
                resolved[i] = typeof(object);
    }

    /// <summary>
    /// Computes, for an instance (virtual) method, which parameter positions must use an
    /// <c>object</c> slot so a value-type default can fire via the entry prologue. The decision is
    /// <b>hierarchy-consistent</b>: a position is flagged when ANY member of the method's override
    /// group (its root declaration plus every class that overrides it) makes that position an
    /// optional value-type parameter. Flagging the whole group keeps every override's CLR signature
    /// identical, so a derived override that adds a default still lands in the base's vtable slot
    /// and a base-typed call dispatches to it correctly. (#737)
    /// </summary>
    private static bool[] ComputeInstanceMethodWidenMask(
        string className, string methodName, int paramCount, TypeMapper typeMapper, TypeMap typeMap)
    {
        var mask = new bool[paramCount];
        if (paramCount == 0)
            return mask;

        var root = FindMethodRootDeclarer(className, methodName, typeMap);
        if (root == null)
            return mask;

        // Union the optional-value-type positions over every class whose method shares this root
        // declaration (i.e. the override group). A class only declares the method if it is a key in
        // its OWN Methods map (lookup walks the chain, but Methods holds own-declared entries only).
        foreach (var (otherName, otherClass) in typeMap.ClassTypes)
        {
            if (!otherClass.Methods.TryGetValue(methodName, out var otherMethodType))
                continue;
            if (FindMethodRootDeclarer(otherName, methodName, typeMap) != root)
                continue;
            if (ExtractFunctionType(otherMethodType) is not { } f)
                continue;

            int minArity = f.MinArity;
            int count = Math.Min(paramCount, f.ParamTypes.Count);
            for (int i = 0; i < count; i++)
            {
                // Positions below the member's arity are required there — a required param is always
                // supplied, so it never needs an undefined-capable slot on this member's behalf.
                if (i < minArity)
                    continue;
                if (IsValueTypeParamSlot(typeMapper, f.ParamTypes[i]))
                    mask[i] = true;
            }
        }

        return mask;
    }

    /// <summary>
    /// Returns the name of the topmost class in <paramref name="className"/>'s ancestry (inclusive)
    /// that declares <paramref name="methodName"/> — the class that owns the method's vtable slot.
    /// Two classes share an override group iff they have the same root declarer. Returns null if the
    /// method is not declared anywhere in the chain.
    /// </summary>
    private static string? FindMethodRootDeclarer(string className, string methodName, TypeMap typeMap)
    {
        var cls = typeMap.GetClassType(className);
        if (cls == null)
            return null;

        string? root = cls.Methods.ContainsKey(methodName) ? className : null;
        var visited = new HashSet<string>(StringComparer.Ordinal) { className };
        string? superName = GetSuperclassName(cls);
        while (superName != null && visited.Add(superName))
        {
            var super = typeMap.GetClassType(superName);
            if (super == null)
                break;
            if (super.Methods.ContainsKey(methodName))
                root = superName;
            superName = GetSuperclassName(super);
        }
        return root;
    }

    /// <summary>
    /// Extracts the (simple) name of a class's direct superclass, unwrapping an instantiated generic
    /// base (<c>extends Box&lt;number&gt;</c>) to its generic definition's name. Returns null when the
    /// class has no superclass.
    /// </summary>
    private static string? GetSuperclassName(TSTypeInfo.Class cls) => cls.Superclass switch
    {
        TSTypeInfo.Class c => c.Name,
        TSTypeInfo.MutableClass mc => mc.Name,
        TSTypeInfo.GenericClass gc => gc.Name,
        TSTypeInfo.InstantiatedGeneric ig => ig.GenericDefinition switch
        {
            TSTypeInfo.Class c => c.Name,
            TSTypeInfo.MutableClass mc => mc.Name,
            TSTypeInfo.GenericClass gc => gc.Name,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// True when a parameter's TS type maps to an unboxed CLR value-type slot (e.g. <c>number</c> →
    /// <c>double</c>, <c>boolean</c> → <c>bool</c>) — the slots that cannot hold the <c>$Undefined</c>
    /// sentinel and so must be widened to <c>object</c> when defaulted. Maps defensively (a mapping
    /// can throw during early definition) and treats failures as non-value-type (do not widen).
    /// </summary>
    private static bool IsValueTypeParamSlot(TypeMapper typeMapper, TSTypeInfo paramType)
    {
        try
        {
            return typeMapper.MapTypeInfoStrict(paramType).IsValueType;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves return type for a class method.
    /// </summary>
    public static Type ResolveMethodReturnType(
        string className,
        string methodName,
        bool isAsync,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        if (typeMap == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        var classType = typeMap.GetClassType(className);
        if (classType == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        TSTypeInfo.Function? funcType = null;

        // Check instance methods
        if (classType.Methods.TryGetValue(methodName, out var methodType))
        {
            funcType = ExtractFunctionType(methodType);
        }
        // Check static methods
        else if (classType.StaticMethods.TryGetValue(methodName, out var staticMethodType))
        {
            funcType = ExtractFunctionType(staticMethodType);
        }

        if (funcType == null)
            return isAsync ? typeof(Task<object>) : typeof(object);

        return ResolveReturnType(funcType.ReturnType, isAsync, typeMapper);
    }

    /// <summary>
    /// Resolves constructor parameter types for a class, widening value-type-defaulted params to
    /// <c>object</c> so the entry prologue can fire their defaults (#705).
    /// </summary>
    public static Type[] ResolveConstructorParameters(
        string className,
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        var resolved = ResolveConstructorParametersCore(className, parameters, typeMapper, typeMap);
        WidenDefaultedParamsToObject(resolved, parameters, typeof(object));
        return resolved;
    }

    private static Type[] ResolveConstructorParametersCore(
        string className,
        List<Stmt.Parameter> parameters,
        TypeMapper typeMapper,
        TypeMap? typeMap)
    {
        if (typeMap == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        var classType = typeMap.GetClassType(className);
        if (classType == null)
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        if (!classType.Methods.TryGetValue("constructor", out var ctorTypeInfo))
            return parameters.Select(p => ResolveParameterType(p, typeMapper)).ToArray();

        var funcType = ExtractFunctionType(ctorTypeInfo);
        if (funcType == null || funcType.ParamTypes.Count != parameters.Count)
            return parameters.Select(p => WidenIfUndefinedReachableParam(ResolveParameterType(p, typeMapper), p, typeMap)).ToArray();

        // Map each parameter type, handling optional parameters and BigInteger
        return funcType.ParamTypes
            .Select((pt, i) =>
            {
                Type mappedType;
                try
                {
                    mappedType = typeMapper.MapTypeInfoStrict(pt);
                }
                catch (NotSupportedException)
                {
                    // Union types may throw during early definition phase
                    return typeof(object);
                }

                // BigInteger parameters need to stay as object
                if (mappedType == typeof(System.Numerics.BigInteger))
                {
                    return typeof(object);
                }

                // Optional parameters without defaults should use object for null-checking
                if (i < parameters.Count &&
                    parameters[i].DefaultValue == null &&
                    parameters[i].IsOptional)
                {
                    if (mappedType.IsValueType)
                    {
                        return typeof(object);
                    }
                }

                return i < parameters.Count
                    ? CoerceParamSlotType(WidenIfUndefinedReachableParam(mappedType, parameters[i], typeMap), pt, typeMapper)
                    : CoerceParamSlotType(mappedType, pt, typeMapper);
            })
            .ToArray();
    }

    /// <summary>
    /// Extracts parameter types from a method TypeInfo.
    /// </summary>
    private static Type[] ExtractParameterTypes(TSTypeInfo methodType, int paramCount, TypeMapper typeMapper)
    {
        var funcType = ExtractFunctionType(methodType);
        if (funcType == null || funcType.ParamTypes.Count != paramCount)
            return Enumerable.Repeat(typeof(object), paramCount).ToArray();

        return funcType.ParamTypes
            .Select(pt => typeMapper.MapTypeInfoStrict(pt))
            .ToArray();
    }

    /// <summary>
    /// Extracts Function type from a method TypeInfo (handles overloads).
    /// </summary>
    private static TSTypeInfo.Function? ExtractFunctionType(TSTypeInfo methodType)
    {
        return methodType switch
        {
            TSTypeInfo.Function f => f,
            TSTypeInfo.OverloadedFunction of => of.Implementation,
            _ => null
        };
    }

    /// <summary>
    /// Parses a type annotation string into TypeInfo.
    /// Delegates to centralized PrimitiveTypeMappings for consistency.
    /// </summary>
    private static TSTypeInfo ParseTypeAnnotation(string typeAnnotation) =>
        PrimitiveTypeMappings.ParseAnnotation(typeAnnotation);
}
