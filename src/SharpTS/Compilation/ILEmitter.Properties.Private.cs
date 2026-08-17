using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    #region ES2022 Private Class Elements

    /// <summary>
    /// Emits IL for ES2022 private field access (obj.#field).
    /// Supports both instance private fields (via ConditionalWeakTable) and static private fields.
    /// </summary>
    protected override void EmitGetPrivate(Expr.GetPrivate gp)
    {
        // Get the field name without the # prefix
        string fieldName = gp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        // Check if this is accessing the current class (this.#field or ClassName.#field)
        string? className = _ctx.CurrentClassName;
        if (className == null)
        {
            // Fallback: throw runtime error if context isn't set up
            IL.Emit(OpCodes.Ldstr, $"Cannot access private field '#{fieldName}' - class context not available");
            IL.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            IL.Emit(OpCodes.Throw);
            return;
        }
        className = _ctx.ResolvePrivateFieldOwner(className, fieldName);

        // Check if it's a static private field access (ClassName.#field)
        if ((gp.Object is Expr.Variable classVar && classVar.Name.Lexeme == _ctx.CurrentClassShortName)
            || (gp.Object is Expr.This && !_ctx.IsInstanceMethod))
        {
            // Try static private field
            if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
            {
                if (gp.Object is Expr.This)
                    EmitStaticPrivateReceiverBrandCheck(className, fieldName);
                IL.Emit(OpCodes.Ldsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Instance private field access (this.#field or other.#field)
        var storageField = _ctx.ClassRegistry!.GetPrivateFieldStorage(className);
        if (storageField != null)
        {
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);

            // Declare local for the dictionary result
            var dictLocal = IL.DeclareLocal(dictType);

            // Load __privateFields static field
            IL.Emit(OpCodes.Ldsfld, storageField);

            // Emit the object expression
            EmitExpression(gp.Object);
            EmitBoxIfNeeded(gp.Object);

            // Load address of dictLocal for out parameter
            IL.Emit(OpCodes.Ldloca, dictLocal);

            // Call TryGetValue(object key, out TValue value)
            var tryGetValueMethod = Types.GetMethod(
                cwtType,
                "TryGetValue",
                typeof(object),
                dictType.MakeByRefType());
            IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

            // If false, throw TypeError (brand check failed)
            var successLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, successLabel);

            // Brand check failed - throw TypeError
            GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime!,
                $"Cannot read private member #{fieldName} from an object whose class did not declare it");

            IL.MarkLabel(successLabel);

            // Access dictionary: dictLocal[fieldName]
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, fieldName);
            IL.Emit(OpCodes.Callvirt, dictType.GetMethod("get_Item", [typeof(string)])!);

            SetStackUnknown();
            return;
        }

        // Fallback: check for static private field (covers ClassName.#staticField case)
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var fallbackStaticField))
        {
            IL.Emit(OpCodes.Ldsfld, fallbackStaticField!);
            SetStackUnknown();
            return;
        }

        // No private field found
        GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime!,
            $"Private field '#{fieldName}' not found in class '{className}'");
    }

    /// <summary>
    /// Emits IL for ES2022 private field assignment (obj.#field = value).
    /// </summary>
    protected override void EmitSetPrivate(Expr.SetPrivate sp)
    {
        // Get the field name without the # prefix
        string fieldName = sp.Name.Lexeme;
        if (fieldName.StartsWith('#'))
            fieldName = fieldName[1..];

        string? className = _ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot write private field '#{fieldName}' - class context not available");
            IL.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            IL.Emit(OpCodes.Throw);
            return;
        }
        className = _ctx.ResolvePrivateFieldOwner(className, fieldName);

        // Check if it's a static private field
        if ((sp.Object is Expr.Variable classVar && classVar.Name.Lexeme == _ctx.CurrentClassShortName)
            || (sp.Object is Expr.This && !_ctx.IsInstanceMethod))
        {
            if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var staticField))
            {
                if (sp.Object is Expr.This)
                    EmitStaticPrivateReceiverBrandCheck(className, fieldName);
                // Emit value, box, store, but also leave value on stack for expression result
                EmitExpression(sp.Value);
                EmitBoxIfNeeded(sp.Value);
                IL.Emit(OpCodes.Dup);  // Keep copy on stack for expression result
                IL.Emit(OpCodes.Stsfld, staticField!);
                SetStackUnknown();
                return;
            }
        }

        // Instance private field assignment
        var setStorageField = _ctx.ClassRegistry!.GetPrivateFieldStorage(className);
        if (setStorageField != null)
        {
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            var dictType = typeof(Dictionary<string, object?>);

            // Evaluate the private reference's base first, then the RHS, and only then perform
            // the brand check/PutValue.  The brand check is part of assignment, not reference
            // creation, so an abrupt RHS must win over a bad receiver (ECMA evaluation order).
            var dictLocal = IL.DeclareLocal(dictType);
            var objectLocal = IL.DeclareLocal(typeof(object));
            var valueLocal = IL.DeclareLocal(typeof(object));

            EmitExpression(sp.Object);
            EmitBoxIfNeeded(sp.Object);
            IL.Emit(OpCodes.Stloc, objectLocal);

            EmitExpression(sp.Value);
            EmitBoxIfNeeded(sp.Value);
            IL.Emit(OpCodes.Stloc, valueLocal);

            // Load __privateFields static field
            IL.Emit(OpCodes.Ldsfld, setStorageField);
            IL.Emit(OpCodes.Ldloc, objectLocal);

            // Load address of dictLocal for out parameter
            IL.Emit(OpCodes.Ldloca, dictLocal);

            // Call TryGetValue
            var tryGetValueMethod = Types.GetMethod(
                cwtType,
                "TryGetValue",
                typeof(object),
                dictType.MakeByRefType());
            IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

            // If false, throw TypeError
            var successLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue, successLabel);

            // Brand check failed
            GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime!,
                $"Cannot write private member #{fieldName} to an object whose class did not declare it");

            IL.MarkLabel(successLabel);

            // Set dictionary: dictLocal[fieldName] = value
            IL.Emit(OpCodes.Ldloc, dictLocal);
            IL.Emit(OpCodes.Ldstr, fieldName);
            IL.Emit(OpCodes.Ldloc, valueLocal);
            IL.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item", [typeof(string), typeof(object)])!);

            // Leave value on stack for expression result
            IL.Emit(OpCodes.Ldloc, valueLocal);
            SetStackUnknown();
            return;
        }

        // Fallback for static private field
        if (_ctx.ClassRegistry!.TryGetStaticPrivateField(className, fieldName, out var setFallbackStaticField))
        {
            EmitExpression(sp.Value);
            EmitBoxIfNeeded(sp.Value);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Stsfld, setFallbackStaticField!);
            SetStackUnknown();
            return;
        }

        // No private field found
        GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime!,
            $"Private field '#{fieldName}' not found in class '{className}'");
    }

    /// <summary>
    /// Emits IL for ES2022 private method call (obj.#method()).
    /// </summary>
    protected override void EmitCallPrivate(Expr.CallPrivate cp)
    {
        // Get the method name without the # prefix
        string methodName = cp.Name.Lexeme;
        if (methodName.StartsWith('#'))
            methodName = methodName[1..];

        string? className = _ctx.CurrentClassName;
        if (className == null)
        {
            IL.Emit(OpCodes.Ldstr, $"Cannot call private method '#{methodName}' - class context not available");
            IL.Emit(OpCodes.Newobj, Types.InvalidOperationExceptionCtorString);
            IL.Emit(OpCodes.Throw);
            return;
        }
        className = _ctx.ResolvePrivateMethodOwner(className, methodName);

        // Check for static private method (ClassName.#method())
        if ((cp.Object is Expr.Variable classVar && classVar.Name.Lexeme == _ctx.CurrentClassShortName)
            || (cp.Object is Expr.This && !_ctx.IsInstanceMethod))
        {
            if (_ctx.ClassRegistry!.TryGetStaticPrivateMethod(className, methodName, out var staticMethod))
            {
                if (cp.Object is Expr.This)
                    EmitStaticPrivateReceiverBrandCheck(className, methodName);
                // Emit arguments
                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Pad omitted trailing arguments with `undefined` (fixed-arity method).
                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, staticMethod!.GetParameters().Length);

                // Call static method
                IL.Emit(OpCodes.Call, staticMethod!);
                SetStackUnknown();
                return;
            }
        }

        // Instance private method call (this.#method() or other.#method())
        if (_ctx.ClassRegistry!.TryGetPrivateMethod(className, methodName, out var instanceMethod))
        {
            // For instance methods, we need to verify the brand (that the object has this class's private slots)
            // We can check via the ConditionalWeakTable
            var callStorageField = _ctx.ClassRegistry!.GetPrivateFieldStorage(className);
            if (callStorageField != null)
            {
                var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
                var dictType = typeof(Dictionary<string, object?>);

                // Emit object and keep a copy for method call
                EmitExpression(cp.Object);
                EmitBoxIfNeeded(cp.Object);

                // Store object in local for reuse
                var objLocal = IL.DeclareLocal(typeof(object));
                IL.Emit(OpCodes.Stloc, objLocal);

                // Brand check: verify object is in the ConditionalWeakTable
                var dictLocal = IL.DeclareLocal(dictType);
                IL.Emit(OpCodes.Ldsfld, callStorageField);
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Ldloca, dictLocal);
                var tryGetValueMethod = Types.GetMethod(
                    cwtType,
                    "TryGetValue",
                    typeof(object),
                    dictType.MakeByRefType());
                IL.Emit(OpCodes.Callvirt, tryGetValueMethod);

                var validLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Brtrue, validLabel);

                // Brand check failed
                GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime!,
                    $"Cannot call private method #{methodName} on an object whose class did not declare it");

                IL.MarkLabel(validLabel);

                // Load receiver (cast to class type)
                IL.Emit(OpCodes.Ldloc, objLocal);
                if (_ctx.ClassRegistry.TryGetClass(className, out var privateOwnerBuilder)
                    && privateOwnerBuilder != null)
                {
                    IL.Emit(OpCodes.Castclass, privateOwnerBuilder);
                }

                // Emit arguments
                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Pad omitted trailing arguments with `undefined` (fixed-arity method).
                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, instanceMethod!.GetParameters().Length);

                // Call instance method
                IL.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
            else
            {
                // No private fields, so no brand check needed (class has only private methods)
                EmitExpression(cp.Object);
                EmitBoxIfNeeded(cp.Object);
                if (_ctx.ClassRegistry.TryGetClass(className, out var privateOwnerBuilder)
                    && privateOwnerBuilder != null)
                {
                    IL.Emit(OpCodes.Castclass, privateOwnerBuilder);
                }

                foreach (var arg in cp.Arguments)
                {
                    EmitExpression(arg);
                    EmitBoxIfNeeded(arg);
                }

                // Pad omitted trailing arguments with `undefined` (fixed-arity method).
                EmitPrivateCallUndefinedPadding(cp.Arguments.Count, instanceMethod!.GetParameters().Length);
                IL.Emit(OpCodes.Callvirt, instanceMethod!);
                SetStackUnknown();
                return;
            }
        }

        // Fallback: check for static private method
        if (_ctx.ClassRegistry!.TryGetStaticPrivateMethod(className, methodName, out var fallbackStaticMethod))
        {
            foreach (var arg in cp.Arguments)
            {
                EmitExpression(arg);
                EmitBoxIfNeeded(arg);
            }

            EmitPrivateCallUndefinedPadding(cp.Arguments.Count, fallbackStaticMethod!.GetParameters().Length);
            IL.Emit(OpCodes.Call, fallbackStaticMethod!);
            SetStackUnknown();
            return;
        }

        // No private method found
        GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime!,
            $"Private method '#{methodName}' not found in class '{className}'");
    }

    private void EmitStaticPrivateReceiverBrandCheck(string className, string memberName)
    {
        if (_ctx.Runtime?.CurrentFunctionThisField == null
            || _ctx.ClassRegistry?.TryGetClass(className, out var ownerBuilder) != true
            || ownerBuilder == null)
            return;

        var receiverReady = IL.DefineLabel();
        var valid = IL.DefineLabel();

        // Direct statically-bound calls do not publish a dynamic receiver; use the
        // lexical owner in that case. Value calls (including through Proxy) publish
        // their actual receiver through InvokeWithThis and must pass the private brand.
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.CurrentFunctionThisField);
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brtrue, receiverReady);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldtoken, ownerBuilder);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.MarkLabel(receiverReady);

        IL.Emit(OpCodes.Ldtoken, ownerBuilder);
        IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
        IL.Emit(OpCodes.Ceq);
        IL.Emit(OpCodes.Brtrue, valid);
        GuestErrorEmitter.ThrowTypeError(IL, _ctx.Runtime,
            $"Cannot access private member #{memberName} from an object whose class did not declare it");
        IL.MarkLabel(valid);
    }

    #endregion
}
