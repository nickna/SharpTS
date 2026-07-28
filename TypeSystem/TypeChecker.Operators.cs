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
        if (DotNetTypeSynthesizer.TryResolveBinaryOperator(
                left, right, binary.Operator.Type, out var clrResult))
        {
            return clrResult;
        }
        var desc = SemanticOperatorResolver.Resolve(binary.Operator.Type);
        int line = binary.Operator.Line;

        return desc switch
        {
            OperatorDescriptor.Plus => CheckPlusOperator(left, right, line),
            OperatorDescriptor.Arithmetic or OperatorDescriptor.Power => CheckArithmeticBinary(left, right, line),
            OperatorDescriptor.Comparison => CheckComparisonBinary(left, right, binary.Operator.Lexeme, line),
            OperatorDescriptor.Equality => TypeInfo.Primitive.Boolean,
            OperatorDescriptor.Bitwise or OperatorDescriptor.BitwiseShift => CheckBitwiseBinary(left, right, line),
            OperatorDescriptor.UnsignedRightShift => CheckUnsignedShiftBinary(left, right, line),
            OperatorDescriptor.In => CheckInOperator(right, line),
            // instanceof operand validation (TS2358/TS2359) is intentionally NOT done here: the
            // symbolType1 case needs `Symbol() || {}` to stay a `symbol | {}` union, but CheckLogical
            // currently collapses it to a bare `symbol`, which would make a bare-symbol LHS check
            // fire spuriously on `(Symbol() || {}) instanceof Object`. Deferred with that inference gap.
            OperatorDescriptor.InstanceOf => TypeInfo.Primitive.Boolean,
            _ => TypeInfo.Any.Shared
        };
    }

    /// <summary>
    /// The `in` operator: the right operand must be an object / `any` / type parameter. tsc rejects
    /// a symbol right operand (`"" in Symbol.toPrimitive`) with TS2322. Scoped to symbol so we don't
    /// newly reject other non-object right operands the checker currently tolerates.
    /// </summary>
    private TypeInfo CheckInOperator(TypeInfo right, int line)
    {
        if (right is TypeInfo.Symbol or TypeInfo.UniqueSymbol)
            throw new TypeCheckException($"Type '{right}' is not assignable to type 'object'.", line > 0 ? line : null, tsCode: "TS2322");
        return TypeInfo.Primitive.Boolean;
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
        // symbol poisons '+' even against `any` (unlike every other operator category here, which
        // bypasses on `any`) — checked first, before the any-bypass below. tsc picks between two
        // diagnostics depending on the OTHER operand: a bare number opposite symbol (or symbol on
        // both sides) gets the generic "cannot be applied to types 'A' and 'B'"; anything else
        // (string, any, or a union merely containing symbol like `sym || ""`) gets the
        // symbol-specific "The '+' operator cannot be applied to type 'symbol'".
        bool leftHasSymbol = ContainsSymbolType(left);
        bool rightHasSymbol = ContainsSymbolType(right);
        if (leftHasSymbol || rightHasSymbol)
        {
            // A plain pattern match, not a helper built on the Any-permissive IsTypeOfKind (which
            // would treat `any` as satisfying "is symbol" and wrongly make `s + a` look pure-symbol).
            bool bothPureSymbol = left is TypeInfo.Symbol or TypeInfo.UniqueSymbol
                && right is TypeInfo.Symbol or TypeInfo.UniqueSymbol;
            bool otherIsBareNumber = (leftHasSymbol && IsBareNumberType(right)) || (rightHasSymbol && IsBareNumberType(left));
            if (bothPureSymbol || otherIsBareNumber)
                throw new TypeCheckException($"Operator '+' cannot be applied to types '{left}' and '{right}'.", line > 0 ? line : null, tsCode: "TS2365");
            throw new TypeCheckException("The '+' operator cannot be applied to type 'symbol'.", line > 0 ? line : null, tsCode: "TS2469");
        }
        // Any/Inferred (incl. in unions) bypasses type checking - return any
        if (IsAnyPermissive(left) || IsAnyPermissive(right)) return TypeInfo.Any.Shared;
        if (IsBigInt(left) && IsBigInt(right)) return TypeInfo.BigInt.Shared;
        if (IsNumber(left) && IsNumber(right)) return TypeInfo.Primitive.Number;
        if (IsString(left) || IsString(right)) return TypeInfo.String.Shared;
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in arithmetic operations. Use explicit BigInt() or Number() conversion.", line > 0 ? line : null, tsCode: "TS2365");
        throw new TypeCheckException("Operator '+' cannot be applied to types '" + left + "' and '" + right + "'.", line > 0 ? line : null, tsCode: "TS2365");
    }

    /// <summary>Exactly a number primitive or number-literal type — deliberately NOT the
    /// Any-permissive <see cref="IsNumber"/> (which treats `any` as "is a number" for ordinary
    /// operand compatibility checks); this is used to pick between two symbol-vs-other-operand
    /// diagnostic messages where `any` must NOT be treated as "the other side was a number".</summary>
    private static bool IsBareNumberType(TypeInfo t) => t is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral;

    private TypeInfo CheckArithmeticBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return TypeInfo.Any.Shared;
        // Allow number+number OR bigint+bigint, NOT mixed
        if (IsBigInt(left) && IsBigInt(right))
            return TypeInfo.BigInt.Shared;
        if (IsNumber(left) && IsNumber(right))
            return TypeInfo.Primitive.Number;
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in arithmetic operations. Use explicit BigInt() or Number() conversion.", line > 0 ? line : null, tsCode: "TS2365");
        RecordInvalidArithmeticOperand(left, right, line);
        return TypeInfo.Any.Shared;
    }

    /// <summary>
    /// tsc reports each invalid arithmetic/bitwise operand independently — TS2362 for the left side,
    /// TS2363 for the right — so both can fire on the same expression (`s * s` where `s: symbol`
    /// gets two diagnostics, `s * 0` gets only the one for `s`). Always throws, like every other
    /// operator check in this file, so single-shot (non-recovery) callers still see an exception;
    /// when BOTH sides are invalid, the one NOT thrown is recorded directly first, so recovery
    /// mode's per-statement catch (which records whichever one propagates) still ends up with both.
    /// </summary>
    private void RecordInvalidArithmeticOperand(TypeInfo left, TypeInfo right, int line)
    {
        const string message = "An arithmetic operand must be of type 'any', 'number', 'bigint' or an enum type.";
        bool leftInvalid = !IsNumber(left) && !IsBigInt(left);
        bool rightInvalid = !IsNumber(right) && !IsBigInt(right);
        if (leftInvalid && rightInvalid)
            RecordTypeError(new TypeCheckException(message, line: line > 0 ? line : null, tsCode: "TS2363"));
        if (leftInvalid)
            throw new TypeCheckException(message, line > 0 ? line : null, tsCode: "TS2362");
        throw new TypeCheckException(message, line > 0 ? line : null, tsCode: "TS2363");
    }

    private TypeInfo CheckComparisonBinary(TypeInfo left, TypeInfo right, string op, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return TypeInfo.Primitive.Boolean;
        // Allow number vs number, bigint vs bigint, or string vs string
        // (JS AbstractRelationalComparison: both strings → lexicographic).
        if ((IsBigInt(left) && IsBigInt(right))
            || (IsNumber(left) && IsNumber(right))
            || (IsString(left) && IsString(right)))
            return TypeInfo.Primitive.Boolean;
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot compare bigint and number directly. Use explicit conversion.", line > 0 ? line : null, tsCode: "TS2365");
        // tsc has a symbol-specific message for relational comparisons (one diagnostic for the whole
        // expression, unlike the arithmetic per-side TS2362/2363 split above).
        if (ContainsSymbolType(left) || ContainsSymbolType(right))
            throw new TypeCheckException($"The '{op}' operator cannot be applied to type 'symbol'.", line > 0 ? line : null, tsCode: "TS2469");
        throw new TypeCheckException($"Comparison operands must be numbers, bigints, or strings of the same type. Got '{left}' and '{right}'.", line > 0 ? line : null, tsCode: "TS2365");
    }

    private TypeInfo CheckBitwiseBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return TypeInfo.Any.Shared;
        // Allow both number and bigint (separately)
        if (IsBigInt(left) && IsBigInt(right))
            return TypeInfo.BigInt.Shared;
        if (IsNumber(left) && IsNumber(right))
            return TypeInfo.Primitive.Number;
        if ((IsBigInt(left) && IsNumber(right)) || (IsNumber(left) && IsBigInt(right)))
            throw new TypeCheckException("Cannot mix bigint and number in bitwise operations.", line > 0 ? line : null, tsCode: "TS2365");
        // Same per-side TS2362/TS2363 split as arithmetic — tsc uses the identical codes for bitwise.
        RecordInvalidArithmeticOperand(left, right, line);
        return TypeInfo.Any.Shared;
    }

    private TypeInfo CheckUnsignedShiftBinary(TypeInfo left, TypeInfo right, int line = 0)
    {
        // Any/Inferred (incl. in unions) bypasses type checking
        if (IsAnyPermissive(left) || IsAnyPermissive(right))
            return TypeInfo.Any.Shared;
        // Unsigned right shift - NOT SUPPORTED for bigint in TypeScript!
        if (IsBigInt(left) || IsBigInt(right))
            throw new TypeCheckException("Unsigned right shift (>>>) is not supported for bigint.", line > 0 ? line : null, tsCode: "TS2791");
        if (!IsNumber(left) || !IsNumber(right))
        {
            // Same per-side TS2362/TS2363 split as the other arithmetic/bitwise operators (see
            // RecordInvalidArithmeticOperand) — bigint is excluded above (not a valid >>> operand
            // at all), so only number qualifies here. Always throws; when both sides are invalid,
            // the one not thrown is recorded directly so recovery mode still gets both.
            const string message = "An arithmetic operand must be of type 'any', 'number', 'bigint' or an enum type.";
            if (!IsNumber(left) && !IsNumber(right))
                RecordTypeError(new TypeCheckException(message, line: line > 0 ? line : null, tsCode: "TS2363"));
            if (!IsNumber(left))
                throw new TypeCheckException(message, line > 0 ? line : null, tsCode: "TS2362");
            throw new TypeCheckException(message, line > 0 ? line : null, tsCode: "TS2363");
        }
        return TypeInfo.Primitive.Number;
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
            return TypeInfo.Any.Shared;
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

        // Return whichever of the two is the wider type (the other is assignable to it) — same
        // "pick wider of two" pattern as CheckTernary/CheckLogicalAssign; this one previously
        // returned nonNullishLeft unconditionally even when rightType was the wider of the two.
        if (IsCompatible(nonNullishLeft, rightType))
        {
            return nonNullishLeft;
        }
        if (IsCompatible(rightType, nonNullishLeft))
        {
            return rightType;
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

        // Return whichever branch type is the wider of the two (the other is assignable to it),
        // or thenType if they're mutually compatible (e.g. identical types).
        if (IsCompatible(thenType, elseType))
        {
            return thenType;
        }
        if (IsCompatible(elseType, thenType))
        {
            return elseType;
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

        int line = compound.Operator.Line;

        if (DotNetTypeSynthesizer.TryResolveCompoundOperator(
                varType, valueType, compound.Operator.Type, out var clrResult))
        {
            if (!IsCompatible(varType, clrResult))
            {
                throw new TypeCheckException(
                    $"Operator result type '{clrResult}' is not assignable to '{varType}'.",
                    line: line > 0 ? line : null,
                    tsCode: "TS2322");
            }
            return clrResult;
        }

        // For += with strings, allow string concatenation
        if (compound.Operator.Type == TokenType.PLUS_EQUAL)
        {
            // Same symbol-vs-other-operand branching as binary '+' (CheckPlusOperator) — checked
            // FIRST, ahead of the string-concatenation shortcut below: symbol poisons '+=' even
            // against `any` or a string LHS (`str += sym` still errors).
            bool leftHasSymbol = ContainsSymbolType(varType);
            bool rightHasSymbol = ContainsSymbolType(valueType);
            if (leftHasSymbol || rightHasSymbol)
            {
                bool bothPureSymbol = varType is TypeInfo.Symbol or TypeInfo.UniqueSymbol
                    && valueType is TypeInfo.Symbol or TypeInfo.UniqueSymbol;
                bool otherIsBareNumber = (leftHasSymbol && IsBareNumberType(valueType)) || (rightHasSymbol && IsBareNumberType(varType));
                if (bothPureSymbol || otherIsBareNumber)
                    throw new TypeCheckException($"Operator '+=' cannot be applied to types '{varType}' and '{valueType}'.", line: line > 0 ? line : null, tsCode: "TS2365");
                throw new TypeCheckException("The '+=' operator cannot be applied to type 'symbol'.", line: line > 0 ? line : null, tsCode: "TS2469");
            }

            if (IsString(varType)) return varType;
            if (!IsNumber(varType) || !IsNumber(valueType))
                throw new TypeCheckException("Compound assignment requires numeric operands.", tsCode: "TS2365");
            return varType;
        }

        // All other compound operators require numbers — same per-side TS2362/TS2363 split as the
        // binary arithmetic/bitwise operators.
        if (!IsNumber(varType) || !IsNumber(valueType))
        {
            RecordInvalidArithmeticOperand(varType, valueType, line);
        }

        return varType;
    }

    private TypeInfo CheckCompoundSet(Expr.CompoundSet compound)
    {
        TypeInfo objType = CheckExpr(compound.Object);
        TypeInfo valueType = CheckExpr(compound.Value);

        // Invalidate any narrowings affected by this property assignment
        var basePath = Narrowing.NarrowingPathExtractor.TryExtract(compound.Object);
        if (basePath != null)
        {
            var assignedPath = new Narrowing.NarrowingPath.PropertyAccess(basePath, compound.Name.Lexeme);
            InvalidateNarrowingsFor(assignedPath);
        }

        if (DotNetTypeSynthesizer.TryGetClrType(objType, out _))
        {
            TypeInfo memberType = CheckGetOnType(objType, compound.Name);
            if (DotNetTypeSynthesizer.TryResolveCompoundOperator(
                    memberType, valueType, compound.Operator.Type, out var clrResult))
            {
                if (!IsCompatible(memberType, clrResult))
                {
                    throw new TypeCheckException(
                        $"Operator result type '{clrResult}' is not assignable to '{memberType}'.",
                        line: compound.Operator.Line > 0 ? compound.Operator.Line : null,
                        tsCode: "TS2322");
                }
                return clrResult;
            }
        }

        return TypeInfo.Any.Shared;
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

        if (DotNetTypeSynthesizer.TryGetClrType(objType, out _))
        {
            TokenType keyType = indexType switch
            {
                TypeInfo.String or TypeInfo.StringLiteral => TokenType.TYPE_STRING,
                TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral =>
                    TokenType.TYPE_NUMBER,
                _ => TokenType.EOF
            };
            TypeInfo? elementType = GetClassIndexType(objType, keyType);
            if (elementType != null &&
                DotNetTypeSynthesizer.TryResolveCompoundOperator(
                    elementType, valueType, compound.Operator.Type, out var clrResult))
            {
                if (!IsCompatible(elementType, clrResult))
                {
                    throw new TypeCheckException(
                        $"Operator result type '{clrResult}' is not assignable to '{elementType}'.",
                        line: compound.Operator.Line > 0 ? compound.Operator.Line : null,
                        tsCode: "TS2322");
                }
                return clrResult;
            }
        }

        if (!IsNumber(indexType))
            throw new TypeCheckException("Array index must be a number.", tsCode: "TS7053");

        if (objType is TypeInfo.Array arrayType)
        {
            if (!IsNumber(arrayType.ElementType) || !IsNumber(valueType))
                throw new TypeCheckException("Compound assignment requires numeric operands.", tsCode: "TS2365");
            return arrayType.ElementType;
        }

        return TypeInfo.Any.Shared;
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
        else if (narrowedVar is TypeInfo.Any || valueType is TypeInfo.Any) resultType = TypeInfo.Any.Shared;
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
                0 => TypeInfo.Never.Shared,
                1 => kept[0],
                _ => new TypeInfo.Union(kept),
            };
        }
        return keep(type) ? type : TypeInfo.Never.Shared;
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

        return TypeInfo.Any.Shared;
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

        return TypeInfo.Any.Shared;
    }

    private TypeInfo CheckPrefixIncrement(Expr.PrefixIncrement prefix)
    {
        TypeInfo operandType = CheckExpr(prefix.Operand);
        if (DotNetTypeSynthesizer.TryResolveIncrementOperator(
                operandType, prefix.Operator.Type, out var clrResult))
            return clrResult;
        if (!IsNumber(operandType) && !IsBigInt(operandType))
        {
            throw new TypeCheckException("An arithmetic operand must be of type 'any', 'number', 'bigint' or an enum type.", line: prefix.Operator.Line, tsCode: "TS2356");
        }
        return TypeInfo.Primitive.Number;
    }

    private TypeInfo CheckPostfixIncrement(Expr.PostfixIncrement postfix)
    {
        TypeInfo operandType = CheckExpr(postfix.Operand);
        if (DotNetTypeSynthesizer.TryResolveIncrementOperator(
                operandType, postfix.Operator.Type, out _))
            return operandType;
        if (!IsNumber(operandType) && !IsBigInt(operandType))
        {
            throw new TypeCheckException("An arithmetic operand must be of type 'any', 'number', 'bigint' or an enum type.", line: postfix.Operator.Line, tsCode: "TS2356");
        }
        return TypeInfo.Primitive.Number;
    }

    private TypeInfo CheckNonNullAssertion(Expr.NonNullAssertion nna)
    {
        TypeInfo exprType = CheckExpr(nna.Expression);

        // Remove null and undefined from the type
        if (exprType is TypeInfo.Union u && (u.ContainsNull || u.ContainsUndefined))
        {
            var nonNullishTypes = u.FlattenedTypes.Where(t => t is not TypeInfo.Null and not TypeInfo.Undefined).ToList();
            return nonNullishTypes.Count == 0 ? TypeInfo.Never.Shared :
                nonNullishTypes.Count == 1 ? nonNullishTypes[0] :
                new TypeInfo.Union(nonNullishTypes);
        }

        // If the type is just null or undefined, return never (asserting nullish is not nullish is a type error)
        if (exprType is TypeInfo.Null or TypeInfo.Undefined)
        {
            return TypeInfo.Never.Shared;
        }

        // Otherwise, return the type unchanged (it's already non-nullable)
        return exprType;
    }

    private TypeInfo CheckDelete(Expr.Delete delete)
    {
        // `delete x.p` where p is a read-only property is TS2704. Detect it on the receiver's
        // type before the generic operand check (e.g. `delete Symbol.iterator` — the well-known
        // symbol members of SymbolConstructor are readonly).
        if (delete.Operand is Expr.Get get)
        {
            TypeInfo recvType = CheckExpr(get.Object);
            if (recvType is TypeInfo.Interface itf && itf.IsMemberReadonly(get.Name.Lexeme))
                throw new TypeCheckException("The operand of a 'delete' operator cannot be a read-only property.", line: get.Name.Line, tsCode: "TS2704");
        }
        // Type check the operand (for any side effects or errors)
        CheckExpr(delete.Operand);
        // delete always returns boolean
        return TypeInfo.Primitive.Boolean;
    }

    private TypeInfo CheckUnary(Expr.Unary unary)
    {
        // typeof never throws on undeclared variables - it returns "undefined"
        if (unary.Operator.Type == TokenType.TYPEOF)
        {
            // Still type-check the operand if possible, but don't fail on undeclared
            // variables. Only the undeclared-name failure (TS2304) is legal to
            // swallow here — any other type error in the operand must surface.
            if (unary.Right is Expr.Variable)
            {
                try { CheckExpr(unary.Right); }
                catch (TypeCheckException ex) when (ex.Diagnostic.TsCode == "TS2304") { }
            }
            else
            {
                CheckExpr(unary.Right);
            }
            return TypeInfo.String.Shared;
        }
        TypeInfo right = CheckExpr(unary.Right);
        if (DotNetTypeSynthesizer.TryResolveUnaryOperator(
                right, unary.Operator.Type, out var clrResult))
        {
            return clrResult;
        }
        if (unary.Operator.Type == TokenType.VOID)
            return TypeInfo.Undefined.Shared;
        if (unary.Operator.Type == TokenType.MINUS)
        {
            // tsc types -x as number when x is any; IsBigInt(any) is true via
            // IsTypeOfKind, which would mis-type -x as bigint and make any
            // enclosing binary expression take the bigint emit path (#190).
            if (right is TypeInfo.Any) return TypeInfo.Primitive.Number;
            if (IsBigInt(right)) return TypeInfo.BigInt.Shared;
            if (IsNumber(right)) return TypeInfo.Primitive.Number;
            // tsc has a symbol-specific message for unary '-'/'~'/'+', distinct from the generic one.
            if (ContainsSymbolType(right))
                throw new TypeCheckException("The '-' operator cannot be applied to type 'symbol'.", tsCode: "TS2469");
            throw new TypeCheckException("Unary '-' expects a number or bigint.", tsCode: "TS2362");
        }
        if (unary.Operator.Type == TokenType.BANG)
             return TypeInfo.Primitive.Boolean;
        if (unary.Operator.Type == TokenType.TILDE)
        {
            // Same any→number rule as unary minus above.
            if (right is TypeInfo.Any) return TypeInfo.Primitive.Number;
            if (IsBigInt(right)) return TypeInfo.BigInt.Shared;
            if (IsNumber(right)) return TypeInfo.Primitive.Number;
            if (ContainsSymbolType(right))
                throw new TypeCheckException("The '~' operator cannot be applied to type 'symbol'.", tsCode: "TS2469");
            throw new TypeCheckException("Bitwise NOT requires a numeric operand.", tsCode: "TS2362");
        }
        if (unary.Operator.Type == TokenType.PLUS)
        {
            // Unary '+' had no check at all — any operand (including symbol) silently passed
            // through unchanged. Add only the symbol-specific rejection tsc requires here;
            // leave the (separately incorrect, pre-existing) passthrough for every other operand
            // type alone rather than widen this fix into "unary + always coerces to number".
            if (ContainsSymbolType(right))
                throw new TypeCheckException("The '+' operator cannot be applied to type 'symbol'.", tsCode: "TS2469");
            return right;
        }

        return right;
    }
}
