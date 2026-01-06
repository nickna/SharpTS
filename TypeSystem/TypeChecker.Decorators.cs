using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    /// <summary>
    /// Type-checks decorators applied to a class, method, field, accessor, or parameter.
    /// </summary>
    private void CheckDecorators(List<Decorator>? decorators, DecoratorTarget target, TypeInfo? contextType = null)
    {
        if (decorators == null || decorators.Count == 0)
            return;

        if (_decoratorMode == DecoratorMode.None)
        {
            throw new Exception($"Type Error at line {decorators[0].AtToken.Line}: " +
                               "Decorators require --experimentalDecorators or --decorators flag.");
        }

        foreach (var decorator in decorators)
        {
            CheckDecorator(decorator, target, contextType);
        }
    }

    /// <summary>
    /// Type-checks a single decorator application.
    /// </summary>
    private void CheckDecorator(Decorator decorator, DecoratorTarget target, TypeInfo? contextType)
    {
        // Evaluate the decorator expression type
        TypeInfo decoratorType = CheckExpr(decorator.Expression);

        // For factory decorators (calls), the result type is what we need to validate
        // For direct decorators, the decorator itself should be callable
        if (decorator.Expression is not Expr.Call)
        {
            // Direct decorator: must be callable
            if (decoratorType is not (TypeInfo.Function or TypeInfo.Any))
            {
                throw new Exception($"Type Error at line {decorator.AtToken.Line}: " +
                                   $"Decorator must be a function, got '{decoratorType}'.");
            }
        }

        // Mode-specific validation
        if (_decoratorMode == DecoratorMode.Legacy)
        {
            ValidateLegacyDecorator(decorator, target, decoratorType);
        }
        else if (_decoratorMode == DecoratorMode.Stage3)
        {
            ValidateStage3Decorator(decorator, target, decoratorType);
        }
    }

    /// <summary>
    /// Validates decorator according to Legacy (Stage 2) specification.
    /// Legacy decorators receive different arguments based on target:
    /// - Class: (constructor) => constructor | void
    /// - Method: (target, propertyKey, descriptor) => descriptor | void
    /// - Accessor: (target, propertyKey, descriptor) => descriptor | void
    /// - Property: (target, propertyKey) => void
    /// - Parameter: (target, propertyKey, parameterIndex) => void
    /// </summary>
    private void ValidateLegacyDecorator(Decorator decorator, DecoratorTarget target, TypeInfo decoratorType)
    {
        // For 'any' type, skip detailed validation (TypeScript behavior)
        if (decoratorType is TypeInfo.Any)
            return;

        if (decoratorType is TypeInfo.Function funcType)
        {
            int expectedArity = target switch
            {
                DecoratorTarget.Class => 1,
                DecoratorTarget.Method or DecoratorTarget.StaticMethod => 3,
                DecoratorTarget.Getter or DecoratorTarget.Setter => 3,
                DecoratorTarget.Field or DecoratorTarget.StaticField => 2,
                DecoratorTarget.Parameter => 3,
                _ => 0
            };

            if (funcType.MinArity > expectedArity)
            {
                throw new Exception($"Type Error at line {decorator.AtToken.Line}: " +
                                   $"Decorator expects {funcType.MinArity} arguments but {target} decorators receive {expectedArity}.");
            }
        }
    }

    /// <summary>
    /// Validates decorator according to TC39 Stage 3 specification.
    /// Stage 3 decorators receive (value, context) where context contains metadata.
    /// </summary>
    private void ValidateStage3Decorator(Decorator decorator, DecoratorTarget target, TypeInfo decoratorType)
    {
        // For 'any' type, skip detailed validation
        if (decoratorType is TypeInfo.Any)
            return;

        if (decoratorType is TypeInfo.Function funcType)
        {
            // Stage 3 decorators always receive exactly 2 arguments: (value, context)
            if (funcType.MinArity > 2)
            {
                throw new Exception($"Type Error at line {decorator.AtToken.Line}: " +
                                   $"Stage 3 decorator expects at most 2 arguments but requires {funcType.MinArity}.");
            }
        }

        // Note: Parameter decorators are not part of TC39 Stage 3 spec
        if (target == DecoratorTarget.Parameter)
        {
            throw new Exception($"Type Error at line {decorator.AtToken.Line}: " +
                               "Parameter decorators are not supported in Stage 3 decorators. Use --experimentalDecorators for legacy parameter decorators.");
        }
    }
}
