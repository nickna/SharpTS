using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Core property access methods for the IL emitter (get, set, index, direct dispatch, special cases).
/// Literal emission is in ILEmitter.Properties.Literals.cs.
/// External/static member access is in ILEmitter.Properties.External.cs.
/// Private class elements are in ILEmitter.Properties.Private.cs.
/// Built-in type property access is in ILEmitter.Properties.BuiltIns.cs.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitGet(Expr.Get g)
    {
        if (!g.Optional
            && g.Name.Lexeme == "length"
            && g.Object is Expr.Variable restLength
            && _ctx.FlattenedNumericRestParameter is { } flattenedLength
            && restLength.Name.Lexeme == flattenedLength.Name)
        {
            EmitDoubleConstant(flattenedLength.Length);
            SetStackType(StackType.Double);
            return;
        }

        if (!g.Optional
            && g.Name.Lexeme == "length"
            && g.Object is Expr.Variable typedArrayLength
            && _ctx.TryGetHoistedTypedArray(typedArrayLength.Name.Lexeme) is
                { Backing: { } backing })
        {
            IL.Emit(OpCodes.Ldloc, backing.LengthLocal);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        // CommonJS: `module.exports` reads → ldsfld $exports
        if (TryEmitCjsGet(g)) return;

        // Built-in module live property reads (cluster.schedulingPolicy, #1170).
        // Must precede the TypeInfo.Record fast path — the namespace object is
        // Record-typed, but these members are live runtime state, not dict entries.
        if (TryEmitBuiltInModuleLivePropertyGet(g)) return;

        // Promoted object-literal shape struct (#862): `o.KEY` reads the typed struct field directly
        // (ldloca + ldfld) — no Dictionary lookup, no string hash, no unbox. Keyed off the slot's CLR
        // type, so it is scope-correct and never misfires for a non-promoted local. The analyzer
        // guarantees KEY is one of the shape's fields. Must precede the TypeInfo.Record fast path below:
        // a promoted local is also Record-typed, but its slot is a struct, not a Dictionary.
        if (!g.Optional && g.Object is Expr.Variable shapeVarGet
            && _ctx.TryGetPromotedObjectLocal(shapeVarGet.Name.Lexeme) is { } poGet
            && poGet.Shape.FieldBuilders.TryGetValue(g.Name.Lexeme, out var fbGet))
        {
            IL.Emit(OpCodes.Ldloca, poGet.Local);
            IL.Emit(OpCodes.Ldfld, fbGet);
            SetStackTypeForFieldType(fbGet.FieldType);
            return;
        }

        // A carrier-typed, immutable parameter remains the exact generated CLR
        // record throughout the function. When flow analysis has also narrowed
        // away null, emit the recursive slot load directly: no isinst, materialized
        // guard, descriptor probe, or general GetProperty fallback is observable.
        if (TryEmitExactCompactRecordParameterGet(g))
            return;

        // Syntactic shortcut: `arguments.length` → load $Arguments._length
        // directly. The static-type-driven dispatch path emits .NET
        // List<object>.Count (via a helper that bypasses GetProperty),
        // missing the JS-visible length per ECMA-262 sloppy arguments.
        // Catches direct uses inside the function body — the most common
        // pattern in test262's "applied to Arguments object" cluster.
        if (g.Name.Lexeme == "length"
            && g.Object is Expr.Variable argsVar
            && argsVar.Name.Lexeme == "arguments"
            && _ctx.Runtime?.ArgumentsType != null
            && _ctx.Runtime?.ArgumentsLengthField != null)
        {
            EmitExpression(g.Object);
            EmitBoxIfNeeded(g.Object);
            var argsLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, argsLocal);
            var notArgsTypeLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, argsLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.ArgumentsType);
            IL.Emit(OpCodes.Brfalse, notArgsTypeLabel);
            IL.Emit(OpCodes.Ldloc, argsLocal);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.ArgumentsType);
            IL.Emit(OpCodes.Ldfld, _ctx.Runtime!.ArgumentsLengthField);
            IL.Emit(OpCodes.Conv_R8);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(notArgsTypeLabel);
            // Fallback: arg may have been overwritten by user code with a
            // non-$Arguments value (`arguments = ...`). Use GetLength.
            IL.Emit(OpCodes.Ldloc, argsLocal);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetLength);
            IL.Emit(OpCodes.Conv_R8);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            IL.MarkLabel(endLabel);
            SetStackUnknown();
            return;
        }

        // Static type property dispatch via registry (Math.PI, Number.MAX_VALUE, Symbol.iterator, etc.)
        if (g.Object is Expr.Variable staticVar && _ctx.TypeEmitterRegistry != null)
        {
            var staticStrategy = _ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
            if (staticStrategy != null && staticStrategy.TryEmitStaticPropertyGet(this, g.Name.Lexeme))
            {
                SetStackUnknown();
                return;
            }
        }

        // Special case: process.stdin.isTTY, process.stdout.isTTY, process.stderr.isTTY
        if (TryEmitProcessStreamProperty(g))
        {
            return;
        }

        // Special case: globalThis.Math.PI, globalThis.JSON.parse, etc.
        if (TryEmitGlobalThisChainedProperty(g))
        {
            return;
        }

        // Built-in module property access (path.sep, path.delimiter, os.EOL, etc.)
        if (g.Object is Expr.Variable builtInVar &&
            _ctx.BuiltInModuleNamespaces != null &&
            _ctx.BuiltInModuleNamespaces.TryGetValue(builtInVar.Name.Lexeme, out var builtInModuleName) &&
            _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(builtInModuleName) is { } builtInEmitter)
        {
            if (builtInEmitter.TryEmitPropertyGet(this, g.Name.Lexeme))
            {
                SetStackUnknown();
                return;
            }
        }

        // EventEmitter.defaultMaxListeners static property
        if (g.Object is Expr.Variable eeVar && g.Name.Lexeme == "defaultMaxListeners" &&
            _ctx.BuiltInModuleMethodBindings?.TryGetValue(eeVar.Name.Lexeme, out var eeBinding) == true &&
            eeBinding.ModuleName == "events" && eeBinding.MethodName == "EventEmitter" &&
            _ctx.Runtime?.TSEventEmitterDefaultMaxListeners != null)
        {
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.TSEventEmitterDefaultMaxListeners);
            IL.Emit(OpCodes.Conv_R8);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            SetStackUnknown();
            return;
        }

        // Enum forward mapping: Direction.Up -> 0 or Status.Success -> "SUCCESS"
        if (g.Object is Expr.Variable enumVar &&
            _ctx.EnumMembers?.TryGetValue(_ctx.ResolveEnumName(enumVar.Name.Lexeme), out var members) == true &&
            members.TryGetValue(g.Name.Lexeme, out var value))
        {
            if (value is double d)
            {
                IL.Emit(OpCodes.Ldc_R8, d);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                SetStackUnknown();
            }
            else if (value is string s)
            {
                IL.Emit(OpCodes.Ldstr, s);
                SetStackType(StackType.String);
            }
            return;
        }

        // External read of a namespace-level mutable variable must observe the live binding,
        // not the declaration-time snapshot stored in the namespace object (#623). Redirect
        // `N.x` (and nested `N.M.x`) to the var's static backing field — the same field member
        // functions read and write (#567) — so a mutation made through a member function is
        // visible here. Mirrors the interpreter's live-binding exposure.
        if (TryEmitNamespaceVarGet(g)) return;

        // Handle static member access via 'this' in static context (static blocks, static methods)
        // In static blocks, 'this' refers to the class constructor, so this.property accesses static members
        if (g.Object is Expr.This && !_ctx.IsInstanceMethod && _ctx.CurrentClassBuilder != null)
        {
            // Use cached CurrentClassName instead of linear search
            string? currentClassName = _ctx.CurrentClassName;

            if (currentClassName != null)
            {
                // Emit as static field access on the current class
                if (EmitStaticMemberAccess(currentClassName, _ctx.CurrentClassBuilder, g.Name.Lexeme))
                {
                    return;
                }
            }
        }

        if (TryEmitStaticClassMethodValue(g))
            return;

        // Handle static member access via class name
        if (g.Object is Expr.Variable classVar)
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.Classes.TryGetValue(resolvedClassName, out var classBuilder))
            {
                // Try static getter first (for auto-accessors and explicit static accessors)
                if (_ctx.ClassRegistry!.TryGetCallableStaticGetter(resolvedClassName, g.Name.Lexeme, classBuilder, out var staticGetter))
                {
                    IL.Emit(OpCodes.Call, staticGetter!);

                    // The getter returns the typed value (e.g., double for number).
                    // Track the stack type so EmitBoxIfNeeded can box only when necessary.
                    // This avoids unnecessary boxing in numeric contexts like `Counter.count + 1`.
                    string pascalPropName = NamingConventions.ToPascalCase(g.Name.Lexeme);
                    if (_ctx.PropertyTypes != null &&
                        _ctx.PropertyTypes.TryGetValue(resolvedClassName, out var propTypes) &&
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
                            // Other reference types
                            SetStackUnknown();
                        }
                    }
                    else
                    {
                        // Fallback: assume object return (legacy behavior)
                        SetStackUnknown();
                    }
                    return;
                }

                // Try to find static field using stored FieldBuilders
                // Use TryGetCallableStaticField to handle generic classes properly
                if (_ctx.ClassRegistry!.TryGetCallableStaticField(resolvedClassName, g.Name.Lexeme, classBuilder, out var callableStaticField))
                {
                    EmitStaticFieldLoadWithShadow(resolvedClassName, classBuilder, g.Name.Lexeme, callableStaticField!);
                    return;
                }

                // Static methods are handled in EmitCall, so just fall through for now
                // If we get here for a method reference (not call), we'll use the generic path
            }
        }

        // Handle static member access via imported class alias (import X = require('./module') where module exports a class)
        if (g.Object is Expr.Variable importedClassVar &&
            _ctx.ImportedClassAliases?.TryGetValue(importedClassVar.Name.Lexeme, out var importedQualifiedClassName) == true &&
            _ctx.Classes.TryGetValue(importedQualifiedClassName, out var importedClassBuilder))
        {
            // Try static getter first
            if (_ctx.ClassRegistry!.TryGetCallableStaticGetter(importedQualifiedClassName, g.Name.Lexeme, importedClassBuilder, out var importedStaticGetter))
            {
                IL.Emit(OpCodes.Call, importedStaticGetter!);
                SetStackUnknown();
                return;
            }

            // Try static field
            if (_ctx.ClassRegistry!.TryGetCallableStaticField(importedQualifiedClassName, g.Name.Lexeme, importedClassBuilder, out var importedStaticField))
            {
                EmitStaticFieldLoadWithShadow(importedQualifiedClassName, importedClassBuilder, g.Name.Lexeme, importedStaticField!);
                return;
            }
        }

        // Handle static member access via class expression variable
        if (g.Object is Expr.Variable classExprVar &&
            _ctx.VarToClassExpr != null &&
            _ctx.VarToClassExpr.TryGetValue(classExprVar.Name.Lexeme, out var classExpr) &&
            _ctx.ClassExprStaticFields != null &&
            _ctx.ClassExprStaticFields.TryGetValue(classExpr, out var exprStaticFields) &&
            exprStaticFields.TryGetValue(g.Name.Lexeme, out var exprStaticField))
        {
            IL.Emit(OpCodes.Ldsfld, exprStaticField);
            SetStackUnknown();
            return;
        }

        // Handle static property access on external .NET types (@DotNetType)
        if (g.Object is Expr.Variable extVar && _ctx.TypeMapper.ExternalTypes.TryGetValue(extVar.Name.Lexeme, out var externalType))
        {
            if (TryEmitExternalStaticPropertyGet(externalType, g.Name.Lexeme))
                return;
        }

        // Promoted typed-array local `.length` (#857): direct List<T>.Count, no GetLength/isinst.
        if (!g.Optional && g.Name.Lexeme == "length" && g.Object is Expr.Variable promVarLen
            && _ctx.TryGetPromotedArrayLocal(promVarLen.Name.Lexeme) is { } promLen)
        {
            var listType = promLen.Descriptor.GetListType(_ctx.Types);
            IL.Emit(OpCodes.Ldloc, promLen.Local);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetProperty(listType, "Count").GetGetMethod()!);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        // Promoted string-accumulator `.length` (#857): direct StringBuilder.Length. .NET StringBuilder
        // .Length is UTF-16 code units, identical to JS string .length — no materialization.
        if (!g.Optional && g.Name.Lexeme == "length" && g.Object is Expr.Variable accLenVar
            && _ctx.TryGetPromotedStringAccumulator(accLenVar.Name.Lexeme) is { } accLenSb)
        {
            IL.Emit(OpCodes.Ldloc, accLenSb);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetProperty(_ctx.Types.StringBuilder, "Length").GetGetMethod()!);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        // Promoted numeric Map `.size` (#1482): keep Dictionary.Count as an
        // unboxed TypeScript number. The registry property contract otherwise
        // resets stack tracking to object because ordinary Map properties box.
        if (!g.Optional && g.Name.Lexeme == "size" && g.Object is Expr.Variable mapSizeVar
            && _ctx.TryGetPromotedNumericMapLocal(mapSizeVar.Name.Lexeme) is { } numericMap)
        {
            IL.Emit(OpCodes.Ldloc, numericMap);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetProperty(
                _ctx.Types.DictionaryDoubleDouble, "Count").GetMethod!);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        // Try direct getter dispatch for known class instance types
        TypeInfo? objType = _ctx.TypeMap?.Get(g.Object);
        if (TryEmitDirectGetterCall(g.Object, objType, g.Name.Lexeme))
            return;

        // Type-first dispatch: Use TypeEmitterRegistry for property getters
        if (objType != null && _ctx.TypeEmitterRegistry != null)
        {
            var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitPropertyGet(this, g.Object, g.Name.Lexeme))
            {
                SetStackUnknown();
                return;
            }
        }

        // Category-based built-in type property dispatch
        if (objType != null && TryEmitBuiltInTypePropertyGet(g, objType))
            return;

        // Phase H fast path: when the receiver's static type is a plain
        // record (`{ x: T, y: U }`), the runtime value is most often a
        // bare `Dictionary<string, object>` produced by EmitObjectLiteral.
        // Bypass GetProperty's isinst chain ($TSNamespace / $Object / Map /
        // Set / Dict / $Array / List / object[] / ...) with a single
        // Isinst Dict + direct TryGetValue. Fall through to GetProperty if
        // the runtime shape doesn't match (function parameters typed as
        // record, class instances assigned to record-typed variables, etc.).
        // Skipped when optional chaining is in play — null-check semantics
        // there are non-trivial and hot-path optionals are rare.
        if (!g.Optional
            && objType is TypeInfo.Record recordType
            && _ctx.Runtime?.UndefinedInstance != null)
        {
            EmitTypedRecordPropertyGet(g, recordType);
            return;
        }

        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);

        if (g.Optional)
        {
            var builder = _ctx.ILBuilder;
            var nullishLabel = builder.DefineLabel("optional_nullish");
            var endLabel = builder.DefineLabel("optional_end");

            // Check for null
            IL.Emit(OpCodes.Dup);
            builder.Emit_Brfalse(nullishLabel);

            // Check for undefined (non-null singleton $Undefined.Instance)
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
            builder.Emit_Brtrue(nullishLabel);

            // Not nullish - proceed with property access
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
            builder.Emit_Br(endLabel);

            builder.MarkLabel(nullishLabel);
            IL.Emit(OpCodes.Pop);
            // Optional chaining returns undefined (not null) when object is nullish
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);

            builder.MarkLabel(endLabel);
        }
        else
        {
            // RequireObjectCoercible: a non-optional read on `undefined` throws a
            // guest TypeError instead of silently yielding undefined (#701). Also
            // rejects a genuine value-null (#735) now that sloppy `this` resolves to
            // the globalThis sentinel. Null-placeholder globals (e.g. `process`)
            // are exempt — uncovered properties there yield undefined, not a throw.
            if (!IsNullPlaceholderGlobal(g.Object))
                EmitThrowIfUndefinedReceiverOnStack(g.Name.Lexeme);
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
        }
    }

    /// <summary>
    /// Phase H fast path for <c>obj.x</c> on a record-typed receiver.
    /// Emits a runtime-guarded <c>Dictionary&lt;string, object&gt;.TryGetValue</c>
    /// that bypasses the long isinst chain inside <c>$Runtime.GetProperty</c>
    /// for the common case: object-literal-shaped values produced by
    /// <c>EmitObjectLiteral</c> are bare dictionaries, and the guard
    /// succeeds. On miss (function params typed as record, class
    /// instances downcast to record shape, etc.) we fall through to the
    /// existing dispatch.
    /// </summary>
    private void EmitTypedRecordPropertyGet(Expr.Get g, TypeInfo.Record recordType)
    {
        EmitExpression(g.Object);
        EmitBoxIfNeeded(g.Object);

        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        bool hasCompactCarrier =
            (_ctx.RuntimeFeatures?.UsesJSON == true ||
             _ctx.RuntimeFeatures?.UsesCompactObjectRecords == true);
        var dictLocal = IL.DeclareLocal(_ctx.Types.DictionaryStringObject);
        var outLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, receiverLocal);

        var fallbackLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var notFoundLabel = IL.DefineLabel();
        bool exactCarrierSpecialized = false;

        if (hasCompactCarrier && _ctx.ProgramType is not null &&
            JsonSerializationShapeAnalyzer.TryAnalyze(recordType, out var analyzedShape) &&
            analyzedShape is JsonSerializationShape.Record recordShape)
        {
            int scalarIndex = -1;
            for (int index = 0; index < recordShape.Fields.Count; index++)
            {
                if (recordShape.Fields[index].Key == g.Name.Lexeme)
                {
                    scalarIndex = index;
                    break;
                }
            }

            string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(recordShape);
            if (scalarIndex >= 0 &&
                _ctx.Runtime!.JsonTypedScalarRecordTypes.TryGetValue(
                    fingerprint, out var jsonExactType) &&
                _ctx.Runtime.JsonTypedScalarRecordValueFields.TryGetValue(
                    (fingerprint, scalarIndex), out var jsonExactValueField) &&
                jsonExactValueField.FieldType == _ctx.Types.Double)
            {
                // A statically-numbered slot can stay as a native double through
                // the consumer.  The non-carrier fallback applies ToNumber just
                // as the old boxed path's numeric consumer did.
                var jsonExactLocal = IL.DeclareLocal(jsonExactType);
                var numberResult = IL.DeclareLocal(_ctx.Types.Double);
                var jsonFallback = IL.DefineLabel();
                var jsonEnd = IL.DefineLabel();
                IL.Emit(OpCodes.Ldloc, receiverLocal);
                IL.Emit(OpCodes.Isinst, jsonExactType);
                IL.Emit(OpCodes.Stloc, jsonExactLocal);
                IL.Emit(OpCodes.Ldloc, jsonExactLocal);
                IL.Emit(OpCodes.Brfalse, jsonFallback);
                IL.Emit(OpCodes.Ldloc, jsonExactLocal);
                IL.Emit(OpCodes.Callvirt,
                    _ctx.Runtime.JsonScalarRecordIsMaterializedGetter);
                IL.Emit(OpCodes.Brtrue, jsonFallback);
                if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true)
                {
                    IL.Emit(OpCodes.Ldloc, jsonExactLocal);
                    IL.Emit(OpCodes.Call, _ctx.Runtime.PDSHasPropertyDescriptors);
                    IL.Emit(OpCodes.Brtrue, jsonFallback);
                }
                IL.Emit(OpCodes.Ldloc, jsonExactLocal);
                IL.Emit(OpCodes.Ldfld, jsonExactValueField);
                IL.Emit(OpCodes.Stloc, numberResult);
                IL.Emit(OpCodes.Br, jsonEnd);
                IL.MarkLabel(jsonFallback);
                IL.Emit(OpCodes.Ldloc, receiverLocal);
                IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
                IL.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);
                IL.Emit(OpCodes.Call, _ctx.Runtime.ConvertToNumber);
                IL.Emit(OpCodes.Stloc, numberResult);
                IL.MarkLabel(jsonEnd);
                IL.Emit(OpCodes.Ldloc, numberResult);
                SetStackType(StackType.Double);
                return;
            }
            else if (scalarIndex >= 0 &&
                _ctx.Runtime!.CompactObjectRecordTypes.TryGetValue(
                    fingerprint, out var exactType) &&
                _ctx.Runtime.CompactObjectRecordValueFields.TryGetValue(
                    (fingerprint, scalarIndex), out var exactValueField) &&
                _ctx.Runtime.CompactObjectRecordAnyMaterializedFields.TryGetValue(
                    fingerprint, out var anyMaterializedField))
            {
                exactCarrierSpecialized = true;
                LocalBuilder exactLocal;
                if (g.Object is Expr.Variable receiverVariable &&
                    _ctx.HoistedCompactRecordParameters.TryGetValue(
                        receiverVariable.Name.Lexeme, out var hoisted) &&
                    hoisted.Fingerprint == fingerprint)
                {
                    exactLocal = hoisted.TypedLocal;
                }
                else
                {
                    exactLocal = IL.DeclareLocal(exactType);
                    IL.Emit(OpCodes.Ldloc, receiverLocal);
                    IL.Emit(OpCodes.Isinst, exactType);
                    IL.Emit(OpCodes.Stloc, exactLocal);
                }
                IL.Emit(OpCodes.Ldloc, exactLocal);
                IL.Emit(OpCodes.Brfalse, fallbackLabel);
                if (!_ctx.RuntimeFeatures!.CanAssumeCompactObjectRecordIsUnmaterialized(
                        fingerprint))
                {
                    IL.Emit(OpCodes.Ldsfld, anyMaterializedField);
                    IL.Emit(OpCodes.Brtrue, fallbackLabel);
                }
                if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true)
                {
                    IL.Emit(OpCodes.Ldloc, exactLocal);
                    IL.Emit(OpCodes.Call, _ctx.Runtime.PDSHasPropertyDescriptors);
                    IL.Emit(OpCodes.Brtrue, fallbackLabel);
                }
                IL.Emit(OpCodes.Ldloc, exactLocal);
                IL.Emit(OpCodes.Ldfld, exactValueField);
                if (exactValueField.FieldType.IsValueType)
                    IL.Emit(OpCodes.Box, exactValueField.FieldType);
                IL.Emit(OpCodes.Br, endLabel);
            }
            else if (scalarIndex >= 0 &&
                _ctx.Runtime!.JsonScalarRecordInlineTypes.TryGetValue(
                    recordShape.Fields.Count, out var inlineType) &&
                _ctx.Runtime.JsonScalarRecordInlineGetters.TryGetValue(
                    (recordShape.Fields.Count, scalarIndex), out var directGetter))
            {
                var inlineLocal = IL.DeclareLocal(inlineType);
                var dictionaryLabel = IL.DefineLabel();
                var shapeField = Emitters.JSONStaticEmitter.GetOrDefineShapeField(
                    _ctx, recordShape);
                bool closed = JsonSerializationShapeAnalyzer.IsClosed(recordShape);
                bool carrierTypeIdentifiesShape =
                    _ctx.RuntimeFeatures!.HasUniqueCompactObjectRecordShape(
                        recordShape.Fields.Count,
                        JsonSerializationShapeAnalyzer.Fingerprint(recordShape));
                IL.Emit(OpCodes.Ldloc, receiverLocal);
                IL.Emit(OpCodes.Isinst, inlineType);
                IL.Emit(OpCodes.Stloc, inlineLocal);
                IL.Emit(OpCodes.Ldloc, inlineLocal);
                IL.Emit(OpCodes.Brfalse, dictionaryLabel);
                IL.Emit(OpCodes.Ldloc, inlineLocal);
                IL.Emit(OpCodes.Callvirt, _ctx.Runtime.JsonScalarRecordIsMaterializedGetter);
                IL.Emit(OpCodes.Brtrue, fallbackLabel);
                // A descriptor can replace an own slot with an accessor, so programs
                // that mention descriptor APIs retain the per-read PDS guard. When
                // the whole-program detector proves those APIs absent, no descriptor
                // entry can exist and the ConditionalWeakTable probe is dead weight.
                if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true)
                {
                    IL.Emit(OpCodes.Ldloc, inlineLocal);
                    IL.Emit(OpCodes.Call, _ctx.Runtime.PDSHasPropertyDescriptors);
                    IL.Emit(OpCodes.Brtrue, fallbackLabel);
                }
                // Prototype mutation cannot shadow this carrier's known own slot.
                // Deletion or any write materializes the record first and is caught
                // by IsMaterialized above, so a separate prototype-table lookup is
                // unnecessary for this exact-shape read.
                if (!carrierTypeIdentifiesShape)
                {
                    IL.Emit(OpCodes.Ldloc, inlineLocal);
                    IL.Emit(OpCodes.Callvirt, _ctx.Runtime.JsonScalarRecordShapeGetter);
                    Emitters.JSONStaticEmitter.EmitLazyShapeDescriptor(
                        _ctx, recordShape, shapeField, closed);
                    IL.Emit(OpCodes.Bne_Un, fallbackLabel);
                }
                IL.Emit(OpCodes.Ldloc, inlineLocal);
                IL.Emit(OpCodes.Call, directGetter);
                IL.Emit(OpCodes.Br, endLabel);
                IL.MarkLabel(dictionaryLabel);
            }
        }

        if (!exactCarrierSpecialized)
        {
            // dictLocal = receiver as Dictionary<string, object>
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Types.DictionaryStringObject);
            IL.Emit(OpCodes.Stloc, dictLocal);
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Brfalse, fallbackLabel);

            // dict.TryGetValue(name, out value) ? value : $Undefined
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Ldloca, outLocal);
            var tryGetValue = _ctx.Types.GetMethod(
                _ctx.Types.DictionaryStringObject,
                "TryGetValue",
                _ctx.Types.String,
                _ctx.Types.Object.MakeByRefType());
            IL.Emit(OpCodes.Callvirt, tryGetValue);
            IL.Emit(OpCodes.Brfalse, notFoundLabel);
            IL.Emit(OpCodes.Ldloc, outLocal);
            IL.Emit(OpCodes.Br, endLabel);

            IL.MarkLabel(notFoundLabel);
            // ECMA-262: missing property reads as undefined.
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
            IL.Emit(OpCodes.Br, endLabel);
        }

        IL.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);

        IL.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private bool TryEmitExactCompactRecordParameterGet(Expr.Get g)
    {
        if (g.Optional || g.Object is not Expr.Variable receiver ||
            !_ctx.HoistedCompactRecordParameters.TryGetValue(
                receiver.Name.Lexeme, out var hoisted) ||
            !hoisted.IsExact ||
            _ctx.TypeMap?.Get(g.Object) is not { } narrowedRecord ||
            _ctx.RuntimeFeatures is not { } features ||
            (!features.CanAssumeCompactObjectRecordIsUnmaterialized(
                hoisted.Fingerprint) &&
                !_ctx.HoistedCompactRecordMaterializationGuards.Contains(
                    hoisted.Fingerprint)) ||
            features.UsesDynamicPropertyDescriptors ||
            !ILCompiler.TryGetCompactRecordShape(narrowedRecord, out var recordShape) ||
            JsonSerializationShapeAnalyzer.Fingerprint(recordShape) != hoisted.Fingerprint)
            return false;

        int index = -1;
        for (int candidate = 0; candidate < recordShape.Fields.Count; candidate++)
        {
            if (recordShape.Fields[candidate].Key == g.Name.Lexeme)
            {
                index = candidate;
                break;
            }
        }
        if (index < 0 ||
            !_ctx.Runtime!.CompactObjectRecordValueFields.TryGetValue(
                (hoisted.Fingerprint, index), out var field))
            return false;

        IL.Emit(OpCodes.Ldloc, hoisted.TypedLocal);
        IL.Emit(OpCodes.Ldfld, field);
        SetStackTypeForFieldType(field.FieldType);
        return true;
    }

    /// <summary>
    /// Phase I fast path for <c>obj.x = v</c> on a record-typed receiver.
    /// Symmetric to <see cref="EmitTypedRecordPropertyGet"/>, but with
    /// extra guards for the spec-mandated semantics SetProperty handles:
    /// <list type="bullet">
    /// <item>Object.freeze: silent fail in sloppy mode.</item>
    /// <item>Object.seal: existing-property writes succeed, new-property
    /// adds silently fail.</item>
    /// <item>Object.preventExtensions: tracked via PropertyDescriptorStore;
    /// fall back to slow path which calls PDSCanAddProperty.</item>
    /// </list>
    /// We check FrozenObjects/SealedObjects directly; on hit, route to
    /// the slow path. For the non-frozen, non-sealed common case we go
    /// straight to <c>dict.set_Item</c>. Skipping the long isinst chain
    /// inside SetProperty saves ~10 ns/call on hot paths.
    ///
    /// Stack on entry: empty. Stack on exit: <c>[boxedValue]</c> — the
    /// assignment expression's result, matching the slow path.
    /// </summary>
    private void EmitTypedRecordPropertySet(Expr.Set s)
    {
        EmitExpression(s.Object);
        EmitBoxIfNeeded(s.Object);
        var receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, receiverLocal);

        EmitExpression(s.Value);
        EmitBoxIfNeeded(s.Value);
        var valueLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, valueLocal);

        var fallbackLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var ignoredLocal = IL.DeclareLocal(_ctx.Types.Object);
        var cwtTryGet = _ctx.Types.GetMethod(
            _ctx.Types.ConditionalWeakTable, "TryGetValue",
            _ctx.Types.Object, _ctx.Types.Object.MakeByRefType());

        // Bail to slow path on Object.freeze/seal — keeps spec semantics
        // intact without having to replicate the property-descriptor
        // dance here. Check FrozenObjects first; then SealedObjects.
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.FrozenObjectsField);
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Ldloca, ignoredLocal);
        IL.Emit(OpCodes.Callvirt, cwtTryGet);
        IL.Emit(OpCodes.Brtrue, fallbackLabel);

        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SealedObjectsField);
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Ldloca, ignoredLocal);
        IL.Emit(OpCodes.Callvirt, cwtTryGet);
        IL.Emit(OpCodes.Brtrue, fallbackLabel);

        // dictLocal = receiver as Dictionary<string, object>; if null,
        // not the shape we're optimized for → fall back.
        var dictLocal = IL.DeclareLocal(_ctx.Types.DictionaryStringObject);
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.DictionaryStringObject);
        IL.Emit(OpCodes.Stloc, dictLocal);
        IL.Emit(OpCodes.Ldloc, dictLocal);
        IL.Emit(OpCodes.Brfalse, fallbackLabel);

        // dict[name] = value
        IL.Emit(OpCodes.Ldloc, dictLocal);
        IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        var setItem = _ctx.Types.GetMethod(
            _ctx.Types.DictionaryStringObject, "set_Item",
            _ctx.Types.String, _ctx.Types.Object);
        IL.Emit(OpCodes.Callvirt, setItem);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(fallbackLabel);
        IL.Emit(OpCodes.Ldloc, receiverLocal);
        IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);

        IL.MarkLabel(endLabel);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// Coerces the value currently on the stack to a promoted shape struct field's CLR type (#862).
    /// The analyzer guarantees the value's static kind already matches the field, so the underlying
    /// Ensure* helper is a no-op in practice — it only fixes an unexpected boxed/widened representation.
    /// </summary>
    private void EnsureForFieldType(Type fieldType)
    {
        if (fieldType == _ctx.Types.Double) EnsureDouble();
        else if (fieldType == _ctx.Types.Boolean) EnsureBoolean();
        else EnsureString();
    }

    /// <summary>Sets the stack-type tracker to match a promoted shape struct field's CLR type (#862).</summary>
    private void SetStackTypeForFieldType(Type fieldType)
    {
        if (fieldType == _ctx.Types.Double) SetStackType(StackType.Double);
        else if (fieldType == _ctx.Types.Boolean) SetStackType(StackType.Boolean);
        else SetStackType(StackType.String);
    }

    protected override void EmitSet(Expr.Set s)
    {
        // CommonJS: `module.exports = X` writes → stsfld $exports
        if (TryEmitCjsSet(s)) return;

        // Built-in module property writes (cluster.schedulingPolicy = x, #1170).
        // Must precede the Record fast path (see EmitGet).
        if (TryEmitBuiltInModulePropertySet(s)) return;

        // Handle globalThis.x = value
        if (s.Object is Expr.Variable gtVar && gtVar.Name.Lexeme == "globalThis")
        {
            EmitExpression(s.Value);
            EnsureBoxed();
            var gtResultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, gtResultTemp);
            IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, gtResultTemp);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisSetProperty);
            IL.Emit(OpCodes.Ldloc, gtResultTemp); // expression result
            SetStackUnknown();
            return;
        }

        // Handle process.exitCode assignment
        if (s.Object is Expr.Variable processVar && processVar.Name.Lexeme == "process" && s.Name.Lexeme == "exitCode")
        {
            EmitExpression(s.Value);
            // #886: coerce to a native double only when the value isn't already one.
            // An unconditional EmitUnboxToDouble() (Convert.ToDouble(object)) fails IL
            // verification for native-double inputs (e.g. a numeric literal), which arrive
            // unboxed with _stackType == Double.
            EnsureDouble();
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Dup); // Keep value for expression result
            IL.Emit(OpCodes.Call, _ctx.Types.GetPropertySetter(_ctx.Types.Environment, "ExitCode"));
            IL.Emit(OpCodes.Conv_R8); // Convert back to double for JS number
            // #886: EmitBoxDouble boxes AND resets _stackType to Unknown. A raw
            // IL.Emit(Box) leaves _stackType == Double stale over a boxed value, so a
            // downstream consumer (e.g. the top-level await-drain wrapper) emits a second
            // box and the IL fails verification.
            EmitBoxDouble();
            return;
        }

        // Other `process.X = value` assignments route through the live
        // $Process object's SetProperty: title, the deprecation flags, and
        // arbitrary expando properties (epic #1078).
        if (s.Object is Expr.Variable processSetVar && processSetVar.Name.Lexeme == "process")
        {
            EmitExpression(s.Value);
            EmitBoxIfNeeded(s.Value);
            var processValueTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, processValueTemp);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProcessObject);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.IHasFieldsInterface);
            IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
            IL.Emit(OpCodes.Ldloc, processValueTemp);
            IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.IHasFieldsSetProperty);
            IL.Emit(OpCodes.Ldloc, processValueTemp); // expression result
            SetStackUnknown();
            return;
        }

        // Promoted object-literal shape struct (#862): `o.KEY = v` writes the typed struct field
        // directly (ldloca + stfld) — no Dictionary, no freeze/seal probe, no boxing. The expression's
        // result is the assigned value (typed). Keyed off the slot's CLR type (scope-correct). The
        // analyzer guarantees KEY ∈ shape and v's static kind matches the field. Must precede the
        // TypeInfo.Record fast path below (a promoted local is Record-typed but slot is a struct).
        if (s.Object is Expr.Variable shapeVarSet
            && _ctx.TryGetPromotedObjectLocal(shapeVarSet.Name.Lexeme) is { } poSet
            && poSet.Shape.FieldBuilders.TryGetValue(s.Name.Lexeme, out var fbSet))
        {
            EmitExpression(s.Value);
            EnsureForFieldType(fbSet.FieldType);
            var valueTemp = IL.DeclareLocal(fbSet.FieldType);
            IL.Emit(OpCodes.Stloc, valueTemp);
            IL.Emit(OpCodes.Ldloca, poSet.Local);
            IL.Emit(OpCodes.Ldloc, valueTemp);
            IL.Emit(OpCodes.Stfld, fbSet);
            IL.Emit(OpCodes.Ldloc, valueTemp); // expression result: the assigned value
            SetStackTypeForFieldType(fbSet.FieldType);
            return;
        }

        // Handle static property assignment via 'this' in static context (static blocks, static methods)
        if (s.Object is Expr.This && !_ctx.IsInstanceMethod && _ctx.CurrentClassBuilder != null)
        {
            // First check for class expressions
            if (_ctx.CurrentClassExpr != null &&
                _ctx.ClassExprStaticFields != null &&
                _ctx.ClassExprStaticFields.TryGetValue(_ctx.CurrentClassExpr, out var classExprStaticFields) &&
                classExprStaticFields.TryGetValue(s.Name.Lexeme, out var classExprStaticField))
            {
                EmitExpression(s.Value);
                EmitBoxIfNeeded(s.Value);
                IL.Emit(OpCodes.Dup); // Keep value for expression result
                IL.Emit(OpCodes.Stsfld, classExprStaticField);
                return;
            }

            // Use cached CurrentClassName instead of linear search (class declarations)
            string? currentClassName = _ctx.CurrentClassName;

            if (currentClassName != null)
            {
                // Emit as static field assignment on the current class
                if (EmitStaticMemberSet(currentClassName, _ctx.CurrentClassBuilder, s.Name.Lexeme, s.Value))
                {
                    return;
                }
            }
        }

        // Handle static property assignment via class name: delegate to EmitStaticMemberSet
        // which handles setters (auto-accessor + explicit), regular static fields, and private
        // static fields with correct signature-driven coercion + return-value handling.
        if (s.Object is Expr.Variable classVar)
        {
            string resolvedClassName = _ctx.ResolveClassName(classVar.Name.Lexeme);
            if (_ctx.Classes.TryGetValue(resolvedClassName, out var classBuilder))
            {
                if (EmitStaticMemberSet(resolvedClassName, classBuilder, s.Name.Lexeme, s.Value))
                    return;
            }
        }

        // Try direct setter dispatch for known class instance types
        TypeInfo? objType = _ctx.TypeMap?.Get(s.Object);
        if (TryEmitDirectSetterCall(s.Object, objType, s.Name.Lexeme, s.Value))
            return;

        // Type-first dispatch: Use TypeEmitterRegistry for property setters
        if (objType != null && _ctx.TypeEmitterRegistry != null)
        {
            var strategy = _ctx.TypeEmitterRegistry.GetStrategy(objType);
            if (strategy != null && strategy.TryEmitPropertySet(this, s.Object, s.Name.Lexeme, s.Value))
            {
                SetStackUnknown();
                return;
            }
        }

        // Phase I fast path: symmetric to the EmitGet typed-record fast
        // path. When the receiver's static type is `TypeInfo.Record`, the
        // runtime value is most often a bare `Dictionary<string, object>`
        // produced by EmitObjectLiteral. Bypass SetProperty's isinst
        // chain with a direct `Castclass Dictionary; set_Item` on the
        // common case, falling through to SetProperty for non-Dict
        // shapes ($Object with setters, class instances, etc.).
        // Skipped under strict mode — SetPropertyStrict surfaces a
        // TypeError for assignments to read-only properties / sealed
        // objects, which we can't replicate in IL without re-doing the
        // dispatch chain.
        if (!_ctx.IsStrictMode
            && objType is TypeInfo.Record)
        {
            EmitTypedRecordPropertySet(s);
            return;
        }

        // Build stack for SetProperty(obj, name, value) or SetPropertyStrict(obj, name, value, strictMode).
        // The LHS base is evaluated and recorded before the RHS (ECMA-262 §13.15 — the
        // reference's base is captured during LeftHandSideExpression eval), and the RHS
        // value is captured into a local so the coercibility guard below runs AFTER its
        // side effects (PutValue follows RHS evaluation).
        EmitExpression(s.Object);
        EmitBoxIfNeeded(s.Object);
        var setRecvLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, setRecvLocal);

        EmitExpression(s.Value);
        EmitBoxIfNeeded(s.Value);
        var setResultLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, setResultLocal);

        // RequireObjectCoercible (PutValue): a null/undefined base throws a guest
        // TypeError ("Cannot set properties of undefined|null (setting 'X')") instead
        // of silently no-op'ing. Compiled sloppy `this` is the globalThis sentinel, so
        // `this.x = v` in a loose function still routes to GlobalThisSetProperty. (#733)
        // Null-placeholder globals (e.g. `process`) are exempt.
        if (!IsNullPlaceholderGlobal(s.Object))
            EmitThrowIfReceiverUndefined(setRecvLocal, s.Name.Lexeme, isWrite: true);

        // Stack: [obj, name, value] - call SetProperty or SetPropertyStrict
        IL.Emit(OpCodes.Ldloc, setRecvLocal);
        IL.Emit(OpCodes.Ldstr, s.Name.Lexeme);
        IL.Emit(OpCodes.Ldloc, setResultLocal);
        if (_ctx.IsStrictMode)
        {
            IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetPropertyStrict);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetProperty);
        }

        // Put result back on stack
        IL.Emit(OpCodes.Ldloc, setResultLocal);
    }

    protected override void EmitGetIndex(Expr.GetIndex gi)
    {
        if (TryEmitFlattenedNumericRestIndex(gi))
            return;

        if (TryEmitStableMapEntryIndex(gi))
            return;

        // A literal string key on a built-in constructor/namespace is the
        // computed-property spelling of the same ordinary property access:
        // Number["MAX_VALUE"] === Number.MAX_VALUE, Math["PI"] === Math.PI,
        // Date["prototype"] === Date.prototype, and so on.  The dot form is
        // handled by the static-emitter registry near the top of EmitGet;
        // route the bracket form through that same source of truth instead of
        // evaluating the bare built-in token and hoping the generic runtime
        // Type/namespace dispatch has duplicated every static property.
        if (!gi.Optional
            && gi.Object is Expr.Variable staticIndexVar
            && gi.Index is Expr.Literal { Value: string staticIndexName }
            && _ctx.TypeEmitterRegistry?.GetStaticStrategy(staticIndexVar.Name.Lexeme) is { } staticIndexStrategy
            && staticIndexStrategy.TryEmitStaticPropertyGet(this, staticIndexName))
        {
            SetStackUnknown();
            return;
        }

        // A statically known external CLR instance can expose a real indexer. Emit its getter
        // directly so compiled output remains standalone and matches interpreter reflection.
        if (!gi.Optional &&
            TryResolveExternalReceiverType(gi.Object, out var externalIndexerType) &&
            TryEmitExternalIndexerGet(gi.Object, externalIndexerType, gi.Index))
        {
            return;
        }

        // Optional bracket access: emit nullish check around the entire index operation
        if (gi.Optional)
        {
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);

            var builder = _ctx.ILBuilder;
            var nullishLabel = builder.DefineLabel("optional_idx_nullish");
            var endLabel = builder.DefineLabel("optional_idx_end");

            IL.Emit(OpCodes.Dup);
            builder.Emit_Brfalse(nullishLabel);

            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
            builder.Emit_Brtrue(nullishLabel);

            // Not nullish — proceed with index access
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
            builder.Emit_Br(endLabel);

            builder.MarkLabel(nullishLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);

            builder.MarkLabel(endLabel);
            SetStackUnknown();
            return;
        }

        // globalThis[key] → GlobalThisGetProperty(key)
        if (gi.Object is Expr.Variable gtGetIdx && gtGetIdx.Name.Lexeme == "globalThis")
        {
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.Object, "ToString")!);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisGetProperty);
            SetStackUnknown();
            return;
        }

        // Enum reverse mapping: Direction[0] -> "Up"
        if (gi.Object is Expr.Variable enumVar &&
            _ctx.EnumReverse?.TryGetValue(_ctx.ResolveEnumName(enumVar.Name.Lexeme), out var reverse) == true)
        {
            // Check if index is a literal we can resolve at compile time
            if (gi.Index is Expr.Literal lit && lit.Value is double d && reverse.TryGetValue(d, out var memberName))
            {
                IL.Emit(OpCodes.Ldstr, memberName);
                SetStackType(StackType.String);
                return;
            }

            // Runtime lookup using cached helper
            var keys = reverse.Keys.ToArray();
            var values = reverse.Values.ToArray();
            IL.Emit(OpCodes.Ldstr, enumVar.Name.Lexeme);
            EmitExpression(gi.Index);
            EmitUnboxToDouble();
            IL.Emit(OpCodes.Ldc_I4, keys.Length);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Double);
            for (int i = 0; i < keys.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldc_R8, keys[i]);
                IL.Emit(OpCodes.Stelem_R8);
            }
            IL.Emit(OpCodes.Ldc_I4, values.Length);
            IL.Emit(OpCodes.Newarr, _ctx.Types.String);
            for (int i = 0; i < values.Length; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Ldstr, values[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetEnumMemberName);
            SetStackType(StackType.String);
            return;
        }

        // Promoted typed-array local (#857): the slot IS a List<double>/List<bool>, so read
        // directly with no isinst dispatch and no $Array indirection. An out-of-range read must
        // yield `undefined` (JS semantics) — NOT throw, which a bare get_Item would, and which would
        // regress arrays that were previously $Array-backed (e.g. boolean[]). List.get_Item also
        // can't return the undefined sentinel from a value-type slot, so the result is boxed at the
        // OOB/in-range merge (the #860 unboxed-element read is deferred — see plan B3). Even boxed,
        // this still drops the per-access isinst ladder and the $Array virtual dispatch. The
        // `(uint)i >= (uint)Count` compare folds the negative-index case into the OOB branch.
        if (!gi.Optional && gi.Object is Expr.Variable promVarGet
            && _ctx.TryGetPromotedArrayLocal(promVarGet.Name.Lexeme) is { } promGet
            && _ctx.TypeMap?.Get(gi.Index) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral)
        {
            var listType = promGet.Descriptor.GetListType(_ctx.Types);
            var oobLabel = IL.DefineLabel();
            var endLabel = IL.DefineLabel();

            EmitExpressionAsDouble(gi.Index);
            IL.Emit(OpCodes.Conv_I4);
            var idxLocal = IL.DeclareLocal(_ctx.Types.Int32);
            IL.Emit(OpCodes.Stloc, idxLocal);

            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Ldloc, promGet.Local);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetProperty(listType, "Count").GetGetMethod()!);
            IL.Emit(OpCodes.Bge_Un, oobLabel); // unsigned: i < 0 reads as huge, also branches to OOB

            // In range: box(list[i]) so this branch converges on `object` with the OOB branch.
            IL.Emit(OpCodes.Ldloc, promGet.Local);
            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(listType, "get_Item", _ctx.Types.Int32));
            IL.Emit(OpCodes.Box, promGet.Descriptor.GetElementType(_ctx.Types));
            IL.Emit(OpCodes.Br, endLabel);

            // Out of range: undefined (matches the interpreter and the $Array path).
            IL.MarkLabel(oobLabel);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);

            IL.MarkLabel(endLabel);
            SetStackUnknown();
            return;
        }

        // Numeric typed-array fast path (#3, generalizing #878 past Float64): when the receiver is
        // a variable statically typed as a numeric typed array, read the element UNBOXED via
        // $XArray.GetUnboxed → native double on the stack. Eliminates the Runtime.GetIndex dispatch,
        // the GetTypedArrayElement isinst/castclass, the virtual Get, and the per-element box. BigInt
        // and Uint8Clamped have no entry → fall through to the boxed path. Out-of-range access faults
        // exactly as the boxed path does today. Receiver is side-effect-free, so it is loaded once.
        if (!gi.Optional && gi.Object is Expr.Variable
            && _ctx.TypeMap?.Get(gi.Object) is TypeInfo.TypedArray gta
            && _ctx.Runtime!.GetTypedArrayType(gta.ElementType) is { } gtaType
            && _ctx.Runtime!.TypedArrayGetUnboxedByElement.TryGetValue(gta.ElementType, out var taGetU))
        {
            if (TryGetDirectTypedArrayBacking(gi.Object, gta.ElementType, out var backing))
            {
                EmitIndexAsInt32(gi.Index);
                var indexLocal = IL.DeclareLocal(_ctx.Types.Int32);
                IL.Emit(OpCodes.Stloc, indexLocal);
                EmitDirectTypedArrayRead(backing, indexLocal);
                SetStackType(StackType.Double);
                return;
            }

            // Receiver: hoisted loop-invariant cast when available, else per-access cast (#928).
            EmitTypedArrayReceiver(gi.Object, gtaType);
            // Native-int fast path when the index is an integer loop counter (#928).
            EmitIndexAsInt32(gi.Index);
            // Non-virtual call to the sealed-type accessor → the JIT inlines it (AggressiveInlining)
            // so the receiver's _buffer load and the element bounds check can hoist out of loops.
            IL.Emit(OpCodes.Call, taGetU);
            SetStackType(StackType.Double);
            return;
        }

        // Descriptor-driven fast path: when receiver is statically known to be an array,
        // emit direct List<T> access — skips runtime type dispatch,
        // index boxing, and Convert.ToInt32(object) overhead.
        var desc = ArrayElements.Resolve(_ctx.TypeMap?.Get(gi.Object));
        // Object/Reflect descriptor APIs can install indexed accessors on a
        // statically typed array. In those programs, route reads through the
        // descriptor-aware runtime instead of reading the backing list.
        if (desc != null
            && _ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != true
            && _ctx.TypeMap?.Get(gi.Index) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral)
        {
            // Hoisted path: if the array's isinst was hoisted out of a loop,
            // use the cached typed local — no isinst/castclass per access.
            if (gi.Object is Expr.Variable arrVarGi)
            {
                var hoisted = _ctx.TryGetHoistedArray(arrVarGi.Name.Lexeme);
                if (hoisted.HasValue)
                {
                    var h = hoisted.Value;
                    var fallbackLabel = IL.DefineLabel();
                    var endLabel = IL.DefineLabel();

                    if (h.Descriptor.Kind == ArrayElementsKind.Double)
                    {
                        // Hoisted numeric $Array (escaping number[], #927 step 1): the loop-invariant
                        // `isinst $Array` lives in the preamble, so per-access we only null-check the typed
                        // local and call the numeric-aware Get(long) — it reads the unboxed double[] store
                        // and returns a boxed double, with NO deopt (so the array stays numeric across
                        // reads, keeping interleaved read/write on the fast path). The read-site unbox
                        // (GetDouble) is deliberately NOT used here: it needs the type checker to resolve
                        // arr[i] to number, else the raw double mis-feeds the generic Add path (#918).
                        IL.Emit(OpCodes.Ldloc, h.TypedLocal);
                        IL.Emit(OpCodes.Brfalse, fallbackLabel);

                        IL.Emit(OpCodes.Ldloc, h.TypedLocal);
                        EmitExpressionAsDouble(gi.Index);
                        IL.Emit(OpCodes.Conv_I8);
                        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayGetLong);
                        SetStackUnknown();
                        IL.Emit(OpCodes.Br, endLabel);
                    }
                    else
                    {
                        var listType = h.Descriptor.GetListType(_ctx.Types);
                        IL.Emit(OpCodes.Ldloc, h.TypedLocal);
                        IL.Emit(OpCodes.Brfalse, fallbackLabel);

                        // Fast path: typed local is valid
                        IL.Emit(OpCodes.Ldloc, h.TypedLocal);
                        EmitExpressionAsDouble(gi.Index);
                        IL.Emit(OpCodes.Conv_I4);
                        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(listType, "get_Item", _ctx.Types.Int32));
                        // Box the unboxed element so this branch converges on `object` with the
                        // $Array / List<object?> / fallback paths at endLabel. The typed List<T>
                        // fast path otherwise leaves a native double/bool where the merge point — and
                        // every consumer, which reads the clobbered StackType=Unknown — expects an
                        // object ref. That ran only because the typed branch is dead for $Array-backed
                        // values, but is unverifiable IL (#751).
                        h.Descriptor.EmitBoxElement(IL, _ctx.Types);
                        SetStackUnknown();
                        IL.Emit(OpCodes.Br, endLabel);
                    }

                    // Fallback: type didn't match at loop entry
                    IL.MarkLabel(fallbackLabel);
                    EmitExpression(gi.Object);
                    EmitBoxIfNeeded(gi.Object);
                    EmitExpression(gi.Index);
                    EmitBoxIfNeeded(gi.Index);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
                    SetStackUnknown();

                    IL.MarkLabel(endLabel);
                    return;
                }
            }

            // Non-hoisted path: per-access isinst guard
            var fallbackLabelNH = IL.DefineLabel();
            var endLabelNH = IL.DefineLabel();

            EmitExpression(gi.Object);
            EnsureBoxed();

            var objLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, objLocal);

            // Typed fast path: isinst List<T> → direct get_Item with native type on stack
            if (desc.Kind != ArrayElementsKind.Object)
            {
                var listType = desc.GetListType(_ctx.Types);
                var notTypedLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Isinst, listType);
                IL.Emit(OpCodes.Brfalse, notTypedLabel);

                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Castclass, listType);
                EmitExpressionAsDouble(gi.Index);
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(listType, "get_Item", _ctx.Types.Int32));
                // Box so this branch converges on `object` with the sibling paths at endLabelNH
                // (see the hoisted get path above for the full rationale, #751).
                desc.EmitBoxElement(IL, _ctx.Types);
                SetStackUnknown();
                IL.Emit(OpCodes.Br, endLabelNH);

                IL.MarkLabel(notTypedLabel);
                IL.Emit(OpCodes.Ldloc, objLocal);
            }

            // $Array first (inherits List<object?>; checking List first
            // truncates large indices via Conv_I4 and would throw or misread
            // for uint32-range writes). TSArrayGetLong handles OOB and holes.
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.TSArrayType);
            var notTSArrayGet = IL.DefineLabel();
            IL.Emit(OpCodes.Brfalse, notTSArrayGet);
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSArrayType);
            EmitExpressionAsDouble(gi.Index);
            IL.Emit(OpCodes.Conv_I8);
            IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayGetLong);
            SetStackUnknown();
            IL.Emit(OpCodes.Br, endLabelNH);

            IL.MarkLabel(notTSArrayGet);
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Types.ListOfObject);
            IL.Emit(OpCodes.Brfalse, fallbackLabelNH);

            // List<object?> path: cast + get_Item (int-indexed; ordinary arrays
            // don't exceed int.MaxValue so no widening needed here).
            IL.Emit(OpCodes.Ldloc, objLocal);
            IL.Emit(OpCodes.Castclass, _ctx.Types.ListOfObject);
            EmitExpressionAsDouble(gi.Index);
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.ListOfObject, "get_Item", _ctx.Types.Int32));
            SetStackUnknown();
            IL.Emit(OpCodes.Br, endLabelNH);

            // Fallback: generic dispatch
            IL.MarkLabel(fallbackLabelNH);
            IL.Emit(OpCodes.Ldloc, objLocal);
            EmitExpression(gi.Index);
            EmitBoxIfNeeded(gi.Index);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
            SetStackUnknown();

            IL.MarkLabel(endLabelNH);
            return;
        }

        // Generic (non-array) dynamic bracket read. Spill both operands so the
        // RequireObjectCoercible guard can inspect the receiver and splice the key
        // into the TypeError message: `undefined[k]` throws instead of silently
        // yielding undefined (#701), and `null[k]` too now that sloppy `this` is the
        // globalThis sentinel (#735). The optional `o?.[k]` case returned early above.
        // Null-placeholder globals (e.g. `process`) are exempt.
        var idxRecvLocal = SpillBoxed(gi.Object);
        var idxKeyLocal = SpillBoxed(gi.Index);
        if (!IsNullPlaceholderGlobal(gi.Object))
            EmitThrowIfUndefinedIndexReceiver(idxRecvLocal, idxKeyLocal);
        IL.Emit(OpCodes.Ldloc, idxRecvLocal);
        IL.Emit(OpCodes.Ldloc, idxKeyLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
    }

    private bool TryEmitFlattenedNumericRestIndex(Expr.GetIndex expression)
    {
        if (expression.Optional
            || expression.Object is not Expr.Variable restVariable
            || _ctx.FlattenedNumericRestParameter is not { } flattened
            || restVariable.Name.Lexeme != flattened.Name
            || expression.Index is not Expr.Literal { Value: double index }
            || index < 0
            || index != Math.Truncate(index)
            || index >= flattened.Length)
        {
            return false;
        }

        IL.Emit(OpCodes.Ldarg, flattened.FirstArgumentIndex + (int)index);
        SetStackType(StackType.Double);
        return true;
    }

    /// <summary>
    /// Loads the key/value local for a proven non-escaping Map entry binding.
    /// The analyzer admits only non-optional literal index 0/1 reads, so this
    /// preserves evaluation order while avoiding a per-iteration entry array.
    /// </summary>
    private bool TryEmitStableMapEntryIndex(Expr.GetIndex expression)
    {
        if (expression.Optional
            || expression.Object is not Expr.Variable variable
            || expression.Index is not Expr.Literal { Value: double index }
            || index is not (0d or 1d))
        {
            return false;
        }

        foreach (var binding in _stableMapEntryBindings)
        {
            if (binding.Name != variable.Name.Lexeme)
                continue;

            IL.Emit(OpCodes.Ldloc, index == 0d ? binding.Key : binding.Value);
            SetStackType(StackType.Double);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Emits a native-double numeric-consumer path for a statically-number[] read.
    /// The hot arm is limited to a dense numeric-mode $Array and an exactly integral,
    /// Int32 index. Every unsupported receiver/index shape takes the ordinary GetIndex
    /// path and then the caller's pre-existing ToNumber coercion, so raw/any consumers
    /// never enter this specialization and continue to observe the original JS value.
    /// </summary>
    private bool TryEmitNumberArrayGetIndexAsDouble(Expr.GetIndex gi)
    {
        if (gi.Optional
            || gi.Object is not Expr.Variable arrayVariable
            || ArrayElements.Resolve(_ctx.TypeMap?.Get(gi.Object)) is not
                { Kind: ArrayElementsKind.Double }
            || _ctx.TryGetPromotedArrayLocal(arrayVariable.Name.Lexeme) != null
            || !IsNumericType(_ctx.TypeMap?.Get(gi.Index))
            || _ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true
            || _ctx.RuntimeFeatures?.UsesArrayPrototypeMutation == true)
        {
            return false;
        }

        var hoisted = _ctx.TryGetHoistedArray(arrayVariable.Name.Lexeme);
        if (hoisted is { Descriptor.Kind: not ArrayElementsKind.Double })
            return false;

        // Capture the receiver before evaluating the key. A hoisted exact-$Array
        // local already captured the stable binding at loop entry; parameters and
        // non-hoisted locals are spilled at this read site.
        LocalBuilder? receiverLocal = null;
        LocalBuilder? arrayLocal = null;
        if (hoisted is null)
        {
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);
            receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, receiverLocal);

            arrayLocal = IL.DeclareLocal(_ctx.Runtime!.TSArrayType);
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime.TSArrayType);
            IL.Emit(OpCodes.Stloc, arrayLocal);
        }

        EmitExpressionAsDouble(gi.Index);
        var indexDouble = IL.DeclareLocal(_ctx.Types.Double);
        var indexInt = IL.DeclareLocal(_ctx.Types.Int32);
        IL.Emit(OpCodes.Stloc, indexDouble);
        IL.Emit(OpCodes.Ldloc, indexDouble);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, indexInt);

        var fallbackLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Conv_I4 is used only as a candidate. Round-tripping to double proves
        // exact integrality and Int32 range; Bne_Un also rejects NaN.
        IL.Emit(OpCodes.Ldloc, indexDouble);
        IL.Emit(OpCodes.Ldloc, indexInt);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Bne_Un, fallbackLabel);

        var guardedArray = hoisted?.TypedLocal ?? arrayLocal!;
        IL.Emit(OpCodes.Ldloc, guardedArray);
        IL.Emit(OpCodes.Brfalse, fallbackLabel);
        IL.Emit(OpCodes.Ldloc, guardedArray);
        IL.Emit(OpCodes.Ldloc, indexInt);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayCanGetDouble);
        IL.Emit(OpCodes.Brfalse, fallbackLabel);

        IL.Emit(OpCodes.Ldloc, guardedArray);
        IL.Emit(OpCodes.Ldloc, indexInt);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime.TSArrayGetDouble);
        IL.Emit(OpCodes.Br, endLabel);

        // Cold arm: preserve the numeric key exactly (including fractional,
        // negative, and uint32-range values) and use the descriptor/prototype-
        // aware runtime lookup before applying the numeric consumer's ToNumber.
        IL.MarkLabel(fallbackLabel);
        if (receiverLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, receiverLocal);
        }
        else
        {
            // When the hoisted cast succeeded, it is the captured receiver. If it
            // failed (ordinary object/list supplied through an alias), reload the
            // side-effect-free variable binding for the generic fallback.
            var haveReceiver = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, hoisted!.Value.TypedLocal);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Brtrue, haveReceiver);
            IL.Emit(OpCodes.Pop);
            EmitExpression(gi.Object);
            EmitBoxIfNeeded(gi.Object);
            IL.MarkLabel(haveReceiver);
        }
        IL.Emit(OpCodes.Ldloc, indexDouble);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.GetIndex);
        SetStackUnknown();
        EnsureDouble();

        IL.MarkLabel(endLabel);
        SetStackType(StackType.Double);
        return true;
    }

    /// <summary>
    /// Emits a stack-neutral statement-position write through a guarded numeric
    /// <c>$Array</c>. The ordinary result-producing path must reload and box the
    /// assigned double so arbitrary expression consumers see the JS assignment
    /// value. A discarded expression has no such consumer, so this specialization
    /// keeps the fast arm unboxed while retaining the guarded generic fallback
    /// for non-<c>$Array</c> values passed through a cast. Loop-local receivers
    /// reuse the existing hoisted guard; parameters use the same guard per write.
    /// </summary>
    private bool TryEmitDiscardedNumberArraySetIndex(Expr.SetIndex si)
    {
        if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true
            || si.Object is not Expr.Variable arrayVariable)
            return false;

        var hoisted = _ctx.TryGetHoistedArray(arrayVariable.Name.Lexeme);
        if (hoisted is not { Descriptor.Kind: ArrayElementsKind.Double }
            && ArrayElements.Resolve(_ctx.TypeMap?.Get(si.Object)) is not
                { Kind: ArrayElementsKind.Double })
            return false;

        // Parameters and captured bindings do not have a local eligible for the
        // loop-preamble hoist. Spill those receivers once per write, preserving
        // receiver-before-index-before-RHS evaluation, then guard the same
        // $Array fast arm the ordinary result-producing emitter uses.
        LocalBuilder? receiverLocal = null;
        if (hoisted is null)
        {
            EmitExpression(si.Object);
            EmitBoxIfNeeded(si.Object);
            receiverLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, receiverLocal);
        }

        // Preserve reference evaluation order for the remaining operands:
        // index before RHS. Keep the original numeric key as a double for the
        // array-like fallback (3.5 must remain property "3.5" there); only the
        // guarded $Array arm narrows it exactly as the ordinary fast path does.
        EmitExpressionAsDouble(si.Index);
        var indexLocal = IL.DeclareLocal(_ctx.Types.Double);
        IL.Emit(OpCodes.Stloc, indexLocal);

        EmitExpression(si.Value);
        EnsureDouble();
        var valueLocal = IL.DeclareLocal(_ctx.Types.Double);
        IL.Emit(OpCodes.Stloc, valueLocal);

        var fallbackLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        if (hoisted is { } cached)
        {
            IL.Emit(OpCodes.Ldloc, cached.TypedLocal);
            IL.Emit(OpCodes.Brfalse, fallbackLabel);
            IL.Emit(OpCodes.Ldloc, cached.TypedLocal);
        }
        else
        {
            IL.Emit(OpCodes.Ldloc, receiverLocal!);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.TSArrayType);
            IL.Emit(OpCodes.Brfalse, fallbackLabel);
            IL.Emit(OpCodes.Ldloc, receiverLocal!);
            IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSArrayType);
        }
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArraySetDouble);
        IL.Emit(OpCodes.Br, endLabel);

        // A value asserted to number[] can still be an arbitrary array-like at
        // runtime. Reuse the spec-complete setter on that guarded cold arm.
        IL.MarkLabel(fallbackLabel);
        if (receiverLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, receiverLocal);
        }
        else
        {
            EmitExpression(si.Object);
            EmitBoxIfNeeded(si.Object);
        }
        IL.Emit(OpCodes.Ldloc, indexLocal);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Box, _ctx.Types.Double);
        if (_ctx.IsStrictMode)
        {
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndexStrict);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
        }

        IL.MarkLabel(endLabel);
        return true;
    }

    protected override void EmitSetIndex(Expr.SetIndex si)
    {
        if (TryResolveExternalReceiverType(si.Object, out var externalIndexerType) &&
            TryEmitExternalIndexerSet(si.Object, externalIndexerType, si.Index, si.Value))
        {
            return;
        }

        // globalThis[key] = value → GlobalThisSetProperty(key, value)
        if (si.Object is Expr.Variable gtSetIdx && gtSetIdx.Name.Lexeme == "globalThis")
        {
            EmitExpression(si.Index);
            EnsureBoxed();
            var indexTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, indexTemp);
            EmitExpression(si.Value);
            EmitBoxIfNeeded(si.Value);
            var valueTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, valueTemp);
            IL.Emit(OpCodes.Ldloc, indexTemp);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.Object, "ToString")!);
            IL.Emit(OpCodes.Ldloc, valueTemp);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisSetProperty);
            IL.Emit(OpCodes.Ldloc, valueTemp); // expression result
            SetStackUnknown();
            return;
        }

        // Promoted typed-array local (#857/#860): the slot IS a List<double>/List<bool>, so write
        // directly via the typed auto-extend setter — no isinst dispatch, no value boxing. Value is
        // evaluated before index, matching the hoisted path's ordering.
        if (si.Object is Expr.Variable promVarSet
            && _ctx.TryGetPromotedArrayLocal(promVarSet.Name.Lexeme) is { } promSet)
        {
            EmitExpression(si.Value);
            if (promSet.Descriptor.Kind == ArrayElementsKind.Double) EnsureDouble();
            else EnsureBoolean();
            var valLocal = IL.DeclareLocal(promSet.Descriptor.GetElementType(_ctx.Types));
            IL.Emit(OpCodes.Stloc, valLocal);

            IL.Emit(OpCodes.Ldloc, promSet.Local);
            EmitExpressionAsDouble(si.Index);
            IL.Emit(OpCodes.Conv_I4);
            IL.Emit(OpCodes.Ldloc, valLocal);
            IL.Emit(OpCodes.Call, promSet.Descriptor.GetSetArrayElementMethod(_ctx.Runtime!));

            // Assignment expression result: the (unboxed) assigned value.
            IL.Emit(OpCodes.Ldloc, valLocal);
            SetStackType(promSet.Descriptor.StackType);
            return;
        }

        // Numeric typed-array fast path (#3, generalizing #878 past Float64): variable statically
        // typed as a numeric typed array with a statically-numeric RHS — write the element UNBOXED
        // via $XArray.SetUnboxed. Eliminates the Runtime.SetIndex dispatch, the isinst, the value
        // box, and the Convert.ToDouble coercion; the double→element narrowing lives in SetUnboxed
        // and matches the boxed Set. A non-numeric RHS (or BigInt/Uint8Clamped) falls through to the
        // boxed path, which performs JS ToNumber coercion. OOB faults exactly as the boxed path does.
        // Evaluate index then value (the receiver var is side-effect-free).
        if (si.Object is Expr.Variable
            && _ctx.TypeMap?.Get(si.Object) is TypeInfo.TypedArray sta
            && _ctx.Runtime!.GetTypedArrayType(sta.ElementType) is { } staType
            && _ctx.Runtime!.TypedArraySetUnboxedByElement.TryGetValue(sta.ElementType, out var taSetU)
            && _ctx.TypeMap?.Get(si.Value) is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral)
        {
            // Native-int fast path when the index is an integer loop counter (#928).
            EmitIndexAsInt32(si.Index);
            var idxLocal = IL.DeclareLocal(_ctx.Types.Int32);
            IL.Emit(OpCodes.Stloc, idxLocal);

            bool hasDirectBacking =
                TryGetDirectTypedArrayBacking(si.Object, sta.ElementType, out var backing);
            if (hasDirectBacking
                && TryEmitDirectInt32CounterWrite(si.Index, si.Value, backing, idxLocal))
            {
                return;
            }

            EmitExpression(si.Value);
            EnsureDouble();
            var valLocal = IL.DeclareLocal(_ctx.Types.Double);
            IL.Emit(OpCodes.Stloc, valLocal);

            if (hasDirectBacking)
            {
                EmitDirectTypedArrayWrite(backing, idxLocal, valLocal);
                IL.Emit(OpCodes.Ldloc, valLocal);
                SetStackType(StackType.Double);
                return;
            }

            // Receiver: hoisted loop-invariant cast when available, else per-access cast (#928).
            EmitTypedArrayReceiver(si.Object, staType);
            IL.Emit(OpCodes.Ldloc, idxLocal);
            IL.Emit(OpCodes.Ldloc, valLocal);
            // Non-virtual call to the sealed-type accessor → the JIT inlines it (AggressiveInlining).
            IL.Emit(OpCodes.Call, taSetU);

            // Assignment expression result: the (unboxed) assigned value (the RHS, JS semantics).
            IL.Emit(OpCodes.Ldloc, valLocal);
            SetStackType(StackType.Double);
            return;
        }

        // Descriptor-driven fast path: when receiver is statically known to be an array,
        // emit direct List<T> access with auto-extension — skips runtime type dispatch,
        // index boxing, and Convert.ToInt32(object) overhead.
        // Try hoisted path first — works even when TypeMap doesn't have the receiver type
        if (_ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != true
            && si.Object is Expr.Variable arrVarSiEarly)
        {
            var hoistedEarly = _ctx.TryGetHoistedArray(arrVarSiEarly.Name.Lexeme);
            if (hoistedEarly.HasValue)
            {
                var h = hoistedEarly.Value;
                var listType = h.Descriptor.GetListType(_ctx.Types);
                var fallbackLabel = IL.DefineLabel();
                var endLabel = IL.DefineLabel();

                // Emit and coerce value
                EmitExpression(si.Value);
                if (h.Descriptor.Kind == ArrayElementsKind.Double) EnsureDouble();
                else if (h.Descriptor.Kind == ArrayElementsKind.Bool) EnsureBoolean();
                else EmitBoxIfNeeded(si.Value);
                var typedValueLocal = IL.DeclareLocal(h.Descriptor.GetElementType(_ctx.Types));
                IL.Emit(OpCodes.Stloc, typedValueLocal);

                IL.Emit(OpCodes.Ldloc, h.TypedLocal);
                IL.Emit(OpCodes.Brfalse, fallbackLabel);

                // Fast path: typed local is valid
                IL.Emit(OpCodes.Ldloc, h.TypedLocal);
                EmitExpressionAsDouble(si.Index);
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Ldloc, typedValueLocal);
                if (h.Descriptor.Kind == ArrayElementsKind.Double)
                    // Hoisted numeric $Array (#927 step 1): SetDouble stores the unboxed double straight
                    // into the double[] store (mode-checked — a boxed $Array delegates to the boxed setter,
                    // so this is behaviour-identical for both modes). h.TypedLocal is the hoisted $Array.
                    IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArraySetDouble);
                else
                    IL.Emit(OpCodes.Call, h.Descriptor.GetSetArrayElementMethod(_ctx.Runtime!));
                IL.Emit(OpCodes.Ldloc, typedValueLocal);
                // Box the assigned value so this branch leaves `object` like the fallback path at
                // endLabel (the assignment result is consumed via StackType=Unknown), #751.
                h.Descriptor.EmitBoxElement(IL, _ctx.Types);
                SetStackUnknown();
                IL.Emit(OpCodes.Br, endLabel);

                // Fallback: type didn't match at loop entry
                IL.MarkLabel(fallbackLabel);
                IL.Emit(OpCodes.Ldloc, typedValueLocal);
                if (h.Descriptor.NeedsBoxOnGet)
                    IL.Emit(OpCodes.Box, h.Descriptor.GetElementType(_ctx.Types));
                var fallbackValueLocal = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, fallbackValueLocal);
                EmitExpression(si.Object);
                EmitBoxIfNeeded(si.Object);
                EmitExpression(si.Index);
                EmitBoxIfNeeded(si.Index);
                IL.Emit(OpCodes.Ldloc, fallbackValueLocal);
                if (_ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndexStrict);
                }
                else
                {
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
                }
                IL.Emit(OpCodes.Ldloc, fallbackValueLocal);
                SetStackUnknown();

                IL.MarkLabel(endLabel);
                return;
            }
        }

        var siTypeInfo = _ctx.TypeMap?.Get(si.Object);
        var desc = ArrayElements.Resolve(siTypeInfo);

        if (desc != null && _ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != true)
        {
            // Non-hoisted path: per-access isinst guard
            var fallbackLabelNH = IL.DefineLabel();
            var endLabelNH = IL.DefineLabel();

            // Emit and coerce value based on descriptor
            EmitExpression(si.Value);
            if (desc.Kind == ArrayElementsKind.Double) EnsureDouble();
            else if (desc.Kind == ArrayElementsKind.Bool) EnsureBoolean();
            else EmitBoxIfNeeded(si.Value);

            var typedValueLocalNH = IL.DeclareLocal(desc.GetElementType(_ctx.Types));
            IL.Emit(OpCodes.Stloc, typedValueLocalNH);

            EmitExpression(si.Object);
            EnsureBoxed();

            var objLocal = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stloc, objLocal);

            // Typed fast path: isinst List<T> → direct SetArrayElement{Kind}
            if (desc.Kind != ArrayElementsKind.Object)
            {
                var listType = desc.GetListType(_ctx.Types);
                var notTypedLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Isinst, listType);
                IL.Emit(OpCodes.Brfalse, notTypedLabel);

                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Castclass, listType);
                EmitExpressionAsDouble(si.Index);
                IL.Emit(OpCodes.Conv_I4);
                IL.Emit(OpCodes.Ldloc, typedValueLocalNH);
                IL.Emit(OpCodes.Call, desc.GetSetArrayElementMethod(_ctx.Runtime!));
                IL.Emit(OpCodes.Ldloc, typedValueLocalNH);
                // Box so this branch converges on `object` with the sibling paths at endLabelNH
                // (see the hoisted set path above for the full rationale, #751).
                desc.EmitBoxElement(IL, _ctx.Types);
                SetStackUnknown();
                IL.Emit(OpCodes.Br, endLabelNH);

                IL.MarkLabel(notTypedLabel);

                // $Array numeric fast path (number[] unboxing): SetDouble takes the
                // UNBOXED double, so a numeric-mode $Array stores it straight into
                // its double[] with no allocation (the write side of the 73x gap).
                // SetDouble is mode-checked — a boxed $Array delegates to Set, so
                // this is behaviour-identical until numeric creation is wired.
                if (desc.Kind == ArrayElementsKind.Double)
                {
                    IL.Emit(OpCodes.Ldloc, objLocal);
                    IL.Emit(OpCodes.Isinst, _ctx.Runtime!.TSArrayType);
                    var notTSArraySet = IL.DefineLabel();
                    IL.Emit(OpCodes.Brfalse, notTSArraySet);
                    IL.Emit(OpCodes.Ldloc, objLocal);
                    IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSArrayType);
                    EmitExpressionAsDouble(si.Index);
                    IL.Emit(OpCodes.Conv_I4);
                    IL.Emit(OpCodes.Ldloc, typedValueLocalNH);
                    IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArraySetDouble);
                    IL.Emit(OpCodes.Ldloc, typedValueLocalNH);
                    desc.EmitBoxElement(IL, _ctx.Types);
                    SetStackUnknown();
                    IL.Emit(OpCodes.Br, endLabelNH);
                    IL.MarkLabel(notTSArraySet);
                }

                // Not typed list: box value and fall through to List<object?> path
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Ldloc, typedValueLocalNH);
                IL.Emit(OpCodes.Box, desc.GetElementType(_ctx.Types));
                var boxedValueLocal = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, boxedValueLocal);

                EmitSetIndexListObjectPath(si, objLocal, boxedValueLocal, fallbackLabelNH, endLabelNH);
                return;
            }

            // Object descriptor: go straight to List<object?> path
            EmitSetIndexListObjectPath(si, objLocal, typedValueLocalNH, fallbackLabelNH, endLabelNH);
            return;
        }

        // No static type info: evaluate the complete reference before the RHS.
        EmitExpression(si.Object);
        EmitBoxIfNeeded(si.Object);
        var objLocalGeneric = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, objLocalGeneric);

        EmitExpression(si.Index);
        EmitBoxIfNeeded(si.Index);
        var idxLocalGeneric = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, idxLocalGeneric);

        EmitExpression(si.Value);
        EmitBoxIfNeeded(si.Value);
        var valueLocalGeneric = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, valueLocalGeneric);

        // RequireObjectCoercible (PutValue): a null/undefined base throws a guest
        // TypeError ("Cannot set properties of undefined|null (setting 'X')") (#733).
        // Null-placeholder globals (e.g. `process`) are exempt.
        if (!IsNullPlaceholderGlobal(si.Object))
            EmitThrowIfUndefinedIndexReceiver(objLocalGeneric, idxLocalGeneric, isWrite: true);

        IL.Emit(OpCodes.Ldloc, objLocalGeneric);
        IL.Emit(OpCodes.Ldloc, idxLocalGeneric);
        IL.Emit(OpCodes.Ldloc, valueLocalGeneric);

        if (_ctx.IsStrictMode)
        {
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndexStrict);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
        }

        IL.Emit(OpCodes.Ldloc, valueLocalGeneric);
    }

    /// <summary>
    /// Emits <c>x.push(args)</c> for a promoted typed-array local (#857/#860): append each
    /// (unboxed) argument directly to the bare <c>List&lt;T&gt;</c> via the typed
    /// <c>ArrayPush{Double,Bool}</c> helper, leaving the final length (a JS number) as the
    /// expression result. No <c>$Array</c> unwrap/copy and no per-element boxing.
    /// </summary>
    private void EmitPromotedArrayPush(LocalBuilder list, ArrayElementsDescriptor desc, List<Expr> arguments)
    {
        var pushMethod = desc.Kind == ArrayElementsKind.Double
            ? _ctx.Runtime!.ArrayPushDouble
            : _ctx.Runtime!.ArrayPushBool;

        if (arguments.Count == 0)
        {
            // push() with no args returns the current length.
            var listType = desc.GetListType(_ctx.Types);
            IL.Emit(OpCodes.Ldloc, list);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetProperty(listType, "Count").GetGetMethod()!);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, list);
            EmitExpression(arguments[i]);
            if (desc.Kind == ArrayElementsKind.Double) EnsureDouble();
            else EnsureBoolean();
            IL.Emit(OpCodes.Call, pushMethod);
            if (i < arguments.Count - 1)
                IL.Emit(OpCodes.Pop); // discard intermediate length; keep only the final one
        }
        SetStackType(StackType.Double);
    }

    private void EmitPromotedArrayShift(LocalBuilder list, ArrayElementsDescriptor desc)
    {
        IL.Emit(OpCodes.Ldloc, list);
        IL.Emit(OpCodes.Call, desc.Kind == ArrayElementsKind.Double
            ? _ctx.Runtime!.ArrayShiftDouble
            : _ctx.Runtime!.ArrayShiftBool);
        SetStackUnknown();
    }

    private void EmitPromotedArrayUnshift(
        LocalBuilder list,
        ArrayElementsDescriptor desc,
        List<Expr> arguments)
    {
        var listType = desc.GetListType(_ctx.Types);
        if (arguments.Count == 0)
        {
            IL.Emit(OpCodes.Ldloc, list);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetProperty(listType, "Count").GetGetMethod()!);
            IL.Emit(OpCodes.Conv_R8);
            SetStackType(StackType.Double);
            return;
        }

        // ECMAScript evaluates every argument before moving any element. Spill
        // them in source order, then prepend in reverse order so the final array
        // preserves the original argument order without an object[] pack.
        var elementType = desc.GetElementType(_ctx.Types);
        var values = new LocalBuilder[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            EmitExpression(arguments[i]);
            if (desc.Kind == ArrayElementsKind.Double) EnsureDouble();
            else EnsureBoolean();
            values[i] = IL.DeclareLocal(elementType);
            IL.Emit(OpCodes.Stloc, values[i]);
        }

        var helper = desc.Kind == ArrayElementsKind.Double
            ? _ctx.Runtime!.ArrayUnshiftDouble
            : _ctx.Runtime!.ArrayUnshiftBool;
        for (int i = arguments.Count - 1; i >= 0; i--)
        {
            IL.Emit(OpCodes.Ldloc, list);
            IL.Emit(OpCodes.Ldloc, values[i]);
            IL.Emit(OpCodes.Call, helper);
            if (i != 0)
                IL.Emit(OpCodes.Pop);
        }
        SetStackType(StackType.Double);
    }

    /// <summary>
    /// Emits <c>x.push(args)</c> for an ESCAPING <c>number[]</c> whose runtime value is a <c>$Array</c>
    /// (number[] unboxing project): append each (unboxed) <c>double</c> argument via the mode-checked
    /// <c>$Array.PushDouble</c>, then leave the new length (a JS number) as the result. A numeric-mode
    /// receiver appends straight into its <c>double[]</c> store with no boxing and stays numeric (unlike
    /// the generic dispatcher, which unwraps the array and deopts it); a boxed receiver has PushDouble
    /// delegate to the boxed append, so this is behaviour-identical for both. Caller gates on a statically
    /// <c>number[]</c> receiver with statically <c>number</c> arguments (see EmitMethodCall).
    /// </summary>
    private void EmitEscapingNumberArrayPush(Expr receiver, List<Expr> arguments)
    {
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSArrayType);
        var arrLocal = IL.DeclareLocal(_ctx.Runtime!.TSArrayType);
        IL.Emit(OpCodes.Stloc, arrLocal);

        for (int i = 0; i < arguments.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, arrLocal);
            EmitExpressionAsDouble(arguments[i]);
            IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayPushDouble);
        }

        // push() returns the new length. _length is authoritative in both modes
        // (PushDouble maintains it numeric; SyncLength reconciles it boxed).
        IL.Emit(OpCodes.Ldloc, arrLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArrayLongLengthGetter);
        IL.Emit(OpCodes.Conv_R8);
        SetStackType(StackType.Double);
    }

    /// <summary>
    /// Emits <c>s.charCodeAt(i)</c> for a promoted string-accumulator (StringBuilder slot): reads the
    /// UTF-16 code unit directly via the <c>this[int]</c> indexer (identical to JS charCodeAt), with an
    /// out-of-range (incl. negative, via unsigned compare) result of NaN. Leaves a boxed double, matching
    /// the string-method call convention. See EmitMethodCall and StringAccumulatorPromotionAnalyzer.
    /// </summary>
    private void EmitPromotedStringCharCodeAt(LocalBuilder sb, List<Expr> arguments)
    {
        var getLength = _ctx.Types.GetProperty(_ctx.Types.StringBuilder, "Length").GetGetMethod()!;
        var getChars = _ctx.Types.GetMethod(_ctx.Types.StringBuilder, "get_Chars", _ctx.Types.Int32);

        var idxLocal = IL.DeclareLocal(_ctx.Types.Int32);
        if (arguments.Count > 0) EmitExpressionAsDouble(arguments[0]);
        else IL.Emit(OpCodes.Ldc_R8, 0.0);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, idxLocal);

        var oob = IL.DefineLabel();
        var end = IL.DefineLabel();

        // if ((uint)idx >= (uint)sb.Length) -> NaN (unsigned fold catches negative indices too)
        IL.Emit(OpCodes.Ldloc, idxLocal);
        IL.Emit(OpCodes.Ldloc, sb);
        IL.Emit(OpCodes.Callvirt, getLength);
        IL.Emit(OpCodes.Bge_Un, oob);

        IL.Emit(OpCodes.Ldloc, sb);
        IL.Emit(OpCodes.Ldloc, idxLocal);
        IL.Emit(OpCodes.Callvirt, getChars);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Br, end);

        IL.MarkLabel(oob);
        IL.Emit(OpCodes.Ldc_R8, double.NaN);

        // #859: leave a raw float64 (both branches push double) instead of boxing. The result is
        // typically consumed by a numeric op (`sum + s.charCodeAt(i)`), whose EnsureDouble is then a
        // no-op; a boxed-object consumer re-boxes via EmitBoxIfNeeded (which checks StackType). This
        // elides the per-char `box Double` (a heap allocation) plus the consumer's `ConvertToNumber`.
        IL.MarkLabel(end);
        SetStackType(StackType.Double);
    }

    /// <summary>
    /// Emits the common List&lt;object?&gt; / $Array set path with frozen checks and fallback.
    /// Shared by all descriptor-driven SetIndex paths (typed miss fallthrough and object direct).
    /// Stack: obj is on the stack (from the isinst result). objLocal and valueLocal are populated.
    /// </summary>
    private void EmitSetIndexListObjectPath(
        Expr.SetIndex si, LocalBuilder objLocal, LocalBuilder valueLocal,
        Label fallbackLabel, Label endLabel)
    {
        // $Array first — since $Array inherits List<object?>, checking List
        // first would catch $Array via the typed-list fast path and truncate
        // large indices through Conv_I4 (2147483648 → int.MinValue), then
        // SetArrayElement's pad-loop OOMs. The long-indexed TSArraySetLong
        // handles uint32 range and sparse transitions natively.
        IL.Emit(OpCodes.Isinst, _ctx.Runtime!.TSArrayType);
        var notTSArrayLabel = IL.DefineLabel();
        IL.Emit(OpCodes.Brfalse, notTSArrayLabel);

        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, _ctx.Runtime!.TSArrayType);
        EmitExpressionAsDouble(si.Index);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSArraySetLong);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(notTSArrayLabel);

        // Check List<object?>
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Isinst, _ctx.Types.ListOfObject);
        var isListLabel = IL.DefineLabel();
        IL.Emit(OpCodes.Brtrue, isListLabel);
        IL.Emit(OpCodes.Br, fallbackLabel);

        // List path: check frozen, then cast
        IL.MarkLabel(isListLabel);
        var frozenCheckLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.FrozenObjectsField);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloca, frozenCheckLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(
            _ctx.Types.ConditionalWeakTable, "TryGetValue",
            _ctx.Types.Object, _ctx.Types.Object.MakeByRefType()));
        IL.Emit(OpCodes.Brtrue, fallbackLabel);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Castclass, _ctx.Types.ListOfObject);

        // List<object?>: SetArrayElement(list, index, value)
        EmitExpressionAsDouble(si.Index);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetArrayElement);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        IL.Emit(OpCodes.Br, endLabel);

        // Fallback: generic dispatch
        IL.MarkLabel(fallbackLabel);
        var idxFallbackLocal = SpillBoxed(si.Index);
        // RequireObjectCoercible (PutValue): a null/undefined base throws a guest
        // TypeError. Reached when a statically-typed receiver is null/undefined at
        // runtime (typed miss) — its value/index side effects have already run. (#733)
        // Null-placeholder globals (e.g. `process`) are exempt.
        if (!IsNullPlaceholderGlobal(si.Object))
            EmitThrowIfUndefinedIndexReceiver(objLocal, idxFallbackLocal, isWrite: true);
        IL.Emit(OpCodes.Ldloc, objLocal);
        IL.Emit(OpCodes.Ldloc, idxFallbackLocal);
        IL.Emit(OpCodes.Ldloc, valueLocal);
        if (_ctx.IsStrictMode)
        {
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndexStrict);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
        }
        IL.Emit(OpCodes.Ldloc, valueLocal);

        IL.MarkLabel(endLabel);
    }

    /// <summary>
    /// Try to emit a direct getter call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectGetterCall(Expr receiver, TypeInfo? receiverType, string propertyName)
    {
        // Resolve TypeParameter constraints (e.g., T extends Animal → Instance(Animal))
        if (receiverType is TypeInfo.TypeParameter { Constraint: TypeInfo.Instance } tp)
            receiverType = tp.Constraint;

        // Only handle Instance types (e.g., let p: Person = ...)
        if (receiverType is not TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? simpleClassName = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Check if this is an external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(simpleClassName, out var externalType))
        {
            EmitExternalPropertyGet(receiver, externalType, propertyName);
            return true;
        }

        // Resolve to qualified name for multi-module compilation
        string className = _ctx.ResolveClassName(simpleClassName);

        // Also check if the qualified name is an external type
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out externalType))
        {
            EmitExternalPropertyGet(receiver, externalType, propertyName);
            return true;
        }

        // Convert TypeScript camelCase property name to .NET PascalCase for lookup
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Look up the getter in the class hierarchy
        var getterBuilder = _ctx.ResolveInstanceGetter(className, pascalPropertyName);
        if (getterBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Generic classes need instantiated tokens (Stack<!T>), only expressible inside
        // the class's own bodies; otherwise fall back to runtime dispatch (#178)
        if (!EmitterTypeHelpers.TryResolveInstanceDispatch(
                classType, getterBuilder, _ctx.EmittingTypeBuilder, out var castType, out var getterTarget))
            return false;

        // Emit: ((ClassName)receiver).get_PropertyName()
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        IL.Emit(OpCodes.Castclass, castType);
        IL.Emit(OpCodes.Callvirt, getterTarget);

        // Check the actual return type of the getter method
        // Field properties have typed getters, but explicit accessors return object
        var getterReturnType = getterBuilder.ReturnType;

        if (_ctx.Types.IsDouble(getterReturnType))
        {
            // A declared `number` field is already a native double. Keep that representation
            // through arithmetic and loop consumers; they will box only if an object boundary
            // actually requires it.
            SetStackType(StackType.Double);
        }
        else if (_ctx.Types.IsBoolean(getterReturnType))
        {
            SetStackType(StackType.Boolean);
        }
        else if (getterReturnType.IsValueType)
        {
            // Other CLR value types are not represented by StackType and must retain the
            // established boxed-object contract.
            IL.Emit(OpCodes.Box, getterReturnType);
            SetStackUnknown();
        }
        else if (_ctx.Types.IsString(getterReturnType))
        {
            SetStackType(StackType.String);
        }
        else
        {
            // Reference types (including object) don't need boxing
            SetStackUnknown();
        }

        return true;
    }

    /// <summary>
    /// Try to emit a direct setter call for known class instance types.
    /// Returns true if direct dispatch was emitted, false to fall back to runtime dispatch.
    /// </summary>
    private bool TryEmitDirectSetterCall(Expr receiver, TypeInfo? receiverType, string propertyName, Expr value)
    {
        // Only handle Instance types (e.g., let p: Person = ...)
        if (receiverType is not TypeInfo.Instance instance)
            return false;

        // Extract the class name from the instance's class type
        string? simpleClassName = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            _ => null
        };
        if (simpleClassName == null)
            return false;

        // Check if this is an external .NET type (@DotNetType)
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(simpleClassName, out var externalType))
        {
            EmitExternalPropertySet(receiver, externalType, propertyName, value);
            return true;
        }

        // Resolve to qualified name for multi-module compilation
        string className = _ctx.ResolveClassName(simpleClassName);

        // Also check if the qualified name is an external type
        if (_ctx.TypeMapper.ExternalTypes.TryGetValue(className, out externalType))
        {
            EmitExternalPropertySet(receiver, externalType, propertyName, value);
            return true;
        }

        // Convert TypeScript camelCase property name to .NET PascalCase for lookup
        string pascalPropertyName = NamingConventions.ToPascalCase(propertyName);

        // Look up the setter in the class hierarchy
        var setterBuilder = _ctx.ResolveInstanceSetter(className, pascalPropertyName);
        if (setterBuilder == null)
            return false;

        // Get the class type builder to cast the receiver
        if (!_ctx.Classes.TryGetValue(className, out var classType))
            return false;

        // Generic classes need instantiated tokens (Stack<!T>), only expressible inside
        // the class's own bodies; otherwise fall back to runtime dispatch (#178)
        if (!EmitterTypeHelpers.TryResolveInstanceDispatch(
                classType, setterBuilder, _ctx.EmittingTypeBuilder, out var castType, out var setterTarget))
            return false;

        // Get the actual parameter type of the setter method
        // Field properties have typed setters, but explicit accessors take object
        var setterParams = setterBuilder.GetParameters();
        var setterParamType = setterParams.Length > 0 ? setterParams[0].ParameterType : _ctx.Types.Object;

        // Emit: ((ClassName)receiver).set_PropertyName(value), while retaining the value as the
        // assignment expression result. Primitive field values stay in typed locals across the
        // frozen-object branch so the common path does not box merely to duplicate a value.

        // Emit receiver and save for freeze check and potential setter call
        EmitExpression(receiver);
        EmitBoxIfNeeded(receiver);
        var receiverTemp = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, receiverTemp);

        if (TryEmitTypedDirectSetter(
                receiverTemp, castType, setterBuilder, setterTarget, setterParamType, value))
            return true;

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        var notFrozenLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();
        var frozenCheckLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.FrozenObjectsField);
        IL.Emit(OpCodes.Ldloc, receiverTemp);
        IL.Emit(OpCodes.Ldloca, frozenCheckLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.ConditionalWeakTable, "TryGetValue", _ctx.Types.Object, _ctx.Types.Object.MakeByRefType()));
        IL.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Object is frozen - emit value but skip setter call
        // Just return the value as the expression result
        EmitExpression(value);
        EmitBoxIfNeeded(value);
        IL.Emit(OpCodes.Br, endLabel);

        // Not frozen - proceed with normal setter call
        IL.MarkLabel(notFrozenLabel);

        // Load receiver and cast to class type
        IL.Emit(OpCodes.Ldloc, receiverTemp);
        IL.Emit(OpCodes.Castclass, castType);

        // Emit value and convert to setter parameter type
        EmitExpression(value);

        // Check if setter returns void (field properties) or object (explicit accessors)
        var setterReturnsVoid = _ctx.Types.IsVoid(setterBuilder.ReturnType);

        // Save a copy for expression result (need to box if value type for consistent handling)
        if (setterParamType.IsValueType)
        {
            // For value types: box first, then dup, then unbox for setter
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup);
            var resultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, resultTemp);
            IL.Emit(OpCodes.Unbox_Any, setterParamType);
            IL.Emit(OpCodes.Callvirt, setterTarget);
            // Pop setter return value if not void (explicit accessors return object)
            if (!setterReturnsVoid)
            {
                IL.Emit(OpCodes.Pop);
            }
            IL.Emit(OpCodes.Ldloc, resultTemp);
        }
        else
        {
            // For reference types (including object): dup, optionally cast, call setter
            EmitBoxIfNeeded(value);
            IL.Emit(OpCodes.Dup);
            var resultTemp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, resultTemp);
            if (!_ctx.Types.IsObject(setterParamType))
            {
                IL.Emit(OpCodes.Castclass, setterParamType);
            }
            IL.Emit(OpCodes.Callvirt, setterTarget);
            // Pop setter return value if not void (explicit accessors return object)
            if (!setterReturnsVoid)
            {
                IL.Emit(OpCodes.Pop);
            }
            IL.Emit(OpCodes.Ldloc, resultTemp);
        }

        IL.MarkLabel(endLabel);
        SetStackUnknown();  // Result is boxed object
        return true;
    }

    /// <summary>
    /// Emits a direct class-field setter without erasing a statically matching primitive value.
    /// The frozen-object check and setter invocation semantics are identical to the boxed path;
    /// only the temporary/result representation differs.
    /// </summary>
    private bool TryEmitTypedDirectSetter(
        LocalBuilder receiverTemp,
        Type castType,
        MethodBuilder setterBuilder,
        System.Reflection.MethodInfo setterTarget,
        Type setterParamType,
        Expr value)
    {
        StackType resultStackType;
        if (_ctx.Types.IsDouble(setterParamType) && IsNumericType(_ctx.TypeMap?.Get(value)))
            resultStackType = StackType.Double;
        else if (_ctx.Types.IsBoolean(setterParamType)
                 && _ctx.TypeMap?.Get(value) is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN })
            resultStackType = StackType.Boolean;
        else if (_ctx.Types.IsString(setterParamType)
                 && _ctx.TypeMap?.Get(value) is TypeInfo.String)
            resultStackType = StackType.String;
        else
            return false;

        EmitExpression(value);
        switch (resultStackType)
        {
            case StackType.Double: EnsureDouble(); break;
            case StackType.Boolean: EnsureBoolean(); break;
            case StackType.String: EnsureString(); break;
        }

        var valueTemp = IL.DeclareLocal(setterParamType);
        IL.Emit(OpCodes.Stloc, valueTemp);

        if (_ctx.RuntimeFeatures?.UsesObjectIntegrityMutation == false)
        {
            EmitTypedDirectSetterCall(
                receiverTemp, castType, setterBuilder, setterTarget, valueTemp);
            IL.Emit(OpCodes.Ldloc, valueTemp);
            SetStackType(resultStackType);
            return true;
        }

        var endLabel = IL.DefineLabel();
        var frozenCheckLocal = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.FrozenObjectsField);
        IL.Emit(OpCodes.Ldloc, receiverTemp);
        IL.Emit(OpCodes.Ldloca, frozenCheckLocal);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(
            _ctx.Types.ConditionalWeakTable, "TryGetValue",
            _ctx.Types.Object, _ctx.Types.Object.MakeByRefType()));
        IL.Emit(OpCodes.Brtrue, endLabel);

        EmitTypedDirectSetterCall(
            receiverTemp, castType, setterBuilder, setterTarget, valueTemp);

        IL.MarkLabel(endLabel);
        IL.Emit(OpCodes.Ldloc, valueTemp);
        SetStackType(resultStackType);
        return true;
    }

    private void EmitTypedDirectSetterCall(
        LocalBuilder receiverTemp,
        Type castType,
        MethodBuilder setterBuilder,
        System.Reflection.MethodInfo setterTarget,
        LocalBuilder valueTemp)
    {
        IL.Emit(OpCodes.Ldloc, receiverTemp);
        IL.Emit(OpCodes.Castclass, castType);
        IL.Emit(OpCodes.Ldloc, valueTemp);
        IL.Emit(OpCodes.Callvirt, setterTarget);
        if (!_ctx.Types.IsVoid(setterBuilder.ReturnType))
            IL.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Tries to emit IL for process.stdin.isTTY, process.stdout.isTTY, process.stderr.isTTY property access.
    /// Returns true if the property was handled.
    /// </summary>
    private bool TryEmitProcessStreamProperty(Expr.Get g)
    {
        // Pattern: process.stdin.X, process.stdout.X, process.stderr.X
        // g.Object is Expr.Get { Object: Expr.Variable("process"), Name: "stdin/stdout/stderr" }

        if (g.Object is not Expr.Get streamGet)
            return false;

        if (streamGet.Object is not Expr.Variable processVar || processVar.Name.Lexeme != "process")
            return false;

        string streamName = streamGet.Name.Lexeme;
        string propertyName = g.Name.Lexeme;

        // Handle isTTY for all streams
        if (propertyName == "isTTY")
        {
            switch (streamName)
            {
                case "stdin":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StdinIsTTY);
                    SetStackUnknown();
                    return true;
                case "stdout":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StdoutIsTTY);
                    SetStackUnknown();
                    return true;
                case "stderr":
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.StderrIsTTY);
                    SetStackUnknown();
                    return true;
            }
        }

        // Handle writable stream properties for stdout/stderr
        if (streamName is "stdout" or "stderr")
        {
            switch (propertyName)
            {
                case "writable":
                    IL.Emit(OpCodes.Ldc_I4_1);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return true;
                case "writableEnded":
                case "writableFinished":
                case "destroyed":
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return true;
            }
        }

        // Handle readable stream properties for stdin
        if (streamName == "stdin")
        {
            switch (propertyName)
            {
                case "readable":
                    IL.Emit(OpCodes.Ldc_I4_1);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return true;
                case "readableEnded":
                case "destroyed":
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                    SetStackUnknown();
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the expression statically resolves to globalThis.
    /// Handles Var("globalThis") and any chain of globalThis.globalThis.globalThis...
    /// </summary>
    private static bool IsGlobalThisExpression(Expr expr) => expr switch
    {
        Expr.Variable v when v.Name.Lexeme == "globalThis" => true,
        Expr.Get g when g.Name.Lexeme == "globalThis" => IsGlobalThisExpression(g.Object),
        _ => false
    };

    /// <summary>
    /// Tries to emit IL for globalThis chained property access like globalThis.Math.PI, globalThis.console.log, etc.
    /// Returns true if the property was handled.
    /// </summary>
    private bool TryEmitGlobalThisChainedProperty(Expr.Get g)
    {
        // Pattern: globalThis.Math.PI, globalThis.globalThis.Math.PI, etc.
        // g.Object is Expr.Get { Object: <globalThis-expression>, Name: "Math/JSON/console/etc" }
        // g.Name.Lexeme is "PI/parse/log/etc"

        if (g.Object is not Expr.Get innerGet)
            return false;

        if (!IsGlobalThisExpression(innerGet.Object))
            return false;

        string namespaceName = innerGet.Name.Lexeme;
        string propertyName = g.Name.Lexeme;

        // Handle globalThis.globalThis.X case (self-reference chain)
        if (namespaceName == "globalThis")
        {
            var selfStrategy = _ctx.TypeEmitterRegistry?.GetStaticStrategy("globalThis");
            if (selfStrategy != null && selfStrategy.TryEmitStaticPropertyGet(this, propertyName))
            {
                SetStackUnknown();
                return true;
            }
        }

        // Try to use the static emitter for the inner namespace
        var staticStrategy = _ctx.TypeEmitterRegistry?.GetStaticStrategy(namespaceName);
        if (staticStrategy != null && staticStrategy.TryEmitStaticPropertyGet(this, propertyName))
        {
            SetStackUnknown();
            return true;
        }

        return false;
    }
}
