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
        int line = binary.Operator.Line;

        return desc switch
        {
            OperatorDescriptor.Plus => CheckPlusOperator(left, right, line),
            OperatorDescriptor.Arithmetic or OperatorDescriptor.Power => CheckArithmeticBinary(left, right, line),
            OperatorDescriptor.Comparison => CheckComparisonBinary(left, right, line),
            OperatorDescriptor.Equality => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
            OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift => CheckBitwiseBinary(left, right, line),
            OperatorDescriptor.UnsignedRightShift => CheckUnsignedShiftBinary(left, right, line),
            OperatorDescriptor.In or OperatorDescriptor.InstanceOf => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
            _ => new TypeInfo.Any()
        };
    }

    /// <summary>
    /// True if this type is Any, Inferred, or a Union containing an Any/Inferred arm.
    /// A union that includes 'any' is effectively permissive (JS semantics); operators
    /// should not reject the other arms in that case.
    /// </summary>
    private static bool IsAnyPermissive(TypeInfo t) =>
        t is TypeInfo.Any or TypeInfo.Inferred
        || (t is TypeInfo.Union u && u.FlattenedTypes.Any(inner => inner is TypeInfo.Any or TypeInfo.Inferred));

    private TypeInfo CheckPlusOperator(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking - return any
        if (IsAnyPermissive(left) || IsAnyPermissive(right)) return new TypeInfo.Any();
        if (IsBigInt(left) && IsBigInt(right)) return new TypeInfo.BigInt();
        if (IsNumber(left) && IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if (IsString(left) || IsString(right)) return new TypeInfo.String();
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in arithmetic operations. Use explicit BigInt() or Number() conversion.", line > 0 ? line : null, tsCode: "TS2365");
        throw new TypeCheckException("Operator '+' cannot be applied to types '" + left + "' and '" + right + "'.", line > 0 ? line : null, tsCode: "TS2365");
    }

    private TypeInfo CheckArithmeticBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return new TypeInfo.Any();
        // Allow number+number OR bigint+bigint, NOT mixed
        if (IsBigInt(left) && IsBigInt(right))
            return new TypeInfo.BigInt();
        if (IsNumber(left) && IsNumber(right))
            return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in arithmetic operations. Use explicit BigInt() or Number() conversion.", line > 0 ? line : null, tsCode: "TS2365");
        throw new TypeCheckException($"Operands must be numbers or bigints of the same type. Got '{left}' and '{right}'.", line > 0 ? line : null, tsCode: "TS2362");
    }

    private TypeInfo CheckComparisonBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        // Allow number vs number, bigint vs bigint, or string vs string
        // (JS AbstractRelationalComparison: both strings → lexicographic).
        if ((IsBigInt(left) && IsBigInt(right))
            || (IsNumber(left) && IsNumber(right))
            || (IsString(left) && IsString(right)))
            return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot compare bigint and number directly. Use explicit conversion.", line > 0 ? line : null, tsCode: "TS2365");
        throw new TypeCheckException($"Comparison operands must be numbers, bigints, or strings of the same type. Got '{left}' and '{right}'.", line > 0 ? line : null, tsCode: "TS2365");
    }

    private TypeInfo CheckBitwiseBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return new TypeInfo.Any();
        // Allow both number and bigint (separately)
        if (IsBigInt(left) && IsBigInt(right))
            return new TypeInfo.BigInt();
        if (IsNumber(left) && IsNumber(right))
            return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in bitwise operations.", line > 0 ? line : null, tsCode: "TS2365");
        throw new TypeCheckException($"Bitwise operators require numeric operands. Got '{left}' and '{right}'.", line > 0 ? line : null, tsCode: "TS2365");
    }

    private TypeInfo CheckUnsignedShiftBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return new TypeInfo.Any();
        // Unsigned right shift - NOT SUPPORTED for bigint in TypeScript!
        if (IsBigInt(left) || IsBigInt(right))
            throw new TypeCheckException("Unsigned right shift (>>>) is not supported for bigint.", line > 0 ? line : null, tsCode: "TS2791");
        if (!IsNumber(left) || !IsNumber(right))
            throw new TypeCheckException("Bitwise operators require numeric operands.", line > 0 ? line : null, tsCode: "TS2365");
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckLogical(Expr.Logical logical)
    {
        TypeInfo leftType = CheckExpr(logical.Left);

        // Apply expression-level narrowing for the right operand
        // For &&: right is evaluated when left is truthy, so apply "narrowed" types
        // For ||: right is evaluated when left is falsy, so apply "excluded" types.
        //   The || case decomposes a disjunction LHS (De Morgan: !(A || B) =
        //   !A && !B), so `a == null || b == null || a.length` narrows both
        //   a and b for the last operand (#216). An && LHS contributes nothing
        //   to || — its negation is itself a disjunction.
        TypeInfo rightType;
        var narrowings = logical.Operator.Type == TokenType.OR_OR
            ? CollectDisjunctGuards(logical.Left)
            : AnalyzeCompoundTypeGuards(logical.Left);

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

    private TypeInfo CheckTernary(Expr.Ternary ternary, TypeInfo? contextualType = null)
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
                    thenType = CheckExprWithContext(ternary.ThenBranch, contextualType);
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
                    elseType = CheckExprWithContext(ternary.ElseBranch, contextualType);
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
            thenType = CheckExprWithContext(ternary.ThenBranch, contextualType);
            elseType = CheckExprWithContext(ternary.ElseBranch, contextualType);
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
                throw new TypeCheckException("Compound assignment requires numeric operands.", tsCode: "TS2365");
            return varType;
        }

        // All other compound operators require numbers
        if (!IsNumber(varType) || !IsNumber(valueType))
        {
            throw new TypeCheckException("Compound assignment requires numeric operands.", tsCode: "TS2365");
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
            throw new TypeCheckException("Array index must be a number.", tsCode: "TS7053");

        if (objType is TypeInfo.Array arrayType)
        {
            if (!IsNumber(arrayType.ElementType) || !IsNumber(valueType))
                throw new TypeCheckException("Compound assignment requires numeric operands.", tsCode: "TS2365");
            return arrayType.ElementType;
        }

        return new TypeInfo.Any();
    }

    private TypeInfo CheckLogicalAssign(Expr.LogicalAssign logical)
    {
        TypeInfo varType = LookupVariable(logical.Name);

        // `&&=` only evaluates (and assigns) its RHS when the LHS is truthy — narrow the LHS to
        // its truthy constituents while checking the RHS, mirroring plain `&&`'s CheckLogical.
        // Without this, `thing &&= thing.original` spuriously flags `thing` as possibly
        // undefined/null on the very read that `&&=`'s short-circuit already guarantees is safe.
        TypeInfo valueType;
        if (logical.Operator.Type == TokenType.AND_AND_EQUAL)
        {
            var narrowedEnv = new TypeEnvironment(_environment);
            narrowedEnv.Define(logical.Name.Lexeme, NarrowLogicalTruthy(varType));
            using (new EnvironmentScope(this, narrowedEnv))
            {
                valueType = CheckExpr(logical.Value);
            }
        }
        else
        {
            valueType = CheckExpr(logical.Value);
        }

        // Invalidate any narrowings affected by this assignment
        var assignedPath = new Narrowing.NarrowingPath.Variable(logical.Name.Lexeme);
        InvalidateNarrowingsFor(assignedPath);

        // The value of `a OP= b` is the value of the binary `a OP b`:
        //   a ||= b  ->  Truthy(a) | b       (a used when falsy -> replaced by b)
        //   a ??= b  ->  NonNullish(a) | b    (a used when null/undefined -> b)
        //   a &&= b  ->  Falsy(a) | b         (a kept when falsy, else b)
        // Narrowing the left operand here is what makes `(results ||= []).push()`
        // type-check (undefined dropped) while keeping `(results &&= []).push()`
        // flagged (undefined kept). Without it every form kept the full declared
        // type and `||=`/`??=` produced spurious possibly-undefined errors.
        TypeInfo narrowedVar = logical.Operator.Type switch
        {
            TokenType.OR_OR_EQUAL => NarrowLogicalTruthy(varType),
            TokenType.QUESTION_QUESTION_EQUAL => ExpandNonNullable(varType),
            TokenType.AND_AND_EQUAL => NarrowLogicalFalsy(varType),
            _ => varType,
        };

        // Picks the WIDER of the two candidate types when one subsumes the other (so a value that
        // could only ever be the narrower candidate doesn't lose the other candidate's possibilities);
        // IsCompatible(expected, actual) is true when actual fits inside expected, so the branch that
        // fires names the wider (subsuming) type directly — not its own first argument's counterpart.
        TypeInfo resultType;
        if (narrowedVar is TypeInfo.Never) resultType = valueType;
        else if (narrowedVar is TypeInfo.Any || valueType is TypeInfo.Any) resultType = new TypeInfo.Any();
        else if (IsCompatible(narrowedVar, valueType)) resultType = narrowedVar;
        else if (IsCompatible(valueType, narrowedVar)) resultType = valueType;
        else resultType = new TypeInfo.Union([narrowedVar, valueType]);

        // Post-assignment narrowing: the variable's new value is exactly `resultType`, so
        // subsequent reads in the same scope see it narrowed — mirrors CheckAssign's
        // environment update for a plain `x = v` (fixes `results ||= []; results.push(...)`,
        // which needs the narrowing to persist past the statement, not just within the
        // expression itself).
        if (IsDeclaredTypeTracked(logical.Name.Lexeme))
        {
            var declaredType = GetDeclaredType(logical.Name.Lexeme) ?? varType;
            if (NarrowToDeclaredSlot(declaredType, resultType) is { } narrowedSlot)
            {
                _environment.Define(logical.Name.Lexeme, narrowedSlot);
            }
        }

        return resultType;
    }

    /// <summary>Truthy narrowing of a type: drops the definitely-falsy constituents
    /// (null, undefined, and the literals <c>false</c>/<c>0</c>/<c>""</c>). Applied to
    /// the left operand of <c>||</c> / <c>||=</c>.</summary>
    private static TypeInfo NarrowLogicalTruthy(TypeInfo type)
        => FilterUnionConstituents(type, t => !IsDefinitelyFalsy(t));

    /// <summary>Falsy narrowing of a type: keeps only the possibly-falsy constituents.
    /// A type with none (e.g. a bare array or function) narrows to <c>never</c>.
    /// Applied to the left operand of <c>&amp;&amp;</c> / <c>&amp;&amp;=</c>.</summary>
    private static TypeInfo NarrowLogicalFalsy(TypeInfo type)
        => FilterUnionConstituents(type, t => !IsDefinitelyTruthy(t));

    private static TypeInfo FilterUnionConstituents(TypeInfo type, Func<TypeInfo, bool> keep)
    {
        if (type is TypeInfo.Union u)
        {
            var kept = u.FlattenedTypes.Where(keep).ToList();
            return kept.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => kept[0],
                _ => new TypeInfo.Union(kept),
            };
        }
        return keep(type) ? type : new TypeInfo.Never();
    }

    /// <summary>A constituent that is always falsy: null, undefined, or a falsy literal.</summary>
    private static bool IsDefinitelyFalsy(TypeInfo t) => t switch
    {
        TypeInfo.Null or TypeInfo.Undefined => true,
        TypeInfo.BooleanLiteral bl => !bl.Value,
        TypeInfo.NumberLiteral nl => nl.Value == 0,
        TypeInfo.StringLiteral sl => sl.Value.Length == 0,
        _ => false,
    };

    /// <summary>A constituent that is always truthy: object-like types (array, tuple,
    /// class instance, record, interface, function, class, the non-primitive
    /// <c>object</c>). Primitives and literals may be falsy and so are excluded.</summary>
    private static bool IsDefinitelyTruthy(TypeInfo t) => t is
        TypeInfo.Array or TypeInfo.Tuple or TypeInfo.Instance or TypeInfo.Record
        or TypeInfo.Interface or TypeInfo.Function or TypeInfo.Class or TypeInfo.Object;

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
            throw new TypeCheckException("Increment/decrement operand must be a number.", tsCode: "TS2356");
        }
        return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
    }

    private TypeInfo CheckPostfixIncrement(Expr.PostfixIncrement postfix)
    {
        TypeInfo operandType = CheckExpr(postfix.Operand);
        if (!IsNumber(operandType))
        {
            throw new TypeCheckException("Increment/decrement operand must be a number.", tsCode: "TS2356");
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
        // typeof never throws on undeclared variables - it returns "undefined"
        if (unary.Operator.Type == TokenType.TYPEOF)
        {
            // Still type-check the operand if possible, but don't fail on undeclared variables
            if (unary.Right is Expr.Variable)
            {
                try { CheckExpr(unary.Right); } catch (TypeCheckException) { }
            }
            else
            {
                CheckExpr(unary.Right);
            }
            return new TypeInfo.String();
        }
        TypeInfo right = CheckExpr(unary.Right);
        if (unary.Operator.Type == TokenType.VOID)
            return new TypeInfo.Undefined();
        if (unary.Operator.Type == TokenType.MINUS)
        {
            // tsc types -x as number when x is any; IsBigInt(any) is true via
            // IsTypeOfKind, which would mis-type -x as bigint and make any
            // enclosing binary expression take the bigint emit path (#190).
            if (right is TypeInfo.Any) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
            if (IsBigInt(right)) return new TypeInfo.BigInt();
            if (IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
            throw new TypeCheckException("Unary '-' expects a number or bigint.", tsCode: "TS2362");
        }
        if (unary.Operator.Type == TokenType.BANG)
             return new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN);
        if (unary.Operator.Type == TokenType.TILDE)
        {
            // Same any→number rule as unary minus above.
            if (right is TypeInfo.Any) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
            if (IsBigInt(right)) return new TypeInfo.BigInt();
            if (IsNumber(right)) return new TypeInfo.Primitive(TokenType.TYPE_NUMBER);
            throw new TypeCheckException("Bitwise NOT requires a numeric operand.", tsCode: "TS2362");
        }

        return right;
    }
}
