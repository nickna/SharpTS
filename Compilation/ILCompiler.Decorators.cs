using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Decorator support for IL compilation.
/// Maps known decorators to .NET attributes and executes decorator functions at runtime.
/// </summary>
/// <remarks>
/// Decorators are handled in two ways:
/// - Known decorators (Obsolete, Serializable) are mapped to .NET attributes at compile time
/// - All decorators with runtime semantics are executed as function calls during class definition
///   (emitted as IL in the entry point, matching the interpreter's behavior)
/// </remarks>
public partial class ILCompiler
{
    private DecoratorMode _decoratorMode = DecoratorMode.None;

    /// <summary>
    /// Sets the decorator mode for compilation.
    /// </summary>
    public void SetDecoratorMode(DecoratorMode mode) => _decoratorMode = mode;

    /// <summary>
    /// Applies decorators to a class definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyClassDecorators(Stmt.Class classStmt, TypeBuilder typeBuilder)
    {
        if (classStmt.Decorators == null || classStmt.Decorators.Count == 0)
            return;

        foreach (var decorator in classStmt.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                typeBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a method definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyMethodDecorators(Stmt.Function method, MethodBuilder methodBuilder)
    {
        if (method.Decorators == null || method.Decorators.Count == 0)
            return;

        foreach (var decorator in method.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                methodBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a field definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyFieldDecorators(Stmt.Field field, FieldBuilder fieldBuilder)
    {
        if (field.Decorators == null || field.Decorators.Count == 0)
            return;

        foreach (var decorator in field.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                fieldBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a property (accessor pair) definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyAccessorDecorators(Stmt.Accessor accessor, MethodBuilder methodBuilder)
    {
        if (accessor.Decorators == null || accessor.Decorators.Count == 0)
            return;

        foreach (var decorator in accessor.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                methodBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Gets the name of a decorator from its expression.
    /// </summary>
    private static string? GetDecoratorName(Decorator decorator)
    {
        return decorator.Expression switch
        {
            Expr.Variable variable => variable.Name.Lexeme,
            Expr.Call call when call.Callee is Expr.Variable v => v.Name.Lexeme,
            Expr.Get get => get.Name.Lexeme,
            _ => null
        };
    }

    /// <summary>
    /// Checks if a method has the @lock decorator.
    /// </summary>
    private static bool HasLockDecorator(Stmt.Function method)
    {
        if (method.Decorators == null || method.Decorators.Count == 0)
            return false;

        return method.Decorators.Any(d => GetDecoratorName(d) == "lock");
    }

    /// <summary>
    /// Checks if an accessor has the @lock decorator.
    /// </summary>
    private static bool HasLockDecorator(Stmt.Accessor accessor)
    {
        if (accessor.Decorators == null || accessor.Decorators.Count == 0)
            return false;

        return accessor.Decorators.Any(d => GetDecoratorName(d) == "lock");
    }

    /// <summary>
    /// Analyzes a class to determine what lock fields are needed.
    /// </summary>
    private static (bool NeedsSyncLock, bool NeedsAsyncLock, bool NeedsStaticSyncLock, bool NeedsStaticAsyncLock) AnalyzeLockRequirements(Stmt.Class classStmt)
    {
        bool needsSyncLock = false;
        bool needsAsyncLock = false;
        bool needsStaticSyncLock = false;
        bool needsStaticAsyncLock = false;

        // Check all methods
        foreach (var method in classStmt.Methods)
        {
            if (!HasLockDecorator(method))
                continue;

            if (method.IsStatic)
            {
                if (method.IsAsync)
                    needsStaticAsyncLock = true;
                else
                    needsStaticSyncLock = true;
            }
            else
            {
                if (method.IsAsync)
                    needsAsyncLock = true;
                else
                    needsSyncLock = true;
            }
        }

        // Check accessors
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                if (!HasLockDecorator(accessor))
                    continue;

                // Accessors are never async, so only sync locks
                needsSyncLock = true;
            }
        }

        return (needsSyncLock, needsAsyncLock, needsStaticSyncLock, needsStaticAsyncLock);
    }

    // ──────────────────────────────────────────────────────────────
    //  Runtime decorator execution (emitted IL in entry point)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Known compile-time-only decorators that should NOT be executed as runtime function calls.
    /// These are handled specially by the compiler (attribute mapping, lock emission, .NET interop).
    /// </summary>
    private static readonly HashSet<string> CompileTimeOnlyDecorators = new(StringComparer.OrdinalIgnoreCase)
    {
        "lock", "DotNetType", "DotNetOverload", "Namespace",
        "Obsolete", "deprecated", "Serializable", "NonSerialized", "attribute"
    };

    private static bool IsRuntimeDecorator(Decorator decorator)
    {
        var name = GetDecoratorName(decorator);
        return name == null || !CompileTimeOnlyDecorators.Contains(name);
    }

    /// <summary>
    /// Returns true if the class or any of its members have decorators that should
    /// be executed at runtime (i.e., not purely compile-time decorators).
    /// </summary>
    private bool HasAnyRuntimeDecorators(Stmt.Class classStmt)
    {
        if (_decoratorMode == DecoratorMode.None) return false;

        if (classStmt.Decorators?.Any(IsRuntimeDecorator) == true) return true;

        foreach (var method in classStmt.Methods)
        {
            if (method.Decorators?.Any(IsRuntimeDecorator) == true) return true;
            if (_decoratorMode == DecoratorMode.Legacy)
            {
                foreach (var param in method.Parameters)
                    if (param.Decorators?.Any(IsRuntimeDecorator) == true) return true;
            }
        }

        foreach (var field in classStmt.Fields)
            if (field.Decorators?.Any(IsRuntimeDecorator) == true) return true;

        if (classStmt.Accessors != null)
            foreach (var accessor in classStmt.Accessors)
                if (accessor.Decorators?.Any(IsRuntimeDecorator) == true) return true;

        return false;
    }

    /// <summary>
    /// Emits IL to execute all runtime decorators for a class in the correct order.
    /// Called from EmitDefaultEntryPoint when a class statement with decorators is encountered.
    /// </summary>
    /// <remarks>
    /// Execution order matches the interpreter (Interpreter.Decorators.cs ApplyAllDecorators):
    /// Legacy:  parameters → methods → accessors → fields → class  (right-to-left within each)
    /// Stage 3: methods → accessors → fields → class  (right-to-left within each; no parameter decorators)
    /// </remarks>
    private void EmitRuntimeDecorators(Stmt.Class classStmt, ILEmitter emitter, ILGenerator il)
    {
        string className = _modules.CurrentDotNetNamespace != null
            ? $"{_modules.CurrentDotNetNamespace}.{classStmt.Name.Lexeme}"
            : classStmt.Name.Lexeme;

        if (!_classes.Builders.TryGetValue(className, out var classBuilder))
            return;

        // Legacy parameter decorators (right-to-left per method)
        if (_decoratorMode == DecoratorMode.Legacy)
        {
            foreach (var method in classStmt.Methods)
            {
                for (int i = method.Parameters.Count - 1; i >= 0; i--)
                {
                    var param = method.Parameters[i];
                    if (param.Decorators == null || param.Decorators.Count == 0) continue;
                    for (int d = param.Decorators.Count - 1; d >= 0; d--)
                    {
                        if (!IsRuntimeDecorator(param.Decorators[d])) continue;
                        EmitLegacyParameterDecoratorCall(param.Decorators[d], method, i, classBuilder, emitter, il);
                    }
                }
            }
        }

        // Method decorators (right-to-left per method, methods in source order)
        foreach (var method in classStmt.Methods)
        {
            if (method.Decorators == null || method.Decorators.Count == 0) continue;
            for (int d = method.Decorators.Count - 1; d >= 0; d--)
            {
                if (!IsRuntimeDecorator(method.Decorators[d])) continue;
                if (_decoratorMode == DecoratorMode.Legacy)
                    EmitLegacyMethodDecoratorCall(method.Decorators[d], method, classBuilder, emitter, il);
                else
                    EmitStage3MethodDecoratorCall(method.Decorators[d], method, emitter, il);
            }
        }

        // Accessor decorators (right-to-left)
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                if (accessor.Decorators == null || accessor.Decorators.Count == 0) continue;
                for (int d = accessor.Decorators.Count - 1; d >= 0; d--)
                {
                    if (!IsRuntimeDecorator(accessor.Decorators[d])) continue;
                    if (_decoratorMode == DecoratorMode.Legacy)
                        EmitLegacyMethodDecoratorCall(accessor.Decorators[d], accessor.Name.Lexeme, false, classBuilder, emitter, il);
                    else
                        EmitStage3MethodDecoratorCall(accessor.Decorators[d], accessor.Name.Lexeme, false, emitter, il);
                }
            }
        }

        // Field decorators (right-to-left)
        foreach (var field in classStmt.Fields)
        {
            if (field.Decorators == null || field.Decorators.Count == 0) continue;
            for (int d = field.Decorators.Count - 1; d >= 0; d--)
            {
                if (!IsRuntimeDecorator(field.Decorators[d])) continue;
                if (_decoratorMode == DecoratorMode.Legacy)
                    EmitLegacyFieldDecoratorCall(field.Decorators[d], field, classBuilder, emitter, il);
                else
                    EmitStage3FieldDecoratorCall(field.Decorators[d], field, emitter, il);
            }
        }

        // Class decorators (right-to-left)
        if (classStmt.Decorators != null)
        {
            for (int d = classStmt.Decorators.Count - 1; d >= 0; d--)
            {
                if (!IsRuntimeDecorator(classStmt.Decorators[d])) continue;
                if (_decoratorMode == DecoratorMode.Legacy)
                    EmitLegacyClassDecoratorCall(classStmt.Decorators[d], classBuilder, emitter, il);
                else
                    EmitStage3ClassDecoratorCall(classStmt.Decorators[d], classBuilder, classStmt.Name.Lexeme, emitter, il);
            }
        }
    }

    // ── Legacy decorator call emitters ───────────────────────────

    /// <summary>Emits: decorator(classType)</summary>
    private void EmitLegacyClassDecoratorCall(Decorator decorator, TypeBuilder classBuilder, ILEmitter emitter, ILGenerator il)
    {
        // Evaluate decorator expression → function value
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        // args = new object[] { classType }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldtoken, classBuilder);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Stelem_Ref);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        // InvokeMethodValue(null, func, args)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Emits: decorator(classType, propertyKey, null)</summary>
    private void EmitLegacyMethodDecoratorCall(Decorator decorator, Stmt.Function method, TypeBuilder classBuilder, ILEmitter emitter, ILGenerator il)
    {
        EmitLegacyMethodDecoratorCall(decorator, method.Name.Lexeme, method.IsStatic, classBuilder, emitter, il);
    }

    private void EmitLegacyMethodDecoratorCall(Decorator decorator, string memberName, bool isStatic, TypeBuilder classBuilder, ILEmitter emitter, ILGenerator il)
    {
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        // args = new object[] { classType, propertyKey, null }
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        // [0] = classType
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldtoken, classBuilder);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Stelem_Ref);
        // [1] = propertyKey
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldstr, memberName);
        il.Emit(OpCodes.Stelem_Ref);
        // [2] = null (descriptor)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Emits: decorator(classType, propertyKey)</summary>
    private void EmitLegacyFieldDecoratorCall(Decorator decorator, Stmt.Field field, TypeBuilder classBuilder, ILEmitter emitter, ILGenerator il)
    {
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        // args = new object[] { classType, propertyKey }
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldtoken, classBuilder);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Emits: decorator(classType, propertyKey, parameterIndex)</summary>
    private void EmitLegacyParameterDecoratorCall(Decorator decorator, Stmt.Function method, int paramIndex, TypeBuilder classBuilder, ILEmitter emitter, ILGenerator il)
    {
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        // args = new object[] { classType, propertyKey, (double)paramIndex }
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        // [0] = classType
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldtoken, classBuilder);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Stelem_Ref);
        // [1] = method name (null for constructor params)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        if (method.Name.Lexeme == "constructor")
            il.Emit(OpCodes.Ldnull);
        else
            il.Emit(OpCodes.Ldstr, method.Name.Lexeme);
        il.Emit(OpCodes.Stelem_Ref);
        // [2] = parameterIndex as double (TypeScript numbers are doubles)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldc_R8, (double)paramIndex);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    // ── Stage 3 decorator call emitters ──────────────────────────

    /// <summary>Emits: decorator(classType, { kind: "class", name, static: false, private: false })</summary>
    private void EmitStage3ClassDecoratorCall(Decorator decorator, TypeBuilder classBuilder, string className, ILEmitter emitter, ILGenerator il)
    {
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        // Build context dictionary
        var contextLocal = il.DeclareLocal(_types.Object);
        EmitStage3Context(il, "class", className, isStatic: false);
        il.Emit(OpCodes.Stloc, contextLocal);

        // args = new object[] { classType, context }
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldtoken, classBuilder);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Emits: decorator(null, { kind: "method", name, static, private: false })</summary>
    private void EmitStage3MethodDecoratorCall(Decorator decorator, Stmt.Function method, ILEmitter emitter, ILGenerator il)
    {
        EmitStage3MethodDecoratorCall(decorator, method.Name.Lexeme, method.IsStatic, emitter, il);
    }

    private void EmitStage3MethodDecoratorCall(Decorator decorator, string memberName, bool isStatic, ILEmitter emitter, ILGenerator il)
    {
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        var contextLocal = il.DeclareLocal(_types.Object);
        EmitStage3Context(il, "method", memberName, isStatic);
        il.Emit(OpCodes.Stloc, contextLocal);

        // args = new object[] { null, context }
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Emits: decorator(undefined, { kind: "field", name, static, private: false })</summary>
    private void EmitStage3FieldDecoratorCall(Decorator decorator, Stmt.Field field, ILEmitter emitter, ILGenerator il)
    {
        emitter.EmitExpression(decorator.Expression);
        emitter.Helpers.EnsureBoxed();
        var funcLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, funcLocal);

        var contextLocal = il.DeclareLocal(_types.Object);
        EmitStage3Context(il, "field", field.Name.Lexeme, field.IsStatic);
        il.Emit(OpCodes.Stloc, contextLocal);

        // args = new object[] { null, context }
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, contextLocal);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, funcLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, _runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits IL to construct a Stage 3 decorator context as Dictionary&lt;string, object?&gt;.
    /// Leaves the dictionary on the stack.
    /// </summary>
    private void EmitStage3Context(ILGenerator il, string kind, string name, bool isStatic)
    {
        // new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectNullableCtor);
        // .kind = "class" | "method" | "field" | ...
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "kind");
        il.Emit(OpCodes.Ldstr, kind);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableSetItem);
        // .name = memberName
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableSetItem);
        // .static = true/false
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "static");
        il.Emit(isStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableSetItem);
        // .private = false
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "private");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectNullableSetItem);
    }
}
