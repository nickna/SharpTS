using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        // Typed array optimization: emit List<double> or List<bool> for empty typed arrays.
        // Only for empty arrays (populated via index assignment) to avoid issues with
        // array methods (flatMap, map, etc.) that expect List<object?>.
        if (!hasSpreads && a.Elements.Count == 0)
        {
            var desc = ArrayElements.Resolve(_ctx.TypeMap?.Get(a));
            if (desc != null && desc.Kind != ArrayElementsKind.Object)
            {
                EmitTypedArrayLiteral(a, desc);
                return;
            }
        }

        if (!hasSpreads)
        {
            // Simple case: no spreads, just create array directly
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                if (a.IsHole(i))
                {
                    // Elided position → true ECMA-262 hole, not an undefined element.
                    IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.ArrayHoleInstance);
                }
                else
                {
                    EmitExpression(a.Elements[i]);
                    EmitBoxIfNeeded(a.Elements[i]);
                }
                IL.Emit(OpCodes.Stelem_Ref);
            }

            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
        }
        else
        {
            // Complex case: has spreads, use ConcatArrays
            // Build array of arrays/elements to concat
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread spread)
                {
                    // Spread: emit the array directly
                    EmitExpression(spread.Expression);
                    EmitBoxIfNeeded(spread.Expression);
                }
                else
                {
                    // Non-spread: wrap in single-element array
                    IL.Emit(OpCodes.Ldc_I4, 1);
                    IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, 0);
                    if (a.IsHole(i))
                    {
                        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.ArrayHoleInstance);
                    }
                    else
                    {
                        EmitExpression(a.Elements[i]);
                        EmitBoxIfNeeded(a.Elements[i]);
                    }
                    IL.Emit(OpCodes.Stelem_Ref);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateArray);
                }

                IL.Emit(OpCodes.Stelem_Ref);
            }

            // Pass Symbol.iterator and runtimeType for iterator protocol support
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.SymbolIterator);
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.RuntimeType);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ConcatArrays);
        }
        // The stack now holds a List<object?> reference. Reset the stack-type
        // tracker so a subsequent EmitBoxIfNeeded doesn't reinterpret the
        // reference as whatever primitive the previous expression left behind.
        SetStackUnknown();
    }

    protected override void EmitObjectLiteral(Expr.ObjectLiteral o)
    {
        // Check if any property is a spread, computed key, or accessor (getter/setter)
        bool hasSpreads = o.Properties.Any(p => p.IsSpread);
        bool hasComputedKeys = o.Properties.Any(p => p.Key is Expr.ComputedKey);
        bool hasAccessors = o.Properties.Any(p => p.Kind is Expr.ObjectPropertyKind.Getter or Expr.ObjectPropertyKind.Setter);

        if (hasAccessors)
        {
            // Object has getters/setters - use $Object type which supports accessors
            EmitObjectLiteralWithAccessors(o);
        }
        else if (!hasSpreads && !hasComputedKeys)
        {
            // Simple case: no spreads, no computed keys. Drop the legacy
            // `Call CreateObject` no-op identity helper. Pre-size the
            // dictionary only when property count > 3: .NET's
            // Dictionary<,> default-ctors a capacity-0 instance that
            // lazily allocates buckets/entries on the first set_Item, then
            // resizes through prime sequence 3 → 7 → … Pre-sizing wins
            // when we know we'll exceed capacity 3 (i.e. ≥ 4 props),
            // saving a resize. For smaller literals the default ctor is
            // actually cheaper since the lazy entries-array allocation
            // matches what we'd pre-size to anyway, and the extra IL +
            // capacity ctor work measurably regresses small-object hot
            // loops.
            if (o.Properties.Count > 3)
            {
                IL.Emit(OpCodes.Ldc_I4, o.Properties.Count);
                IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject, _ctx.Types.Int32));
            }
            else
            {
                IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject));
            }

            foreach (var prop in o.Properties)
            {
                IL.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EmitBoxIfNeeded(prop.Value);
                IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));
            }
        }
        else
        {
            // Complex case: has spreads or computed keys, use Dictionary<string, object?> and SetIndex.
            // Pre-size only when Properties.Count > 3 (matches simple-case
            // heuristic); spreads may add more, but the resize then is
            // hard to avoid without runtime info.
            if (o.Properties.Count > 3)
            {
                IL.Emit(OpCodes.Ldc_I4, o.Properties.Count);
                IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject, _ctx.Types.Int32));
            }
            else
            {
                IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject));
            }

            foreach (var prop in o.Properties)
            {
                IL.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    // Spread: merge the object into target
                    EmitExpression(prop.Value);
                    EmitBoxIfNeeded(prop.Value);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.MergeIntoObject);
                }
                else if (prop.Key is Expr.ComputedKey ck)
                {
                    // Computed key: evaluate key expression and use SetIndex
                    EmitExpression(ck.Expression);
                    EmitBoxIfNeeded(ck.Expression);
                    EmitExpression(prop.Value);
                    EmitBoxIfNeeded(prop.Value);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
                }
                else
                {
                    // Static key: set directly
                    EmitStaticPropertyKey(prop.Key!);
                    EmitExpression(prop.Value);
                    EmitBoxIfNeeded(prop.Value);
                    IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));
                }
            }

            // Result is already Dictionary<string, object?>, no CreateObject needed
        }
        // The stack now holds a Dictionary reference (or $Object wrapper).
        // Reset the stack-type tracker so a subsequent EmitBoxIfNeeded doesn't
        // reinterpret the reference as whatever primitive the previous
        // expression left behind — e.g. `const x = 0; const y = {};` used to
        // emit `Box Double` on the fresh Dictionary pointer, producing a
        // double whose bits were the reinterpreted heap address.
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an object literal that has getter/setter accessors.
    /// Uses the $Object type which supports DefineGetter/DefineSetter.
    /// </summary>
    protected override void EmitObjectLiteralWithAccessors(Expr.ObjectLiteral o)
    {
        // Create $Object: new $Object(new Dictionary<string, object?>())
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.DictionaryStringObject));
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSObjectCtor);

        // Store in local for repeated use
        var objLocal = IL.DeclareLocal(_ctx.Runtime!.TSObjectType);
        IL.Emit(OpCodes.Stloc, objLocal);

        foreach (var prop in o.Properties)
        {
            if (prop.IsSpread)
            {
                // Spread: merge the source object's properties into target $Object
                IL.Emit(OpCodes.Ldloc, objLocal);
                EmitExpression(prop.Value);
                EmitBoxIfNeeded(prop.Value);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.MergeIntoTSObject);
                continue;
            }

            // Computed-key accessors handle their own key emission below;
            // static-key path needs the resolved string up front.
            string propKey = prop.Key is Expr.ComputedKey
                ? "" // unused — accessor branches below dispatch on prop.Key
                : GetPropertyKeyString(prop.Key!);

            switch (prop.Kind)
            {
                case Expr.ObjectPropertyKind.Getter:
                    if (prop.Key is Expr.ComputedKey gck)
                    {
                        // Computed-key getter (Symbol or runtime string).
                        // $Runtime.DefineSymbolAccessor(obj, key, getter, null).
                        IL.Emit(OpCodes.Ldloc, objLocal);
                        EmitExpression(gck.Expression);
                        EmitBoxIfNeeded(gck.Expression);
                        EmitExpression(prop.Value);
                        EmitBoxIfNeeded(prop.Value);
                        IL.Emit(OpCodes.Ldnull);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.DefineSymbolAccessor);
                    }
                    else
                    {
                        // obj.DefineGetter(name, getterFunction)
                        IL.Emit(OpCodes.Ldloc, objLocal);
                        IL.Emit(OpCodes.Ldstr, propKey);
                        EmitExpression(prop.Value); // Emits the getter function (arrow function)
                        EmitBoxIfNeeded(prop.Value);
                        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSObjectDefineGetter);
                    }
                    break;

                case Expr.ObjectPropertyKind.Setter:
                    if (prop.Key is Expr.ComputedKey sck)
                    {
                        IL.Emit(OpCodes.Ldloc, objLocal);
                        EmitExpression(sck.Expression);
                        EmitBoxIfNeeded(sck.Expression);
                        IL.Emit(OpCodes.Ldnull);
                        EmitExpression(prop.Value);
                        EmitBoxIfNeeded(prop.Value);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.DefineSymbolAccessor);
                    }
                    else
                    {
                        // obj.DefineSetter(name, setterFunction)
                        IL.Emit(OpCodes.Ldloc, objLocal);
                        IL.Emit(OpCodes.Ldstr, propKey);
                        EmitExpression(prop.Value); // Emits the setter function (arrow function)
                        EmitBoxIfNeeded(prop.Value);
                        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSObjectDefineSetter);
                    }
                    break;

                case Expr.ObjectPropertyKind.Method:
                case Expr.ObjectPropertyKind.Value:
                default:
                    if (prop.Key is Expr.ComputedKey vck)
                    {
                        // Computed-key data property: route through $Runtime.SetIndex
                        // so symbol keys hit the symbol-dict.
                        IL.Emit(OpCodes.Ldloc, objLocal);
                        EmitExpression(vck.Expression);
                        EmitBoxIfNeeded(vck.Expression);
                        EmitExpression(prop.Value);
                        EmitBoxIfNeeded(prop.Value);
                        IL.Emit(OpCodes.Call, _ctx.Runtime!.SetIndex);
                    }
                    else
                    {
                        // Regular property: obj.SetProperty(name, value)
                        IL.Emit(OpCodes.Ldloc, objLocal);
                        IL.Emit(OpCodes.Ldstr, propKey);
                        EmitExpression(prop.Value);
                        EmitBoxIfNeeded(prop.Value);
                        IL.Emit(OpCodes.Callvirt, _ctx.Runtime!.TSObjectSetProperty);
                    }
                    break;
            }
        }

        // Leave the $Object on the stack. Reset the stack-type tracker —
        // see EmitObjectLiteral for the rationale.
        IL.Emit(OpCodes.Ldloc, objLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// Extracts the string key from a property key expression.
    /// </summary>
    private static new string GetPropertyKeyString(Expr.PropertyKey key)
    {
        return key switch
        {
            Expr.IdentifierKey ik => ik.Name.Lexeme,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING => (string)lk.Literal.Literal!,
            Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER => lk.Literal.Literal!.ToString()!,
            Expr.ComputedKey => throw new CompileException("Computed keys not supported in accessor context"),
            _ => throw new CompileException($"Unexpected property key type: {key.GetType().Name}")
        };
    }

    /// <summary>
    /// Emits a static property key (identifier, string literal, or number literal) as a string.
    /// </summary>
    protected override void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                IL.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                IL.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                // Number keys are converted to strings in JS/TS
                IL.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new CompileException($"Unexpected static property key type: {key.GetType().Name}");
        }
    }

    /// <summary>
    /// Emits a typed array literal using the given descriptor.
    /// Eliminates element boxing by using List&lt;double&gt; or List&lt;bool&gt; directly.
    /// </summary>
    private void EmitTypedArrayLiteral(Expr.ArrayLiteral a, ArrayElementsDescriptor desc)
    {
        var listType = desc.GetListType(_ctx.Types);
        var elemType = desc.GetElementType(_ctx.Types);

        if (a.Elements.Count == 0)
        {
            IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(listType));
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(listType, _ctx.Types.Int32));

            var addMethod = _ctx.Types.GetMethod(listType, "Add", elemType);
            for (int i = 0; i < a.Elements.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                if (desc.Kind == ArrayElementsKind.Double)
                    EmitExpressionAsDouble(a.Elements[i]);
                else
                {
                    EmitExpression(a.Elements[i]);
                    EnsureBoolean();
                }
                IL.Emit(OpCodes.Callvirt, addMethod);
            }
        }

        SetStackUnknown();
    }
}
