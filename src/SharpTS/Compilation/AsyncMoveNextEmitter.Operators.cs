using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        _helpers.EmitNullishCoalescing(
            () => EmitExpression(nc.Left),
            () => EmitExpression(nc.Right));
    }

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // TemplateLiteral has Strings (literal parts) and Expressions (interpolated parts)
        // Structure: strings[0] + expressions[0] + strings[1] + expressions[1] + ... + strings[n]
        if (tl.Strings.Count == 0 && tl.Expressions.Count == 0)
        {
            _il.Emit(OpCodes.Ldstr, "");
            SetStackType(StackType.String);
            return;
        }

        // In async context, expressions may contain await which clears the stack.
        // Phase 1: Evaluate all expressions to temps first (awaits happen here)
        var exprTemps = new List<LocalBuilder>();
        for (int i = 0; i < tl.Expressions.Count; i++)
        {
            // SpillBoxed registers each temp so an await in a later interpolation persists
            // the earlier ones across the suspension (#400).
            exprTemps.Add(SpillBoxed(tl.Expressions[i]));
        }

        // Phase 2: Build string from temps (no awaits, stack safe)
        _il.Emit(OpCodes.Ldstr, tl.Strings[0]);

        for (int i = 0; i < exprTemps.Count; i++)
        {
            // Load expression value from temp and convert to string.
            // StringifyCoerce: interpolation is an implicit ToString coercion —
            // Symbol parts throw TypeError (ECMA-262 §7.1.17).
            _il.Emit(OpCodes.Ldloc, exprTemps[i]);
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.StringifyCoerce);
            _il.Emit(OpCodes.Call, Types.StringConcat2);

            // Emit next string part
            if (i + 1 < tl.Strings.Count)
            {
                _il.Emit(OpCodes.Ldstr, tl.Strings[i + 1]);
                _il.Emit(OpCodes.Call, Types.StringConcat2);
            }
        }
        _helpers.StackType = StackType.String;
    }

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // Spill the tag and interpolated expressions first: an await inside any of them
        // must suspend with an empty stack (same two-phase pattern as EmitTemplateLiteral).
        var tagLocal = SpillBoxed(ttl.Tag);
        var exprLocals = ttl.Expressions.Select(SpillBoxed).ToList();

        // 1. Load the tag function reference
        _il.Emit(OpCodes.Ldloc, tagLocal);

        // 2. Create cooked strings array (object?[] to allow null for invalid escapes)
        _il.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
            {
                _il.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
            }
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 3. Create raw strings array
        _il.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        _il.Emit(OpCodes.Newarr, typeof(string));
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 4. Create expressions array (from the pre-spilled locals)
        _il.Emit(OpCodes.Ldc_I4, exprLocals.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < exprLocals.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            _il.Emit(OpCodes.Ldloc, exprLocals[i]);
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // 5. Call runtime helper: InvokeTaggedTemplate(tag, cooked, raw, exprs)
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeTaggedTemplate);
        SetStackUnknown();
    }

    // EmitSet is inherited from ExpressionEmitterBase: it covers the same cases
    // (globalThis, static fields, registry, dynamic SetProperty) plus CommonJS
    // module.exports, and spills operands so awaits inside Value are suspension-safe.

    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // Get the field name without the # prefix
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        string? className = _ctx?.CurrentClassName;
        if (className == null)
        {
            _il.Emit(OpCodes.Ldstr, $"Cannot access private field '#{fieldName}' - class context not available");
            _il.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            _il.Emit(OpCodes.Throw);
            return;
        }
        className = _ctx!.ResolvePrivateFieldOwner(className, fieldName);

        // Static private field: ClassName.#field
        if (((gp.Object is Expr.Variable classVar &&
              classVar.Name.Lexeme == _ctx!.CurrentClassShortName)
             || (gp.Object is Expr.This && !_ctx!.IsInstanceMethod)) &&
            _ctx!.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
        {
            _il.Emit(OpCodes.Ldsfld, staticField!);
            SetStackUnknown();
            return;
        }

        // Instance private field via class private storage (brand check + dictionary lookup)
        var storageField = _ctx!.ClassRegistry!.GetPrivateFieldStorage(className);
        if (storageField != null)
        {
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);
            var dictLocal = _il.DeclareLocal(dictType);

            // Spill the object so an await inside it doesn't suspend with the storage field on the stack.
            var objLocal = SpillBoxed(gp.Object);
            _il.Emit(OpCodes.Ldsfld, storageField);
            _il.Emit(OpCodes.Ldloc, objLocal);
            _il.Emit(OpCodes.Ldloca, dictLocal);
            var tryGetValueMethod = Types.GetMethod(cwtType, "TryGetValue", typeof(object), dictType.MakeByRefType());
            _il.Emit(OpCodes.Callvirt, tryGetValueMethod);

            var successLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Brtrue, successLabel);
            GuestErrorEmitter.ThrowTypeError(_il, _ctx.Runtime!,
                $"Cannot read private member #{fieldName} from an object whose class did not declare it");
            _il.MarkLabel(successLabel);

            _il.Emit(OpCodes.Ldloc, dictLocal);
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Callvirt, dictType.GetMethod("get_Item", [typeof(string)])!);
            SetStackUnknown();
            return;
        }

        // Fallback static private field lookup
        if (_ctx!.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var fallbackStaticField))
        {
            _il.Emit(OpCodes.Ldsfld, fallbackStaticField!);
            SetStackUnknown();
            return;
        }

        GuestErrorEmitter.ThrowTypeError(_il, _ctx.Runtime!,
            $"Private field '#{fieldName}' not found in class '{className}'");
    }

    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        // Get the field name without the # prefix
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        string? className = _ctx?.CurrentClassName;
        if (className == null)
        {
            _il.Emit(OpCodes.Ldstr, $"Cannot write private field '#{fieldName}' - class context not available");
            _il.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            _il.Emit(OpCodes.Throw);
            return;
        }
        className = _ctx!.ResolvePrivateFieldOwner(className, fieldName);

        // Static private field assignment: ClassName.#field = value
        if (((sp.Object is Expr.Variable classVar &&
              classVar.Name.Lexeme == _ctx!.CurrentClassShortName)
             || (sp.Object is Expr.This && !_ctx!.IsInstanceMethod)) &&
            _ctx!.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
        {
            EmitExpression(sp.Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Stsfld, staticField!);
            SetStackUnknown();
            return;
        }

        // Instance private field assignment via class private storage
        var storageField = _ctx!.ClassRegistry!.GetPrivateFieldStorage(className);
        if (storageField != null)
        {
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);
            var dictLocal = _il.DeclareLocal(dictType);
            var valueLocal = _il.DeclareLocal(typeof(object));

            // Spill the object so an await inside it doesn't suspend with the storage field on the stack.
            var objLocal = SpillBoxed(sp.Object);
            _il.Emit(OpCodes.Ldsfld, storageField);
            _il.Emit(OpCodes.Ldloc, objLocal);
            _il.Emit(OpCodes.Ldloca, dictLocal);
            var tryGetValueMethod = Types.GetMethod(cwtType, "TryGetValue", typeof(object), dictType.MakeByRefType());
            _il.Emit(OpCodes.Callvirt, tryGetValueMethod);

            var successLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Brtrue, successLabel);
            GuestErrorEmitter.ThrowTypeError(_il, _ctx.Runtime!,
                $"Cannot write private member #{fieldName} to an object whose class did not declare it");
            _il.MarkLabel(successLabel);

            EmitExpression(sp.Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Stloc, valueLocal);

            _il.Emit(OpCodes.Ldloc, dictLocal);
            _il.Emit(OpCodes.Ldstr, fieldName);
            _il.Emit(OpCodes.Ldloc, valueLocal);
            _il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!);
            _il.Emit(OpCodes.Ldloc, valueLocal);
            SetStackUnknown();
            return;
        }

        // Fallback static private field assignment
        if (_ctx!.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var fallbackStaticField))
        {
            EmitExpression(sp.Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Stsfld, fallbackStaticField!);
            SetStackUnknown();
            return;
        }

        GuestErrorEmitter.ThrowTypeError(_il, _ctx.Runtime!,
            $"Private field '#{fieldName}' not found in class '{className}'");
    }

    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        // Get the method name without the # prefix
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        string? className = _ctx?.CurrentClassName;
        if (className == null)
        {
            _il.Emit(OpCodes.Ldstr, $"Cannot call private method '#{methodName}' - class context not available");
            _il.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            _il.Emit(OpCodes.Throw);
            return;
        }
        className = _ctx!.ResolvePrivateMethodOwner(className, methodName);

        // Static private method: ClassName.#method(...)
        if (((cp.Object is Expr.Variable classVar &&
              classVar.Name.Lexeme == _ctx!.CurrentClassShortName)
             || (cp.Object is Expr.This && !_ctx!.IsInstanceMethod)) &&
            _ctx!.ClassRegistry!.TryGetStaticPrivateMethod(className, methodName, out var staticMethod))
        {
            // Spill args so an await inside one doesn't suspend with earlier args on the stack.
            var argLocals = cp.Arguments.Select(SpillBoxed).ToList();
            foreach (var argLocal in argLocals)
                _il.Emit(OpCodes.Ldloc, argLocal);
            EmitPrivateCallUndefinedPadding(cp.Arguments.Count, staticMethod!.GetParameters().Length);
            _il.Emit(OpCodes.Call, staticMethod!);
            SetStackUnknown();
            return;
        }

        // Instance private method with brand check via class private storage
        if (_ctx!.ClassRegistry!.TryGetPrivateMethod(className, methodName, out var instanceMethod))
        {
            var storageField = _ctx!.ClassRegistry!.GetPrivateFieldStorage(className);
            if (storageField != null)
            {
                var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
                var dictType = typeof(Dictionary<string, object?>);
                var objLocal = _il.DeclareLocal(typeof(object));
                var dictLocal = _il.DeclareLocal(dictType);

                EmitExpression(cp.Object);
                EnsureBoxed();
                _il.Emit(OpCodes.Stloc, objLocal);

                _il.Emit(OpCodes.Ldsfld, storageField);
                _il.Emit(OpCodes.Ldloc, objLocal);
                _il.Emit(OpCodes.Ldloca, dictLocal);
                var tryGetValueMethod = Types.GetMethod(cwtType, "TryGetValue", typeof(object), dictType.MakeByRefType());
                _il.Emit(OpCodes.Callvirt, tryGetValueMethod);

                var validLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Brtrue, validLabel);
                GuestErrorEmitter.ThrowTypeError(_il, _ctx.Runtime!,
                    $"Cannot call private method #{methodName} on an object whose class did not declare it");
                _il.MarkLabel(validLabel);

                // Spill args so an await inside one doesn't suspend with the receiver on the stack.
                var argLocals = cp.Arguments.Select(SpillBoxed).ToList();

                _il.Emit(OpCodes.Ldloc, objLocal);
                if (_ctx.CurrentClassBuilder != null)
                    _il.Emit(OpCodes.Castclass, _ctx.CurrentClassBuilder);

                foreach (var argLocal in argLocals)
                    _il.Emit(OpCodes.Ldloc, argLocal);

                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, instanceMethod!.GetParameters().Length);
                _il.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
            else
            {
                // No private field storage (class has private methods but no private fields)
                // Skip brand check - spill receiver and args, then emit the call directly
                var objLocal = SpillBoxed(cp.Object);
                var argLocals = cp.Arguments.Select(SpillBoxed).ToList();

                _il.Emit(OpCodes.Ldloc, objLocal);
                if (_ctx.CurrentClassBuilder != null)
                    _il.Emit(OpCodes.Castclass, _ctx.CurrentClassBuilder);

                foreach (var argLocal in argLocals)
                    _il.Emit(OpCodes.Ldloc, argLocal);

                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, instanceMethod!.GetParameters().Length);
                _il.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
        }

        GuestErrorEmitter.ThrowTypeError(_il, _ctx.Runtime!,
            $"Private method '#{methodName}' not found in class '{className}'");
    }

}
