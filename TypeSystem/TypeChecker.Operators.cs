using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

/// <summary>
/// Operator type checking - binary, unary, logical, compound assignment.
/// </summary>
/// <remarks>
/// Contains operator handlers:
/// CheckBinary, CheckLogical, CheckNullishCoalescing, CheckTernary,
/// CheckCompoundAssign, CheckCompoundSet, CheckCompoundSetIndex,
/// CheckPrefixIncrement, CheckPostfixIncrement, CheckNonNullAssertion, CheckUnary.
/// </remarks>
public partial class TypeChecker
{
    private TypeInfo CheckBinary(Expr.Binary binary)
    {
        TypeInfo left = CheckExpr(binary.Left);
        TypeInfo right = CheckExpr(binary.Right);
        var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);

        return desc switch
        {
            OperatorDescriptor.Plus => CheckPlusOperator(left, right),
            OperatorDescriptor.Arithmetic or OperatorDescriptor.Power => CheckArithmeticBinary(left, right),
            OperatorDescriptor.Comparison => CheckComparisonBinary(left, right),
            OperatorDescriptor.Equality => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
            OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift => CheckBitwiseBinary(left, right),
            OperatorDescriptor.UnsignedRightShift => CheckUnsignedShiftBinary(left, right),
            OperatorDescriptor.In or OperatorDescriptor.InstanceOf => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
            _ => new TypeInfo.Any()
        };
    }

    private TypeInfo CheckPlusOperator(TypeInfo left, TypeInfo right)
    {
        // Any bypasses type checking - return any for any+anything
        if (left is TypeInfo.Any || right is TypeInfo.Any) return new TypeInfo.Any();
        if (IsBigInt(left) && IsBigInt(right)) return new TypeInfo.BigInt();
        if (IsNumber(left) && IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (IsString(left) || IsString(right)) return new TypeInfo.String();
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in arithmetic operations. Use explicit BigInt() or Number() conversion.");
        throw new TypeCheckException("Operator '+' cannot be applied to types '" + left + "' and '" + right + "'.");
    }

    private TypeInfo CheckArithmeticBinary(TypeInfo left, TypeInfo right)
    {
        // Any bypasses type checking - return any for any op anything
        if (left is TypeInfo.Any || right is TypeInfo.Any)
            return new TypeInfo.Any();
        // Allow number+number OR bigint+bigint, NOT mixed
        if (IsBigInt(left) && IsBigInt(right))
            return new TypeInfo.BigInt();
        if (IsNumber(left) && IsNumber(right))
            return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in arithmetic operations. Use explicit BigInt() or Number() conversion.");
        throw new TypeCheckException("Operands must be numbers or bigints of the same type.");
    }

    private TypeInfo CheckComparisonBinary(TypeInfo left, TypeInfo right)
    {
        // Any bypasses type checking
        if (left is TypeInfo.Any || right is TypeInfo.Any)
            return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        // Allow number vs number OR bigint vs bigint
        if ((IsBigInt(left) && IsBigInt(right)) || (IsNumber(left) && IsNumber(right)))
            return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot compare bigint and number directly. Use explicit conversion.");
        throw new TypeCheckException("Comparison operands must be numbers or bigints of the same type.");
    }

    private TypeInfo CheckBitwiseBinary(TypeInfo left, TypeInfo right)
    {
        // Any bypasses type checking - return any for any op anything
        if (left is TypeInfo.Any || right is TypeInfo.Any)
            return new TypeInfo.Any();
        // Allow both number and bigint (separately)
        if (IsBigInt(left) && IsBigInt(right))
            return new TypeInfo.BigInt();
        if (IsNumber(left) && IsNumber(right))
            return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in bitwise operations.");
        throw new TypeCheckException("Bitwise operators require numeric operands.");
    }

    private TypeInfo CheckUnsignedShiftBinary(TypeInfo left, TypeInfo right)
    {
        // Any bypasses type checking - return any
        if (left is TypeInfo.Any || right is TypeInfo.Any)
            return new TypeInfo.Any();
        // Unsigned right shift - NOT SUPPORTED for bigint in TypeScript!
        if (IsBigInt(left) || IsBigInt(right))
            throw new TypeCheckException("Unsigned right shift (>>>) is not supported for bigint.");
        if (!IsNumber(left) || !IsNumber(right))
            throw new TypeCheckException("Bitwise operators require numeric operands.");
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckLogical(Expr.Logical logical)
    {
        TypeInfo leftType = CheckExpr(logical.Left);

        // Apply expression-level narrowing for the right operand
        // For &&: right is evaluated when left is truthy, so apply "narrowed" types
        // For ||: right is evaluated when left is falsy, so apply "excluded" types
        TypeInfo rightType;
        var narrowings = AnalyzeCompoundTypeGuards(logical.Left);

        if (narrowings.Count > 0)
        {
            bool isAnd = logical.Operator.Type == TokenType.AND_AND;

            // Build environment with variable narrowings
            var narrowedEnv = new TypeEnvironment(_environment);
            foreach (var (path, narrowedType, excludedType) in narrowings)
            {
                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    narrowedEnv.Define(varPath.Name, isAnd ? narrowedType : excludedType);
                }
            }

            // Build context with property narrowings
            var narrowedContext = Narrowing.NarrowingContext.Empty;
            foreach (var (path, narrowedType, excludedType) in narrowings)
            {
                if (path is not Narrowing.NarrowingPath.Variable)
                {
                    narrowedContext = narrowedContext.WithNarrowing(path, isAnd ? narrowedType : excludedType);
                }
            }

            // Check right side with narrowings applied
            using (new EnvironmentScope(this, narrowedEnv))
            {
                if (!narrowedContext.IsEmpty)
                {
                    PushNarrowingContext(narrowedContext);
                }
                try
                {
                    rightType = CheckExpr(logical.Right);
                }
                finally
                {
                    if (!narrowedContext.IsEmpty)
                    {
                        PopNarrowingContext();
                    }
                }
            }
        }
        else
        {
            rightType = CheckExpr(logical.Right);
        }

        // In JavaScript/TypeScript, || and && return one of their operands, not a boolean.
        // - `a || b` returns `a` if truthy, otherwise `b`. Type is A | B.
        // - `a && b` returns `a` if falsy, otherwise `b`. Type is A | B.

        // If one type is `any`, return `any`
        if (leftType is TypeInfo.Any || rightType is TypeInfo.Any)
        {
            return new TypeInfo.Any();
        }

        // If one is assignable to the other, return the broader type
        if (IsCompatible(leftType, rightType))
        {
            return rightType;
        }
        if (IsCompatible(rightType, leftType))
        {
            return leftType;
        }

        // Otherwise, return the union of both types
        return new TypeInfo.Union([leftType, rightType]);
    }

    private TypeInfo CheckNullishCoalescing(Expr.NullishCoalescing nc)
    {
        TypeInfo leftType = CheckExpr(nc.Left);
        TypeInfo rightType = CheckExpr(nc.Right);

        // Remove null and undefined from left type since ?? handles both cases
        TypeInfo nonNullishLeft = leftType;
        if (leftType is TypeInfo.Union u && (u.ContainsNull || u.ContainsUndefined))
        {
            var nonNullishTypes = u.FlattenedTypes.Where(t => t is not TypeInfo.Null and not TypeInfo.Undefined).ToList();
            nonNullishLeft = nonNullishTypes.Count == 0 ? rightType :
                nonNullishTypes.Count == 1 ? nonNullishTypes[0] :
                new TypeInfo.Union(nonNullishTypes);
        }
        else if (leftType is TypeInfo.Null or TypeInfo.Undefined)
        {
            return rightType;  // null/undefined ?? right = right
        }

        // If left (non-nullish) and right are compatible, return non-nullish left
        if (IsCompatible(nonNullishLeft, rightType) || IsCompatible(rightType, nonNullishLeft))
        {
            return nonNullishLeft;
        }

        // Otherwise return union of non-nullish left and right
        return new TypeInfo.Union([nonNullishLeft, rightType]);
    }

    private TypeInfo CheckTernary(Expr.Ternary ternary)
    {
        CheckExpr(ternary.Condition);

        // Apply expression-level narrowing for the branches
        var narrowings = AnalyzeCompoundTypeGuards(ternary.Condition);

        TypeInfo thenType;
        TypeInfo elseType;

        if (narrowings.Count > 0)
        {
            // Build environment with variable narrowings for then branch (condition is true)
            var thenEnv = new TypeEnvironment(_environment);
            foreach (var (path, narrowedType, _) in narrowings)
            {
                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    thenEnv.Define(varPath.Name, narrowedType);
                }
            }

            // Build context with property narrowings for then branch
            var thenContext = Narrowing.NarrowingContext.Empty;
            foreach (var (path, narrowedType, _) in narrowings)
            {
                if (path is not Narrowing.NarrowingPath.Variable)
                {
                    thenContext = thenContext.WithNarrowing(path, narrowedType);
                }
            }

            // Check then branch with narrowings applied
            using (new EnvironmentScope(this, thenEnv))
            {
                if (!thenContext.IsEmpty)
                {
                    PushNarrowingContext(thenContext);
                }
                try
                {
                    thenType = CheckExpr(ternary.ThenBranch);
                }
                finally
                {
                    if (!thenContext.IsEmpty)
                    {
                        PopNarrowingContext();
                    }
                }
            }

            // Build environment with variable narrowings for else branch (condition is false)
            var elseEnv = new TypeEnvironment(_environment);
            foreach (var (path, _, excludedType) in narrowings)
            {
                if (path is Narrowing.NarrowingPath.Variable varPath)
                {
                    elseEnv.Define(varPath.Name, excludedType);
                }
            }

            // Build context with property narrowings for else branch
            var elseContext = Narrowing.NarrowingContext.Empty;
            foreach (var (path, _, excludedType) in narrowings)
            {
                if (path is not Narrowing.NarrowingPath.Variable)
                {
                    elseContext = elseContext.WithNarrowing(path, excludedType);
                }
            }

            // Check else branch with excluded types applied
            using (new EnvironmentScope(this, elseEnv))
            {
                if (!elseContext.IsEmpty)
                {
                    PushNarrowingContext(elseContext);
                }
                try
                {
                    elseType = CheckExpr(ternary.ElseBranch);
                }
                finally
                {
                    if (!elseContext.IsEmpty)
                    {
                        PopNarrowingContext();
                    }
                }
            }
        }
        else
        {
            thenType = CheckExpr(ternary.ThenBranch);
            elseType = CheckExpr(ternary.ElseBranch);
        }

        // Return the more specific type, or thenType if both are compatible
        if (IsCompatible(thenType, elseType) || IsCompatible(elseType, thenType))
        {
            return thenType;
        }

        // Return union of both branch types
        return new TypeInfo.Union([thenType, elseType]);
    }

    private TypeInfo CheckCompoundAssign(Expr.CompoundAssign compound)
    {
        TypeInfo varType = LookupVariable(compound.Name);
        TypeInfo valueType = CheckExpr(compound.Value);

        // Invalidate any narrowings affected by this assignment
        var assignedPath = new Narrowing.NarrowingPath.Variable(compound.Name.Lexeme);
        InvalidateNarrowingsFor(assignedPath);

        // For += with strings, allow string concatenation
        if (compound.Operator.Type == TokenType.PLUS_EQUAL)
        {
            if (IsString(varType)) return varType;
            if (!IsNumber(varType) || !IsNumber(valueType))
                throw new TypeCheckException("Compound assignment requires numeric operands.");
            return varType;
        }

        // All other compound operators require numbers
        if (!IsNumber(varType) || !IsNumber(valueType))
        {
            throw new TypeCheckException("Compound assignment requires numeric operands.");
        }

        return varType;
    }

    private TypeInfo CheckCompoundSet(Expr.CompoundSet compound)
    {
        CheckExpr(compound.Object);
        CheckExpr(compound.Value);

        // Invalidate any narrowings affected by this property assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(compound.Object);
        if (basePath != null)
        {
            var assignedPath = new Narrowing.NarrowingPath.PropertyAccess(basePath, compound.Name.Lexeme);
            InvalidateNarrowingsFor(assignedPath);
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckCompoundSetIndex(Expr.CompoundSetIndex compound)
    {
        TypeInfo objType = CheckExpr(compound.Object);
        TypeInfo indexType = CheckExpr(compound.Index);
        TypeInfo valueType = CheckExpr(compound.Value);

        // Invalidate any narrowings affected by this index assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(compound.Object);
        if (basePath != null)
        {
            Narrowing.NarrowingPath? assignedPath = null;

            // For numeric literal index, create ElementAccess path
            if (compound.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
            {
                assignedPath = new Narrowing.NarrowingPath.ElementAccess(basePath, (int)d);
            }
            else
            {
                // For computed index, conservatively invalidate the entire object's narrowings
                assignedPath = basePath;
            }

            InvalidateNarrowingsFor(assignedPath);
        }

        if (!IsNumber(indexType))
            throw new TypeCheckException("Array index must be a number.");

        if (objType is TypeInfo.Array arrayType)
        {
            if (!IsNumber(arrayType.ElementType) || !IsNumber(valueType))
                throw new TypeCheckException("Compound assignment requires numeric operands.");
            return arrayType.ElementType;
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckLogicalAssign(Expr.LogicalAssign logical)
    {
        TypeInfo varType = LookupVariable(logical.Name);
        TypeInfo valueType = CheckExpr(logical.Value);

        // Invalidate any narrowings affected by this assignment
        var assignedPath = new Narrowing.NarrowingPath.Variable(logical.Name.Lexeme);
        InvalidateNarrowingsFor(assignedPath);

        // Logical assignment can return either the current value or the new value
        // For simplicity, return union of both types or the variable type
        return varType;
    }

    private TypeInfo CheckLogicalSet(Expr.LogicalSet logical)
    {
        CheckExpr(logical.Object);
        CheckExpr(logical.Value);

        // Invalidate any narrowings affected by this property assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(logical.Object);
        if (basePath != null)
        {
            var assignedPath = new Narrowing.NarrowingPath.PropertyAccess(basePath, logical.Name.Lexeme);
            InvalidateNarrowingsFor(assignedPath);
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckLogicalSetIndex(Expr.LogicalSetIndex logical)
    {
        TypeInfo objType = CheckExpr(logical.Object);
        CheckExpr(logical.Index);
        CheckExpr(logical.Value);

        // Invalidate any narrowings affected by this index assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(logical.Object);
        if (basePath != null)
        {
            Narrowing.NarrowingPath? assignedPath = null;

            // For numeric literal index, create ElementAccess path
            if (logical.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
            {
                assignedPath = new Narrowing.NarrowingPath.ElementAccess(basePath, (int)d);
            }
            else
            {
                // For computed index, conservatively invalidate the entire object's narrowings
                assignedPath = basePath;
            }

            InvalidateNarrowingsFor(assignedPath);
        }

        if (objType is TypeInfo.Array arrayType)
        {
            return arrayType.ElementType;
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckPrefixIncrement(Expr.PrefixIncrement prefix)
    {
        TypeInfo operandType = CheckExpr(prefix.Operand);
        if (!IsNumber(operandType))
        {
            throw new TypeCheckException("Increment/decrement operand must be a number.");
        }
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckPostfixIncrement(Expr.PostfixIncrement postfix)
    {
        TypeInfo operandType = CheckExpr(postfix.Operand);
        if (!IsNumber(operandType))
        {
            throw new TypeCheckException("Increment/decrement operand must be a number.");
        }
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckNonNullAssertion(Expr.NonNullAssertion nna)
    {
        TypeInfo exprType = CheckExpr(nna.Expression);

        // Remove null and undefined from the type
        if (exprType is TypeInfo.Union u && (u.ContainsNull || u.ContainsUndefined))
        {
            var nonNullishTypes = u.FlattenedTypes.Where(t => t is not TypeInfo.Null and not TypeInfo.Undefined).ToList();
            return nonNullishTypes.Count == 0 ? new TypeInfo.Never() :
                nonNullishTypes.Count == 1 ? nonNullishTypes[0] :
                new TypeInfo.Union(nonNullishTypes);
        }

        // If the type is just null or undefined, return never (asserting nullish is not nullish is a type error)
        if (exprType is TypeInfo.Null or TypeInfo.Undefined)
        {
            return new TypeInfo.Never();
        }

        // Otherwise, return the type unchanged (it's already non-nullable)
        return exprType;
    }

    private TypeInfo CheckDelete(Expr.Delete delete)
    {
        // Type check the operand (for any side effects or errors)
        CheckExpr(delete.Operand);
        // delete always returns boolean
        return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
    }

    private TypeInfo CheckUnary(Expr.Unary unary)
    {
        TypeInfo right = CheckExpr(unary.Right);
        if (unary.Operator.Type == TokenType.TYPEOF)
            return new TypeInfo.String();
        if (unary.Operator.Type == TokenType.VOID)
            return new TypeInfo.Undefined();
        if (unary.Operator.Type == TokenType.MINUS)
        {
            if (IsBigInt(right)) return new TypeInfo.BigInt();
            if (IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
            throw new TypeCheckException("Unary '-' expects a number or bigint.");
        }
        if (unary.Operator.Type == TokenType.BANG)
             return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (unary.Operator.Type == TokenType.TILDE)
        {
            if (IsBigInt(right)) return new TypeInfo.BigInt();
            if (IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
            throw new TypeCheckException("Bitwise NOT requires a numeric operand.");
        }

        return right;
    }
}
