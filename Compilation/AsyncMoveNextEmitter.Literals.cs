using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitArrayLiteral(Expr.ArrayLiteral a)
    {
        // Check if any element is a spread
        bool hasSpreads = a.Elements.Any(e => e is Expr.Spread);

        if (!hasSpreads)
        {
            // Simple case: no spreads, just create array directly
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);
                EmitExpression(a.Elements[i]);
                EnsureBoxed();
                _il.Emit(OpCodes.Stelem_Ref);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
        }
        else
        {
            // Complex case: has spreads, use ConcatArrays
            _il.Emit(OpCodes.Ldc_I4, a.Elements.Count);
            _il.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < a.Elements.Count; i++)
            {
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Ldc_I4, i);

                if (a.Elements[i] is Expr.Spread spread)
                {
                    EmitExpression(spread.Expression);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldc_I4, 1);
                    _il.Emit(OpCodes.Newarr, typeof(object));
                    _il.Emit(OpCodes.Dup);
                    _il.Emit(OpCodes.Ldc_I4_0);
                    EmitExpression(a.Elements[i]);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Stelem_Ref);
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateArray);
                }

                _il.Emit(OpCodes.Stelem_Ref);
            }

            // Pass Symbol.iterator and runtimeType for iterator protocol support
            _il.Emit(OpCodes.Ldsfld, _ctx!.Runtime!.SymbolIterator);
            _il.Emit(OpCodes.Ldtoken, _ctx!.Runtime!.RuntimeType);
            _il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConcatArrays);
        }
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
            // Simple case: no spreads, no computed keys
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);
                EmitStaticPropertyKey(prop.Key!);
                EmitExpression(prop.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        else
        {
            // Complex case: has spreads or computed keys, use SetIndex
            _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([])!);

            foreach (var prop in o.Properties)
            {
                _il.Emit(OpCodes.Dup);

                if (prop.IsSpread)
                {
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.MergeIntoObject);
                }
                else if (prop.Key is Expr.ComputedKey ck)
                {
                    // Computed key: evaluate key expression and use SetIndex
                    EmitExpression(ck.Expression);
                    EnsureBoxed();
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
                }
                else
                {
                    // Static key: set directly
                    EmitStaticPropertyKey(prop.Key!);
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item")!);
                }
            }

            _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateObject);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Emits an object literal that has getter/setter accessors in async context.
    /// Uses the $Object type which supports DefineGetter/DefineSetter.
    /// </summary>
    private void EmitObjectLiteralWithAccessors(Expr.ObjectLiteral o)
    {
        // Create $Object: new $Object(new Dictionary<string, object?>())
        _il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor([])!);
        _il.Emit(OpCodes.Newobj, _ctx!.Runtime!.TSObjectCtor);

        // Store in local for repeated use
        var objLocal = _il.DeclareLocal(_ctx!.Runtime!.TSObjectType);
        _il.Emit(OpCodes.Stloc, objLocal);

        foreach (var prop in o.Properties)
        {
            if (prop.IsSpread)
            {
                // Spread: merge the source object's properties into target $Object
                _il.Emit(OpCodes.Ldloc, objLocal);
                EmitExpression(prop.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.MergeIntoTSObject);
                continue;
            }

            string propKey = GetPropertyKeyString(prop.Key!);

            switch (prop.Kind)
            {
                case Expr.ObjectPropertyKind.Getter:
                    // obj.DefineGetter(name, getterFunction)
                    _il.Emit(OpCodes.Ldloc, objLocal);
                    _il.Emit(OpCodes.Ldstr, propKey);
                    EmitExpression(prop.Value); // Emits the getter function (arrow function)
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, _ctx!.Runtime!.TSObjectDefineGetter);
                    break;

                case Expr.ObjectPropertyKind.Setter:
                    // obj.DefineSetter(name, setterFunction)
                    _il.Emit(OpCodes.Ldloc, objLocal);
                    _il.Emit(OpCodes.Ldstr, propKey);
                    EmitExpression(prop.Value); // Emits the setter function (arrow function)
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, _ctx!.Runtime!.TSObjectDefineSetter);
                    break;

                case Expr.ObjectPropertyKind.Method:
                case Expr.ObjectPropertyKind.Value:
                default:
                    // Regular property: obj.SetProperty(name, value)
                    _il.Emit(OpCodes.Ldloc, objLocal);
                    _il.Emit(OpCodes.Ldstr, propKey);
                    EmitExpression(prop.Value);
                    EnsureBoxed();
                    _il.Emit(OpCodes.Callvirt, _ctx!.Runtime!.TSObjectSetProperty);
                    break;
            }
        }

        // Leave the $Object on the stack
        _il.Emit(OpCodes.Ldloc, objLocal);
        SetStackUnknown();
    }

    /// <summary>
    /// Extracts the string key from a property key expression.
    /// </summary>
    private static string GetPropertyKeyString(Expr.PropertyKey key)
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
    private void EmitStaticPropertyKey(Expr.PropertyKey key)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                _il.Emit(OpCodes.Ldstr, ik.Name.Lexeme);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.STRING:
                _il.Emit(OpCodes.Ldstr, (string)lk.Literal.Literal!);
                break;
            case Expr.LiteralKey lk when lk.Literal.Type == TokenType.NUMBER:
                // Number keys are converted to strings in JS/TS
                _il.Emit(OpCodes.Ldstr, lk.Literal.Literal!.ToString()!);
                break;
            default:
                throw new CompileException($"Unexpected static property key type: {key.GetType().Name}");
        }
    }

    protected override void EmitGetIndex(Expr.GetIndex gi)
    {
        EmitExpression(gi.Object);
        EnsureBoxed();
        EmitExpression(gi.Index);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetIndex);
        SetStackUnknown();
    }

    protected override void EmitSetIndex(Expr.SetIndex si)
    {
        EmitExpression(si.Object);
        EnsureBoxed();
        EmitExpression(si.Index);
        EnsureBoxed();
        EmitExpression(si.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.SetIndex);
        SetStackUnknown();
    }
}
