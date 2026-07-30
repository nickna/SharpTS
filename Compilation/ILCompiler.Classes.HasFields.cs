using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    /// <summary>
    /// Defines $IHasFields interface method stubs for user-defined classes.
    /// Bodies are emitted later via EmitHasFieldsInterfaceMethodBodies after method definitions are available.
    /// </summary>
    private void DefineHasFieldsInterfaceMethods(TypeBuilder typeBuilder, string className, FieldInfo fieldsField)
    {
        var methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;

        // get_Fields: Dictionary<string, object?> Fields { get; }
        var fieldsGetter = typeBuilder.DefineMethod(
            "get_Fields",
            methodAttrs | MethodAttributes.SpecialName,
            _types.DictionaryStringObject,
            Type.EmptyTypes
        );

        var fieldsProp = typeBuilder.DefineProperty(
            "Fields",
            PropertyAttributes.None,
            _types.DictionaryStringObject,
            null
        );
        fieldsProp.SetGetMethod(fieldsGetter);

        // GetProperty(string name) -> object?
        var getPropertyMethod = typeBuilder.DefineMethod(
            "GetProperty",
            methodAttrs,
            _types.Object,
            [_types.String]
        );

        // SetProperty(string name, object? value) -> void
        var setPropertyMethod = typeBuilder.DefineMethod(
            "SetProperty",
            methodAttrs,
            _types.Void,
            [_types.String, _types.Object]
        );

        // HasProperty(string name) -> bool
        var hasPropertyMethod = typeBuilder.DefineMethod(
            "HasProperty",
            methodAttrs,
            _types.Boolean,
            [_types.String]
        );

        // Map methods to interface slots
        typeBuilder.DefineMethodOverride(fieldsGetter, _runtime.IHasFieldsFieldsGetter);
        typeBuilder.DefineMethodOverride(getPropertyMethod, _runtime.IHasFieldsGetProperty);
        typeBuilder.DefineMethodOverride(setPropertyMethod, _runtime.IHasFieldsSetProperty);
        typeBuilder.DefineMethodOverride(hasPropertyMethod, _runtime.IHasFieldsHasProperty);

        // Store stubs for later body emission
        _classes.HasFieldsStubs[className] = new HasFieldsMethodStubs
        {
            GetFields = fieldsGetter,
            GetProperty = getPropertyMethod,
            SetProperty = setPropertyMethod,
            HasProperty = hasPropertyMethod,
            FieldsField = fieldsField
        };
    }

    /// <summary>
    /// Emits the bodies for $IHasFields interface methods.
    /// Called after DefineClassMethodsOnly so that MethodBuilders are available.
    /// Uses compile-time dispatch (no runtime reflection).
    /// </summary>
    private void EmitHasFieldsInterfaceMethodBodies(string className, Stmt.Class classStmt)
    {
        if (!_classes.HasFieldsStubs.TryGetValue(className, out var stubs))
            return;

        var fieldsField = stubs.FieldsField;

        // Emit get_Fields body: returns a dictionary combining typed backing fields + dynamic _fields
        EmitGetFieldsBody(stubs.GetFields, className, fieldsField);

        // Emit GetProperty body with compile-time dispatch
        EmitGetPropertyBody(stubs.GetProperty, className, classStmt, fieldsField);

        // Emit SetProperty body with compile-time dispatch for typed properties
        EmitSetPropertyBody(stubs.SetProperty, className, classStmt, fieldsField);

        // Emit HasProperty body with compile-time dispatch for typed backing fields
        EmitHasPropertyBody(stubs.HasProperty, className, fieldsField);
    }

    /// <summary>
    /// Emits the get_Fields body that returns a combined dictionary of typed backing fields + dynamic _fields.
    /// Typed properties stored in backing fields are not in the _fields dictionary, so we must include them.
    /// </summary>
    private void EmitGetFieldsBody(MethodBuilder method, string className, FieldInfo fieldsField)
    {
        _typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields);
        EmitGetFieldsBodyCore(method, backingFields, fieldsField);
    }

    private void EmitGetFieldsBodyCore(MethodBuilder method, Dictionary<string, FieldBuilder>? backingFields, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();

        // Check if this class has typed backing fields
        if (backingFields == null || backingFields.Count == 0)
        {
            // No typed backing fields - just return _fields directly
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fieldsField);
            il.Emit(OpCodes.Ret);
            return;
        }

        // Create a new dictionary from _fields (copy constructor), then add backing fields
        var iDictType = _types.MakeGenericType(typeof(IDictionary<,>), typeof(string), typeof(object));
        var copyCtor = _types.DictionaryStringObject.GetConstructor([iDictType])!;
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item");

        // var result = new Dictionary<string, object?>(this._fields);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Newobj, copyCtor);

        // result["propName"] = this._BackingField; for each typed property
        foreach (var (propName, backingField) in backingFields)
        {
            var camelName = NamingConventions.ToCamelCase(propName);
            il.Emit(OpCodes.Dup); // keep dict on stack
            il.Emit(OpCodes.Ldstr, camelName);
            il.Emit(OpCodes.Ldarg_0); // this
            il.Emit(OpCodes.Ldfld, backingField);
            if (backingField.FieldType.IsValueType)
                il.Emit(OpCodes.Box, backingField.FieldType);
            il.Emit(OpCodes.Callvirt, setItem);
        }

        // return result;
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits HasProperty body with compile-time dispatch for typed backing fields.
    /// Checks typed property names first, then falls back to _fields.ContainsKey.
    /// </summary>
    private void EmitHasPropertyBody(MethodBuilder method, string className, FieldInfo fieldsField)
    {
        _typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields);
        EmitHasPropertyBodyCore(method, backingFields, fieldsField);
    }

    private void EmitHasPropertyBodyCore(MethodBuilder method, Dictionary<string, FieldBuilder>? backingFields, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        var returnTrueLabel = il.DefineLabel();

        // Check typed backing field names
        if (backingFields != null)
        {
            foreach (var (propName, _) in backingFields)
            {
                var camelName = NamingConventions.ToCamelCase(propName);

                // if (name == "camelName") return true;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brtrue, returnTrueLabel);
            }
        }

        // Fall back to _fields.ContainsKey(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the GetProperty method body with compile-time dispatch.
    /// No runtime reflection - directly checks property/method names and calls getters or wraps methods.
    /// </summary>
    /// <summary>
    /// Checks whether a class (directly or transitively) extends a built-in Error type.
    /// Uses the flag set during DefineClass — no string matching needed.
    /// </summary>
    private bool IsErrorSubclass(string qualifiedClassName)
    {
        return _classes.ErrorSubclasses.Contains(qualifiedClassName);
    }

    /// <summary>
    /// Emits a fallback check for an Error base-class property in the GetProperty method body.
    /// Uses the runtime helper (e.g. ErrorGetName) which safely handles the isinst check.
    /// </summary>
    private void EmitErrorPropertyFallback(ILGenerator il, string propName, MethodBuilder runtimeGetter)
    {
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipLabel);
        // Call the runtime helper: ErrorGetName(object obj) -> string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtimeGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits the <c>instance.constructor</c> branch for a class's GetProperty body:
    /// when the requested name is "constructor", returns the class itself,
    /// represented — like a bare class identifier in value position
    /// (see <see cref="ExpressionEmitterBase"/>'s class-token path) — as the
    /// class's <see cref="System.Type"/>. This makes <c>x.constructor === MyClass</c>
    /// and <c>x.constructor.staticMember</c> behave as in the interpreter (where an
    /// instance's <c>constructor</c> resolves to its class). Without it, compiled
    /// <c>instance.constructor</c> silently returned <c>undefined</c>; benign while
    /// member reads were lenient, but #701 (a read on <c>undefined</c> now throws)
    /// surfaced it (e.g. the <c>yaml</c> package reads <c>coll.constructor.tagName</c>).
    /// Emitted after the own-field lookups so an own data property named
    /// "constructor" still shadows it, matching JS.
    /// </summary>
    private void EmitConstructorPropertyBranch(ILGenerator il, Type classType)
    {
        var notCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notCtorLabel);
        il.Emit(OpCodes.Ldtoken, classType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notCtorLabel);
    }

    private void EmitGetPropertyBody(MethodBuilder method, string className, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var returnValueLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var tryMethodsLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();

        // 1. Check typed properties with backing fields (compile-time dispatch)
        if (_typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields))
        {
            foreach (var (propName, backingField) in backingFields)
            {
                // Convert PascalCase property name to camelCase for JS-style access
                var camelName = NamingConventions.ToCamelCase(propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") return this.backingField;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, backingField);
                // Box value types
                if (backingField.FieldType.IsValueType)
                    il.Emit(OpCodes.Box, backingField.FieldType);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 2. Check _fields dictionary
        il.MarkLabel(tryFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryMethodsLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // 2b. If this class extends Error, fall back to Error base class properties
        il.MarkLabel(tryMethodsLabel);
        if (IsErrorSubclass(className))
        {
            EmitErrorPropertyFallback(il, "name", _runtime.ErrorGetName);
            EmitErrorPropertyFallback(il, "message", _runtime.ErrorGetMessage);
            EmitErrorPropertyFallback(il, "stack", _runtime.ErrorGetStack);
        }

        // 2d. `instance.constructor` → the class (a System.Type value). See
        // EmitConstructorPropertyBranch; after own fields so they can shadow it.
        EmitConstructorPropertyBranch(il, _classes.Builders[className]);

        // 2c. Check instance getters (accessors with `get` keyword). JS semantics: reading
        // `obj.foo` where `foo` is a getter must INVOKE the getter and return its result.
        // Without this, dynamic property access (via Runtime.GetProperty, used in generator
        // bodies and anywhere else the receiver type isn't statically known) would fall
        // through to the method-wrapper path and return a $TSFunction instead of the value.
        if (_classes.InstanceGetters.TryGetValue(className, out var instanceGetters))
        {
            foreach (var (getterPascalName, getterMethod) in instanceGetters)
            {
                // Getters are registered by PascalCase method-name convention (no "get_" prefix
                // in InstanceGetters dict — the dict value keys match PascalCase property names).
                // JS access is camelCase; accept both forms to match InstanceGetters' key style.
                var camelName = NamingConventions.ToCamelCase(getterPascalName);
                var nextLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                // return this.<getter>()
                // (instantiated form for generic classes — open MethodDef tokens are unloadable, #178)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.SelfMethodReference(getterMethod));
                // GetProperty's return slot is object. A typed getter (e.g. `number` → double)
                // returns a value type, and a generic-class getter returns a type parameter, so box
                // the result to match the slot. Without this the verifier reports StackUnexpected —
                // a value-type result reaching an object ret slot — even though it runs (the typed
                // getter for a parameter property is reachable via the dynamic-dispatch path). (#279)
                if (getterMethod.ReturnType.IsValueType || getterMethod.ReturnType.IsGenericParameter)
                    il.Emit(OpCodes.Box, getterMethod.ReturnType);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 3. Check instance methods (compile-time dispatch)
        // (redefine label after error property fallback — previous tryMethodsLabel was already marked)
        if (_classes.InstanceMethods.TryGetValue(className, out var instanceMethods))
        {
            // Check if this is a generic type - generic types require runtime reflection
            // because ldtoken gives us an open generic type's method which can't be invoked
            var isGenericType = classStmt.TypeParams?.Count > 0;

            // Get the TypeBuilder for the two-token ldtoken pattern
            var typeBuilder = _classes.Builders[className];
            var getMethodFromHandleWithType = _types.MethodBaseGetMethodFromHandleWithType;

            foreach (var (methodName, methodBuilder) in instanceMethods)
            {
                // Skip constructor
                if (methodName == "constructor")
                    continue;

                // #791: synthetic computed-method bodies ($symmethod_N) are not user-dispatchable
                // by their internal name — they resolve via the registry fallback below.
                if (methodName.StartsWith("$symmethod_", System.StringComparison.Ordinal))
                    continue;

                var nextLabel = il.DefineLabel();

                // if (name == "methodName") return new $TSFunction(this, methodBuilder);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, methodName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                // Load 'this' for target
                il.Emit(OpCodes.Ldarg_0);

                if (isGenericType)
                {
                    // For generic types, use runtime reflection to get the method from the closed type
                    // this.GetType().GetMethod("methodName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
                    il.Emit(OpCodes.Ldstr, methodBuilder.Name);
                    il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
                }
                else
                {
                    // For non-generic types, use two-token ldtoken pattern for dynamically emitted types
                    il.Emit(OpCodes.Ldtoken, methodBuilder);
                    il.Emit(OpCodes.Ldtoken, typeBuilder);
                    il.Emit(OpCodes.Call, getMethodFromHandleWithType);
                    il.Emit(OpCodes.Castclass, _types.MethodInfo);
                }
                il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 3b. #791: non-symbol (string/number) computed method keys. Their bodies are emitted as
        // $symmethod_N and registered in the symbol-method registry under the property-key string
        // (RegisterSymbolMethod), but named/string-index access (obj.dyn, obj["dyn"]) funnels here
        // rather than through the symbol-key branch, so consult the registry as a fallback. Gated on
        // this class declaring a computed method to keep the common no-computed-method path untouched.
        // FindSymbolMethod walks the receiver's base chain, so an inherited key still resolves once
        // base-fallthrough reaches the declaring class's GetProperty.
        if (classStmt.Methods.Any(m => m.ComputedKey != null && m.Body != null))
        {
            var noStrMethodLabel = il.DefineLabel();
            var strMethodLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _runtime.FindSymbolMethod);
            il.Emit(OpCodes.Stloc, strMethodLocal);
            il.Emit(OpCodes.Ldloc, strMethodLocal);
            il.Emit(OpCodes.Brfalse, noStrMethodLabel);
            // return new $TSFunction(this, (MethodInfo)mi)  — receiver-bound, mirroring the symbol path.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, strMethodLocal);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noStrMethodLabel);
        }

        // 4. Not found on this class — delegate to the base class's GetProperty
        // so inherited members resolve under dynamic dispatch (else fall back to
        // returning $Undefined).
        il.MarkLabel(returnNullLabel);
        EmitGetPropertyBaseFallthrough(il, ResolveBaseStubClassName(classStmt), _classes.Builders[className].BaseType);
    }

    /// <summary>
    /// Emits the SetProperty method body with compile-time dispatch.
    /// Handles typed properties with backing fields first, then falls back to _fields dictionary.
    /// </summary>
    private void EmitSetPropertyBody(MethodBuilder method, string className, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        var setFieldsLabel = il.DefineLabel();

        // 1. Check typed properties with backing fields (compile-time dispatch)
        _typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields);
        if (backingFields != null)
        {
            foreach (var (propName, backingField) in backingFields)
            {
                // Convert PascalCase property name to camelCase for JS-style access
                var camelName = NamingConventions.ToCamelCase(propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") { this.backingField = value; return; }
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                // Unbox value types
                if (backingField.FieldType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, backingField.FieldType);
                else if (backingField.FieldType != _types.Object)
                    il.Emit(OpCodes.Castclass, backingField.FieldType);
                il.Emit(OpCodes.Stfld, backingField);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 1b. Check instance setters (accessors with `set` keyword). JS semantics: writing
        // `obj.foo` where `foo` is a setter must INVOKE it — mirrors the getter dispatch in
        // EmitGetPropertyBody. Without this, dynamic property writes (the main path for
        // generic class instances) would store into _fields while reads keep hitting the
        // accessor, silently desynchronizing the two. Backing-field names are handled above.
        if (_classes.InstanceSetters.TryGetValue(className, out var instanceSetters))
        {
            foreach (var (setterPascalName, setterMethod) in instanceSetters)
            {
                if (backingFields != null && backingFields.ContainsKey(setterPascalName))
                    continue;

                var camelName = NamingConventions.ToCamelCase(setterPascalName);
                var nextLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                // this.set_<Name>(value)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                var setterParams = setterMethod.GetParameters();
                var paramType = setterParams.Length > 0 ? setterParams[0].ParameterType : _types.Object;
                if (paramType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, paramType);
                else if (paramType != _types.Object)
                    il.Emit(OpCodes.Castclass, paramType);
                // (instantiated form for generic classes — open MethodDef tokens are unloadable, #178)
                il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.SelfMethodReference(setterMethod));
                if (setterMethod.ReturnType != _types.Void)
                    il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 1c. Getter-only accessors: assigning to a property that has a `get` but no `set` is a
        // no-op in sloppy mode (matches the interpreter). Without this the write falls through to
        // _fields and that entry then shadows the getter on subsequent reads (#293).
        _classes.InstanceGetters.TryGetValue(className, out var instanceGetters);
        EmitGetterOnlyNoOpBranches(il, instanceGetters, instanceSettersForNoOp: _classes.InstanceSetters.GetValueOrDefault(className), backingFields);

        // 2. Fall back to _fields dictionary
        il.MarkLabel(setFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a no-op (return) branch in a SetProperty body for each getter-only accessor — a
    /// property registered as a getter that has neither a matching setter nor a same-named
    /// backing field. Per JS <c>[[Set]]</c>, simple assignment to a property whose prototype
    /// chain exposes an accessor without a setter never creates an own data property; in sloppy
    /// mode the write is silently ignored. Emitting the no-op here keeps GetProperty's accessor
    /// dispatch authoritative, since no shadowing <c>_fields</c> entry is ever created (#293).
    /// </summary>
    private void EmitGetterOnlyNoOpBranches(
        ILGenerator il,
        Dictionary<string, MethodBuilder>? getters,
        Dictionary<string, MethodBuilder>? instanceSettersForNoOp,
        Dictionary<string, FieldBuilder>? backingFields)
    {
        if (getters == null)
            return;

        foreach (var getterPascalName in getters.Keys)
        {
            // A getter WITH a setter is handled by the setter section above; a getter that shadows
            // a backing field is handled by the field section. Only a true getter-only property
            // reaches the _fields fallback and needs the no-op.
            if (instanceSettersForNoOp != null && instanceSettersForNoOp.ContainsKey(getterPascalName))
                continue;
            if (backingFields != null && backingFields.ContainsKey(getterPascalName))
                continue;

            var camelName = NamingConventions.ToCamelCase(getterPascalName);
            var nextLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, camelName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, nextLabel);

            // Assignment to a getter-only accessor is a no-op (sloppy mode).
            il.Emit(OpCodes.Ret);

            il.MarkLabel(nextLabel);
        }
    }

    /// <summary>
    /// Emits the $IHasFields interface method bodies for a class expression.
    /// This overload uses _classExprs dictionaries instead of _classes dictionaries.
    /// </summary>
    private void EmitHasFieldsInterfaceMethodBodies(string className, Expr.ClassExpr classExpr)
    {
        if (!_classes.HasFieldsStubs.TryGetValue(className, out var stubs))
            return;

        var fieldsField = stubs.FieldsField;

        // Emit get_Fields body: returns a dictionary combining typed backing fields + dynamic _fields
        _classExprs.BackingFields.TryGetValue(classExpr, out var backingFields);
        EmitGetFieldsBodyCore(stubs.GetFields, backingFields, fieldsField);

        // Emit GetProperty body with compile-time dispatch
        EmitGetPropertyBodyForClassExpr(stubs.GetProperty, className, classExpr, fieldsField);

        // Emit SetProperty body with compile-time dispatch
        EmitSetPropertyBodyForClassExpr(stubs.SetProperty, className, classExpr, fieldsField);

        // Emit HasProperty body with compile-time dispatch for typed backing fields
        EmitHasPropertyBodyCore(stubs.HasProperty, backingFields, fieldsField);
    }

    /// <summary>
    /// Emits the GetProperty method body for a class expression with compile-time dispatch.
    /// Uses _classExprs.InstanceMethods for method lookup.
    /// </summary>
    private void EmitGetPropertyBodyForClassExpr(MethodBuilder method, string className, Expr.ClassExpr classExpr, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var returnValueLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var tryMethodsLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();

        // 1. Check typed properties with backing fields (compile-time dispatch)
        if (_classExprs.BackingFields.TryGetValue(classExpr, out var backingFields))
        {
            foreach (var (propName, backingField) in backingFields)
            {
                // Convert PascalCase property name to camelCase for JS-style access
                var camelName = NamingConventions.ToCamelCase(propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") return this.backingField;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, backingField);
                // Box value types
                if (backingField.FieldType.IsValueType)
                    il.Emit(OpCodes.Box, backingField.FieldType);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 2. Check _fields dictionary
        il.MarkLabel(tryFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryMethodsLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // 2c. Check instance getters (accessors with `get` keyword). Mirrors section 2c in
        // EmitGetPropertyBody for class declarations: reading `obj.foo` where `foo` is a getter
        // must INVOKE the getter and return its result. Without this, dynamic property access on
        // a class-expression instance falls through to the method-wrapper path (or returns
        // $Undefined for a getter with no same-named method) instead of the getter's value (#283).
        // _classExprs.Getters mixes backing-field property getters with user `get` accessors, but
        // backing-field names are already handled (and returned) by section 1, so those entries
        // become dead branches here — matching the class-declaration registry's behavior.
        il.MarkLabel(tryMethodsLabel);

        // 2d. `instance.constructor` → the class (a System.Type value). See
        // EmitConstructorPropertyBranch; after own fields so they can shadow it.
        EmitConstructorPropertyBranch(il, _classExprs.Builders[classExpr]);

        if (_classExprs.Getters.TryGetValue(classExpr, out var instanceGetters))
        {
            foreach (var (getterPascalName, getterMethod) in instanceGetters)
            {
                var camelName = NamingConventions.ToCamelCase(getterPascalName);
                var nextLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                // return this.<getter>()
                // (instantiated form for generic classes — open MethodDef tokens are unloadable, #178)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.SelfMethodReference(getterMethod));
                // GetProperty's return slot is object; box value-type / generic-parameter results
                // to satisfy the verifier (#279).
                if (getterMethod.ReturnType.IsValueType || getterMethod.ReturnType.IsGenericParameter)
                    il.Emit(OpCodes.Box, getterMethod.ReturnType);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 3. Check instance methods (compile-time dispatch)
        if (_classExprs.InstanceMethods.TryGetValue(classExpr, out var instanceMethods))
        {
            // Check if this is a generic type - generic types require runtime reflection
            // because ldtoken gives us an open generic type's method which can't be invoked
            var isGenericType = classExpr.TypeParams?.Count > 0;

            // Get the TypeBuilder for the two-token ldtoken pattern
            var typeBuilder = _classExprs.Builders[classExpr];
            var getMethodFromHandleWithType = _types.MethodBaseGetMethodFromHandleWithType;

            foreach (var (methodName, methodBuilder) in instanceMethods)
            {
                // Skip constructor
                if (methodName == "constructor")
                    continue;

                // #791: synthetic computed-method bodies ($symmethod_N) are not user-dispatchable
                // by their internal name — they resolve via the registry fallback below.
                if (methodName.StartsWith("$symmethod_", System.StringComparison.Ordinal))
                    continue;

                var nextLabel = il.DefineLabel();

                // if (name == "methodName") return new $TSFunction(this, methodBuilder);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, methodName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                // Load 'this' for target
                il.Emit(OpCodes.Ldarg_0);

                if (isGenericType)
                {
                    // For generic types, use runtime reflection to get the method from the closed type
                    // this.GetType().GetMethod("methodName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
                    il.Emit(OpCodes.Ldstr, methodBuilder.Name);
                    il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
                }
                else
                {
                    // For non-generic types, use two-token ldtoken pattern for dynamically emitted types
                    il.Emit(OpCodes.Ldtoken, methodBuilder);
                    il.Emit(OpCodes.Ldtoken, typeBuilder);
                    il.Emit(OpCodes.Call, getMethodFromHandleWithType);
                    il.Emit(OpCodes.Castclass, _types.MethodInfo);
                }
                il.Emit(OpCodes.Newobj, _runtime.TSFunctionCtor);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 4. Not found on this class — delegate to the base class's GetProperty
        // so inherited members resolve under dynamic dispatch (else fall back to
        // returning $Undefined).
        il.MarkLabel(returnNullLabel);
        EmitGetPropertyBaseFallthrough(il, ResolveBaseStubClassName(classExpr), _classExprs.Builders[classExpr].BaseType);
    }

    /// <summary>
    /// Emits the GetProperty inheritance fallthrough: when a class's own
    /// compile-time dispatch finds nothing, delegate to the base class's
    /// GetProperty so members inherited from a base class (methods, getters,
    /// typed properties) resolve under DYNAMIC dispatch. Without this, an
    /// inherited member accessed dynamically — e.g. `(x as any).inherited()`,
    /// or ANY method call on a class-expression instance (always anonymously
    /// typed, so always dynamically dispatched) — resolves to undefined and the
    /// call throws "undefined is not a function". Falls back to returning
    /// $Undefined when the base is System.Object or a built-in (no emitted
    /// $IHasFields stub).
    /// </summary>
    private void EmitGetPropertyBaseFallthrough(ILGenerator il, string? baseClassName, Type? baseType)
    {
        if (baseClassName != null && _classes.HasFieldsStubs.TryGetValue(baseClassName, out var baseStubs))
        {
            // return base.GetProperty(name);  — non-virtual `call` to the
            // specific base implementation (not callvirt, which would recurse
            // into this class's own override). The chain terminates at the
            // topmost emitted class whose base has no stub.
            MethodInfo target = baseStubs.GetProperty;
            if (baseType != null && baseType.IsGenericType && baseType.IsConstructedGenericType)
                target = EmitterTypeHelpers.ResolveMethod(baseType, baseStubs.GetProperty);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            return;
        }

        il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Resolves the emitted base class name (a $IHasFields stub key) for a class
    /// declaration's GetProperty inheritance fallthrough, or null when the base
    /// is System.Object / a built-in without a stub.
    /// </summary>
    private string? ResolveBaseStubClassName(Stmt.Class classStmt)
    {
        if (classStmt.SuperclassExpr == null)
            return null;
        var leaf = Expr.GetSuperclassLeafName(classStmt.SuperclassExpr);
        if (leaf == null)
            return null;
        var resolved = GetDefinitionContext().ResolveClassName(leaf);
        return _classes.HasFieldsStubs.ContainsKey(resolved) ? resolved : null;
    }

    /// <summary>
    /// Resolves the emitted base class name (a $IHasFields stub key) for a class
    /// expression's GetProperty inheritance fallthrough. The superclass may be
    /// another class expression bound to a variable or a class declaration.
    /// </summary>
    private string? ResolveBaseStubClassName(Expr.ClassExpr classExpr)
    {
        if (!_classExprs.Superclass.TryGetValue(classExpr, out var superName) || superName == null)
            return null;

        string? baseClassName;
        if (_classExprs.VarToClassExpr.TryGetValue(superName, out var parentExpr)
            && _classExprs.Names.TryGetValue(parentExpr, out var generatedName))
            baseClassName = generatedName;
        else
            baseClassName = GetDefinitionContext().ResolveClassName(superName);

        return baseClassName != null && _classes.HasFieldsStubs.ContainsKey(baseClassName)
            ? baseClassName
            : null;
    }

    /// <summary>
    /// Emits the SetProperty method body for a class expression with compile-time dispatch.
    /// Handles typed properties with backing fields first, then falls back to _fields dictionary.
    /// </summary>
    private void EmitSetPropertyBodyForClassExpr(MethodBuilder method, string className, Expr.ClassExpr classExpr, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        var setFieldsLabel = il.DefineLabel();

        // 1. Check typed properties with backing fields (compile-time dispatch)
        if (_classExprs.BackingFields.TryGetValue(classExpr, out var backingFields))
        {
            foreach (var (propName, backingField) in backingFields)
            {
                // Convert PascalCase property name to camelCase for JS-style access
                var camelName = NamingConventions.ToCamelCase(propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") { this.backingField = value; return; }
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                // Unbox value types
                if (backingField.FieldType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, backingField.FieldType);
                else if (backingField.FieldType != _types.Object)
                    il.Emit(OpCodes.Castclass, backingField.FieldType);
                il.Emit(OpCodes.Stfld, backingField);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 1b. Check instance setters (accessors with `set` keyword). Mirrors section 1b in
        // EmitSetPropertyBody for class declarations: writing `obj.foo` where `foo` is a setter
        // must INVOKE it rather than storing into _fields, which would silently desynchronize
        // reads (hitting the getter) from writes (hitting the dict) (#283). _classExprs.Setters
        // mixes backing-field property setters with user `set` accessors; backing-field names are
        // already handled (and returned) by section 1, so skip them here to avoid dead branches.
        if (_classExprs.Setters.TryGetValue(classExpr, out var instanceSetters))
        {
            foreach (var (setterPascalName, setterMethod) in instanceSetters)
            {
                if (backingFields != null && backingFields.ContainsKey(setterPascalName))
                    continue;

                var camelName = NamingConventions.ToCamelCase(setterPascalName);
                var nextLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                // this.set_<Name>(value)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                var setterParams = setterMethod.GetParameters();
                var paramType = setterParams.Length > 0 ? setterParams[0].ParameterType : _types.Object;
                if (paramType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, paramType);
                else if (paramType != _types.Object)
                    il.Emit(OpCodes.Castclass, paramType);
                // (instantiated form for generic classes — open MethodDef tokens are unloadable, #178)
                il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.SelfMethodReference(setterMethod));
                if (setterMethod.ReturnType != _types.Void)
                    il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 1c. Getter-only accessors are no-ops on write (sloppy mode) — mirrors section 1c in
        // EmitSetPropertyBody. Without this the write shadows the getter via _fields (#293).
        _classExprs.Getters.TryGetValue(classExpr, out var classExprGetters);
        EmitGetterOnlyNoOpBranches(il, classExprGetters, _classExprs.Setters.GetValueOrDefault(classExpr), backingFields);

        // 2. Fall back to _fields dictionary
        il.MarkLabel(setFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fieldsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Ret);
    }
}
