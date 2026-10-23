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
        GetClassRegistry().RegisterGuestFieldOwner(className, typeBuilder);
        var methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;

        // The expando dictionary is deliberately absent from a newly constructed instance.
        // JavaScript cannot observe the backing store itself, so allocate it only when a
        // dynamic own property is actually created or the compatibility Fields surface is
        // requested. The weak side table keeps the common typed-only object layout compact.
        // Keeping this helper on the emitted type also makes base-constructor virtual dispatch
        // safe: an override can materialize its own store before the derived constructor has
        // resumed after super().
        var ensureFields = typeBuilder.DefineMethod(
            "$EnsureFields",
            MethodAttributes.Private | MethodAttributes.HideBySig,
            _types.DictionaryStringObject,
            Type.EmptyTypes
        );
        EmitEnsureFieldsBody(ensureFields, fieldsField);

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
        typeBuilder.DefineMethodOverride(fieldsGetter, _runtime.ObjectFields.FieldsGetter);
        typeBuilder.DefineMethodOverride(getPropertyMethod, _runtime.ObjectFields.GetProperty);
        typeBuilder.DefineMethodOverride(setPropertyMethod, _runtime.ObjectFields.SetProperty);
        typeBuilder.DefineMethodOverride(hasPropertyMethod, _runtime.ObjectFields.HasProperty);

        _classes.PropertyDispatch.Declare(className, typeBuilder, new ClassPropertyDispatch(
            ensureFields, fieldsGetter, getPropertyMethod, setPropertyMethod,
            hasPropertyMethod, fieldsField));
        EmitGuestDataFieldBodies(typeBuilder, fieldsField, ensureFields);
    }

    private void EmitEnsureFieldsBody(MethodBuilder method, FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        fieldsField = EmitterTypeHelpers.SelfFieldReference(fieldsField);

        if (fieldsField.IsStatic)
        {
            // Compact layout: return _fields.GetOrCreateValue(this);
            il.Emit(OpCodes.Ldsfld, fieldsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                fieldsField.FieldType, "GetOrCreateValue", [_types.Object])!);
        }
        else
        {
            // General layout: return _fields ??= new Dictionary<string, object?>();
            var readyLabel = il.DefineLabel();
            var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fieldsField);
            il.Emit(OpCodes.Stloc, fieldsLocal);
            il.Emit(OpCodes.Ldloc, fieldsLocal);
            il.Emit(OpCodes.Brtrue_S, readyLabel);
            il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            il.Emit(OpCodes.Stloc, fieldsLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, fieldsLocal);
            il.Emit(OpCodes.Stfld, fieldsField);
            il.MarkLabel(readyLabel);
            il.Emit(OpCodes.Ldloc, fieldsLocal);
        }
        il.Emit(OpCodes.Ret);
    }

    private void EmitLoadDynamicFieldsOrBranch(
        ILGenerator il,
        FieldInfo fieldsField,
        LocalBuilder fieldsLocal,
        Label missingLabel)
    {
        fieldsField = EmitterTypeHelpers.SelfFieldReference(fieldsField);
        if (fieldsField.IsStatic)
        {
            il.Emit(OpCodes.Ldsfld, fieldsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, fieldsLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                fieldsField.FieldType,
                "TryGetValue",
                [_types.Object, _types.DictionaryStringObject.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, missingLabel);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, fieldsField);
            il.Emit(OpCodes.Stloc, fieldsLocal);
            il.Emit(OpCodes.Ldloc, fieldsLocal);
            il.Emit(OpCodes.Brfalse, missingLabel);
        }
    }

    /// <summary>
    /// Emits the bodies for $IHasFields interface methods.
    /// Called after DefineClassMethodsOnly so that MethodBuilders are available.
    /// Uses compile-time dispatch (no runtime reflection).
    /// </summary>
    private void EmitHasFieldsInterfaceMethodBodies(string className, Stmt.Class classStmt)
    {
        var stubs = _classes.PropertyDispatch.Require(className);

        var fieldsField = stubs.FieldsField;

        // Emit get_Fields body: returns a dictionary combining typed backing fields + dynamic _fields
        EmitGetFieldsBody(stubs.GetFields, className, stubs.EnsureFields);

        // Emit GetProperty body with compile-time dispatch
        EmitGetPropertyBody(stubs.GetProperty, className, classStmt, fieldsField);

        // Emit SetProperty body with compile-time dispatch for typed properties
        EmitSetPropertyBody(stubs.SetProperty, className, classStmt, stubs.EnsureFields);

        // Emit HasProperty body with compile-time dispatch for typed backing fields
        EmitHasPropertyBody(stubs.HasProperty, className, fieldsField);
        _classes.PropertyDispatch.MarkBodiesEmitted(className);
    }

    /// <summary>
    /// Emits the get_Fields body that returns a combined dictionary of typed backing fields + dynamic _fields.
    /// Typed properties stored in backing fields are not in the _fields dictionary, so we must include them.
    /// </summary>
    private void EmitGetFieldsBody(
        MethodBuilder method,
        string className,
        MethodInfo ensureFields)
    {
        _typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields);
        EmitGetFieldsBodyCore(method, backingFields, ensureFields);
    }

    private void EmitGetFieldsBodyCore(
        MethodBuilder method,
        Dictionary<string, FieldBuilder>? backingFields,
        MethodInfo ensureFields)
    {
        var il = method.GetILGenerator();
        var owner = (TypeBuilder)method.DeclaringType!;
        var instanceFields = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.IsPrototype);
        il.Emit(OpCodes.Brfalse, instanceFields);
        // Prototype carriers have never initialized the class's instance data.
        // Their compatibility view contains only explicitly stored own values.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(instanceFields);
        var ownFields = GetClassRegistry().GetOwnGuestDataFields(owner).ToArray();
        var parent = owner.BaseType;
        var parentDefinition = parent?.IsConstructedGenericType == true ? parent.GetGenericTypeDefinition() : parent;
        ClassPropertyDispatch? parentDispatch = null;
        if (parentDefinition is TypeBuilder parentBuilder)
            _classes.PropertyDispatch.TryGet(parentBuilder, out parentDispatch);
        if ((backingFields == null || backingFields.Count == 0) && ownFields.Length == 0 && parentDispatch == null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
            il.Emit(OpCodes.Ret);
            return;
        }
        var fields = il.DeclareLocal(_types.DictionaryStringObject);
        var result = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, result);
        if (parentDispatch != null)
        {
            if (!EmitterTypeHelpers.TryResolveInstanceDispatch(owner, parentDispatch.GetFields, owner, out _, out var parentGet))
                throw new InvalidOperationException("Cannot resolve inherited field snapshot owner.");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, parentGet);
            il.Emit(OpCodes.Stloc, fields);
            // Auto backing slots retain their existing owner-local compatibility
            // projection; they are prototype accessors, not inherited own data.
            HashSet<string> autoNames = new(StringComparer.Ordinal);
            HashSet<string> dataNames = new(StringComparer.Ordinal);
            for (Type? ancestor = parent; ancestor != null; ancestor = ancestor.BaseType)
            {
                var definition = ancestor.IsConstructedGenericType ? ancestor.GetGenericTypeDefinition() : ancestor;
                if (definition is not TypeBuilder builder) break;
                if (_autoAccessorFieldNames.TryGetValue(builder, out var names)) autoNames.UnionWith(names);
                dataNames.UnionWith(GetClassRegistry().GetOwnGuestDataFields(builder).Select(field => field.SourceName));
            }
            autoNames.ExceptWith(dataNames);
            EmitAppendFieldSnapshot(il, fields, result, overwrite: true, autoNames);
        }
        var setItem = _types.DictionaryStringObjectSetItem;
        foreach (var field in ownFields)
        {
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldstr, field.SourceName);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(field.GuestGetter));
            il.Emit(OpCodes.Callvirt, setItem);
        }
        foreach (var (name, backing) in backingFields ?? [])
        {
            if (ownFields.Any(field => ReferenceEquals(field.BackingField, backing))) continue;
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldstr, NamingConventions.ToCamelCase(name));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(backing));
            if (backing.FieldType.IsValueType || backing.FieldType.IsGenericParameter)
                il.Emit(OpCodes.Box, backing.FieldType);
            il.Emit(OpCodes.Callvirt, setItem);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
        il.Emit(OpCodes.Stloc, fields);
        EmitAppendFieldSnapshot(il, fields, result, overwrite: false);
        EmitInstanceFieldOrderProjection(il, owner, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    private void EmitInstanceFieldOrderProjection(ILGenerator il, TypeBuilder owner, LocalBuilder values)
    {
        var owners = new Stack<(Type Type, Expr.ClassExpr? Expression, IReadOnlyList<Stmt.Field> Fields)>();
        for (Type? type = owner; type != null; type = type.BaseType)
        {
            var definition = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
            var expression = _classExprs.Builders.FirstOrDefault(entry => ReferenceEquals(entry.Value, definition)).Key;
            var declaration = _classes.Declarations.FirstOrDefault(source =>
                !_runtimeClassDeclarations.ContainsKey(source)
                && _classes.DeclarationNames.TryGetValue(source, out var name)
                && _classes.Builders.TryGetValue(name, out var builder) && ReferenceEquals(builder, definition));
            if (expression?.Fields is { } expressionFields) owners.Push((type, expression, expressionFields));
            else if (declaration?.Fields is { } declarationFields) owners.Push((type, null, declarationFields));
        }
        var plan = owners.SelectMany(entry => entry.Fields
            .Where(field => !field.IsStatic && !field.IsPrivate && !field.IsDeclare)
            .Select(field => (entry.Type, entry.Expression, Field: field))).ToArray();
        if (!plan.Any(entry => entry.Field.ComputedKey != null)) return;

        // The complete merged values already include dynamic writes dispatched into
        // a derived owner's store. Project only existing keys, base before derived,
        // then append expandos in their unchanged dictionary order.
        var ordered = il.DeclareLocal(_types.DictionaryStringObject);
        var key = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object);
        var descriptor = il.DeclareLocal(_runtime.DescriptorStorage.DescriptorType);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, ordered);
        foreach (var entry in plan)
        {
            var next = il.DefineLabel();
            var haveValue = il.DefineLabel();
            if (entry.Field.ComputedKey != null)
            {
                if (entry.Expression is { } expression)
                {
                    var definitionField = _classExprs.DefinitionFields[expression];
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, entry.Type.IsConstructedGenericType
                        ? EmitterTypeHelpers.ResolveField(entry.Type, definitionField)
                        : EmitterTypeHelpers.SelfFieldReference(definitionField));
                    il.Emit(OpCodes.Isinst, _runtime.ClassDefinitions.Type);
                    var definitionReady = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Brtrue, definitionReady);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Br, next);
                    il.MarkLabel(definitionReady);
                    il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.Keys);
                    var keys = _classExprs.Factories[expression].Keys;
                    il.Emit(OpCodes.Ldc_I4, Enumerable.Range(0, keys.Count)
                        .First(index => ReferenceEquals(keys[index], entry.Field.ComputedKey)));
                    il.Emit(OpCodes.Ldelem_Ref);
                }
                else il.Emit(OpCodes.Ldsfld, _classes.DeferredDefinitions.RequireFieldKey(entry.Field));
                // Captured keys have already undergone ToPropertyKey. Symbols stay
                // on their existing separate enumeration path without coercion.
                il.Emit(OpCodes.Isinst, _types.String);
                il.Emit(OpCodes.Stloc, key);
                il.Emit(OpCodes.Ldloc, key);
                il.Emit(OpCodes.Brfalse, next);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, key);
                il.Emit(OpCodes.Call, _runtime.ObjectDeletion.WasClassFieldDeleted);
                il.Emit(OpCodes.Brtrue, next);
            }
            else
            {
                il.Emit(OpCodes.Ldstr, entry.Field.Name.Lexeme);
                il.Emit(OpCodes.Stloc, key);
            }
            il.Emit(OpCodes.Ldloc, values);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
                [_types.String, _types.Object.MakeByRefType()])!);
            if (entry.Field.ComputedKey != null)
            {
                il.Emit(OpCodes.Brtrue, haveValue);
                // Preserve complete merged-value precedence. Only a computed key
                // absent from that snapshot needs its own PDS data fallback.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc, key);
                il.Emit(OpCodes.Call, _runtime.DescriptorStorage.GetPropertyDescriptor);
                il.Emit(OpCodes.Stloc, descriptor);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Brfalse, next);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!);
                il.Emit(OpCodes.Brtrue, next);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!);
                il.Emit(OpCodes.Brtrue, next);
                // Hidden descriptor data stays on its existing PDS-only path.
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorEnumerable.GetGetMethod()!);
                il.Emit(OpCodes.Brfalse, next);
                il.Emit(OpCodes.Ldloc, descriptor);
                il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!);
                il.Emit(OpCodes.Stloc, value);
            }
            else il.Emit(OpCodes.Brfalse, next);
            il.MarkLabel(haveValue);
            il.Emit(OpCodes.Ldloc, ordered);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryAdd",
                [_types.String, _types.Object])!);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(next);
        }
        EmitAppendFieldSnapshot(il, values, ordered, overwrite: false);
        il.Emit(OpCodes.Ldloc, ordered);
        il.Emit(OpCodes.Stloc, values);
    }

    private void EmitAppendFieldSnapshot(ILGenerator il, LocalBuilder fields, LocalBuilder result, bool overwrite,
        IReadOnlySet<string>? excludedNames = null)
    {
        var enumeratorType = _types.DictionaryStringObjectEnumerator;
        var enumerator = il.DeclareLocal(enumeratorType);
        var entry = il.DeclareLocal(_types.KeyValuePairStringObject);
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fields);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.DictionaryStringObject, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, enumerator);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloca, enumerator);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloca, enumerator);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, entry);
        foreach (var name in excludedNames ?? new HashSet<string>())
        {
            il.Emit(OpCodes.Ldloca, entry);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, loop);
        }
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloca, entry);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, entry);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, overwrite ? _types.DictionaryStringObjectSetItem
            : _types.GetMethod(_types.DictionaryStringObject, "TryAdd", [_types.String, _types.Object])!);
        if (!overwrite) il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done);
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
        var noFieldsLabel = il.DefineLabel();
        var dynamicFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.IsPrototype);
        il.Emit(OpCodes.Brtrue, dynamicFieldsLabel);

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

        // A missing expando store is observably equivalent to an empty one. Do not
        // allocate merely to answer a property probe.
        il.MarkLabel(dynamicFieldsLabel);
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        EmitLoadDynamicFieldsOrBranch(il, fieldsField, fieldsLocal, noFieldsLabel);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noFieldsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
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
        if (propName is "name" or "message")
        {
            // Constructor-created own messages are PDS data properties and have already
            // won at the runtime read boundary. The native name and absent-message slots
            // here represent inherited defaults, so an intervening prototype descriptor
            // must win even when its getter is missing and its value is undefined (#2236).
            // Walk the complete receiver chain so a further derived class cannot restore
            // the native default before an inherited source accessor is considered.
            EmitComputedInstanceGetterFallback(il);
        }
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
        EmitCanonicalClassConstructorValue(il, classType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notCtorLabel);
    }

    private void EmitGetPropertyBody(
        MethodBuilder method,
        string className,
        Stmt.Class classStmt,
        FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        EmitClassPrototypeReadBranch(il);
        var valueLocal = il.DeclareLocal(_types.Object);
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var returnValueLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var tryMethodsLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();
        EmitDictionaryGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: false);

        // 1. Check typed properties with backing fields (compile-time dispatch)
        if (_typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields))
        {
            foreach (var (propName, backingField) in backingFields)
            {
                // Preserve the exact JavaScript spelling. The backing-field map is
                // keyed by PascalCase for .NET interop, but an already-uppercase JS
                // name (for example `B = class {}`) must not become `b` on dynamic
                // access when type checking is disabled.
                var jsName = classStmt.Fields
                    .FirstOrDefault(f => !f.IsStatic && !f.IsPrivate && !f.IsDeclare
                        && NamingConventions.ToPascalCase(f.Name.Lexeme) == propName)
                    ?.Name.Lexeme ?? NamingConventions.ToCamelCase(propName);
                var nextLabel = il.DefineLabel();

                // if (name == "jsName") return this.backingField;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, jsName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                if (GetClassRegistry().TryGetOwnGuestDataField((TypeBuilder)method.DeclaringType!, jsName, out var guestField))
                    il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(guestField.GuestGetter));
                else
                {
                il.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(backingField));
                // GetProperty returns object even when the backing slot is T.
                if (backingField.FieldType.IsValueType || backingField.FieldType.IsGenericParameter)
                    il.Emit(OpCodes.Box, backingField.FieldType);
                }
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 2. Check _fields dictionary
        il.MarkLabel(tryFieldsLabel);
        EmitLoadDynamicFieldsOrBranch(il, fieldsField, fieldsLocal, tryMethodsLabel);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryMethodsLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(tryMethodsLabel);
        EmitInheritedGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: false);
        EmitClassPrototypeFieldFallback(il);
        if (classStmt.Methods.Any(m => m.ComputedKey != null && m.Body != null))
            EmitDeclaredComputedMethodRead(il, _classes.Builders[className]);

        // 2c. Check instance getters (accessors with `get` keyword). JS semantics: reading
        // `obj.foo` where `foo` is a getter must INVOKE the getter and return its result.
        // Without this, dynamic property access (via Runtime.GetProperty, used in generator
        // bodies and anywhere else the receiver type isn't statically known) would fall
        // through to the method-wrapper path and return a $TSFunction instead of the value.
        if (_classes.InstanceGetters.TryGetValue(className, out var instanceGetters))
        {
            foreach (var (getterPascalName, getterMethod) in instanceGetters)
            {
                var guestName = GetClassRegistry().GetSourceInstanceGetterName(getterMethod)
                    ?? NamingConventions.ToCamelCase(getterPascalName);
                var nextLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, guestName);
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

        if (IsErrorSubclass(className))
        {
            EmitErrorPropertyFallback(il, "name", _runtime.Errors.GetName);
            EmitErrorPropertyFallback(il, "message", _runtime.Errors.GetMessage);
            EmitErrorPropertyFallback(il, "stack", _runtime.Errors.GetStack);
        }

        // Declared accessors also take precedence over the implicit constructor.
        EmitConstructorPropertyBranch(il, _classes.Builders[className]);

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

                if (TryEmitDeclaredInstanceMethodValue(il, typeBuilder, methodBuilder))
                {
                    il.Emit(OpCodes.Ret);
                    il.MarkLabel(nextLabel);
                    continue;
                }

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
                il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        if (classStmt.Accessors?.Any(accessor => accessor.ComputedKey != null && !accessor.IsStatic) == true)
            EmitComputedInstanceGetterFallback(il);

        // Evaluated computed members may supply the other accessor half or a
        // later data method. Only claim a missing getter after those own reads.
        EmitSetterOnlyUndefinedBranches(il, classStmt.Accessors, backingFields);

        // 4. Not found on this class — delegate to the base class's GetProperty
        // so inherited members resolve under dynamic dispatch (else fall back to
        // returning $Undefined).
        il.MarkLabel(returnNullLabel);
        EmitGetPropertyBaseFallthrough(il, _classes.Builders[className].BaseType);
    }

    /// <summary>
    /// Emits the SetProperty method body with compile-time dispatch.
    /// Handles typed properties with backing fields first, then falls back to _fields dictionary.
    /// </summary>
    private void EmitSetPropertyBody(
        MethodBuilder method,
        string className,
        Stmt.Class classStmt,
        MethodInfo ensureFields)
    {
        var il = method.GetILGenerator();
        EmitClassPrototypeWriteBranch(il);
        var setFieldsLabel = il.DefineLabel();
        EmitDictionaryGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: true);

        // 1. Check typed properties with backing fields (compile-time dispatch)
        _typedInterop.PropertyBackingFields.TryGetValue(className, out var backingFields);
        if (backingFields != null)
        {
            foreach (var (propName, backingField) in backingFields)
            {
                // Convert PascalCase property name to camelCase for JS-style access
                var camelName = classStmt.Fields.FirstOrDefault(field => !field.IsStatic && !field.IsPrivate
                    && NamingConventions.ToPascalCase(field.Name.Lexeme) == propName)?.Name.Lexeme
                    ?? NamingConventions.ToCamelCase(propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") { this.backingField = value; return; }
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                if (GetClassRegistry().TryGetOwnGuestDataField((TypeBuilder)method.DeclaringType!, camelName, out var guestField))
                    il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(guestField.GuestSetter));
                else
                {
                    // Unconstrained T can represent either a value or a reference type.
                    if (backingField.FieldType.IsValueType || backingField.FieldType.IsGenericParameter)
                        il.Emit(OpCodes.Unbox_Any, backingField.FieldType);
                    else if (backingField.FieldType != _types.Object)
                        il.Emit(OpCodes.Castclass, backingField.FieldType);
                    il.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(backingField));
                }
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
                if (paramType.IsValueType || paramType.IsGenericParameter)
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
        EmitInheritedGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: true);

        // 2. Fall back to _fields dictionary
        il.MarkLabel(setFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);
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

    private void EmitSetterOnlyUndefinedBranches(
        ILGenerator il,
        IReadOnlyList<Stmt.Accessor>? accessors,
        Dictionary<string, FieldBuilder>? backingFields)
    {
        var namedAccessors = (accessors ?? []).Where(accessor => !accessor.IsStatic)
            .Select(accessor => (Accessor: accessor, Name: accessor.ComputedKey switch
            {
                null => accessor.Name.Lexeme,
                Expr.Literal { Value: string name } => name,
                _ => null,
            })).Where(entry => entry.Name != null).ToArray();
        var getterNames = namedAccessors.Where(entry => entry.Accessor.Kind.Type == TokenType.GET)
            .Select(entry => entry.Name!).ToHashSet(StringComparer.Ordinal);
        foreach (var name in namedAccessors.Where(entry => entry.Accessor.Kind.Type == TokenType.SET)
                     .Select(entry => entry.Name!).Distinct(StringComparer.Ordinal))
        {
            if (getterNames.Contains(name) || backingFields?.ContainsKey(NamingConventions.ToPascalCase(name)) == true)
                continue;
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
    }

    /// <summary>
    /// Emits the $IHasFields interface method bodies for a class expression.
    /// This overload uses _classExprs dictionaries instead of _classes dictionaries.
    /// </summary>
    private void EmitHasFieldsInterfaceMethodBodies(string className, Expr.ClassExpr classExpr)
    {
        var stubs = _classes.PropertyDispatch.Require(className);

        var fieldsField = stubs.FieldsField;

        // Emit get_Fields body: returns a dictionary combining typed backing fields + dynamic _fields
        _classExprs.BackingFields.TryGetValue(classExpr, out var backingFields);
        EmitGetFieldsBodyCore(stubs.GetFields, backingFields, stubs.EnsureFields);

        // Emit GetProperty body with compile-time dispatch
        EmitGetPropertyBodyForClassExpr(
            stubs.GetProperty, className, classExpr, fieldsField);

        // Emit SetProperty body with compile-time dispatch
        EmitSetPropertyBodyForClassExpr(
            stubs.SetProperty, className, classExpr, stubs.EnsureFields);

        // Emit HasProperty body with compile-time dispatch for typed backing fields
        EmitHasPropertyBodyCore(stubs.HasProperty, backingFields, fieldsField);
        _classes.PropertyDispatch.MarkBodiesEmitted(className);
    }

    /// <summary>
    /// Emits the GetProperty method body for a class expression with compile-time dispatch.
    /// Uses _classExprs.InstanceMethods for method lookup.
    /// </summary>
    private void EmitGetPropertyBodyForClassExpr(
        MethodBuilder method,
        string className,
        Expr.ClassExpr classExpr,
        FieldInfo fieldsField)
    {
        var il = method.GetILGenerator();
        EmitClassPrototypeReadBranch(il);
        var valueLocal = il.DeclareLocal(_types.Object);
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var returnValueLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var tryMethodsLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();
        EmitDictionaryGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: false);

        // 1. Check typed properties with backing fields (compile-time dispatch)
        if (_classExprs.BackingFields.TryGetValue(classExpr, out var backingFields))
        {
            foreach (var (propName, backingField) in backingFields)
            {
                var camelName = GetClassExpressionFieldGuestName(classExpr, propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") return this.backingField;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                if (GetClassRegistry().TryGetOwnGuestDataField((TypeBuilder)method.DeclaringType!, camelName, out var guestField))
                    il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(guestField.GuestGetter));
                else
                {
                il.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(backingField));
                // GetProperty returns object even when the backing slot is T.
                if (backingField.FieldType.IsValueType || backingField.FieldType.IsGenericParameter)
                    il.Emit(OpCodes.Box, backingField.FieldType);
                }
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        // 2. Check _fields dictionary
        il.MarkLabel(tryFieldsLabel);
        EmitLoadDynamicFieldsOrBranch(il, fieldsField, fieldsLocal, tryMethodsLabel);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
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
        EmitInheritedGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: false);

        EmitInheritedClassExpressionFieldReads(il, classExpr);
        var notLiveConstructor = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.StringOpEquality); il.Emit(OpCodes.Brfalse, notLiveConstructor);
        // Evaluated class expressions own the back-reference on their live
        // prototype. A computed method/accessor can replace it, and a missing
        // property must not restore the template CLR constructor token.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLiveConstructor);
        EmitClassPrototypeFieldFallback(il);

        if (_classExprs.Getters.TryGetValue(classExpr, out var instanceGetters))
        {
            foreach (var (getterPascalName, getterMethod) in instanceGetters)
            {
                var guestName = GetClassRegistry().GetSourceInstanceGetterName(getterMethod)
                    ?? NamingConventions.ToCamelCase(getterPascalName);
                var nextLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, guestName);
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

        if (_classes.ErrorSubclasses.Contains(className))
        {
            EmitErrorPropertyFallback(il, "name", _runtime.Errors.GetName);
            EmitErrorPropertyFallback(il, "message", _runtime.Errors.GetMessage);
            EmitErrorPropertyFallback(il, "stack", _runtime.Errors.GetStack);
        }

        EmitConstructorPropertyBranch(il, _classExprs.Builders[classExpr]);

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
                il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(nextLabel);
            }
        }

        if (classExpr.Accessors?.Any(accessor => accessor.ComputedKey != null && !accessor.IsStatic) == true)
            EmitComputedInstanceGetterFallback(il);

        // Computed non-symbol methods use synthetic CLR names and are
        // registered under their evaluated JavaScript property-key string.
        // Class declarations already consult this registry; class expressions
        // need the same fallback for `instance[key]` dispatch.
        if (classExpr.Methods.Any(m => m.ComputedKey != null && m.Body != null))
        {
            var noComputedMethodLabel = il.DefineLabel();
            var computedMethodLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _runtime.SymbolAccessors.FindMethod);
            il.Emit(OpCodes.Stloc, computedMethodLocal);
            il.Emit(OpCodes.Ldloc, computedMethodLocal);
            il.Emit(OpCodes.Brfalse, noComputedMethodLabel);
            EmitComputedMethodOwnerCheck(il, computedMethodLocal, _classExprs.Builders[classExpr], noComputedMethodLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, computedMethodLocal);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noComputedMethodLabel);
        }

        _classExprs.BackingFields.TryGetValue(classExpr, out var classExprBackingFields);
        EmitSetterOnlyUndefinedBranches(il, classExpr.Accessors, classExprBackingFields);

        // 4. Not found on this class — delegate to the base class's GetProperty
        // so inherited members resolve under dynamic dispatch (else fall back to
        // returning $Undefined).
        il.MarkLabel(returnNullLabel);
        EmitGetPropertyBaseFallthrough(il, _classExprs.Builders[classExpr].BaseType);
    }

    private void EmitInheritedClassExpressionFieldReads(ILGenerator il, Expr.ClassExpr expression)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        Type? parent = _classExprs.Builders[expression].BaseType;
        while (parent != null)
        {
            var definition = parent.IsConstructedGenericType ? parent.GetGenericTypeDefinition() : parent;
            var parentExpression = _classExprs.Builders.FirstOrDefault(entry => ReferenceEquals(entry.Value, definition)).Key;
            var declaration = _classes.Declarations.FirstOrDefault(source =>
                !_runtimeClassDeclarations.ContainsKey(source)
                && _classes.DeclarationNames.TryGetValue(source, out var name)
                && _classes.Builders.TryGetValue(name, out var builder) && ReferenceEquals(builder, definition));
            var fields = parentExpression?.Fields ?? declaration?.Fields;
            var backingFields = parentExpression != null
                ? _classExprs.BackingFields.GetValueOrDefault(parentExpression)
                : declaration != null
                    ? _typedInterop.PropertyBackingFields.GetValueOrDefault(_classes.DeclarationNames[declaration])
                    : null;
            foreach (var field in fields ?? [])
            {
                if (field.IsStatic || field.IsPrivate || field.IsDeclare || field.ComputedKey != null
                    || backingFields?.ContainsKey(NamingConventions.ToPascalCase(field.Name.Lexeme)) != true
                    || !seenNames.Add(field.Name.Lexeme)) continue;
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, next);
                // A field inherited through the CLR chain remains own data on this
                // instance. Read its declaring body's field branch before any
                // prototype compatibility view can expose an uninitialized field.
                EmitGetPropertyBaseFallthrough(il, parent);
                il.MarkLabel(next);
            }
            parent = definition.BaseType is { } nextParent
                ? ResolveInheritedConstructorSlot(nextParent, parent) : null;
        }
    }

    private static string GetClassExpressionFieldGuestName(Expr.ClassExpr expression, string propertyName) =>
        expression.Fields.FirstOrDefault(field => !field.IsStatic && !field.IsPrivate && !field.IsDeclare
            && field.ComputedKey == null && NamingConventions.ToPascalCase(field.Name.Lexeme) == propertyName)
            ?.Name.Lexeme ?? NamingConventions.ToCamelCase(propertyName);

    private void EmitComputedMethodOwnerCheck(ILGenerator il, LocalBuilder method, TypeBuilder owner, Label notOwn)
    {
        // Base GetProperty bodies still receive the original derived instance.
        // Normalize the closed method owner rather than using receiver.GetType().
        il.Emit(OpCodes.Ldloc, method);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(MemberInfo), "DeclaringType").GetGetMethod()!);
        il.Emit(OpCodes.Call, _runtime.SymbolAccessors.RegistryKey);
        il.Emit(OpCodes.Ldtoken, owner);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Call, _runtime.SymbolAccessors.RegistryKey);
        il.Emit(OpCodes.Bne_Un, notOwn);
    }

    private void EmitDeclaredComputedMethodRead(ILGenerator il, TypeBuilder owner)
    {
        // The registry records which evaluated ordinary keys belong to this
        // declaration. Their live values belong to descriptors on the prototype,
        // including undefined, replacement values, and an inherited value after
        // deletion. Check before CLR methods so computed/literal collisions use
        // the descriptor selected by source order.
        var absent = il.DefineLabel();
        var method = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.SymbolAccessors.FindMethod);
        il.Emit(OpCodes.Stloc, method);
        il.Emit(OpCodes.Ldloc, method); il.Emit(OpCodes.Brfalse, absent);
        EmitComputedMethodOwnerCheck(il, method, owner, absent);

        var prototype = il.DeclareLocal(_types.Object);
        var havePrototype = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.Get);
        il.Emit(OpCodes.Stloc, prototype);
        // A prototype can itself be read through $IHasFields after its own
        // descriptor was deleted. Start on it rather than bypassing its slots.
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Beq, havePrototype);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototype);
        il.MarkLabel(havePrototype);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, prototype); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(absent);
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
    private void EmitGetPropertyBaseFallthrough(ILGenerator il, Type? baseType)
    {
        // A guest runtime parent may not be a CLR base. Only an actual base
        // owner can receive this nonvirtual call; guest inheritance is read
        // through the prototype fallback before reaching this branch.
        var baseDefinition = baseType?.IsConstructedGenericType == true
            ? baseType.GetGenericTypeDefinition() : baseType;
        if (baseDefinition is TypeBuilder owner && _classes.PropertyDispatch.TryGet(owner, out var baseStubs))
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

        // Every ordinary class ultimately inherits Object.prototype. Return
        // its shared methods (hasOwnProperty, toString, valueOf, ...) when no
        // emitted base-class stub claimed the property.
        il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.Populate);
        il.Emit(OpCodes.Ldsfld, _runtime.ObjectPrototypes.Prototype);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ObjectRead.Property);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reads properties assigned directly to a generated class's stable
    /// prototype object (for example <c>Err.prototype.message = "custom"</c>).
    /// The prototype itself is an instance of the generated class, so guard
    /// against looking it up from itself before probing its own fields.
    /// </summary>
    private void EmitClassPrototypeFieldFallback(ILGenerator il)
    {
        var prototypeLocal = il.DeclareLocal(_types.Object);
        var prototypeFieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var prototypeValueLocal = il.DeclareLocal(_types.Object);
        var skipLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototypeLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, skipLabel);
        // An alternate allocation prototype replaces the nominal CLR method
        // chain. Own fields were checked above; an inherited miss must remain
        // a miss rather than falling back to methods from the allocated type.
        var nominalPrototype = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.DescriptorStorage.HasPrototypeEntry);
        il.Emit(OpCodes.Brfalse, nominalPrototype);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.Get);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Beq, nominalPrototype);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nominalPrototype);
        if (_features?.UsesClassPrototypeMutation != false)
        {
            var unchangedChain = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
            il.Emit(OpCodes.Call, _runtime.ClassPrototypes.HasModifiedChain);
            il.Emit(OpCodes.Brfalse, unchangedChain);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, prototypeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(unchangedChain);
        }
        // Assigned prototype properties live in PDS, independently of the
        // prototype's dynamic Fields dictionary. A present own descriptor
        // decides the read even when its value/getter result is undefined.
        // Without one, keep the existing method and ancestor fallback order.
        var noOwnPrototypeDescriptor = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Brfalse, noOwnPrototypeDescriptor);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noOwnPrototypeDescriptor);
        var noDescriptor = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _runtime.ClassDefinitions.InstanceInterface);
        il.Emit(OpCodes.Brfalse, noDescriptor);
        var inherited = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
        il.Emit(OpCodes.Stloc, inherited);
        il.Emit(OpCodes.Ldloc, inherited);
        il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, noDescriptor);
        il.Emit(OpCodes.Ldloc, inherited);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noDescriptor);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Isinst, _runtime.ObjectFields.Interface);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Castclass, _runtime.ObjectFields.Interface);
        il.Emit(OpCodes.Callvirt, _runtime.ObjectFields.FieldsGetter);
        il.Emit(OpCodes.Stloc, prototypeFieldsLocal);
        il.Emit(OpCodes.Ldloc, prototypeFieldsLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldloc, prototypeFieldsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, prototypeValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldloc, prototypeValueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits the SetProperty method body for a class expression with compile-time dispatch.
    /// Handles typed properties with backing fields first, then falls back to _fields dictionary.
    /// </summary>
    private void EmitSetPropertyBodyForClassExpr(
        MethodBuilder method,
        string className,
        Expr.ClassExpr classExpr,
        MethodInfo ensureFields)
    {
        var il = method.GetILGenerator();
        EmitClassPrototypeWriteBranch(il);
        var setFieldsLabel = il.DefineLabel();
        EmitDictionaryGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: true);

        // 1. Check typed properties with backing fields (compile-time dispatch)
        if (_classExprs.BackingFields.TryGetValue(classExpr, out var backingFields))
        {
            foreach (var (propName, backingField) in backingFields)
            {
                var camelName = GetClassExpressionFieldGuestName(classExpr, propName);
                var nextLabel = il.DefineLabel();

                // if (name == "camelName") { this.backingField = value; return; }
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, camelName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, nextLabel);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_2);
                if (GetClassRegistry().TryGetOwnGuestDataField((TypeBuilder)method.DeclaringType!, camelName, out var guestField))
                    il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(guestField.GuestSetter));
                else
                {
                // Unbox value types
                if (backingField.FieldType.IsValueType)
                    il.Emit(OpCodes.Unbox_Any, backingField.FieldType);
                else if (backingField.FieldType != _types.Object)
                    il.Emit(OpCodes.Castclass, backingField.FieldType);
                il.Emit(OpCodes.Stfld, backingField);
                }
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
        EmitInheritedGuestFieldBranches(il, (TypeBuilder)method.DeclaringType!, write: true);

        // 2. Fall back to _fields dictionary
        il.MarkLabel(setFieldsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(ensureFields));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Ret);
    }
    private void EmitClassPrototypeReadBranch(ILGenerator il)
    {
        var instance = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.IsPrototype);
        il.Emit(OpCodes.Brfalse, instance);
        // Read descriptors/dynamic storage from the prototype itself. Falling
        // through to typed getters would expose uninitialized instance slots.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadPrototypeProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(instance);
    }

    private void EmitClassPrototypeWriteBranch(ILGenerator il)
    {
        var instance = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ClassPrototypes.IsPrototype);
        il.Emit(OpCodes.Brfalse, instance);
        // Existing descriptors are handled by the shared setter before this
        // dispatch. New prototype properties need presence independent of CLR
        // default values, including explicit undefined/null, and normal deletion.
        var storage = _runtime.DescriptorStorage;
        var descriptor = il.DeclareLocal(storage.DescriptorType);
        il.Emit(OpCodes.Newobj, storage.DescriptorConstructor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, storage.DescriptorValue.GetSetMethod()!);
        foreach (var property in new[] { storage.DescriptorWritable, storage.DescriptorEnumerable,
                     storage.DescriptorConfigurable })
        {
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Call, storage.DefineProperty);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(instance);
    }

    private void EmitComputedInstanceGetterFallback(ILGenerator il)
    {
        // Computed declaration accessors are installed as live prototype
        // descriptors. Read that state before the legacy method registry so
        // replacement, setter-only accessors, and data values stay authoritative.
        var prototype = il.DeclareLocal(_types.Object);
        var descriptor = il.DeclareLocal(_runtime.DescriptorStorage.DescriptorType);
        var nextPrototype = il.DefineLabel();
        var descriptorLoop = il.DefineLabel();
        var noDescriptor = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototype);
        il.MarkLabel(descriptorLoop);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Brfalse, noDescriptor);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Brfalse, nextPrototype);
        var descriptorGetter = il.DeclareLocal(_types.Object);
        var dataDescriptor = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, descriptorGetter);
        il.Emit(OpCodes.Ldloc, descriptorGetter);
        il.Emit(OpCodes.Brfalse, dataDescriptor);
        var invokeDescriptorGetter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptorGetter);
        il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brfalse, invokeDescriptorGetter);
        il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(invokeDescriptorGetter);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, descriptorGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, _runtime.Invocation.Method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(dataDescriptor);
        var descriptorValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, descriptorValue);
        il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(descriptorValue);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Callvirt, _runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nextPrototype);
        il.Emit(OpCodes.Ldloc, prototype);
        il.Emit(OpCodes.Call, _runtime.ObjectPrototypes.GetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototype);
        il.Emit(OpCodes.Br, descriptorLoop);
        il.MarkLabel(noDescriptor);

        var missing = il.DefineLabel();
        var getter = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.SymbolAccessors.FindGetter);
        il.Emit(OpCodes.Stloc, getter);
        il.Emit(OpCodes.Ldloc, getter);
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldloc, getter);
        il.Emit(OpCodes.Castclass, _types.MethodBase);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(missing);
    }

}
