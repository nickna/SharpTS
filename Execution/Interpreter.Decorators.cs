using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// Decorator execution for the tree-walking interpreter.
/// Supports both Legacy (Stage 2) and TC39 Stage 3 decorator specifications.
/// </summary>
public partial class Interpreter
{
    private DecoratorMode _decoratorMode = DecoratorMode.None;

    /// <summary>
    /// Sets the decorator mode for interpretation.
    /// </summary>
    public void SetDecoratorMode(DecoratorMode mode) => _decoratorMode = mode;

    /// <summary>
    /// Applies all decorators to a class in the correct order:
    /// 1. Parameter decorators (per method, right-to-left)
    /// 2. Method decorators (bottom-to-top, right-to-left per member)
    /// 3. Accessor decorators (bottom-to-top, right-to-left per member)
    /// 4. Field decorators (bottom-to-top)
    /// 5. Class decorators (right-to-left)
    /// </summary>
    internal SharpTSClass ApplyAllDecorators(
        Stmt.Class classStmt,
        SharpTSClass klass,
        Dictionary<string, ISharpTSCallable> methods,
        Dictionary<string, ISharpTSCallable> staticMethods,
        Dictionary<string, SharpTSFunction> getters,
        Dictionary<string, SharpTSFunction> setters)
    {
        if (_decoratorMode == DecoratorMode.None)
            return klass;

        // 1. Apply parameter decorators (Legacy only)
        if (_decoratorMode == DecoratorMode.Legacy)
        {
            ApplyParameterDecorators(classStmt, klass);
        }

        // 2. Apply method decorators (bottom-to-top order)
        foreach (var method in classStmt.Methods.Where(m => m.Body != null && m.Decorators?.Count > 0))
        {
            var targetDict = method.IsStatic ? staticMethods : methods;
            if (targetDict.TryGetValue(method.Name.Lexeme, out var func))
            {
                var decorated = ApplyMethodDecorators(method, func, klass);
                targetDict[method.Name.Lexeme] = decorated;
            }
        }

        // 3. Apply accessor decorators
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors.Where(a => a.Decorators?.Count > 0))
            {
                var targetDict = accessor.Kind.Type == TokenType.GET ? getters : setters;
                if (targetDict.TryGetValue(accessor.Name.Lexeme, out var func))
                {
                    var decorated = ApplyAccessorDecorators(accessor, func, klass);
                    targetDict[accessor.Name.Lexeme] = decorated;
                }
            }
        }

        // 4. Apply field decorators
        foreach (var field in classStmt.Fields.Where(f => f.Decorators?.Count > 0))
        {
            ApplyFieldDecorators(field, klass);
        }

        // 5. Apply class decorators (right-to-left)
        if (classStmt.Decorators?.Count > 0)
        {
            klass = ApplyClassDecorators(classStmt.Decorators, klass);
        }

        return klass;
    }

    /// <summary>
    /// Applies class decorators in right-to-left order.
    /// Legacy: decorator(constructor) => constructor | void
    /// Stage 3: decorator(value, context) => value | void
    /// </summary>
    private SharpTSClass ApplyClassDecorators(List<Decorator> decorators, SharpTSClass klass)
    {
        // Apply in right-to-left order
        for (int i = decorators.Count - 1; i >= 0; i--)
        {
            var decorator = decorators[i];
            var decoratorFn = EvaluateDecoratorExpression(decorator);

            object? result;
            if (_decoratorMode == DecoratorMode.Legacy)
            {
                // Legacy: decorator(constructor)
                result = decoratorFn.Call(this, [klass]);
            }
            else
            {
                // Stage 3: decorator(value, context)
                var context = SharpTSDecoratorContext.ForClass(klass.Name);
                result = decoratorFn.Call(this, [klass, context.ToRuntimeObject()]);
            }

            // If decorator returns a class, use it as replacement
            if (result is SharpTSClass replacement)
            {
                klass = replacement;
            }
        }

        return klass;
    }

    /// <summary>
    /// Applies method decorators in right-to-left order.
    /// Legacy: decorator(target, propertyKey, descriptor) => descriptor | void
    /// Stage 3: decorator(value, context) => value | void
    /// </summary>
    private ISharpTSCallable ApplyMethodDecorators(Stmt.Function method, ISharpTSCallable func, SharpTSClass klass)
    {
        if (method.Decorators == null || method.Decorators.Count == 0)
            return func;

        // Apply in right-to-left order
        for (int i = method.Decorators.Count - 1; i >= 0; i--)
        {
            var decorator = method.Decorators[i];
            var decoratorFn = EvaluateDecoratorExpression(decorator);

            object? result;
            if (_decoratorMode == DecoratorMode.Legacy)
            {
                // Legacy: decorator(target, propertyKey, descriptor)
                var target = method.IsStatic ? (object)klass : klass; // prototype would be instance, but we use class
                var descriptor = SharpTSPropertyDescriptor.ForMethod(func);
                result = decoratorFn.Call(this, [target, method.Name.Lexeme, descriptor.ToObject()]);

                if (result is SharpTSObject resultObj)
                {
                    var newDescriptor = SharpTSPropertyDescriptor.FromObject(resultObj);
                    if (newDescriptor.Value is ISharpTSCallable newFunc)
                    {
                        func = newFunc;
                    }
                }
            }
            else
            {
                // Stage 3: decorator(value, context)
                var context = SharpTSDecoratorContext.ForMethod(method.Name.Lexeme, method.IsStatic);
                result = decoratorFn.Call(this, [func, context.ToRuntimeObject()]);

                if (result is ISharpTSCallable replacement)
                {
                    func = replacement;
                }
            }
        }

        return func;
    }

    /// <summary>
    /// Applies accessor decorators (getter/setter) in right-to-left order.
    /// Note: In the current AST, accessors don't have IsStatic - they're always instance members.
    /// </summary>
    private SharpTSFunction ApplyAccessorDecorators(Stmt.Accessor accessor, SharpTSFunction func, SharpTSClass klass)
    {
        if (accessor.Decorators == null || accessor.Decorators.Count == 0)
            return func;

        bool isGetter = accessor.Kind.Type == TokenType.GET;
        const bool isStatic = false; // Accessors in current AST are always instance members

        for (int i = accessor.Decorators.Count - 1; i >= 0; i--)
        {
            var decorator = accessor.Decorators[i];
            var decoratorFn = EvaluateDecoratorExpression(decorator);

            object? result;
            if (_decoratorMode == DecoratorMode.Legacy)
            {
                // Legacy: decorator(target, propertyKey, descriptor)
                var descriptor = isGetter
                    ? SharpTSPropertyDescriptor.ForGetter(func)
                    : SharpTSPropertyDescriptor.ForSetter(func);

                result = decoratorFn.Call(this, [klass, accessor.Name.Lexeme, descriptor.ToObject()]);

                if (result is SharpTSObject resultObj)
                {
                    var newDescriptor = SharpTSPropertyDescriptor.FromObject(resultObj);
                    var newFunc = isGetter ? newDescriptor.Get : newDescriptor.Set;
                    if (newFunc is SharpTSFunction replacement)
                    {
                        func = replacement;
                    }
                }
            }
            else
            {
                // Stage 3: decorator(value, context)
                var context = isGetter
                    ? SharpTSDecoratorContext.ForGetter(accessor.Name.Lexeme, isStatic)
                    : SharpTSDecoratorContext.ForSetter(accessor.Name.Lexeme, isStatic);

                result = decoratorFn.Call(this, [func, context.ToRuntimeObject()]);

                if (result is SharpTSFunction replacement)
                {
                    func = replacement;
                }
            }
        }

        return func;
    }

    /// <summary>
    /// Applies field/property decorators.
    /// Legacy: decorator(target, propertyKey) => void
    /// Stage 3: decorator(undefined, context) => initializer | void
    /// </summary>
    private void ApplyFieldDecorators(Stmt.Field field, SharpTSClass klass)
    {
        if (field.Decorators == null || field.Decorators.Count == 0)
            return;

        for (int i = field.Decorators.Count - 1; i >= 0; i--)
        {
            var decorator = field.Decorators[i];
            var decoratorFn = EvaluateDecoratorExpression(decorator);

            if (_decoratorMode == DecoratorMode.Legacy)
            {
                // Legacy: decorator(target, propertyKey)
                var target = field.IsStatic ? (object)klass : klass;
                decoratorFn.Call(this, [target, field.Name.Lexeme]);
            }
            else
            {
                // Stage 3: decorator(undefined, context)
                var context = SharpTSDecoratorContext.ForField(field.Name.Lexeme, field.IsStatic);
                decoratorFn.Call(this, [null, context.ToRuntimeObject()]);
            }
        }
    }

    /// <summary>
    /// Applies parameter decorators (Legacy only - not part of Stage 3 spec).
    /// Legacy: decorator(target, propertyKey, parameterIndex) => void
    /// </summary>
    private void ApplyParameterDecorators(Stmt.Class classStmt, SharpTSClass klass)
    {
        foreach (var method in classStmt.Methods.Where(m => m.Body != null))
        {
            for (int paramIndex = 0; paramIndex < method.Parameters.Count; paramIndex++)
            {
                var param = method.Parameters[paramIndex];
                if (param.Decorators == null || param.Decorators.Count == 0)
                    continue;

                for (int i = param.Decorators.Count - 1; i >= 0; i--)
                {
                    var decorator = param.Decorators[i];
                    var decoratorFn = EvaluateDecoratorExpression(decorator);

                    // Legacy: decorator(target, methodName, parameterIndex)
                    var target = method.IsStatic ? (object)klass : klass;
                    string? propertyKey = method.Name.Lexeme == "constructor" ? null : method.Name.Lexeme;
                    decoratorFn.Call(this, [target, propertyKey, (double)paramIndex]);
                }
            }
        }
    }

    /// <summary>
    /// Evaluates a decorator expression and returns the callable decorator function.
    /// Handles both direct decorators (@decorator) and factory decorators (@decorator(args)).
    /// </summary>
    private ISharpTSCallable EvaluateDecoratorExpression(Decorator decorator)
    {
        var result = Evaluate(decorator.Expression);

        // For factory decorators (@log("message")), the expression is a Call
        // which already returned the actual decorator function
        if (result is ISharpTSCallable callable)
        {
            return callable;
        }

        throw new Exception($"Runtime Error at line {decorator.AtToken.Line}: Decorator must be a function.");
    }
}
